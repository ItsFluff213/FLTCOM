import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parseServerMessage, type Fleet, type OrbitalLocation, type Vessel } from "../protocol.gen.ts";
import { buildScene, lookOf, rockScale, ROLE_COLOR, surfacePoint } from "./Scene.ts";

function load(name: string): Fleet {
  const parsed = parseServerMessage(readFileSync(new URL(`../../../tests/fixtures/telemetry/${name}.json`, import.meta.url), "utf8"));
  if (!parsed.ok || parsed.message.type !== "fleet") {
    throw new Error(`bad fixture ${name}`);
  }
  return parsed.message;
}

const length = (v: { x: number; y: number; z: number }) => Math.hypot(v.x, v.y, v.z);

describe("buildScene", () => {
  const kerbin = load("kerbin-small");
  const orbital = kerbin.vessels.find((vessel) => vessel.location.kind === "orbit" && vessel.location.orbit.referenceBody === "Kerbin" && vessel.location.orbit.eccentricity < 1)!;

  it("shows Kerbin, its moons and every vessel around them", () => {
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: 1101 });
    expect(scene.bodies.map((body) => body.name)).toEqual(expect.arrayContaining(["Kerbin", "Mun", "Minmus"]));
    expect(scene.entities.filter((entity) => entity.kind === "vessel")).toHaveLength(kerbin.vessels.length);
    expect(scene.center.name).toBe("Kerbin");
    expect(scene.extent).toBe(scene.center.sphereOfInfluence);
  });

  it("places everything at true scale: a low orbit sits just above the surface", () => {
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: orbital.id });
    const orbit = scene.orbits.find((line) => line.id === `vessel:${orbital.id}`)!;
    const radius = scene.center.radius;
    expect(orbit.points.every((point) => length(point) > radius)).toBe(true);
    const mun = scene.bodies.find((body) => body.name === "Mun")!;
    expect(length(mun.position)).toBeGreaterThan(10_000_000);
  });

  it("puts a vessel around a moon next to that moon", () => {
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null });
    const mun = scene.bodies.find((body) => body.name === "Mun")!;
    const munVessel = kerbin.vessels.find((vessel) => vessel.location.kind === "orbit" && vessel.location.orbit.referenceBody === "Mun");
    const entity = scene.entities.find((candidate) => candidate.vesselId === munVessel?.id && candidate.kind === "vessel");
    expect(entity).toBeDefined();
    const apart = { x: entity!.position.x - mun.position.x, y: entity!.position.y - mun.position.y, z: entity!.position.z - mun.position.z };
    expect(length(apart)).toBeLessThan(mun.radius * 20);
  });

  it("marks the selected ship and the picked group green and ships in a live operation blue", () => {
    const [first, second, third] = kerbin.vessels.filter((vessel) => vessel.comms !== "los");
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, members: new Set([first!.id, second!.id]), operating: new Set([third!.id]) });
    const marked = scene.entities.filter((entity) => entity.kind === "vessel" && entity.selected);
    expect(marked.map((entity) => entity.vesselId).sort()).toEqual([first!.id, second!.id].sort());
    expect(marked.every((entity) => entity.color === ROLE_COLOR.selected)).toBe(true);
    expect(scene.entities.find((entity) => entity.vesselId === third!.id)?.role).toBe("operation");
    const plain = scene.entities.filter((entity) => entity.kind === "vessel" && entity.role === "fleet");
    expect(plain.length).toBeGreaterThan(0);
    expect(plain.every((entity) => entity.color === ROLE_COLOR.fleet)).toBe(true);
  });

  it("draws a planned burn as a yellow orbit and a labelled marker, red when missed", () => {
    const burn = { vesselId: orbital.id, label: "CIRCULARIZE BURN", nodeUt: kerbin.ut + 2530, missed: false };
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [burn] });
    expect(scene.orbits.some((line) => line.role === "planned")).toBe(true);
    expect(scene.entities.find((entity) => entity.kind === "burn")).toMatchObject({ text: "CIRCULARIZE BURN T+00:42:10", role: "planned" });
    expect(buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [{ ...burn, missed: true }] }).entities.find((entity) => entity.kind === "burn")?.role).toBe("alert");
  });

  it("draws one marker when a held operation and a recovery briefing burn the same vessel at the same moment", () => {
    const burn = { vesselId: orbital.id, label: "CIRCULARIZE BURN", nodeUt: kerbin.ut + 2530, missed: false };
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [burn, { ...burn }] });
    expect(scene.entities.filter((entity) => entity.kind === "burn")).toHaveLength(1);
  });

  it("marks the predicted touchdown on the surface of the body the vessel orbits", () => {
    const landing = { vesselId: orbital.id, latitude: -0.0017, longitude: -1.3013, ut: kerbin.ut + 600 };
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, landings: [landing] });
    const marker = scene.entities.find((entity) => entity.id === `landing:${orbital.id}`)!;
    const body = kerbin.bodies.find((candidate) => candidate.name === "Kerbin")!;
    expect(marker).toMatchObject({ kind: "burn", role: "planned", text: "touchdown T+00:10:00" });
    expect(length(marker.position)).toBeCloseTo(body.radius, 0);
    expect(marker.position).toEqual(surfacePoint(body, landing.latitude, landing.longitude));
  });

  it("draws the planned orbit after a burn", () => {
    const burn = { vesselId: orbital.id, label: "CIRCULARIZE BURN", nodeUt: kerbin.ut + 2530, missed: false };
    const before = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [burn] }).orbits.length;
    const orbitAfter = { ...(orbital.location as OrbitalLocation).orbit, eccentricity: 0 };
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [{ ...burn, orbitAfter }] });
    expect(scene.orbits.length).toBe(before + 1);
    expect(scene.orbits.filter((line) => line.role === "planned").length).toBe(2);
  });

  it("draws the flight between planets as an arc from the leave burn to the next burn, in the star's frame", () => {
    const sun = kerbin.bodies.find((body) => body.orbit === null)!;
    const home = kerbin.bodies.find((body) => body.name === "Kerbin")!;
    const r1 = home.orbit!.semiMajorAxis;
    const r2 = 20_726_155_264;
    const flight = Math.PI * Math.sqrt(((r1 + r2) / 2) ** 3 / sun.gravParameter);
    const leg = { referenceBody: sun.name, semiMajorAxis: (r1 + r2) / 2, eccentricity: (r2 - r1) / (r1 + r2), inclination: 0, longitudeOfAscendingNode: 0, argumentOfPeriapsis: 0, meanAnomaly: 0 };
    const leave = { vesselId: orbital.id, label: "LEAVE FOR DUNA", nodeUt: kerbin.ut + 1000, missed: false, orbitAfter: leg };
    const capture = { vesselId: orbital.id, label: "CAPTURE AT DUNA", nodeUt: kerbin.ut + 1000 + flight, missed: false };
    const drawn = buildScene({ fleet: kerbin, center: sun.name, selectedId: null, burns: [leave, capture] }).orbits.find((line) => line.id.startsWith("after:"))!;
    const first = drawn.points[0]!;
    const last = drawn.points.at(-1)!;
    expect(Math.hypot(first.x, first.y, first.z) / r1).toBeCloseTo(1, 6);
    expect(Math.hypot(last.x, last.y, last.z) / r2).toBeCloseTo(1, 6);
    expect(last.x / r2).toBeCloseTo(-1, 6);
    const fromKerbin = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, burns: [leave, capture] }).orbits.find((line) => line.id.startsWith("after:"));
    expect(fromKerbin?.points.length).toBe(129);
  });

  it("colors a vessel without a link red even when selected", () => {
    const lost = load("comms-lost");
    const scene = buildScene({ fleet: lost, center: "Mun", selectedId: 1108 });
    const entity = scene.entities.find((candidate) => candidate.vesselId === 1108);
    expect(entity?.role).toBe("alert");
    expect(entity?.selected).toBe(true);
  });

  it("shows the planets and their moons around the sun, lit from the center", () => {
    const scene = buildScene({ fleet: kerbin, center: "Sun", selectedId: null });
    const names = scene.entities.filter((entity) => entity.kind === "body").map((entity) => entity.text);
    for (const expected of ["EVE", "KERBIN", "DUNA", "MUN", "IKE", "GILLY", "LAYTHE"]) {
      expect(names).toContain(expected);
    }
    expect(scene.sunDirection).toBeNull();
    const lit = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null }).sunDirection!;
    expect(length(lit)).toBeCloseTo(1, 9);
  });

  it("carries each vessel's attitude and its design's model to the map", () => {
    const scene = buildScene({ fleet: kerbin, center: "Kerbin", selectedId: null, models: new Map([[orbital.id, "/art/craft/two-stage.json"]]) });
    const entity = scene.entities.find((candidate) => candidate.vesselId === orbital.id)!;
    expect(entity.model).toBe("/art/craft/two-stage.json");
    expect(entity.attitude).toEqual(orbital.attitude);
  });

  it("keeps every vessel of a 200 vessel fleet", () => {
    const fleet = load("fleet-200");
    const scene = buildScene({ fleet, center: "Kerbin", selectedId: fleet.vessels[0]!.id });
    const around = fleet.vessels.filter((vessel) => ["Kerbin", "Mun", "Minmus"].includes(vessel.location.kind === "orbit" ? vessel.location.orbit.referenceBody : vessel.location.body));
    expect(scene.entities.filter((entity) => entity.kind === "vessel")).toHaveLength(around.length);
  });
});

describe("rocks", () => {
  const rock = (name: string, spaceObject: Vessel["spaceObject"]) => ({ vesselType: "spaceObject" as const, name, spaceObject });

  it("trusts the plugin's comet module over the name, and falls back to the name without it", () => {
    expect(lookOf(rock("Ast. QXT-411", { kind: "comet", sizeClass: "b" }))).toBe("comet");
    expect(lookOf(rock("Comet Hale", { kind: "asteroid", sizeClass: "b" }))).toBe("asteroid");
    expect(lookOf(rock("C/2026 K1", null))).toBe("comet");
    expect(lookOf(rock("Ast. QXT-411", null))).toBe("asteroid");
  });

  it("draws asteroids larger class by class from a to e, and comets on their own scale", () => {
    const asteroids = (["a", "b", "c", "d", "e"] as const).map((sizeClass) => rockScale(rock("Ast.", { kind: "asteroid", sizeClass })));
    expect(asteroids).toEqual([...asteroids].sort((x, y) => x - y));
    expect(new Set(asteroids).size).toBe(5);
    expect(rockScale(rock("C/1", { kind: "comet", sizeClass: "a" }))).not.toBe(asteroids[0]);
    expect(rockScale(rock("C/1", { kind: "comet", sizeClass: "a" }))).toBeLessThan(rockScale(rock("C/1", { kind: "comet", sizeClass: "e" })));
    expect(rockScale(rock("Ast.", { kind: "asteroid", sizeClass: null }))).toBe(1);
    expect(rockScale({ vesselType: "probe", name: "Probe", spaceObject: null })).toBe(1);
  });

  it("gives the crowded fixture's rocks their own sizes on the map", () => {
    const fleet = load("crowded");
    const scene = buildScene({ fleet, center: "Sun", selectedId: null });
    const rocks = scene.entities.filter((entity) => entity.look !== "craft");
    expect(new Set(rocks.map((entity) => entity.scale)).size).toBeGreaterThanOrEqual(5);
    expect(rocks.find((entity) => entity.text.startsWith("C/"))?.look).toBe("comet");
  });
});
