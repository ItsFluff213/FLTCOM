import { arcPath, meanMotion, propagate, vesselPosition, type Vec3 } from "../lib/kepler.ts";
import { finalOrbit, safeFloor } from "../lib/missions.ts";
import { buildScene, surfacePoint } from "../map/Scene.ts";
import { PROTOCOL_VERSION, type Body, type BriefingMessage, type Fleet, type Orbit, type Vessel } from "../protocol.gen.ts";
import { SITE_DEGREES } from "./surfaceMap.ts";

export interface PreviewBody {
  name: string;
  radius: number;
  color: string;
  position: Vec3;
}

/** One burn of the briefed vessel. `index` is its row in the burn timeline. */
export interface PreviewBurn {
  index: number;
  label: string;
  /** Where the burn fires, or null when the plan does not say. */
  at: Vec3 | null;
  /** What the burn leads to: the orbit after it, a descent to the ground, or the climb from the pad. */
  path: Vec3[] | null;
  final: boolean;
}

/** The planned maneuver in metres from the centre body, in the map's orbit frame (z north), at the time of the last burn. */
export interface ManeuverPreview {
  center: PreviewBody & { floor: number };
  /** Other bodies a leg goes round, such as the moon a transfer captures at. */
  bodies: PreviewBody[];
  current: Vec3[] | null;
  burns: PreviewBurn[];
  /** The final orbit dips under its body's safe floor. */
  low: boolean;
  touchdown: Vec3 | null;
  /** A constellation's ring and where each satellite sits on it once the last one is out. */
  ring: Vec3[] | null;
  slots: Vec3[];
}

const DEG = Math.PI / 180;
const length = (p: Vec3) => Math.hypot(p.x, p.y, p.z);
const plus = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });

function untilGround(path: Vec3[], origin: Vec3, radius: number): Vec3[] {
  const hit = path.findIndex((point, i) => i > 0 && length({ x: point.x - origin.x, y: point.y - origin.y, z: point.z - origin.z }) <= radius);
  return hit < 0 ? path : path.slice(0, hit + 1);
}

/** A gravity turn sketch: up from the pad and over to the east until the target orbit's periapsis height, a quarter turn round. */
function ascent(pad: Vec3, height: number): Vec3[] {
  const r0 = length(pad);
  const up = { x: pad.x / r0, y: pad.y / r0, z: pad.z / r0 };
  const flat = Math.hypot(up.x, up.y) || 1;
  const east = { x: -up.y / flat, y: up.x / flat, z: 0 };
  return Array.from({ length: 33 }, (_, i) => {
    const t = i / 32;
    const turn = (t * Math.PI) / 2;
    const r = r0 + (height - r0) * Math.sin(turn);
    return { x: r * (Math.cos(turn) * up.x + Math.sin(turn) * east.x), y: r * (Math.cos(turn) * up.y + Math.sin(turn) * east.y), z: r * (Math.cos(turn) * up.z + Math.sin(turn) * east.z) };
  });
}

export function maneuverPreview(briefing: BriefingMessage, vessel: Vessel | null, bodies: ReadonlyMap<string, Body>, ut: number): ManeuverPreview | null {
  const own = briefing.burns.map((burn, index) => ({ burn, index })).filter(({ burn }) => burn.vesselId === vessel?.id);
  if (!vessel || own.length === 0) {
    return null;
  }
  const last = own[own.length - 1]!.burn;
  const end = Math.max(last.nodeUt, last.startUt + last.duration);
  const now: Fleet = { protocolVersion: PROTOCOL_VERSION, type: "fleet", seq: 0, ut, warpRate: 1, activeVesselId: null, bodies: [...bodies.values()], vessels: [vessel], markers: null };
  const fleet = propagate(now, Math.max(0, end - ut));
  const home = vessel.location.kind === "orbit" ? vessel.location.orbit.referenceBody : vessel.location.body;
  const interplanetary = own.find(({ burn }) => burn.orbitAfter && bodies.get(burn.orbitAfter.referenceBody)?.orbit === null);
  const scene = buildScene({
    fleet,
    center: interplanetary?.burn.orbitAfter?.referenceBody ?? home,
    selectedId: vessel.id,
    burns: own.map(({ burn }) => ({ vesselId: burn.vesselId, label: burn.label, nodeUt: burn.nodeUt, missed: false, orbitAfter: burn.orbitAfter })),
  });
  const center = fleet.bodies.find((body) => body.name === scene.center.name)!;
  const placed = new Map(scene.bodies.map((body) => [body.name, body.position]));
  const sceneOrbit = (id: string) => scene.orbits.find((orbit) => orbit.id === id)?.points ?? null;
  const landing = briefing.change.kind === "land";
  const launch = vessel.location.kind === "surface";
  const pad = launch ? (scene.entities.find((entity) => entity.id === `vessel:${vessel.id}`)?.position ?? null) : null;
  const aimed = own.find(({ burn }) => burn.orbitAfter)?.burn.orbitAfter ?? null;
  const climb = pad && aimed ? ascent(pad, aimed.semiMajorAxis * (1 - aimed.eccentricity)) : null;

  /** The stretch of `orbit` flown from its mean anomaly for `seconds`, or for up to one revolution, cut where it meets the ground. */
  const descent = (orbit: Orbit, seconds: number | null): Vec3[] | null => {
    const frame = fleet.bodies.find((body) => body.name === orbit.referenceBody);
    const origin = placed.get(orbit.referenceBody);
    const span = seconds ?? (frame ? (2 * Math.PI) / meanMotion(orbit, frame.gravParameter) : 0);
    return frame && origin ? untilGround(arcPath(orbit, frame.gravParameter, span, 96).map((point) => plus(origin, point)), origin, frame.radius) : null;
  };
  /** The same orbit with its mean anomaly moved from `fromUt` to `toUt`. */
  const moved = (orbit: Orbit, fromUt: number, toUt: number): Orbit => {
    const mu = fleet.bodies.find((body) => body.name === orbit.referenceBody)?.gravParameter ?? 0;
    return { ...orbit, meanAnomaly: orbit.meanAnomaly + meanMotion(orbit, mu) * (toUt - fromUt) };
  };
  const along = (orbit: Orbit, fromUt: number, atUt: number): Vec3 | null => {
    const origin = placed.get(orbit.referenceBody);
    return origin ? plus(origin, vesselPosition(moved(orbit, fromUt, atUt))) : null;
  };

  const burns = own.map(({ burn, index }, k): PreviewBurn => {
    const key = `${burn.vesselId}:${Math.round(burn.nodeUt)}`;
    const after = burn.orbitAfter;
    const final = k === own.length - 1;
    const nextUt = own[k + 1]?.burn.nodeUt ?? end;
    if (climb) {
      return after ? { index, label: burn.label, at: climb[climb.length - 1]!, path: sceneOrbit(`after:${key}`), final } : { index, label: burn.label, at: pad, path: climb, final };
    }
    const before = own.slice(0, k).reverse().find(({ burn: earlier }) => earlier.orbitAfter)?.burn ?? null;
    if (!after && before?.orbitAfter) {
      return { index, label: burn.label, at: along(before.orbitAfter, before.nodeUt, burn.nodeUt), path: landing ? descent(moved(before.orbitAfter, before.nodeUt, burn.nodeUt), null) : null, final };
    }
    const at = after ? along(after, burn.nodeUt, burn.nodeUt) : (scene.entities.find((entity) => entity.id === `burn:${key}`)?.position ?? null);
    const path = landing && after ? descent(after, k < own.length - 1 ? nextUt - burn.nodeUt : null) : sceneOrbit(`after:${key}`);
    return { index, label: burn.label, at, path, final };
  });

  const spot = briefing.change.landing && (briefing.change.landing.site === "custom" ? briefing.change.landing : { latitude: SITE_DEGREES[briefing.change.landing.site].latitudeDeg * DEG, longitude: SITE_DEGREES[briefing.change.landing.site].longitudeDeg * DEG });
  const lands = landing && spot ? fleet.bodies.find((body) => body.name === home) : undefined;
  const releases = briefing.deployment?.releases ?? [];
  const settled = Math.max(end, ...releases.map((release) => release.nodeUt));
  const ringBody = releases[0] && fleet.bodies.find((body) => body.name === releases[0]!.finalOrbit.referenceBody);
  const ringOrigin = releases[0] && placed.get(releases[0].finalOrbit.referenceBody);
  const ring = releases[0] && ringBody && ringOrigin ? arcPath(releases[0].finalOrbit, ringBody.gravParameter, (2 * Math.PI) / meanMotion(releases[0].finalOrbit, ringBody.gravParameter), 96).map((point) => plus(ringOrigin, point)) : null;
  const slots = releases.flatMap((release) => along(release.finalOrbit, release.nodeUt, settled) ?? []);
  const frames = new Set(own.flatMap(({ burn }) => (burn.orbitAfter ? [burn.orbitAfter.referenceBody] : [])));
  return {
    center: { name: center.name, radius: center.radius, color: center.color, position: { x: 0, y: 0, z: 0 }, floor: safeFloor(center) },
    bodies: fleet.bodies
      .filter((body) => body !== center && frames.has(body.name) && placed.has(body.name))
      .map((body) => ({ name: body.name, radius: body.radius, color: body.color, position: placed.get(body.name)! })),
    current: sceneOrbit(`vessel:${vessel.id}`),
    burns,
    low: finalOrbit(briefing, bodies)?.low ?? false,
    touchdown: lands && spot ? plus(placed.get(lands.name) ?? { x: 0, y: 0, z: 0 }, surfacePoint(lands, spot.latitude, spot.longitude)) : null,
    ring,
    slots,
  };
}

/** The view sits 35 degrees above the orbit plane, looking north-east, so an inclination change still shows. */
const ELEVATION = 35 * DEG;
const SIN = Math.sin(ELEVATION);
const COS = Math.cos(ELEVATION);

export interface Projected {
  x: number;
  y: number;
  /** Toward the viewer is positive: a point with negative depth inside the centre body's disc is hidden by it. */
  depth: number;
}

export function project(p: Vec3): Projected {
  return { x: p.x, y: -(p.y * SIN + p.z * COS), depth: -p.y * COS + p.z * SIN };
}

export interface Stroke {
  d: string;
  behind: boolean;
}

export interface Drawing {
  scale: number;
  low: boolean;
  current: Stroke[];
  burns: { index: number; label: string; final: boolean; mark: { x: number; y: number } | null; strokes: Stroke[] }[];
  center: { r: number; floor: number; color: string };
  bodies: { name: string; x: number; y: number; r: number; color: string }[];
  touchdown: { x: number; y: number } | null;
  ring: Stroke[];
  slots: { x: number; y: number }[];
}

/** Fits the preview into a width by height box centred on the centre body and splits each line where it passes behind it. */
export function draw(preview: ManeuverPreview, width: number, height: number, pad = 14): Drawing {
  const lines = [preview.current, preview.ring, ...preview.burns.map((burn) => burn.path)].filter((line): line is Vec3[] => line !== null);
  const points = [...lines.flat(), ...preview.burns.flatMap((burn) => (burn.at ? [burn.at] : []))].map(project);
  const reach = preview.center.radius + preview.center.floor;
  const spanX = Math.max(reach, ...points.map((p) => Math.abs(p.x)), ...preview.bodies.map((body) => Math.abs(project(body.position).x) + body.radius));
  const spanY = Math.max(reach, ...points.map((p) => Math.abs(p.y)), ...preview.bodies.map((body) => Math.abs(project(body.position).y) + body.radius));
  const scale = Math.min((width / 2 - pad) / spanX, (height / 2 - pad) / spanY);
  const at = (p: Projected) => ({ x: width / 2 + p.x * scale, y: height / 2 + p.y * scale });
  const xy = (p: Projected) => {
    const s = at(p);
    return `${s.x.toFixed(1)},${s.y.toFixed(1)}`;
  };
  const strokes = (line: Vec3[] | null): Stroke[] => {
    const out: Stroke[] = [];
    let run: Projected[] = [];
    let behind: boolean | null = null;
    for (const p of (line ?? []).map(project)) {
      const hidden = p.depth < 0;
      if (behind !== null && hidden !== behind) {
        out.push({ d: `M${run.map(xy).join("L")}`, behind });
        run = [run[run.length - 1]!];
      }
      behind = hidden;
      run.push(p);
    }
    if (behind !== null && run.length > 1) {
      out.push({ d: `M${run.map(xy).join("L")}`, behind });
    }
    return out;
  };
  return {
    scale,
    low: preview.low,
    current: strokes(preview.current),
    burns: preview.burns.map((burn) => ({ index: burn.index, label: burn.label, final: burn.final, mark: burn.at ? at(project(burn.at)) : null, strokes: strokes(burn.path) })),
    center: { r: preview.center.radius * scale, floor: (preview.center.radius + preview.center.floor) * scale, color: preview.center.color },
    bodies: preview.bodies.map((body) => ({ name: body.name, ...at(project(body.position)), r: Math.max(2, body.radius * scale), color: body.color })),
    touchdown: preview.touchdown ? at(project(preview.touchdown)) : null,
    ring: strokes(preview.ring),
    slots: preview.slots.map((slot) => at(project(slot))),
  };
}
