<script lang="ts">
  import { duration } from "../lib/kerbinTime.ts";
  import type { CraftLayout, StageDeltaV } from "../protocol.gen.ts";
  import { layoutBlueprint, stageColor, type Pt } from "./blueprint.ts";

  interface Props {
    layout: CraftLayout | null;
    stages: readonly StageDeltaV[];
    selectedStage: number | null;
  }

  let { layout, stages, selectedStage = $bindable(null) }: Props = $props();

  const blueprint = $derived(layout ? layoutBlueprint(layout.parts, stages) : null);

  const WIDTH = 330;
  const HEIGHT = 340;
  const DRAW_RIGHT = 140;

  const fit = $derived.by(() => {
    if (!blueprint) {
      return null;
    }
    const { left, right, top, bottom } = blueprint.bounds;
    const scale = Math.min((DRAW_RIGHT - 16) / Math.max(right - left, 0.01), (HEIGHT - 28) / Math.max(top - bottom, 0.01), 60);
    return { scale, ox: DRAW_RIGHT / 2 - ((left + right) / 2) * scale, oy: HEIGHT / 2 + ((top + bottom) / 2) * scale };
  });

  const px = (point: Pt) => (fit ? `${(fit.ox + point.x * fit.scale).toFixed(1)},${(fit.oy - point.y * fit.scale).toFixed(1)}` : "");
  const select = (stage: number) => (selectedStage = selectedStage === stage ? null : stage);

  const SLOT = 58;
  const mids = $derived.by(() => {
    if (!blueprint || !fit) {
      return new Map<number, number>();
    }
    const out = new Map<number, number>();
    let previous = -Infinity;
    for (const band of blueprint.stages) {
      const mid = Math.max(fit.oy - ((band.top + band.bottom) / 2) * fit.scale, previous + SLOT);
      out.set(band.stage, mid);
      previous = mid;
    }
    const overflow = previous - (HEIGHT - 28);
    return overflow > 0 ? new Map([...out].map(([stage, mid]) => [stage, mid - overflow] as const)) : out;
  });

  function statsText(stats: StageDeltaV | null): string[] {
    if (!stats) {
      return ["NO ESTIMATE"];
    }
    return [`Δv ${Math.round(stats.deltaV)} m/s`, `TWR ${stats.twr === null ? "—" : stats.twr.toFixed(2)}`, `BURN ${stats.burnTime === null ? "—" : duration(stats.burnTime)}`];
  }
</script>

{#if blueprint && fit}
  <svg class="blueprint" viewBox="0 0 {WIDTH} {HEIGHT}" role="group" aria-label="Craft blueprint" data-blueprint data-selected-stage={selectedStage ?? ""}>
    <defs>
      <pattern id="paper" width="20" height="20" patternUnits="userSpaceOnUse">
        <path d="M20 0H0V20" fill="none" stroke="#8d8a86" stroke-opacity="0.12" />
      </pattern>
    </defs>
    <rect width={WIDTH} height={HEIGHT} fill="url(#paper)" />
    <line x1={DRAW_RIGHT / 2} y1="6" x2={DRAW_RIGHT / 2} y2={HEIGHT - 6} stroke="#8d8a86" stroke-opacity="0.25" stroke-dasharray="6 3 1 3" />
    {#each blueprint.parts as part (part.id)}
      {@const color = stageColor(part.stage)}
      {@const active = selectedStage === part.stage}
      <g class="part" data-category={part.category} data-part-stage={part.stage}>
        <polygon points={part.outline.map(px).join(" ")} fill={color} fill-opacity={active ? 0.38 : 0.14} stroke={color} stroke-width={active ? 2 : 1.2} stroke-linejoin="round" />
        {#each part.detail as line}
          <polyline points={line.map(px).join(" ")} fill="none" stroke={color} stroke-opacity="0.8" stroke-dasharray={part.category === "decoupler" ? "3 2" : undefined} />
        {/each}
      </g>
    {/each}
    {#each blueprint.stages as band (band.stage)}
      {@const color = stageColor(band.stage)}
      {@const mid = mids.get(band.stage) ?? 0}
      {@const natural = fit.oy - ((band.top + band.bottom) / 2) * fit.scale}
      {@const lines = statsText(band.stats)}
      {@const active = selectedStage === band.stage}
      <g
        class="stage"
        class:active
        role="button"
        tabindex="0"
        aria-pressed={active}
        data-stage={band.stage}
        onclick={() => select(band.stage)}
        onkeydown={(event) => (event.key === "Enter" || event.key === " ") && select(band.stage)}
      >
        <rect x={DRAW_RIGHT + 4} y={mid - 17} width={WIDTH - DRAW_RIGHT - 8} height="52" fill={color} fill-opacity={active ? 0.25 : 0.07} stroke={color} stroke-opacity={active ? 1 : 0.5} />
        <line x1={fit.ox + band.right * fit.scale + 2} y1={natural} x2={DRAW_RIGHT + 4} y2={mid + 5} stroke={color} stroke-opacity="0.6" stroke-dasharray="2 2" />
        <text x={DRAW_RIGHT + 8} y={mid - 5} fill={color} class="head">{band.stage < 0 ? "UNSTAGED" : `STAGE ${band.stage}`}</text>
        {#each lines as line, row}
          <text x={DRAW_RIGHT + 8} y={mid + 8 + row * 11} fill={color} class="stat" data-stat={row}>{line}</text>
        {/each}
      </g>
    {/each}
  </svg>
{:else}
  <p class="none" data-blueprint-none>NO LAYOUT RECEIVED</p>
{/if}

<style>
  .blueprint {
    display: block;
    width: 100%;
    max-height: 18rem;
    border: 1px solid var(--rule);
    background: rgb(34 30 29 / 0.45);
  }

  .stage {
    cursor: pointer;
    outline: none;
  }

  .stage:focus-visible rect {
    stroke-width: 2;
  }

  text {
    font-family: var(--font-ui);
    font-size: 9.5px;
  }

  .head {
    font-family: var(--font-ui);
    font-size: 10.5px;
    letter-spacing: 0.1em;
  }

  .none {
    margin: 0;
    opacity: 0.6;
  }
</style>
