import { Vector3 } from "three";
import { describe, expect, it } from "vitest";

describe("display toolchain", () => {
  it("resolves three", () => {
    expect(new Vector3(3, 4, 0).length()).toBe(5);
  });
});
