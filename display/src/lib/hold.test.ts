import { describe, expect, it } from "vitest";
import { Hold, HOLD_MS } from "./hold.ts";

describe("hold to confirm", () => {
  it("fills over the hold time and confirms exactly once", () => {
    const hold = new Hold();
    hold.press(1000);
    expect(hold.tick(1000 + HOLD_MS / 2)).toEqual({ progress: 0.5, confirm: false });
    expect(hold.tick(1000 + HOLD_MS)).toEqual({ progress: 1, confirm: true });
    expect(hold.tick(1000 + HOLD_MS * 2)).toEqual({ progress: 1, confirm: false });
  });

  it("confirms nothing when let go early, and starts over on the next press", () => {
    const hold = new Hold(600);
    hold.press(0);
    expect(hold.tick(590).confirm).toBe(false);
    hold.release();
    expect(hold.tick(700)).toEqual({ progress: 0, confirm: false });
    hold.press(800);
    expect(hold.tick(1100)).toEqual({ progress: 0.5, confirm: false });
    expect(hold.tick(1400).confirm).toBe(true);
  });

  it("ignores a second press while held, as a repeating key would send", () => {
    const hold = new Hold(600);
    hold.press(0);
    hold.press(500);
    expect(hold.tick(600).confirm).toBe(true);
  });

  it("confirms again after a release and a full new hold", () => {
    const hold = new Hold(600);
    hold.press(0);
    expect(hold.tick(600).confirm).toBe(true);
    hold.release();
    hold.press(1000);
    expect(hold.tick(1600).confirm).toBe(true);
  });
});
