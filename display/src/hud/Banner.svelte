<script lang="ts">
  import type { FlightAlarm, Vessel } from "../protocol.gen.ts";

  interface Alarmed {
    vesselId: number;
    vesselName: string;
    alarm: FlightAlarm;
  }

  let { vessel, alarmed = null, onraise }: { vessel: Vessel | null; alarmed?: Alarmed | null; onraise?: (vesselId: number) => void } = $props();
</script>

{#if alarmed}
  <div class="banner" role="alert" data-banner={alarmed.alarm.kind}>
    <span class="title">&gt;&gt; {alarmed.alarm.kind === "impact" ? "IMPACT COURSE" : "HARD LANDING"} &lt;&lt;</span>
    <small>{alarmed.vesselName.toUpperCase()} · {alarmed.alarm.text.toUpperCase()}</small>
    {#if alarmed.alarm.kind === "impact" && onraise}
      <button type="button" data-raise-periapsis onclick={() => onraise(alarmed.vesselId)}>RAISE PERIAPSIS</button>
    {/if}
  </div>
{:else if vessel && vessel.comms === "los"}
  <div class="banner" role="alert" data-banner="comms-lost">
    <span class="title">&gt;&gt; COMMUNICATIONS LOST &lt;&lt;</span>
    <small>{vessel.registryCode} · {vessel.name.toUpperCase()}</small>
  </div>
{/if}

<style>
  .banner {
    position: absolute;
    top: 4.6rem;
    left: 50%;
    z-index: 3;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.3rem;
    padding: 0.55rem 1.6rem 0.5rem;
    transform: translateX(-50%);
    border-top: 1px solid var(--alert);
    border-bottom: 1px solid var(--alert);
    background: linear-gradient(90deg, transparent, rgb(70 6 6 / 0.72) 18%, rgb(70 6 6 / 0.72) 82%, transparent);
    color: var(--alert);
    white-space: nowrap;
  }

  .title,
  small {
    animation: pulse 1.4s steps(2, jump-none) infinite;
  }

  .title {
    padding-left: 0.4em;
    font: 400 1.35rem var(--font-ui);
    letter-spacing: 0.4em;
  }

  small {
    color: #ffb3b3;
    font: 700 0.78rem var(--font-type);
    letter-spacing: 0.12em;
  }

  button {
    margin-top: 0.2rem;
    padding: 0.3rem 1rem;
    border: 1px solid var(--alert);
    background: rgb(70 6 6 / 0.9);
    color: var(--alert);
    font: 700 0.78rem var(--font-type);
    letter-spacing: 0.16em;
    cursor: pointer;
  }

  button:hover,
  button:focus-visible {
    background: var(--alert);
    color: #1a0303;
  }

  @keyframes pulse {
    50% {
      opacity: 0.55;
    }
  }

  :global([data-clean="true"]) .banner * {
    animation: none;
  }
</style>
