import { PROTOCOL_VERSION, parseServerMessage } from "../../display/src/protocol.gen.ts";

const DEG = Math.PI / 180;

const BODIES = [
  { name: "Sun", radius: 261600000, gravParameter: 1172332800000000000, sphereOfInfluence: null, atmosphereHeight: 600000, rotationPeriod: 432000, color: "#ffd84a" },
  { name: "Moho", radius: 250000, gravParameter: 168609380000, sphereOfInfluence: 9646663, atmosphereHeight: 0, rotationPeriod: 1210000, color: "#8c6b4f", orbit: { parent: "Sun", semiMajorAxis: 5263138304, eccentricity: 0.2, inclinationDeg: 7, lanDeg: 70, argPeDeg: 15, meanAnomalyRad: 3.14 } },
  { name: "Eve", radius: 700000, gravParameter: 8171730200000, sphereOfInfluence: 85109365, atmosphereHeight: 90000, rotationPeriod: 80500, color: "#8a4fc9", orbit: { parent: "Sun", semiMajorAxis: 9832684544, eccentricity: 0.01, inclinationDeg: 2.1, lanDeg: 15, argPeDeg: 0, meanAnomalyRad: 3.14 } },
  { name: "Gilly", radius: 13000, gravParameter: 8289449.8, sphereOfInfluence: 126123, atmosphereHeight: 0, rotationPeriod: 28255, color: "#a08f7a", orbit: { parent: "Eve", semiMajorAxis: 31500000, eccentricity: 0.55, inclinationDeg: 12, lanDeg: 80, argPeDeg: 10, meanAnomalyRad: 0.9 } },
  { name: "Kerbin", radius: 600000, gravParameter: 3531600000000, sphereOfInfluence: 84159286, atmosphereHeight: 70000, rotationPeriod: 21549.425, color: "#4f8fd6", orbit: { parent: "Sun", semiMajorAxis: 13599840256, eccentricity: 0, inclinationDeg: 0, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 3.14 } },
  { name: "Mun", radius: 200000, gravParameter: 65138398000, sphereOfInfluence: 2429559, atmosphereHeight: 0, rotationPeriod: 138984.38, color: "#9a9a9a", orbit: { parent: "Kerbin", semiMajorAxis: 12000000, eccentricity: 0, inclinationDeg: 0, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 1.7 } },
  { name: "Minmus", radius: 60000, gravParameter: 1765800000, sphereOfInfluence: 2247428, atmosphereHeight: 0, rotationPeriod: 40400, color: "#9fe0c8", orbit: { parent: "Kerbin", semiMajorAxis: 47000000, eccentricity: 0, inclinationDeg: 6, lanDeg: 78, argPeDeg: 38, meanAnomalyRad: 0.9 } },
  { name: "Duna", radius: 320000, gravParameter: 301363210000, sphereOfInfluence: 47921949, atmosphereHeight: 50000, rotationPeriod: 65517.859, color: "#c4553a", orbit: { parent: "Sun", semiMajorAxis: 20726155264, eccentricity: 0.051, inclinationDeg: 0.06, lanDeg: 135.5, argPeDeg: 0, meanAnomalyRad: 3.14 } },
  { name: "Ike", radius: 130000, gravParameter: 18568369000, sphereOfInfluence: 1049599, atmosphereHeight: 0, rotationPeriod: 65517.862, color: "#7d7d7d", orbit: { parent: "Duna", semiMajorAxis: 3200000, eccentricity: 0.03, inclinationDeg: 0.2, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 1.7 } },
  { name: "Dres", radius: 138000, gravParameter: 21484489000, sphereOfInfluence: 32832840, atmosphereHeight: 0, rotationPeriod: 34800, color: "#9b8f86", orbit: { parent: "Sun", semiMajorAxis: 40839348203, eccentricity: 0.145, inclinationDeg: 5, lanDeg: 280, argPeDeg: 90, meanAnomalyRad: 3.14 } },
  { name: "Jool", radius: 6000000, gravParameter: 282528000000000, sphereOfInfluence: 2455985200, atmosphereHeight: 200000, rotationPeriod: 36000, color: "#6fbf3a", orbit: { parent: "Sun", semiMajorAxis: 68773560320, eccentricity: 0.05, inclinationDeg: 1.304, lanDeg: 52, argPeDeg: 0, meanAnomalyRad: 0.1 } },
  { name: "Laythe", radius: 500000, gravParameter: 1962000000000, sphereOfInfluence: 3723646, atmosphereHeight: 50000, rotationPeriod: 52980.879, color: "#3d5fa8", orbit: { parent: "Jool", semiMajorAxis: 27184000, eccentricity: 0, inclinationDeg: 0, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 3.14 } },
  { name: "Vall", radius: 300000, gravParameter: 207481500000, sphereOfInfluence: 2406401, atmosphereHeight: 0, rotationPeriod: 105962.09, color: "#a6c6d9", orbit: { parent: "Jool", semiMajorAxis: 43152000, eccentricity: 0, inclinationDeg: 0, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 0.9 } },
  { name: "Tylo", radius: 600000, gravParameter: 2825280000000, sphereOfInfluence: 10856518, atmosphereHeight: 0, rotationPeriod: 211926.36, color: "#d8c8b0", orbit: { parent: "Jool", semiMajorAxis: 68500000, eccentricity: 0, inclinationDeg: 0.025, lanDeg: 0, argPeDeg: 0, meanAnomalyRad: 3.14 } },
  { name: "Bop", radius: 65000, gravParameter: 2486834900, sphereOfInfluence: 1221061, atmosphereHeight: 0, rotationPeriod: 544507.43, color: "#8a7458", orbit: { parent: "Jool", semiMajorAxis: 128500000, eccentricity: 0.235, inclinationDeg: 15, lanDeg: 10, argPeDeg: 25, meanAnomalyRad: 0.9 } },
  { name: "Pol", radius: 44000, gravParameter: 721702080, sphereOfInfluence: 1042139, atmosphereHeight: 0, rotationPeriod: 901902.62, color: "#c9b45a", orbit: { parent: "Jool", semiMajorAxis: 179890000, eccentricity: 0.171, inclinationDeg: 4.25, lanDeg: 2, argPeDeg: 15, meanAnomalyRad: 0.9 } },
  { name: "Eeloo", radius: 210000, gravParameter: 74410815000, sphereOfInfluence: 119082940, atmosphereHeight: 0, rotationPeriod: 19460, color: "#d9d9d9", orbit: { parent: "Sun", semiMajorAxis: 90118820000, eccentricity: 0.26, inclinationDeg: 6.15, lanDeg: 50, argPeDeg: 260, meanAnomalyRad: 3.14 } },
];

const HIGHEST_TERRAIN = { Sun: 0, Moho: 6817, Eve: 7526, Gilly: 6401, Kerbin: 6767, Mun: 7061, Minmus: 5725, Duna: 8264, Ike: 12738, Dres: 5700, Jool: 0, Laythe: 6079, Vall: 7985, Tylo: 11290, Bop: 21757, Pol: 5590, Eeloo: 3874 };

const RADIUS = new Map(BODIES.map((body) => [body.name, body.radius]));

const UT = 5184000;

function bodies() {
  return BODIES.map(({ orbit: elements, ...body }) => ({
    name: body.name,
    orbit: elements ? orbit(elements.parent, elements.semiMajorAxis, elements.eccentricity, elements.inclinationDeg * DEG, elements.lanDeg * DEG, elements.argPeDeg * DEG, elements.meanAnomalyRad) : null,
    radius: body.radius,
    gravParameter: body.gravParameter,
    sphereOfInfluence: body.sphereOfInfluence,
    atmosphereHeight: body.atmosphereHeight,
    rotationPeriod: body.rotationPeriod,
    rotationAngle: ((2 * Math.PI * UT) / body.rotationPeriod) % (2 * Math.PI),
    highestTerrainAltitude: HIGHEST_TERRAIN[body.name],
    color: body.color,
    texture: body.name === "Sun" ? null : `/art/body/${body.name}.png`,
  }));
}

function orbit(referenceBody, semiMajorAxis, eccentricity, inclination, longitudeOfAscendingNode, argumentOfPeriapsis, meanAnomaly) {
  return { referenceBody, semiMajorAxis, eccentricity, inclination, longitudeOfAscendingNode, argumentOfPeriapsis, meanAnomaly };
}

function orbitKm(body, periapsisKm, apoapsisKm, inclinationDeg, lanDeg, argPeDeg, meanAnomalyRad) {
  const rp = RADIUS.get(body) + periapsisKm * 1000;
  const ra = RADIUS.get(body) + apoapsisKm * 1000;
  return orbit(body, (rp + ra) / 2, (ra - rp) / (ra + rp), inclinationDeg * DEG, lanDeg * DEG, argPeDeg * DEG, meanAnomalyRad);
}

const stageDv = (stage, deltaV, twr, burnTime) => ({ stage, deltaV, twr, burnTime });
const dv = (total, source) => ({ total, source, stages: [stageDv(0, total, 1.2, Math.round(total / 9.2))] });

const part = (id, name, category, stage, x, y, sx, sy, sz = sx) => ({ id, name, position: { x, y, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: sx, y: sy, z: sz }, category, stage });

// Craft frame: y up the axis, root part at the origin.
const SINGLE_STAGE = [
  part(1, "probeCoreOcto", "pod", 0, 0, 0, 0.9, 0.4),
  part(2, "fuelTank", "tank", 0, 0, -0.9, 1.25, 1.1),
  part(3, "liquidEngine", "engine", 0, 0, -1.9, 1.25, 1.0),
  part(4, "solarPanels5", "other", 0, 0.9, 0, 0.9, 0.1),
  part(5, "solarPanels5", "other", 0, -0.9, 0, 0.9, 0.1),
];

const TWO_STAGE = [
  part(11, "mk1pod", "pod", 2, 0, 0, 1.25, 1.5),
  part(12, "decoupler1-2", "decoupler", 1, 0, -0.95, 1.25, 0.2),
  part(13, "fuelTank", "tank", 1, 0, -1.9, 1.25, 1.6),
  part(14, "liquidEngine", "engine", 1, 0, -3.1, 1.25, 0.9),
  part(15, "decoupler1-2", "decoupler", 0, 0, -3.7, 1.25, 0.2),
  part(16, "fuelTank", "tank", 0, 0, -5.0, 1.25, 2.4),
  part(17, "fuelTank", "tank", 0, 0, -7.4, 1.25, 2.4),
  part(18, "liquidEngine", "engine", 0, 0, -9.0, 1.25, 0.9),
  part(19, "radialEngineBody", "engine", 0, 1.3, -8.4, 0.4, 0.8),
  part(20, "radialEngineBody", "engine", 0, -1.3, -8.4, 0.4, 0.8),
  part(21, "fuelTank", "tank", 0, 1.3, -6.0, 0.8, 2.6),
  part(22, "fuelTank", "tank", 0, -1.3, -6.0, 0.8, 2.6),
];

const STATION = [
  part(31, "dockingPort2", "other", -1, 0, 1.6, 0.9, 0.4),
  part(32, "crewCabin", "pod", -1, 0, 0, 2.5, 2.6),
  part(33, "fuelTank", "tank", -1, 0, -2.4, 2.5, 2.2),
  part(34, "largeSolarPanel", "other", -1, 3.4, -0.2, 4.2, 0.1, 1.4),
  part(35, "largeSolarPanel", "other", -1, -3.4, -0.2, 4.2, 0.1, 1.4),
  part(36, "crewCabin", "pod", -1, 0, -4.8, 2.5, 2.6),
  part(37, "dockingPort2", "other", -1, 0, -6.4, 0.9, 0.4),
];

const LAYOUTS = { 1101: STATION, 1102: SINGLE_STAGE, 1104: SINGLE_STAGE, 1105: TWO_STAGE, 1107: SINGLE_STAGE, 1201: SINGLE_STAGE, 1202: SINGLE_STAGE, 1203: SINGLE_STAGE, 1204: SINGLE_STAGE, 1210: TWO_STAGE, 1211: SINGLE_STAGE, 1212: SINGLE_STAGE, 1213: SINGLE_STAGE, 1306: TWO_STAGE };
const DESIGNS = new Map([
  [STATION, "station"],
  [SINGLE_STAGE, "single-stage"],
  [TWO_STAGE, "two-stage"],
]);

/** Hand-made craft models for the mock server: each part as an eight-sided prism, in the attitude craft frame (y and z swapped). */
export function craftModel(design) {
  const parts = [...DESIGNS].find(([, name]) => name === design)?.[0];
  if (!parts) {
    return null;
  }
  const positions = [];
  const indices = [];
  const SIDES = 8;
  for (const { position, size } of parts) {
    const start = positions.length / 3;
    for (const end of [-0.5, 0.5]) {
      for (let k = 0; k < SIDES; k++) {
        const angle = (k / SIDES) * 2 * Math.PI;
        positions.push(position.x + (Math.cos(angle) * size.x) / 2, position.z + (Math.sin(angle) * size.z) / 2, position.y + end * size.y);
      }
    }
    for (let k = 0; k < SIDES; k++) {
      const next = (k + 1) % SIDES;
      indices.push(start + k, start + next, start + SIDES + k, start + next, start + SIDES + next, start + SIDES + k);
      if (k > 0 && k < SIDES - 1) {
        indices.push(start, start + k + 1, start + k, start + SIDES, start + SIDES + k, start + SIDES + k + 1);
      }
    }
  }
  return { positions: positions.map((value) => Math.round(value * 1000) / 1000), indices };
}

/** A fixed, made-up attitude per vessel so the map shows craft turned different ways. */
function attitudeOf(id) {
  const yaw = (id * 0.731) % (2 * Math.PI);
  const pitch = ((id * 0.377) % 1.6) - 0.8;
  const cy = Math.cos(yaw / 2);
  const sy = Math.sin(yaw / 2);
  const cp = Math.cos(pitch / 2);
  const sp = Math.sin(pitch / 2);
  const round = (value) => Math.round(value * 1e6) / 1e6;
  return { x: round(cy * sp), y: round(sy * sp), z: round(sy * cp), w: round(cy * cp) };
}

const TAGS = { station: "STN", relay: "RLY", lander: "LDR", probe: "PRB", ship: "SHP", debris: "DBR", rover: "RVR", base: "BAS", plane: "PLN", eva: "EVA", flag: "FLG", spaceObject: "AST" };

const RESOURCE_SETS = {
  station: [["ElectricCharge", 4000], ["MonoPropellant", 750], ["LiquidFuel", 1800], ["Oxidizer", 2200], ["Ore", 1500], ["XenonGas", 5700]],
  relay: [["ElectricCharge", 1200], ["MonoPropellant", 50]],
  lander: [["ElectricCharge", 600], ["LiquidFuel", 360], ["Oxidizer", 440], ["MonoPropellant", 120]],
  probe: [["ElectricCharge", 300], ["MonoPropellant", 30], ["XenonGas", 720]],
  ship: [["ElectricCharge", 1000], ["LiquidFuel", 2880], ["Oxidizer", 3520], ["MonoPropellant", 200], ["SolidFuel", 820]],
  rover: [["ElectricCharge", 800]],
  base: [["ElectricCharge", 6000], ["Ore", 3000], ["LiquidFuel", 900], ["Oxidizer", 1100]],
  plane: [["ElectricCharge", 400], ["LiquidFuel", 1200], ["IntakeAir", 12]],
  debris: [],
  eva: [["EVA Propellant", 5]],
  flag: [],
  spaceObject: [],
};

/** Stations carry two Clamp-O-Tron ports and a Senior; ships one Clamp-O-Tron; nothing else docks. */
const PORTS = { station: ["size1", "size1", "size2"], ship: ["size1"] };
const PORT_NAMES = { size1: "Clamp-O-Tron Docking Port", size2: "Clamp-O-Tron Sr. Docking Port" };
const dockingOf = (id, vesselType) =>
  PORTS[vesselType] ? { ports: PORTS[vesselType].map((size, index) => ({ id: id * 10 + index + 1, name: PORT_NAMES[size], size, free: true })) } : null;

const TRAITS = ["Pilot", "Engineer", "Scientist", "Tourist"];
const FIRST = ["Jebediah", "Bill", "Bob", "Valentina", "Gene", "Wernher", "Lodan", "Herdun", "Mortimer", "Ludo", "Sigrid", "Dilsby"];

function vessel({ id, name, vesselType, location, loaded = false, comms = "link", crew = [], crewCapacity = crew.length, fill = 0.7, deltaV, mass, spaceObject = null }) {
  const resources = RESOURCE_SETS[vesselType].map(([resource, capacity]) => ({ name: resource, amount: Math.round(capacity * fill * 100) / 100, capacity }));
  return {
    id,
    registryCode: `FLT-${String(id % 10000).padStart(4, "0")}-${TAGS[vesselType]}`,
    name,
    vesselType,
    location,
    loaded,
    controllable: !["debris", "flag", "spaceObject"].includes(vesselType),
    comms,
    signalStrength: comms === "link" ? 0.82 : null,
    crewCapacity,
    crew,
    resources,
    mass,
    deltaV,
    attitude: attitudeOf(id),
    docking: dockingOf(id, vesselType),
    spaceObject,
  };
}

const orbiting = (situation, orbitValue) => ({ kind: "orbit", situation, orbit: orbitValue });
const surface = (situation, body, latitudeDeg, longitudeDeg, altitude) => ({ kind: "surface", situation, body, latitude: latitudeDeg * DEG, longitude: longitudeDeg * DEG, altitude });
const crewMember = (name, trait, level) => ({ name, trait, level });

const marker = (id, name, kind, body, latitudeDeg, longitudeDeg) => ({ id, name, kind, body, latitude: latitudeDeg * DEG, longitude: longitudeDeg * DEG });

/** What the plugin collects from the game: the KSC spots, a custom and a contract waypoint, a planted flag and the landed lander. */
const KERBIN_SMALL_MARKERS = [
  marker("site:launchPad", "KSC Launch Pad", "site", "Kerbin", -0.09694444, -74.5575),
  marker("site:runway", "KSC Runway", "site", "Kerbin", -0.0493916, -74.60864),
  marker("waypoint:5f1c0a2e", "Crater Rim", "waypoint", "Mun", 8.4, 23.6),
  marker("contract:9b7d41c3", "Site Gamma Mun", "contract", "Mun", -14.2, 112.5),
  marker("flag:1190", "First Footprint", "flag", "Mun", 0.67, -38.9),
  marker("vessel:1103", "Mun Lander 2", "vessel", "Mun", 3.2, -51.7),
];

function fleet(vessels, activeVesselId, markers = null) {
  return { protocolVersion: PROTOCOL_VERSION, type: "fleet", seq: 0, ut: UT, warpRate: 1, activeVesselId, bodies: bodies(), vessels, markers: markers && { items: markers } };
}

function kerbinSmall() {
  return fleet(
    [
      vessel({ id: 1101, name: "Kerbin Station Alpha", vesselType: "station", location: orbiting("orbiting", orbitKm("Kerbin", 248, 252, 0.5, 30, 10, 0.4)), loaded: true, crew: [crewMember("Jebediah Kerman", "Pilot", 5), crewMember("Bill Kerman", "Engineer", 3), crewMember("Bob Kerman", "Scientist", 3)], crewCapacity: 6, mass: 48200, deltaV: dv(820, "stock") }),
      vessel({ id: 1102, name: "Comsat K1", vesselType: "relay", location: orbiting("orbiting", orbitKm("Kerbin", 2863, 2864, 0, 0, 0, 2.1)), mass: 1250, deltaV: dv(410, "estimate") }),
      vessel({ id: 1103, name: "Mun Lander 2", vesselType: "lander", location: surface("landed", "Mun", 3.2, -51.7, 1840), crew: [crewMember("Valentina Kerman", "Pilot", 4)], crewCapacity: 2, mass: 6400, deltaV: dv(1210, "estimate") }),
      vessel({ id: 1104, name: "Minmus Prospector", vesselType: "probe", location: orbiting("orbiting", orbitKm("Minmus", 29, 31, 4, 120, 45, 5.2)), mass: 980, deltaV: dv(640, "estimate") }),
      vessel({ id: 1105, name: "Kerbal X", vesselType: "ship", location: surface("prelaunch", "Kerbin", -0.0972, -74.5577, 67), crew: [crewMember("Wernher Kerman", "Pilot", 1)], crewCapacity: 1, fill: 1, mass: 132600, deltaV: { total: 4820, source: "stock", stages: [stageDv(1, 2900, 1.45, 187), stageDv(0, 1920, 0.92, 164)] } }),
      vessel({ id: 1106, name: "Ascent Stage Debris", vesselType: "debris", location: orbiting("orbiting", orbitKm("Kerbin", 80, 400, 1.2, 290, 75, 1.1)), comms: "none", mass: 3100, deltaV: null }),
      vessel({ id: 1107, name: "Mun Survey", vesselType: "probe", location: orbiting("orbiting", orbitKm("Mun", 40, 90, 85, 15, 200, 0.3)), mass: 720, deltaV: dv(380, "estimate") }),
    ],
    1101,
    KERBIN_SMALL_MARKERS,
  );
}

function relayRing() {
  const base = kerbinSmall();
  const relays = [
    [1201, "Relay R1", 690, 735, 0.35],
    [1202, "Relay R2", 702, 748, 0.9],
    [1203, "Relay R3", 695, 760, 1.25],
    [1204, "Relay R4", 708, 742, 2.6],
  ].map(([id, name, pe, ap, anomaly]) => vessel({ id, name, vesselType: "relay", location: orbiting("orbiting", orbitKm("Kerbin", pe, ap, 0.2, 0, 0, anomaly)), fill: 1, mass: 1100, deltaV: dv(620, "estimate") }));
  return { ...base, vessels: [base.vessels[0], base.vessels[2], base.vessels[4], ...relays] };
}

/** Just after a polar carrier deployment: three relays evenly spaced on one polar ring, and the empty carrier near the low point of its resonant orbit. */
function polarRing() {
  const base = kerbinSmall();
  const relays = [
    [1211, "Polar Relay 1", 0.3],
    [1212, "Polar Relay 2", 0.3 + (2 * Math.PI) / 3],
    [1213, "Polar Relay 3", 0.3 + (4 * Math.PI) / 3],
  ].map(([id, name, anomaly]) => vessel({ id, name, vesselType: "relay", location: orbiting("orbiting", orbitKm("Kerbin", 1500, 1500, 90, 40, 0, anomaly)), fill: 0.9, mass: 680, deltaV: dv(760, "estimate") }));
  const carrier = vessel({ id: 1210, name: "Ring Carrier", vesselType: "ship", location: orbiting("orbiting", orbitKm("Kerbin", 640, 1500, 90, 40, 180, 0.4)), fill: 0.4, mass: 3900, deltaV: dv(1150, "estimate") });
  return { ...base, vessels: [carrier, ...relays], activeVesselId: 1210 };
}

function commsLost() {
  const base = kerbinSmall();
  const vessels = base.vessels.slice(0, 4).concat(
    vessel({ id: 1108, name: "Far Side Relay Probe", vesselType: "probe", location: orbiting("orbiting", orbitKm("Mun", 60, 62, 0, 0, 0, 3.14)), comms: "los", mass: 640, deltaV: dv(290, "estimate") }),
  );
  return { ...base, vessels, activeVesselId: 1108 };
}

/** A busy save zoomed out: ships piled on Kerbin, landers on the Mun, ships at Jool's moons, asteroids and a comet near Kerbin's orbit, two pieces of debris. */
function crowded() {
  const base = kerbinSmall();
  const random = mulberry32(15);
  const ship = (id, name, vesselType, body, pe, ap, inc, anomaly) =>
    vessel({ id, name, vesselType, location: orbiting("orbiting", orbitKm(body, pe, ap, inc, (id * 37) % 360, (id * 53) % 360, anomaly)), mass: 4200, deltaV: dv(900, "estimate") });
  const kerbinShips = [
    ship(1301, "Kerbin Tanker", "ship", "Kerbin", 180, 190, 0.2, 0.9),
    ship(1302, "Comsat K2", "relay", "Kerbin", 2860, 2866, 0, 4.2),
    ship(1303, "Comsat K3", "relay", "Kerbin", 2861, 2865, 0, 0.1),
    ship(1304, "Polar Scanner", "probe", "Kerbin", 300, 320, 88, 2.7),
    ship(1305, "Crew Shuttle", "ship", "Kerbin", 95, 110, 0.4, 5.6),
  ];
  const munLander = vessel({ id: 1306, name: "Mun Lander 1", vesselType: "lander", location: orbiting("orbiting", orbitKm("Mun", 14, 16, 2, 0, 0, 1.7)), crew: [crewMember("Lodan Kerman", "Pilot", 2)], crewCapacity: 2, mass: 5100, deltaV: dv(1430, "estimate") });
  const joolShips = [
    ship(1311, "Laythe Relay", "relay", "Laythe", 600, 620, 0, 1.2),
    ship(1312, "Vall Survey", "probe", "Vall", 90, 140, 30, 3.3),
    ship(1313, "Tylo Lander", "lander", "Tylo", 20, 25, 0, 0.4),
    ship(1314, "Jool Mothership", "ship", "Jool", 2800, 3100, 1, 2.2),
  ];
  const rock = (id, name, elements, kind, sizeClass) =>
    vessel({ id, name, vesselType: "spaceObject", comms: "none", mass: 150000, deltaV: null, location: orbiting("orbiting", elements), spaceObject: { kind, sizeClass } });
  const loose = () => orbit("Sun", 12.6e9 + random() * 2.0e9, random() * 0.08, random() * 8 * DEG, random() * 2 * Math.PI, random() * 2 * Math.PI, random() * 2 * Math.PI);
  // Three rocks of different classes and the comet a few km apart on one orbit, so one close view shows them together.
  const group = (step) => orbit("Sun", 13.9e9, 0.03, 2 * DEG, 0.8, 1.9, 2.4 + step * 1.2e-6);
  const LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ";
  const code = () => Array.from({ length: 3 }, () => LETTERS[Math.floor(random() * LETTERS.length)]).join("") + "-" + String(100 + Math.floor(random() * 900));
  const asteroids = Array.from({ length: 15 }, (_, i) => rock(1401 + i, `Ast. ${code()}`, i < 3 ? group(i) : loose(), "asteroid", i < 3 ? "ace"[i] : "abcde"[i % 5]));
  const comet = rock(1420, "C/2026 K1 (Comet)", group(3.4), "comet", "d");
  // Two rocks captured by Kerbin: one inside the Mun's orbit, one between the Mun and Minmus.
  const captured = [
    rock(1421, "Ast. KCA-101", orbitKm("Kerbin", 2200, 3400, 12, 70, 20, 1.1), "asteroid", "b"),
    rock(1422, "Ast. KCE-202", orbitKm("Kerbin", 20000, 30000, 4, 200, 90, 4.0), "asteroid", "e"),
  ];
  const debris = [
    vessel({ id: 1431, name: "Mun Lander 1 Debris", vesselType: "debris", location: orbiting("orbiting", orbitKm("Kerbin", 75, 600, 0.6, 210, 40, 3.9)), comms: "none", mass: 900, deltaV: null }),
    vessel({ id: 1432, name: "Mun Lander 1 Debris", vesselType: "debris", location: orbiting("orbiting", orbitKm("Mun", 8, 300, 2, 10, 0, 2.4)), comms: "none", mass: 600, deltaV: null }),
  ];
  const own = base.vessels.filter((v) => v.vesselType !== "debris");
  return { ...base, vessels: [...own, ...kerbinShips, munLander, ...joolShips, ...asteroids, comet, ...captured, ...debris], activeVesselId: 1306 };
}

function mulberry32(seed) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function fleet200() {
  const random = mulberry32(200);
  const pick = (list) => list[Math.floor(random() * list.length)];
  const round = (value, places) => Math.round(value * 10 ** places) / 10 ** places;
  const types = ["station", "relay", "lander", "probe", "ship", "debris", "rover", "base", "plane"];
  const homes = ["Kerbin", "Kerbin", "Kerbin", "Mun", "Minmus", "Duna", "Ike", "Eve", "Laythe", "Tylo", "Jool", "Dres", "Eeloo"];
  const vessels = [];
  for (let n = 0; n < 200; n++) {
    const vesselType = pick(types);
    const grounded = vesselType === "rover" || vesselType === "base" || (vesselType === "lander" && random() < 0.5);
    const body = pick(grounded ? homes.filter((home) => home !== "Jool") : homes);
    const location = grounded
      ? surface(pick(["Kerbin", "Eve", "Laythe"].includes(body) ? ["landed", "splashed"] : ["landed"]), body, round(random() * 160 - 80, 4), round(random() * 360 - 180, 4), round(random() * 3000, 1))
      : orbiting(
          "orbiting",
          orbitKm(body, round(70 + random() * 900, 3), round(1000 + random() * 4000, 3), round(random() * 90, 3), round(random() * 360, 3), round(random() * 360, 3), round(random() * 2 * Math.PI, 6)),
        );
    const crewed = ["station", "lander", "ship", "base", "rover", "plane"].includes(vesselType);
    const crew = crewed
      ? Array.from({ length: 1 + Math.floor(random() * 4) }, () => crewMember(`${pick(FIRST)} Kerman`, pick(TRAITS), Math.floor(random() * 6)))
      : [];
    vessels.push(
      vessel({
        id: 300000 + n * 7919,
        name: `${pick(["Horizon", "Vanguard", "Pathfinder", "Longhaul", "Sentinel", "Relay", "Outpost", "Survey"])} ${body} ${String(n + 1).padStart(3, "0")}`,
        vesselType,
        location,
        loaded: n === 0,
        comms: vesselType === "debris" ? "none" : random() < 0.1 ? "los" : "link",
        crew,
        crewCapacity: crewed ? crew.length + Math.floor(random() * 3) : 0,
        fill: round(random(), 2),
        mass: round(500 + random() * 120000, 1),
        deltaV: vesselType === "debris" ? null : dv(round(random() * 5000, 1), random() < 0.5 ? "stock" : "estimate"),
      }),
    );
  }
  return fleet(vessels, vessels[0].id);
}

export function layoutMessages(name) {
  const ids = new Set(FIXTURES[name]().vessels.map((vessel) => vessel.id));
  return Object.entries(LAYOUTS)
    .filter(([id]) => ids.has(Number(id)))
    .map(([id, parts]) => ({ protocolVersion: PROTOCOL_VERSION, type: "layout", vesselId: Number(id), revision: 1, parts, model: `/art/craft/${DESIGNS.get(parts)}.json` }));
}

export const FIXTURES = { "kerbin-small": kerbinSmall, "fleet-200": fleet200, "comms-lost": commsLost, "relay-ring": relayRing, "polar-ring": polarRing, crowded };

export function renderFixtures() {
  const out = {};
  for (const [name, build] of Object.entries(FIXTURES)) {
    const text = JSON.stringify(build(), null, 2) + "\n";
    const parsed = parseServerMessage(text);
    if (!parsed.ok) {
      throw new Error(`fixture ${name} does not match the protocol: ${JSON.stringify(parsed.error)}`);
    }
    out[name] = text;
    const layouts = layoutMessages(name);
    if (layouts.length > 0) {
      for (const layout of layouts) {
        if (!parseServerMessage(JSON.stringify(layout)).ok) {
          throw new Error(`layout ${layout.vesselId} of ${name} does not match the protocol`);
        }
      }
      out[`${name}.layouts`] = JSON.stringify(layouts, null, 2) + "\n";
    }
  }
  return out;
}
