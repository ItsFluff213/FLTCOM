import { describe, expect, it } from "vitest";
import { duration, kerbinDate } from "./kerbinTime.ts";

describe("kerbin time", () => {
  it("starts at year 1 day 1", () => {
    expect(kerbinDate(0)).toBe("Y1 D1 00:00:00");
  });

  it("rolls hours, days and years on Kerbin's 6 hour day and 426 day year", () => {
    expect(kerbinDate(3600 * 5 + 61)).toBe("Y1 D1 05:01:01");
    expect(kerbinDate(21600 * 12)).toBe("Y1 D13 00:00:00");
    expect(kerbinDate(21600 * 426)).toBe("Y2 D1 00:00:00");
  });

  it("writes durations compactly", () => {
    expect(duration(95)).toBe("1m 35s");
    expect(duration(7325)).toBe("2h 02m 05s");
    expect(duration(21600 * 3 + 7200)).toBe("3d 2h 0m");
  });
});
