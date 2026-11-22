import { declutter, type LabelInput, type Obstacle, type PlacedLabel } from "../lib/declutter.ts";
import type { Anchor } from "./camera.ts";
import { bracketHalf, MIN_SHARE } from "./marks.ts";
import type { Entity } from "./Scene.ts";

/** One chip standing in for the ships piled on a body at this zoom. */
export interface Chip {
  id: string;
  body: string;
  text: string;
  members: readonly string[];
}

export interface Bracket {
  entity: Entity;
  x: number;
  y: number;
  r: number;
  behind: boolean;
}

export interface LabelPlan {
  placed: PlacedLabel[];
  chips: ReadonlyMap<string, Chip>;
  brackets: Bracket[];
}

export interface PlanView {
  width: number;
  height: number;
  /** Entity under the pointer, whose label shows even when it would rank too low. */
  hovered: string | null;
}

/** Courier Prime advances 0.6 em; the labels add 0.06 em of tracking. */
const CHAR = 13 * 0.66;
export const LABEL_HEIGHT = 17;
/** The map's corner readouts (delta-v, next burn, UT, center), which labels keep off. */
const READOUT = { left: 230, right: 310, h: 72 };
/** Ships this close to their body's mark, as a share of the map height, count as piled on it. */
const CLUSTER_SHARE = 0.06;
const CLUSTER_MIN = 3;

export const RANK = { selected: 1000, operation: 900, burn: 800, alert: 600, chip: 450, vessel: 400, body: 200, unowned: 0 } as const;

/** Label priority, highest first; null means the entity gets no label. Unowned objects label only when picked or pointed at. */
export function rank(entity: Entity, hovered: string | null): number | null {
  if (entity.selected) {
    return RANK.selected;
  }
  if (entity.kind === "burn") {
    return RANK.burn;
  }
  if (entity.kind === "body") {
    return RANK.body;
  }
  if (entity.unowned) {
    return entity.id === hovered ? RANK.unowned : null;
  }
  return entity.role === "operation" ? RANK.operation : entity.role === "alert" ? RANK.alert : RANK.vessel;
}

const clusterable = (entity: Entity) => entity.kind === "vessel" && !entity.unowned && !entity.selected && entity.role === "fleet" && entity.home !== null;

/** Groups the plain fleet ships drawn on top of their body into one chip per body. Selected, alerting and operating ships stay out. */
export function clusters(entities: readonly Entity[], anchors: ReadonlyMap<string, Anchor>, height: number): Chip[] {
  const reach = CLUSTER_SHARE * height;
  const byBody = new Map<string, string[]>();
  for (const entity of entities) {
    if (!clusterable(entity)) {
      continue;
    }
    const at = anchors.get(entity.id);
    const body = anchors.get(`body:${entity.home}`);
    if (!at?.visible || !body?.visible || body.behind || Math.hypot(at.x - body.x, at.y - body.y) > reach) {
      continue;
    }
    byBody.set(entity.home!, [...(byBody.get(entity.home!) ?? []), entity.id]);
  }
  return [...byBody]
    .filter(([, members]) => members.length >= CLUSTER_MIN)
    .map(([body, members]) => ({ id: `cluster:${body}`, body, text: `${body} · ${members.length} ships`, members }));
}

const widthOf = (text: string, cap: number) => Math.min(cap, Math.ceil(text.length * CHAR) + 10);

/** Decides which labels and brackets the map shows and where, so no two shown labels overlap. */
export function planLabels(entities: readonly Entity[], anchors: ReadonlyMap<string, Anchor>, view: PlanView): LabelPlan {
  const { width, height, hovered } = view;
  const chips = clusters(entities, anchors, height);
  const merged = new Set(chips.flatMap((chip) => chip.members));
  const chipBodies = new Set(chips.map((chip) => `body:${chip.body}`));

  const brackets: Bracket[] = [];
  const inputs: LabelInput[] = [];
  for (const entity of entities) {
    const anchor = anchors.get(entity.id);
    if (!anchor?.visible || merged.has(entity.id) || chipBodies.has(entity.id)) {
      continue;
    }
    const priority = rank(entity, hovered);
    if (entity.kind === "vessel" && (priority !== null || entity.id === hovered)) {
      brackets.push({ entity, x: anchor.x, y: anchor.y, r: bracketHalf(entity, height), behind: anchor.behind === true });
    }
    if (priority === null) {
      continue;
    }
    const clear = entity.kind === "vessel" ? bracketHalf(entity, height) + 3 : entity.kind === "body" ? Math.round(MIN_SHARE.body * height) + 4 : 10;
    inputs.push({ id: entity.id, ax: anchor.x, ay: anchor.y, w: widthOf(entity.text, entity.kind === "burn" ? 320 : 230), h: LABEL_HEIGHT, priority, clear, keep: entity.selected });
  }
  for (const chip of chips) {
    const body = anchors.get(`body:${chip.body}`)!;
    inputs.push({ id: chip.id, ax: body.x, ay: body.y, w: widthOf(chip.text, 260), h: LABEL_HEIGHT, priority: RANK.chip, clear: Math.round(MIN_SHARE.body * height) + 4 });
  }

  const obstacles: Obstacle[] = [
    { id: "readout:tl", x: 0, y: 0, w: READOUT.left, h: READOUT.h },
    { id: "readout:tr", x: width - READOUT.right, y: 0, w: READOUT.right, h: READOUT.h },
    { id: "readout:bl", x: 0, y: height - READOUT.h, w: READOUT.left, h: READOUT.h },
    { id: "readout:br", x: width - READOUT.right, y: height - READOUT.h, w: READOUT.right, h: READOUT.h },
    ...brackets.map((b) => ({ id: b.entity.id, x: b.x - b.r, y: b.y - b.r, w: b.r * 2, h: b.r * 2 })),
  ];
  return { placed: declutter(inputs, { w: width, h: height }, obstacles), chips: new Map(chips.map((chip) => [chip.id, chip])), brackets };
}
