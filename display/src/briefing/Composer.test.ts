import { describe, expect, it } from "vitest";
import { END_OF_LIFE, KINDS, RING_PRESETS, freePortsOf, synchronousKm, toDeployRequest, toLandRequest, toLaunchRequest, toMeetRequest, toRequest } from "./Composer.svelte";

describe("deploy constellation", () => {
  it("is offered for one vessel and sends its ring altitude in metres", () => {
    expect(KINDS.find((entry) => entry.kind === "deployConstellation")).toMatchObject({ needs: "altitude", vesselOnly: true });
    expect(toRequest("deployConstellation", 2863, null)).toMatchObject({ kind: "deployConstellation", altitude: 2_863_000, inclination: null, body: null });
  });

  it("defaults to the synchronous altitude: 2,863 km at Kerbin, none for a body that does not turn", () => {
    expect(synchronousKm({ gravParameter: 3.5316e12, radius: 600_000, rotationPeriod: 21_549.425 })).toBe(2863);
    expect(synchronousKm({ gravParameter: 1, radius: 1, rotationPeriod: 0 })).toBeNull();
  });

  it("sends no plane for the equatorial default, so the plugin plans as it always has", () => {
    expect(toDeployRequest(2863, 0, 45)).toEqual(toRequest("deployConstellation", 2863, null));
    expect(toDeployRequest(2863, null, null)).toMatchObject({ inclination: null, lan: null });
  });

  it("sends a polar ring's plane in radians and refuses an inclination past 180", () => {
    const polar = RING_PRESETS.find((preset) => preset.label === "POLAR")!;
    expect(toDeployRequest(2863, polar.inclinationDeg, 30)).toMatchObject({ kind: "deployConstellation", altitude: 2_863_000, inclination: Math.PI / 2, lan: Math.PI / 6 });
    expect(toDeployRequest(2863, 181, 0)).toBeNull();
    expect(toDeployRequest(0, 90, 0)).toBeNull();
  });

  it("sends the end of life the operator picked, and null for AUTO", () => {
    expect(END_OF_LIFE.map((choice) => choice.label)).toEqual(["AUTO", "BURN UP", "LAND HOME", "PARK"]);
    expect(toDeployRequest(2863, 0, 0)).toMatchObject({ endOfLife: null });
    expect(toDeployRequest(2863, 90, 0, "park")).toMatchObject({ endOfLife: "park", inclination: Math.PI / 2 });
    expect(toDeployRequest(2863, 0, 0, "burnUp")).toEqual({ ...toRequest("deployConstellation", 2863, null), endOfLife: "burnUp" });
  });
});

describe("toLaunchRequest", () => {
  it("launches to a circular orbit due east when only the apoapsis is given", () => {
    expect(toLaunchRequest(80, null, null, null)).toEqual({ kind: "launch", altitude: 80000, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null });
  });

  it("carries a custom orbit, an inclination and a LAN in metres and radians", () => {
    const request = toLaunchRequest(150, 120, 45, 120);
    expect(request?.altitude).toBe(150000);
    expect(request?.periapsis).toBe(120000);
    expect(request?.inclination).toBeCloseTo(Math.PI / 4);
    expect(request?.lan).toBeCloseTo((2 * Math.PI) / 3);
  });

  it("refuses a LAN without an inclination, an inclination past 180 degrees and a missing apoapsis", () => {
    expect(toLaunchRequest(80, null, null, 30)).toBeNull();
    expect(toLaunchRequest(80, null, 181, null)).toBeNull();
    expect(toLaunchRequest(null, null, null, null)).toBeNull();
  });
});

describe("toRequest", () => {
  it("sends a transfer with its body and orbit altitude in metres", () => {
    expect(toRequest("transfer", 50, null, "Mun")).toEqual({ kind: "transfer", altitude: 50000, periapsis: null, inclination: null, lan: null, body: "Mun", targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null });
  });

  it("refuses a transfer without a body, and leaves the body empty for every other change", () => {
    expect(toRequest("transfer", 50, null, null)).toBeNull();
    expect(toRequest("circularOrbit", 100, 0, "Mun")?.body).toBeNull();
  });

  it("carries an inclination at the body in radians, and refuses one past 180 degrees", () => {
    expect(toRequest("transfer", 50, 80, "Mun")?.inclination).toBeCloseTo((80 * Math.PI) / 180);
    expect(toRequest("transfer", 50, 181, "Mun")).toBeNull();
  });

  it("offers a transfer to one vessel only", () => {
    expect(KINDS.find((entry) => entry.kind === "transfer")?.vesselOnly).toBe(true);
  });
});

describe("rendezvous", () => {
  it("names the picked target and waits until one is picked", () => {
    expect(toMeetRequest("rendezvous", 4242)).toEqual({ kind: "rendezvous", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: 4242, targetPortId: null, landing: null, autoSystems: null, endOfLife: null });
    expect(toMeetRequest("rendezvous", null)).toBeNull();
    expect(toRequest("rendezvous", 100, 0)).toBeNull();
  });

  it("is offered for one vessel, not for a group", () => {
    const entry = KINDS.find((kind) => kind.kind === "rendezvous");
    expect(entry?.vesselOnly).toBe(true);
    expect(entry?.groupOnly).toBeUndefined();
  });
});

describe("dock", () => {
  const port = (id: number, size: string, free: boolean) => ({ id, name: "Clamp-O-Tron Docking Port", size, free });
  const targets = [{ id: 7, name: "Station", ports: [port(71, "size1", false), port(72, "size1", true), port(73, "size2", true)] }, { id: 8, name: "Probe" }];

  it("sends the overridden port, or none so the plugin picks", () => {
    expect(toMeetRequest("dock", 7, 72)).toEqual({ kind: "dock", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: 7, targetPortId: 72, landing: null, autoSystems: null, endOfLife: null });
    expect(toMeetRequest("dock", 7, null)?.targetPortId).toBeNull();
    expect(toMeetRequest("rendezvous", 7, 72)?.targetPortId).toBeNull();
    expect(toMeetRequest("dock", null, 72)).toBeNull();
  });

  it("offers only the target's free ports", () => {
    expect(freePortsOf(targets, 7).map((p) => p.id)).toEqual([72, 73]);
    expect(freePortsOf(targets, 8)).toEqual([]);
    expect(freePortsOf(targets, null)).toEqual([]);
  });

  it("is offered for one vessel, not for a group", () => {
    expect(KINDS.find((kind) => kind.kind === "dock")?.vesselOnly).toBe(true);
  });
});

describe("toLandRequest", () => {
  it("lands at the launch pad by default with no coordinates of its own", () => {
    expect(toLandRequest("launchPad", null, null)).toEqual({
      kind: "land",
      altitude: null,
      periapsis: null,
      inclination: null,
      lan: null,
      body: null,
      targetVesselId: null,
      targetPortId: null,
      landing: { site: "launchPad", latitude: 0, longitude: 0 },
      autoSystems: null,
      endOfLife: null,
    });
    expect(toLandRequest("runway", 5, 5)?.landing).toEqual({ site: "runway", latitude: 0, longitude: 0 });
  });

  it("carries custom coordinates in radians, east positive", () => {
    const landing = toLandRequest("custom", -10, 90)?.landing;
    expect(landing?.site).toBe("custom");
    expect(landing?.latitude).toBeCloseTo(-Math.PI / 18);
    expect(landing?.longitude).toBeCloseTo(Math.PI / 2);
  });

  it("refuses custom coordinates that are blank or off the globe", () => {
    expect(toLandRequest("custom", null, 10)).toBeNull();
    expect(toLandRequest("custom", 91, 10)).toBeNull();
    expect(toLandRequest("custom", 0, 181)).toBeNull();
  });

  it("is offered for one vessel, not for a group", () => {
    expect(KINDS.find((entry) => entry.kind === "land")?.vesselOnly).toBe(true);
  });
});
