import { describe, expect, it } from "vitest";
import type { BurnPlan, OperationStatus } from "../protocol.gen.ts";
import { descentView } from "./descent.ts";

const DEG = Math.PI / 180;
const burn = (label: string, startUt: number): BurnPlan => ({ vesselId: 1, label, nodeUt: startUt, startUt, duration: 300, prograde: -20, normal: 0, radial: 0, deltaV: 20, orbitAfter: null });
const landing = (patch: Partial<OperationStatus> = {}): OperationStatus => ({
  id: "op-1",
  codename: "IRON LANTERN",
  change: { kind: "land", altitude: null, periapsis: null, inclination: null, lan: null, body: "Mun", targetVesselId: null, targetPortId: null, landing: { site: "custom", latitude: 6.33 * DEG, longitude: -78.17 * DEG }, autoSystems: null, endOfLife: null },
  description: "land",
  state: "executing",
  burns: [burn("deorbit", 100), burn("descent", 1000)],
  burnIndex: 1,
  deltaVDone: 0,
  note: null,
  landingPrediction: { latitude: 6.331 * DEG, longitude: -78.17 * DEG, ut: 1400 },
  alarm: null,
  descent: { phase: "brake", altitude: 3200, verticalSpeed: -61.4, miss: 140 },
  ...patch,
});

describe("descentView", () => {
  it("shows the live phase, height, sink rate, time to touchdown and the miss beside the target", () => {
    expect(descentView(landing(), 1100)).toEqual({
      phase: "BRAKING BURN",
      released: null,
      altitude: "3.2 km",
      verticalSpeed: "-61.4 m/s",
      touchdownIn: 300,
      target: "6.33 N 78.17 W",
      predicted: "6.33 N 78.17 W",
      miss: "140 m",
    });
  });

  it("names the deorbit burn before the descent has a reading", () => {
    expect(descentView(landing({ burnIndex: 0, descent: null }), 150)?.phase).toBe("DEORBIT BURN");
    expect(descentView(landing({ burnIndex: 0, descent: null, state: "waiting" }), 50)).toBeNull();
  });

  it("makes a released landing unmistakable", () => {
    const view = descentView(landing({ state: "hold", alarm: { kind: "impact", body: "Mun", text: "released on impact course with Mun" } }), 1100);
    expect(view?.phase).toBe("RELEASED");
    expect(view?.released).toBe("released on impact course with Mun");
  });

  it("stays out of every other operation", () => {
    const other = landing();
    expect(descentView({ ...other, change: { ...other.change, kind: "circularize", landing: null } }, 1100)).toBeNull();
  });
});
