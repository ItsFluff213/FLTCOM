import { arcPath, meanMotion, orbitPath, vesselPosition, type Vec3 } from "../lib/kepler.ts";
import { offsetText } from "../lib/missions.ts";
import type { Body, Fleet, Orbit, Quat, SizeClass, Vessel } from "../protocol.gen.ts";
import type { LayoutRock } from "./systemLayout.ts";

export type Role = "fleet" | "selected" | "alert" | "planned" | "operation" | "body";

/** Colour marks meaning only: grey for the ordinary, one colour each for selection, plans, trouble and the live operation. */
export const ROLE_COLOR: Record<Role, string> = {
  fleet: "#c9c6c2",
  selected: "#39ff88",
  alert: "#ff4d4d",
  planned: "#ffd84a",
  operation: "#4da3ff",
  body: "#8d8a86",
};

export interface PlannedBurn {
  vesselId: number;
  label: string;
  nodeUt: number;
  missed: boolean;
  orbitAfter?: Orbit | null;
}

/** A predicted touchdown on the body the vessel orbits; latitude and longitude in radians, east positive. */
export interface PlannedLanding {
  vesselId: number;
  latitude: number;
  longitude: number;
  ut: number;
}

/** Where a drawn thing hangs: the body whose drawn position it moves with, and the body whose orbit spread scales it (null: true size). */
export interface Placement {
  frame: string;
  scaledBy: string | null;
}

export interface MapBody {
  name: string;
  /** The body it orbits in this scene, or null for the center. */
  parent: string | null;
  semiMajorAxis: number;
  eccentricity: number;
  radius: number;
  texture: string | null;
  rotationAngle: number;
  star: boolean;
  position: Vec3;
}

export interface MapOrbit extends Placement {
  id: string;
  role: Role;
  points: Vec3[];
}

/** How a vessel is drawn: its craft model, or a rock for asteroids and comets. */
export type Look = "craft" | "asteroid" | "comet";

export interface Entity extends Placement {
  id: string;
  kind: "vessel" | "body" | "burn";
  text: string;
  role: Role;
  selected: boolean;
  color: string;
  /** Metres from the center body, in the orbit frame (z north). */
  position: Vec3;
  vesselId: number | null;
  attitude: Quat | null;
  model: string | null;
  look: Look;
  /** Drawn size against a standard marker: an asteroid or comet by its size class, 1 for everything else. */
  scale: number;
  /** Debris, asteroids and comets: nobody flies them, so they rank last and carry no label at system scale. */
  unowned: boolean;
  /** The body a vessel is at, for clustering; null for bodies and burns. */
  home: string | null;
}

export interface MapScene {
  center: Body;
  /** How far out the map reaches, in metres: the sphere of influence, or past the farthest moon. */
  extent: number;
  /** Unit vector from the center toward the star, or null when the center is the star. */
  sunDirection: Vec3 | null;
  bodies: MapBody[];
  orbits: MapOrbit[];
  entities: Entity[];
  /** Asteroids and comets on orbits, for the layout to keep clear of bodies and of each other. */
  rocks: LayoutRock[];
}

export interface SceneInput {
  fleet: Fleet;
  center: string;
  selectedId: number | null;
  burns?: readonly PlannedBurn[];
  landings?: readonly PlannedLanding[];
  members?: ReadonlySet<number>;
  operating?: ReadonlySet<number>;
  models?: ReadonlyMap<number, string | null>;
}

const add = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });
const sub = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const ORIGIN: Vec3 = { x: 0, y: 0, z: 0 };

const UNOWNED: ReadonlySet<Vessel["vesselType"]> = new Set(["debris", "spaceObject", "droppedPart"]);

/** The plugin tells comets from asteroids by the game's comet module; without that, KSP names comets "... Comet" or "C/...". */
export function lookOf(vessel: Pick<Vessel, "vesselType" | "name" | "spaceObject">): Look {
  if (vessel.vesselType !== "spaceObject") {
    return "craft";
  }
  return (vessel.spaceObject ? vessel.spaceObject.kind === "comet" : /comet|^c\//i.test(vessel.name)) ? "comet" : "asteroid";
}

/** Asteroids grow from class a to e and on through the rare larger letters; comets keep a gentler scale of their own. */
const ROCK_SCALE: Record<"asteroid" | "comet", Record<SizeClass, number>> = {
  asteroid: { a: 0.55, b: 0.75, c: 1, d: 1.3, e: 1.65, f: 1.85, g: 2.05, h: 2.25, i: 2.45 },
  comet: { a: 0.9, b: 1.05, c: 1.2, d: 1.35, e: 1.5, f: 1.65, g: 1.8, h: 1.95, i: 2.1 },
};

export function rockScale(vessel: Pick<Vessel, "vesselType" | "name" | "spaceObject">): number {
  const look = lookOf(vessel);
  const size = vessel.spaceObject?.sizeClass;
  return look === "craft" || !size ? 1 : ROCK_SCALE[look][size];
}

export function homeOf(vessel: Vessel): string {
  return vessel.location.kind === "orbit" ? vessel.location.orbit.referenceBody : vessel.location.body;
}

export function surfacePoint(body: Body, latitude: number, longitude: number, altitude = 0): Vec3 {
  const r = body.radius + altitude;
  const angle = longitude + body.rotationAngle;
  return { x: r * Math.cos(latitude) * Math.cos(angle), y: r * Math.cos(latitude) * Math.sin(angle), z: r * Math.sin(latitude) };
}

export function buildScene({ fleet, center: centerName, selectedId, burns = [], landings = [], members = new Set(), operating = new Set(), models = new Map() }: SceneInput): MapScene {
  const planned = new Set(burns.map((burn) => burn.vesselId));
  const bodies = new Map(fleet.bodies.map((body) => [body.name, body]));
  const center = bodies.get(centerName) ?? fleet.bodies.find((body) => body.orbit === null) ?? fleet.bodies[0]!;

  const placed = new Map<string, Vec3>([[center.name, ORIGIN]]);
  const where = (name: string): Vec3 | null => {
    const known = placed.get(name);
    if (known) {
      return known;
    }
    const body = bodies.get(name);
    const parent = body?.orbit ? where(body.orbit.referenceBody) : null;
    if (!body?.orbit || !parent) {
      return null;
    }
    const position = add(parent, vesselPosition(body.orbit));
    placed.set(name, position);
    return position;
  };

  const heliocentric = (name: string): Vec3 => {
    let position = ORIGIN;
    for (let body = bodies.get(name); body?.orbit; body = bodies.get(body.orbit.referenceBody)) {
      position = add(position, vesselPosition(body.orbit));
    }
    return position;
  };
  const fromStar = heliocentric(center.name);
  const starDistance = Math.hypot(fromStar.x, fromStar.y, fromStar.z);
  const sunDirection = center.orbit && starDistance > 0 ? { x: -fromStar.x / starDistance, y: -fromStar.y / starDistance, z: -fromStar.z / starDistance } : null;

  const children = fleet.bodies.filter((body) => body.orbit?.referenceBody === center.name);
  const extent = center.sphereOfInfluence ?? Math.max(center.radius * 4, ...children.map((body) => body.orbit!.semiMajorAxis * (1 + body.orbit!.eccentricity) * 1.1));

  const mapBodies: MapBody[] = [];
  const orbits: MapOrbit[] = [];
  const entities: Entity[] = [];
  const rocks: LayoutRock[] = [];
  const offsetPath = (orbit: Orbit, origin: Vec3) => orbitPath(orbit, 128).map((point) => add(origin, point));

  for (const body of fleet.bodies) {
    const position = where(body.name);
    if (!position) {
      continue;
    }
    const parent = body === center ? null : body.orbit!.referenceBody;
    mapBodies.push({ name: body.name, parent, semiMajorAxis: body.orbit?.semiMajorAxis ?? 0, eccentricity: body.orbit?.eccentricity ?? 0, radius: body.radius, texture: body.texture, rotationAngle: body.rotationAngle, star: body.orbit === null, position });
    if (body === center) {
      continue;
    }
    const spread = { frame: parent!, scaledBy: body.name };
    orbits.push({ id: `body:${body.name}`, role: "body", points: offsetPath(body.orbit!, where(parent!)!), ...spread });
    entities.push({ id: `body:${body.name}`, kind: "body", text: body.name.toUpperCase(), role: "body", selected: false, color: ROLE_COLOR.body, position, vesselId: null, attitude: null, model: null, look: "craft", scale: 1, unowned: false, home: null, ...spread });
  }

  // Something orbiting the star itself spreads with the planet whose orbit is nearest its own, so an asteroid near
  // Kerbin's orbit is still drawn near Kerbin's spread-out ring.
  const spreadOf = (home: Body, location: Vessel["location"]): string | null => {
    if (home.orbit !== null || location.kind !== "orbit") {
      return null;
    }
    const near = (body: MapBody) => Math.abs(Math.log(body.semiMajorAxis / Math.abs(location.orbit.semiMajorAxis)));
    const nearest = mapBodies.filter((body) => body.parent === home.name).sort((a, b) => near(a) - near(b))[0];
    return nearest?.name ?? null;
  };

  const roleOf = (vessel: Vessel): Role =>
    vessel.comms === "los" ? "alert" : vessel.id === selectedId || members.has(vessel.id) ? "selected" : operating.has(vessel.id) ? "operation" : "fleet";

  for (const vessel of fleet.vessels) {
    const home = bodies.get(homeOf(vessel));
    const origin = home ? where(home.name) : null;
    if (!home || !origin) {
      continue;
    }
    const location = vessel.location;
    const role = roleOf(vessel);
    const id = `vessel:${vessel.id}`;
    const look = lookOf(vessel);
    const rock = look !== "craft" && location.kind === "orbit";
    // A rock around a planet or moon gets a ring of its own in the layout, so it and its orbit spread by that ring.
    const scaledBy = rock && home.orbit !== null ? id : spreadOf(home, location);
    const position = add(origin, location.kind === "orbit" ? vesselPosition(location.orbit) : surfacePoint(home, location.latitude, location.longitude, location.altitude));
    if (rock) {
      rocks.push({ id, frame: home.name, spreadWith: home.orbit === null ? scaledBy : null, position, semiMajorAxis: location.orbit.semiMajorAxis, eccentricity: location.orbit.eccentricity, size: rockScale(vessel) });
    }
    if (location.kind === "orbit" && location.orbit.eccentricity < 1 && (!UNOWNED.has(vessel.vesselType) || role === "selected")) {
      orbits.push({ id: `vessel:${vessel.id}`, role: planned.has(vessel.id) && role === "fleet" ? "planned" : role, points: offsetPath(location.orbit, origin), frame: home.name, scaledBy });
    }
    entities.push({
      id,
      kind: "vessel",
      text: vessel.name,
      role,
      selected: role === "selected" || (role === "alert" && (vessel.id === selectedId || members.has(vessel.id))),
      color: ROLE_COLOR[role],
      position,
      vesselId: vessel.id,
      attitude: vessel.attitude,
      model: models.get(vessel.id) ?? null,
      look,
      scale: rockScale(vessel),
      unowned: UNOWNED.has(vessel.vesselType),
      home: home.name,
      frame: home.name,
      scaledBy,
    });
  }

  const drawn = new Set<string>();
  for (const burn of burns) {
    const key = `${burn.vesselId}:${Math.round(burn.nodeUt)}`;
    if (drawn.has(key)) {
      continue;
    }
    drawn.add(key);
    const vessel = fleet.vessels.find((candidate) => candidate.id === burn.vesselId);
    const location = vessel?.location;
    const home = location?.kind === "orbit" ? bodies.get(location.orbit.referenceBody) : undefined;
    const origin = home ? where(home.name) : null;
    const role: Role = burn.missed ? "alert" : "planned";
    if (burn.orbitAfter && burn.orbitAfter.eccentricity < 1) {
      const after = burn.orbitAfter;
      const frame = bodies.get(after.referenceBody);
      const afterOrigin = where(after.referenceBody) ?? (frame ? sub(heliocentric(frame.name), fromStar) : null);
      const next = burns.filter((other) => other.vesselId === burn.vesselId && other.nodeUt > burn.nodeUt).reduce<number | null>((soonest, other) => (soonest === null || other.nodeUt < soonest ? other.nodeUt : soonest), null);
      if (afterOrigin && frame) {
        // A leg in another body's frame, such as the flight between planets, is drawn from this burn to the next one, not as a whole orbit.
        const leg = after.referenceBody !== home?.name && next !== null;
        const points = leg ? arcPath(after, frame.gravParameter, next - burn.nodeUt, 128).map((point) => add(afterOrigin, point)) : offsetPath(after, afterOrigin);
        orbits.push({ id: `after:${key}`, role, points, frame: placed.has(frame.name) ? frame.name : center.name, scaledBy: null });
      }
    }
    if (location?.kind !== "orbit" || !home || !origin || location.orbit.eccentricity >= 1) {
      continue;
    }
    const orbit = location.orbit;
    const meanAnomaly = (orbit.meanAnomaly + meanMotion(orbit, home.gravParameter) * (burn.nodeUt - fleet.ut)) % (2 * Math.PI);
    entities.push({
      id: `burn:${key}`,
      kind: "burn",
      text: `${burn.label} ${offsetText(burn.nodeUt - fleet.ut)}`,
      role,
      selected: false,
      color: ROLE_COLOR[role],
      position: add(origin, vesselPosition({ ...orbit, meanAnomaly })),
      vesselId: burn.vesselId,
      attitude: null,
      model: null,
      look: "craft",
      scale: 1,
      unowned: false,
      home: null,
      frame: home.name,
      scaledBy: null,
    });
  }

  for (const landing of landings) {
    const vessel = fleet.vessels.find((candidate) => candidate.id === landing.vesselId);
    const body = vessel ? bodies.get(homeOf(vessel)) : undefined;
    const origin = body ? where(body.name) : null;
    if (!body || !origin) {
      continue;
    }
    entities.push({
      id: `landing:${landing.vesselId}`,
      kind: "burn",
      text: `touchdown ${offsetText(landing.ut - fleet.ut)}`,
      role: "planned",
      selected: false,
      color: ROLE_COLOR.planned,
      position: add(origin, surfacePoint(body, landing.latitude, landing.longitude)),
      vesselId: landing.vesselId,
      attitude: null,
      model: null,
      look: "craft",
      scale: 1,
      unowned: false,
      home: null,
      frame: body.name,
      scaledBy: null,
    });
  }

  return { center, extent, sunDirection, bodies: mapBodies, rocks, orbits, entities };
}

/** The bodies the map can centre on: the star, every planet by distance, the moons of the planet in view, and anywhere a ship is. */
export function centerChoices(bodies: readonly Body[], shipBodies: readonly string[], center: string): string[] {
  const byName = new Map(bodies.map((body) => [body.name, body]));
  const star = bodies.find((body) => body.orbit === null)?.name ?? "Sun";
  const planets = bodies
    .filter((body) => body.orbit?.referenceBody === star)
    .sort((a, b) => a.orbit!.semiMajorAxis - b.orbit!.semiMajorAxis)
    .map((body) => body.name);
  let planet = center;
  for (let parent = byName.get(planet)?.orbit?.referenceBody; parent && parent !== star; parent = byName.get(planet)?.orbit?.referenceBody) {
    planet = parent;
  }
  const moons = bodies.filter((body) => body.orbit?.referenceBody === planet && planet !== star).map((body) => body.name);
  return [...new Set([star, ...planets, ...moons, ...shipBodies])];
}
