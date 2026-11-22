<script lang="ts">
  import { conflictsOf, steps } from "../lib/missions.ts";
  import type { BurnPlan, OperationStatus } from "../protocol.gen.ts";

  interface Props {
    burns: readonly BurnPlan[];
    ut: number;
    operation?: OperationStatus | null;
    others?: readonly OperationStatus[];
    vesselName?: (id: number) => string;
  }

  let { burns, ut, operation = null, others = [], vesselName }: Props = $props();

  const ships = $derived(vesselName && new Set(burns.map((burn) => burn.vesselId)).size > 1 ? vesselName : null);

  const rows = $derived(steps(burns, ut, operation, operation ? conflictsOf(operation, others) : new Set()));
  const tag = { done: "DONE", current: "BURNING", pending: "", missed: "MISSED", hold: "HOLD", void: "VOID", conflict: "CONFLICT" } as const;
</script>

<ol class="timeline" data-timeline>
  {#each rows as row, index (index)}
    <li data-step={row.status} class={row.status}>
      <span class="t">{row.offset}</span>
      <span class="label">{#if ships}<span class="ship" data-timeline-ship>{ships(row.burn.vesselId).toUpperCase()}</span> {/if}{row.burn.label}</span>
      <span class="dv">{row.burn.deltaV.toFixed(1)} m/s · {row.burn.duration.toFixed(row.burn.duration < 10 ? 1 : 0)} s</span>
      {#if tag[row.status]}<span class="tag">{tag[row.status]}</span>{/if}
    </li>
  {/each}
  {#if operation?.note}
    <li class="note" data-timeline-note>{operation.note}</li>
  {/if}
</ol>

<style>
  .timeline {
    margin: 0;
    padding: 0;
    list-style: none;
    font-size: 0.75rem;
  }

  li {
    display: grid;
    grid-template-columns: 6.2rem 1fr auto auto;
    gap: 0.1rem 0.6rem;
    padding: 0.15rem 0;
    border-bottom: 1px solid var(--rule);
    color: #ffd84a;
  }

  .done {
    color: #39ff88;
    opacity: 0.8;
  }

  .missed,
  .conflict,
  .void {
    color: var(--alert);
  }

  .hold {
    color: #ffd84a;
  }

  .current .tag {
    color: #ffd84a;
  }

  .tag {
    font-weight: 700;
    letter-spacing: 0.1em;
  }

  .ship {
    margin-right: 0.6ch;
    color: #d8d5d0;
  }

  .note {
    display: block;
    color: #ffd84a;
    border: 0;
  }

  @media (max-width: 600px) {
    li {
      grid-template-columns: 1fr auto;
    }
  }
</style>
