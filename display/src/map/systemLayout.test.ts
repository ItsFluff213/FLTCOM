import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parseServerMessage, type Fleet } from "../protocol.gen.ts";
import { unitsPerPixel } from "./camera.ts";
import { MIN_SHARE } from "./marks.ts";
import { buildScene, type MapScene } from "./Scene.ts";
import { drawnPoint, layoutSystem, type LayoutScale, type SystemLayout } from "./systemLayout.ts";

function load(name: string): Fleet {
  const parsed = parseServerMessage(readFileSync(new URL(`../../../tests/fixtures/telemetry/${name}.json`, import.meta.url), "utf8"));
  if (!parsed.ok || parsed.message.type !== "fleet") {
    throw new Error(`bad fixture ${name}`);
  }
  return parsed.message;
}

const W = 1280;
const H = 800;
const fleet = load("kerbin-small");

/** The scale the renderer uses for a camera this many map units from its target. */
function scaleFor(scene: MapScene, cameraDistance: number): LayoutScale {
  return {
    metresPerPx: unitsPerPixel(cameraDistance, H) * scene.extent,
    budgetPx: 0.46 * Math.min(W, 2 * H),
    starPx: MIN_SHARE.star * H,
    planetPx: MIN_SHARE.body * H,
    moonPx: MIN_SHARE.moon * H,
    rockPx: (MIN_SHARE.vessel * H) / 2,
    gapPx: 6,
  };
}

const gap = (a: { x: number; y: number; z: number }, b: { x: number; y: number; z: number }) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);

function overlapping(layout: SystemLayout): string[] {
  const names = [...layout.keys()];
  const found: string[] = [];
  for (let i = 0; i < names.length; i++) {
    for (let j = i + 1; j < names.length; j++) {
      const a = layout.get(names[i]!)!;
      const b = layout.get(names[j]!)!;
      if (gap(a.position, b.position) < a.radius + b.radius) {
        found.push(`${names[i]}/${names[j]}`);
      }
    }
  }
  return found;
}

describe("layoutSystem at system scale", () => {
  const sun = buildScene({ fleet, center: "Sun", selectedId: null });
  const scale = scaleFor(sun, 2.4);
  const layout = layoutSystem(sun.bodies, scale);
  const moons = sun.bodies.filter((body) => body.parent === "Jool").sort((a, b) => a.semiMajorAxis - b.semiMajorAxis);

  it("spreads Jool's five moons outside Jool's drawn sphere, each on its own ring in true order", () => {
    expect(moons.map((moon) => moon.name)).toEqual(["Laythe", "Vall", "Tylo", "Bop", "Pol"]);
    const jool = layout.get("Jool")!;
    const rings = moons.map((moon) => ({ moon, drawn: layout.get(moon.name)!, ring: layout.get(moon.name)!.scale * moon.semiMajorAxis }));
    for (const { moon, drawn, ring } of rings) {
      expect(ring * (1 - moon.eccentricity) - drawn.radius, moon.name).toBeGreaterThan(jool.radius);
      expect(gap(drawn.position, jool.position), moon.name).toBeGreaterThan(jool.radius + drawn.radius);
    }
    for (let i = 1; i < rings.length; i++) {
      const inner = rings[i - 1]!;
      const outer = rings[i]!;
      expect(outer.ring - inner.ring, `${inner.moon.name} to ${outer.moon.name}`).toBeGreaterThanOrEqual(inner.drawn.radius + outer.drawn.radius);
    }
  });

  it("draws no body on top of another anywhere in the system", () => {
    expect(overlapping(layout)).toEqual([]);
  });

  it("draws no body on top of another around the Sun, Jool or Kerbin at any zoom", () => {
    for (const center of ["Sun", "Jool", "Kerbin"]) {
      const scene = buildScene({ fleet, center, selectedId: null });
      for (const distance of [12, 4, 2.4, 1, 0.3, 0.1, 0.03, 0.01]) {
        expect(overlapping(layoutSystem(scene.bodies, scaleFor(scene, distance))), `${center} at ${distance}`).toEqual([]);
      }
    }
  });

  it("keeps the whole spread system inside the screen budget", () => {
    const farthest = Math.max(...[...layout.values()].map((drawn) => Math.hypot(drawn.position.x, drawn.position.y, drawn.position.z) + drawn.radius));
    expect(farthest / scale.metresPerPx).toBeLessThanOrEqual(scale.budgetPx * 1.15);
  });
});

describe("layoutSystem zoomed in", () => {
  const sun = buildScene({ fleet, center: "Sun", selectedId: null });

  it("returns to true scale and true positions close to Jool", () => {
    const layout = layoutSystem(sun.bodies, { ...scaleFor(sun, 2.4), metresPerPx: 50 });
    for (const body of sun.bodies) {
      const drawn = layout.get(body.name)!;
      expect(drawn.scale, body.name).toBe(1);
      expect(gap(drawn.position, body.position) / Math.max(1, Math.hypot(body.position.x, body.position.y, body.position.z)), body.name).toBeLessThan(1e-9);
    }
  });

  it("blends with no jump as the camera closes in", () => {
    let last = layoutSystem(sun.bodies, scaleFor(sun, 4));
    for (let distance = 4; distance > 1e-6; distance /= 1.02) {
      const next = layoutSystem(sun.bodies, scaleFor(sun, distance));
      for (const [name, drawn] of next) {
        expect(drawn.scale / last.get(name)!.scale, `${name} at ${distance}`).toBeGreaterThan(0.94);
        expect(drawn.scale / last.get(name)!.scale, `${name} at ${distance}`).toBeLessThan(1 / 0.94);
      }
      last = next;
    }
    expect([...last.values()].every((drawn) => drawn.scale === 1)).toBe(true);
  });
});

describe("layoutSystem around Kerbin", () => {
  const kerbin = buildScene({ fleet, center: "Kerbin", selectedId: null });

  it("keeps the Mun and Minmus clear of Kerbin and of each other when zoomed out", () => {
    const layout = layoutSystem(kerbin.bodies, scaleFor(kerbin, 12));
    expect(overlapping(layout)).toEqual([]);
  });

  it("moves a ship around the Mun with the Mun's drawn position", () => {
    const layout = layoutSystem(kerbin.bodies, scaleFor(kerbin, 12));
    const mun = kerbin.bodies.find((body) => body.name === "Mun")!;
    const truePositions = new Map(kerbin.bodies.map((body) => [body.name, body.position]));
    const ship = { x: mun.position.x + 250_000, y: mun.position.y, z: mun.position.z };
    const drawn = drawnPoint(layout, truePositions, "Mun", null, ship);
    expect(drawn.x - layout.get("Mun")!.position.x).toBeCloseTo(250_000, 3);
  });
});

describe("layoutSystem with objects orbiting the star", () => {
  const crowded = buildScene({ fleet: load("crowded"), center: "Sun", selectedId: null });
  const layout = layoutSystem(crowded.bodies, scaleFor(crowded, 2.4), crowded.rocks);
  const kerbinRing = Math.hypot(layout.get("Kerbin")!.position.x, layout.get("Kerbin")!.position.y, layout.get("Kerbin")!.position.z);

  it("draws an asteroid near Kerbin's orbit near Kerbin's spread-out ring, not down by Moho", () => {
    const rocks = crowded.entities.filter((entity) => entity.look === "asteroid" && entity.frame === "Sun");
    expect(rocks).toHaveLength(15);
    for (const rock of rocks) {
      const at = layout.get(rock.id)!.position;
      expect(Math.hypot(at.x, at.y, at.z) / kerbinRing, rock.text).toBeGreaterThan(0.85);
      expect(Math.hypot(at.x, at.y, at.z) / kerbinRing, rock.text).toBeLessThan(1.15);
    }
  });
});

describe("layoutSystem with asteroids and comets", () => {
  const fleet = load("crowded");
  const sun = buildScene({ fleet, center: "Sun", selectedId: null });
  const kerbin = buildScene({ fleet, center: "Kerbin", selectedId: null });
  const captured = sun.rocks.filter((rock) => rock.frame === "Kerbin");
  const DISTANCES = [12, 4, 2.4, 1, 0.3, 0.1, 0.03, 0.01, 1e-3, 1e-4];

  it("draws no rock on a body, a moon or another rock around the Sun or Kerbin at any zoom", () => {
    expect(captured).toHaveLength(2);
    for (const scene of [sun, kerbin]) {
      for (const distance of DISTANCES) {
        const layout = layoutSystem(scene.bodies, scaleFor(scene, distance), scene.rocks);
        expect(scene.rocks.every((rock) => layout.has(rock.id)), `${scene.center.name} at ${distance}`).toBe(true);
        expect(overlapping(layout), `${scene.center.name} at ${distance}`).toEqual([]);
      }
    }
  });

  it("keeps every rock smaller than the planet it is near, larger classes larger", () => {
    const scale = scaleFor(sun, 2.4);
    const layout = layoutSystem(sun.bodies, scale, sun.rocks);
    const planet = layout.get("Kerbin")!.radius;
    for (const rock of sun.rocks) {
      expect(layout.get(rock.id)!.radius, rock.id).toBeLessThan(planet);
    }
    const loose = sun.rocks.filter((rock) => rock.frame === "Sun" && rock.spreadWith === "Kerbin").sort((a, b) => a.size - b.size);
    for (let i = 1; i < loose.length; i++) {
      expect(layout.get(loose[i]!.id)!.radius).toBeGreaterThanOrEqual(layout.get(loose[i - 1]!.id)!.radius);
    }
  });

  it("keeps a captured asteroid off Kerbin's drawn sphere and off the Mun's and Minmus's rings", () => {
    for (const scene of [sun, kerbin]) {
      for (const distance of [12, 2.4, 0.3]) {
        const layout = layoutSystem(scene.bodies, scaleFor(scene, distance), scene.rocks);
        const planet = layout.get("Kerbin")!;
        for (const rock of captured) {
          const drawn = layout.get(rock.id)!;
          const out = gap(drawn.position, planet.position);
          expect(out, `${rock.id} in ${scene.center.name} at ${distance}`).toBeGreaterThan(planet.radius + drawn.radius);
          for (const moon of ["Mun", "Minmus"]) {
            const ring = layout.get(moon)!;
            expect(Math.abs(out - gap(ring.position, planet.position)), `${rock.id} on the ${moon} ring`).toBeGreaterThan(ring.radius + drawn.radius);
          }
        }
      }
    }
  });

  it("blends every rock back to its true place with no jump as the camera closes in", () => {
    const truePositions = new Map(sun.bodies.map((body) => [body.name, body.position]));
    let last = layoutSystem(sun.bodies, scaleFor(sun, 4), sun.rocks);
    for (let distance = 4; distance > 1e-6; distance /= 1.02) {
      const scale = scaleFor(sun, distance);
      const next = layoutSystem(sun.bodies, scale, sun.rocks);
      for (const rock of sun.rocks) {
        const before = last.get(rock.id)!.position;
        const moved = gap(next.get(rock.id)!.position, before);
        expect(moved, `${rock.id} at ${distance}`).toBeLessThan(0.05 * Math.hypot(before.x, before.y, before.z) + 4 * scale.metresPerPx);
      }
      last = next;
    }
    for (const rock of sun.rocks) {
      const home = truePositions.get(rock.frame)!;
      const drawnHome = last.get(rock.frame)!.position;
      const at = last.get(rock.id)!.position;
      expect(gap(at, { x: rock.position.x - home.x + drawnHome.x, y: rock.position.y - home.y + drawnHome.y, z: rock.position.z - home.z + drawnHome.z }), rock.id).toBeLessThan(1);
    }
  });
});
