import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { PROTOCOL_VERSION, parseServerMessage } from "../../display/src/protocol.gen.ts";
import { createMissions } from "./missions.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const fleet = JSON.parse(readFileSync(join(dir, "relay-ring.json"), "utf8"));
const meet = (targetVesselId, targetPortId = null) => ({ kind: "rendezvous", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId, targetPortId, landing: null, autoSystems: null, endOfLife: null });
const request = (vesselId, change) => ({ type: "brief", requestId: 1, vesselId, groupId: null, change, codename: null, pinnedStage: null });
const valid = (message) => assert.equal(parseServerMessage(JSON.stringify({ ...message, protocolVersion: PROTOCOL_VERSION })).ok, true, JSON.stringify(message).slice(0, 200));

describe("mock rendezvous", () => {
  it("briefs a rendezvous that ends holding beside the target", () => {
    const briefing = createMissions().brief(fleet, request(1201, meet(1202)));
    valid(briefing);
    assert.equal(briefing.canApprove, true, JSON.stringify(briefing.contingencies));
    assert.equal(briefing.burns.at(-1).label, "KILL RELATIVE VELOCITY");
  });

  it("refuses a vessel meeting itself and a missing target", () => {
    const missions = createMissions();
    assert.equal(missions.brief(fleet, request(1201, meet(1201))).error, "A VESSEL CANNOT MEET ITSELF");
    assert.equal(missions.brief(fleet, request(1201, meet(null))).error, "PICK A TARGET IN THE FLEET");
  });

  it("refuses a second rendezvous to a reserved port and names the holder", () => {
    const missions = createMissions();
    const first = missions.brief(fleet, request(1201, meet(1202, 7)));
    assert.equal(missions.approve({ operationId: first.operationId, codename: "QUIET HERON" }, fleet.ut), true);

    const second = missions.brief(fleet, request(1203, meet(1202, 7)));
    assert.equal(second.canApprove, false);
    assert.match(second.contingencies.at(-1).text, /RESERVED BY OPERATION QUIET HERON/);
    assert.equal(missions.brief(fleet, request(1203, meet(1202, 8))).canApprove, true);
  });
});
