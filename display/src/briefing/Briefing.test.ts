import { describe, expect, it } from "vitest";
import { PROTOCOL_VERSION, type Body, type BriefingMessage, type BurnPlan, type Missions, type Orbit, type OperationStatus } from "../protocol.gen.ts";
import { codename } from "../lib/codenames.ts";
import { ALIGN_LEAD, alarmed, approveBlocked, burnPhase, conflictsOf, countdownTick, finalOrbit, marginText, missionCues, nextBurn, offsetText, steps } from "../lib/missions.ts";

const burn = (startUt: number, vesselId = 1): BurnPlan => ({ vesselId, label: "CIRCULARIZE BURN", nodeUt: startUt + 5, startUt, duration: 10, prograde: 100, normal: 0, radial: 0, deltaV: 100, orbitAfter: null });
const operation = (patch: Partial<OperationStatus> = {}): OperationStatus => ({
  id: "op-1",
  codename: "IRON LANTERN",
  change: { kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null },
  description: "circularize",
  state: "waiting",
  burns: [burn(1000), burn(2000)],
  burnIndex: 0,
  deltaVDone: 0,
  note: null,
  landingPrediction: null,
  alarm: null,
  descent: null,
  ...patch,
});
const missions = (operations: OperationStatus[], prompts: Missions["prompts"] = []): Missions => ({ protocolVersion: PROTOCOL_VERSION, type: "missions", ut: 0, operations, prompts });
const briefing = (patch: Partial<BriefingMessage> = {}): BriefingMessage => ({
  protocolVersion: PROTOCOL_VERSION,
  type: "briefing",
  requestId: 1,
  operationId: "op-1",
  codename: "IRON LANTERN",
  vesselId: 1,
  groupId: null,
  change: { kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null },
  description: "circularize",
  burns: [burn(1000)],
  requiredDeltaV: 100,
  availableDeltaV: 500,
  margin: 4,
  stages: [],
  contingencies: [],
  canApprove: true,
  error: null,
  porkchop: null,
  stagePlan: [],
  autoActions: [],
  deployment: null,
  ...patch,
});

describe("briefing margin", () => {
  it("shows spare m/s and caps the percent", () => {
    expect(marginText(briefing({ requiredDeltaV: 2, availableDeltaV: 684, margin: 341 }))).toBe("+682 m/s (>999%)");
    expect(marginText(briefing({ requiredDeltaV: 100, availableDeltaV: 150, margin: 0.5 }))).toBe("+50 m/s (50%)");
    expect(marginText(briefing({ requiredDeltaV: 100, availableDeltaV: 80, margin: -0.2 }))).toBe("-20 m/s (-20%)");
    expect(marginText(briefing({ margin: null }))).toBe("—");
  });
});

describe("briefing approval", () => {
  it("allows a clean briefing", () => {
    expect(approveBlocked(briefing())).toBe(false);
  });
  it("blocks on a red contingency even when the server said canApprove", () => {
    expect(approveBlocked(briefing({ contingencies: [{ severity: "red", vesselId: 1, text: "NO FUEL" }] }))).toBe(true);
  });
  it("does not block on amber", () => {
    expect(approveBlocked(briefing({ contingencies: [{ severity: "amber", vesselId: 1, text: "THIN MARGIN" }] }))).toBe(false);
  });
  it("blocks when a stage cannot cover its burns or no plan exists", () => {
    expect(approveBlocked(briefing({ stages: [{ vesselId: 1, stage: 1, deltaV: 10, assigned: 100, twr: 1, burnTime: 5, insufficient: true }] }))).toBe(true);
    expect(approveBlocked(briefing({ operationId: null, error: "no" }))).toBe(true);
  });
});

describe("timeline", () => {
  it("formats T offsets both ways", () => {
    expect(offsetText(2530)).toBe("T+00:42:10");
    expect(offsetText(-65)).toBe("T-00:01:05");
  });
  it("marks done, current and pending steps", () => {
    const burns = [burn(1000), burn(2000), burn(3000)];
    const rows = steps(burns, 900, operation({ state: "executing", burnIndex: 1, burns }));
    expect(rows.map((row) => row.status)).toEqual(["done", "current", "pending"]);
    expect(rows[0]!.offset).toBe("T+00:01:40");
  });
  it("marks the step that missed its window", () => {
    expect(steps([burn(1000)], 0, operation({ state: "missed" }))[0]!.status).toBe("missed");
  });
});

describe("conflicts", () => {
  it("flags overlapping burns of different vessels, both ways, and nothing else", () => {
    const a = operation({ id: "a", burns: [burn(1000, 1)] });
    const b = operation({ id: "b", burns: [burn(1005, 2)] });
    const c = operation({ id: "c", burns: [burn(1005, 3)], state: "scrubbed" });
    expect([...conflictsOf(a, [a, b, c])]).toEqual([0]);
    expect([...conflictsOf(b, [a, b, c])]).toEqual([0]);
    expect(conflictsOf(a, [a, operation({ id: "d", burns: [burn(2000, 2)] })]).size).toBe(0);
  });
  it("shows a conflict step as conflict", () => {
    expect(steps([burn(1000)], 0, operation(), new Set([0]))[0]!.status).toBe("conflict");
  });
});

describe("next burn and cues", () => {
  it("picks the earliest live burn across operations and ignores scrubbed ones", () => {
    const m = missions([operation({ id: "a", burns: [burn(5000, 2)] }), operation({ id: "b", burns: [burn(3000, 1)] }), operation({ id: "c", state: "scrubbed", burns: [burn(10, 1)] })]);
    expect(nextBurn(m, (id) => `V${id}`)).toMatchObject({ ut: 3000, vessel: "V1" });
    expect(nextBurn(null, String)).toBeNull();
  });
  it("names the burn phase from state, time to ignition and the latest note", () => {
    expect(burnPhase(operation({ burns: [burn(1000)] }), 0)).toBe("coasting");
    expect(burnPhase(operation({ burns: [burn(1000)] }), 1000 - ALIGN_LEAD)).toBe("aligning");
    expect(burnPhase(operation({ state: "executing", burns: [burn(1000)] }), 990)).toBe("aligning");
    expect(burnPhase(operation({ state: "executing", burns: [burn(1000)] }), 1000)).toBe("burning");
    expect(burnPhase(operation({ state: "hold" }), 0)).toBe("held");
    expect(burnPhase(operation({ note: "missed the window; re-planned to Y1 D3" }), 0)).toBe("replanned");
    expect(burnPhase(operation({ note: "RE-PLANNED TO THE NEXT WINDOW" }), 1000 - ALIGN_LEAD)).toBe("aligning");
    expect(burnPhase(operation({ note: "RESUMED AFTER HOLD" }), 0)).toBe("coasting");
    expect(burnPhase(operation({ state: "done" }), 0)).toBeNull();
    expect(burnPhase(operation({ state: "scrubbed" }), 0)).toBeNull();
  });
  it("watches a burn in flight first, then the earliest coming up, and a held one only when nothing else is live", () => {
    const flying = operation({ id: "f", state: "executing", burns: [burn(-20, 3)], deltaVDone: 40 });
    const held = operation({ id: "h", state: "hold", burns: [burn(-500, 4)] });
    const soon = operation({ id: "s", burns: [burn(3000, 1)] });
    expect(nextBurn(missions([soon, held, flying]), (id) => `V${id}`)).toMatchObject({ vessel: "V3", phase: "burning", deltaVDone: 40, deltaV: 100 });
    expect(nextBurn(missions([held, soon]), (id) => `V${id}`)).toMatchObject({ vessel: "V1", phase: "coasting" });
    expect(nextBurn(missions([held]), (id) => `V${id}`)).toMatchObject({ vessel: "V4", phase: "held" });
  });
  it("cues burn start, end, and alerts on missed, hold and new prompts", () => {
    const waiting = missions([operation()]);
    expect(missionCues(waiting, missions([operation({ state: "executing" })]))).toEqual(["burnStart"]);
    expect(missionCues(missions([operation({ state: "executing" })]), missions([operation({ state: "waiting", burnIndex: 1 })]))).toEqual(["burnEnd"]);
    expect(missionCues(waiting, missions([operation({ state: "missed" })]))).toEqual(["alert"]);
    expect(missionCues(waiting, missions([operation({ state: "hold" })]))).toEqual(["alert"]);
    expect(missionCues(waiting, missions([operation()], [{ id: 1, kind: "warp", operationId: "op-1", vesselId: 1, text: "warp?", untilUt: 990 }]))).toEqual(["alert"]);
    expect(missionCues(null, waiting)).toEqual([]);
  });
  it("alerts when a red alarm appears on an operation that does not change state", () => {
    const landing = missions([operation({ state: "executing" })]);
    const hard = missions([operation({ state: "executing", alarm: { kind: "landing", body: "Mun", text: "touchdown predicted at 40 m/s, over the safe 10 m/s" } })]);
    expect(missionCues(landing, hard)).toEqual(["alert"]);
    expect(missionCues(hard, hard)).toEqual([]);
  });
  it("ticks once per second inside the last ten", () => {
    expect(countdownTick(11.2, 10.8)).toBe(false);
    expect(countdownTick(10.2, 9.8)).toBe(true);
    expect(countdownTick(9.8, 9.4)).toBe(false);
    expect(countdownTick(null, 5)).toBe(false);
    expect(countdownTick(0.6, 0)).toBe(false);
  });
});

describe("codename", () => {
  it("gives two uppercase words, deterministically", () => {
    expect(codename(0)).toMatch(/^[A-Z]+ [A-Z]+$/);
    expect(codename(37)).toBe(codename(37));
    expect(codename(1)).not.toBe(codename(2));
  });
});

const mun: Body = { name: "Mun", orbit: null, radius: 200_000, gravParameter: 6.5138398e10, sphereOfInfluence: 2_429_559, atmosphereHeight: 0, rotationPeriod: 138_984, rotationAngle: 0, highestTerrainAltitude: 7_061, color: "#888", texture: null };
const munOrbit = (pe: number, ap: number): Orbit => {
  const a = (pe + ap) / 2 + mun.radius;
  return { referenceBody: "Mun", semiMajorAxis: a, eccentricity: (ap - pe) / (2 * a), inclination: 0, longitudeOfAscendingNode: 0, argumentOfPeriapsis: 0, meanAnomaly: 0 };
};

describe("final orbit", () => {
  const bodies = new Map([["Mun", mun]]);
  it("shows where the last burn leaves the vessel", () => {
    const final = finalOrbit(briefing({ burns: [{ ...burn(1000), orbitAfter: munOrbit(30_000, 30_000) }, burn(2000)] }), bodies);
    expect(final).toMatchObject({ body: "Mun", low: false });
    expect(final!.periapsis).toBeCloseTo(30_000, -1);
    expect(final!.apoapsis).toBeCloseTo(30_000, -1);
  });
  it("is low when the periapsis is under the body's safe floor", () => {
    const final = finalOrbit(briefing({ burns: [{ ...burn(1000), orbitAfter: munOrbit(-57_000, 40_000) }] }), bodies);
    expect(final).toMatchObject({ low: true, floor: 7_061 });
    expect(final!.periapsis).toBeCloseTo(-57_000, -1);
  });
  it("is absent for a landing or a plan with no orbit", () => {
    expect(finalOrbit(briefing(), bodies)).toBeNull();
    expect(finalOrbit(briefing({ change: { ...briefing().change, kind: "land" }, burns: [{ ...burn(1000), orbitAfter: munOrbit(-1000, 30_000) }] }), bodies)).toBeNull();
  });
});

describe("alarm", () => {
  const impact = { kind: "impact" as const, body: "Mun", text: "on impact course with Mun, periapsis -56.7 km" };
  it("names the vessel of the held operation with a red alert", () => {
    const m = missions([operation({ id: "a" }), operation({ id: "b", state: "hold", burnIndex: 1, burns: [burn(1000, 3), burn(2000, 4)], alarm: impact })]);
    expect(alarmed(m, (id) => `V${id}`)).toEqual({ operationId: "b", vesselId: 4, vesselName: "V4", alarm: impact });
  });
  it("ignores operations that are over", () => {
    expect(alarmed(missions([operation({ state: "aborted", alarm: impact })]), String)).toBeNull();
    expect(alarmed(null, String)).toBeNull();
  });
});
