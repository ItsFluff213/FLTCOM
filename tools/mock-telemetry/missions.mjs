import { planGroupBriefing, span } from "./groups.mjs";

const G0 = 9.81;
const FLY_ASK = 120;
const MISS_GRACE = 30;
const BACKGROUND_LEAD = 10;
const ACTIVE_LEAD = 60;
const TWO_PI = 2 * Math.PI;
const hms = (seconds) => {
  const total = Math.max(0, Math.round(seconds));
  const pad = (n) => String(n).padStart(2, "0");
  return `${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
};
const wrap = (angle) => ((angle % TWO_PI) + TWO_PI) % TWO_PI;
const NAMES = ["IRON LANTERN", "SILENT HERON", "AMBER ANVIL", "COBALT COMET", "PALE MERIDIAN", "HOLLOW FALCON"];

function transfer(orbit, body, ut, change, bodies = []) {
  const mu = body.gravParameter;
  const a = orbit.semiMajorAxis;
  const e = orbit.eccentricity;
  const n = Math.sqrt(mu / a ** 3);
  const rp = a * (1 - e);
  const ra = a * (1 + e);
  const speed = (r, sma) => Math.sqrt(mu * (2 / r - 1 / sma));
  const timeTo = (anomaly) => ut + wrap(anomaly - orbit.meanAnomaly) / n;
  const atPeri = (rOther) => ({ r: rp, anomaly: 0, before: a, after: (rp + rOther) / 2 });
  const atApo = (rOther) => ({ r: ra, anomaly: Math.PI, before: a, after: (ra + rOther) / 2 });
  const burn = (label, where) => ({ label, nodeUt: timeTo(where.anomaly), dv: speed(where.r, where.after) - speed(where.r, where.before) });
  const after = (sma, ecc, argument, meanAnomaly, inclination = orbit.inclination) => ({
    referenceBody: orbit.referenceBody, semiMajorAxis: sma, eccentricity: ecc, inclination,
    longitudeOfAscendingNode: orbit.longitudeOfAscendingNode, argumentOfPeriapsis: argument, meanAnomaly,
  });
  const radiusFor = (altitude) => body.radius + altitude;
  const name = (dv, up, down) => (dv >= 0 ? up : down);
  const circularOrbit = (altitude) => {
    const target = radiusFor(altitude);
    const first = target >= rp ? atPeri(target) : atApo(target);
    const burn1 = burn("", first);
    const transferTime = Math.PI / Math.sqrt(mu / ((first.r + target) / 2) ** 3);
    const dv2 = Math.sqrt(mu / target) - speed(target, (first.r + target) / 2);
    return [
      { ...burn1, label: name(burn1.dv, "TRANSFER RAISE BURN", "TRANSFER LOWER BURN"), orbitAfter: after((first.r + target) / 2, Math.abs(target - first.r) / (target + first.r), orbit.argumentOfPeriapsis, target >= first.r ? 0 : Math.PI) },
      { label: "CIRCULARIZE BURN", nodeUt: burn1.nodeUt + transferTime, dv: dv2, orbitAfter: after(target, 0, orbit.argumentOfPeriapsis, wrap(first.anomaly + Math.PI)) },
    ];
  };
  switch (change.kind) {
    case "circularize": {
      const sma = ra;
      const dv = Math.sqrt(mu / sma) - speed(ra, a);
      return [{ label: "CIRCULARIZE BURN", nodeUt: timeTo(Math.PI), dv, orbitAfter: after(sma, 0, orbit.argumentOfPeriapsis, Math.PI) }];
    }
    case "changeApoapsis": {
      const target = radiusFor(change.altitude);
      if (target < rp) {
        throw new Error("APOAPSIS CANNOT GO BELOW PERIAPSIS");
      }
      const b = burn("", atPeri(target));
      const sma = (rp + target) / 2;
      return [{ ...b, label: name(b.dv, "RAISE APOAPSIS BURN", "LOWER APOAPSIS BURN"), orbitAfter: after(sma, (target - rp) / (target + rp), orbit.argumentOfPeriapsis, 0) }];
    }
    case "changePeriapsis": {
      const target = radiusFor(change.altitude);
      if (target > ra) {
        throw new Error("PERIAPSIS CANNOT GO ABOVE APOAPSIS");
      }
      const b = burn("", atApo(target));
      return [{ ...b, label: name(b.dv, "RAISE PERIAPSIS BURN", "LOWER PERIAPSIS BURN"), orbitAfter: after((ra + target) / 2, (ra - target) / (ra + target), orbit.argumentOfPeriapsis, Math.PI) }];
    }
    case "raiseApoapsis":
      return transfer(orbit, body, ut, { ...change, kind: "changeApoapsis", altitude: ra - body.radius + change.altitude }, bodies);
    case "circularOrbit":
    case "spreadEvenly":
      return circularOrbit(change.altitude);
    case "standardLowOrbit":
      return circularOrbit(body.name === "Kerbin" ? 100000 : 50000);
    case "transfer": {
      const moon = bodies.find((candidate) => candidate.name === change.body);
      const star = body.orbit ? bodies.find((candidate) => candidate.name === body.orbit.referenceBody) : null;
      if (moon && moon.orbit && star && !star.orbit && moon.orbit.referenceBody === star.name && moon.name !== body.name) {
        return toPlanet(orbit, body, star, moon, ut, change);
      }
      if (!moon || !moon.orbit || moon.orbit.referenceBody !== body.name) {
        throw new Error("ONLY MOONS OF " + body.name.toUpperCase() + " AND OTHER PLANETS OF ITS STAR CAN BE REACHED FROM HERE");
      }
      const target = moon.orbit.semiMajorAxis;
      const flight = Math.PI * Math.sqrt(((rp + target) / 2) ** 3 / mu);
      const moonN = Math.sqrt(mu / target ** 3);
      const angleOf = (o) => o.longitudeOfAscendingNode + o.argumentOfPeriapsis + o.meanAnomaly;
      const wait = wrap(angleOf(moon.orbit) + moonN * flight - Math.PI - angleOf(orbit)) / (n - moonN);
      const leave = { nodeUt: ut + wait, dv: speed(rp, (rp + target) / 2) - speed(rp, a), orbitAfter: after((rp + target) / 2, (target - rp) / (target + rp), angleOf(orbit) + n * wait - orbit.longitudeOfAscendingNode, 0) };
      const arrive = leave.nodeUt + flight;
      const rc = moon.radius + change.altitude;
      const vInf = Math.abs(Math.sqrt(mu / target) - speed(target, (rp + target) / 2));
      const capture = Math.sqrt(vInf ** 2 + (2 * moon.gravParameter) / rc) - Math.sqrt(moon.gravParameter / rc);
      return [
        { ...leave, label: "LEAVE FOR " + moon.name.toUpperCase() },
        { label: "CAPTURE AT " + moon.name.toUpperCase(), nodeUt: arrive, dv: -capture, orbitAfter: { referenceBody: moon.name, semiMajorAxis: rc, eccentricity: 0, inclination: 0, longitudeOfAscendingNode: 0, argumentOfPeriapsis: 0, meanAnomaly: 0 } },
      ];
    }
    case "changeInclination": {
      const delta = Math.abs(change.inclination - orbit.inclination);
      const dv = 2 * Math.sqrt(mu / a) * Math.sin(delta / 2);
      return [{ label: "INCLINATION CHANGE BURN", nodeUt: ut + Math.PI / (2 * n), dv, normal: true, orbitAfter: after(a, e, orbit.argumentOfPeriapsis, wrap(orbit.meanAnomaly + n * Math.PI / (2 * n)), change.inclination) }];
    }
  }
  throw new Error("UNKNOWN OPERATION");
}

/**
 * A rough transfer to another planet of the same star: a Hohmann transfer between circular orbits, flown at the next phase-angle window,
 * with a small course correction half way. The porkchop grid is a smooth valley around that window, enough to draw the plot.
 */
function toPlanet(orbit, body, star, planet, ut, change) {
  const muSun = star.gravParameter;
  const r1 = body.orbit.semiMajorAxis;
  const r2 = planet.orbit.semiMajorAxis;
  const n1 = Math.sqrt(muSun / r1 ** 3);
  const n2 = Math.sqrt(muSun / r2 ** 3);
  const flight = Math.PI * Math.sqrt(((r1 + r2) / 2) ** 3 / muSun);
  const longitude = (o) => o.longitudeOfAscendingNode + o.argumentOfPeriapsis + o.meanAnomaly;
  const needed = Math.PI - n2 * flight;
  const phase = longitude(planet.orbit) - longitude(body.orbit);
  const wait = wrap((needed - phase) * Math.sign(n2 - n1)) / Math.abs(n2 - n1);
  const departure = ut + Math.max(wait, orbit.semiMajorAxis > 0 ? 2 * Math.PI * Math.sqrt(orbit.semiMajorAxis ** 3 / body.gravParameter) : 0);
  const excess = (r) => Math.abs(Math.sqrt(muSun / r) - Math.sqrt(muSun * (2 / r - 2 / (r1 + r2))));
  const rp = orbit.semiMajorAxis * (1 - orbit.eccentricity);
  const eject = Math.sqrt(excess(r1) ** 2 + (2 * body.gravParameter) / rp) - Math.sqrt(body.gravParameter * (2 / rp - 1 / orbit.semiMajorAxis));
  const rc = planet.radius + change.altitude;
  const capture = Math.sqrt(excess(r2) ** 2 + (2 * planet.gravParameter) / rc) - Math.sqrt(planet.gravParameter / rc);
  const best = eject + capture;
  const columns = 72;
  const rows = 36;
  const departureSpan = (2 * Math.PI) / Math.abs(n1 - n2);
  const departureStart = Math.max(ut, departure - departureSpan / 2);
  const departureStep = departureSpan / columns;
  const flightStart = 0.5 * flight;
  const flightStep = flight / rows;
  const deltaV = [];
  for (let row = 0; row < rows; row++) {
    for (let column = 0; column < columns; column++) {
      const d = (departureStart + column * departureStep - departure) / (0.15 * departureSpan);
      const f = (flightStart + row * flightStep - flight) / (0.3 * flight);
      deltaV.push(best * (1 + d * d + f * f + 0.6 * d * f));
    }
  }
  const sunOrbit = {
    referenceBody: star.name, semiMajorAxis: (r1 + r2) / 2, eccentricity: Math.abs(r2 - r1) / (r1 + r2), inclination: 0,
    longitudeOfAscendingNode: 0, argumentOfPeriapsis: wrap(longitude(body.orbit) + n1 * (departure - ut) + (r2 > r1 ? 0 : Math.PI)), meanAnomaly: r2 > r1 ? 0 : Math.PI,
  };
  const burns = [
    { label: "LEAVE FOR " + planet.name.toUpperCase(), nodeUt: departure, dv: eject, orbitAfter: sunOrbit },
    { label: "COURSE CORRECTION", nodeUt: departure + flight / 2, dv: 2, normal: true },
    {
      label: "CAPTURE AT " + planet.name.toUpperCase(), nodeUt: departure + flight, dv: -capture,
      orbitAfter: { referenceBody: planet.name, semiMajorAxis: rc, eccentricity: 0, inclination: change.inclination ?? 0, longitudeOfAscendingNode: 0, argumentOfPeriapsis: 0, meanAnomaly: 0 },
    },
  ];
  burns.porkchop = { departureStart, departureStep, flightStart, flightStep, columns, rows, deltaV, bestDeparture: departure, bestFlight: flight, bestDeltaV: best };
  return burns;
}

/** A rough rendezvous: phase when both share an orbit, transfer to the target's height, match its velocity, then hold within 50 m. */
function rendezvous(fleet, vessel, orbit, body, ut, change) {
  const target = fleet.vessels.find((candidate) => candidate.id === change.targetVesselId);
  if (!target) {
    throw new Error("PICK A TARGET IN THE FLEET");
  }
  if (target.id === vessel.id) {
    throw new Error("A VESSEL CANNOT MEET ITSELF");
  }
  if (target.location.kind !== "orbit" || target.location.orbit.referenceBody !== orbit.referenceBody) {
    throw new Error(`${target.name.toUpperCase()} IS NOT IN ORBIT AROUND ${orbit.referenceBody.toUpperCase()}`);
  }
  const goal = target.location.orbit;
  const mu = body.gravParameter;
  const speed = Math.sqrt(mu / orbit.semiMajorAxis);
  const period = TWO_PI * Math.sqrt(orbit.semiMajorAxis ** 3 / mu);
  const raw = [];
  if (Math.abs(goal.inclination - orbit.inclination) > 1e-3) {
    raw.push({ label: "MATCH THE TARGET PLANE", nodeUt: ut + period / 4, dv: 2 * speed * Math.sin(Math.abs(goal.inclination - orbit.inclination) / 2), normal: true });
  }
  const lead = wrap(goal.meanAnomaly - orbit.meanAnomaly);
  const phasing = Math.abs(goal.semiMajorAxis - orbit.semiMajorAxis) < 1000 ? (speed * Math.min(lead, TWO_PI - lead)) / (3 * TWO_PI) : 0;
  const start = (raw.at(-1)?.nodeUt ?? ut) + period / 2;
  if (phasing > 0) {
    raw.push({ label: "PHASING ORBIT", nodeUt: start, dv: lead < Math.PI ? -phasing : phasing }, { label: "TRANSFER TO THE TARGET", nodeUt: start + 3 * period, dv: lead < Math.PI ? phasing : -phasing });
  } else {
    const a = (orbit.semiMajorAxis + goal.semiMajorAxis) / 2;
    raw.push({ label: "TRANSFER TO THE TARGET", nodeUt: start, dv: Math.sqrt(mu * (2 / orbit.semiMajorAxis - 1 / a)) - speed });
  }
  const arrive = raw.at(-1).nodeUt + period / 2;
  raw.push({ label: "MATCH VELOCITY", nodeUt: arrive, dv: Math.abs(raw.at(-1).dv) }, { label: "KILL RELATIVE VELOCITY", nodeUt: arrive + 120, dv: 1, orbitAfter: goal });
  if (change.kind === "dock") {
    raw.push({ label: "DOCK WITH RCS", nodeUt: arrive + 240, dv: 0, orbitAfter: goal });
  }
  return raw;
}

/** The target port a dock joins: the operator's pick, else the first free port with a free chaser port of the same size. */
function dockPort(vessel, target, portId) {
  const ours = (vessel.docking?.ports ?? []).filter((port) => port.free);
  const theirs = target?.docking?.ports ?? [];
  if (ours.length === 0) {
    return { refusal: `${vessel.name.toUpperCase()} HAS NO FREE DOCKING PORT` };
  }
  const fits = (port) => ours.some((mine) => mine.size === port.size);
  if (portId !== null) {
    const port = theirs.find((candidate) => candidate.id === portId);
    if (!port) return { refusal: `NO PORT ${portId} ON ${target.name.toUpperCase()}` };
    if (!port.free) return { refusal: `${port.name.toUpperCase()} ON ${target.name.toUpperCase()} IS OCCUPIED` };
    return fits(port) ? { port } : { refusal: `SIZE MISMATCH: ${port.size.toUpperCase()} AGAINST ${ours.map((mine) => mine.size.toUpperCase()).join(", ")}` };
  }
  const port = theirs.find((candidate) => candidate.free && fits(candidate));
  return port ? { port } : { refusal: `SIZE MISMATCH OR NO FREE PORT ON ${target.name.toUpperCase()}` };
}

/** A rough launch plan for a vessel on the pad: an ascent at ut + 60 s and the burn that finishes the orbit. */
function launch(vessel, body, ut, change) {
  const mu = body.gravParameter;
  const floor = Math.max(body.atmosphereHeight, body.highestTerrainAltitude);
  const apoapsis = change.altitude ?? floor + 10000;
  const periapsis = Math.min(change.periapsis ?? apoapsis, apoapsis);
  if (periapsis <= floor) {
    throw new Error("PERIAPSIS MUST CLEAR THE ATMOSPHERE AND TERRAIN");
  }
  const r0 = body.radius + vessel.location.altitude;
  const ra = body.radius + apoapsis;
  const rp = body.radius + periapsis;
  const v0 = Math.sqrt(mu * (2 / r0 - 2 / (r0 + ra)));
  const raise = Math.sqrt(mu * (2 / ra - 2 / (ra + rp))) - (v0 * r0) / ra;
  const spin = body.rotationPeriod ? ((TWO_PI * r0) / body.rotationPeriod) * Math.cos(vessel.location.latitude) * Math.cos(change.inclination ?? 0) : 0;
  const losses = body.atmosphereHeight > 0 ? (1000 * mu) / body.radius ** 2 / G0 : 0.05 * v0;
  const finish = body.atmosphereHeight > 0 ? Math.max(raise, 0.25 * Math.sqrt(mu / ra)) : raise;
  const total = v0 - spin + losses + raise;
  const start = ut + 60;
  const orbitAfter = {
    referenceBody: body.name, semiMajorAxis: (ra + rp) / 2, eccentricity: (ra - rp) / (ra + rp), inclination: change.inclination ?? 0,
    longitudeOfAscendingNode: change.lan ?? 0, argumentOfPeriapsis: 0, meanAnomaly: Math.PI,
  };
  return [
    { label: "ASCENT", nodeUt: start, startUt: start, dv: total - finish },
    { label: "FINISH THE ORBIT (ESTIMATE)", nodeUt: start + 240, dv: finish, orbitAfter },
  ];
}

const HOME = "Kerbin";
const DEG = Math.PI / 180;
const SITES = { launchPad: { latitude: -0.09694444 * DEG, longitude: -74.5575 * DEG }, runway: { latitude: -0.0493916 * DEG, longitude: -74.60864 * DEG } };

/** Where a landing request touches down, in radians: the fixed KSC spot, or the custom coordinates. */
export function landingSpot(landing) {
  return SITES[landing.site] ?? { latitude: landing.latitude, longitude: landing.longitude };
}

/** The landing target in words, the way the plugin says it: "land at the launch pad", "land at 3.20 N 51.70 W". */
export function describeLanding(landing) {
  if (landing.site === "launchPad") {
    return "land at the launch pad";
  }
  if (landing.site === "runway") {
    return "land on the runway";
  }
  const deg = (rad) => Math.abs(rad / DEG).toFixed(2);
  return `land at ${deg(landing.latitude)} ${landing.latitude < 0 ? "S" : "N"} ${deg(landing.longitude)} ${landing.longitude < 0 ? "W" : "E"}`;
}

/** A rough landing plan: a retrograde deorbit burn five minutes out, then the descent the landing guidance flies. */
function land(orbit, body, ut, change) {
  if (!change.landing) {
    throw new Error("A LANDING NEEDS ITS TARGET");
  }
  if (change.landing.site !== "custom" && body.name !== HOME) {
    throw new Error(`THE LAUNCH PAD AND THE RUNWAY ARE ON THE HOME PLANET; PICK COORDINATES ON ${body.name.toUpperCase()}`);
  }
  const mu = body.gravParameter;
  const r = orbit.semiMajorAxis;
  const rp = body.atmosphereHeight > 0 ? body.radius + 0.3 * body.atmosphereHeight : 0.9 * body.radius;
  const deorbit = Math.sqrt(mu / r) - Math.sqrt(mu * (2 / r - 2 / (r + rp)));
  const fall = Math.PI * Math.sqrt(((r + body.radius) / 2) ** 3 / mu) * 0.5;
  const braking = body.atmosphereHeight > 0 ? 150 : Math.sqrt(mu * (2 / body.radius - 2 / (r + body.radius))) + 60;
  const nodeUt = ut + 300;
  const at = orbit.argumentOfPeriapsis + orbit.meanAnomaly + Math.sqrt(mu / r ** 3) * 300;
  const descent = { ...orbit, semiMajorAxis: (r + rp) / 2, eccentricity: (r - rp) / (r + rp), argumentOfPeriapsis: at - Math.PI, meanAnomaly: Math.PI };
  return [
    { label: "DEORBIT", nodeUt, dv: -deorbit, orbitAfter: descent },
    { label: "DESCENT", nodeUt: nodeUt + 30, startUt: nodeUt + 30, dv: braking, duration: fall },
  ];
}

const clock = (seconds) => `${Math.floor(seconds / 60)}:${String(Math.round(seconds % 60)).padStart(2, "0")}`;

/** The stage plan and automatic actions the plugin briefs, shaped like its Core rules for a simple stack: one engine per stage, chutes under the last. */
export function autoBrief(vessel, change, lines) {
  const on = change.autoSystems !== false;
  const stages = [...lines].sort((x, y) => y.stage - x.stage);
  const lowest = Math.min(...stages.filter((line) => line.assigned > 0).map((line) => line.stage));
  let elapsed = 0;
  const stagePlan = stages.map((line, index) => {
    const fires = line.stage >= lowest;
    const when = !fires ? "kept" : change.kind === "launch" ? (index === 0 ? "at liftoff" : `at ${clock(elapsed)}`) : index === 0 ? "at the first burn" : "when the stage above runs dry";
    elapsed += line.burnTime;
    return { vesselId: vessel.id, stage: line.stage, does: index === 0 ? "light 1 engine" : "drop the spent stage, light 1 engine", when, fires, severity: null, warning: null };
  });
  if (stages.length > 0 && stages.at(-1).stage > 0) {
    stagePlan.push({ vesselId: vessel.id, stage: 0, does: "parachutes", when: change.kind === "land" ? "kept for landing" : "kept", fires: false, severity: null, warning: null });
  }
  const texts = !on
    ? ["auto systems off: no gear, fairing or panel actions; staging only as before (ascent, landing, a flamed-out node burn)"]
    : [
        "stage in every burn: drop spent boosters and empty tanks, never parachutes or the probe core",
        ...(change.kind === "launch" ? ["retract 2 panels, antennas and radiators before liftoff, extend them in a stable orbit", "jettison 1 fairing above 63 km"] : []),
        ...(change.kind === "land" ? ["retract 2 panels, antennas and radiators before the descent", "lower landing gear 15 s or 1000 m above the ground, brakes on at touchdown"] : []),
      ];
  return { stagePlan, autoActions: texts.map((text) => ({ vesselId: vessel.id, text })) };
}

/** The altitude where an orbit takes one turn of the body, or null for a body that does not turn. */
export function synchronousAltitude(body) {
  return body.rotationPeriod ? Math.cbrt((body.gravParameter * body.rotationPeriod ** 2) / (4 * Math.PI ** 2)) - body.radius : null;
}

const SATELLITES = 3;

/**
 * A rough constellation deployment the way the plugin briefs it: the carrier climbs to a resonant orbit with its
 * apoapsis on the ring, lets one satellite go each pass, and each satellite circularizes itself. Three made-up
 * satellites stand in for the payloads the plugin finds in the part tree.
 */
function deploy(fleet, vessel, orbit, body, codename, request, nextId, failed) {
  const mu = body.gravParameter;
  const highest = Math.floor(((body.sphereOfInfluence ?? Infinity) * 0.95 - body.radius) / 1000) * 1000;
  const floor = Math.max(body.atmosphereHeight, body.highestTerrainAltitude) + 10_000;
  const sync = synchronousAltitude(body);
  const altitude = request.change.altitude ?? sync;
  const km = (m) => `${Math.round(m / 1000).toLocaleString("en-US")} KM`;
  if (altitude === null || altitude === undefined) {
    return failed(`${body.name.toUpperCase()} DOES NOT TURN, PICK AN ALTITUDE`, codename);
  }
  if (altitude > highest) {
    return failed(`${km(altitude)} LIES OUTSIDE THE SPHERE OF INFLUENCE OF ${body.name.toUpperCase()}, HIGHEST VALID ALTITUDE ${km(highest)}`, codename);
  }
  if (altitude < floor) {
    return failed(`${km(altitude)} IS UNDER THE SAFE FLOOR OF ${km(floor)}`, codename);
  }
  const n = SATELLITES;
  const ring = body.radius + altitude;
  const inside = ring * ((n - 1) / n) ** (2 / 3);
  const resonantA = 2 * inside - ring > body.radius + floor ? inside : ring * ((n + 1) / n) ** (2 / 3);
  const atApoapsis = resonantA < ring;
  const other = 2 * resonantA - ring;
  const resonantPeriod = TWO_PI * Math.sqrt(resonantA ** 3 / mu);
  const speed = (r, a) => Math.sqrt(mu * (2 / r - 1 / a));
  const here = orbit.semiMajorAxis;
  const climbA = (here + ring) / 2;
  const climb = speed(here, climbA) - speed(here, here);
  const shape = speed(ring, resonantA) - speed(ring, climbA);
  const circularize = Math.abs(Math.sqrt(mu / ring) - speed(ring, resonantA));
  const climbUt = fleet.ut + 600;
  const arrive = climbUt + Math.PI * Math.sqrt(climbA ** 3 / mu);
  const ringArgument = orbit.argumentOfPeriapsis + Math.PI;
  const elements = (a, ecc, argument, meanAnomaly) => ({
    referenceBody: body.name, semiMajorAxis: a, eccentricity: ecc, inclination: 0, longitudeOfAscendingNode: 0, argumentOfPeriapsis: argument, meanAnomaly,
  });
  const working = elements(resonantA, Math.abs(ring - other) / (ring + other), atApoapsis ? ringArgument + Math.PI : ringArgument, atApoapsis ? Math.PI : 0);
  const apsis = atApoapsis ? "at apoapsis" : "at periapsis";
  const releases = Array.from({ length: n }, (_, index) => {
    const name = `${vessel.name} Sat ${index + 1}`;
    const nodeUt = arrive + (index + 1) * resonantPeriod;
    return {
      payloadId: 900_001 + index, name, pass: index + 1, ut: nodeUt - 90, nodeUt, apsis: atApoapsis ? "apoapsis" : "periapsis",
      spacingError: [0, 0.0021, -0.0013][index] ?? 0, finalOrbit: elements(ring, 0, ringArgument, 0),
    };
  });
  const payloads = releases.map((release) => ({ id: release.payloadId, name: release.name, mass: 680, deltaV: 1180, circularizeDeltaV: circularize }));
  const lastUt = releases.at(-1).nodeUt + 60;
  const home = body.name === HOME;
  const deorbit = home ? Math.abs(speed(ring, resonantA) - speed(ring, (ring + body.radius + 30_000) / 2)) : Math.sqrt((2 * mu) / ring) - speed(ring, resonantA);
  const carrierDeltaV = vessel.deltaV.total;
  const used = Math.abs(climb) + Math.abs(shape);
  const left = carrierDeltaV - used;
  const entry = body.radius + body.atmosphereHeight * 0.45;
  const burnUp = Math.abs(speed(ring, resonantA) - speed(ring, (ring + entry) / 2));
  const park = Math.abs(speed(ring, resonantA) - speed(ring, ring + 50_000));
  const choice = request.change.endOfLife ?? null;
  const parked = (why) =>
    left >= park * 1.1
      ? { kind: "disposal", deltaV: park, available: left, text: `${why ? `${why}: ` : ""}parks in a disposal orbit 100 km above the ring`, fallback: why !== null && choice !== null }
      : { kind: "stay", deltaV: 0, available: left, text: `${why ? `${why}: ` : ""}not enough delta-v for a disposal orbit, so the carrier stays on its release orbit`, fallback: true };
  const returnPlan =
    choice === "landHome"
      ? left >= deorbit * 1.1
        ? { kind: home ? "land" : "transferAndLand", deltaV: deorbit, available: left, text: home ? "deorbit at apoapsis, then parachutes to the launch pad" : `escape ${body.name} and transfer to ${HOME}, then land`, fallback: false }
        : { ...parked(`not enough for the way home (${Math.round(deorbit)} m/s needed, ${Math.round(left)} m/s left)`), fallback: true }
      : choice === "park"
        ? parked(null)
        : body.atmosphereHeight <= 0
          ? parked(`${body.name} has no atmosphere to burn up in`)
          : left >= burnUp * 1.1
            ? { kind: "burnUp", deltaV: burnUp, available: left, text: `periapsis to ${Math.round((entry - body.radius) / 100) / 10} km, about ${Math.round(burnUp)} m/s, then it burns up in the atmosphere`, fallback: false }
            : parked(`the transfer stage has ${Math.round(left)} m/s left of the ${Math.round(burnUp * 1.1)} m/s a burn-up needs`);
  const parking = speed(here, here) - speed(here, (here + entry) / 2);
  const launchStage = home
    ? { comesHome: true, name: `${vessel.name} Launch Stage`, deltaV: parking, available: 2_900, text: `splits off in the parking orbit, deorbits about ${Math.round(parking)} m/s once the satellites are out, then lands on parachutes at the launch pad` }
    : { comesHome: false, name: `${vessel.name} Launch Stage`, deltaV: 0, available: 2_900, text: `stays attached: it comes home only from an orbit around ${HOME}` };
  const endUt = lastUt + 300;
  const carrierBurn = (label, nodeUt, dv, orbitAfter) => {
    const duration = Math.abs(dv) / 4;
    return { vesselId: vessel.id, label, nodeUt, startUt: nodeUt - duration / 2, duration, prograde: dv, normal: 0, radial: 0, deltaV: Math.abs(dv), orbitAfter };
  };
  const step = (vesselId, label, ut) => ({ vesselId, label, nodeUt: ut, startUt: ut, duration: 0, prograde: 0, normal: 0, radial: 0, deltaV: 0, orbitAfter: null });
  const burns = [
    ...(launchStage.comesHome ? [step(vessel.id, `release ${launchStage.name}`, fleet.ut + 60)] : []),
    carrierBurn(`raise apoapsis to ${Math.round(altitude / 1000)} km`, climbUt, climb, elements(climbA, (ring - here) / (ring + here), orbit.argumentOfPeriapsis, 0)),
    carrierBurn(atApoapsis ? "lower periapsis to the resonant orbit" : "raise periapsis to the resonant orbit", arrive, shape, working),
    ...releases.flatMap((release) => [
      step(vessel.id, `release ${release.name} ${apsis}, pass ${release.pass}`, release.ut),
      { ...carrierBurn(`circularize ${release.name}`, release.nodeUt, circularize, release.finalOrbit), vesselId: release.payloadId, duration: 32, startUt: release.nodeUt - 16 },
    ]),
    ...(returnPlan.kind === "burnUp" ? [carrierBurn("burn up: periapsis into the atmosphere", lastUt + 300, -returnPlan.deltaV, null)] : []),
    ...(returnPlan.kind === "disposal" ? [carrierBurn("disposal orbit", lastUt + 300, returnPlan.deltaV, null)] : []),
    step(vessel.id, returnPlan.kind === "land" || returnPlan.kind === "transferAndLand" ? "fly home" : "check spacing", endUt + 300),
  ];
  const required = used + returnPlan.deltaV;
  const contingencies = [];
  if (returnPlan.fallback) {
    contingencies.push({ severity: "amber", vesselId: vessel.id, text: `CARRIER: ${returnPlan.text.toUpperCase()}` });
  }
  for (const release of releases.filter((candidate) => Math.abs(candidate.spacingError) > Math.PI / 180)) {
    contingencies.push({ severity: "amber", vesselId: vessel.id, text: `${release.name.toUpperCase()} OFF ITS SLOT BY ${(Math.abs(release.spacingError) / DEG).toFixed(1)} DEG, A SPREAD TRIM FOLLOWS` });
  }
  const margin = required > 0 ? (carrierDeltaV - required) / required : null;
  if (margin !== null && margin < 0) {
    contingencies.push({ severity: "red", vesselId: vessel.id, text: `CARRIER SHORT BY ${Math.ceil(-margin * required)} M/S` });
  }
  return {
    type: "briefing", requestId: request.requestId, operationId: `op-${nextId}`, codename, vesselId: vessel.id, groupId: null, change: request.change,
    description: `deploy constellation at ${Math.round(altitude / 1000).toLocaleString("en-US")} km · ${n} satellites · end of life: ${returnPlan.kind}`,
    burns, requiredDeltaV: required, availableDeltaV: carrierDeltaV, margin, stages: [], contingencies, canApprove: !contingencies.some((note) => note.severity === "red"),
    error: null, porkchop: null,
    stagePlan: releases.map((release) => ({ vesselId: vessel.id, stage: 0, does: `release ${release.name}`, when: `${apsis}, pass ${release.pass}`, fires: false, severity: null, warning: null })),
    autoActions: [], deployment: { altitude, synchronousAltitude: sync, suggestedAltitude: null, method: "resonant", workingOrbit: working, payloads, releases, returnPlan, launchStage },
  };
}

export function planBriefing(fleet, request, nextId) {
  const failed = (error, codename) => ({
    type: "briefing", requestId: request.requestId, operationId: null, codename, vesselId: request.vesselId, groupId: request.groupId ?? null, change: request.change,
    description: "", burns: [], requiredDeltaV: 0, availableDeltaV: 0, margin: null, stages: [], contingencies: [], canApprove: false, error, porkchop: null, stagePlan: [], autoActions: [],
    deployment: null,
  });
  const codename = request.codename || NAMES[nextId % NAMES.length];
  const vessel = fleet.vessels.find((candidate) => candidate.id === request.vesselId);
  if (!vessel) {
    return failed("NO SUCH VESSEL", codename);
  }
  const launching = request.change.kind === "launch";
  if (launching && (vessel.location.kind !== "surface" || vessel.location.situation !== "prelaunch")) {
    return failed("LAUNCH NEEDS A VESSEL ON THE LAUNCH PAD", codename);
  }
  if (!launching && (vessel.location.kind !== "orbit" || vessel.location.orbit.eccentricity >= 1)) {
    return failed("VESSEL IS NOT IN A CLOSED ORBIT", codename);
  }
  if (!vessel.deltaV || vessel.deltaV.stages.length === 0) {
    return failed("NO DELTA-V DATA FOR THIS VESSEL", codename);
  }
  const orbit = launching ? null : vessel.location.orbit;
  const body = fleet.bodies.find((candidate) => candidate.name === (orbit ? orbit.referenceBody : vessel.location.body));
  if (request.change.kind === "deployConstellation") {
    return deploy(fleet, vessel, orbit, body, codename, request, nextId, failed);
  }
  let raw;
  try {
    raw = !orbit ? launch(vessel, body, fleet.ut, request.change) : request.change.kind === "rendezvous" || request.change.kind === "dock" ? rendezvous(fleet, vessel, orbit, body, fleet.ut, request.change) : request.change.kind === "land" ? land(orbit, body, fleet.ut, request.change) : transfer(orbit, body, fleet.ut, request.change, fleet.bodies);
  } catch (error) {
    return failed(error.message, codename);
  }
  const gravity = body.gravParameter / body.radius ** 2;
  const stages = [...vessel.deltaV.stages].sort((x, y) => x.stage - y.stage);
  const remaining = new Map(stages.map((stage) => [stage.stage, stage.deltaV]));
  const assigned = new Map(stages.map((stage) => [stage.stage, 0]));
  const burns = raw.map((entry, index) => {
    const dv = Math.abs(entry.dv);
    const chosen =
      request.pinnedStage !== null && remaining.has(request.pinnedStage)
        ? request.pinnedStage
        : (stages.find((stage) => remaining.get(stage.stage) >= dv) ?? stages.reduce((best, stage) => (remaining.get(stage.stage) > remaining.get(best.stage) ? stage : best))).stage;
    remaining.set(chosen, remaining.get(chosen) - dv);
    assigned.set(chosen, assigned.get(chosen) + dv);
    const twr = stages.find((stage) => stage.stage === chosen).twr ?? 1;
    const duration = entry.duration ?? dv / (twr * gravity);
    return {
      vesselId: vessel.id, label: entry.label, nodeUt: entry.nodeUt, startUt: entry.startUt ?? entry.nodeUt - duration / 2, duration,
      prograde: entry.normal ? 0 : entry.dv, normal: entry.normal ? entry.dv : 0, radial: 0, deltaV: dv,
      orbitAfter: entry.orbitAfter ?? null,
    };
  });
  const required = burns.reduce((sum, burn) => sum + burn.deltaV, 0);
  const pool = request.pinnedStage !== null && assigned.has(request.pinnedStage) ? stages.filter((stage) => stage.stage === request.pinnedStage) : stages;
  const available = pool.reduce((sum, stage) => sum + stage.deltaV, 0);
  const lines = stages
    .filter((stage) => pool.includes(stage))
    .map((stage) => ({
      vesselId: vessel.id, stage: stage.stage, deltaV: stage.deltaV, assigned: assigned.get(stage.stage), twr: stage.twr ?? 0,
      burnTime: stage.twr ? assigned.get(stage.stage) / (stage.twr * gravity) : 0, insufficient: assigned.get(stage.stage) > stage.deltaV + 1e-6,
    }));
  const margin = required > 0 ? (available - required) / required : null;
  const contingencies = [];
  for (const line of lines.filter((candidate) => candidate.insufficient)) {
    contingencies.push({ severity: "red", vesselId: vessel.id, text: `STAGE ${line.stage} SHORT BY ${Math.ceil(line.assigned - line.deltaV)} M/S` });
  }
  if (vessel.comms === "none") {
    contingencies.push({ severity: "red", vesselId: vessel.id, text: "NO COMMNET LINK, AN UPLOAD NEEDS A LINK" });
  } else if (vessel.comms === "los") {
    contingencies.push({ severity: "amber", vesselId: vessel.id, text: "SIGNAL LOST, UPLOAD WAITS FOR A LINK" });
  }
  if (margin !== null && margin >= 0 && margin < 0.1) {
    contingencies.push({ severity: "amber", vesselId: vessel.id, text: `THIN MARGIN, ${Math.round(margin * 100)}% SPARE` });
  }
  let change = request.change;
  if (change.kind === "dock") {
    const { port, refusal } = dockPort(vessel, fleet.vessels.find((candidate) => candidate.id === change.targetVesselId), change.targetPortId);
    if (refusal) {
      contingencies.push({ severity: "red", vesselId: vessel.id, text: refusal });
    } else {
      change = { ...change, targetPortId: port.id };
    }
    if (!vessel.resources.some((resource) => resource.name === "MonoPropellant" && resource.amount > 0)) {
      contingencies.push({ severity: "amber", vesselId: vessel.id, text: "NO RCS: THE DOCK STOPS WITHIN 50 M, CONTINGENCY, NO RCS, MANUAL DOCKING" });
    }
  }
  if (burns.some((burn) => burn.duration > 300)) {
    contingencies.push({ severity: "amber", vesselId: vessel.id, text: "LONG BURN, OVER 5 MINUTES" });
  }
  return {
    type: "briefing", requestId: request.requestId, operationId: `op-${nextId}`, codename, vesselId: vessel.id, groupId: request.groupId ?? null, change,
    description: `${request.change.kind === "land" ? describeLanding(request.change.landing) : request.change.kind} · ${burns.length} burn${burns.length === 1 ? "" : "s"}`, burns, requiredDeltaV: required, availableDeltaV: available,
    margin, stages: lines, contingencies, canApprove: !contingencies.some((note) => note.severity === "red"), error: null, porkchop: raw.porkchop ?? null,
    ...autoBrief(vessel, request.change, lines),
    deployment: null,
  };
}

/** A made-up descent from 15 km over the ground, `f` from 0 at the handover to 1 on the ground. */
export function descentAt(f) {
  const left = Math.max(0, 1 - f);
  const phase = f >= 1 ? "landed" : f < 0.1 ? "correct" : f < 0.55 ? "coast" : f < 0.92 ? "brake" : "final";
  return { phase, altitude: Math.round(14_700 * left ** 1.5), verticalSpeed: f >= 1 ? 0 : -Math.round(5 + 120 * Math.sin(Math.PI * Math.min(1, f))), miss: f < 0.05 ? null : Math.round(3 + 400 * left ** 2) };
}

const LIVE = ["approved", "uploaded", "waiting", "executing", "hold"];

const MEETS = ["rendezvous", "dock"];

/** A live rendezvous or dock holds its target port; without a port it holds the whole target vessel. */
const clashes = (held, wanted) =>
  MEETS.includes(held.kind) && MEETS.includes(wanted.kind) && held.targetVesselId === wanted.targetVesselId && (held.targetPortId === null || wanted.targetPortId === null || held.targetPortId === wanted.targetPortId);

export function createMissions({ onFlown } = {}) {
  let nextId = 1;
  let nextPrompt = 1;
  const briefings = new Map();
  const groupPlans = new Map();
  const runs = new Map();
  const operations = [];
  let prompts = [];

  const askWarp = (operation, ut) => {
    const burn = operation.burns[operation.burnIndex];
    const untilUt = burn.startUt - 2;
    if (burn && untilUt - ut > 30 && !prompts.some((prompt) => prompt.operationId === operation.id)) {
      prompts.push({ id: nextPrompt++, kind: "warp", operationId: operation.id, vesselId: burn.vesselId, text: `Next burn in ${hms(burn.startUt - ut)}, warp?`, untilUt });
    }
  };
  const find = (id) => operations.find((operation) => operation.id === id);
  const vesselsOf = (operation) => new Set(operation.burns.map((burn) => burn.vesselId));
  const shares = (a, b) => [...vesselsOf(a)].some((id) => vesselsOf(b).has(id));
  const askGroupWarp = (operation, ut) => {
    const run = runs.get(operation.id);
    const left = operation.burns.slice(operation.burnIndex);
    if (left.length === 0 || prompts.some((prompt) => prompt.operationId === operation.id && prompt.kind === "warp")) {
      return;
    }
    const end = left.at(-1).startUt + left.at(-1).duration;
    prompts.push({
      id: nextPrompt++, kind: "warp", operationId: operation.id, vesselId: left[0].vesselId,
      text: `${run.ships} ships, ${left.length} burns over ${span(end - ut)}: warp through all?`, untilUt: Math.max(ut, left[0].startUt - BACKGROUND_LEAD),
    });
  };
  const missGroupBurn = (operation, run, ut) => {
    const burn = operation.burns[operation.burnIndex];
    const period = run.periods.get(burn.vesselId) ?? 5400;
    const done = operation.burns.slice(0, operation.burnIndex);
    const left = operation.burns.slice(operation.burnIndex).map((candidate) => (candidate.vesselId === burn.vesselId ? { ...candidate, nodeUt: candidate.nodeUt + period, startUt: candidate.startUt + period } : candidate));
    operation.burns = [...done, ...left.sort((x, y) => x.startUt - y.startUt)];
    operation.note = `MISSED "${burn.label}": MOVED 1 ORBIT LATER`;
    run.warpThrough = false;
    prompts = prompts.filter((prompt) => prompt.operationId !== operation.id);
    askGroupWarp(operation, ut);
  };

  return {
    brief(fleet, request) {
      const briefing = planBriefing(fleet, request, nextId);
      const holder = operations.find((operation) => LIVE.includes(operation.state) && clashes(operation.change, request.change));
      if (holder && !briefing.error) {
        briefing.contingencies.push({ severity: "red", vesselId: request.change.targetVesselId, text: `${request.change.targetPortId === null ? "TARGET" : "THAT PORT"} IS RESERVED BY OPERATION ${holder.codename}` });
        briefing.canApprove = false;
      }
      if (briefing.operationId) {
        briefings.set(briefing.operationId, briefing);
        nextId += 1;
      }
      return briefing;
    },
    briefGroup(fleet, request, group) {
      if (!group) {
        return { ...planBriefing(fleet, { ...request, vesselId: -1 }, nextId), groupId: request.groupId, error: "NO SUCH GROUP" };
      }
      const plan = planGroupBriefing(fleet, request, group, nextId);
      if (plan.briefing.operationId) {
        briefings.set(plan.briefing.operationId, plan.briefing);
        groupPlans.set(plan.briefing.operationId, plan);
        nextId += 1;
      }
      return plan.briefing;
    },
    warpTarget(ut, activeId) {
      let best = null;
      for (const operation of operations) {
        const run = runs.get(operation.id);
        const burn = operation.burns[operation.burnIndex];
        if (!run?.warpThrough || operation.state !== "waiting" || !burn) {
          continue;
        }
        const until = burn.startUt - (burn.vesselId === activeId ? ACTIVE_LEAD : BACKGROUND_LEAD);
        if (until > ut + 1 && (best === null || until < best)) {
          best = until;
        }
      }
      return best;
    },
    interrupt(why, ut) {
      for (const operation of operations) {
        const run = runs.get(operation.id);
        if (run?.warpThrough && LIVE.includes(operation.state)) {
          run.warpThrough = false;
          operation.note = why;
          askGroupWarp(operation, ut);
        }
      }
    },
    approve(message, ut) {
      const briefing = briefings.get(message.operationId);
      if (!briefing || !briefing.canApprove || find(message.operationId) || operations.some((operation) => LIVE.includes(operation.state) && clashes(operation.change, briefing.change))) {
        return false;
      }
      if (operations.some((operation) => ["uploaded", "waiting", "executing"].includes(operation.state) && shares(operation, briefing))) {
        return false;
      }
      const operation = {
        id: briefing.operationId, codename: message.codename, change: briefing.change, description: briefing.description, state: "waiting",
        burns: briefing.burns, burnIndex: 0, deltaVDone: 0, note: null,
        landingPrediction: briefing.change.landing ? { ...landingSpot(briefing.change.landing), ut: briefing.burns.at(-1).startUt + briefing.burns.at(-1).duration } : null,
        alarm: null,
        descent: null,
      };
      for (const held of operations.filter((candidate) => candidate.state === "hold" && shares(candidate, operation))) {
        held.state = "aborted";
        held.alarm = null;
        held.note = `SUPERSEDED BY ${operation.codename}`;
      }
      operations.push(operation);
      const plan = groupPlans.get(operation.id);
      if (plan) {
        runs.set(operation.id, { ships: new Set(briefing.burns.map((burn) => burn.vesselId)).size, slots: plan.slots, periods: plan.periods, warpThrough: false, fly: new Set() });
        askGroupWarp(operation, ut);
      } else {
        askWarp(operation, ut);
      }
      return true;
    },
    scrub(id) {
      const operation = find(id);
      if (operation && LIVE.includes(operation.state)) {
        operation.state = operation.state === "executing" ? "aborted" : "scrubbed";
        prompts = prompts.filter((prompt) => prompt.operationId !== id);
      }
    },
    resume(id, ut) {
      const operation = find(id);
      if (operation?.state === "hold") {
        operation.state = "waiting";
        operation.alarm = null;
        operation.note = "RESUMED AFTER HOLD";
        askWarp(operation, ut);
      }
    },
    answer(message) {
      const prompt = prompts.find((candidate) => candidate.id === message.promptId);
      prompts = prompts.filter((candidate) => candidate !== prompt);
      const run = prompt && runs.get(prompt.operationId);
      if (run && message.accept) {
        if (prompt.kind === "fly") {
          run.fly.add(`${find(prompt.operationId).burnIndex}`);
          return null;
        }
        run.warpThrough = true;
      }
      return prompt && message.accept && prompt.kind === "warp" ? prompt.untilUt : null;
    },
    hold() {
      for (const operation of operations.filter((candidate) => ["waiting", "executing"].includes(candidate.state))) {
        operation.state = "hold";
        operation.note = "HOLD AFTER GAME LOAD, AWAITING RESUME";
      }
      prompts = [];
    },
    /** The first live single-vessel operation flies into an impact course: it holds with the red alert. */
    impact(fleet) {
      const operation = operations.find((candidate) => ["waiting", "executing"].includes(candidate.state) && candidate.change.kind !== "land" && !runs.has(candidate.id));
      if (!operation) {
        return;
      }
      const vessel = fleet.vessels.find((candidate) => candidate.id === operation.burns[operation.burnIndex].vesselId);
      const body = vessel?.location.orbit?.referenceBody ?? "Kerbin";
      operation.state = "hold";
      operation.alarm = { kind: "impact", body, text: `on impact course with ${body}, periapsis -56.7 km` };
      operation.note = `IMPACT: ${operation.alarm.text}`;
      prompts = prompts.filter((prompt) => prompt.operationId !== operation.id);
    },
    /** The descending landing lets go: it holds with the red alert, the way the plugin reports a released descent. */
    release() {
      const operation = operations.find((candidate) => candidate.state === "executing" && candidate.descent);
      if (!operation) {
        return;
      }
      const body = operation.change.body ?? "Mun";
      operation.state = "hold";
      operation.alarm = { kind: "impact", body, text: `released on impact course with ${body}, periapsis -13.7 km: the vessel is spinning at 75 deg/s; FLTCOM cut the throttle and let go` };
      operation.note = `IMPACT: ${operation.alarm.text}`;
    },
    miss() {
      const operation = operations.find((candidate) => ["waiting", "executing"].includes(candidate.state));
      if (operation) {
        operation.state = "missed";
        operation.note = `MISSED ${operation.burns[operation.burnIndex].label}, WINDOW PASSED`;
        prompts = prompts.filter((prompt) => prompt.operationId !== operation.id);
      }
    },
    align() {
      const live = operations.filter((candidate) => ["waiting", "executing"].includes(candidate.state));
      const [first, second] = live;
      if (first && second) {
        const shift = first.burns[first.burnIndex].startUt - second.burns[second.burnIndex].startUt;
        second.burns = second.burns.map((burn, index) => (index >= second.burnIndex ? { ...burn, nodeUt: burn.nodeUt + shift, startUt: burn.startUt + shift } : burn));
        prompts = prompts.filter((prompt) => prompt.operationId !== second.id);
      }
    },
    replan(ut) {
      const operation = operations.find((candidate) => candidate.state === "missed");
      if (operation) {
        const shift = 5400;
        operation.burns = operation.burns.map((burn, index) => (index >= operation.burnIndex ? { ...burn, nodeUt: burn.nodeUt + shift, startUt: burn.startUt + shift } : burn));
        operation.state = "waiting";
        operation.note = "RE-PLANNED TO THE NEXT WINDOW, DELTA-V STILL FITS";
        askWarp(operation, ut);
      }
    },
    tick(ut, dt, activeId = null) {
      let changed = false;
      for (const operation of operations) {
        const burn = operation.burns[operation.burnIndex];
        const run = runs.get(operation.id);
        if (run && operation.state === "waiting" && burn.vesselId === activeId && !run.fly.has(`${operation.burnIndex}`)) {
          if (ut > burn.startUt + MISS_GRACE) {
            missGroupBurn(operation, run, ut);
            changed = true;
          } else if (burn.startUt - ut <= FLY_ASK && !prompts.some((prompt) => prompt.operationId === operation.id && prompt.kind === "fly")) {
            prompts.push({ id: nextPrompt++, kind: "fly", operationId: operation.id, vesselId: burn.vesselId, text: `"${burn.label}" on the ship you fly: let FLTCOM fly it, or fly it yourself`, untilUt: null });
            changed = true;
          }
          continue;
        }
        if (operation.state === "waiting" && ut >= burn.startUt) {
          operation.state = "executing";
          operation.deltaVDone = 0;
          prompts = prompts.filter((prompt) => prompt.operationId !== operation.id);
          changed = true;
        } else if (operation.state === "executing") {
          operation.deltaVDone = Math.min(burn.deltaV, operation.deltaVDone + (burn.deltaV / burn.duration) * dt);
          if (burn.label.toLowerCase() === "descent") {
            operation.descent = descentAt((ut - burn.startUt) / Math.max(1, burn.duration));
          }
          changed = true;
          if (ut >= burn.startUt + burn.duration) {
            if (run) {
              onFlown?.(burn, ut, run.slots, !operation.burns.slice(operation.burnIndex + 1).some((later) => later.vesselId === burn.vesselId));
            }
            operation.deltaVDone = 0;
            operation.burnIndex += 1;
            if (operation.burnIndex >= operation.burns.length) {
              operation.burnIndex = operation.burns.length - 1;
              operation.state = "done";
              operation.note = "OPERATION COMPLETE";
            } else {
              operation.state = "waiting";
              if (!run) {
                askWarp(operation, ut);
              }
            }
          }
        }
      }
      return changed;
    },
    snapshot: (ut) => ({ type: "missions", ut, operations: operations.map((operation) => ({ ...operation })), prompts: prompts.map((prompt) => ({ ...prompt })) }),
  };
}
