<script lang="ts">
  import type { Tracked } from "../lib/acks.ts";
  import { holdToConfirm } from "../lib/hold.ts";
  import { approveBlocked, finalOrbit, marginText } from "../lib/missions.ts";
  import { stageRows } from "../lib/stages.ts";
  import Dossier from "../hud/Dossier.svelte";
  import type { Body, BriefingMessage, CraftLayout, Vessel } from "../protocol.gen.ts";
  import Contingencies from "./Contingencies.svelte";
  import ManeuverPreview from "./ManeuverPreview.svelte";
  import Porkchop from "./Porkchop.svelte";
  import Timeline from "./Timeline.svelte";

  interface Props {
    briefing: BriefingMessage;
    vessel: Vessel | null;
    bodies: ReadonlyMap<string, Body>;
    layout: CraftLayout | null;
    ut: number;
    selectedStage: number | null;
    onapprove: (codename: string) => void;
    onscrub: () => void;
    onpin: (stage: number | null) => void;
    vesselName?: (id: number) => string;
    approveAck?: Tracked | null;
  }

  let { briefing, vessel, bodies, layout, ut, selectedStage = $bindable(null), onapprove, onscrub, onpin, vesselName = (id: number) => `VESSEL ${id}`, approveAck = null }: Props = $props();

  const ships = $derived(
    briefing.groupId === null
      ? []
      : [...new Set(briefing.burns.map((burn) => burn.vesselId))].map((id) => {
          const burns = briefing.burns.filter((burn) => burn.vesselId === id);
          const lines = briefing.stages.filter((line) => line.vesselId === id);
          return {
            id,
            burns: burns.length,
            required: burns.reduce((sum, burn) => sum + burn.deltaV, 0),
            available: lines.reduce((sum, line) => sum + line.deltaV, 0),
            short: lines.some((line) => line.insufficient),
          };
        }),
  );

  let edited = $state<{ id: number; text: string } | null>(null);
  const codename = $derived(edited?.id === briefing.requestId ? edited.text : briefing.codename);
  const blocked = $derived(approveBlocked(briefing));
  const final = $derived(finalOrbit(briefing, bodies));
  const km = (meters: number) => (meters / 1000).toFixed(1);
  const margin = $derived(marginText(briefing));
  /** Released satellites are not vessels yet: their burns carry the payload's id, named from the deployment. */
  const nameOf = (id: number) => briefing.deployment?.payloads.find((payload) => payload.id === id)?.name ?? vesselName(id);
  const deployment = $derived(briefing.deployment);
  const deg = (radians: number) => ((radians * 180) / Math.PI).toFixed(2);
  const autoActions = $derived([...new Set(briefing.autoActions.map((action) => (ships.length > 0 ? `${vesselName(action.vesselId)}: ${action.text}` : action.text)))]);
  let lastPin = selectedStage;
  $effect(() => {
    const stage = selectedStage;
    if (stage !== lastPin) {
      lastPin = stage;
      onpin(stage);
    }
  });
</script>

<div class="scrim" data-briefing-scrim>
  <section class="briefing" aria-label="Operation briefing" data-briefing data-blocked={blocked ? "" : undefined}>
    <header>
      <h2>OPERATION <input class="codename" aria-label="Codename" maxlength="40" value={codename} oninput={(event) => (edited = { id: briefing.requestId, text: event.currentTarget.value.toUpperCase() })} data-codename /></h2>
      <p class="desc">{briefing.description.toUpperCase()}</p>
    </header>

    {#if briefing.error}
      <p class="error" data-briefing-error>{briefing.error.toUpperCase()}</p>
    {/if}

    <div class="grid">
      <div class="col">
        <Dossier {vessel} {bodies} {layout} bind:selectedStage />
        {#if briefing.groupId === null}<p class="hint">CLICK A STAGE TO PIN THE BURNS TO IT{selectedStage === null ? " · AUTO, LOWEST STAGE FIRST" : ` · PINNED TO STAGE ${selectedStage}`}</p>{/if}
      </div>
      <div class="col">
        <h3>BURN TIMELINE</h3>
        <Timeline burns={briefing.burns} {ut} vesselName={nameOf} />
        {#if briefing.porkchop}
          <h3>TRANSFER WINDOWS</h3>
          <Porkchop plot={briefing.porkchop} {ut} />
        {/if}
        {#if ships.length > 0}
          <h3>PER SHIP</h3>
          <table class="stages" data-per-ship>
            <thead><tr><th>SHIP</th><th>BURNS</th><th>NEEDS</th><th>HAS</th></tr></thead>
            <tbody>
              {#each ships as ship (ship.id)}
                <tr class:red={ship.short} data-ship={ship.id}>
                  <td>{vesselName(ship.id).toUpperCase()}</td><td>{ship.burns}</td><td>{Math.round(ship.required)} m/s</td><td>{Math.round(ship.available)} m/s</td>
                </tr>
              {/each}
            </tbody>
          </table>
        {/if}
        {#if final}
          <h3>FINAL ORBIT</h3>
          <dl class="budget" data-final-orbit data-low={final.low ? "" : undefined}>
            <dt>BODY</dt><dd>{final.body.toUpperCase()}</dd>
            <dt>PE</dt><dd data-final-pe class:red={final.low}>{km(final.periapsis)} km{final.low ? ` · UNDER ${km(final.floor)} km FLOOR` : ""}</dd>
            <dt>AP</dt><dd data-final-ap>{final.apoapsis === null ? "ESCAPE" : `${km(final.apoapsis)} km`}</dd>
          </dl>
        {/if}
        <h3>DELTA-V BUDGET</h3>
        <dl class="budget" data-budget>
          <dt>REQUIRED</dt><dd data-required>{Math.round(briefing.requiredDeltaV)} m/s</dd>
          <dt>AVAILABLE</dt><dd data-available>{Math.round(briefing.availableDeltaV)} m/s</dd>
          <dt>MARGIN</dt><dd data-margin class:red={briefing.margin !== null && briefing.margin < 0}>{margin}</dd>
        </dl>
        {#if briefing.stages.length > 0}
          <table class="stages">
            <thead><tr><th>STAGE</th><th>Δv</th><th>ASSIGNED</th><th>TWR</th><th>BURN</th></tr></thead>
            <tbody>
              {#each briefing.stages as line (line.vesselId + ":" + line.stage)}
                <tr class:red={line.insufficient} data-stage-line={line.stage} data-insufficient={line.insufficient ? "" : undefined}>
                  <td>{line.stage}</td><td>{Math.round(line.deltaV)}</td><td>{line.assigned.toFixed(1)}</td><td>{line.twr.toFixed(2)}</td><td>{line.burnTime.toFixed(1)} s</td>
                </tr>
              {/each}
            </tbody>
          </table>
        {/if}
        {#if briefing.stagePlan.length > 0}
          <h3>STAGE PLAN</h3>
          <ol class="plan" data-stage-plan>
            {#each briefing.stagePlan as step (step.vesselId + ":" + step.stage + ":" + step.does)}
              <li class={step.severity ?? undefined} class:kept={!step.fires} data-plan-stage={step.stage} data-severity={step.severity ?? undefined}>
                {ships.length > 0 ? `${vesselName(step.vesselId).toUpperCase()} · ` : ""}{deployment && step.does.startsWith("release ") ? "" : `STAGE ${step.stage}: `}{step.does.toUpperCase()}, {step.when.toUpperCase()}{step.warning ? ` · ${step.warning.toUpperCase()}` : ""}
              </li>
            {/each}
          </ol>
        {/if}
        {#if deployment}
          <h3>DEPLOYMENT</h3>
          <dl class="budget" data-deployment>
            <dt>RING</dt><dd data-deploy-ring>{km(deployment.altitude)} km{deployment.synchronousAltitude !== null && Math.abs(deployment.synchronousAltitude - deployment.altitude) < 1000 ? " · SYNCHRONOUS" : ""}</dd>
            <dt>METHOD</dt><dd>{deployment.method === "resonant" ? "RESONANT ORBIT, ONE RELEASE PER PASS" : "CARRIER CIRCULARIZES, PHASES, RELEASES"}</dd>
            {#if deployment.suggestedAltitude !== null}<dt>HIGHEST</dt><dd data-deploy-suggested>{km(deployment.suggestedAltitude)} km</dd>{/if}
          </dl>
          {#if deployment.payloads.length > 0}
            <table class="stages" data-deploy-payloads>
              <thead><tr><th>SATELLITE</th><th>MASS</th><th>OWN Δv</th><th>CIRC.</th><th>RELEASE</th><th>SPACING</th></tr></thead>
              <tbody>
                {#each deployment.payloads as payload (payload.id)}
                  {@const release = deployment.releases.find((candidate) => candidate.payloadId === payload.id)}
                  <tr class:red={payload.deltaV < payload.circularizeDeltaV} data-deploy-payload={payload.id}>
                    <td>{payload.name.toUpperCase()}</td><td>{(payload.mass / 1000).toFixed(2)} t</td><td>{Math.round(payload.deltaV)} m/s</td><td>{Math.round(payload.circularizeDeltaV)} m/s</td>
                    <td>{release ? `PASS ${release.pass} · ${release.apsis.toUpperCase()}` : "—"}</td>
                    <td class:amber={release !== undefined && Math.abs(release.spacingError) > Math.PI / 180} data-deploy-spacing>{release ? `${deg(Math.abs(release.spacingError))}°` : "—"}</td>
                  </tr>
                {/each}
              </tbody>
            </table>
          {/if}
          {#if deployment.payloads.length > 0}
            <h3>STAGES</h3>
            <table class="stages" data-deploy-stages>
              <thead><tr><th>STAGE</th><th>END</th><th>Δv</th></tr></thead>
              <tbody>
                {#each stageRows(deployment) as row (row.stage)}
                  <tr class:amber={row.amber} data-stage={row.stage}>
                    <td>{row.name}</td><td>{row.end}</td><td>{row.deltaV}</td>
                  </tr>
                {/each}
              </tbody>
            </table>
          {/if}
        {/if}
        <h3>AUTOMATIC ACTIONS</h3>
        <ul class="plan" data-auto-actions>
          {#each autoActions as text (text)}
            <li>{text.toUpperCase()}</li>
          {/each}
        </ul>
        <h3>CONTINGENCIES</h3>
        <Contingencies notes={briefing.contingencies} />
        {#if briefing.burns.some((burn) => burn.vesselId === vessel?.id)}
          <h3>PLAN PREVIEW</h3>
          <ManeuverPreview {briefing} {vessel} {bodies} {ut} />
        {/if}
      </div>
    </div>

    <footer>
      <button
        type="button"
        class="approve ctl hold"
        title="Hold to approve: mouse, Enter or Space"
        data-approve
        data-ack={approveAck?.status}
        disabled={blocked}
        use:holdToConfirm={{ onconfirm: () => approveAck?.status !== "pending" && onapprove(codename.trim() || briefing.codename) }}
      >
        {approveAck?.status === "pending" ? "UPLINKING..." : approveAck?.status === "ok" ? "APPROVED" : "HOLD TO APPROVE"}
      </button>
      <button type="button" class="scrub ctl hold" title="Hold to scrub: mouse, Enter or Space" data-scrub use:holdToConfirm={{ onconfirm: onscrub }}>HOLD TO SCRUB</button>
    </footer>
    {#if approveAck?.status === "refused"}
      <p class="refusal" role="status" data-refusal="approve">{approveAck.reason}</p>
    {/if}
  </section>
</div>

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 40;
    display: grid;
    place-items: start center;
    overflow-y: auto;
    padding: 1rem 16px;
    box-sizing: border-box;
    background: rgb(12 10 10 / 0.88);
  }

  .briefing {
    width: min(70rem, 100%);
    padding: 0.75rem 1rem;
    box-sizing: border-box;
    border: 1px solid #ffd84a;
    background: var(--bg);
  }

  h2 {
    margin: 0;
    font-size: 1.2rem;
    letter-spacing: 0.15em;
    color: #ffd84a;
  }

  h3 {
    margin: 0.8rem 0 0.3rem;
    font-family: var(--font-ui);
    padding-left: 0.45rem;
    border-left: 2px solid var(--fg);
    font-size: 0.8rem;
    letter-spacing: 0.32em;
  }

  .codename {
    width: min(18rem, 100%);
    border: 0;
    border-bottom: 1px dashed #ffd84a;
    background: none;
    color: inherit;
    font: inherit;
    letter-spacing: inherit;
  }

  .desc {
    margin: 0.2rem 0 0.5rem;
    font-size: 0.8rem;
  }

  .grid {
    display: grid;
    grid-template-columns: minmax(0, 20rem) minmax(0, 1fr);
    gap: 1rem;
  }

  .hint {
    margin: 0.3rem 0 0;
    font-size: 0.65rem;
    opacity: 0.7;
  }

  .budget {
    display: grid;
    grid-template-columns: 6rem 1fr;
    margin: 0 0 0.4rem;
    font-size: 0.8rem;
  }

  dd {
    margin: 0;
  }

  .stages {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.72rem;
  }

  th,
  td {
    padding: 0.1rem 0.3rem;
    text-align: right;
    border-bottom: 1px solid var(--rule);
  }

  th:first-child,
  td:first-child {
    text-align: left;
  }

  .plan {
    margin: 0;
    padding: 0;
    list-style: none;
    font-size: 0.72rem;
  }

  .plan li {
    padding: 0.1rem 0.4rem;
    border-left: 3px solid var(--rule);
  }

  .plan .kept {
    opacity: 0.6;
  }

  .plan .amber {
    color: #ffd84a;
    border-left-color: #ffd84a;
    opacity: 1;
  }

  .plan .red {
    border-left-color: var(--alert);
    opacity: 1;
  }

  .red,
  .error {
    color: var(--alert);
  }

  .amber {
    color: #ffd84a;
  }

  footer {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    margin-top: 1rem;
  }

  footer button {
    min-width: 8rem;
    min-height: 2.4rem;
    border: 1px solid;
    background: #151211;
    font: 700 0.95rem var(--font-ui);
    letter-spacing: 0.2em;
    cursor: pointer;
  }

  .approve {
    color: #39ff88;
  }

  .scrub {
    color: var(--alert);
  }

  .approve:disabled {
    color: inherit;
    opacity: 0.35;
    cursor: not-allowed;
  }

  @media (max-width: 900px) {
    .grid {
      grid-template-columns: minmax(0, 1fr);
    }
  }
</style>
