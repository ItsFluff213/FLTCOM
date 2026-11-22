import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { PROTOCOL_VERSION, parseServerMessage } from "../../display/src/protocol.gen.ts";
import { createMissions } from "./missions.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const port = (id, size, free = true) => ({ id, name: "Clamp-O-Tron Docking Port", size, free });
const fleetWith = (chaserPorts, chaserMono = 35) => {
  const fleet = JSON.parse(readFileSync(join(dir, "kerbin-small.json"), "utf8"));
  const chaser = fleet.vessels.find((vessel) => vessel.id === 1102);
  chaser.docking = chaserPorts ? { ports: chaserPorts } : null;
  chaser.deltaV.stages = chaser.deltaV.stages.map((stage) => ({ ...stage, deltaV: 5000 }));
  chaser.resources = chaser.resources.map((resource) => (resource.name === "MonoPropellant" ? { ...resource, amount: chaserMono } : resource));
  return fleet;
};
const dock = (targetPortId = null) => ({ kind: "dock", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: 1101, targetPortId, landing: null, autoSystems: null, endOfLife: null });
const request = (change) => ({ type: "brief", requestId: 1, vesselId: 1102, groupId: null, change, codename: null, pinnedStage: null });
const valid = (message) => assert.equal(parseServerMessage(JSON.stringify({ ...message, protocolVersion: PROTOCOL_VERSION })).ok, true, JSON.stringify(message).slice(0, 200));

describe("mock dock", () => {
  it("briefs the rendezvous, then a dock with RCS to the first free port that fits", () => {
    const briefing = createMissions().brief(fleetWith([port(1, "size1")]), request(dock()));
    valid(briefing);
    assert.equal(briefing.canApprove, true, JSON.stringify(briefing.contingencies));
    assert.deepEqual(briefing.burns.slice(-2).map((burn) => burn.label), ["KILL RELATIVE VELOCITY", "DOCK WITH RCS"]);
    assert.equal(briefing.change.targetPortId, 11011);
  });

  it("takes the operator's port and refuses a size mismatch or a chaser without ports", () => {
    const missions = createMissions();
    assert.equal(missions.brief(fleetWith([port(1, "size2")]), request(dock(11013))).change.targetPortId, 11013);
    const mismatch = missions.brief(fleetWith([port(1, "size1")]), request(dock(11013)));
    assert.equal(mismatch.canApprove, false);
    assert.match(mismatch.contingencies.at(-1).text, /SIZE MISMATCH/);
    assert.match(missions.brief(fleetWith(null), request(dock())).contingencies.at(-1).text, /HAS NO FREE DOCKING PORT/);
  });

  it("warns that a chaser without monopropellant stops for manual docking", () => {
    const briefing = createMissions().brief(fleetWith([port(1, "size1")], 0), request(dock()));
    assert.equal(briefing.canApprove, true);
    assert.match(briefing.contingencies.at(-1).text, /CONTINGENCY, NO RCS, MANUAL DOCKING/);
  });

  it("lets two docks share a station on different ports but not on the same one", () => {
    const missions = createMissions();
    const fleet = fleetWith([port(1, "size1")]);
    const first = missions.brief(fleet, request(dock(11011)));
    assert.equal(missions.approve({ operationId: first.operationId, codename: "QUIET HERON" }, fleet.ut), true);
    assert.equal(missions.brief(fleet, request(dock(11012))).canApprove, true);
    assert.match(missions.brief(fleet, request(dock(11011))).contingencies.at(-1).text, /RESERVED BY OPERATION QUIET HERON/);
  });
});
