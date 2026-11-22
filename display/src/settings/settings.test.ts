import { describe, expect, it } from "vitest";
import { DEFAULT_SETTINGS, parseSettings } from "./settings.svelte.ts";

describe("parseSettings", () => {
  it("reads back what was saved", () => {
    expect(parseSettings(JSON.stringify({ clean: true, intensity: 0.25, muted: true }))).toEqual({ clean: true, intensity: 0.25, muted: true });
  });

  it("falls back to the defaults for missing, broken or out-of-range values", () => {
    expect(parseSettings(null)).toEqual(DEFAULT_SETTINGS);
    expect(parseSettings("{not json")).toEqual(DEFAULT_SETTINGS);
    expect(parseSettings(JSON.stringify({ clean: "yes", intensity: 7 }))).toEqual(DEFAULT_SETTINGS);
  });
});
