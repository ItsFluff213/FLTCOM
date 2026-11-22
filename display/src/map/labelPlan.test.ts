import { describe, expect, it } from "vitest";
import { countOverlaps } from "../lib/declutter.ts";
import type { Anchor } from "./camera.ts";
import { clusters, planLabels, rank, RANK } from "./labelPlan.ts";
import { ROLE_COLOR, type Entity, type Role } from "./Scene.ts";

function vessel(id: number, home: string, extra: Partial<Entity> = {}): Entity {
  const role: Role = extra.role ?? "fleet";
  return {
    id: `vessel:${id}`,
    kind: "vessel",
    text: `Ship ${id}`,
    role,
    selected: false,
    color: ROLE_COLOR[role],
    position: { x: 0, y: 0, z: 0 },
    vesselId: id,
    attitude: null,
    model: null,
    look: "craft",
    scale: 1,
    unowned: false,
    home,
    frame: home,
    scaledBy: null,
    ...extra,
  };
}

function body(name: string): Entity {
  return {
    id: `body:${name}`,
    kind: "body",
    text: name.toUpperCase(),
    role: "body",
    selected: false,
    color: ROLE_COLOR.body,
    position: { x: 0, y: 0, z: 0 },
    vesselId: null,
    attitude: null,
    model: null,
    look: "craft",
    scale: 1,
    unowned: false,
    home: null,
    frame: "Sun",
    scaledBy: name,
  };
}

const anchor = (id: string, x: number, y: number): [string, Anchor] => [id, { id, x, y, depth: 1, visible: true }];
const view = { width: 1280, height: 800, hovered: null };

describe("rank", () => {
  it("orders the selected ship, an operation, burns, alerts, own ships, bodies, then unowned objects", () => {
    const order = [
      rank(vessel(1, "Kerbin", { selected: true, role: "selected" }), null),
      rank(vessel(2, "Kerbin", { role: "operation" }), null),
      rank({ ...vessel(3, "Kerbin"), kind: "burn" }, null),
      rank(vessel(4, "Kerbin", { role: "alert" }), null),
      rank(vessel(5, "Kerbin"), null),
      rank(body("Kerbin"), null),
    ];
    expect(order).toEqual([RANK.selected, RANK.operation, RANK.burn, RANK.alert, RANK.vessel, RANK.body]);
  });

  it("gives debris, asteroids and comets no label unless pointed at or selected", () => {
    const rock = vessel(9, "Sun", { unowned: true, look: "asteroid" });
    expect(rank(rock, null)).toBeNull();
    expect(rank(rock, rock.id)).toBe(RANK.unowned);
    expect(rank({ ...rock, selected: true }, null)).toBe(RANK.selected);
  });
});

describe("clusters", () => {
  const kerbin = body("Kerbin");
  const ships = Array.from({ length: 7 }, (_, i) => vessel(100 + i, "Kerbin"));

  it("folds seven ships piled on Kerbin into one chip", () => {
    const anchors = new Map([anchor(kerbin.id, 400, 300), ...ships.map((ship, i) => anchor(ship.id, 400 + i * 3, 300 - i * 2))]);
    const chips = clusters([kerbin, ...ships], anchors, 800);
    expect(chips).toHaveLength(1);
    expect(chips[0]!.text).toBe("Kerbin · 7 ships");
    const plan = planLabels([kerbin, ...ships], anchors, view);
    expect(plan.placed.filter((label) => !label.hidden).map((label) => label.id)).toEqual(["cluster:Kerbin"]);
    expect(plan.brackets).toEqual([]);
  });

  it("splits when zoomed in far enough that the ships spread out", () => {
    const anchors = new Map([anchor(kerbin.id, 640, 400), ...ships.map((ship, i) => anchor(ship.id, 640 + Math.cos(i) * 300, 400 + Math.sin(i) * 300))]);
    expect(clusters([kerbin, ...ships], anchors, 800)).toEqual([]);
  });

  it("keeps the selected ship, alerts and operation ships out of the chip", () => {
    const special = [vessel(1, "Kerbin", { selected: true, role: "selected" }), vessel(2, "Kerbin", { role: "alert" }), vessel(3, "Kerbin", { role: "operation" })];
    const all = [kerbin, ...ships, ...special];
    const anchors = new Map([anchor(kerbin.id, 400, 300), ...[...ships, ...special].map((ship) => anchor(ship.id, 402, 301))]);
    expect(clusters(all, anchors, 800)[0]!.members).toHaveLength(7);
    const shown = planLabels(all, anchors, view).placed.filter((label) => !label.hidden).map((label) => label.id);
    expect(shown).toContain("vessel:1");
  });

  it("needs at least three ships", () => {
    const anchors = new Map([anchor(kerbin.id, 400, 300), anchor(ships[0]!.id, 401, 300), anchor(ships[1]!.id, 402, 300)]);
    expect(clusters([kerbin, ships[0]!, ships[1]!], anchors, 800)).toEqual([]);
  });
});

describe("planLabels on a crowded Sun view", () => {
  const bodies = ["Kerbin", "Mun", "Minmus", "Duna", "Ike", "Eve", "Jool", "Laythe", "Vall", "Tylo", "Bop", "Pol"].map(body);
  const rocks = Array.from({ length: 15 }, (_, i) => vessel(500 + i, "Sun", { unowned: true, look: "asteroid", text: `Ast. XYZ-${i}` }));
  const debris = [0, 1].map((i) => vessel(600 + i, "Kerbin", { unowned: true, text: "Moon Lander 1 Debris" }));
  const fleet = Array.from({ length: 6 }, (_, i) => vessel(700 + i, i < 3 ? "Kerbin" : "Mun"));
  const selected = vessel(800, "Mun", { selected: true, role: "selected", text: "Moon Lander 1" });
  const entities = [...bodies, ...rocks, ...debris, ...fleet, selected];
  const anchors = new Map<string, Anchor>(entities.map((entity, i) => anchor(entity.id, 640 + Math.cos(i * 2.4) * (20 + i * 4), 400 + Math.sin(i * 2.4) * (10 + i * 2))));
  const unowned = new Set([...rocks, ...debris].map((entity) => entity.id));

  it("shows no two labels on top of each other, always shows the selected ship, and labels no unowned object", () => {
    const plan = planLabels(entities, anchors, view);
    const shown = plan.placed.filter((label) => !label.hidden).map((label) => label.id);
    expect(countOverlaps(plan.placed)).toBe(0);
    expect(shown).toContain(selected.id);
    expect(shown.filter((id) => unowned.has(id))).toEqual([]);
    expect(plan.brackets.filter((bracket) => bracket.entity.unowned)).toEqual([]);
  });

  it("shows the selected ship even when its anchor sits under a corner readout", () => {
    const corner = new Map(anchors);
    corner.set(selected.id, { id: selected.id, x: 5, y: 5, depth: 1, visible: true });
    const shown = planLabels(entities, corner, view).placed.filter((label) => !label.hidden);
    expect(shown.map((label) => label.id)).toContain(selected.id);
  });

  it("labels an asteroid while the pointer is on it", () => {
    const plan = planLabels(entities, anchors, { ...view, hovered: rocks[3]!.id });
    expect(plan.placed.some((label) => label.id === rocks[3]!.id)).toBe(true);
    expect(plan.brackets.some((bracket) => bracket.entity.id === rocks[3]!.id)).toBe(true);
  });
});
