import type { Vec3 } from "../lib/kepler.ts";

/** An orbiting camera in map units (z north): it looks at target from distance, turned by azimuth and raised by elevation. */
export interface View {
  target: Vec3;
  distance: number;
  azimuth: number;
  elevation: number;
}

export interface Anchor {
  id: string;
  x: number;
  y: number;
  /** Map units from the camera, for ordering and size. */
  depth: number;
  visible: boolean;
  /** In front of the camera but behind a body. Ships draw over bodies, so they stay visible, dimmed; bodies hide. */
  behind?: boolean;
}

export const FOV_DEGREES = 40;
export const MAX_ELEVATION = 1.45;
export const MIN_DISTANCE = 1e-6;
export const MAX_DISTANCE = 12;
export const DRIFT_RADIANS_PER_SECOND = 0.012;
export const DRIFT_AFTER_MS = 8000;

export const HOME_VIEW: View = { target: { x: 0, y: 0, z: 0 }, distance: 2.4, azimuth: -0.7, elevation: 0.42 };

export function cameraPosition(view: View): Vec3 {
  const flat = Math.cos(view.elevation) * view.distance;
  return {
    x: view.target.x + flat * Math.cos(view.azimuth),
    y: view.target.y + flat * Math.sin(view.azimuth),
    z: view.target.z + Math.sin(view.elevation) * view.distance,
  };
}

/** How many map units one screen pixel spans at a given distance from the camera. */
export function unitsPerPixel(distance: number, heightPx: number, fovDegrees = FOV_DEGREES): number {
  return (2 * distance * Math.tan((fovDegrees * Math.PI) / 360)) / Math.max(1, heightPx);
}

/** The radius to draw something at so it never shrinks below minPx on screen. */
export function shownRadius(trueRadius: number, distance: number, heightPx: number, minPx: number): number {
  return Math.max(trueRadius, minPx * unitsPerPixel(distance, heightPx));
}

export function rotate(view: View, dxPx: number, dyPx: number): View {
  const elevation = Math.max(-MAX_ELEVATION, Math.min(MAX_ELEVATION, view.elevation + dyPx * 0.005));
  return { ...view, azimuth: view.azimuth - dxPx * 0.005, elevation };
}

/** Moves the target with the pointer: right-drag slides the map across the screen. */
export function pan(view: View, dxPx: number, dyPx: number, heightPx: number): View {
  const k = unitsPerPixel(view.distance, heightPx);
  const right = { x: -Math.sin(view.azimuth), y: Math.cos(view.azimuth), z: 0 };
  const up = {
    x: -Math.sin(view.elevation) * Math.cos(view.azimuth),
    y: -Math.sin(view.elevation) * Math.sin(view.azimuth),
    z: Math.cos(view.elevation),
  };
  return {
    ...view,
    target: {
      x: view.target.x + (-dxPx * right.x + dyPx * up.x) * k,
      y: view.target.y + (-dxPx * right.y + dyPx * up.y) * k,
      z: view.target.z + (-dxPx * right.z + dyPx * up.z) * k,
    },
  };
}

export function zoom(view: View, wheelDelta: number): View {
  const distance = Math.max(MIN_DISTANCE, Math.min(MAX_DISTANCE, view.distance * Math.exp(wheelDelta * 0.0012)));
  return { ...view, distance };
}

const ease = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);

/** The view part way along a flight; distance moves in log space so a long zoom feels even. */
export function between(from: View, to: View, t: number): View {
  const k = ease(Math.max(0, Math.min(1, t)));
  const mix = (a: number, b: number) => a + (b - a) * k;
  let turn = (to.azimuth - from.azimuth) % (2 * Math.PI);
  if (turn > Math.PI) turn -= 2 * Math.PI;
  if (turn < -Math.PI) turn += 2 * Math.PI;
  return {
    target: { x: mix(from.target.x, to.target.x), y: mix(from.target.y, to.target.y), z: mix(from.target.z, to.target.z) },
    distance: Math.exp(mix(Math.log(from.distance), Math.log(to.distance))),
    azimuth: from.azimuth + turn * k,
    elevation: mix(from.elevation, to.elevation),
  };
}

/** The nearest visible anchor within reach of a click, ships before bodies when both are close. */
export function pickAt<T extends Anchor>(anchors: readonly T[], x: number, y: number, reachPx = 14, prefer?: (anchor: T) => boolean): T | null {
  let best: T | null = null;
  let bestScore = Infinity;
  for (const anchor of anchors) {
    if (!anchor.visible) {
      continue;
    }
    const d = Math.hypot(anchor.x - x, anchor.y - y);
    if (d > reachPx) {
      continue;
    }
    const score = d - (prefer?.(anchor) ? reachPx / 2 : 0);
    if (score < bestScore) {
      bestScore = score;
      best = anchor;
    }
  }
  return best;
}

/** True when the segment from eye to point passes through the sphere, so a body hides what is behind it. */
export function hiddenBehind(eye: Vec3, point: Vec3, center: Vec3, radius: number): boolean {
  const d = { x: point.x - eye.x, y: point.y - eye.y, z: point.z - eye.z };
  const f = { x: eye.x - center.x, y: eye.y - center.y, z: eye.z - center.z };
  const a = d.x * d.x + d.y * d.y + d.z * d.z;
  if (a === 0) {
    return false;
  }
  const t = -(f.x * d.x + f.y * d.y + f.z * d.z) / a;
  if (t <= 0 || t >= 1) {
    return false;
  }
  const c = { x: f.x + d.x * t, y: f.y + d.y * t, z: f.z + d.z * t };
  const inside = c.x * c.x + c.y * c.y + c.z * c.z < radius * radius;
  const pointInside = (point.x - center.x) ** 2 + (point.y - center.y) ** 2 + (point.z - center.z) ** 2 <= radius * radius * 1.0001;
  return inside && !pointInside;
}

/**
 * The opening view: close enough that most of the given positions (map units, never past the map edge) fill the
 * screen. One probe far out would otherwise shrink a whole fleet in low orbit to dots, so the farthest fifth may lie
 * off screen. Seen from above at an angle, a flat orbit looks sin(elevation) as tall as it is wide, so on a wide
 * screen the width limits how close the camera can come.
 */
export function framing(positions: readonly Vec3[], aspect = 1): View {
  const distances = positions.map((p) => Math.hypot(p.x, p.y, p.z)).sort((a, b) => a - b);
  const most = distances[Math.max(0, Math.ceil(distances.length * 0.8) - 1)] ?? 0;
  const reach = Math.min(1, Math.max(0.02, most));
  const share = Math.max(Math.sin(HOME_VIEW.elevation), 1 / Math.max(1, aspect));
  return { ...HOME_VIEW, distance: (reach * 1.15 * share) / Math.tan((FOV_DEGREES * Math.PI) / 360) };
}
