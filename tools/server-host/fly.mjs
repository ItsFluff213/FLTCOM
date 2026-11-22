// Briefs, approves and follows one orbit change through the plugin's server, as the display would.
// Usage: node tools/server-host/fly.mjs <port> <change> [altitude-m] [--no-warp]
// change: circularize | apoapsis | periapsis | circular. Flies the active vessel; answers warp prompts yes unless --no-warp.
import { openSocket } from "./ws-client.mjs";

const [portText, change = "circularize", altitudeText] = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const warp = !process.argv.includes("--no-warp");
const kinds = { circularize: "circularize", apoapsis: "changeApoapsis", periapsis: "changePeriapsis", circular: "circularOrbit" };
const V = 3;
const deadline = Date.now() + 30 * 60 * 1000;

let client;
let operationId = null;
let briefed = false;
let lastLine = "";
const answered = new Set();

const done = new Promise((resolve) => {
  const onMessage = (m) => {
    if (m.type === "fleet" && !briefed && m.activeVesselId != null) {
      briefed = true;
      const altitude = altitudeText ? Number(altitudeText) : null;
      send({ type: "brief", requestId: 1, vesselId: m.activeVesselId, change: { kind: kinds[change], altitude, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null }, codename: null, pinnedStage: null });
    } else if (m.type === "briefing") {
      console.log(`briefing ${m.codename}: ${m.description}; dv ${m.requiredDeltaV.toFixed(1)}/${m.availableDeltaV.toFixed(1)} m/s; burns ${m.burns.map((b) => `${b.label} start=${b.startUt.toFixed(0)} dur=${b.duration.toFixed(1)}s dv=${b.deltaV.toFixed(1)}${b.orbitAfter ? ` after: a=${b.orbitAfter.semiMajorAxis.toFixed(0)} e=${b.orbitAfter.eccentricity.toFixed(4)}` : ""}`).join(", ")}; canApprove=${m.canApprove}${m.error ? " error=" + m.error : ""}`);
      for (const c of m.contingencies) console.log(`  ${c.severity}: ${c.text}`);
      if (!m.canApprove) return resolve(1);
      operationId = m.operationId;
      send({ type: "approve", operationId, codename: m.codename });
    } else if (m.type === "missions") {
      const op = m.operations.find((o) => o.id === operationId);
      if (op) {
        const key = `${op.state} burn=${op.burnIndex}/${op.burns.length} note=${op.note ?? ""}`;
        if (key !== lastLine) {
          console.log(`ut=${m.ut.toFixed(0)} dvDone=${op.deltaVDone.toFixed(1)} ${key}`);
          lastLine = key;
        }
        if (["done", "aborted", "missed", "scrubbed"].includes(op.state.toLowerCase())) return resolve(op.state.toLowerCase() === "done" ? 0 : 2);
      }
      for (const p of m.prompts) {
        if (p.operationId === operationId && !answered.has(p.id)) {
          answered.add(p.id);
          const accept = p.kind === "switch" || warp;
          console.log(`prompt ${p.kind}: ${p.text} -> ${accept ? "yes" : "no"}`);
          send({ type: "answer", promptId: p.id, accept });
        }
      }
    }
  };
  openSocket({
    port: Number(portText),
    onFrame: ({ opcode, payload }) => opcode === 1 && onMessage(JSON.parse(payload.toString("utf8"))),
  }).then((c) => {
    client = c;
    send({ type: "hello", client: "fly.mjs" });
  });
  const timer = setInterval(() => Date.now() > deadline && resolve(3), 1000);
  timer.unref();
});

function send(message) {
  client.sendText(JSON.stringify({ protocolVersion: V, ...message }));
}

const code = await done;
client?.close();
process.exit(code);
