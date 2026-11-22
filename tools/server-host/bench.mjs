import { parseArgs } from "node:util";
import { OPCODE_TEXT, openSocket } from "./ws-client.mjs";

const USAGE = `usage: node tools/server-host/bench.mjs --target <name>=<port> [--target <name>=<port> ...]
  [--rounds 5] [--pings 200] [--fleets 20] [--clients 3]
Servers must already run, each replaying the same fixture. Targets are measured interleaved.`;

const { values: args } = parseArgs({
  options: {
    target: { type: "string", multiple: true },
    rounds: { type: "string", default: "5" },
    pings: { type: "string", default: "200" },
    fleets: { type: "string", default: "20" },
    clients: { type: "string", default: "3" },
  },
});
if (!args.target?.length) {
  console.error(USAGE);
  process.exit(2);
}
const targets = args.target.map((spec) => {
  const [name, port] = spec.split("=");
  return { name, port: Number(port) };
});

const quantile = (values, q) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))];
};
const summary = (values) => ({ n: values.length, p50: quantile(values, 0.5), p95: quantile(values, 0.95), max: Math.max(...values) });
const ms = (value) => value.toFixed(2);

async function pingRoundTrips(port, count) {
  const waiting = new Map();
  const client = await openSocket({
    port,
    onFrame: ({ opcode, payload }) => {
      if (opcode !== OPCODE_TEXT) return;
      const text = payload.toString("utf8", 0, Math.min(payload.length, 120));
      const match = /"type":"pong","nonce":(\d+)/.exec(text);
      if (match) waiting.get(Number(match[1]))?.(performance.now());
    },
  });
  const trips = [];
  for (let nonce = 1; nonce <= count; nonce++) {
    const sent = performance.now();
    const answered = new Promise((resolve) => waiting.set(nonce, resolve));
    client.sendText(JSON.stringify({ protocolVersion: 1, type: "ping", nonce }));
    const timeout = new Promise((resolve) => setTimeout(() => resolve(null), 2000));
    const at = await Promise.race([answered, timeout]);
    if (at === null) throw new Error(`no pong for nonce ${nonce} within 2 s`);
    trips.push(at - sent);
  }
  client.close();
  client.socket.end();
  return trips;
}

/** Span from the first byte of a fleet message reaching any client to the last client holding all of it. */
async function fleetDelivery(port, clientCount, fleetCount) {
  const arrivals = new Map();
  let resolveDone;
  const done = new Promise((resolve) => (resolveDone = resolve));
  let firstSeq = null;
  const clients = [];
  for (let i = 0; i < clientCount; i++) {
    clients.push(
      await openSocket({
        port,
        onFrame: ({ opcode, payload, firstByteAt, completeAt }) => {
          if (opcode !== OPCODE_TEXT) return;
          const match = /"type":"fleet","seq":(\d+)/.exec(payload.toString("utf8", 0, 80));
          if (!match) return;
          const seq = Number(match[1]);
          const list = arrivals.get(seq) ?? [];
          list.push({ firstByteAt, completeAt, bytes: payload.length });
          arrivals.set(seq, list);
          if (list.length === clientCount) {
            firstSeq ??= seq;
            if (seq - firstSeq + 1 >= fleetCount) resolveDone();
          }
        },
      }),
    );
  }
  await Promise.race([done, new Promise((_, reject) => setTimeout(() => reject(new Error("fleet messages stopped arriving")), fleetCount * 1000 + 5000))]);
  for (const client of clients) {
    client.close();
    client.socket.end();
  }
  const spans = [];
  let bytes = 0;
  for (const list of arrivals.values()) {
    if (list.length !== clientCount) continue;
    spans.push(Math.max(...list.map((a) => a.completeAt)) - Math.min(...list.map((a) => a.firstByteAt)));
    bytes = list[0].bytes;
  }
  return { spans, bytes };
}

const totals = new Map(targets.map((t) => [t.name, { trips: [], spans: [] }]));
for (let round = 1; round <= Number(args.rounds); round++) {
  for (const target of targets) {
    const trips = await pingRoundTrips(target.port, Number(args.pings));
    const { spans, bytes } = await fleetDelivery(target.port, Number(args.clients), Number(args.fleets));
    const t = summary(trips);
    const s = summary(spans);
    console.log(
      `round ${round} ${target.name}: ping rtt p50 ${ms(t.p50)} p95 ${ms(t.p95)} max ${ms(t.max)} ms (n=${t.n}); ` +
        `fleet ${bytes} B to ${args.clients} clients p50 ${ms(s.p50)} p95 ${ms(s.p95)} max ${ms(s.max)} ms (n=${s.n})`,
    );
    totals.get(target.name).trips.push(...trips);
    totals.get(target.name).spans.push(...spans);
  }
}
for (const [name, { trips, spans }] of totals) {
  const t = summary(trips);
  const s = summary(spans);
  console.log(`total ${name}: ping rtt p50 ${ms(t.p50)} p95 ${ms(t.p95)} ms (n=${t.n}); fleet delivery p50 ${ms(s.p50)} p95 ${ms(s.p95)} ms (n=${s.n})`);
}
