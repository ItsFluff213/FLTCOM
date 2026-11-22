import { describe, expect, it } from "vitest";
import { PROTOCOL_VERSION, type Missions, type OperationState, type OperationStatus } from "../protocol.gen.ts";
import { ACK_TIMEOUT_MS, ackOf, NO_LINK, resolve, settle, track } from "./acks.ts";

const op = (id: string, state: OperationState): OperationStatus => ({
  id,
  codename: "IRON LANTERN",
  change: { kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null },
  description: "circularize",
  state,
  burns: [],
  burnIndex: 0,
  deltaVDone: 0,
  note: null,
  landingPrediction: null,
  alarm: null,
  descent: null,
});
const missions = (...operations: OperationStatus[]): Missions => ({ protocolVersion: PROTOCOL_VERSION, type: "missions", ut: 0, operations, prompts: [] });

describe("command acknowledgement", () => {
  it("confirms an approval once the operation shows up live", () => {
    const sent = track([], "approve", "op-1", 0, true);
    expect(ackOf(settle(sent, missions(), 500), "approve", "op-1")?.status).toBe("pending");
    expect(ackOf(settle(sent, missions(op("op-1", "waiting")), 600), "approve", "op-1")?.status).toBe("ok");
  });

  it("refuses an approval KSP never answers, and says where the reason is", () => {
    const sent = track([], "approve", "op-1", 0, true);
    const refused = ackOf(settle(sent, missions(), ACK_TIMEOUT_MS), "approve", "op-1");
    expect(refused?.status).toBe("refused");
    expect(refused?.reason).toMatch(/GAME SCREEN/);
  });

  it("refuses at once what the socket would not take", () => {
    expect(ackOf(track([], "scrub", "op-1", 0, false), "scrub", "op-1")).toMatchObject({ status: "refused", reason: NO_LINK });
  });

  it("confirms a scrub when the operation ends or is gone, and a resume when it leaves hold", () => {
    expect(ackOf(settle(track([], "scrub", "op-1", 0, true), missions(op("op-1", "aborted")), 100), "scrub", "op-1")?.status).toBe("ok");
    expect(ackOf(settle(track([], "scrub", "op-1", 0, true), missions(), 100), "scrub", "op-1")?.status).toBe("ok");
    expect(ackOf(settle(track([], "resume", "op-1", 0, true), missions(op("op-1", "hold")), 100), "resume", "op-1")?.status).toBe("pending");
    expect(ackOf(settle(track([], "resume", "op-1", 0, true), missions(op("op-1", "waiting")), 100), "resume", "op-1")?.status).toBe("ok");
  });

  it("settles a brief only from its briefing, never by timeout", () => {
    const sent = track([], "brief", "composer", 0, true);
    expect(ackOf(settle(sent, missions(), ACK_TIMEOUT_MS * 3), "brief", "composer")?.status).toBe("pending");
    expect(ackOf(resolve(sent, "brief", "composer", "vessel is on the ground", 10), "brief", "composer")).toMatchObject({ status: "refused", reason: "vessel is on the ground" });
    expect(ackOf(resolve(sent, "brief", "composer", null, 10), "brief", "composer")?.status).toBe("ok");
  });

  it("drops an answer after showing it, and keeps the same list while nothing changes", () => {
    const ok = settle(track([], "approve", "op-1", 0, true), missions(op("op-1", "waiting")), 100);
    expect(settle(ok, null, 200)).toBe(ok);
    expect(settle(ok, null, 100_000)).toEqual([]);
  });

  it("replaces an earlier command for the same control", () => {
    const twice = track(track([], "approve", "op-1", 0, false), "approve", "op-1", 50, true);
    expect(twice).toHaveLength(1);
    expect(twice[0]!.status).toBe("pending");
  });
});
