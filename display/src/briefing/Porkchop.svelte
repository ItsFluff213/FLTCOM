<script lang="ts">
  import { bestPoint, days, porkchopCells } from "../lib/porkchop.ts";
  import type { PorkchopPlot } from "../protocol.gen.ts";

  interface Props {
    plot: PorkchopPlot;
    ut: number;
  }

  let { plot, ut }: Props = $props();

  const cells = $derived(porkchopCells(plot));
  const best = $derived(bestPoint(plot));
  const departureEnd = $derived(plot.departureStart + plot.columns * plot.departureStep);
  const flightEnd = $derived(plot.flightStart + plot.rows * plot.flightStep);
</script>

<figure class="porkchop" data-porkchop>
  <svg viewBox={`0 0 ${plot.columns} ${plot.rows}`} preserveAspectRatio="none" role="img" aria-label="Transfer windows: delta-v by departure and flight time">
    {#each cells as cell (cell.row * plot.columns + cell.column)}
      <rect x={cell.column} y={cell.row} width="1.02" height="1.02" fill={cell.fill} />
    {/each}
    <line class="mark" x1={best.x} x2={best.x} y1="0" y2={plot.rows} vector-effect="non-scaling-stroke" />
    <line class="mark" x1="0" x2={plot.columns} y1={best.y} y2={best.y} vector-effect="non-scaling-stroke" />
    <circle class="best" cx={best.x} cy={best.y} r="0.9" data-porkchop-best />
  </svg>
  <figcaption>
    <span class="axis">DEPART T+{days(plot.departureStart - ut)} … T+{days(departureEnd - ut)} →</span>
    <span class="axis">FLIGHT {days(plot.flightStart)} … {days(flightEnd)} ↑</span>
    <span class="window" data-porkchop-window>BEST: DEPART T+{days(plot.bestDeparture - ut)} · {days(plot.bestFlight)} FLIGHT · {Math.round(plot.bestDeltaV)} m/s</span>
  </figcaption>
</figure>

<style>
  .porkchop {
    margin: 0 0 0.6rem;
  }

  svg {
    display: block;
    width: 100%;
    aspect-ratio: 2 / 1;
    background: var(--bg);
    border: 1px solid var(--rule);
  }

  .mark {
    stroke: var(--fg);
    stroke-width: 1;
    stroke-dasharray: 3 3;
    opacity: 0.7;
  }

  .best {
    fill: none;
    stroke: #39ff88;
    stroke-width: 0.35;
  }

  figcaption {
    display: flex;
    flex-wrap: wrap;
    justify-content: space-between;
    gap: 0.2rem 0.8rem;
    margin-top: 0.25rem;
    font-size: 0.7rem;
  }

  .window {
    flex-basis: 100%;
    color: #39ff88;
  }
</style>
