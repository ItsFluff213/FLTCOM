import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { PROTOCOL_VERSION, parseServerMessage } from "../../display/src/protocol.gen.ts";
import { createGroups, orbitAfterGroupBurn } from "./groups.mjs";
import { createMissions, planBriefing } from "./missions.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const fleet = JSON.parse(readFileSync(join(dir, "relay-ring.json"), "utf8"));
const RELAYS = [1201, 1202, 1203, 1204];
const spread = { kind: "spreadEvenly", altitude: 900000, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
const request = (groupId, change = spread) => ({ type: "brief", requestId: 1, vesselId: RELAYS[0], groupId, change, codename: null, pinnedStage: null });
const valid = (message) => assert.equal(parseServerMessage(JSON.stringify({ ...message, protocolVersion: PROTOCOL_VERSION })).ok, true, JSON.stringify(message).slice(0, 200));

function ring() {
  const groups = createGroups();
  assert.equal(groups.edit(fleet, { action: "create", groupId: null, name: "Relay ring", vesselIds: RELAYS }), null);
  return groups;
}

describe("mock groups", () => {
  it("creates, renames, adds, removes and deletes, and sends a valid roster", () => {
    const groups = ring();
    const id = groups.roster().groups[0].id;
    assert.equal(groups.edit(fleet, { action: "rename", groupId: id, name: "Ring", vesselIds: [] }), null);
    groups.edit(fleet, { action: "remove", groupId: id, name: null, vesselIds: [1204] });
    groups.edit(fleet, { action: "add", groupId: id, name: null, vesselIds: [1204, 1204, 999] });
    assert.deepEqual(groups.roster().groups[0], { id, name: "Ring", vesselIds: [1201, 1202, 1203, 1204] });
    valid(groups.roster());
    assert.equal(groups.edit(fleet, { action: "create", groupId: null, name: " ", vesselIds: [] }), "a group needs a name");
    groups.edit(fleet, { action: "delete", groupId: id, name: null, vesselIds: [] });
    assert.deepEqual(groups.roster().groups, []);
  });

  it("briefs a spread as one operation with each ship's burns apart from the others", () => {
    const groups = ring();
    const missions = createMissions();
    const briefing = missions.briefGroup(fleet, request("g1"), groups.find("g1"));
    valid(briefing);
    assert.equal(briefing.canApprove, true);
    assert.equal(briefing.groupId, "g1");
    assert.equal(new Set(briefing.burns.map((burn) => burn.vesselId)).size, 4);
    assert.equal(briefing.burns.length, 8);
    briefing.burns.slice(1).forEach((burn, index) => assert.ok(burn.startUt >= briefing.burns[index].startUt + briefing.burns[index].duration, `burn ${index + 1} overlaps`));
  });

  it("asks once, warps through every burn and leaves the four ships equally spaced", () => {
    const groups = ring();
    let ut = fleet.ut;
    const orbits = new Map(fleet.vessels.map((vessel) => [vessel.id, vessel.location.orbit]));
    const finished = new Map();
    const missions = createMissions({
      onFlown: (burn, at, slots, last) => {
        if (last) {
          orbits.set(burn.vesselId, orbitAfterGroupBurn(burn, at, slots, orbits.get(burn.vesselId), fleet.bodies.find((body) => body.name === "Kerbin").gravParameter));
          finished.set(burn.vesselId, at);
        }
      },
    });
    const briefing = missions.briefGroup(fleet, request("g1"), groups.find("g1"));
    assert.equal(missions.approve({ operationId: briefing.operationId, codename: "EVEN RING" }, ut), true);
    const [prompt] = missions.snapshot(ut).prompts;
    assert.match(prompt.text, /^4 ships, 8 burns over /);
    missions.answer({ promptId: prompt.id, accept: true });
    let asked = 0;
    for (let step = 0; step < 100000 && missions.snapshot(ut).operations[0].state !== "done"; step++) {
      ut = missions.warpTarget(ut, fleet.activeVesselId) ?? ut + 1;
      missions.tick(ut, 1, fleet.activeVesselId);
      asked += missions.snapshot(ut).prompts.length;
    }
    assert.equal(missions.snapshot(ut).operations[0].state, "done");
    assert.equal(asked, 0);
    const kerbin = fleet.bodies.find((body) => body.name === "Kerbin");
    const along = RELAYS.map((id) => {
      const orbit = orbits.get(id);
      const motion = Math.sqrt(kerbin.gravParameter / orbit.semiMajorAxis ** 3);
      return (orbit.longitudeOfAscendingNode + orbit.argumentOfPeriapsis + orbit.meanAnomaly + motion * (ut - finished.get(id))) % (2 * Math.PI);
    }).sort((x, y) => x - y);
    const gaps = along.map((value, index) => ((((along[(index + 1) % 4] - value) * 180) / Math.PI) + 360) % 360);
    gaps.forEach((gap) => assert.ok(Math.abs(gap - 90) < 0.01, `gap ${gap}`));
    RELAYS.forEach((id) => assert.ok(Math.abs(orbits.get(id).semiMajorAxis - (kerbin.radius + 900000)) < 1));
  });

  it("asks the player before flying their own ship and moves the burn an orbit later without an answer", () => {
    const groups = createGroups();
    groups.edit(fleet, { action: "create", groupId: null, name: "Mixed", vesselIds: [fleet.activeVesselId, 1201] });
    const missions = createMissions();
    const briefing = missions.briefGroup(fleet, request("g1", { kind: "raiseApoapsis", altitude: 20000, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null }), groups.find("g1"));
    missions.approve({ operationId: briefing.operationId, codename: "MIXED" }, fleet.ut);
    const own = briefing.burns.find((burn) => burn.vesselId === fleet.activeVesselId);
    let ut = fleet.ut;
    for (const burn of briefing.burns.filter((candidate) => candidate.startUt < own.startUt)) {
      for (ut = burn.startUt; ut <= burn.startUt + burn.duration + 1; ut += 1) {
        missions.tick(ut, 1, fleet.activeVesselId);
      }
    }
    missions.tick(own.startUt - 100, 1, fleet.activeVesselId);
    const fly = missions.snapshot(ut).prompts.find((prompt) => prompt.kind === "fly");
    assert.ok(fly, "no fly prompt");
    valid(missions.snapshot(ut));
    missions.tick(own.startUt + 1, 1, fleet.activeVesselId);
    assert.equal(missions.snapshot(ut).operations[0].state, "waiting");
    missions.tick(own.startUt + 31, 1, fleet.activeVesselId);
    const operation = missions.snapshot(ut).operations[0];
    assert.match(operation.note, /^MISSED/);
    assert.ok(operation.burns.find((burn) => burn.vesselId === fleet.activeVesselId).startUt > own.startUt + 1000);
  });

  it("briefs a moon transfer as a leave burn and a capture that ends around the moon", () => {
    const change = { kind: "transfer", altitude: 50000, periapsis: null, inclination: null, lan: null, body: "Mun", targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
    const briefing = planBriefing(fleet, { ...request(null, change), vesselId: 1101 }, 1);
    valid(briefing);
    assert.equal(briefing.error, null);
    assert.deepEqual(briefing.burns.map((burn) => burn.label), ["LEAVE FOR MUN", "CAPTURE AT MUN"]);
    assert.equal(briefing.burns[1].orbitAfter.referenceBody, "Mun");
    assert.equal(briefing.porkchop, null);
    assert.match(planBriefing(fleet, { ...request(null, { ...change, body: "Ike" }), vesselId: 1101 }, 2).error, /ONLY MOONS/);
  });

  it("briefs a planet transfer as a leave burn, a course correction and a capture, with a porkchop plot", () => {
    const change = { kind: "transfer", altitude: 100000, periapsis: null, inclination: null, lan: null, body: "Duna", targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
    const briefing = planBriefing(fleet, { ...request(null, change), vesselId: 1101 }, 1);
    valid(briefing);
    assert.equal(briefing.error, null);
    assert.deepEqual(briefing.burns.map((burn) => burn.label), ["LEAVE FOR DUNA", "COURSE CORRECTION", "CAPTURE AT DUNA"]);
    assert.equal(briefing.burns[0].orbitAfter.referenceBody, "Sun");
    assert.equal(briefing.burns[2].orbitAfter.referenceBody, "Duna");
    const plot = briefing.porkchop;
    assert.equal(plot.deltaV.length, plot.columns * plot.rows);
    assert.ok(Math.min(...plot.deltaV) >= plot.bestDeltaV - 1e-6);
    assert.ok(plot.bestDeparture >= fleet.ut);
  });
});
