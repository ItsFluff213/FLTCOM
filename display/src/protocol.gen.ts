export const PROTOCOL_VERSION = 14;
export const MAX_DEPTH = 64;

/** Messages the server sends to the display. */
export type ServerMessage = ServerHello | Fleet | CraftLayout | Pong | Bye | BriefingMessage | Missions | GroupRoster;

function checkServerMessage(value: unknown, path: string): true {
  switch ((value as { type?: unknown } | null)?.type) {
    case "hello":
      return checkServerHello(value, path);
    case "fleet":
      return checkFleet(value, path);
    case "layout":
      return checkCraftLayout(value, path);
    case "pong":
      return checkPong(value, path);
    case "bye":
      return checkBye(value, path);
    case "briefing":
      return checkBriefingMessage(value, path);
    case "missions":
      return checkMissions(value, path);
    case "groups":
      return checkGroupRoster(value, path);
    default:
      return fail(path + ".type", "\"hello\" | \"fleet\" | \"layout\" | \"pong\" | \"bye\" | \"briefing\" | \"missions\" | \"groups\"");
  }
}

/** Parses one message. The protocol version is checked before the shape. */
export function parseServerMessage(text: string): Parsed<ServerMessage> {
  return parseMessage(text, checkServerMessage) as Parsed<ServerMessage>;
}

/** Messages the display sends to the server. */
export type ClientMessage = ClientHello | Ping | BriefRequest | Approve | Scrub | Resume | Answer | GroupEdit;

function checkClientMessage(value: unknown, path: string): true {
  switch ((value as { type?: unknown } | null)?.type) {
    case "hello":
      return checkClientHello(value, path);
    case "ping":
      return checkPing(value, path);
    case "brief":
      return checkBriefRequest(value, path);
    case "approve":
      return checkApprove(value, path);
    case "scrub":
      return checkScrub(value, path);
    case "resume":
      return checkResume(value, path);
    case "answer":
      return checkAnswer(value, path);
    case "groupEdit":
      return checkGroupEdit(value, path);
    default:
      return fail(path + ".type", "\"hello\" | \"ping\" | \"brief\" | \"approve\" | \"scrub\" | \"resume\" | \"answer\" | \"groupEdit\"");
  }
}

/** Parses one message. The protocol version is checked before the shape. */
export function parseClientMessage(text: string): Parsed<ClientMessage> {
  return parseMessage(text, checkClientMessage) as Parsed<ClientMessage>;
}

/** First message on every connection. */
export interface ServerHello {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "hello";
  /** Server name and version, such as FLTCOM 0.1.0. */
  readonly server: string;
  /** The loaded save, null at the main menu. */
  readonly saveName: string | null;
  /** Real time between fleet snapshots while a save is loaded. The display calls its fleet stale when several intervals pass without one. Unit: s. */
  readonly snapshotInterval: number;
}

function checkServerHello(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","server","saveName","snapshotInterval"]);
  stringValue(o.server, path + ".server");
  o.saveName === null || stringValue(o.saveName, path + ".saveName");
  numberValue(o.snapshotInterval, path + ".snapshotInterval");
  return true;
}

/** A complete snapshot of the game at one universal time. The display replaces its state with each one; there are no deltas. */
export interface Fleet {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "fleet";
  /** Counts up by one per snapshot for the life of the server. */
  readonly seq: number;
  /** Universal time of the snapshot. Every orbit's mean anomaly is given at this time. Unit: s. */
  readonly ut: number;
  /** Time warp multiplier, 1 at normal speed. */
  readonly warpRate: number;
  /** The vessel the player flies, null outside flight. */
  readonly activeVesselId: number | null;
  readonly bodies: readonly Body[];
  readonly vessels: readonly Vessel[];
  /** The game's own surface markers on every body. Sent when they change and again every few seconds; null in between, when the display keeps the last list it got. */
  readonly markers: MarkerList | null;
}

function checkFleet(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","seq","ut","warpRate","activeVesselId","bodies","vessels","markers"]);
  uint32Value(o.seq, path + ".seq");
  numberValue(o.ut, path + ".ut");
  numberValue(o.warpRate, path + ".warpRate");
  o.activeVesselId === null || uint32Value(o.activeVesselId, path + ".activeVesselId");
  arrayOf(o.bodies, path + ".bodies", (item, itemPath) => checkBody(item, itemPath)) && uniqueBy(o.bodies as readonly Record<string, unknown>[], path + ".bodies", "name");
  arrayOf(o.vessels, path + ".vessels", (item, itemPath) => checkVessel(item, itemPath)) && uniqueBy(o.vessels as readonly Record<string, unknown>[], path + ".vessels", "id");
  o.markers === null || checkMarkerList(o.markers, path + ".markers");
  return true;
}

/** Answer to a ping, echoing its nonce. */
export interface Pong {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "pong";
  readonly nonce: number;
}

function checkPong(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","nonce"]);
  uint32Value(o.nonce, path + ".nonce");
  return true;
}

/** Sent before the server closes the connection. The display owns the words it shows for each reason. */
export interface Bye {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "bye";
  readonly reason: ByeReason;
}

function checkBye(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","reason"]);
  oneOf(o.reason, path + ".reason", BYE_REASONS);
  return true;
}

/** First message the display sends on every connection. */
export interface ClientHello {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "hello";
  /** Client name, such as fltcom-display. */
  readonly client: string;
}

function checkClientHello(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","client"]);
  stringValue(o.client, path + ".client");
  return true;
}

export interface Ping {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "ping";
  readonly nonce: number;
}

function checkPing(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","nonce"]);
  uint32Value(o.nonce, path + ".nonce");
  return true;
}

/** shutdown: the game is closing or the save unloads. protocolMismatch: the client spoke another protocol version. The rest refuse the link because FLTCOM cannot run in that game. */
export const BYE_REASONS = ["shutdown","protocolMismatch","multiplayerSession","unsupportedPhysics","unsupportedScale"] as const;
export type ByeReason = (typeof BYE_REASONS)[number];

/** A celestial body. Its parent is orbit.referenceBody; the root star has no orbit. */
export interface Body {
  /** Unique body name, the key other messages refer to. */
  readonly name: string;
  readonly orbit: Orbit | null;
  /** Unit: m. */
  readonly radius: number;
  /** Unit: m3/s2. */
  readonly gravParameter: number;
  /** Null for the root star, whose sphere is unbounded. Unit: m. */
  readonly sphereOfInfluence: number | null;
  /** 0 when the body has no atmosphere. Unit: m. */
  readonly atmosphereHeight: number;
  /** Unit: s. */
  readonly rotationPeriod: number;
  /** How far the body has turned at the snapshot's ut: the angle from the reference direction to longitude 0, in [0, 2π). Unit: rad. */
  readonly rotationAngle: number;
  /** Highest terrain above sea level, 0 for a body without solid terrain. Unit: m. */
  readonly highestTerrainAltitude: number;
  /** Orbit color as #rrggbb. */
  readonly color: string;
  /** Server path of an equirectangular PNG of the body's surface captured from the running game, such as /art/body/Kerbin.png. Null until captured or when the capture failed; the display then draws a grey sphere. */
  readonly texture: string | null;
}

function checkBody(value: unknown, path: string): true {
  const o = objectValue(value, path, ["name","orbit","radius","gravParameter","sphereOfInfluence","atmosphereHeight","rotationPeriod","rotationAngle","highestTerrainAltitude","color","texture"]);
  stringValue(o.name, path + ".name");
  o.orbit === null || checkOrbit(o.orbit, path + ".orbit");
  numberValue(o.radius, path + ".radius");
  numberValue(o.gravParameter, path + ".gravParameter");
  o.sphereOfInfluence === null || numberValue(o.sphereOfInfluence, path + ".sphereOfInfluence");
  numberValue(o.atmosphereHeight, path + ".atmosphereHeight");
  numberValue(o.rotationPeriod, path + ".rotationPeriod");
  numberValue(o.rotationAngle, path + ".rotationAngle");
  numberValue(o.highestTerrainAltitude, path + ".highestTerrainAltitude");
  stringValue(o.color, path + ".color");
  o.texture === null || stringValue(o.texture, path + ".texture");
  return true;
}

/** Keplerian elements at the snapshot's universal time. Apoapsis, periapsis and period derive from these and the reference body. */
export interface Orbit {
  /** Name of the body this orbit is around. */
  readonly referenceBody: string;
  /** Negative on a hyperbolic orbit. Unit: m. */
  readonly semiMajorAxis: number;
  readonly eccentricity: number;
  /** Unit: rad. */
  readonly inclination: number;
  /** Unit: rad. */
  readonly longitudeOfAscendingNode: number;
  /** Unit: rad. */
  readonly argumentOfPeriapsis: number;
  /** Unit: rad. */
  readonly meanAnomaly: number;
}

function checkOrbit(value: unknown, path: string): true {
  const o = objectValue(value, path, ["referenceBody","semiMajorAxis","eccentricity","inclination","longitudeOfAscendingNode","argumentOfPeriapsis","meanAnomaly"]);
  stringValue(o.referenceBody, path + ".referenceBody");
  numberValue(o.semiMajorAxis, path + ".semiMajorAxis");
  numberValue(o.eccentricity, path + ".eccentricity");
  numberValue(o.inclination, path + ".inclination");
  numberValue(o.longitudeOfAscendingNode, path + ".longitudeOfAscendingNode");
  numberValue(o.argumentOfPeriapsis, path + ".argumentOfPeriapsis");
  numberValue(o.meanAnomaly, path + ".meanAnomaly");
  return true;
}

export interface Vessel {
  /** KSP persistentId, unique within a fleet snapshot. */
  readonly id: number;
  /** Fleet registry code, such as FLT-0042-PRB. */
  readonly registryCode: string;
  readonly name: string;
  readonly vesselType: VesselType;
  readonly location: Location;
  /** KSP Vessel.loaded: its parts are in the scene. A loaded vessel can still be on rails. */
  readonly loaded: boolean;
  /** KSP Vessel.IsControllable: it accepts control input now. */
  readonly controllable: boolean;
  readonly comms: CommsState;
  /** 0 to 1 while comms is link, otherwise null. */
  readonly signalStrength: number | null;
  readonly crewCapacity: number;
  readonly crew: readonly CrewMember[];
  /** Vessel totals per resource. */
  readonly resources: readonly Resource[];
  /** Unit: kg. */
  readonly mass: number;
  /** Null when the vessel has no engines or no estimate exists. */
  readonly deltaV: DeltaV | null;
  /** Rotation from the craft frame to the orbit frame. Craft frame: CraftPart coordinates with y and z swapped (x right, y forward, z up the craft's axis). Orbit frame: the frame of the orbital elements, z toward the north pole, x where a body with rotationAngle 0 has longitude 0. Null when unknown. */
  readonly attitude: Quat | null;
  /** The vessel's docking ports. Null when it has none or they are unknown. */
  readonly docking: Docking | null;
  /** What kind of rock an asteroid or comet is. Null for every vessel that is not a spaceObject. */
  readonly spaceObject: SpaceObject | null;
}

function checkVessel(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","registryCode","name","vesselType","location","loaded","controllable","comms","signalStrength","crewCapacity","crew","resources","mass","deltaV","attitude","docking","spaceObject"]);
  uint32Value(o.id, path + ".id");
  stringValue(o.registryCode, path + ".registryCode");
  stringValue(o.name, path + ".name");
  oneOf(o.vesselType, path + ".vesselType", VESSEL_TYPES);
  checkLocation(o.location, path + ".location");
  booleanValue(o.loaded, path + ".loaded");
  booleanValue(o.controllable, path + ".controllable");
  oneOf(o.comms, path + ".comms", COMMS_STATES);
  o.signalStrength === null || numberValue(o.signalStrength, path + ".signalStrength");
  int32Value(o.crewCapacity, path + ".crewCapacity");
  arrayOf(o.crew, path + ".crew", (item, itemPath) => checkCrewMember(item, itemPath));
  arrayOf(o.resources, path + ".resources", (item, itemPath) => checkResource(item, itemPath));
  numberValue(o.mass, path + ".mass");
  o.deltaV === null || checkDeltaV(o.deltaV, path + ".deltaV");
  o.attitude === null || checkQuat(o.attitude, path + ".attitude");
  o.docking === null || checkDocking(o.docking, path + ".docking");
  o.spaceObject === null || checkSpaceObject(o.spaceObject, path + ".spaceObject");
  return true;
}

export interface SpaceObject {
  readonly kind: SpaceObjectKind;
  /** Null when the save does not say. */
  readonly sizeClass: SizeClass | null;
}

function checkSpaceObject(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","sizeClass"]);
  oneOf(o.kind, path + ".kind", SPACE_OBJECT_KINDS);
  o.sizeClass === null || oneOf(o.sizeClass, path + ".sizeClass", SIZE_CLASS_VALUES);
  return true;
}

/** comet: the rock carries KSP's ModuleComet. asteroid: any other space object. */
export const SPACE_OBJECT_KINDS = ["asteroid","comet"] as const;
export type SpaceObjectKind = (typeof SPACE_OBJECT_KINDS)[number];

/** KSP's size class of an asteroid or comet (DiscoveryInfo.objectSize), lower case on the wire, a the smallest. Asteroids come as a to e; comets draw from the same letters on their own range. */
export const SIZE_CLASS_VALUES = ["a","b","c","d","e","f","g","h","i"] as const;
export type SizeClass = (typeof SIZE_CLASS_VALUES)[number];

/** The stock KSP vessel types. */
export const VESSEL_TYPES = ["debris","spaceObject","unknown","probe","relay","rover","lander","ship","plane","station","base","eva","flag","deployedScienceController","deployedSciencePart","droppedPart","deployedGroundPart"] as const;
export type VesselType = (typeof VESSEL_TYPES)[number];

/** Where a vessel is: on an orbit, or resting on a surface. */
export type Location = OrbitalLocation | SurfaceLocation;

function checkLocation(value: unknown, path: string): true {
  switch ((value as { kind?: unknown } | null)?.kind) {
    case "orbit":
      return checkOrbitalLocation(value, path);
    case "surface":
      return checkSurfaceLocation(value, path);
    default:
      return fail(path + ".kind", "\"orbit\" | \"surface\"");
  }
}

export interface OrbitalLocation {
  readonly kind: "orbit";
  readonly situation: OrbitalSituation;
  readonly orbit: Orbit;
}

function checkOrbitalLocation(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","situation","orbit"]);
  oneOf(o.situation, path + ".situation", ORBITAL_SITUATIONS);
  checkOrbit(o.orbit, path + ".orbit");
  return true;
}

export interface SurfaceLocation {
  readonly kind: "surface";
  readonly situation: SurfaceSituation;
  /** Name of the body the vessel rests on. */
  readonly body: string;
  /** Unit: rad. */
  readonly latitude: number;
  /** Unit: rad. */
  readonly longitude: number;
  /** Above sea level. Unit: m. */
  readonly altitude: number;
}

function checkSurfaceLocation(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","situation","body","latitude","longitude","altitude"]);
  oneOf(o.situation, path + ".situation", SURFACE_SITUATIONS);
  stringValue(o.body, path + ".body");
  numberValue(o.latitude, path + ".latitude");
  numberValue(o.longitude, path + ".longitude");
  numberValue(o.altitude, path + ".altitude");
  return true;
}

export const ORBITAL_SITUATIONS = ["flying","subOrbital","orbiting","escaping"] as const;
export type OrbitalSituation = (typeof ORBITAL_SITUATIONS)[number];

export const SURFACE_SITUATIONS = ["prelaunch","landed","splashed"] as const;
export type SurfaceSituation = (typeof SURFACE_SITUATIONS)[number];

/** link: connected to the network. los: CommNet is on and the vessel has no path home. none: the vessel cannot communicate (debris, flags). off: CommNet is disabled in this save. */
export const COMMS_STATES = ["link","los","none","off"] as const;
export type CommsState = (typeof COMMS_STATES)[number];

export interface Docking {
  readonly ports: readonly DockingPortInfo[];
}

function checkDocking(value: unknown, path: string): true {
  const o = objectValue(value, path, ["ports"]);
  arrayOf(o.ports, path + ".ports", (item, itemPath) => checkDockingPortInfo(item, itemPath));
  return true;
}

export interface DockingPortInfo {
  /** The port part's persistentId. */
  readonly id: number;
  /** The part's title, such as Clamp-O-Tron Docking Port. */
  readonly name: string;
  /** The stock node type, such as size1. Only equal sizes dock. */
  readonly size: string;
  /** Ready to dock: not docked, not disabled, not behind a closed shield. */
  readonly free: boolean;
}

function checkDockingPortInfo(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","name","size","free"]);
  uint32Value(o.id, path + ".id");
  stringValue(o.name, path + ".name");
  stringValue(o.size, path + ".size");
  booleanValue(o.free, path + ".free");
  return true;
}

export interface CrewMember {
  readonly name: string;
  /** Pilot, Engineer, Scientist, Tourist, or a trait a mod adds. */
  readonly trait: string;
  readonly level: number;
}

function checkCrewMember(value: unknown, path: string): true {
  const o = objectValue(value, path, ["name","trait","level"]);
  stringValue(o.name, path + ".name");
  stringValue(o.trait, path + ".trait");
  int32Value(o.level, path + ".level");
  return true;
}

export interface Resource {
  readonly name: string;
  /** Unit: u. */
  readonly amount: number;
  /** Unit: u. */
  readonly capacity: number;
}

function checkResource(value: unknown, path: string): true {
  const o = objectValue(value, path, ["name","amount","capacity"]);
  stringValue(o.name, path + ".name");
  numberValue(o.amount, path + ".amount");
  numberValue(o.capacity, path + ".capacity");
  return true;
}

export interface DeltaV {
  /** Vacuum delta-v of all stages. Unit: m/s. */
  readonly total: number;
  readonly source: DeltaVSource;
  /** Per-stage figures, highest stage index first (the stage that fires first). Empty when no per-stage estimate exists. */
  readonly stages: readonly StageDeltaV[];
}

function checkDeltaV(value: unknown, path: string): true {
  const o = objectValue(value, path, ["total","source","stages"]);
  numberValue(o.total, path + ".total");
  oneOf(o.source, path + ".source", DELTA_VSOURCES);
  arrayOf(o.stages, path + ".stages", (item, itemPath) => checkStageDeltaV(item, itemPath));
  return true;
}

export interface StageDeltaV {
  /** KSP stage index, matching CraftPart.stage. */
  readonly stage: number;
  /** Vacuum delta-v of this stage. Unit: m/s. */
  readonly deltaV: number;
  /** Thrust to weight at ignition against the body the vessel is at, null when the stage has no engine. */
  readonly twr: number | null;
  /** Full-throttle burn time, null when the stage has no engine. Unit: s. */
  readonly burnTime: number | null;
}

function checkStageDeltaV(value: unknown, path: string): true {
  const o = objectValue(value, path, ["stage","deltaV","twr","burnTime"]);
  int32Value(o.stage, path + ".stage");
  numberValue(o.deltaV, path + ".deltaV");
  o.twr === null || numberValue(o.twr, path + ".twr");
  o.burnTime === null || numberValue(o.burnTime, path + ".burnTime");
  return true;
}

/** A vessel's part layout. The server sends it when it first links a vessel and whenever the parts change (staging, docking, decoupling), not with every fleet snapshot, and resends every layout after a new connection's hello. A newer revision for the same vesselId replaces the older one. */
export interface CraftLayout {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "layout";
  readonly vesselId: number;
  /** Counts up by one each time this vessel's layout changes. */
  readonly revision: number;
  readonly parts: readonly CraftPart[];
  /** Server path of the simplified 3D model of this design, such as /art/craft/1a2b3c4d.json: a CraftModel document. Vessels built alike share one model. Null until exported. */
  readonly model: string | null;
}

function checkCraftLayout(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","vesselId","revision","parts","model"]);
  uint32Value(o.vesselId, path + ".vesselId");
  uint32Value(o.revision, path + ".revision");
  arrayOf(o.parts, path + ".parts", (item, itemPath) => checkCraftPart(item, itemPath));
  o.model === null || stringValue(o.model, path + ".model");
  return true;
}

/** One part, in the root part's frame: x right, y up the craft's axis, z forward. Positions are part origins. */
export interface CraftPart {
  /** KSP persistentId of the part. */
  readonly id: number;
  /** Part name, such as liquidEngine. */
  readonly name: string;
  readonly position: Vec3;
  readonly rotation: Quat;
  /** Full extents of the part's bounds along its own axes. */
  readonly size: Vec3;
  readonly category: PartCategory;
  /** KSP stage index (the part's inverseStage), -1 for a part that is never staged. */
  readonly stage: number;
}

function checkCraftPart(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","name","position","rotation","size","category","stage"]);
  uint32Value(o.id, path + ".id");
  stringValue(o.name, path + ".name");
  checkVec3(o.position, path + ".position");
  checkQuat(o.rotation, path + ".rotation");
  checkVec3(o.size, path + ".size");
  oneOf(o.category, path + ".category", PART_CATEGORYS);
  int32Value(o.stage, path + ".stage");
  return true;
}

export const PART_CATEGORYS = ["engine","tank","pod","decoupler","other"] as const;
export type PartCategory = (typeof PART_CATEGORYS)[number];

export interface Vec3 {
  /** Unit: m. */
  readonly x: number;
  /** Unit: m. */
  readonly y: number;
  /** Unit: m. */
  readonly z: number;
}

function checkVec3(value: unknown, path: string): true {
  const o = objectValue(value, path, ["x","y","z"]);
  numberValue(o.x, path + ".x");
  numberValue(o.y, path + ".y");
  numberValue(o.z, path + ".z");
  return true;
}

export interface Quat {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly w: number;
}

function checkQuat(value: unknown, path: string): true {
  const o = objectValue(value, path, ["x","y","z","w"]);
  numberValue(o.x, path + ".x");
  numberValue(o.y, path + ".y");
  numberValue(o.z, path + ".z");
  numberValue(o.w, path + ".w");
  return true;
}

/** stock: KSP's own calculation for a loaded vessel. estimate: FLTCOM's estimate from the saved parts of an unloaded vessel. */
export const DELTA_VSOURCES = ["stock","estimate"] as const;
export type DeltaVSource = (typeof DELTA_VSOURCES)[number];

/** Ask the server to plan an orbit change for one vessel or one group and answer with a briefing. */
export interface BriefRequest {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "brief";
  /** Chosen by the display; the briefing echoes it. */
  readonly requestId: number;
  /** Vessel.id of the vessel to fly; ignored when groupId is set. */
  readonly vesselId: number;
  /** Plan for every member of this group in one operation; null plans for vesselId alone. */
  readonly groupId: string | null;
  readonly change: OrbitChangeRequest;
  /** Operator's codename; null lets the server generate one. */
  readonly codename: string | null;
  /** Stage that flies every burn; null picks automatically, lowest stage first. */
  readonly pinnedStage: number | null;
}

function checkBriefRequest(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","requestId","vesselId","groupId","change","codename","pinnedStage"]);
  uint32Value(o.requestId, path + ".requestId");
  uint32Value(o.vesselId, path + ".vesselId");
  o.groupId === null || stringValue(o.groupId, path + ".groupId");
  checkOrbitChangeRequest(o.change, path + ".change");
  o.codename === null || stringValue(o.codename, path + ".codename");
  o.pinnedStage === null || int32Value(o.pinnedStage, path + ".pinnedStage");
  return true;
}

/** Approve a briefed operation. The server uploads it and flies it when its burns come due. */
export interface Approve {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "approve";
  readonly operationId: string;
  /** Final codename, possibly edited after the briefing. */
  readonly codename: string;
}

function checkApprove(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","operationId","codename"]);
  stringValue(o.operationId, path + ".operationId");
  stringValue(o.codename, path + ".codename");
  return true;
}

/** Cancel an operation. Before ignition it scrubs; during a burn it aborts and releases control. */
export interface Scrub {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "scrub";
  readonly operationId: string;
}

function checkScrub(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","operationId"]);
  stringValue(o.operationId, path + ".operationId");
  return true;
}

/** Resume an operation in Hold. */
export interface Resume {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "resume";
  readonly operationId: string;
}

function checkResume(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","operationId"]);
  stringValue(o.operationId, path + ".operationId");
  return true;
}

/** The operator's answer to an open prompt. */
export interface Answer {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "answer";
  readonly promptId: number;
  readonly accept: boolean;
}

function checkAnswer(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","promptId","accept"]);
  uint32Value(o.promptId, path + ".promptId");
  booleanValue(o.accept, path + ".accept");
  return true;
}

/** Answer to a brief request: the planned burns, the delta-v budget and the contingencies. */
export interface BriefingMessage {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "briefing";
  readonly requestId: number;
  /** Null when planning failed. */
  readonly operationId: string | null;
  readonly codename: string;
  readonly vesselId: number;
  /** The group the briefing covers, null for one vessel. */
  readonly groupId: string | null;
  readonly change: OrbitChangeRequest;
  /** Readable summary of the change, such as circularize at apoapsis. */
  readonly description: string;
  readonly burns: readonly BurnPlan[];
  /** Unit: m/s. */
  readonly requiredDeltaV: number;
  /** Unit: m/s. */
  readonly availableDeltaV: number;
  /** Spare delta-v as a share of required; null when nothing is required. */
  readonly margin: number | null;
  readonly stages: readonly StageLine[];
  readonly contingencies: readonly ContingencyNote[];
  /** False while any contingency is red. */
  readonly canApprove: boolean;
  /** Why no plan exists, null on success. */
  readonly error: string | null;
  /** The transfer windows searched for a transfer between planets, null otherwise. */
  readonly porkchop: PorkchopPlot | null;
  /** What each stage of each vessel does and when it fires in this operation, next stage first. */
  readonly stagePlan: readonly StagePlanLine[];
  /** Every automatic action the operation will take: staging, landing gear, fairings, panels. */
  readonly autoActions: readonly AutoAction[];
  /** The payloads, releases and carrier return of a constellation deployment, null otherwise. */
  readonly deployment: Deployment | null;
}

function checkBriefingMessage(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","requestId","operationId","codename","vesselId","groupId","change","description","burns","requiredDeltaV","availableDeltaV","margin","stages","contingencies","canApprove","error","porkchop","stagePlan","autoActions","deployment"]);
  uint32Value(o.requestId, path + ".requestId");
  o.operationId === null || stringValue(o.operationId, path + ".operationId");
  stringValue(o.codename, path + ".codename");
  uint32Value(o.vesselId, path + ".vesselId");
  o.groupId === null || stringValue(o.groupId, path + ".groupId");
  checkOrbitChangeRequest(o.change, path + ".change");
  stringValue(o.description, path + ".description");
  arrayOf(o.burns, path + ".burns", (item, itemPath) => checkBurnPlan(item, itemPath));
  numberValue(o.requiredDeltaV, path + ".requiredDeltaV");
  numberValue(o.availableDeltaV, path + ".availableDeltaV");
  o.margin === null || numberValue(o.margin, path + ".margin");
  arrayOf(o.stages, path + ".stages", (item, itemPath) => checkStageLine(item, itemPath));
  arrayOf(o.contingencies, path + ".contingencies", (item, itemPath) => checkContingencyNote(item, itemPath));
  booleanValue(o.canApprove, path + ".canApprove");
  o.error === null || stringValue(o.error, path + ".error");
  o.porkchop === null || checkPorkchopPlot(o.porkchop, path + ".porkchop");
  arrayOf(o.stagePlan, path + ".stagePlan", (item, itemPath) => checkStagePlanLine(item, itemPath));
  arrayOf(o.autoActions, path + ".autoActions", (item, itemPath) => checkAutoAction(item, itemPath));
  o.deployment === null || checkDeployment(o.deployment, path + ".deployment");
  return true;
}

/** Total delta-v of a transfer between planets over a grid of departure times and flight times, and the best window found. */
export interface PorkchopPlot {
  /** Departure time of the first column. Unit: s. */
  readonly departureStart: number;
  /** Unit: s. */
  readonly departureStep: number;
  /** Flight time of the first row. Unit: s. */
  readonly flightStart: number;
  /** Unit: s. */
  readonly flightStep: number;
  readonly columns: number;
  readonly rows: number;
  /** Row by row, rows of flight time and columns of departure time; 0 where no transfer exists. */
  readonly deltaV: readonly number[];
  /** Unit: s. */
  readonly bestDeparture: number;
  /** Unit: s. */
  readonly bestFlight: number;
  /** Unit: m/s. */
  readonly bestDeltaV: number;
}

function checkPorkchopPlot(value: unknown, path: string): true {
  const o = objectValue(value, path, ["departureStart","departureStep","flightStart","flightStep","columns","rows","deltaV","bestDeparture","bestFlight","bestDeltaV"]);
  numberValue(o.departureStart, path + ".departureStart");
  numberValue(o.departureStep, path + ".departureStep");
  numberValue(o.flightStart, path + ".flightStart");
  numberValue(o.flightStep, path + ".flightStep");
  int32Value(o.columns, path + ".columns");
  int32Value(o.rows, path + ".rows");
  arrayOf(o.deltaV, path + ".deltaV", (item, itemPath) => numberValue(item, itemPath));
  numberValue(o.bestDeparture, path + ".bestDeparture");
  numberValue(o.bestFlight, path + ".bestFlight");
  numberValue(o.bestDeltaV, path + ".bestDeltaV");
  return true;
}

/** Every operation the server knows and every open prompt. Sent on connect and on each change; the display replaces its state with each one. */
export interface Missions {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "missions";
  /** Unit: s. */
  readonly ut: number;
  readonly operations: readonly OperationStatus[];
  readonly prompts: readonly Prompt[];
}

function checkMissions(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","ut","operations","prompts"]);
  numberValue(o.ut, path + ".ut");
  arrayOf(o.operations, path + ".operations", (item, itemPath) => checkOperationStatus(item, itemPath)) && uniqueBy(o.operations as readonly Record<string, unknown>[], path + ".operations", "id");
  arrayOf(o.prompts, path + ".prompts", (item, itemPath) => checkPrompt(item, itemPath)) && uniqueBy(o.prompts as readonly Record<string, unknown>[], path + ".prompts", "id");
  return true;
}

/** What the orbit should become. */
export interface OrbitChangeRequest {
  readonly kind: ChangeKind;
  /** Target altitude above sea level for changeApoapsis, changePeriapsis, circularOrbit and spreadEvenly; the rise for raiseApoapsis; the apoapsis for launch, where null means the standard low orbit; otherwise null. Unit: m. */
  readonly altitude: number | null;
  /** Target periapsis above sea level for launch, null for a circular orbit at altitude; otherwise null. Unit: m. */
  readonly periapsis: number | null;
  /** Target inclination for changeInclination and launch, where null flies due east; for deployConstellation, the ring's inclination from 0 to 180 degrees, where null or 0 is the equatorial ring; otherwise null. Unit: rad. */
  readonly inclination: number | null;
  /** Longitude of the ascending node for launch: the launch then waits for the site to pass under that plane. For deployConstellation, the ring's ascending node, null meaning 0. Needs an inclination. Otherwise null. Unit: rad. */
  readonly lan: number | null;
  /** The body a transfer flies to, by name; the orbit there is circular at altitude, inclined by inclination when that is set. Otherwise null. */
  readonly body: string | null;
  /** The vessel a rendezvous meets, by persistentId; otherwise null. */
  readonly targetVesselId: number | null;
  /** The docking port on the target that a rendezvous reserves, by part persistentId; null reserves the whole target vessel. Otherwise null. */
  readonly targetPortId: number | null;
  /** Where land touches down; null for every other kind. */
  readonly landing: LandingTarget | null;
  /** true or null: FLTCOM stages in every burn and works landing gear, fairings, solar panels, antennas and radiators. false: it stages only where it did before (ascent, landing, a flamed-out node burn) and touches nothing else. */
  readonly autoSystems: boolean | null;
  /** What a deployConstellation's transfer stage does once its satellites are out; null picks burnUp where the body has air and the delta-v suffices, otherwise park. Null for every other kind. */
  readonly endOfLife: EndOfLife | null;
}

function checkOrbitChangeRequest(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","altitude","periapsis","inclination","lan","body","targetVesselId","targetPortId","landing","autoSystems","endOfLife"]);
  oneOf(o.kind, path + ".kind", CHANGE_KINDS);
  o.altitude === null || numberValue(o.altitude, path + ".altitude");
  o.periapsis === null || numberValue(o.periapsis, path + ".periapsis");
  o.inclination === null || numberValue(o.inclination, path + ".inclination");
  o.lan === null || numberValue(o.lan, path + ".lan");
  o.body === null || stringValue(o.body, path + ".body");
  o.targetVesselId === null || uint32Value(o.targetVesselId, path + ".targetVesselId");
  o.targetPortId === null || uint32Value(o.targetPortId, path + ".targetPortId");
  o.landing === null || checkLandingTarget(o.landing, path + ".landing");
  o.autoSystems === null || booleanValue(o.autoSystems, path + ".autoSystems");
  o.endOfLife === null || oneOf(o.endOfLife, path + ".endOfLife", END_OF_LIFES);
  return true;
}

/** raiseApoapsis: raise every apoapsis by altitude metres. spreadEvenly: one circular orbit at altitude with the vessels equally spaced around it. launch: fly a vessel on the launch pad to orbit. transfer: fly one vessel to a circular orbit at altitude around body, at inclination there when set. rendezvous: close on targetVesselId and hold within 50 m. dock: rendezvous, then dock with RCS to targetPortId, or the best free port when it is null. land: deorbit and touch down at landing. deployConstellation: release every payload of the carrier onto one circle at altitude around the body it orbits, in the plane set by inclination and lan (equatorial when inclination is null), evenly spaced, then bring the carrier home. */
export const CHANGE_KINDS = ["circularize","changeApoapsis","changePeriapsis","changeInclination","circularOrbit","standardLowOrbit","raiseApoapsis","spreadEvenly","launch","transfer","rendezvous","dock","land","deployConstellation"] as const;
export type ChangeKind = (typeof CHANGE_KINDS)[number];

/** Where a landing touches down. launchPad and runway are the KSC spots on the home planet, and their coordinates are fixed by the server; custom uses latitude and longitude on the vessel's body. */
export interface LandingTarget {
  readonly site: LandingSite;
  /** Unit: rad. */
  readonly latitude: number;
  /** East-positive. Unit: rad. */
  readonly longitude: number;
}

function checkLandingTarget(value: unknown, path: string): true {
  const o = objectValue(value, path, ["site","latitude","longitude"]);
  oneOf(o.site, path + ".site", LANDING_SITES);
  numberValue(o.latitude, path + ".latitude");
  numberValue(o.longitude, path + ".longitude");
  return true;
}

export interface MarkerList {
  readonly items: readonly SurfaceMarker[];
}

function checkMarkerList(value: unknown, path: string): true {
  const o = objectValue(value, path, ["items"]);
  arrayOf(o.items, path + ".items", (item, itemPath) => checkSurfaceMarker(item, itemPath)) && uniqueBy(o.items as readonly Record<string, unknown>[], path + ".items", "id");
  return true;
}

/** A spot the game marks on a body's surface, offered as a landing target. */
export interface SurfaceMarker {
  /** Stable key, such as site:launchPad, flag:1234 or waypoint:<guid>. */
  readonly id: string;
  readonly name: string;
  readonly kind: SurfaceMarkerKind;
  /** Name of the body the marker is on. */
  readonly body: string;
  /** Unit: rad. */
  readonly latitude: number;
  /** East-positive, in (-π, π]. Unit: rad. */
  readonly longitude: number;
}

function checkSurfaceMarker(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","name","kind","body","latitude","longitude"]);
  stringValue(o.id, path + ".id");
  stringValue(o.name, path + ".name");
  oneOf(o.kind, path + ".kind", SURFACE_MARKER_KINDS);
  stringValue(o.body, path + ".body");
  numberValue(o.latitude, path + ".latitude");
  numberValue(o.longitude, path + ".longitude");
  return true;
}

/** waypoint: a custom waypoint the player placed. contract: a contract's waypoint. flag: a planted flag. vessel: a landed or splashed vessel. site: the launch pad or the runway on the home planet, with id site:launchPad or site:runway. */
export const SURFACE_MARKER_KINDS = ["waypoint","contract","flag","vessel","site"] as const;
export type SurfaceMarkerKind = (typeof SURFACE_MARKER_KINDS)[number];

export const LANDING_SITES = ["launchPad","runway","custom"] as const;
export type LandingSite = (typeof LANDING_SITES)[number];

/** Where and when the descent is predicted to touch down. */
export interface LandingPrediction {
  /** Unit: rad. */
  readonly latitude: number;
  /** East-positive. Unit: rad. */
  readonly longitude: number;
  /** Unit: s. */
  readonly ut: number;
}

function checkLandingPrediction(value: unknown, path: string): true {
  const o = objectValue(value, path, ["latitude","longitude","ut"]);
  numberValue(o.latitude, path + ".latitude");
  numberValue(o.longitude, path + ".longitude");
  numberValue(o.ut, path + ".ut");
  return true;
}

/** One planned burn. Delta-v components are in m/s along prograde, orbit normal and radial-out at the node. */
export interface BurnPlan {
  readonly vesselId: number;
  readonly label: string;
  /** Universal time of the impulsive node. Unit: s. */
  readonly nodeUt: number;
  /** Ignition, so the burn is centred on the node in delta-v terms. Unit: s. */
  readonly startUt: number;
  /** Burn time from the vessel's thrust and mass. Unit: s. */
  readonly duration: number;
  /** Unit: m/s. */
  readonly prograde: number;
  /** Unit: m/s. */
  readonly normal: number;
  /** Unit: m/s. */
  readonly radial: number;
  /** Unit: m/s. */
  readonly deltaV: number;
  /** The planned orbit after this burn, with meanAnomaly at nodeUt, for drawing the plan. Set on every burn whose result the planner knows: each orbit change and transfer burn, a landing's deorbit, the orbit a launch aims for. Null on an ascent, a descent, and the earlier burns of an operation reloaded from a save. */
  readonly orbitAfter: Orbit | null;
}

function checkBurnPlan(value: unknown, path: string): true {
  const o = objectValue(value, path, ["vesselId","label","nodeUt","startUt","duration","prograde","normal","radial","deltaV","orbitAfter"]);
  uint32Value(o.vesselId, path + ".vesselId");
  stringValue(o.label, path + ".label");
  numberValue(o.nodeUt, path + ".nodeUt");
  numberValue(o.startUt, path + ".startUt");
  numberValue(o.duration, path + ".duration");
  numberValue(o.prograde, path + ".prograde");
  numberValue(o.normal, path + ".normal");
  numberValue(o.radial, path + ".radial");
  numberValue(o.deltaV, path + ".deltaV");
  o.orbitAfter === null || checkOrbit(o.orbitAfter, path + ".orbitAfter");
  return true;
}

export interface StageLine {
  readonly vesselId: number;
  readonly stage: number;
  /** Unit: m/s. */
  readonly deltaV: number;
  /** Delta-v of the planned burns this stage flies. Unit: m/s. */
  readonly assigned: number;
  /** Vacuum thrust-to-weight at the body's surface gravity. */
  readonly twr: number;
  /** Unit: s. */
  readonly burnTime: number;
  /** The stage cannot cover its assigned burns. */
  readonly insufficient: boolean;
}

function checkStageLine(value: unknown, path: string): true {
  const o = objectValue(value, path, ["vesselId","stage","deltaV","assigned","twr","burnTime","insufficient"]);
  uint32Value(o.vesselId, path + ".vesselId");
  int32Value(o.stage, path + ".stage");
  numberValue(o.deltaV, path + ".deltaV");
  numberValue(o.assigned, path + ".assigned");
  numberValue(o.twr, path + ".twr");
  numberValue(o.burnTime, path + ".burnTime");
  booleanValue(o.insufficient, path + ".insufficient");
  return true;
}

export interface StagePlanLine {
  readonly vesselId: number;
  /** KSP stage index, matching CraftPart.stage. */
  readonly stage: number;
  /** What firing the stage does, such as drop 2 boosters, light 1 engine. */
  readonly does: string;
  /** When it fires in this operation, such as at 1:12, kept, kept for landing. */
  readonly when: string;
  /** The operation fires this stage to reach its burns. */
  readonly fires: boolean;
  /** red: the stager will hold at this unsafe stage; amber: unsafe but never reached; null when safe. */
  readonly severity: Alert | null;
  /** Why the stage is unsafe, such as opens parachutes in vacuum; null when safe. */
  readonly warning: string | null;
}

function checkStagePlanLine(value: unknown, path: string): true {
  const o = objectValue(value, path, ["vesselId","stage","does","when","fires","severity","warning"]);
  uint32Value(o.vesselId, path + ".vesselId");
  int32Value(o.stage, path + ".stage");
  stringValue(o.does, path + ".does");
  stringValue(o.when, path + ".when");
  booleanValue(o.fires, path + ".fires");
  o.severity === null || oneOf(o.severity, path + ".severity", ALERTS);
  o.warning === null || stringValue(o.warning, path + ".warning");
  return true;
}

/** A constellation deployment: the carrier's working orbit, each payload, each release and the carrier's return. */
export interface Deployment {
  /** The circle the payloads end on; the requested altitude when it was refused. Unit: m. */
  readonly altitude: number;
  /** Where an orbit lasts one day of the body, null when its day is unknown. Unit: m. */
  readonly synchronousAltitude: number | null;
  /** When the altitude is refused: the highest one that works. Unit: m. */
  readonly suggestedAltitude: number | null;
  readonly method: DeployMethod;
  /** The carrier's orbit while it releases, meanAnomaly at the first release; null when refused. */
  readonly workingOrbit: Orbit | null;
  readonly payloads: readonly DeployPayload[];
  /** In release order. */
  readonly releases: readonly DeployRelease[];
  readonly returnPlan: CarrierReturnPlan;
  readonly launchStage: LaunchStagePlan;
}

function checkDeployment(value: unknown, path: string): true {
  const o = objectValue(value, path, ["altitude","synchronousAltitude","suggestedAltitude","method","workingOrbit","payloads","releases","returnPlan","launchStage"]);
  numberValue(o.altitude, path + ".altitude");
  o.synchronousAltitude === null || numberValue(o.synchronousAltitude, path + ".synchronousAltitude");
  o.suggestedAltitude === null || numberValue(o.suggestedAltitude, path + ".suggestedAltitude");
  oneOf(o.method, path + ".method", DEPLOY_METHODS);
  o.workingOrbit === null || checkOrbit(o.workingOrbit, path + ".workingOrbit");
  arrayOf(o.payloads, path + ".payloads", (item, itemPath) => checkDeployPayload(item, itemPath));
  arrayOf(o.releases, path + ".releases", (item, itemPath) => checkDeployRelease(item, itemPath));
  checkCarrierReturnPlan(o.returnPlan, path + ".returnPlan");
  checkLaunchStagePlan(o.launchStage, path + ".launchStage");
  return true;
}

/** resonant: the carrier flies an orbit whose period is (n-1)/n or (n+1)/n of the circle's and each payload circularizes itself. carrierPhasing: payloads without engines; the carrier circularizes and phases between releases. */
export const DEPLOY_METHODS = ["resonant","carrierPhasing"] as const;
export type DeployMethod = (typeof DEPLOY_METHODS)[number];

export interface DeployPayload {
  /** The payload's command part: its burns use this id until it is released and becomes a vessel. */
  readonly id: number;
  /** The name it gets once released, such as Relay Carrier Sat 1. */
  readonly name: string;
  /** Unit: kg. */
  readonly mass: number;
  /** Vacuum delta-v of its own engines and propellant. Unit: m/s. */
  readonly deltaV: number;
  /** What its own circularization needs; 0 when the carrier places it. Unit: m/s. */
  readonly circularizeDeltaV: number;
}

function checkDeployPayload(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","name","mass","deltaV","circularizeDeltaV"]);
  uint32Value(o.id, path + ".id");
  stringValue(o.name, path + ".name");
  numberValue(o.mass, path + ".mass");
  numberValue(o.deltaV, path + ".deltaV");
  numberValue(o.circularizeDeltaV, path + ".circularizeDeltaV");
  return true;
}

export interface DeployRelease {
  readonly payloadId: number;
  readonly name: string;
  /** Which pass of the working orbit, from 1. */
  readonly pass: number;
  /** When the carrier lets go. Unit: s. */
  readonly ut: number;
  /** Where the payload reaches the circle: its circularization node, or the release on the circle. Unit: s. */
  readonly nodeUt: number;
  /** apoapsis, periapsis, or circle. */
  readonly apsis: string;
  /** Signed angle from its even slot once every payload is out. Unit: rad. */
  readonly spacingError: number;
  readonly finalOrbit: Orbit;
}

function checkDeployRelease(value: unknown, path: string): true {
  const o = objectValue(value, path, ["payloadId","name","pass","ut","nodeUt","apsis","spacingError","finalOrbit"]);
  uint32Value(o.payloadId, path + ".payloadId");
  stringValue(o.name, path + ".name");
  int32Value(o.pass, path + ".pass");
  numberValue(o.ut, path + ".ut");
  numberValue(o.nodeUt, path + ".nodeUt");
  stringValue(o.apsis, path + ".apsis");
  numberValue(o.spacingError, path + ".spacingError");
  checkOrbit(o.finalOrbit, path + ".finalOrbit");
  return true;
}

export interface CarrierReturnPlan {
  readonly kind: CarrierReturnKind;
  /** Estimated delta-v of the return or disposal. Unit: m/s. */
  readonly deltaV: number;
  /** Delta-v the carrier has left once the payloads are out. Unit: m/s. */
  readonly available: number;
  readonly text: string;
  /** The carrier cannot come home and this is what it does instead. */
  readonly fallback: boolean;
}

function checkCarrierReturnPlan(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","deltaV","available","text","fallback"]);
  oneOf(o.kind, path + ".kind", CARRIER_RETURN_KINDS);
  numberValue(o.deltaV, path + ".deltaV");
  numberValue(o.available, path + ".available");
  stringValue(o.text, path + ".text");
  booleanValue(o.fallback, path + ".fallback");
  return true;
}

/** land: deorbit at home and come down on parachutes. transferAndLand: transfer home first. disposal: park on an orbit clear of the constellation. stay: stay on the release orbit. burnUp: lower the periapsis deep into the atmosphere so it burns up. */
export const CARRIER_RETURN_KINDS = ["land","transferAndLand","disposal","stay","burnUp"] as const;
export type CarrierReturnKind = (typeof CARRIER_RETURN_KINDS)[number];

/** The operator's pick for a transfer stage once its satellites are out. burnUp: lower the periapsis into the atmosphere. landHome: deorbit, or transfer home, and land on parachutes. park: a disposal orbit clear of the constellation. */
export const END_OF_LIFES = ["burnUp","landHome","park"] as const;
export type EndOfLife = (typeof END_OF_LIFES)[number];

/** The stage that lifted the stack: whether it splits off in the parking orbit and lands at the launch pad. */
export interface LaunchStagePlan {
  /** It splits off first and gets its own return operation once the satellites are out. */
  readonly comesHome: boolean;
  /** Its vessel name once split, such as Relay Carrier Launch Stage; null when none was found. */
  readonly name: string | null;
  /** Estimated deorbit delta-v. Unit: m/s. */
  readonly deltaV: number;
  /** Its own delta-v. Unit: m/s. */
  readonly available: number;
  /** What it does, or why it stays attached. */
  readonly text: string;
}

function checkLaunchStagePlan(value: unknown, path: string): true {
  const o = objectValue(value, path, ["comesHome","name","deltaV","available","text"]);
  booleanValue(o.comesHome, path + ".comesHome");
  o.name === null || stringValue(o.name, path + ".name");
  numberValue(o.deltaV, path + ".deltaV");
  numberValue(o.available, path + ".available");
  stringValue(o.text, path + ".text");
  return true;
}

export interface AutoAction {
  readonly vesselId: number;
  /** One automatic action, such as jettison 1 fairing above 63 km. */
  readonly text: string;
}

function checkAutoAction(value: unknown, path: string): true {
  const o = objectValue(value, path, ["vesselId","text"]);
  uint32Value(o.vesselId, path + ".vesselId");
  stringValue(o.text, path + ".text");
  return true;
}

export interface ContingencyNote {
  readonly severity: Alert;
  readonly vesselId: number;
  readonly text: string;
}

function checkContingencyNote(value: unknown, path: string): true {
  const o = objectValue(value, path, ["severity","vesselId","text"]);
  oneOf(o.severity, path + ".severity", ALERTS);
  uint32Value(o.vesselId, path + ".vesselId");
  stringValue(o.text, path + ".text");
  return true;
}

/** red blocks approval; amber warns. */
export const ALERTS = ["amber","red"] as const;
export type Alert = (typeof ALERTS)[number];

/** One operation and where it stands. */
export interface OperationStatus {
  readonly id: string;
  readonly codename: string;
  readonly change: OrbitChangeRequest;
  readonly description: string;
  readonly state: OperationState;
  readonly burns: readonly BurnPlan[];
  /** Index into burns of the burn now due or flying. */
  readonly burnIndex: number;
  /** Delta-v flown in the current burn. Unit: m/s. */
  readonly deltaVDone: number;
  /** Latest timeline note, such as a re-plan, a released control or a missed window. */
  readonly note: string | null;
  /** For land: the latest predicted touchdown point; otherwise null. */
  readonly landingPrediction: LandingPrediction | null;
  /** A red alert: the vessel is headed into the ground, or its landing would come down too hard; otherwise null. */
  readonly alarm: FlightAlarm | null;
  /** For land: the descent as last flown, once descent guidance has taken over; otherwise null. */
  readonly descent: DescentStatus | null;
}

function checkOperationStatus(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","codename","change","description","state","burns","burnIndex","deltaVDone","note","landingPrediction","alarm","descent"]);
  stringValue(o.id, path + ".id");
  stringValue(o.codename, path + ".codename");
  checkOrbitChangeRequest(o.change, path + ".change");
  stringValue(o.description, path + ".description");
  oneOf(o.state, path + ".state", OPERATION_STATES);
  arrayOf(o.burns, path + ".burns", (item, itemPath) => checkBurnPlan(item, itemPath));
  int32Value(o.burnIndex, path + ".burnIndex");
  numberValue(o.deltaVDone, path + ".deltaVDone");
  o.note === null || stringValue(o.note, path + ".note");
  o.landingPrediction === null || checkLandingPrediction(o.landingPrediction, path + ".landingPrediction");
  o.alarm === null || checkFlightAlarm(o.alarm, path + ".alarm");
  o.descent === null || checkDescentStatus(o.descent, path + ".descent");
  return true;
}

/** Where a descent stands: the guidance phase, height over the ground under the vessel, vertical speed and the predicted miss. */
export interface DescentStatus {
  readonly phase: DescentPhase;
  /** Unit: m. */
  readonly altitude: number;
  /** Negative while coming down. Unit: m/s. */
  readonly verticalSpeed: number;
  /** Predicted touchdown to target; null before the first prediction reaches the ground. Unit: m. */
  readonly miss: number | null;
}

function checkDescentStatus(value: unknown, path: string): true {
  const o = objectValue(value, path, ["phase","altitude","verticalSpeed","miss"]);
  oneOf(o.phase, path + ".phase", DESCENT_PHASES);
  numberValue(o.altitude, path + ".altitude");
  numberValue(o.verticalSpeed, path + ".verticalSpeed");
  o.miss === null || numberValue(o.miss, path + ".miss");
  return true;
}

/** correct: small burns to put the predicted touchdown on the target. coast: engines off. brake: the braking burn. final: the vertical touchdown. landed: on the ground. */
export const DESCENT_PHASES = ["correct","coast","brake","final","landed"] as const;
export type DescentPhase = (typeof DESCENT_PHASES)[number];

/** A red alert on an operation. impact: the coast comes down under the body's safe floor (the top of its air or its highest terrain) where no landing is planned; the operation holds. landing: a planned landing would touch down too hard, cannot brake, or can no longer be flown before its deorbit. */
export interface FlightAlarm {
  readonly kind: AlarmKind;
  readonly body: string;
  /** What is wrong, such as "on impact course with Mun, periapsis -56.7 km". */
  readonly text: string;
}

function checkFlightAlarm(value: unknown, path: string): true {
  const o = objectValue(value, path, ["kind","body","text"]);
  oneOf(o.kind, path + ".kind", ALARM_KINDS);
  stringValue(o.body, path + ".body");
  stringValue(o.text, path + ".text");
  return true;
}

export const ALARM_KINDS = ["impact","landing"] as const;
export type AlarmKind = (typeof ALARM_KINDS)[number];

/** Where an operation stands. hold: paused after a game load or a released control, waiting for resume. */
export const OPERATION_STATES = ["draft","briefed","approved","uploaded","waiting","executing","hold","done","missed","scrubbed","aborted"] as const;
export type OperationState = (typeof OPERATION_STATES)[number];

/** A question the server will not act on without the operator's answer. No answer means no action. */
export interface Prompt {
  readonly id: number;
  readonly kind: PromptKind;
  readonly operationId: string;
  readonly vesselId: number;
  readonly text: string;
  /** For warp: the universal time the warp would stop at. Unit: s. */
  readonly untilUt: number | null;
}

function checkPrompt(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","kind","operationId","vesselId","text","untilUt"]);
  uint32Value(o.id, path + ".id");
  oneOf(o.kind, path + ".kind", PROMPT_KINDS);
  stringValue(o.operationId, path + ".operationId");
  uint32Value(o.vesselId, path + ".vesselId");
  stringValue(o.text, path + ".text");
  o.untilUt === null || numberValue(o.untilUt, path + ".untilUt");
  return true;
}

/** warp: time warp to the next burn, or through every burn of a group operation. switch: switch to the vessel that burns next. fly: let FLTCOM fly the burn of the vessel the player is flying. */
export const PROMPT_KINDS = ["warp","switch","fly"] as const;
export type PromptKind = (typeof PROMPT_KINDS)[number];

/** A saved, named group of vessels that plans and flies as one unit. */
export interface Group {
  readonly id: string;
  readonly name: string;
  readonly vesselIds: readonly number[];
}

function checkGroup(value: unknown, path: string): true {
  const o = objectValue(value, path, ["id","name","vesselIds"]);
  stringValue(o.id, path + ".id");
  stringValue(o.name, path + ".name");
  arrayOf(o.vesselIds, path + ".vesselIds", (item, itemPath) => uint32Value(item, itemPath));
  return true;
}

/** Every saved group. Sent on connect and on each change; the display replaces its groups with each one. */
export interface GroupRoster {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "groups";
  readonly groups: readonly Group[];
}

function checkGroupRoster(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","groups"]);
  arrayOf(o.groups, path + ".groups", (item, itemPath) => checkGroup(item, itemPath)) && uniqueBy(o.groups as readonly Record<string, unknown>[], path + ".groups", "id");
  return true;
}

export const GROUP_ACTIONS = ["create","rename","delete","add","remove"] as const;
export type GroupAction = (typeof GROUP_ACTIONS)[number];

/** Create, rename or delete a group, or add or remove members. create uses name and vesselIds; rename uses groupId and name; add and remove use groupId and vesselIds. */
export interface GroupEdit {
  readonly protocolVersion: typeof PROTOCOL_VERSION;
  readonly type: "groupEdit";
  readonly action: GroupAction;
  readonly groupId: string | null;
  readonly name: string | null;
  readonly vesselIds: readonly number[];
}

function checkGroupEdit(value: unknown, path: string): true {
  const o = objectValue(value, path, ["protocolVersion","type","action","groupId","name","vesselIds"]);
  oneOf(o.action, path + ".action", GROUP_ACTIONS);
  o.groupId === null || stringValue(o.groupId, path + ".groupId");
  o.name === null || stringValue(o.name, path + ".name");
  arrayOf(o.vesselIds, path + ".vesselIds", (item, itemPath) => uint32Value(item, itemPath));
  return true;
}

export type ParseError =
  | { readonly kind: "versionMismatch"; readonly received: number }
  | { readonly kind: "malformed"; readonly path: string; readonly expected: string };

export type Parsed<M> = { readonly ok: true; readonly message: M } | { readonly ok: false; readonly error: ParseError };

class Malformed {
  readonly path: string;
  readonly expected: string;
  constructor(path: string, expected: string) {
    this.path = path;
    this.expected = expected;
  }
}

function fail(path: string, expected: string): never {
  throw new Malformed(path, expected);
}

function objectValue(value: unknown, path: string, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return fail(path, "object");
  }
  for (const key in value) {
    if (!keys.includes(key)) {
      fail(path + "." + key, "no such field");
    }
  }
  return value as Record<string, unknown>;
}

function stringValue(value: unknown, path: string): true {
  return typeof value === "string" || fail(path, "string");
}

function numberValue(value: unknown, path: string): true {
  return Number.isFinite(value) || fail(path, "finite number");
}

function int32Value(value: unknown, path: string): true {
  return (Number.isInteger(value) && (value as number) >= -2147483648 && (value as number) <= 2147483647) || fail(path, "int32");
}

function uint32Value(value: unknown, path: string): true {
  return (Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 4294967295) || fail(path, "uint32");
}

function booleanValue(value: unknown, path: string): true {
  return typeof value === "boolean" || fail(path, "boolean");
}

function oneOf(value: unknown, path: string, values: readonly string[]): true {
  return values.includes(value as string) || fail(path, values.map((v) => JSON.stringify(v)).join(" | "));
}

function arrayOf(value: unknown, path: string, check: (item: unknown, itemPath: string) => true): true {
  if (!Array.isArray(value)) {
    return fail(path, "array");
  }
  for (let i = 0; i < value.length; i++) {
    check(value[i], path + "[" + i + "]");
  }
  return true;
}

function uniqueBy(items: readonly Record<string, unknown>[], path: string, key: string): true {
  const seen = new Set<unknown>();
  for (let i = 0; i < items.length; i++) {
    if (seen.has(items[i][key])) {
      fail(path + "[" + i + "]." + key, "unique value");
    }
    seen.add(items[i][key]);
  }
  return true;
}

function nestsTooDeep(text: string): boolean {
  let depth = 0;
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (inString) {
      if (c === 0x5c) {
        i++;
      } else if (c === 0x22) {
        inString = false;
      }
    } else if (c === 0x22) {
      inString = true;
    } else if (c === 0x7b || c === 0x5b) {
      if (++depth > MAX_DEPTH) {
        return true;
      }
    } else if (c === 0x7d || c === 0x5d) {
      depth--;
    }
  }
  return false;
}

function parseMessage(text: string, check: (value: unknown, path: string) => true): Parsed<unknown> {
  if (nestsTooDeep(text)) {
    return { ok: false, error: { kind: "malformed", path: "$", expected: "JSON nested at most " + MAX_DEPTH + " deep" } };
  }
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return { ok: false, error: { kind: "malformed", path: "$", expected: "JSON" } };
  }
  const received = (value as { protocolVersion?: unknown } | null)?.protocolVersion;
  if (!Number.isInteger(received)) {
    return { ok: false, error: { kind: "malformed", path: "$.protocolVersion", expected: "int32" } };
  }
  if (received !== PROTOCOL_VERSION) {
    return { ok: false, error: { kind: "versionMismatch", received: received as number } };
  }
  try {
    check(value, "$");
  } catch (error) {
    if (error instanceof Malformed) {
      return { ok: false, error: { kind: "malformed", path: error.path, expected: error.expected } };
    }
    throw error;
  }
  return { ok: true, message: value };
}
