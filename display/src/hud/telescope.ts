import type { Vec3 } from "../lib/kepler.ts";

interface Rotation {
  x: number;
  y: number;
  z: number;
  w: number;
}

const cross = (a: Vec3, b: Vec3): Vec3 => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
const scale = (a: Vec3, k: number): Vec3 => ({ x: a.x * k, y: a.y * k, z: a.z * k });
const plus = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });
const unit = (a: Vec3): Vec3 => scale(a, 1 / Math.max(1e-12, Math.hypot(a.x, a.y, a.z)));

export function rotateVector(q: Rotation, v: Vec3): Vec3 {
  const t = scale(cross(q, v), 2);
  return plus(plus(v, scale(t, q.w)), cross(q, t));
}

/**
 * Where the telescope looks from, as a unit vector from the ship: always side-on to its long axis, so the real
 * attitude shows as roll and pitch against the orbit frame, swinging slowly round the hull as seconds pass.
 */
export function telescopeEye(attitude: Rotation, seconds: number): Vec3 {
  const axis = unit(rotateVector(attitude, { x: 0, y: 0, z: 1 }));
  const across = cross(axis, { x: 0, y: 0, z: 1 });
  const side = Math.hypot(across.x, across.y, across.z) < 1e-3 ? { x: 1, y: 0, z: 0 } : unit(across);
  const other = cross(axis, side);
  const swing = 0.6 + 0.35 * Math.sin(seconds * 0.05);
  const around = plus(scale(side, Math.cos(swing)), scale(other, Math.sin(swing)));
  const tilt = 0.22 + 0.1 * Math.sin(seconds * 0.031);
  return unit(plus(scale(around, Math.cos(tilt)), scale(axis, Math.sin(tilt))));
}
