<script lang="ts">
  import { duration } from "../lib/kerbinTime.ts";
  import type { OperationStatus } from "../protocol.gen.ts";
  import { descentView } from "./descent.ts";

  interface Props {
    operation: OperationStatus;
    ut: number;
  }

  let { operation, ut }: Props = $props();

  const view = $derived(descentView(operation, ut));
</script>

{#if view}
  <div class="descent" class:released={view.released} data-descent={view.released ? "released" : (operation.descent?.phase ?? "deorbit")}>
    <p class="phase">{view.phase}</p>
    {#if view.released}
      <p class="order" role="alert">TAKE THE CONTROLS · {view.released.toUpperCase()}</p>
    {/if}
    <dl>
      {#if view.altitude}<dt>ALT</dt><dd data-descent-altitude>{view.altitude}</dd>{/if}
      {#if view.verticalSpeed}<dt>V/S</dt><dd>{view.verticalSpeed}</dd>{/if}
      {#if view.touchdownIn !== null}<dt>TOUCHDOWN</dt><dd>T-{duration(view.touchdownIn)}</dd>{/if}
      <dt>TARGET</dt><dd>{view.target}</dd>
      {#if view.predicted}<dt>PREDICTED</dt><dd>{view.predicted}</dd>{/if}
      {#if view.miss}<dt>MISS</dt><dd data-descent-miss>{view.miss}</dd>{/if}
    </dl>
  </div>
{/if}

<style>
  .descent {
    margin: 0.3rem 0;
    padding: 0.35rem 0.5rem;
    border-left: 2px solid #39ff88;
    font-size: 0.7rem;
  }

  .phase {
    margin: 0 0 0.2rem;
    color: #39ff88;
    font-weight: 700;
    letter-spacing: 0.14em;
  }

  .released {
    border: 2px solid var(--alert);
    background: color-mix(in srgb, var(--alert) 18%, transparent);
  }

  .released .phase,
  .order {
    color: var(--alert);
  }

  .order {
    margin: 0 0 0.3rem;
    font-weight: 700;
  }

  dl {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: 0.1rem 0.6rem;
    margin: 0;
  }

  dt {
    opacity: 0.7;
  }

  dd {
    margin: 0;
  }
</style>
