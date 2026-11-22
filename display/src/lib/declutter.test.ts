import { describe, expect, it } from "vitest";
import { countOverlaps, declutter, type LabelInput } from "./declutter.ts";

const label = (id: string, ax: number, ay: number, priority = 0): LabelInput => ({ id, ax, ay, w: 80, h: 14, priority });
const view = { w: 400, h: 300 };

describe("declutter", () => {
  it("moves labels stacked on one spot apart and draws leaders once the near slots run out", () => {
    const stack = Array.from({ length: 12 }, (_, i) => label(`s${i}`, 200, 150, i === 0 ? 1 : 0));
    const placed = declutter(stack, view);
    expect(countOverlaps(placed)).toBe(0);
    expect(placed.find((p) => p.id === "s0")?.leader).toBe(false);
    expect(placed.some((p) => p.leader)).toBe(true);
  });

  it("separates two labels on the same spot", () => {
    const placed = declutter([label("a", 200, 150), label("b", 200, 150)], view);
    expect(countOverlaps(placed)).toBe(0);
  });

  it("keeps a label at the screen edge inside the viewport", () => {
    const placed = declutter([label("edge", 398, 299), label("corner", 0, 0)], view);
    for (const p of placed) {
      expect(p.x).toBeGreaterThanOrEqual(0);
      expect(p.y).toBeGreaterThanOrEqual(0);
      expect(p.x + p.w).toBeLessThanOrEqual(view.w);
      expect(p.y + p.h).toBeLessThanOrEqual(view.h);
    }
  });

  it("never leaves two shown labels overlapping in a crowd, and gives the selected one its place", () => {
    const crowd = Array.from({ length: 120 }, (_, i) => label(`v${i}`, 180 + (i % 7) * 6, 140 + (i % 5) * 4, i === 40 ? 1000 : 0));
    const placed = declutter(crowd, view);
    expect(countOverlaps(placed)).toBe(0);
    expect(placed.find((p) => p.id === "v40")?.hidden).toBe(false);
  });

  it("keeps a label clear of the mark at its anchor and off other ships' brackets", () => {
    const ship = { ...label("ship", 200, 150), clear: 30 };
    const placed = declutter([ship], view, [{ id: "other", x: 225, y: 100, w: 120, h: 100 }]);
    const p = placed[0]!;
    expect(p.hidden).toBe(false);
    const gapX = Math.max(p.x - 200, 200 - (p.x + p.w));
    const gapY = Math.max(p.y - 150, 150 - (p.y + p.h));
    expect(Math.max(gapX, gapY)).toBeGreaterThanOrEqual(30);
    expect(p.x + p.w <= 225 || p.x >= 345 || p.y + p.h <= 100 || p.y >= 200).toBe(true);
  });
});
