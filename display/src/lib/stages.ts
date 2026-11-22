import type { CarrierReturnKind, Deployment } from "../protocol.gen.ts";

export interface StageRow {
  readonly stage: "launch" | "transfer" | "satellites";
  readonly name: string;
  readonly end: string;
  readonly deltaV: string;
  readonly amber: boolean;
}

const ENDS: Record<CarrierReturnKind, string> = {
  land: "LAND AT HOME",
  transferAndLand: "TRANSFER HOME, THEN LAND",
  disposal: "PARK",
  stay: "STAYS ON THE RING",
  burnUp: "BURN UP",
};

const share = (needs: number, has: number) => (needs > 0 ? `${Math.round(needs)} OF ${Math.round(has)} m/s` : "—");

/** One row per part of the stack: the launch stage, the transfer stage that carries the satellites, and the satellites. */
export function stageRows(deployment: Deployment): StageRow[] {
  const launch = deployment.launchStage;
  const back = deployment.returnPlan;
  const n = deployment.payloads.length;
  return [
    {
      stage: "launch",
      name: "LAUNCH STAGE",
      end: `${launch.comesHome ? "RETURNS HOME" : "STAYS"}: ${launch.text.toUpperCase()}`,
      deltaV: share(launch.deltaV, launch.available),
      amber: launch.name !== null && !launch.comesHome,
    },
    {
      stage: "transfer",
      name: "TRANSFER STAGE",
      end: `${ENDS[back.kind]}: ${back.text.toUpperCase()}`,
      deltaV: share(back.deltaV, back.available),
      amber: back.fallback,
    },
    {
      stage: "satellites",
      name: `${n} SATELLITE${n === 1 ? "" : "S"}`,
      end: "CIRCULARIZE ON THE RING",
      deltaV: `${Math.round(deployment.payloads.reduce((sum, payload) => sum + payload.circularizeDeltaV, 0))} m/s`,
      amber: deployment.payloads.some((payload) => payload.deltaV < payload.circularizeDeltaV),
    },
  ];
}
