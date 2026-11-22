import { describe, expect, it } from "vitest";
import type { SurfaceMarker } from "../protocol.gen.ts";
import { formatSpot, fromPoint, markersOn, pickMarker, pickedSpot, toPoint } from "./surfaceMap.ts";

const DEG = Math.PI / 180;
const marker = (id: string, kind: SurfaceMarker["kind"], body: string, latitudeDeg: number, longitudeDeg: number): SurfaceMarker => ({
  id,
  name: id,
  kind,
  body,
  latitude: latitudeDeg * DEG,
  longitude: longitudeDeg * DEG,
});

describe("surface map projection", () => {
  it("puts 180 W on the left edge, the prime meridian in the middle and the north pole on top", () => {
    expect(toPoint(0, -180)).toEqual({ x: 0, y: 0.5 });
    expect(toPoint(0, 0)).toEqual({ x: 0.5, y: 0.5 });
    expect(toPoint(90, 180)).toEqual({ x: 1, y: 0 });
    expect(toPoint(-90, 90)).toEqual({ x: 0.75, y: 1 });
  });

  it("reads back the coordinates under a click, to a hundredth of a degree", () => {
    const { x, y } = toPoint(8.4, 23.6);
    expect(fromPoint(x, y)).toEqual({ latitudeDeg: 8.4, longitudeDeg: 23.6 });
    expect(fromPoint(-0.2, 1.4)).toEqual({ latitudeDeg: -90, longitudeDeg: -180 });
  });
});

describe("markers", () => {
  const markers = [
    marker("site:launchPad", "site", "Kerbin", -0.09694444, -74.5575),
    marker("flag:1190", "flag", "Mun", 0.67, -38.9),
    marker("waypoint:a", "waypoint", "Mun", 8.4, 23.6),
  ];

  it("keeps only the markers on the body being landed on, in degrees", () => {
    const mun = markersOn(markers, "Mun");
    expect(mun.map((entry) => entry.id)).toEqual(["flag:1190", "waypoint:a"]);
    expect(mun[0].latitudeDeg).toBeCloseTo(0.67);
    expect(mun[0].longitudeDeg).toBeCloseTo(-38.9);
  });

  it("picks the launch pad as a site and any other marker as custom coordinates", () => {
    const [pad] = markersOn(markers, "Kerbin");
    expect(pickMarker(pad)).toEqual({ site: "launchPad", latitudeDeg: null, longitudeDeg: null });
    const [flag] = markersOn(markers, "Mun");
    expect(pickMarker(flag)).toEqual({ site: "custom", latitudeDeg: 0.67, longitudeDeg: -38.9 });
  });

  it("knows the spot of a site pick and waits for both typed coordinates", () => {
    expect(pickedSpot({ site: "runway", latitudeDeg: null, longitudeDeg: null })).toEqual({ latitudeDeg: -0.0493916, longitudeDeg: -74.60864 });
    expect(pickedSpot({ site: "custom", latitudeDeg: 3, longitudeDeg: null })).toBeNull();
    expect(pickedSpot({ site: "custom", latitudeDeg: 91, longitudeDeg: 0 })).toBeNull();
    expect(pickedSpot({ site: "custom", latitudeDeg: 3.2, longitudeDeg: -51.7 })).toEqual({ latitudeDeg: 3.2, longitudeDeg: -51.7 });
  });

  it("writes a spot the way the plugin's briefing does", () => {
    expect(formatSpot(3.2, -51.7)).toBe("3.20 N 51.70 W");
    expect(formatSpot(-14.2, 112.5)).toBe("14.20 S 112.50 E");
  });
});
