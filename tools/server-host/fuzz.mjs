import { connect } from "node:net";
import { parseArgs } from "node:util";
import { openSocket } from "./ws-client.mjs";

const { values: args } = parseArgs({
  options: {
    port: { type: "string", default: "47800" },
    count: { type: "string", default: "1000" },
    seed: { type: "string", default: "1" },
    parallel: { type: "string", default: "16" },
  },
});
const port = Number(args.port);

let state = Number(args.seed) >>> 0 || 1;
const random = () => {
  state ^= state << 13;
  state ^= state >>> 17;
  state ^= state << 5;
  return (state >>> 0) / 4294967296;
};
const pick = (items) => items[Math.floor(random() * items.length)];
const randomBytes = (length) => Buffer.from(Array.from({ length }, () => Math.floor(random() * 256)));
const randomText = (length) => Array.from({ length }, () => String.fromCharCode(32 + Math.floor(random() * 95))).join("");

const validLines = () => [
  "GET /ws HTTP/1.1",
  `Host: 127.0.0.1:${port}`,
  "Upgrade: websocket",
  "Connection: Upgrade",
  "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
  "Sec-WebSocket-Version: 13",
  `Origin: http://127.0.0.1:${port}`,
];

const MUTATIONS = {
  dropHeader: (lines) => lines.filter((_, i) => i === 0 || random() > 0.3),
  badValue: (lines) => lines.map((line, i) => (i > 0 && random() < 0.3 ? line.split(":")[0] + ": " + randomText(Math.floor(random() * 40)) : line)),
  badOrigin: (lines) => lines.map((line) => (line.startsWith("Origin") ? `Origin: ${pick(["http://evil.example", "null", "http://127.0.0.1", `https://127.0.0.1:${port}`, "http://localhost.evil.example:" + port, ""])}` : line)),
  badHost: (lines) => lines.map((line) => (line.startsWith("Host") ? `Host: ${pick(["evil.example", `127.0.0.1:${port + 1}`, "", "localhost", "[::1]:" + port])}` : line)),
  badVersion: (lines) => lines.map((line) => (line.startsWith("Sec-WebSocket-Version") ? `Sec-WebSocket-Version: ${pick(["8", "14", "-1", "13, 8", "99999999999"])}` : line)),
  badKey: (lines) => lines.map((line) => (line.startsWith("Sec-WebSocket-Key") ? `Sec-WebSocket-Key: ${pick(["", "short", "!!!!", "A".repeat(4000), randomText(24)])}` : line)),
  badRequestLine: (lines) => [pick([`${randomText(5)} /ws HTTP/1.1`, "GET /ws", "GET  HTTP/1.1", "GET /ws HTTP/9.9", `GET /${"a".repeat(9000)} HTTP/1.1`, "GET /../../Windows/win.ini HTTP/1.1", "POST /ws HTTP/1.1"]), ...lines.slice(1)],
  extensions: (lines) => [...lines, "Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits"],
  duplicateHeaders: (lines) => [...lines, ...lines.slice(1)],
  manyHeaders: (lines) => [...lines, ...Array.from({ length: 200 }, (_, i) => `X-Fill-${i}: ${randomText(30)}`)],
  hugeHeader: (lines) => [...lines, `X-Big: ${"b".repeat(20000)}`],
  noColon: (lines) => [...lines, randomText(20)],
  bareLf: (lines) => lines,
};

function buildRequest() {
  const kind = pick([...Object.keys(MUTATIONS), "randomBytes", "tlsHello", "truncated", "slow", "validThenGarbage"]);
  if (kind === "randomBytes") return { kind, bytes: randomBytes(1 + Math.floor(random() * 2048)) };
  if (kind === "tlsHello") return { kind, bytes: Buffer.concat([Buffer.from([0x16, 0x03, 0x01, 0x02, 0x00, 0x01, 0x00, 0x01, 0xfc, 0x03, 0x03]), randomBytes(500)]) };
  const lines = kind in MUTATIONS ? MUTATIONS[kind](validLines()) : validLines();
  const separator = kind === "bareLf" ? "\n" : "\r\n";
  const text = lines.join(separator) + separator + separator;
  if (kind === "truncated") return { kind, bytes: Buffer.from(text.slice(0, Math.floor(random() * text.length))) };
  if (kind === "slow") return { kind, bytes: Buffer.from(text.slice(0, 20)), holdMs: 1500 };
  if (kind === "validThenGarbage") return { kind, bytes: Buffer.concat([Buffer.from(text), randomBytes(1 + Math.floor(random() * 4096))]) };
  return { kind, bytes: Buffer.from(text) };
}

function send({ bytes, holdMs = 0 }) {
  return new Promise((resolve) => {
    const socket = connect({ port, host: "127.0.0.1" });
    let answer = "";
    const finish = (outcome) => {
      clearTimeout(timer);
      socket.destroy();
      resolve(outcome);
    };
    const timer = setTimeout(() => finish(answer ? answer : "no answer"), 2000 + holdMs);
    socket.on("connect", () => socket.write(bytes));
    socket.on("data", (chunk) => {
      answer ||= String(chunk).split("\r\n")[0].slice(0, 40) || "binary";
      if (!holdMs) finish(answer);
    });
    socket.on("close", () => finish(answer || "closed"));
    socket.on("error", (error) => finish(answer || error.code));
  });
}

async function serverStillServes() {
  const page = await fetch(`http://127.0.0.1:${port}/`);
  const html = await page.text();
  const client = await openSocket({ port });
  client.close();
  client.socket.end();
  return page.status === 200 && html.includes("<html");
}

const outcomes = new Map();
const kinds = new Map();
let next = 0;
const count = Number(args.count);
const started = Date.now();
await Promise.all(
  Array.from({ length: Number(args.parallel) }, async () => {
    while (next < count) {
      next++;
      const request = buildRequest();
      kinds.set(request.kind, (kinds.get(request.kind) ?? 0) + 1);
      const outcome = await send(request);
      outcomes.set(outcome, (outcomes.get(outcome) ?? 0) + 1);
    }
  }),
);
console.log(`sent ${count} requests in ${((Date.now() - started) / 1000).toFixed(1)} s, seed ${args.seed}`);
console.log("kinds: " + [...kinds].sort().map(([k, n]) => `${k}=${n}`).join(" "));
for (const [outcome, n] of [...outcomes].sort((a, b) => b[1] - a[1])) {
  console.log(`  ${String(n).padStart(4)}  ${outcome}`);
}
let alive = false;
try {
  alive = await serverStillServes();
} catch (error) {
  console.log(`after fuzz: ${error.message}`);
}
console.log(alive ? "after fuzz: page 200 and WebSocket upgrade ok" : "after fuzz: server no longer serves");
process.exit(alive ? 0 : 1);
