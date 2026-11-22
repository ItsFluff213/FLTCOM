import type { CraftPart, Quat, StageDeltaV } from "../protocol.gen.ts";

export interface Pt {
  x: number;
  y: number;
}

export interface DrawnPart {
  id: number;
  stage: number;
  category: CraftPart["category"];
  outline: Pt[];
  detail: Pt[][];
}

export interface StageBand {
  stage: number;
  top: number;
  bottom: number;
  left: number;
  right: number;
  stats: StageDeltaV | null;
}

export interface Blueprint {
  parts: DrawnPart[];
  stages: StageBand[];
  bounds: { left: number; right: number; top: number; bottom: number };
}

function rotate(q: Quat, v: { x: number; y: number; z: number }) {
  const tx = 2 * (q.y * v.z - q.z * v.y);
  const ty = 2 * (q.z * v.x - q.x * v.z);
  const tz = 2 * (q.x * v.y - q.y * v.x);
  return {
    x: v.x + q.w * tx + (q.y * tz - q.z * ty),
    y: v.y + q.w * ty + (q.z * tx - q.x * tz),
    z: v.z + q.w * tz + (q.x * ty - q.y * tx),
  };
}

const SHAPES: Record<CraftPart["category"], { outline: Pt[]; detail: Pt[][] }> = {
  engine: { outline: [{ x: -0.3, y: 0.5 }, { x: 0.3, y: 0.5 }, { x: 0.5, y: -0.5 }, { x: -0.5, y: -0.5 }], detail: [[{ x: -0.3, y: 0.1 }, { x: 0.3, y: 0.1 }]] },
  tank: { outline: [{ x: -0.5, y: 0.5 }, { x: 0.5, y: 0.5 }, { x: 0.5, y: -0.5 }, { x: -0.5, y: -0.5 }], detail: [[{ x: -0.5, y: 0.17 }, { x: 0.5, y: 0.17 }], [{ x: -0.5, y: -0.17 }, { x: 0.5, y: -0.17 }]] },
  pod: { outline: [{ x: -0.5, y: -0.5 }, { x: 0.5, y: -0.5 }, { x: 0.5, y: 0.1 }, { x: 0.18, y: 0.5 }, { x: -0.18, y: 0.5 }, { x: -0.5, y: 0.1 }], detail: [[{ x: -0.3, y: -0.1 }, { x: 0.3, y: -0.1 }]] },
  decoupler: { outline: [{ x: -0.5, y: 0.5 }, { x: 0.5, y: 0.5 }, { x: 0.5, y: -0.5 }, { x: -0.5, y: -0.5 }], detail: [[{ x: -0.5, y: 0 }, { x: 0.5, y: 0 }]] },
  other: { outline: [{ x: -0.5, y: 0.5 }, { x: 0.5, y: 0.5 }, { x: 0.5, y: -0.5 }, { x: -0.5, y: -0.5 }], detail: [] },
};

export function layoutBlueprint(parts: readonly CraftPart[], stats: readonly StageDeltaV[]): Blueprint | null {
  if (parts.length === 0) {
    return null;
  }
  const place = (part: CraftPart, point: Pt): Pt => {
    const r = rotate(part.rotation, { x: point.x * part.size.x, y: point.y * part.size.y, z: 0 });
    return { x: part.position.x + r.x, y: part.position.y + r.y };
  };
  const drawn: DrawnPart[] = parts.map((part) => ({
    id: part.id,
    stage: part.stage,
    category: part.category,
    outline: SHAPES[part.category].outline.map((point) => place(part, point)),
    detail: SHAPES[part.category].detail.map((line) => line.map((point) => place(part, point))),
  }));
  const all = drawn.flatMap((part) => part.outline);
  const bounds = {
    left: Math.min(...all.map((point) => point.x)),
    right: Math.max(...all.map((point) => point.x)),
    top: Math.max(...all.map((point) => point.y)),
    bottom: Math.min(...all.map((point) => point.y)),
  };
  const stageIds = [...new Set(drawn.map((part) => part.stage))].sort((a, b) => b - a);
  const stages = stageIds.map((stage): StageBand => {
    const own = drawn.filter((part) => part.stage === stage).flatMap((part) => part.outline);
    return {
      stage,
      top: Math.max(...own.map((point) => point.y)),
      bottom: Math.min(...own.map((point) => point.y)),
      left: Math.min(...own.map((point) => point.x)),
      right: Math.max(...own.map((point) => point.x)),
      stats: stats.find((entry) => entry.stage === stage) ?? null,
    };
  });
  return { parts: drawn, stages, bounds };
}

export function stageColor(stage: number): string {
  return stage < 0 ? "#6f6b68" : `hsl(30 5% ${84 - (stage % 4) * 9}%)`;
}
