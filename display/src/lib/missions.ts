import type { Body, BriefingMessage, BurnPlan, FlightAlarm, Missions, OperationState, OperationStatus } from "../protocol.gen.ts";
import type { Cue } from "./sound.ts";

export const LIVE_STATES: readonly OperationState[] = ["approved", "uploaded", "waiting", "executing"];

export type StepStatus = "done" | "current" | "conflict" | "pending" | "missed" | "hold" | "void";

export interface Step {
  burn: BurnPlan;
  offset: string;
  status: StepStatus;
}

/** Where the burn the countdown watches stands. */
export type BurnPhase = "coasting" | "aligning" | "burning" | "replanned" | "held";

export const PHASE_LABEL: Record<BurnPhase, string> = { coasting: "COASTING", aligning: "ALIGNING", burning: "BURNING", replanned: "RE-PLANNED", held: "HELD" };

/** MissionRunner.AlignLead: the vessel turns to the burn this long before ignition. Unit: s. */
export const ALIGN_LEAD = 60;

export interface NextBurn {
  ut: number;
  deltaV: number;
  deltaVDone: number;
  vessel: string;
  label: string;
  phase: BurnPhase;
}

const pad = (value: number) => String(value).padStart(2, "0");

export function offsetText(seconds: number): string {
  const total = Math.round(Math.abs(seconds));
  return `T${seconds < 0 ? "-" : "+"}${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
}

export function approveBlocked(briefing: BriefingMessage): boolean {
  return (
    briefing.operationId === null ||
    !briefing.canApprove ||
    briefing.contingencies.some((note) => note.severity === "red") ||
    briefing.stages.some((line) => line.insufficient)
  );
}

function statusAt(operation: OperationStatus, index: number): StepStatus {
  switch (operation.state) {
    case "done":
      return "done";
    case "scrubbed":
    case "aborted":
      return "void";
    default:
      if (index < operation.burnIndex) {
        return "done";
      }
      if (index > operation.burnIndex) {
        return "pending";
      }
      return operation.state === "missed" ? "missed" : operation.state === "hold" ? "hold" : operation.state === "executing" ? "current" : "pending";
  }
}

export function steps(burns: readonly BurnPlan[], ut: number, operation: OperationStatus | null, conflicts: ReadonlySet<number> = new Set()): Step[] {
  return burns.map((burn, index) => {
    const status = operation ? statusAt(operation, index) : "pending";
    return { burn, offset: offsetText(burn.startUt - ut), status: status === "pending" && conflicts.has(index) ? "conflict" : status };
  });
}

export function conflictsOf(operation: OperationStatus, others: readonly OperationStatus[]): Set<number> {
  const found = new Set<number>();
  operation.burns.forEach((burn, index) => {
    if (index < operation.burnIndex || !LIVE_STATES.includes(operation.state)) {
      return;
    }
    const clash = others.some(
      (other) =>
        other.id !== operation.id &&
        LIVE_STATES.includes(other.state) &&
        other.burns.some((theirs, theirIndex) => theirIndex >= other.burnIndex && theirs.vesselId !== burn.vesselId && theirs.startUt < burn.startUt + burn.duration && burn.startUt < theirs.startUt + theirs.duration),
    );
    if (clash) {
      found.add(index);
    }
  });
  return found;
}

export function burnPhase(operation: OperationStatus, ut: number): BurnPhase | null {
  if (!operation.burns[operation.burnIndex]) {
    return null;
  }
  if (operation.state === "hold") {
    return "held";
  }
  if (!LIVE_STATES.includes(operation.state)) {
    return null;
  }
  const lead = operation.burns[operation.burnIndex]!.startUt - ut;
  if (operation.state === "executing" && lead <= 0) {
    return "burning";
  }
  if (lead <= ALIGN_LEAD) {
    return "aligning";
  }
  return operation.note !== null && /re-?plan/i.test(operation.note) ? "replanned" : "coasting";
}

const rank = (phase: BurnPhase) => (phase === "burning" ? 0 : phase === "held" ? 2 : 1);

/** The burn to watch: one flying now, else the earliest coming up, else one in Hold. */
export function nextBurn(missions: Missions | null, vesselName: (id: number) => string): NextBurn | null {
  let best: NextBurn | null = null;
  for (const operation of missions?.operations ?? []) {
    const phase = burnPhase(operation, missions!.ut);
    const burn = operation.burns[operation.burnIndex];
    if (phase && burn && (best === null || rank(phase) < rank(best.phase) || (rank(phase) === rank(best.phase) && burn.startUt < best.ut))) {
      best = { ut: burn.startUt, deltaV: burn.deltaV, deltaVDone: operation.deltaVDone, vessel: vesselName(burn.vesselId), label: burn.label, phase };
    }
  }
  return best;
}

export function missionCues(prev: Missions | null, next: Missions): Cue[] {
  const cues: Cue[] = [];
  const before = new Map(prev?.operations.map((operation) => [operation.id, operation]));
  for (const operation of next.operations) {
    const old = before.get(operation.id);
    if (!old) {
      continue;
    }
    if (operation.state === "executing" && old.state !== "executing") {
      cues.push("burnStart");
    } else if (old.state === "executing" && (operation.state !== "executing" || operation.burnIndex !== old.burnIndex)) {
      cues.push(operation.state === "aborted" || operation.state === "missed" ? "alert" : "burnEnd");
    } else if (operation.state !== old.state && ["missed", "hold", "aborted"].includes(operation.state)) {
      cues.push("alert");
    } else if (operation.alarm && operation.alarm.text !== old.alarm?.text) {
      cues.push("alert");
    }
  }
  const known = new Set(prev?.prompts.map((prompt) => prompt.id));
  if (prev && next.prompts.some((prompt) => !known.has(prompt.id))) {
    cues.push("alert");
  }
  return cues;
}

export interface FinalOrbit {
  body: string;
  periapsis: number;
  apoapsis: number | null;
  floor: number;
  low: boolean;
}

/** The lowest safe altitude over a body: the top of its atmosphere or its highest terrain. */
export function safeFloor(body: Body): number {
  return Math.max(body.atmosphereHeight, body.highestTerrainAltitude);
}

/** Where the plan leaves the vessel: the last planned orbit, in altitudes, against the body's safe floor. */
export function finalOrbit(briefing: Pick<BriefingMessage, "burns" | "change">, bodies: ReadonlyMap<string, Body>): FinalOrbit | null {
  const orbit = briefing.change.kind === "land" ? null : ([...briefing.burns].reverse().find((burn) => burn.orbitAfter)?.orbitAfter ?? null);
  const body = orbit ? bodies.get(orbit.referenceBody) : undefined;
  if (!orbit || !body) {
    return null;
  }
  const floor = safeFloor(body);
  const periapsis = orbit.semiMajorAxis * (1 - orbit.eccentricity) - body.radius;
  const apoapsis = orbit.eccentricity < 1 ? orbit.semiMajorAxis * (1 + orbit.eccentricity) - body.radius : null;
  return { body: body.name, periapsis, apoapsis, floor, low: periapsis < floor };
}

export interface Alarmed {
  operationId: string;
  vesselId: number;
  vesselName: string;
  alarm: FlightAlarm;
}

/** The first live operation with a red alert, and the vessel it is about. */
export function alarmed(missions: Missions | null, vesselName: (id: number) => string): Alarmed | null {
  const operation = missions?.operations.find((candidate) => candidate.alarm && !["done", "scrubbed", "aborted"].includes(candidate.state));
  const burn = operation?.burns[operation.burnIndex] ?? operation?.burns[0];
  return operation?.alarm && burn ? { operationId: operation.id, vesselId: burn.vesselId, vesselName: vesselName(burn.vesselId), alarm: operation.alarm } : null;
}

export function countdownTick(previous: number | null, remaining: number): boolean {
  return previous !== null && remaining > 0 && remaining <= 10 && Math.ceil(previous) !== Math.ceil(remaining);
}

export function marginText(briefing: Pick<BriefingMessage, "margin" | "availableDeltaV" | "requiredDeltaV">): string {
  if (briefing.margin === null) {
    return "—";
  }
  const spare = Math.round(briefing.availableDeltaV - briefing.requiredDeltaV);
  const percent = briefing.margin > 9.99 ? ">999%" : `${Math.round(briefing.margin * 100)}%`;
  return `${spare >= 0 ? "+" : "-"}${Math.abs(spare)} m/s (${percent})`;
}
