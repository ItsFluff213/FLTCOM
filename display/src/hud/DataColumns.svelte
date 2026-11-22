<script lang="ts">
  import { commsLabel, whereabouts } from "../lib/describe.ts";
  import type { Body, Vessel } from "../protocol.gen.ts";

  interface Props {
    vessels: readonly Vessel[];
    bodies: ReadonlyMap<string, Body>;
    selectedId: number | null;
    stale: boolean;
    onselect: (vesselId: number) => void;
  }

  let { vessels, bodies, selectedId, stale, onselect }: Props = $props();

</script>

<ol class="fleet" class:stale aria-label="Fleet">
  <li class="row head" aria-hidden="true">
    <span>REGISTRY</span><span>NAME</span><span>TYPE</span><span>SITUATION</span><span>BODY</span>
    <span>AP × PE / POSITION</span><span>MEAN ANOM</span><span>COMMS</span><span>CREW</span><span>Δv m/s</span>
  </li>
  {#each vessels as vessel (vessel.id)}
    {@const at = whereabouts(vessel, bodies)}
    <li data-vessel={vessel.registryCode} class:selected={vessel.id === selectedId}>
      <button type="button" class="row" aria-pressed={vessel.id === selectedId} onclick={() => onselect(vessel.id)}>
        <span class="code">{vessel.registryCode}</span>
        <span class="name">{vessel.name}</span>
        <span>{vessel.vesselType.toUpperCase()}</span>
        <span>{at.situation}</span>
        <span>{at.body.toUpperCase()}</span>
        <span>{at.detail}</span>
        <span data-anomaly>{at.anomaly}</span>
        <span class="comms" class:los={vessel.comms === "los" && vessel.crew.length === 0} data-comms={vessel.comms}>{commsLabel(vessel)}</span>
        <span>{vessel.crew.length}/{vessel.crewCapacity}</span>
        <span>{vessel.deltaV ? Math.round(vessel.deltaV.total) : "—"}</span>
      </button>
    </li>
  {/each}
</ol>

<style>
  .fleet {
    margin: 0;
    padding: 0;
    list-style: none;
    font-size: 0.72rem;
    letter-spacing: 0.04em;
  }

  .stale {
    opacity: 0.45;
  }

  .row {
    display: grid;
    grid-template-columns: 6.6rem minmax(7rem, 1.6fr) 3.6rem 5.6rem 3.8rem minmax(8rem, 1.4fr) 4.2rem 2.6rem 2.2rem 3.4rem;
    gap: 0 0.5rem;
    box-sizing: border-box;
    width: 100%;
    padding: 0.16rem 0.4rem;
    border: 0;
    border-bottom: 1px solid var(--rule);
    background: none;
    color: inherit;
    font: inherit;
    text-align: left;
  }

  button.row {
    cursor: pointer;
  }

  button.row:hover {
    background: rgb(232 230 227 / 0.06);
  }

  .selected .row {
    background: linear-gradient(90deg, rgb(57 255 136 / 0.16), rgb(57 255 136 / 0.04));
    box-shadow: inset 2px 0 0 var(--role-selected);
    color: var(--role-selected);
  }

  .row span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .head {
    position: sticky;
    top: 0;
    z-index: 1;
    padding-top: 0.45rem;
    padding-bottom: 0.3rem;
    background: var(--bg);
    color: var(--dim);
    font-family: var(--font-ui);
    font-size: 0.66rem;
    letter-spacing: 0.2em;
  }

  .code {
    font-weight: 700;
  }

  .los {
    color: var(--alert);
  }

  @media (max-width: 900px) {
    .row.head {
      display: none;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0 0.6rem;
      padding: 0.35rem 0.3rem;
      font-size: 0.75rem;
    }

    .name {
      flex-basis: 100%;
      order: -1;
    }
  }
</style>
