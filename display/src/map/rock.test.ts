import { describe, expect, it } from "vitest";
import { rockShape, tumbleOf } from "./rock.ts";

const radii = (positions: number[]) => Array.from({ length: positions.length / 3 }, (_, i) => Math.hypot(positions[i * 3]!, positions[i * 3 + 1]!, positions[i * 3 + 2]!));

describe("rockShape", () => {
  it("gives the same rock for the same seed", () => {
    expect(rockShape("vessel:501")).toEqual(rockShape("vessel:501"));
    expect(tumbleOf("vessel:501")).toEqual(tumbleOf("vessel:501"));
  });

  it("gives a different rock for a different seed", () => {
    const a = rockShape("vessel:501").positions;
    const b = rockShape("vessel:502").positions;
    expect(a).toHaveLength(b.length);
    expect(Math.max(...a.map((value, i) => Math.abs(value - b[i]!)))).toBeGreaterThan(0.05);
    expect(tumbleOf("vessel:501")).not.toEqual(tumbleOf("vessel:502"));
  });

  it("is a closed, lumpy mesh no larger than unit radius", () => {
    const { positions, indices } = rockShape("Ast. XSZ-308");
    const r = radii(positions);
    expect(Math.max(...r)).toBeCloseTo(1, 6);
    expect(Math.min(...r)).toBeGreaterThan(0.25);
    expect(Math.max(...r) / Math.min(...r)).toBeGreaterThan(1.3);
    expect(indices).toHaveLength(320 * 3);
    expect(indices.every((index) => index >= 0 && index < positions.length / 3)).toBe(true);
  });

  it("tumbles slowly", () => {
    const { axis, speed } = tumbleOf("vessel:9");
    expect(Math.hypot(axis.x, axis.y, axis.z)).toBeCloseTo(1, 9);
    expect(speed).toBeGreaterThan(0);
    expect(speed).toBeLessThan(0.5);
  });
});
