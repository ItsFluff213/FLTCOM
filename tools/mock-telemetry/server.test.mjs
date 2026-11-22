import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { after, describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { PROTOCOL_VERSION, parseServerMessage } from "../../display/src/protocol.gen.ts";

const serverPath = join(dirname(fileURLToPath(import.meta.url)), "server.mjs");
const web = mkdtempSync(join(tmpdir(), "fltcom-mock-web-"));
writeFileSync(join(web, "index.html"), "<!doctype html>");
const running = [];
after(() => {
  for (const child of running) {
    child.kill();
  }
  rmSync(web, { recursive: true, force: true, maxRetries: 3 });
});

function startMock(...extra) {
  const child = spawn(process.execPath, [serverPath, "--fixture", "kerbin-small", "--port", "0", "--web", resolve(web), ...extra]);
  running.push(child);
  return new Promise((done, fail) => {
    let out = "";
    child.stdout.on("data", (chunk) => {
      out += chunk;
      const match = /listening on 127\.0\.0\.1:(\d+)/.exec(out);
      if (match) {
        done(Number(match[1]));
      }
    });
    child.on("exit", (code) => fail(new Error(`mock exited ${code}: ${out}`)));
  });
}

function openSocket(port) {
  const socket = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const received = [];
  socket.onmessage = (event) => received.push(JSON.parse(event.data));
  const closed = new Promise((done) => (socket.onclose = done));
  const opened = new Promise((done) => (socket.onopen = done));
  return { socket, received, closed, opened };
}

const wait = (ms) => new Promise((done) => setTimeout(done, ms));

describe("mock telemetry server", () => {
  it("answers 400 to a malformed path and keeps serving", async () => {
    const port = await startMock();
    assert.equal((await fetch(`http://127.0.0.1:${port}/%E0%A4%A`)).status, 400);
    assert.equal((await fetch(`http://127.0.0.1:${port}/`)).status, 200);
  });

  it("serves a texture for each fixture body and a model for each layout design", async () => {
    const port = await startMock();
    const texture = await fetch(`http://127.0.0.1:${port}/art/body/Kerbin.png`);
    assert.equal(texture.headers.get("content-type"), "image/png");
    assert.deepEqual([...new Uint8Array(await texture.arrayBuffer()).subarray(1, 4)], [0x50, 0x4e, 0x47]);
    const model = await (await fetch(`http://127.0.0.1:${port}/art/craft/two-stage.json`)).json();
    assert.ok(model.indices.length > 0 && model.indices.every((index) => index < model.positions.length / 3));
    assert.equal((await fetch(`http://127.0.0.1:${port}/art/body/Nowhere.png`)).status, 404);
    assert.equal((await fetch(`http://127.0.0.1:${port}/art/craft/unknown.json`)).status, 404);
  });

  it("says bye with protocolMismatch to a client of another version and closes", async () => {
    const port = await startMock();
    const { socket, received, closed, opened } = openSocket(port);
    await opened;
    socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION + 1, type: "hello", client: "test" }));
    await closed;
    assert.deepEqual(received.at(-1), { type: "bye", reason: "protocolMismatch", protocolVersion: PROTOCOL_VERSION });
  });

  it("stops sending fleets after --freeze-after but keeps answering pings", async () => {
    const port = await startMock("--freeze-after", "0");
    const { socket, received, opened } = openSocket(port);
    await opened;
    await wait(1500);
    socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "ping", nonce: 3 }));
    await wait(300);
    socket.close();
    assert.deepEqual(
      received.map((message) => message.type),
      ["hello", "fleet", "missions", "groups", "layout", "layout", "layout", "layout", "layout", "pong"],
    );
    assert.equal(received[9].nonce, 3);
  });

  it("briefs a launch only for the rocket on the pad", async () => {
    const port = await startMock();
    const { socket, received, opened } = openSocket(port);
    await opened;
    const send = (message) => socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, ...message }));
    const launch = { kind: "launch", altitude: 80000, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
    send({ type: "brief", requestId: 21, vesselId: 1105, change: launch, groupId: null, codename: null, pinnedStage: null });
    send({ type: "brief", requestId: 22, vesselId: 1101, change: launch, groupId: null, codename: null, pinnedStage: null });
    await wait(300);
    const pad = received.find((message) => message.type === "briefing" && message.requestId === 21);
    const orbiting = received.find((message) => message.type === "briefing" && message.requestId === 22);
    assert.equal(parseServerMessage(JSON.stringify(pad)).ok, true);
    assert.equal(pad.canApprove, true);
    assert.deepEqual(pad.burns.map((burn) => burn.label), ["ASCENT", "FINISH THE ORBIT (ESTIMATE)"]);
    assert.ok(pad.requiredDeltaV > 3000 && pad.requiredDeltaV < 3600, String(pad.requiredDeltaV));
    assert.equal(orbiting.error, "LAUNCH NEEDS A VESSEL ON THE LAUNCH PAD");
    socket.close();
  });

  it("briefs, approves, asks before warping, warps on yes, flies the burn and finishes", async () => {
    const port = await startMock();
    const { socket, received, opened } = openSocket(port);
    await opened;
    const send = (message) => socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, ...message }));
    const last = (type) => received.findLast((message) => message.type === type);
    send({ type: "brief", requestId: 7, vesselId: 1101, change: { kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null }, groupId: null, codename: null, pinnedStage: null });
    await wait(300);
    const briefing = last("briefing");
    assert.equal(parseServerMessage(JSON.stringify(briefing)).ok, true);
    assert.equal(briefing.requestId, 7);
    assert.equal(briefing.canApprove, true);
    assert.equal(briefing.burns.length, 1);
    assert.ok(briefing.burns[0].orbitAfter.eccentricity < 1e-9);
    send({ type: "approve", operationId: briefing.operationId, codename: "TEST OP" });
    await wait(700);
    const waiting = last("missions");
    assert.equal(parseServerMessage(JSON.stringify(waiting)).ok, true);
    assert.equal(waiting.operations[0].state, "waiting");
    assert.equal(waiting.operations[0].codename, "TEST OP");
    assert.equal(waiting.prompts[0].kind, "warp");
    send({ type: "answer", promptId: waiting.prompts[0].id, accept: true });
    await wait(4500);
    assert.equal(last("missions").operations[0].state, "done");
    assert.deepEqual(last("missions").prompts, []);
    socket.close();
  });

  it("refuses a brief it cannot afford with a red contingency and no approval", async () => {
    const port = await startMock();
    const { socket, received, opened } = openSocket(port);
    await opened;
    socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "brief", requestId: 1, vesselId: 1102, change: { kind: "circularOrbit", altitude: 40000000, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null }, groupId: null, codename: null, pinnedStage: null }));
    await wait(300);
    const briefing = received.findLast((message) => message.type === "briefing");
    assert.equal(briefing.canApprove, false);
    assert.equal(briefing.contingencies[0].severity, "red");
    socket.close();
  });
});
