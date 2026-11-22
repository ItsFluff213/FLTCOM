<script lang="ts">
  import { whereabouts } from "../lib/describe.ts";
  import { typewriter } from "../lib/typewriter.ts";
  import type { Body, CraftLayout, Vessel } from "../protocol.gen.ts";
  import { settings } from "../settings/settings.svelte.ts";
  import Blueprint from "./Blueprint.svelte";
  import Telescope from "./Telescope.svelte";

  interface Props {
    vessel: Vessel | null;
    bodies: ReadonlyMap<string, Body>;
    layout: CraftLayout | null;
    selectedStage: number | null;
  }

  let { vessel, bodies, layout, selectedStage = $bindable(null) }: Props = $props();

  const at = $derived(vessel ? whereabouts(vessel, bodies) : null);
  const percent = (amount: number, capacity: number) => (capacity > 0 ? Math.round((amount / capacity) * 100) : 0);
</script>

<section class="dossier" aria-label="Vessel dossier">
  <h2 class="heading" use:typewriter={{ text: "DOSSIER", clean: settings.clean }}>DOSSIER</h2>
  {#if vessel && at}
    <p class="callsign" data-dossier-code={vessel.registryCode}>
      <span class="code">{vessel.registryCode}</span>
      <span class="name" use:typewriter={{ text: vessel.name.toUpperCase(), clean: settings.clean }}>{vessel.name.toUpperCase()}</span>
    </p>
    <div class="top">
    <dl>
      <dt>TYPE</dt><dd>{vessel.vesselType.toUpperCase()}</dd>
      <dt>SITUATION</dt><dd>{at.situation}</dd>
      <dt>BODY</dt><dd>{at.body.toUpperCase()}</dd>
      <dt>{at.detailLabel}</dt><dd>{at.detail}</dd>
      <dt>INCL</dt><dd>{at.inclination}</dd>
      <dt>PERIOD</dt><dd>{at.period}</dd>
      <dt>COMMS</dt><dd class:alert={vessel.comms === "los"}>{vessel.comms.toUpperCase()}{vessel.signalStrength === null ? "" : ` ${Math.round(vessel.signalStrength * 100)}%`}</dd>
      <dt>CREW</dt><dd>{vessel.crew.length === 0 ? `0/${vessel.crewCapacity}` : vessel.crew.map((member) => member.name.split(" ")[0]).join(", ")}</dd>
      <dt>MASS</dt><dd>{(vessel.mass / 1000).toFixed(2)} t</dd>
      <dt>Δv</dt><dd>{vessel.deltaV ? `${Math.round(vessel.deltaV.total)} m/s ${vessel.deltaV.source.toUpperCase()}` : "—"}</dd>
    </dl>
    <Telescope model={layout?.model ?? null} attitude={vessel.attitude} clean={settings.clean} rock={vessel.vesselType === "spaceObject" ? `vessel:${vessel.id}` : null} />
    </div>
    {#if vessel.resources.length > 0}
      <ul class="resources">
        {#each vessel.resources as resource (resource.name)}
          <li>
            <span>{resource.name}</span>
            <span class="bar"><i style:width="{percent(resource.amount, resource.capacity)}%"></i></span>
            <span>{percent(resource.amount, resource.capacity)}%</span>
          </li>
        {/each}
      </ul>
    {/if}
    <h3 class="heading">BLUEPRINT</h3>
    <Blueprint {layout} stages={vessel.deltaV?.stages ?? []} bind:selectedStage />
  {:else}
    <p class="empty">NO VESSEL SELECTED</p>
  {/if}
</section>

<style>
  .dossier {
    padding: 0.7rem 0.85rem;
  }

  h3 {
    margin-top: 0.9rem;
  }

  .callsign {
    display: flex;
    flex-direction: column;
    gap: 0.1rem;
    margin: 0 0 0.55rem;
    color: var(--role-selected);
  }

  .code {
    font-size: 0.72rem;
    letter-spacing: 0.2em;
    opacity: 0.85;
  }

  .name {
    min-height: 1.2em;
    font: 700 1.2rem var(--font-type);
    letter-spacing: 0.06em;
  }

  .top {
    display: flex;
    flex-wrap: wrap;
    gap: 0.6rem;
    align-items: start;
  }

  dl {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    gap: 0.12rem 0.4rem;
    flex: 1 1 13rem;
    min-width: 0;
    margin: 0;
    font-size: 0.72rem;
    letter-spacing: 0.04em;
  }

  dt {
    color: var(--dim);
    letter-spacing: 0.14em;
  }

  dt,
  dd {
    white-space: nowrap;
  }

  dd {
    margin: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .alert {
    color: var(--alert);
  }

  .resources {
    margin: 0.65rem 0 0;
    padding: 0;
    list-style: none;
    font-size: 0.68rem;
  }

  .resources li {
    display: grid;
    grid-template-columns: 8rem 1fr 2.5rem;
    gap: 0.5rem;
    align-items: center;
    color: var(--dim);
  }

  .resources li span:last-child {
    color: var(--fg);
    text-align: right;
  }

  .bar {
    height: 3px;
    background: var(--rule);
  }

  .bar i {
    display: block;
    height: 100%;
    background: var(--fg);
  }

  .empty {
    color: var(--dim);
    letter-spacing: 0.2em;
  }
</style>
