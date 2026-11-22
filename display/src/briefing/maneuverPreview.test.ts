import { describe, expect, it } from "vitest";
import { vesselPosition, type Vec3 } from "../lib/kepler.ts";
import type { Body, BriefingMessage, BurnPlan, ChangeKind, Orbit, Vessel } from "../protocol.gen.ts";
import { draw, maneuverPreview } from "./maneuverPreview.ts";

const body = (name: string, radius: number, gravParameter: number, orbit: Orbit | null, patch: Partial<Body> = {}): Body => ({
  name,
  orbit,
  radius,
  gravParameter,
  sphereOfInfluence: orbit ? 2.4e6 : null,
  atmosphereHeight: 0,
  rotationPeriod: 138_984,
  rotationAngle: 0,
  highestTerrainAltitude: 0,
  color: "#8d8a86",
  texture: null,
  ...patch,
});
const circle = (referenceBody: string, semiMajorAxis: number, meanAnomaly = 0, eccentricity = 0): Orbit => ({
  referenceBody,
  semiMajorAxis,
  eccentricity,
  inclination: 0,
  longitudeOfAscendingNode: 0,
  argumentOfPeriapsis: 0,
  meanAnomaly,
});
const SUN = body("Sun", 261_600_000, 1.1723328e18, null);
const KERBIN = body("Kerbin", 600_000, 3.5316e12, circle("Sun", 13_599_840_256), { atmosphereHeight: 70_000, highestTerrainAltitude: 6_767, rotationPeriod: 21_549 });
const MUN = body("Mun", 200_000, 6.5138398e10, circle("Kerbin", 12_000_000, 1.7), { highestTerrainAltitude: 7_061 });
const BODIES = new Map([SUN, KERBIN, MUN].map((b) => [b.name, b]));

const vessel = (orbit: Orbit, id = 1): Vessel => ({ id, location: { kind: "orbit", orbit } }) as unknown as Vessel;
const burn = (label: string, nodeUt: number, orbitAfter: Orbit | null, vesselId = 1): BurnPlan => ({
  vesselId,
  label,
  nodeUt,
  startUt: nodeUt - 5,
  duration: 10,
  prograde: 50,
  normal: 0,
  radial: 0,
  deltaV: 50,
  orbitAfter,
});
const briefing = (burns: BurnPlan[], kind: ChangeKind = "circularOrbit", patch: Partial<BriefingMessage["change"]> = {}): BriefingMessage =>
  ({
    burns,
    change: { kind, altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null, ...patch },
  }) as unknown as BriefingMessage;
const distance = (a: Vec3, b: Vec3 = { x: 0, y: 0, z: 0 }) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);

describe("maneuverPreview", () => {
  const start = circle("Mun", 2_416_000, 0.3, 0.0012);
  const plan = [burn("periapsis to 19 km", 1_000, circle("Mun", 1_317_000, 2, 0.83)), burn("circularize at 19 km", 9_000, circle("Mun", 219_000, 1))];

  it("puts each burn on the orbit it leads to and keeps its timeline row", () => {
    const preview = maneuverPreview(briefing(plan), vessel(start), BODIES, 0)!;

    expect(preview.center).toMatchObject({ name: "Mun", radius: 200_000, floor: 7_061 });
    expect(preview.burns.map((b) => [b.index, b.final])).toEqual([
      [0, false],
      [1, true],
    ]);
    for (const [i, b] of preview.burns.entries()) {
      expect(distance(b.at!, vesselPosition(plan[i]!.orbitAfter!))).toBeLessThan(1);
      expect(b.path!.length).toBeGreaterThan(100);
    }
    expect(Math.max(...preview.current!.map((p) => distance(p)))).toBeCloseTo(2_416_000 * 1.0012, -2);
    expect(preview.low).toBe(false);
  });

  it("marks a final orbit under the body's safe floor", () => {
    const low = [plan[0]!, burn("circularize at 3 km", 9_000, circle("Mun", 203_000, 1))];

    expect(maneuverPreview(briefing(low), vessel(start), BODIES, 0)!.low).toBe(true);
  });

  it("numbers only the briefed vessel's burns, by their row in a group timeline", () => {
    const group = [burn("other ship", 500, circle("Mun", 300_000), 2), ...plan];

    expect(maneuverPreview(briefing(group), vessel(start), BODIES, 0)!.burns.map((b) => b.index)).toEqual([1, 2]);
  });

  it("draws a moon transfer round the planet with the capture at the moon where it will be", () => {
    const lko = circle("Kerbin", 700_000);
    const capture = circle("Mun", 230_000);
    const transfer = [burn("leave for Mun", 100, circle("Kerbin", 6_350_000, 0, 1 - 700_000 / 6_350_000)), burn("capture at Mun", 30_000, capture)];
    const preview = maneuverPreview(briefing(transfer, "transfer", { body: "Mun" }), vessel(lko), BODIES, 0)!;

    expect(preview.center.name).toBe("Kerbin");
    const mun = preview.bodies.find((b) => b.name === "Mun")!;
    expect(mun).toBeDefined();
    expect(distance(preview.burns[1]!.at!, mun.position)).toBeCloseTo(230_000, -2);
    expect(distance(preview.burns[0]!.at!)).toBeCloseTo(700_000, -3);
  });

  it("follows a landing's descent down to the ground and marks the target", () => {
    const orbit = circle("Mun", 230_000);
    const descent = { ...circle("Mun", 210_000, Math.PI, 0.095), argumentOfPeriapsis: 0 };
    const land = [burn("deorbit", 100, descent), { ...burn("descent", 130, null), duration: 600 }];
    const preview = maneuverPreview(briefing(land, "land", { landing: { site: "custom", latitude: 0.03, longitude: 0.5 } }), vessel(orbit), BODIES, 0)!;

    const [deorbit, brake] = preview.burns;
    expect(deorbit!.path!.length).toBeGreaterThan(1);
    expect(distance(brake!.at!)).toBeGreaterThan(200_000);
    expect(distance(brake!.path!.at(-1)!)).toBeLessThanOrEqual(200_000);
    expect(distance(preview.touchdown!)).toBeCloseTo(200_000, -1);
    expect(preview.low).toBe(false);
  });

  it("has nothing to preview without the vessel", () => {
    expect(maneuverPreview(briefing(plan), null, BODIES, 0)).toBeNull();
  });
});

describe("draw", () => {
  it("fits every burn inside the box and splits orbits that pass behind the body", () => {
    const preview = maneuverPreview(briefing([burn("raise", 1_000, circle("Mun", 600_000, 2, 0.6))]), vessel(circle("Mun", 260_000)), BODIES, 0)!;
    const drawing = draw(preview, 560, 210);

    for (const b of drawing.burns) {
      expect(b.mark!.x).toBeGreaterThanOrEqual(0);
      expect(b.mark!.x).toBeLessThanOrEqual(560);
      expect(b.mark!.y).toBeGreaterThanOrEqual(0);
      expect(b.mark!.y).toBeLessThanOrEqual(210);
    }
    expect(drawing.current.map((s) => s.behind)).toContain(true);
    expect(drawing.current.map((s) => s.behind)).toContain(false);
    expect(drawing.center.floor).toBeGreaterThan(drawing.center.r);
  });
});

describe("maneuverPreview of a constellation deployment", () => {
  const ring = 3_463_334;
  const period = 2 * Math.PI * Math.sqrt(ring ** 3 / KERBIN.gravParameter);
  const working = { ...circle("Kerbin", ring * (2 / 3) ** (2 / 3), Math.PI, 1 - (2 * ring * (2 / 3) ** (2 / 3) - ring) / ring), argumentOfPeriapsis: Math.PI };
  const releases = [1, 2, 3].map((pass) => ({
    payloadId: 900 + pass,
    name: `Carrier Sat ${pass}`,
    pass,
    ut: 1_000 + (pass * 2 * period) / 3 - 90,
    nodeUt: 1_000 + (pass * 2 * period) / 3,
    apsis: "apoapsis",
    spacingError: 0,
    finalOrbit: circle("Kerbin", ring),
  }));
  const plan = {
    ...briefing([burn("shape the resonant orbit", 1_000, working), ...releases.map((release) => ({ ...burn(`release ${release.name} at apoapsis, pass ${release.pass}`, release.ut, null), deltaV: 0 }))], "deployConstellation"),
    deployment: { releases },
  } as unknown as BriefingMessage;

  it("draws the ring and one slot per satellite, evenly spaced around it", () => {
    const preview = maneuverPreview(plan, vessel(circle("Kerbin", 700_000)), BODIES, 0)!;
    expect(Math.max(...preview.ring!.map((p) => distance(p)))).toBeCloseTo(ring, -2);
    expect(preview.slots).toHaveLength(3);
    const angles = preview.slots.map((slot) => Math.atan2(slot.y, slot.x)).sort((a, b) => a - b);
    expect(angles[1]! - angles[0]!).toBeCloseTo((2 * Math.PI) / 3, 3);
    expect(angles[2]! - angles[1]!).toBeCloseTo((2 * Math.PI) / 3, 3);
    expect(draw(preview, 560, 210).slots).toHaveLength(3);
  });

  it("puts each release on the resonant orbit, at its apoapsis", () => {
    const preview = maneuverPreview(plan, vessel(circle("Kerbin", 700_000)), BODIES, 0)!;
    for (const release of preview.burns.slice(1)) {
      expect(distance(release.at!)).toBeGreaterThan(ring - 50_000);
    }
  });
});
