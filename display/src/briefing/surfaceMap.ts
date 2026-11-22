import type { LandingSite, SurfaceMarker } from "../protocol.gen.ts";

const DEG = 180 / Math.PI;

/** Where a landing points: a KSC site, or custom coordinates in degrees (east positive). */
export interface LandingPick {
  site: LandingSite;
  latitudeDeg: number | null;
  longitudeDeg: number | null;
}

/** A marker in degrees, ready to draw. */
export interface MapMarker {
  id: string;
  name: string;
  kind: SurfaceMarker["kind"];
  latitudeDeg: number;
  longitudeDeg: number;
}

/** KSC sites in degrees, the same spots the plugin's landing planner uses. */
export const SITE_DEGREES: Record<Exclude<LandingSite, "custom">, { latitudeDeg: number; longitudeDeg: number }> = {
  launchPad: { latitudeDeg: -0.09694444, longitudeDeg: -74.5575 },
  runway: { latitudeDeg: -0.0493916, longitudeDeg: -74.60864 },
};

/**
 * Position on the equirectangular body texture as fractions of its width and height: the left edge is 180 W, the
 * centre is the prime meridian, the top is the north pole. This is how the 3D map wraps the same texture on a sphere.
 */
export function toPoint(latitudeDeg: number, longitudeDeg: number): { x: number; y: number } {
  const wrapped = ((((longitudeDeg + 180) % 360) + 360) % 360) / 360;
  return { x: longitudeDeg === 180 ? 1 : wrapped, y: (90 - Math.max(-90, Math.min(90, latitudeDeg))) / 180 };
}

/** The coordinates under a point on the map, rounded to a hundredth of a degree like the typed fields. */
export function fromPoint(x: number, y: number): { latitudeDeg: number; longitudeDeg: number } {
  const round = (value: number) => Math.round(value * 100) / 100 || 0;
  const clamp = (value: number) => Math.max(0, Math.min(1, value));
  return { latitudeDeg: round(90 - clamp(y) * 180), longitudeDeg: round(clamp(x) * 360 - 180) };
}

/** The markers on one body, in degrees. */
export function markersOn(markers: readonly SurfaceMarker[], body: string): MapMarker[] {
  return markers
    .filter((marker) => marker.body === body)
    .map((marker) => ({ id: marker.id, name: marker.name, kind: marker.kind, latitudeDeg: marker.latitude * DEG, longitudeDeg: marker.longitude * DEG }));
}

/** Clicking the pad or runway marker picks that site, so the plugin lands with its own precise spot; anything else picks its coordinates. */
export function pickMarker(marker: MapMarker): LandingPick {
  if (marker.id === "site:launchPad") {
    return { site: "launchPad", latitudeDeg: null, longitudeDeg: null };
  }
  if (marker.id === "site:runway") {
    return { site: "runway", latitudeDeg: null, longitudeDeg: null };
  }
  const round = (value: number) => Math.round(value * 100) / 100 || 0;
  return { site: "custom", latitudeDeg: round(marker.latitudeDeg), longitudeDeg: round(marker.longitudeDeg) };
}

/** Where the pick lands in degrees, or null while the typed coordinates are incomplete. */
export function pickedSpot(pick: LandingPick): { latitudeDeg: number; longitudeDeg: number } | null {
  if (pick.site !== "custom") {
    return SITE_DEGREES[pick.site];
  }
  const { latitudeDeg, longitudeDeg } = pick;
  if (latitudeDeg === null || longitudeDeg === null || !Number.isFinite(latitudeDeg) || !Number.isFinite(longitudeDeg) || Math.abs(latitudeDeg) > 90 || Math.abs(longitudeDeg) > 180) {
    return null;
  }
  return { latitudeDeg, longitudeDeg };
}

/** "3.20 N 51.70 W", the way the plugin writes a landing target. */
export function formatSpot(latitudeDeg: number, longitudeDeg: number): string {
  return `${Math.abs(latitudeDeg).toFixed(2)} ${latitudeDeg < 0 ? "S" : "N"} ${Math.abs(longitudeDeg).toFixed(2)} ${longitudeDeg < 0 ? "W" : "E"}`;
}
