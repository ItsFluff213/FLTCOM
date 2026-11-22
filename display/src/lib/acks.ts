import type { Missions, OperationStatus } from "../protocol.gen.ts";

/** A command the display sent and waits to see answered. brief is answered by its briefing; the rest by the next missions. */
export type CommandKind = "brief" | "approve" | "scrub" | "resume";

export type AckStatus = "pending" | "ok" | "refused";

export interface Tracked {
  kind: CommandKind;
  /** The operation it is about; for brief, the composer. */
  target: string;
  sentAt: number;
  status: AckStatus;
  reason: string | null;
  settledAt: number | null;
}

/** KSP answers an accepted command in its next missions; silence this long means it refused. Unit: ms. */
export const ACK_TIMEOUT_MS = 4000;
const SHOW_OK_MS = 1500;
const SHOW_REFUSED_MS = 8000;

export const NO_LINK = "NO LINK TO KSP · NOTHING SENT";
const REFUSED: Record<Exclude<CommandKind, "brief">, string> = {
  approve: "KSP DID NOT TAKE THE APPROVAL · THE GAME SCREEN SAYS WHY",
  scrub: "KSP DID NOT SCRUB IT · THE GAME SCREEN SAYS WHY",
  resume: "KSP DID NOT RESUME IT · THE GAME SCREEN SAYS WHY",
};

const ENDED = ["done", "missed", "scrubbed", "aborted"];

function answered(kind: CommandKind, operation: OperationStatus | undefined): boolean {
  switch (kind) {
    case "approve":
      return operation !== undefined && operation.state !== "draft" && operation.state !== "briefed";
    case "scrub":
      return operation === undefined || ENDED.includes(operation.state);
    case "resume":
      return operation !== undefined && operation.state !== "hold";
    case "brief":
      return false;
  }
}

/** Starts tracking a command; one the socket would not take is refused at once. Replaces an earlier command of the same kind and target. */
export function track(list: readonly Tracked[], kind: CommandKind, target: string, now: number, delivered: boolean): Tracked[] {
  const entry: Tracked = delivered
    ? { kind, target, sentAt: now, status: "pending", reason: null, settledAt: null }
    : { kind, target, sentAt: now, status: "refused", reason: NO_LINK, settledAt: now };
  return [...list.filter((other) => other.kind !== kind || other.target !== target), entry];
}

/** Settles a command from an explicit answer, such as a briefing with or without an error. */
export function resolve(list: readonly Tracked[], kind: CommandKind, target: string, reason: string | null, now: number): Tracked[] {
  return list.map((entry) =>
    entry.kind === kind && entry.target === target && entry.status === "pending" ? { ...entry, status: reason === null ? "ok" : "refused", reason, settledAt: now } : entry,
  );
}

/** Settles pending commands from the latest missions and drops answers shown long enough. Returns the same list when nothing changed. */
export function settle(list: readonly Tracked[], missions: Missions | null, now: number): readonly Tracked[] {
  let changed = false;
  const next: Tracked[] = [];
  for (const entry of list) {
    if (entry.status === "pending") {
      const operation = missions?.operations.find((candidate) => candidate.id === entry.target);
      if (entry.kind !== "brief" && answered(entry.kind, operation)) {
        next.push({ ...entry, status: "ok", settledAt: now });
        changed = true;
      } else if (entry.kind !== "brief" && now - entry.sentAt >= ACK_TIMEOUT_MS) {
        next.push({ ...entry, status: "refused", reason: REFUSED[entry.kind], settledAt: now });
        changed = true;
      } else {
        next.push(entry);
      }
      continue;
    }
    if (now - entry.settledAt! >= (entry.status === "ok" ? SHOW_OK_MS : SHOW_REFUSED_MS)) {
      changed = true;
      continue;
    }
    next.push(entry);
  }
  return changed ? next : list;
}

export function ackOf(list: readonly Tracked[], kind: CommandKind, target: string | null): Tracked | null {
  return list.find((entry) => entry.kind === kind && entry.target === target) ?? null;
}
