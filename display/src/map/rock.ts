import type { Vec3 } from "../lib/kepler.ts";

export interface RockShape {
  positions: number[];
  indices: number[];
}

export interface Tumble {
  axis: Vec3;
  /** Radians per second. */
  speed: number;
}

/** FNV-1a, so an asteroid's id or name always picks the same shape. */
function hash(seed: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < seed.length; i++) {
    h = Math.imul(h ^ seed.charCodeAt(i), 0x01000193);
  }
  return h >>> 0;
}

function mulberry32(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const unit = (v: Vec3): Vec3 => {
  const length = Math.hypot(v.x, v.y, v.z) || 1;
  return { x: v.x / length, y: v.y / length, z: v.z / length };
};

const randomDirection = (random: () => number): Vec3 => {
  const z = random() * 2 - 1;
  const a = random() * 2 * Math.PI;
  const r = Math.sqrt(1 - z * z);
  return { x: r * Math.cos(a), y: r * Math.sin(a), z };
};

const dot = (a: Vec3, b: Vec3) => a.x * b.x + a.y * b.y + a.z * b.z;

function icosphere(detail: number): { vertices: Vec3[]; faces: [number, number, number][] } {
  const t = (1 + Math.sqrt(5)) / 2;
  const vertices: Vec3[] = [
    [-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0],
    [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t],
    [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1],
  ].map(([x, y, z]) => unit({ x: x!, y: y!, z: z! }));
  let faces: [number, number, number][] = [
    [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11],
    [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
    [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9],
    [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
  ];
  for (let level = 0; level < detail; level++) {
    const midpoints = new Map<string, number>();
    const middle = (a: number, b: number) => {
      const key = a < b ? `${a}:${b}` : `${b}:${a}`;
      let index = midpoints.get(key);
      if (index === undefined) {
        const p = vertices[a]!;
        const q = vertices[b]!;
        index = vertices.push(unit({ x: p.x + q.x, y: p.y + q.y, z: p.z + q.z })) - 1;
        midpoints.set(key, index);
      }
      return index;
    };
    faces = faces.flatMap(([a, b, c]) => {
      const ab = middle(a, b);
      const bc = middle(b, c);
      const ca = middle(c, a);
      return [[a, ab, ca], [b, bc, ab], [c, ca, bc], [ab, bc, ca]] as [number, number, number][];
    });
  }
  return { vertices, faces };
}

/**
 * A lumpy rock of about unit radius: an icosphere pushed in and out by a few seeded lobes, roughened per vertex,
 * with a few crater dents. Its largest vertex sits at radius 1, so the map sizes every rock alike.
 */
export function rockShape(seed: string, detail = 2): RockShape {
  const random = mulberry32(hash(seed));
  const lobes = Array.from({ length: 5 }, () => ({ direction: randomDirection(random), weight: 0.12 + random() * 0.22 }));
  const craters = Array.from({ length: 3 }, () => ({ direction: randomDirection(random), size: 0.35 + random() * 0.3, depth: 0.08 + random() * 0.08 }));
  const stretch = { x: 1, y: 0.75 + random() * 0.25, z: 0.6 + random() * 0.3 };
  const { vertices, faces } = icosphere(detail);
  const radii = vertices.map((v) => {
    let r = 0.8 + (random() - 0.5) * 0.08;
    for (const lobe of lobes) {
      r += lobe.weight * Math.max(0, dot(v, lobe.direction)) ** 3;
    }
    for (const crater of craters) {
      const angle = Math.acos(Math.max(-1, Math.min(1, dot(v, crater.direction))));
      if (angle < crater.size) {
        r -= crater.depth * (1 - (angle / crater.size) ** 2);
      }
    }
    return r;
  });
  const points = vertices.map((v, i) => ({ x: v.x * radii[i]! * stretch.x, y: v.y * radii[i]! * stretch.y, z: v.z * radii[i]! * stretch.z }));
  const largest = Math.max(...points.map((p) => Math.hypot(p.x, p.y, p.z)));
  return {
    positions: points.flatMap((p) => [p.x / largest, p.y / largest, p.z / largest]),
    indices: faces.flat(),
  };
}

/** Each rock turns slowly about its own seeded axis. */
export function tumbleOf(seed: string): Tumble {
  const random = mulberry32(hash(`tumble:${seed}`));
  return { axis: randomDirection(random), speed: 0.06 + random() * 0.24 };
}
