import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parseServerMessage, type CraftLayout } from "../protocol.gen.ts";
import { layoutBlueprint } from "./blueprint.ts";

const layouts: CraftLayout[] = JSON.parse(readFileSync(new URL("../../../tests/fixtures/telemetry/kerbin-small.layouts.json", import.meta.url), "utf8"));

describe("layoutBlueprint", () => {
  it("accepts the fixture layouts as protocol messages", () => {
    for (const layout of layouts) {
      expect(parseServerMessage(JSON.stringify(layout)).ok).toBe(true);
    }
  });

  it("draws a two-stage craft as two bands, first stage to fire first, the pod's stage on top", () => {
    const craft = layouts.find((layout) => layout.vesselId === 1105)!;
    const blueprint = layoutBlueprint(craft.parts, [
      { stage: 1, deltaV: 2900, twr: 1.45, burnTime: 187 },
      { stage: 0, deltaV: 1920, twr: 0.92, burnTime: 164 },
    ])!;
    expect(blueprint.stages.map((band) => band.stage)).toEqual([2, 1, 0]);
    expect(blueprint.stages.find((band) => band.stage === 1)?.stats?.deltaV).toBe(2900);
    expect(blueprint.stages.find((band) => band.stage === 2)?.stats).toBeNull();
    const [pod, , lower] = blueprint.stages;
    expect(pod!.bottom).toBeGreaterThan(lower!.top);
  });

  it("turns a part by its rotation", () => {
    const quarterTurn = { x: 0, y: 0, z: Math.SQRT1_2, w: Math.SQRT1_2 };
    const blueprint = layoutBlueprint(
      [{ id: 1, name: "tank", position: { x: 0, y: 0, z: 0 }, rotation: quarterTurn, size: { x: 1, y: 4, z: 1 }, category: "tank", stage: 0 }],
      [],
    )!;
    expect(blueprint.bounds.right - blueprint.bounds.left).toBeCloseTo(4);
    expect(blueprint.bounds.top - blueprint.bounds.bottom).toBeCloseTo(1);
  });

  it("returns nothing for a craft without parts", () => {
    expect(layoutBlueprint([], [])).toBeNull();
  });
});
