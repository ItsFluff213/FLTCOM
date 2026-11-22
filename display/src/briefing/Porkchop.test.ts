import { describe, expect, it } from "vitest";
import type { PorkchopPlot } from "../protocol.gen.ts";
import { bestPoint, DAY, days, porkchopCells, shade, shadeColor } from "../lib/porkchop.ts";

const plot = (patch: Partial<PorkchopPlot> = {}): PorkchopPlot => {
  const columns = 4;
  const rows = 3;
  const deltaV = Array.from({ length: columns * rows }, (_, index) => 1500 + 100 * index);
  return {
    departureStart: 100 * DAY,
    departureStep: 10 * DAY,
    flightStart: 150 * DAY,
    flightStep: 50 * DAY,
    columns,
    rows,
    deltaV,
    bestDeparture: 100 * DAY,
    bestFlight: 150 * DAY,
    bestDeltaV: 1500,
    ...patch,
  };
};

describe("porkchop plot", () => {
  it("draws a cell for every transfer, the longest flight at the top", () => {
    const cells = porkchopCells(plot());
    expect(cells).toHaveLength(12);
    expect(cells.find((cell) => cell.deltaV === 1500)).toMatchObject({ column: 0, row: 2 });
    expect(cells.find((cell) => cell.deltaV === 2600)).toMatchObject({ column: 3, row: 0 });
  });

  it("leaves out cells where no transfer exists", () => {
    const deltaV = plot().deltaV.map((value, index) => (index === 5 ? 0 : value));
    expect(porkchopCells(plot({ deltaV }))).toHaveLength(11);
  });

  it("shades the best window brightest and saturates at two and a half times its cost", () => {
    expect(shade(1500, 1500)).toBe(0);
    expect(shade(3750, 1500)).toBeCloseTo(1, 12);
    expect(shade(9000, 1500)).toBe(1);
    expect(shadeColor(0)).not.toBe(shadeColor(1));
  });

  it("marks the best window in the middle of its cell", () => {
    expect(bestPoint(plot())).toEqual({ x: 0.5, y: 2.5 });
    expect(bestPoint(plot({ bestDeparture: 125 * DAY, bestFlight: 250 * DAY }))).toEqual({ x: 3, y: 0.5 });
  });

  it("reads times in whole Kerbin days", () => {
    expect(days(236 * DAY + 100)).toBe("236D");
  });
});
