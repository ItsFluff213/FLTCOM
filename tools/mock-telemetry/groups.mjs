import { planBriefing } from "./missions.mjs";

const TWO_PI = 2 * Math.PI;
const wrap = (angle) => ((angle % TWO_PI) + TWO_PI) % TWO_PI;
const SPACING = 30;

export function span(seconds) {
  const hours = Math.round(seconds / 3600);
  return hours >= 6 ? `${Math.floor(hours / 6)}d ${hours % 6}h` : `${Math.max(1, Math.round(seconds / 60))} min`;
}

/** Named sets of vessels, edited like the game edits them: names are needed, members are known vessels, no duplicates. */
export function createGroups() {
  let next = 1;
  let groups = [];
  const find = (id) => groups.find((group) => group.id === id);
  return {
    find,
    roster: () => ({ type: "groups", groups: groups.map((group) => ({ ...group, vesselIds: [...group.vesselIds] })) }),
    edit(fleet, message) {
      const known = new Set(fleet.vessels.map((vessel) => vessel.id));
      const ids = message.vesselIds.filter((id) => known.has(id));
      const name = message.name?.trim() ?? "";
      if (message.action === "create") {
        if (!name) {
          return "a group needs a name";
        }
        groups = [...groups, { id: `g${next++}`, name, vesselIds: [...new Set(ids)] }];
        return null;
      }
      const group = find(message.groupId);
      if (!group) {
        return `no group ${message.groupId}`;
      }
      switch (message.action) {
        case "rename":
          if (!name) {
            return "a group needs a name";
          }
          group.name = name;
          break;
        case "delete":
          groups = groups.filter((candidate) => candidate !== group);
          break;
        case "add":
          group.vesselIds = [...new Set([...group.vesselIds, ...ids])];
          break;
        case "remove":
          group.vesselIds = group.vesselIds.filter((id) => !message.vesselIds.includes(id));
          break;
      }
      return null;
    },
  };
}

const longitude = (orbit) => wrap(orbit.longitudeOfAscendingNode + orbit.argumentOfPeriapsis + orbit.meanAnomaly);
const periodOf = (orbit, body) => TWO_PI * Math.sqrt(orbit.semiMajorAxis ** 3 / body.gravParameter);

/**
 * One briefing for every member. Members that cannot fly become red gates. A member whose burn would overlap another
 * member's moves whole orbits later, so one ship burns at a time. For a spread each member gets a slot on the ring.
 */
export function planGroupBriefing(fleet, request, group, nextId) {
  const members = group.vesselIds.map((id) => fleet.vessels.find((vessel) => vessel.id === id)).filter(Boolean);
  const target = request.change.kind === "spreadEvenly" ? request.change.altitude : null;
  const gates = [];
  const plans = [];
  for (const vessel of members) {
    const orbit = vessel.location.kind === "orbit" ? vessel.location.orbit : null;
    const body = orbit && fleet.bodies.find((candidate) => candidate.name === orbit.referenceBody);
    const single = planBriefing(fleet, { ...request, vesselId: vessel.id, groupId: null, pinnedStage: null }, nextId);
    if (single.error || !single.canApprove) {
      gates.push({ severity: "red", vesselId: vessel.id, text: `${vessel.name.toUpperCase()}: ${single.error ?? single.contingencies.find((note) => note.severity === "red")?.text ?? "CANNOT FLY"}` });
      continue;
    }
    plans.push({ vessel, body, orbit, single, period: periodOf(orbit, body) });
  }

  const busy = [];
  const burns = [];
  for (const plan of plans.sort((x, y) => x.single.burns[0].startUt - y.single.burns[0].startUt)) {
    const period = plan.period;
    let shift = 0;
    const clash = () => plan.single.burns.some((burn) => busy.some(([start, end]) => burn.startUt + shift < end + SPACING && start < burn.startUt + shift + burn.duration + SPACING));
    while (clash()) {
      shift += period;
    }
    for (const burn of plan.single.burns) {
      const moved = { ...burn, nodeUt: burn.nodeUt + shift, startUt: burn.startUt + shift };
      busy.push([moved.startUt, moved.startUt + moved.duration]);
      burns.push(moved);
    }
  }
  burns.sort((x, y) => x.startUt - y.startUt);

  let slots = null;
  if (target !== null && plans.length > 0) {
    const body = plans[0].body;
    const radius = body.radius + target;
    const ranked = [...plans].sort((x, y) => longitude(x.orbit) - longitude(y.orbit));
    slots = {
      motion: Math.sqrt(body.gravParameter / radius ** 3),
      radius,
      epoch: fleet.ut,
      origin: longitude(ranked[0].orbit),
      index: new Map(ranked.map((plan, index) => [plan.vessel.id, index])),
      count: ranked.length,
    };
  }

  const stages = plans.flatMap((plan) => plan.single.stages);
  const contingencies = [...gates, ...plans.flatMap((plan) => plan.single.contingencies)];
  const required = burns.reduce((sum, burn) => sum + burn.deltaV, 0);
  const available = stages.reduce((sum, line) => sum + line.deltaV, 0);
  const canApprove = burns.length > 0 && !contingencies.some((note) => note.severity === "red");
  const ships = new Set(burns.map((burn) => burn.vesselId)).size;
  const briefing = {
    type: "briefing", requestId: request.requestId, operationId: canApprove ? `op-${nextId}` : null, codename: plans[0]?.single.codename ?? request.codename ?? "",
    vesselId: members[0]?.id ?? request.vesselId, groupId: group.id, change: request.change,
    description: `${request.change.kind.replace(/([A-Z])/g, " $1").toLowerCase()} · ${ships} ship${ships === 1 ? "" : "s"} · ${burns.length} burns over ${span(burns.length ? burns.at(-1).startUt + burns.at(-1).duration - fleet.ut : 0)}`,
    burns, requiredDeltaV: required, availableDeltaV: available, margin: required > 0 ? (available - required) / required : null,
    stages, contingencies, canApprove, error: plans.length === 0 ? `${group.name.toUpperCase()} HAS NO SHIP THAT CAN FLY` : null, porkchop: null,
    stagePlan: plans.flatMap((plan) => plan.single.stagePlan), autoActions: plans.flatMap((plan) => plan.single.autoActions), deployment: null,
  };
  return { briefing, slots, periods: new Map(plans.map((plan) => [plan.vessel.id, plan.period])) };
}

/** Where a member ends up after its last burn: its slot on the ring for a spread, otherwise the planned orbit. */
export function orbitAfterGroupBurn(burn, ut, slots, vesselOrbit, mu) {
  if (slots) {
    const slot = slots.index.get(burn.vesselId) ?? 0;
    const along = slots.origin + slots.motion * (ut - slots.epoch) + (slot * TWO_PI) / slots.count;
    return { ...vesselOrbit, semiMajorAxis: slots.radius, eccentricity: 0, argumentOfPeriapsis: 0, meanAnomaly: wrap(along - vesselOrbit.longitudeOfAscendingNode) };
  }
  if (!burn.orbitAfter) {
    return null;
  }
  const motion = Math.sqrt(mu / burn.orbitAfter.semiMajorAxis ** 3);
  return { ...burn.orbitAfter, meanAnomaly: wrap(burn.orbitAfter.meanAnomaly + motion * (ut - burn.nodeUt)) };
}
