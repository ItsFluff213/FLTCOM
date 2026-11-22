import { describe, expect, it } from "vitest";
import { rotateVector, telescopeEye } from "./telescope.ts";

const dot = (a: { x: number; y: number; z: number }, b: { x: number; y: number; z: number }) => a.x * b.x + a.y * b.y + a.z * b.z;

describe("telescope view", () => {
  const level = { x: 0, y: Math.sin(Math.PI / 4), z: 0, w: Math.cos(Math.PI / 4) };

  it("turns the craft's long axis by its attitude", () => {
    const nose = rotateVector(level, { x: 0, y: 0, z: 1 });
    expect(nose.x).toBeCloseTo(1, 9);
    expect(nose.z).toBeCloseTo(0, 9);
  });

  it("looks at the hull nearly side-on, whatever the attitude", () => {
    for (const attitude of [{ x: 0, y: 0, z: 0, w: 1 }, level, { x: 0.3, y: -0.2, z: 0.5, w: 0.79 }]) {
      const length = Math.hypot(attitude.x, attitude.y, attitude.z, attitude.w);
      const q = { x: attitude.x / length, y: attitude.y / length, z: attitude.z / length, w: attitude.w / length };
      const eye = telescopeEye(q, 0);
      expect(Math.hypot(eye.x, eye.y, eye.z)).toBeCloseTo(1, 9);
      expect(Math.abs(dot(eye, rotateVector(q, { x: 0, y: 0, z: 1 })))).toBeLessThan(0.4);
    }
  });

  it("drifts slowly: a second moves the eye a little, a minute moves it more", () => {
    const start = telescopeEye(level, 0);
    const second = telescopeEye(level, 1);
    const minute = telescopeEye(level, 60);
    expect(1 - dot(start, second)).toBeLessThan(1e-3);
    expect(1 - dot(start, minute)).toBeGreaterThan(1e-3);
  });
});
