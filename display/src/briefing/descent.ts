import type { DescentPhase, OperationStatus } from "../protocol.gen.ts";
import { formatSpot } from "./surfaceMap.ts";

const PHASE: Record<DescentPhase, string> = { correct: "COURSE CORRECTION", coast: "COAST", brake: "BRAKING BURN", final: "FINAL DESCENT", landed: "TOUCHDOWN" };
const DEG = 180 / Math.PI;

export interface DescentView {
  phase: string;
  released: string | null;
  altitude: string | null;
  verticalSpeed: string | null;
  touchdownIn: number | null;
  target: string;
  predicted: string | null;
  miss: string | null;
}

/** What a landing's card shows while it flies, or null for any other operation and a landing not yet under way. */
export function descentView(operation: OperationStatus, ut: number): DescentView | null {
  const landing = operation.change.landing;
  const burn = operation.burns[operation.burnIndex];
  if (operation.change.kind !== "land" || !landing || !burn) {
    return null;
  }
  const released = operation.state === "hold" && operation.alarm ? operation.alarm.text : null;
  const deorbit = !operation.descent && burn.label.toLowerCase() === "deorbit";
  if (!released && !operation.descent && !(deorbit && operation.state === "executing")) {
    return null;
  }
  const descent = operation.descent;
  const prediction = operation.landingPrediction;
  return {
    phase: released ? "RELEASED" : descent ? PHASE[descent.phase] : "DEORBIT BURN",
    released,
    altitude: descent ? `${(descent.altitude / 1000).toFixed(descent.altitude < 1000 ? 2 : 1)} km` : null,
    verticalSpeed: descent ? `${descent.verticalSpeed >= 0 ? "+" : ""}${descent.verticalSpeed.toFixed(1)} m/s` : null,
    touchdownIn: prediction && descent?.phase !== "landed" ? Math.max(0, prediction.ut - ut) : null,
    target: formatSpot(landing.latitude * DEG, landing.longitude * DEG),
    predicted: prediction ? formatSpot(prediction.latitude * DEG, prediction.longitude * DEG) : null,
    miss: descent?.miss != null ? (descent.miss < 1000 ? `${Math.round(descent.miss)} m` : `${(descent.miss / 1000).toFixed(1)} km`) : null,
  };
}
