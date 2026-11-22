import { describe, expect, it } from "vitest";
import { bracketHalf, markerPx, orbitStyle } from "./marks.ts";

describe("map marks", () => {
  it("draws a ship at least 3.5% of the map height, the selected one at least 4.5%", () => {
    expect(markerPx({ kind: "vessel", selected: false, scale: 1 }, 1000)).toBeGreaterThanOrEqual(35);
    expect(markerPx({ kind: "vessel", selected: true, scale: 1 }, 1000)).toBeGreaterThanOrEqual(45);
  });

  it("draws a rock at its scale and keeps the bracket around it", () => {
    const boulder = { kind: "vessel" as const, selected: false, scale: 1.65 };
    expect(markerPx(boulder, 1000)).toBeCloseTo(1.65 * markerPx({ kind: "vessel", selected: false, scale: 1 }, 1000));
    expect(bracketHalf(boulder, 1000) * 2).toBeGreaterThan(markerPx(boulder, 1000));
  });

  it("puts the bracket outside the ship it frames", () => {
    for (const selected of [false, true]) {
      const ship = { kind: "vessel" as const, selected, scale: 1 };
      expect(bracketHalf(ship, 800) * 2).toBeGreaterThan(markerPx(ship, 800));
    }
  });

  it("draws orbits wider than one pixel, the selected one widest, plans dashed", () => {
    const fleet = orbitStyle({ id: "vessel:1", role: "fleet" });
    const selected = orbitStyle({ id: "vessel:2", role: "selected" });
    const body = orbitStyle({ id: "body:Mun", role: "body" });
    expect(body.width).toBeGreaterThan(1);
    expect(fleet.width).toBeGreaterThan(body.width);
    expect(selected.width).toBeGreaterThan(fleet.width);
    expect(fleet.dashed).toBe(false);
    expect(orbitStyle({ id: "vessel:3", role: "planned" }).dashed).toBe(true);
    expect(orbitStyle({ id: "after:3:100", role: "alert" }).dashed).toBe(true);
  });
});
