<script lang="ts">
  import { duration } from "../lib/kerbinTime.ts";
  import { ALIGN_LEAD, PHASE_LABEL, type NextBurn } from "../lib/missions.ts";

  interface Props {
    ut: number;
    burn: NextBurn | null;
  }

  let { ut, burn }: Props = $props();

  const IGNITION_MS = 1800;

  const phase = $derived(burn?.phase ?? null);
  const burning = $derived(phase === "burning");
  const remaining = $derived(burn ? Math.max(0, burn.ut - ut) : 0);
  const ring = $derived(1 - Math.min(1, remaining / ALIGN_LEAD));
  const final = $derived(burn !== null && !burning && phase !== "held" && remaining <= 10);
  const flown = $derived(burn && burn.deltaV > 0 ? Math.min(1, burn.deltaVDone / burn.deltaV) : 0);

  const ignitionKey = $derived(burn && burning ? `${burn.vessel}@${burn.ut}` : null);
  let ignition = $state(false);
  $effect(() => {
    if (ignitionKey === null) {
      return;
    }
    ignition = true;
    const clear = setTimeout(() => (ignition = false), IGNITION_MS);
    return () => {
      clearTimeout(clear);
      ignition = false;
    };
  });
</script>

<div class="countdown" data-countdown data-burning={burning ? "" : undefined} data-phase={phase ?? undefined}>
  <span class="key">
    NEXT BURN
    {#if phase}
      {#key phase}<span class="chip {phase}" data-burn-phase={phase}>{PHASE_LABEL[phase]}</span>{/key}
    {/if}
  </span>
  {#if burn && burning}
    <span class="value burning">BURNING</span>
    <span class="who">{burn.vessel}</span>
    <div class="dv" data-dv-progress={Math.round(flown * 100)}>
      <span class="fill" style:transform="scaleX({flown})"></span>
    </div>
    <span class="dv-text">{Math.round(burn.deltaVDone)} / {Math.round(burn.deltaV)} m/s</span>
  {:else if burn && phase === "held"}
    <span class="value idle">AWAITING RESUME</span>
    <span class="who">{burn.vessel}</span>
  {:else if burn}
    <div class="row">
      <svg class="ring" class:final viewBox="0 0 40 40" aria-hidden="true" data-countdown-ring={Math.round(ring * 100)}>
        <circle class="track" cx="20" cy="20" r="16" pathLength="100" />
        <circle class="arc" cx="20" cy="20" r="16" pathLength="100" stroke-dasharray="100" stroke-dashoffset={100 - ring * 100} />
      </svg>
      <span class="value" class:final data-countdown-t={Math.round(remaining)}>T-{duration(remaining)}</span>
    </div>
    <span class="who">{burn.vessel}</span>
  {:else}
    <span class="value idle">NO BURN SCHEDULED</span>
  {/if}
  {#if ignition}
    <span class="ignition" role="status" data-ignition>IGNITION</span>
  {/if}
</div>

<style>
  .countdown {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: 0.15rem;
  }

  .key {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--dim);
    font-size: 0.68rem;
    letter-spacing: 0.28em;
  }

  .chip {
    padding: 0.05rem 0.4rem;
    border: 1px solid currentColor;
    color: var(--role-selected);
    font-size: 0.62rem;
    letter-spacing: 0.2em;
    animation: chip-in 260ms ease-out;
  }

  .chip.aligning,
  .chip.burning {
    color: var(--role-planned);
  }

  .chip.replanned {
    color: var(--role-operation);
  }

  .chip.held {
    color: var(--alert);
  }

  .chip.burning {
    box-shadow: 0 0 8px rgb(255 216 74 / 0.45);
  }

  .row {
    display: flex;
    align-items: center;
    gap: 0.55rem;
  }

  .ring {
    width: 2rem;
    height: 2rem;
    transform: rotate(-90deg);
  }

  .ring circle {
    fill: none;
    stroke-width: 3;
  }

  .ring .track {
    stroke: var(--rule);
  }

  .ring .arc {
    stroke: var(--role-planned);
    filter: drop-shadow(0 0 3px rgb(255 216 74 / 0.6));
  }

  .ring.final {
    animation: pulse 1s ease-in-out infinite;
  }

  .value {
    color: var(--fg);
    font-size: 2.1rem;
    line-height: 1;
    letter-spacing: 0.1em;
    white-space: nowrap;
    text-shadow: 0 0 6px var(--bg);
  }

  .value.final {
    color: var(--role-planned);
  }

  .burning {
    color: var(--role-planned);
  }

  .idle {
    color: var(--dim);
    font-size: 0.95rem;
    letter-spacing: 0.2em;
  }

  .who {
    max-width: 18rem;
    overflow: hidden;
    color: var(--role-planned);
    font: 700 0.8rem var(--font-type);
    letter-spacing: 0.06em;
    text-overflow: ellipsis;
    text-transform: uppercase;
    white-space: nowrap;
  }

  .dv {
    position: relative;
    width: 12rem;
    height: 0.4rem;
    overflow: hidden;
    border: 1px solid var(--role-planned);
  }

  .fill {
    position: absolute;
    inset: 0;
    background: var(--role-planned);
    box-shadow: 0 0 10px var(--role-planned);
    transform-origin: left;
    transition: transform 500ms linear;
  }

  .dv-text {
    color: var(--dim);
    font-size: 0.68rem;
    letter-spacing: 0.16em;
  }

  .ignition {
    margin-top: 0.3rem;
    padding: 0.2rem 0.6rem;
    border: 1px solid var(--role-planned);
    background: var(--bg);
    color: var(--role-planned);
    font-size: 1.1rem;
    letter-spacing: 0.4em;
    box-shadow: 0 0 18px rgb(255 216 74 / 0.5);
    animation: ignite 1.8s ease-out both;
  }

  @keyframes chip-in {
    from {
      opacity: 0;
      transform: translateX(6px);
    }
  }

  @keyframes pulse {
    50% {
      opacity: 0.45;
      transform: rotate(-90deg) scale(1.12);
    }
  }

  @keyframes ignite {
    0%,
    20%,
    40% {
      opacity: 1;
    }
    10%,
    30% {
      opacity: 0.2;
    }
    100% {
      opacity: 0;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .chip,
    .ring.final,
    .fill,
    .ignition {
      animation: none;
      transition: none;
    }
  }
</style>
