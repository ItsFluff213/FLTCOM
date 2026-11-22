import type { Entity, MapOrbit, Role } from "./Scene.ts";

/** Smallest drawn size as a share of the map height: radius for bodies, full length for ships and burns. */
export const MIN_SHARE = { body: 0.022, moon: 0.012, star: 0.035, vessel: 0.036, selected: 0.046, burn: 0.024 };

/** Orbit line widths in CSS pixels. */
export const ORBIT_WIDTH: Record<Role, number> = { body: 1.5, fleet: 2.5, selected: 3.5, planned: 3, alert: 3, operation: 3 };
export const ORBIT_OPACITY: Record<Role, number> = { fleet: 0.6, body: 0.45, selected: 1, planned: 0.95, alert: 0.9, operation: 0.85 };

export interface LineStyle {
  width: number;
  opacity: number;
  dashed: boolean;
}

/** Planned orbits and the orbit after a burn are dashed: they show what will be, not what is. */
export function orbitStyle(orbit: Pick<MapOrbit, "id" | "role">): LineStyle {
  return {
    width: ORBIT_WIDTH[orbit.role],
    opacity: ORBIT_OPACITY[orbit.role],
    dashed: orbit.role === "planned" || orbit.id.startsWith("after:"),
  };
}

/** On-screen length of a ship or burn marker in pixels. */
export function markerPx(entity: Pick<Entity, "kind" | "selected" | "scale">, heightPx: number): number {
  const share = entity.kind === "burn" ? MIN_SHARE.burn : entity.selected ? MIN_SHARE.selected : MIN_SHARE.vessel;
  return share * heightPx * entity.scale;
}

/** Half the side of the corner bracket drawn around a ship, so it stays findable at any zoom. */
export function bracketHalf(entity: Pick<Entity, "kind" | "selected" | "scale">, heightPx: number): number {
  return Math.round(markerPx(entity, heightPx) / 2 + (entity.selected ? 7 : 5));
}
