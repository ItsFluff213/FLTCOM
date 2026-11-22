export interface LabelInput {
  id: string;
  ax: number;
  ay: number;
  w: number;
  h: number;
  priority: number;
  /** Pixels between the anchor and the nearest label edge; clears the mark drawn at the anchor. Default 8. */
  clear?: number;
  /** Never hidden for want of room: shown at its first spot unless that would cover a label already placed. */
  keep?: boolean;
}

/** A screen rectangle labels keep off, such as another ship's bracket. */
export interface Obstacle {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface PlacedLabel {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
  ax: number;
  ay: number;
  leader: boolean;
  hidden: boolean;
}

export interface Viewport {
  w: number;
  h: number;
}

const DIRECTIONS: ReadonlyArray<readonly [number, number]> = [
  [1, 0], [-1, 0], [0, -1], [0, 1], [1, -1], [1, 1], [-1, -1], [-1, 1],
];
const RINGS = 14;
const GAP = 1;

const clamp = (value: number, low: number, high: number) => Math.min(Math.max(value, low), Math.max(low, high));

type Rect = Pick<PlacedLabel, "x" | "y" | "w" | "h">;

const overlaps = (a: Rect, b: Rect) =>
  a.x < b.x + b.w + GAP && b.x < a.x + a.w + GAP && a.y < b.y + b.h + GAP && b.y < a.y + a.h + GAP;

export function declutter(inputs: readonly LabelInput[], viewport: Viewport, obstacles: readonly Obstacle[] = []): PlacedLabel[] {
  const order = [...inputs].sort((a, b) => b.priority - a.priority || a.ay - b.ay || a.ax - b.ax || (a.id < b.id ? -1 : 1));
  const placed: PlacedLabel[] = [];
  for (const input of order) {
    let spot: PlacedLabel | null = null;
    search: for (let ring = 0; ring < RINGS; ring++) {
      const distance = (input.clear ?? 8) + ring * (input.h + 2);
      for (const [dx, dy] of DIRECTIONS) {
        const x = clamp(input.ax + dx * distance - (dx < 0 ? input.w : dx === 0 ? input.w / 2 : 0), 0, viewport.w - input.w);
        const y = clamp(input.ay + dy * distance - (dy < 0 ? input.h : dy === 0 ? input.h / 2 : 0), 0, viewport.h - input.h);
        const candidate: PlacedLabel = { id: input.id, x, y, w: input.w, h: input.h, ax: input.ax, ay: input.ay, leader: ring > 0, hidden: false };
        const blocked = obstacles.some((box) => box.id !== input.id && overlaps(candidate, box));
        if (!blocked && !placed.some((other) => !other.hidden && overlaps(candidate, other))) {
          spot = candidate;
          break search;
        }
      }
    }
    if (!spot) {
      const fallback: PlacedLabel = {
        id: input.id,
        x: clamp(input.ax + (input.clear ?? 8), 0, viewport.w - input.w),
        y: clamp(input.ay - input.h / 2, 0, viewport.h - input.h),
        w: input.w,
        h: input.h,
        ax: input.ax,
        ay: input.ay,
        leader: false,
        hidden: true,
      };
      fallback.hidden = !input.keep || placed.some((other) => !other.hidden && overlaps(fallback, other));
      spot = fallback;
    }
    placed.push(spot);
  }
  return placed;
}

export function countOverlaps(labels: readonly PlacedLabel[]): number {
  const shown = labels.filter((label) => !label.hidden);
  let count = 0;
  for (let i = 0; i < shown.length; i++) {
    for (let j = i + 1; j < shown.length; j++) {
      const a = shown[i]!;
      const b = shown[j]!;
      if (a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h) {
        count++;
      }
    }
  }
  return count;
}
