import type { Vec3 } from "../lib/kepler.ts";
import type { MapBody } from "./Scene.ts";

/** Where the map draws a body or rock at the current zoom: its position and radius in metres, and how far its orbit is spread out. */
export interface DrawnBody {
  position: Vec3;
  radius: number;
  /** Drawn semi-major axis over the true one: 1 at true scale, more when the orbit is spread out to stay readable. */
  scale: number;
}

/** Keyed by body name, and by entity id for each rock. */
export type SystemLayout = ReadonlyMap<string, DrawnBody>;

export interface LayoutScale {
  /** Metres one screen pixel spans at the camera target. */
  metresPerPx: number;
  /** Pixels the whole system may span from the center when fully spread out. */
  budgetPx: number;
  starPx: number;
  planetPx: number;
  moonPx: number;
  /** Radius of a size-1 rock when nothing nearby caps it. */
  rockPx: number;
  gapPx: number;
}

type LayoutBody = Pick<MapBody, "name" | "parent" | "radius" | "star" | "position" | "semiMajorAxis" | "eccentricity">;

/** An asteroid or comet on an orbit: laid out with the bodies so it never sits on a planet, a moon ring or another rock. */
export interface LayoutRock {
  id: string;
  /** The body it orbits. */
  frame: string;
  /** For a rock orbiting the star: the planet whose spread it follows. */
  spreadWith: string | null;
  position: Vec3;
  semiMajorAxis: number;
  eccentricity: number;
  /** Size class scale; larger classes draw larger. */
  size: number;
}

type Node = LayoutBody & { rock?: number };

interface Solved {
  radius: number;
  /** Drawn semi-major axis. */
  ring: number;
  /** How far the body and everything spread around it reach from its center. */
  extent: number;
}

const FIT_FLOOR = 0.3;
/** The largest rock class stays smaller than the planet it is near. */
const ROCK_SHARE = 0.35;
const RELAX_ROUNDS = 12;

const sub = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const length = (v: Vec3) => Math.hypot(v.x, v.y, v.z);
const along = (from: Vec3, direction: Vec3, distance: number): Vec3 => {
  const n = length(direction);
  const unit = n > 0 ? { x: direction.x / n, y: direction.y / n, z: direction.z / n } : { x: 1, y: 0, z: 0 };
  return { x: from.x + unit.x * distance, y: from.y + unit.y * distance, z: from.z + unit.z * distance };
};

/** A rock's drawn radius: its class scale times the smaller of the rock pixel size and a share of the nearby body's drawn sphere. */
const rockRadius = (size: number, metresPerPx: number, rockPx: number, nearRadius: number) => size * Math.min(rockPx * metresPerPx, ROCK_SHARE * nearRadius);

/**
 * Lays out the bodies of a scene so none is drawn inside or on top of another. Each body is drawn at least a few
 * pixels across; each orbit is spread out from its parent's drawn sphere, in order of true semi-major axis, until it
 * clears the parent and the ring inside it. Every bound is the larger of the true value and a pixel clearance, so as
 * the camera closes in the layout blends back to true scale without a jump.
 *
 * Rocks orbiting a planet or moon take a ring of their own among its moons. Rocks orbiting the star follow their
 * planet's spread and are then pushed clear of every planet's spread-out system and of each other.
 */
export function layoutSystem(bodies: readonly LayoutBody[], scale: LayoutScale, rocks: readonly LayoutRock[] = []): SystemLayout {
  const root = bodies.find((body) => body.parent === null);
  if (!root) {
    return new Map();
  }
  const known = new Set(bodies.map((body) => body.name));
  const free = rocks.filter((rock) => known.has(rock.frame) && rock.frame === root.name && root.star);
  const nodes: Node[] = [...bodies];
  for (const rock of rocks) {
    if (known.has(rock.frame) && !free.includes(rock)) {
      const bound = rock.eccentricity < 1;
      nodes.push({
        name: rock.id,
        parent: rock.frame,
        radius: 0,
        star: false,
        position: rock.position,
        // A flyby is spread by its periapsis, the closest it comes.
        semiMajorAxis: bound ? rock.semiMajorAxis : rock.semiMajorAxis * (1 - rock.eccentricity),
        eccentricity: bound ? rock.eccentricity : 0,
        rock: rock.size,
      });
    }
  }
  const children = new Map<string, Node[]>();
  for (const node of nodes) {
    if (node.parent !== null) {
      children.set(node.parent, [...(children.get(node.parent) ?? []), node]);
    }
  }
  for (const list of children.values()) {
    list.sort((a, b) => a.semiMajorAxis - b.semiMajorAxis);
  }
  const minPx = (body: Node, depth: number) => (depth === 0 && body.star ? scale.starPx : depth <= 1 ? scale.planetPx : scale.moonPx);

  const solve = (metresPerPx: number, real: boolean): { solved: Map<string, Solved>; footprint: number } => {
    const solved = new Map<string, Solved>();
    const gap = scale.gapPx * metresPerPx;
    const visit = (body: Node, depth: number, parentRadius: number): number => {
      const radius = body.rock !== undefined ? rockRadius(body.rock, metresPerPx, scale.rockPx, parentRadius) : Math.max(real ? body.radius : 0, minPx(body, depth) * metresPerPx);
      let extent = radius;
      let inner: { ring: number; footprint: number; node: Node } | null = null;
      for (const child of children.get(body.name) ?? []) {
        const footprint = visit(child, depth + 1, radius);
        const e = child.eccentricity;
        const clearParent = (radius + footprint + gap) / (1 - e);
        // Mean rings, not periapsis past apoapsis: that rule compounds outward and orbits that truly cross could never part.
        // Next to a rock whose orbit truly clears its neighbour's, keep the whole orbits apart so the rock never sits on a moon ring.
        const apart = inner && (inner.node.rock !== undefined || child.rock !== undefined) && inner.node.semiMajorAxis * (1 + inner.node.eccentricity) < child.semiMajorAxis * (1 - e);
        const clearInner = !inner ? 0 : apart ? (inner.ring * (1 + inner.node.eccentricity) + inner.footprint + footprint + gap) / (1 - e) : inner.ring + inner.footprint + footprint + gap;
        const ring = Math.max(real ? child.semiMajorAxis : 0, clearParent, clearInner);
        solved.get(child.name)!.ring = ring;
        extent = Math.max(extent, ring * (1 + e) + footprint);
        inner = { ring, footprint, node: child };
      }
      solved.set(body.name, { radius, ring: 0, extent });
      return extent;
    };
    return { solved, footprint: visit(root, 0, 0) };
  };

  const schematic = solve(1, false).footprint;
  const metresPerPx = Math.max(FIT_FLOOR, Math.min(1, scale.budgetPx / Math.max(1, schematic))) * scale.metresPerPx;
  const { solved } = solve(metresPerPx, true);

  const out = new Map<string, DrawnBody>();
  const place = (body: Node, at: Vec3) => {
    for (const child of children.get(body.name) ?? []) {
      const s = child.semiMajorAxis > 0 ? solved.get(child.name)!.ring / child.semiMajorAxis : 1;
      const position = {
        x: at.x + s * (child.position.x - body.position.x),
        y: at.y + s * (child.position.y - body.position.y),
        z: at.z + s * (child.position.z - body.position.z),
      };
      out.set(child.name, { position, radius: solved.get(child.name)!.radius, scale: s });
      place(child, position);
    }
  };
  out.set(root.name, { position: { x: 0, y: 0, z: 0 }, radius: solved.get(root.name)!.radius, scale: 1 });
  place(root, { x: 0, y: 0, z: 0 });

  const gap = scale.gapPx * metresPerPx;
  const obstacles = [
    { at: out.get(root.name)!.position, reach: solved.get(root.name)!.radius },
    ...bodies.filter((body) => body.parent === root.name).map((body) => ({ at: out.get(body.name)!.position, reach: solved.get(body.name)!.extent })),
  ];
  const loose = free.map((rock) => {
    const near = rock.spreadWith ? out.get(rock.spreadWith) : undefined;
    const s = near?.scale ?? 1;
    const position = { x: s * (rock.position.x - root.position.x), y: s * (rock.position.y - root.position.y), z: s * (rock.position.z - root.position.z) };
    return { id: rock.id, position, radius: rockRadius(rock.size, metresPerPx, scale.rockPx, near?.radius ?? scale.planetPx * metresPerPx), scale: s };
  });
  const clearBodies = () => {
    for (const rock of loose) {
      for (const { at, reach } of obstacles) {
        const offset = sub(rock.position, at);
        const need = reach + rock.radius + gap;
        if (length(offset) < need) {
          rock.position = along(at, length(offset) > 0 ? offset : rock.position, need);
        }
      }
    }
  };
  clearBodies();
  for (let round = 0; round < RELAX_ROUNDS; round++) {
    let moved = false;
    for (let i = 0; i < loose.length; i++) {
      for (let j = i + 1; j < loose.length; j++) {
        const a = loose[i]!;
        const b = loose[j]!;
        const offset = sub(b.position, a.position);
        const d = length(offset);
        const need = a.radius + b.radius + gap;
        if (d < need) {
          const push = (need - d) / 2;
          const direction = d > 0 ? offset : { x: Math.cos(j), y: Math.sin(j), z: 0 };
          a.position = along(a.position, direction, -push);
          b.position = along(b.position, direction, push);
          moved = true;
        }
      }
    }
    clearBodies();
    if (!moved) {
      break;
    }
  }
  for (const { id, position, radius, scale: s } of loose) {
    out.set(id, { position, radius, scale: s });
  }
  return out;
}

/** Where something placed in a body's frame is drawn: offset from that body's drawn position, spread by scaledBy's ring scale. */
export function drawnPoint(layout: SystemLayout, truePositions: ReadonlyMap<string, Vec3>, frame: string, scaledBy: string | null, p: Vec3): Vec3 {
  const origin = layout.get(frame)?.position;
  const at = truePositions.get(frame);
  if (!origin || !at) {
    return p;
  }
  const s = scaledBy ? (layout.get(scaledBy)?.scale ?? 1) : 1;
  return { x: origin.x + s * (p.x - at.x), y: origin.y + s * (p.y - at.y), z: origin.z + s * (p.z - at.z) };
}
