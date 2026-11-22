import { describe, expect, it } from "vitest";
import type { Deployment } from "../protocol.gen.ts";
import { stageRows } from "./stages.ts";

const deployment = (patch: Partial<Deployment> = {}): Deployment => ({
  altitude: 2_863_334,
  synchronousAltitude: 2_863_334,
  suggestedAltitude: null,
  method: "resonant",
  workingOrbit: null,
  payloads: [1, 2, 3].map((k) => ({ id: 10 * k + 1, name: `Relay Carrier Sat ${k}`, mass: 270, deltaV: 300, circularizeDeltaV: 120 })),
  releases: [],
  returnPlan: { kind: "burnUp", deltaV: 412.4, available: 1500, text: "periapsis to 31.5 km, about 412 m/s, then it burns up in the atmosphere", fallback: false },
  launchStage: { comesHome: true, name: "Relay Carrier Launch Stage", deltaV: 52.2, available: 2894, text: "splits off in the parking orbit, deorbits about 52 m/s once the satellites are out, then lands on parachutes at the launch pad" },
  ...patch,
});

describe("stage rows", () => {
  it("lists the launch stage, the transfer stage and the satellites with their delta-v shares", () => {
    expect(stageRows(deployment())).toEqual([
      { stage: "launch", name: "LAUNCH STAGE", end: expect.stringMatching(/^RETURNS HOME: SPLITS OFF/), deltaV: "52 OF 2894 m/s", amber: false },
      { stage: "transfer", name: "TRANSFER STAGE", end: expect.stringMatching(/^BURN UP: PERIAPSIS TO 31.5 KM/), deltaV: "412 OF 1500 m/s", amber: false },
      { stage: "satellites", name: "3 SATELLITES", end: "CIRCULARIZE ON THE RING", deltaV: "360 m/s", amber: false },
    ]);
  });

  it("says why a launch stage stays and warns only when one was found", () => {
    const stays = stageRows(deployment({ launchStage: { comesHome: false, name: "Relay Carrier Launch Stage", deltaV: 52, available: 10, text: "stays attached: it has 10 m/s of the 57 m/s its deorbit needs" } }))[0];
    const none = stageRows(deployment({ launchStage: { comesHome: false, name: null, deltaV: 0, available: 0, text: "a launch stage comes home only with its own probe core or crewed pod, engines and parachutes below a decoupler" } }))[0];

    expect(stays).toMatchObject({ end: "STAYS: STAYS ATTACHED: IT HAS 10 M/S OF THE 57 M/S ITS DEORBIT NEEDS", amber: true });
    expect(none).toMatchObject({ end: expect.stringMatching(/^STAYS: A LAUNCH STAGE COMES HOME ONLY WITH ITS OWN PROBE CORE OR CREWED POD/), deltaV: "—", amber: false });
  });

  it("marks a transfer stage that had to park instead", () => {
    const parked = stageRows(deployment({ returnPlan: { kind: "disposal", deltaV: 80, available: 400, text: "Mun has no atmosphere to burn up in: disposal orbit at 400 km instead, clear of the constellation", fallback: true } }))[1];

    expect(parked).toMatchObject({ end: expect.stringMatching(/^PARK: MUN HAS NO ATMOSPHERE/), amber: true });
  });
});
