import type { Body, Fleet, Orbit, Vessel } from "../protocol.gen.ts";

const TWO_PI = 2 * Math.PI;

export function meanMotion(orbit: Orbit, gravParameter: number): number {
  return Math.sqrt(gravParameter / Math.abs(orbit.semiMajorAxis) ** 3);
}

export function apsisAltitudes(orbit: Orbit, body: Body): { apoapsisAltitude: number | null; periapsisAltitude: number } {
  const periapsisAltitude = orbit.semiMajorAxis * (1 - orbit.eccentricity) - body.radius;
  const apoapsisAltitude = orbit.eccentricity < 1 ? orbit.semiMajorAxis * (1 + orbit.eccentricity) - body.radius : null;
  return { apoapsisAltitude, periapsisAltitude };
}

function advance(orbit: Orbit, gravParameter: number, dt: number): Orbit {
  const meanAnomaly = orbit.meanAnomaly + meanMotion(orbit, gravParameter) * dt;
  return { ...orbit, meanAnomaly: orbit.eccentricity < 1 ? ((meanAnomaly % TWO_PI) + TWO_PI) % TWO_PI : meanAnomaly };
}

export function propagate(fleet: Fleet, dt: number): Fleet {
  const mu = new Map(fleet.bodies.map((body) => [body.name, body.gravParameter]));
  const move = (orbit: Orbit): Orbit => advance(orbit, mu.get(orbit.referenceBody) ?? 0, dt);
  const vessels = fleet.vessels.map((vessel): Vessel =>
    vessel.location.kind === "orbit" ? { ...vessel, location: { ...vessel.location, orbit: move(vessel.location.orbit) } } : vessel,
  );
  const bodies = fleet.bodies.map(
    (body): Body => ({
      ...body,
      orbit: body.orbit ? move(body.orbit) : null,
      rotationAngle: (body.rotationAngle + (TWO_PI * dt) / body.rotationPeriod) % TWO_PI,
    }),
  );
  return { ...fleet, ut: fleet.ut + dt, bodies, vessels };
}

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

function solveEccentricAnomaly(meanAnomaly: number, e: number): number {
  let x = e < 0.8 ? meanAnomaly : Math.PI;
  for (let i = 0; i < 32; i++) {
    const step = (x - e * Math.sin(x) - meanAnomaly) / (1 - e * Math.cos(x));
    x -= step;
    if (Math.abs(step) < 1e-12) {
      break;
    }
  }
  return x;
}

function solveHyperbolicAnomaly(meanAnomaly: number, e: number): number {
  let x = Math.asinh(meanAnomaly / e);
  for (let i = 0; i < 48; i++) {
    const step = (e * Math.sinh(x) - x - meanAnomaly) / (e * Math.cosh(x) - 1);
    x -= step;
    if (Math.abs(step) < 1e-12) {
      break;
    }
  }
  return x;
}

export function trueAnomalyAt(orbit: Orbit): number {
  const e = orbit.eccentricity;
  if (e < 1) {
    const eccentric = solveEccentricAnomaly(orbit.meanAnomaly, e);
    return 2 * Math.atan2(Math.sqrt(1 + e) * Math.sin(eccentric / 2), Math.sqrt(1 - e) * Math.cos(eccentric / 2));
  }
  const hyperbolic = solveHyperbolicAnomaly(orbit.meanAnomaly, e);
  return 2 * Math.atan2(Math.sqrt(e + 1) * Math.sinh(hyperbolic / 2), Math.sqrt(e - 1) * Math.cosh(hyperbolic / 2));
}

export function positionAt(orbit: Orbit, trueAnomaly: number): Vec3 {
  const e = orbit.eccentricity;
  const a = Math.abs(orbit.semiMajorAxis);
  const p = e < 1 ? a * (1 - e * e) : a * (e * e - 1);
  const r = p / (1 + e * Math.cos(trueAnomaly));
  const u = orbit.argumentOfPeriapsis + trueAnomaly;
  const cosU = Math.cos(u);
  const sinU = Math.sin(u);
  const cosO = Math.cos(orbit.longitudeOfAscendingNode);
  const sinO = Math.sin(orbit.longitudeOfAscendingNode);
  const cosI = Math.cos(orbit.inclination);
  const sinI = Math.sin(orbit.inclination);
  return {
    x: r * (cosO * cosU - sinO * sinU * cosI),
    y: r * (sinO * cosU + cosO * sinU * cosI),
    z: r * sinU * sinI,
  };
}

export function vesselPosition(orbit: Orbit): Vec3 {
  return positionAt(orbit, trueAnomalyAt(orbit));
}

/** The stretch of an orbit flown in the next `seconds`, from where the orbit's mean anomaly puts the craft now. */
export function arcPath(orbit: Orbit, gravParameter: number, seconds: number, samples: number): Vec3[] {
  const n = meanMotion(orbit, gravParameter);
  const out: Vec3[] = [];
  for (let i = 0; i <= samples; i++) {
    out.push(vesselPosition({ ...orbit, meanAnomaly: orbit.meanAnomaly + (n * seconds * i) / samples }));
  }
  return out;
}

export function orbitPath(orbit: Orbit, samples: number): Vec3[] {
  const e = orbit.eccentricity;
  const limit = e < 1 ? Math.PI : Math.min(Math.acos(-1 / e) * 0.97, Math.PI * 0.95);
  const out: Vec3[] = [];
  for (let i = 0; i <= samples; i++) {
    out.push(positionAt(orbit, -limit + (2 * limit * i) / samples));
  }
  return out;
}
