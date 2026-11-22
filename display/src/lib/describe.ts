import type { Body, Vessel } from "../protocol.gen.ts";
import { apsisAltitudes } from "./kepler.ts";

const DEG = 180 / Math.PI;

export interface Whereabouts {
  body: string;
  situation: string;
  detailLabel: "AP × PE" | "POSITION";
  detail: string;
  anomaly: string;
  period: string;
  inclination: string;
}

const km = (meters: number) => `${(meters / 1000).toFixed(1)} km`;

export const situationText = (vessel: Vessel) => vessel.location.situation.replace(/([A-Z])/g, " $1").toUpperCase();

export function whereabouts(vessel: Vessel, bodies: ReadonlyMap<string, Body>): Whereabouts {
  const location = vessel.location;
  const situation = situationText(vessel);
  if (location.kind === "surface") {
    const lat = location.latitude * DEG;
    const lon = location.longitude * DEG;
    return {
      body: location.body,
      situation,
      detailLabel: "POSITION",
      detail: `${Math.abs(lat).toFixed(2)}°${lat < 0 ? "S" : "N"} ${Math.abs(lon).toFixed(2)}°${lon < 0 ? "W" : "E"}`,
      anomaly: "—",
      period: "—",
      inclination: "—",
    };
  }
  const orbit = location.orbit;
  const body = bodies.get(orbit.referenceBody);
  const altitudes = body ? apsisAltitudes(orbit, body) : null;
  const period = body && orbit.eccentricity < 1 ? (2 * Math.PI * Math.sqrt(orbit.semiMajorAxis ** 3 / body.gravParameter)).toFixed(0) + " s" : "—";
  return {
    body: orbit.referenceBody,
    situation,
    detailLabel: "AP × PE",
    detail: altitudes ? `${altitudes.apoapsisAltitude === null ? "∞" : km(altitudes.apoapsisAltitude)} × ${km(altitudes.periapsisAltitude)}` : "—",
    anomaly: `${(orbit.meanAnomaly * DEG).toFixed(2)}°`,
    period,
    inclination: `${(orbit.inclination * DEG).toFixed(2)}°`,
  };
}

const COMMS: Record<Vessel["comms"], string> = { link: "LINK", los: "LOS", none: "—", off: "OFF" };

/** A crewed vessel out of contact is still flown by its crew, and FLTCOM keeps flying it, so it never reads as lost. */
export const commsLabel = (vessel: Pick<Vessel, "comms" | "crew">) => (vessel.comms === "los" && vessel.crew.length > 0 ? "LOS · CREW FLYING" : COMMS[vessel.comms]);
