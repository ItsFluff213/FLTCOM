<script lang="ts">
  import { countOverlaps } from "../lib/declutter.ts";
  import type { Anchor } from "./camera.ts";
  import { planLabels, type Chip } from "./labelPlan.ts";
  import type { Entity } from "./Scene.ts";

  interface Props {
    entities: readonly Entity[];
    anchors: ReadonlyMap<string, Anchor>;
    width: number;
    height: number;
    hovered: string | null;
    onselect: (entity: Entity) => void;
    onchip: (chip: Chip) => void;
  }

  let { entities, anchors, width, height, hovered, onselect, onchip }: Props = $props();

  const TICK = 0.38;

  const plan = $derived(planLabels(entities, anchors, { width, height, hovered }));
  const placed = $derived(plan.placed);
  const brackets = $derived(plan.brackets);
  const byId = $derived(new Map(entities.map((entity) => [entity.id, entity])));
  const overlaps = $derived(countOverlaps(placed));
  const hidden = $derived(placed.filter((label) => label.hidden).length);

  /** Four corner ticks around a ship, like a tracking reticle. */
  function corners(x: number, y: number, r: number): string {
    const t = r * TICK;
    return [
      `M${x - r},${y - r + t}V${y - r}H${x - r + t}`,
      `M${x + r - t},${y - r}H${x + r}V${y - r + t}`,
      `M${x + r},${y + r - t}V${y + r}H${x + r - t}`,
      `M${x - r + t},${y + r}H${x - r}V${y + r - t}`,
    ].join("");
  }
</script>

<div class="labels" data-overlaps={overlaps} data-hidden={hidden} data-shown={placed.length - hidden}>
  <svg {width} {height} aria-hidden="true">
    {#each brackets as b (b.entity.id)}
      <path class="bracket" class:behind={b.behind} d={corners(b.x, b.y, b.r)} stroke={b.entity.color} stroke-width={b.entity.selected ? 2 : 1.5} data-bracket={b.entity.id} />
    {/each}
    {#each placed as label (label.id)}
      {#if label.leader && !label.hidden}
        {@const entity = byId.get(label.id)}
        <line x1={label.ax} y1={label.ay} x2={label.x + label.w / 2} y2={label.y + label.h / 2} stroke={entity?.color ?? "var(--dim)"} stroke-opacity="0.5" />
      {/if}
    {/each}
  </svg>
  {#each placed as label (label.id)}
    {@const entity = byId.get(label.id)}
    {@const chip = plan.chips.get(label.id)}
    {#if chip && !label.hidden}
      <button
        type="button"
        class="label chip"
        style:left="{label.x}px"
        style:top="{label.y}px"
        style:width="{label.w}px"
        style:height="{label.h}px"
        data-chip={chip.body}
        data-count={chip.members.length}
        onclick={() => onchip(chip)}
      >
        {chip.text}
      </button>
    {:else if entity && !label.hidden}
      <button
        type="button"
        class="label {entity.kind} {entity.role}"
        class:selected={entity.selected}
        class:behind={anchors.get(label.id)?.behind === true}
        style:left="{label.x}px"
        style:top="{label.y}px"
        style:width="{label.w}px"
        style:height="{label.h}px"
        data-label={entity.id}
        data-role={entity.role}
        onclick={() => onselect(entity)}
      >
        {entity.text}
      </button>
    {/if}
  {/each}
</div>

<style>
  .labels,
  svg {
    position: absolute;
    inset: 0;
    pointer-events: none;
  }

  .bracket {
    fill: none;
    stroke-linecap: square;
    stroke-opacity: 0.85;
  }

  .bracket.behind {
    stroke-dasharray: 3 3;
    stroke-opacity: 0.55;
  }

  .label.behind {
    opacity: 0.6;
  }

  .label {
    position: absolute;
    box-sizing: border-box;
    padding: 0 4px;
    overflow: hidden;
    border: 0;
    background: none;
    color: var(--fg);
    font: 700 13px/17px var(--font-type);
    letter-spacing: 0.06em;
    text-align: left;
    text-overflow: ellipsis;
    text-transform: uppercase;
    text-shadow:
      0 0 3px var(--bg),
      0 0 2px var(--bg);
    white-space: nowrap;
    pointer-events: auto;
    cursor: pointer;
  }

  .fleet {
    color: #e6e3de;
  }

  .chip {
    border: 1px solid color-mix(in srgb, var(--dim) 60%, transparent);
    background: color-mix(in srgb, var(--bg) 70%, transparent);
    color: #e6e3de;
    line-height: 15px;
    text-align: center;
  }

  .body {
    color: var(--dim);
    font-weight: 400;
  }

  .operation {
    color: var(--role-operation);
  }

  .planned {
    color: var(--role-planned);
  }

  .alert {
    color: var(--alert);
  }

  .selected {
    color: var(--role-selected);
  }

  .selected.alert {
    color: var(--alert);
  }
</style>
