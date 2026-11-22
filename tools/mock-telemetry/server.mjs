import { existsSync, readFileSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { PROTOCOL_VERSION, parseClientMessage, parseServerMessage } from "../../display/src/protocol.gen.ts";
import { propagate } from "../../display/src/lib/kepler.ts";
import { bodyTexture } from "./art.mjs";
import { craftModel } from "./fixtures.mjs";
import { createGroups, orbitAfterGroupBurn } from "./groups.mjs";
import { createMissions } from "./missions.mjs";
import { acceptWebSocket } from "./websocket.mjs";

const root = resolve(fileURLToPath(import.meta.url), "../../..");
const TICK_SECONDS = 0.5;
const CLOSE_PROTOCOL_ERROR = 1002;
const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".woff2": "font/woff2",
  ".txt": "text/plain; charset=utf-8",
};

const { values: args } = parseArgs({
  options: {
    fixture: { type: "string" },
    port: { type: "string", default: "47801" },
    "protocol-version": { type: "string", default: String(PROTOCOL_VERSION) },
    "freeze-after": { type: "string" },
    web: { type: "string", default: join(root, "dist/GameData/FLTCOM/web") },
    feed: { type: "boolean", default: false },
  },
});

if (!args.fixture) {
  console.error(
    "usage: npm --prefix tools/mock-telemetry start -- --fixture <name> [--port 47801] [--protocol-version N] [--freeze-after <seconds>] [--web <dir>] [--feed]",
  );
  process.exit(2);
}

const fixturePath = join(root, "tests/fixtures/telemetry", `${args.fixture}.json`);
const seed = parseServerMessage(readFileSync(fixturePath, "utf8"));
if (!seed.ok || seed.message.type !== "fleet") {
  console.error(`${fixturePath} is not a valid fleet message: ${JSON.stringify(seed.ok ? seed.message.type : seed.error)}`);
  process.exit(1);
}

const port = Number(args.port);
const protocolVersion = Number(args["protocol-version"]);
const freezeAt = args["freeze-after"] === undefined ? Infinity : Date.now() + Number(args["freeze-after"]) * 1000;
const web = resolve(args.web);
const clients = new Set();
const FUELS = new Set(["LiquidFuel", "Oxidizer", "MonoPropellant", "XenonGas"]);
const groups = createGroups();
const missions = createMissions({
  onFlown: (burn, ut, slots, last) => {
    fleet = {
      ...fleet,
      vessels: fleet.vessels.map((vessel) => {
        if (vessel.id !== burn.vesselId) {
          return vessel;
        }
        const before = vessel.deltaV?.total ?? 0;
        const kept = before > 0 ? Math.max(0, 1 - burn.deltaV / before) : 1;
        const deltaV = vessel.deltaV && { ...vessel.deltaV, total: before * kept, stages: vessel.deltaV.stages.map((stage) => ({ ...stage, deltaV: stage.deltaV * kept })) };
        const resources = vessel.resources.map((resource) => (FUELS.has(resource.name) ? { ...resource, amount: resource.amount * kept } : resource));
        const body = vessel.location.kind === "orbit" ? fleet.bodies.find((candidate) => candidate.name === vessel.location.orbit.referenceBody) : null;
        const orbit = last && body ? orbitAfterGroupBurn(burn, ut, slots, vessel.location.orbit, body.gravParameter) : null;
        const launched = last && vessel.location.kind === "surface" && burn.orbitAfter ? { kind: "orbit", situation: "orbiting", orbit: burn.orbitAfter } : null;
        return { ...vessel, deltaV, resources, location: launched ?? (orbit ? { ...vessel.location, orbit } : vessel.location) };
      }),
    };
    fleetText = encode(fleet);
  },
});
let missionsDirty = false;
let fleet = seed.message;

const encode = (message) => JSON.stringify({ ...message, protocolVersion });
const helloText = encode({ type: "hello", server: "mock-telemetry", saveName: args.fixture, snapshotInterval: TICK_SECONDS });
const layoutsPath = join(root, "tests/fixtures/telemetry", `${args.fixture}.layouts.json`);
const layoutTexts = existsSync(layoutsPath) ? JSON.parse(readFileSync(layoutsPath, "utf8")).map(encode) : [];
let fleetText = encode(fleet);
const tick = (onFleet) =>
  setInterval(() => {
    if (Date.now() >= freezeAt) {
      return;
    }
    fleet ={ ...propagate(fleet, TICK_SECONDS), seq: fleet.seq + 1 };
    fleetText = encode(fleet);
    onFleet();
  }, TICK_SECONDS * 1000);

if (args.feed) {
  const writeLine = () => process.stdout.write(fleetText + "\n");
  writeLine();
  tick(writeLine);
} else {
  console.log(`fleet message bytes=${Buffer.byteLength(fleetText)} vessels=${fleet.vessels.length}`);
}

const CONTROLS = {
  hold: () => missions.hold(),
  miss: () => missions.miss(),
  impact: () => missions.impact(fleet),
  release: () => missions.release(),
  replan: () => missions.replan(fleet.ut),
  align: () => missions.align(),
  interrupt: () => missions.interrupt("LOST SIGNAL TO A GROUP SHIP, WARP STOPPED", fleet.ut),
};

const artCache = new Map();

/** Stands in for the art the plugin makes from the running game: body textures and craft models. */
function serveArt(path) {
  if (!artCache.has(path)) {
    const body = /^\/art\/body\/(\w+)\.png$/.exec(path);
    const craft = /^\/art\/craft\/([\w-]+)\.json$/.exec(path);
    const known = body && fleet.bodies.find((candidate) => candidate.name === body[1] && candidate.texture === path);
    const model = craft && craftModel(craft[1]);
    if (!known && !model) {
      return undefined;
    }
    artCache.set(path, known ? { type: "image/png", body: bodyTexture(known.name, known.color) } : { type: "application/json", body: JSON.stringify(model) });
  }
  return artCache.get(path);
}

const server = createServer((request, response) => {
  const url = new URL(request.url ?? "/", "http://127.0.0.1");
  const control = url.pathname.startsWith("/mock/") ? CONTROLS[url.pathname.slice(6)] : undefined;
  if (control) {
    control();
    missionsDirty = true;
    response.writeHead(200, { "content-type": "text/plain" }).end("ok");
    return;
  }
  const art = request.method === "GET" ? serveArt(url.pathname) : undefined;
  if (art) {
    response.writeHead(200, { "content-type": art.type, "cache-control": "max-age=3600" }).end(art.body);
    return;
  }
  let file;
  try {
    file = resolve(web, "." + decodeURIComponent(url.pathname === "/" ? "/index.html" : url.pathname));
  } catch {
    response.writeHead(400).end();
    return;
  }
  if (request.method !== "GET" || !file.startsWith(web + sep)) {
    response.writeHead(404).end();
    return;
  }
  let body;
  try {
    body = readFileSync(file);
  } catch {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { "content-type": MIME[extname(file)] ?? "application/octet-stream", "cache-control": "no-store" }).end(body);
});

server.on("upgrade", (request, socket) => {
  socket.on("error", () => {});
  if (new URL(request.url ?? "/", "http://127.0.0.1").pathname !== "/ws") {
    socket.end("HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n");
    return;
  }
  const client = acceptWebSocket(request, socket, (text) => {
    const parsed = parseClientMessage(text);
    const clientVersion = parsed.ok ? PROTOCOL_VERSION : parsed.error.kind === "versionMismatch" ? parsed.error.received : null;
    if (clientVersion !== null && clientVersion !== protocolVersion) {
      console.log(`client speaks protocol ${clientVersion}, closing`);
      client.sendText(encode({ type: "bye", reason: "protocolMismatch" }));
      client.close(CLOSE_PROTOCOL_ERROR);
      return;
    }
    if (!parsed.ok) {
      console.log(`client message rejected: ${JSON.stringify(parsed.error)}`);
      return;
    }
    const message = parsed.message;
    switch (message.type) {
      case "ping":
        client.sendText(encode({ type: "pong", nonce: message.nonce }));
        break;
      case "brief":
        client.sendText(encode(message.groupId === null ? missions.brief(fleet, message) : missions.briefGroup(fleet, message, groups.find(message.groupId))));
        break;
      case "groupEdit": {
        const error = groups.edit(fleet, message);
        if (error) {
          console.log(`group edit refused: ${error}`);
        }
        const roster = encode(groups.roster());
        for (const each of clients) {
          each.sendText(roster);
        }
        break;
      }
      case "approve":
        missions.approve(message, fleet.ut);
        missionsDirty = true;
        break;
      case "scrub":
        missions.scrub(message.operationId);
        missionsDirty = true;
        break;
      case "resume":
        missions.resume(message.operationId, fleet.ut);
        missionsDirty = true;
        break;
      case "answer": {
        const warpTo = missions.answer(message);
        if (warpTo !== null && warpTo > fleet.ut) {
          fleet = { ...propagate(fleet, warpTo - fleet.ut), seq: fleet.seq + 1 };
        }
        missionsDirty = true;
        break;
      }
    }
  });
  if (!client) {
    return;
  }
  clients.add(client);
  socket.on("close", () => clients.delete(client));
  client.sendText(helloText);
  client.sendText(fleetText);
  client.sendText(encode(missions.snapshot(fleet.ut)));
  client.sendText(encode(groups.roster()));
  for (const text of layoutTexts) {
    client.sendText(text);
  }
});

server.on("error", (error) => {
  console.error(error.code === "EADDRINUSE" ? `port ${port} busy` : error.message);
  process.exit(1);
});

if (!args.feed) {
  server.listen(port, "127.0.0.1", () => {
    console.log(`serving ${web}`);
    console.log(`mock telemetry listening on 127.0.0.1:${server.address().port}`);
    tick(() => {
      missionsDirty = missions.tick(fleet.ut, TICK_SECONDS, fleet.activeVesselId) || missionsDirty;
      const warpTo = missions.warpTarget(fleet.ut, fleet.activeVesselId);
      if (warpTo !== null) {
        fleet = { ...propagate(fleet, warpTo - fleet.ut), seq: fleet.seq + 1 };
        fleetText = encode(fleet);
        missionsDirty = true;
      }
      const missionsText = missionsDirty ? encode(missions.snapshot(fleet.ut)) : null;
      missionsDirty = false;
      for (const client of clients) {
        client.sendText(fleetText);
        if (missionsText) {
          client.sendText(missionsText);
        }
      }
    });
  });
}

function shutdown() {
  const bye = encode({ type: "bye", reason: "shutdown" });
  for (const client of clients) {
    client.sendText(bye);
    client.close(1001);
  }
  process.exit(0);
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
