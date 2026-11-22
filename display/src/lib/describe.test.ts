import { describe, expect, it } from "vitest";
import { commsLabel, whereabouts } from "./describe.ts";
import type { Body, Vessel } from "../protocol.gen.ts";

describe("whereabouts", () => {
  const kerbin = { name: "Kerbin", radius: 600000, gravParameter: 3.5316e12 } as Body;
  const vessel = {
    location: { kind: "orbit", situation: "subOrbital", orbit: { referenceBody: "Kerbin", semiMajorAxis: 700000, eccentricity: 0.1, inclination: 0.5, longitudeOfAscendingNode: 0, argumentOfPeriapsis: 0, meanAnomaly: 1 } },
  } as Vessel;

  it("writes apsis altitudes, inclination, period and spaced situations", () => {
    const at = whereabouts(vessel, new Map([["Kerbin", kerbin]]));
    expect(at.detailLabel).toBe("AP × PE");
    expect(at.detail).toBe("170.0 km × 30.0 km");
    expect(at.situation).toBe("SUB ORBITAL");
    expect(at.inclination).toBe("28.65°");
    expect(at.period).toBe("1958 s");
  });

  it("labels a landed or prelaunch vessel by its position, not its apsides", () => {
    const pad = { location: { kind: "surface", situation: "prelaunch", body: "Kerbin", latitude: -0.0017, longitude: -1.2959 } } as Vessel;
    const at = whereabouts(pad, new Map([["Kerbin", kerbin]]));
    expect(at.detailLabel).toBe("POSITION");
    expect(at.detail).toBe("0.10°S 74.25°W");
    expect(at.situation).toBe("PRELAUNCH");
  });
});

describe("commsLabel", () => {
  const kerbal = { name: "Jebediah Kerman" } as Vessel["crew"][number];

  it("says a crewed vessel out of contact is still flown", () => {
    expect(commsLabel({ comms: "los", crew: [kerbal] })).toBe("LOS · CREW FLYING");
    expect(commsLabel({ comms: "los", crew: [] })).toBe("LOS");
    expect(commsLabel({ comms: "link", crew: [kerbal] })).toBe("LINK");
  });
});
