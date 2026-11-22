import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { CHANGE_KINDS, parseServerMessage } from "../../display/src/protocol.gen.ts";
import { createMissions, descentAt, landingSpot } from "./missions.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const fleet = JSON.parse(readFileSync(join(dir, "kerbin-small.json"), "utf8"));
const valid = (message) => assert.equal(parseServerMessage(JSON.stringify({ ...message, protocolVersion: fleet.protocolVersion })).ok, true, JSON.stringify(message).slice(0, 300));
const land = (site, latitude = 0, longitude = 0) => ({ kind: "land", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: { site, latitude, longitude }, autoSystems: null, endOfLife: null });
const brief = (vesselId, change) => ({ type: "brief", requestId: 1, vesselId, groupId: null, change, codename: null, pinnedStage: null });

describe("mock briefing for every operation kind", () => {
  const blank = { altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
  const fields = {
    changeApoapsis: { altitude: 300_000 }, changePeriapsis: { altitude: 75_000 }, changeInclination: { inclination: 0.2 }, circularOrbit: { altitude: 150_000 },
    raiseApoapsis: { altitude: 50_000 }, spreadEvenly: { altitude: 150_000 }, transfer: { body: "Mun", altitude: 30_000 }, rendezvous: { targetVesselId: 1102 },
    dock: { targetVesselId: 1102 }, land: { landing: { site: "launchPad", latitude: 0, longitude: 0 } }, deployConstellation: { altitude: 150_000 },
  };

  for (const kind of CHANGE_KINDS) {
    it(`plans ${kind} with at least one burn`, () => {
      const briefing = createMissions().brief(fleet, brief(kind === "launch" ? 1105 : 1101, { ...blank, ...fields[kind], kind }));
      valid(briefing);
      assert.equal(briefing.error, null);
      assert.ok(briefing.burns.length > 0);
    });
  }

  it("raises a single vessel's apoapsis by the asked amount", () => {
    const station = fleet.vessels.find((vessel) => vessel.id === 1101).location.orbit;
    const briefing = createMissions().brief(fleet, brief(1101, { ...blank, kind: "raiseApoapsis", altitude: 50_000 }));
    const after = briefing.burns.at(-1).orbitAfter;
    assert.equal(briefing.burns[0].label, "RAISE APOAPSIS BURN");
    assert.ok(Math.abs(after.semiMajorAxis * (1 + after.eccentricity) - station.semiMajorAxis * (1 + station.eccentricity) - 50_000) < 1);
  });
});

describe("mock flight safety", () => {
  const circularize = { kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null };
  it("holds an operation on impact course and lets a recovery on the same vessel supersede it", () => {
    const missions = createMissions();
    const first = missions.brief(fleet, brief(1107, circularize));
    assert.equal(missions.approve({ type: "approve", operationId: first.operationId, codename: "BRIGHT WARDEN" }, fleet.ut), true);
    const second = missions.brief(fleet, brief(1107, circularize));
    assert.equal(missions.approve({ type: "approve", operationId: second.operationId, codename: "LUNAR HERON" }, fleet.ut), false, "one operation per vessel");
    missions.impact(fleet);
    let snapshot = missions.snapshot(fleet.ut);
    valid(snapshot);
    assert.equal(snapshot.operations[0].state, "hold");
    assert.deepEqual(snapshot.operations[0].alarm, { kind: "impact", body: "Mun", text: "on impact course with Mun, periapsis -56.7 km" });
    const recovery = missions.brief(fleet, brief(1107, circularize));
    assert.equal(missions.approve({ type: "approve", operationId: recovery.operationId, codename: "RAISE" }, fleet.ut), true);
    snapshot = missions.snapshot(fleet.ut);
    assert.equal(snapshot.operations[0].state, "aborted");
    assert.equal(snapshot.operations[0].alarm, null);
    assert.equal(snapshot.operations.at(-1).state, "waiting");
  });
});

describe("mock landing", () => {
  it("briefs a deorbit and a descent to the launch pad, and the approved operation predicts the touchdown there", () => {
    const missions = createMissions();
    const briefing = missions.brief(fleet, brief(1101, land("launchPad")));
    valid(briefing);
    assert.equal(briefing.error, null);
    assert.deepEqual(briefing.burns.map((burn) => burn.label), ["DEORBIT", "DESCENT"]);
    assert.ok(briefing.burns[0].prograde < 0, "the deorbit burns retrograde");
    assert.equal(missions.approve({ type: "approve", operationId: briefing.operationId, codename: briefing.codename }, fleet.ut), true);
    const snapshot = missions.snapshot(fleet.ut);
    valid(snapshot);
    const prediction = snapshot.operations[0].landingPrediction;
    assert.deepEqual({ latitude: prediction.latitude, longitude: prediction.longitude }, landingSpot({ site: "launchPad", latitude: 0, longitude: 0 }));
    assert.ok(prediction.ut > briefing.burns[1].startUt);
  });

  it("lands at custom coordinates on the Mun but refuses the launch pad there", () => {
    const missions = createMissions();
    const custom = missions.brief(fleet, brief(1107, land("custom", 0.1, 0.5)));
    valid(custom);
    assert.equal(custom.error, null);
    assert.match(custom.description, /^land at 5\.73 N 28\.65 E/);
    const pad = missions.brief(fleet, brief(1107, land("launchPad")));
    valid(pad);
    assert.match(pad.error, /HOME PLANET/);
  });
});

describe("mock auto systems", () => {
  it("briefs a stage plan and the automatic actions, and lists none beyond the old staging when switched off", () => {
    const briefing = createMissions().brief(fleet, brief(1101, land("launchPad")));
    valid(briefing);
    assert.ok(briefing.stagePlan.length > 0);
    assert.deepEqual(briefing.stagePlan.map((step) => [step.stage, step.does, step.when]), [[0, "light 1 engine", "at the first burn"]]);
    assert.ok(briefing.autoActions.some((action) => action.text.startsWith("lower landing gear")));
    const off = createMissions().brief(fleet, brief(1101, { ...land("launchPad"), autoSystems: false }));
    assert.deepEqual(off.autoActions.map((action) => action.text), ["auto systems off: no gear, fairing or panel actions; staging only as before (ascent, landing, a flamed-out node burn)"]);
  });
});

describe("mock descent", () => {
  it("reports the descent phases in order down to the ground", () => {
    const phases = [0, 0.3, 0.7, 0.95, 1].map((f) => descentAt(f).phase);
    assert.deepEqual(phases, ["correct", "coast", "brake", "final", "landed"]);
    assert.equal(descentAt(1).altitude, 0);
  });

  it("flies the descent burn with a live reading, and a release holds it on the red alert", () => {
    const missions = createMissions();
    const briefing = missions.brief(fleet, brief(1101, land("launchPad")));
    missions.approve({ type: "approve", operationId: briefing.operationId, codename: briefing.codename }, fleet.ut);
    const descent = briefing.burns[1];
    for (let ut = fleet.ut; ut < descent.startUt + descent.duration / 2; ut += 5) {
      missions.tick(ut, 5);
    }
    let operation = missions.snapshot(fleet.ut).operations[0];
    assert.equal(operation.state, "executing");
    assert.ok(operation.descent, "a descending landing carries its reading");
    missions.release();
    const snapshot = missions.snapshot(fleet.ut);
    valid(snapshot);
    operation = snapshot.operations[0];
    assert.equal(operation.state, "hold");
    assert.equal(operation.alarm.kind, "impact");
  });
});
