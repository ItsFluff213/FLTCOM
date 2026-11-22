import type { PorkchopPlot } from "../protocol.gen.ts";

/** A Kerbin day, six hours. */
export const DAY = 21600;

export interface PorkchopCell {
  column: number;
  /** Counted from the top of the plot: the longest flight is row 0. */
  row: number;
  fill: string;
  deltaV: number;
}

/** How costly a cell is against the best window: 0 at the best window, 1 at 2.5 times its delta-v and beyond. */
export function shade(deltaV: number, best: number): number {
  return Math.min(1, Math.max(0, Math.log(deltaV / best) / Math.log(2.5)));
}

/** Cheap windows bright amber, costly ones fading into the background. */
export function shadeColor(t: number): string {
  return `hsl(${Math.round(48 + 172 * t)} ${Math.round(90 - 50 * t)}% ${Math.round(60 - 46 * t)}%)`;
}

/** Every cell with a transfer, flight time growing upward; cells where no transfer exists (deltaV 0) are left out. */
export function porkchopCells(plot: PorkchopPlot): PorkchopCell[] {
  const out: PorkchopCell[] = [];
  for (let row = 0; row < plot.rows; row++) {
    for (let column = 0; column < plot.columns; column++) {
      const deltaV = plot.deltaV[row * plot.columns + column] ?? 0;
      if (deltaV > 0 && Number.isFinite(deltaV)) {
        out.push({ column, row: plot.rows - 1 - row, fill: shadeColor(shade(deltaV, plot.bestDeltaV)), deltaV });
      }
    }
  }
  return out;
}

/** Where the best window sits, in cell units from the plot's top left corner (cell centres are at half units). */
export function bestPoint(plot: PorkchopPlot): { x: number; y: number } {
  const x = (plot.bestDeparture - plot.departureStart) / plot.departureStep + 0.5;
  const y = plot.rows - 0.5 - (plot.bestFlight - plot.flightStart) / plot.flightStep;
  return { x, y };
}

/** Whole days, as the axis and the window read them. */
export function days(seconds: number): string {
  return `${Math.round(seconds / DAY)}D`;
}
