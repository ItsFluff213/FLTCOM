import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { parseServerMessage } from "../../display/src/protocol.gen.ts";
import { createMissions, synchronousAltitude } from "./missions.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const fleet = JSON.parse(readFileSync(join(dir, "kerbin-small.json"), "utf8"));
const valid = (message) => assert.equal(parseServerMessage(JSON.stringify({ ...message, protocolVersion: fleet.protocolVersion })).ok, true, JSON.stringify(message).slice(0, 300));
const deploy = (altitude, endOfLife = null) => ({ kind: "deployConstellation", altitude, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife });
const brief = (vesselId, change) => ({ type: "brief", requestId: 1, vesselId, groupId: null, change, codename: null, pinnedStage: null });
const fueled = { ...fleet, vessels: fleet.vessels.map((vessel) => (vessel.id === 1101 ? { ...vessel, deltaV: { ...vessel.deltaV, total: 4000 } } : vessel)) };
const body = (name) => fleet.bodies.find((candidate) => candidate.name === name);

describe("mock constellation deployment", () => {
  it("puts Kerbin's synchronous orbit at about 2,863 km", () => {
    assert.ok(Math.abs(synchronousAltitude(body("Kerbin")) - 2_863_334) < 100);
  });

  it("briefs three releases a pass apart, each satellite circularizing itself, then the carrier flying home", () => {
    const briefing = createMissions().brief(fueled, brief(1101, deploy(null, "landHome")));
    valid(briefing);
    assert.equal(briefing.error, null);
    const deployment = briefing.deployment;
    assert.equal(deployment.payloads.length, 3);
    assert.deepEqual(deployment.releases.map((release) => release.pass), [1, 2, 3]);
    assert.deepEqual(
      briefing.burns.filter((burn) => burn.label.startsWith("release ") && !burn.label.endsWith(" Launch Stage")).map((burn) => burn.label),
      deployment.releases.map((release) => `release ${release.name} at apoapsis, pass ${release.pass}`),
    );
    assert.equal(briefing.stagePlan.length, 3);
    assert.equal(briefing.burns.at(-1).label, "fly home");
    assert.equal(deployment.returnPlan.kind, "land");
    const period = deployment.releases[1].nodeUt - deployment.releases[0].nodeUt;
    const ring = 2 * Math.PI * Math.sqrt((body("Kerbin").radius + deployment.altitude) ** 3 / body("Kerbin").gravParameter);
    assert.ok(Math.abs(period / ring - 2 / 3) < 1e-9, "resonant orbit is two thirds of the ring period");
  });

  it("parks a carrier short of the way home in a disposal orbit, amber, without a fly-home step", () => {
    const briefing = createMissions().brief(fleet, brief(1102, deploy(null, "landHome")));
    valid(briefing);
    assert.equal(briefing.deployment.returnPlan.kind, "disposal");
    assert.equal(briefing.deployment.returnPlan.fallback, true);
    assert.ok(!briefing.burns.some((burn) => burn.label === "fly home"));
    assert.ok(briefing.contingencies.some((note) => note.severity === "amber" && note.text.includes("DISPOSAL ORBIT")));
  });

  it("splits the launch stage off first and burns the transfer stage up by default over Kerbin", () => {
    const briefing = createMissions().brief(fueled, brief(1101, deploy(null)));
    valid(briefing);
    const { launchStage, returnPlan } = briefing.deployment;
    assert.equal(launchStage.comesHome, true);
    assert.equal(briefing.burns[0].label, `release ${launchStage.name}`);
    assert.equal(returnPlan.kind, "burnUp");
    assert.ok(returnPlan.deltaV > 0 && briefing.requiredDeltaV > returnPlan.deltaV);
    assert.ok(briefing.burns.at(-2).label.startsWith("burn up: "));
    assert.equal(briefing.burns.at(-1).label, "check spacing");
  });

  it("parks when the operator picks PARK, without a warning", () => {
    const briefing = createMissions().brief(fueled, brief(1101, deploy(null, "park")));
    valid(briefing);
    assert.equal(briefing.deployment.returnPlan.kind, "disposal");
    assert.equal(briefing.deployment.returnPlan.fallback, false);
    assert.ok(!briefing.contingencies.some((note) => note.text.startsWith("CARRIER")));
  });

  it("refuses the Mun's synchronous orbit and names the highest valid altitude", () => {
    const briefing = createMissions().brief(fleet, brief(1107, deploy(null)));
    valid(briefing);
    assert.match(briefing.error, /OUTSIDE THE SPHERE OF INFLUENCE OF MUN, HIGHEST VALID ALTITUDE 2,108 KM/);
    assert.equal(briefing.canApprove, false);
  });
});
