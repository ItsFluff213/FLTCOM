<script lang="ts">
  import { flushSync, onMount, untrack } from "svelte";
  import Briefing from "./briefing/Briefing.svelte";
  import Composer from "./briefing/Composer.svelte";
  import SurfaceMap from "./briefing/SurfaceMap.svelte";
  import type { LandingPick } from "./briefing/surfaceMap.ts";
  import Groups from "./briefing/Groups.svelte";
  import Operations from "./briefing/Operations.svelte";
  import { ackOf, resolve, settle, track, type CommandKind, type Tracked } from "./lib/acks.ts";
  import { connect, knownFleet, socketUrl, type Connection, type DenyReason, type Link } from "./lib/connection.ts";
  import { alarmed, countdownTick, LIVE_STATES, missionCues, nextBurn } from "./lib/missions.ts";
  import { kerbinDate } from "./lib/kerbinTime.ts";
  import { createSound } from "./lib/sound.ts";
  import { typewriter } from "./lib/typewriter.ts";
  import Banner from "./hud/Banner.svelte";
  import Countdown from "./hud/Countdown.svelte";
  import DataColumns from "./hud/DataColumns.svelte";
  import Dossier from "./hud/Dossier.svelte";
  import StatusCodes from "./hud/StatusCodes.svelte";
  import { applyFx, startGrain } from "./map/PostFx.ts";
  import MapView from "./map/MapView.svelte";
  import type { PlannedBurn, PlannedLanding } from "./map/Scene.ts";
  import { centerChoices } from "./map/Scene.ts";
  import { PROTOCOL_VERSION, type Body, type BriefRequest, type BriefingMessage, type CraftLayout, type Fleet, type Group, type Missions, type OrbitChangeRequest, type ChangeKind } from "./protocol.gen.ts";
  import Effects from "./settings/Effects.svelte";
  import { settings } from "./settings/settings.svelte.ts";

  const DENIED: Record<DenyReason, string> = {
    multiplayerSession: "LINK DENIED, MULTIPLAYER SESSION",
    unsupportedPhysics: "LINK DENIED, UNSUPPORTED PHYSICS",
    unsupportedScale: "LINK DENIED, UNSUPPORTED SCALE",
  };

  let link = $state.raw<Link>({ status: "connecting" });
  let layouts = $state.raw<ReadonlyMap<number, CraftLayout>>(new Map());
  let pickedId = $state<number | null>(null);
  let pickedCenter = $state<string | null>(null);
  let selectedStage = $state<number | null>(null);
  let grain: HTMLCanvasElement;
  let missions = $state.raw<Missions | null>(null);
  let briefing = $state.raw<BriefingMessage | null>(null);
  let groups = $state.raw<readonly Group[]>([]);
  let pickedGroupId = $state<string | null>(null);
  let pendingBrief = $state(false);
  let commands = $state.raw<readonly Tracked[]>([]);
  let composeKind = $state<ChangeKind>("circularize");
  let landing = $state<LandingPick>({ site: "launchPad", latitudeDeg: null, longitudeDeg: null });
  let connection: Connection | null = null;
  let lastRequest: Omit<BriefRequest, "protocolVersion" | "type"> | null = null;
  let requestSeq = 0;
  let lastRemaining: number | null = null;

  const sound = createSound(() => settings.muted);

  onMount(() => {
    const stopGrain = startGrain(grain, () => (settings.clean ? 0 : settings.intensity));
    const stop = connect({
      onBriefing: (message) => {
        if (message.requestId === lastRequest?.requestId) {
          pendingBrief = false;
          briefing = message;
          commands = resolve(commands, "brief", "composer", message.error, Date.now());
        }
      },
      onGroups: (roster) => {
        groups = roster.groups;
      },
      onMissions: (message) => {
        for (const cue of missionCues(missions, message)) {
          sound.play(cue);
        }
        missions = message;
        commands = settle(commands, message, Date.now());
      },
      url: socketUrl(location),
      onLink: (next) =>
        flushSync(() => {
          link = next;
        }),
      onLayout: (layout) => {
        const known = layouts.get(layout.vesselId);
        if (!known || known.revision <= layout.revision) {
          layouts = new Map(layouts).set(layout.vesselId, layout);
        }
      },
    });
    connection = stop;
    const ackTimer = setInterval(() => {
      commands = settle(commands, missions, Date.now());
    }, 250);
    return () => {
      stop();
      stopGrain();
      clearInterval(ackTimer);
    };
  });

  $effect(() => {
    applyFx(document.documentElement, settings);
  });

  const fleet: Fleet | null = $derived(knownFleet(link));
  const bodies = $derived(new Map<string, Body>(fleet?.bodies.map((body) => [body.name, body]) ?? []));
  const selected = $derived(fleet?.vessels.find((vessel) => vessel.id === (pickedId ?? fleet.activeVesselId ?? fleet.vessels[0]?.id)) ?? null);
  const transferTargets = $derived.by(() => {
    const from = selected?.location.kind === "orbit" ? selected.location.orbit.referenceBody : null;
    return fleet && from ? fleet.bodies.filter((body) => body.orbit !== null && body.name !== from).map((body) => body.name) : [];
  });
  const rendezvousTargets = $derived(
    selected?.location.kind === "orbit"
      ? (fleet?.vessels ?? [])
          .filter((vessel) => vessel.id !== selected.id && vessel.location.kind === "orbit" && selected.location.kind === "orbit" && vessel.location.orbit.referenceBody === selected.location.orbit.referenceBody)
          .map((vessel) => ({ id: vessel.id, name: vessel.name, ports: vessel.docking?.ports }))
      : [],
  );
  const homeBody = $derived(selected ? (selected.location.kind === "orbit" ? selected.location.orbit.referenceBody : selected.location.body) : "Kerbin");
  const center = $derived(pickedCenter ?? homeBody);
  const centers = $derived(
    fleet ? centerChoices(fleet.bodies, fleet.vessels.map((vessel) => (vessel.location.kind === "orbit" ? vessel.location.orbit.referenceBody : vessel.location.body)), center) : [],
  );
  const vesselName = (id: number) => fleet?.vessels.find((vessel) => vessel.id === id)?.name ?? `VESSEL ${id}`;
  const upcoming = $derived(nextBurn(missions, vesselName));
  const alarm = $derived(alarmed(missions, vesselName));
  const pickedGroup = $derived(groups.find((group) => group.id === pickedGroupId) ?? null);
  const onPad = $derived(selected?.location.kind === "surface" && selected.location.situation === "prelaunch");
  const orbitBody = $derived.by(() => {
    const location = selected?.location;
    return location?.kind === "orbit" ? (fleet?.bodies.find((body) => body.name === location.orbit.referenceBody) ?? null) : null;
  });
  /** The body under the vessel while LAND is being composed: the surface map replaces the 3D map for the pick. */
  const landingBody = $derived(composeKind === "land" && pickedGroup === null && selected && !onPad ? (bodies.get(homeBody) ?? null) : null);
  const members = $derived(new Set<number>(pickedGroup?.vesselIds ?? []));
  const planned = $derived.by((): PlannedBurn[] => {
    const out: PlannedBurn[] = [];
    for (const operation of missions?.operations ?? []) {
      if (LIVE_STATES.includes(operation.state) || operation.state === "hold" || operation.state === "missed") {
        operation.burns.forEach((burn, index) => {
          if (index >= operation.burnIndex) {
            out.push({ vesselId: burn.vesselId, label: burn.label, nodeUt: burn.nodeUt, missed: operation.state === "missed" && index === operation.burnIndex, orbitAfter: burn.orbitAfter });
          }
        });
      }
    }
    for (const burn of briefing?.burns ?? []) {
      out.push({ vesselId: burn.vesselId, label: burn.label, nodeUt: burn.nodeUt, missed: false, orbitAfter: burn.orbitAfter });
    }
    return out;
  });
  const landings = $derived(
    (missions?.operations ?? []).flatMap((operation): PlannedLanding[] =>
      operation.landingPrediction && operation.burns[0] && operation.state !== "done" && operation.state !== "scrubbed" && operation.state !== "aborted"
        ? [{ vesselId: operation.burns[0].vesselId, ...operation.landingPrediction }]
        : [],
    ),
  );
  const operating = $derived(
    new Set<number>((missions?.operations ?? []).filter((operation) => LIVE_STATES.includes(operation.state)).flatMap((operation) => operation.burns.map((burn) => burn.vesselId))),
  );
  const models = $derived(new Map([...layouts].map(([id, layout]) => [id, layout.model])));
  const briefedVessel = $derived(briefing ? (fleet?.vessels.find((vessel) => vessel.id === briefing!.vesselId) ?? null) : null);

  $effect(() => {
    const remaining = upcoming && fleet && upcoming.phase !== "burning" && upcoming.phase !== "held" ? upcoming.ut - fleet.ut : null;
    untrack(() => {
      if (remaining !== null && countdownTick(lastRemaining, remaining)) {
        sound.play("countdown");
      }
      lastRemaining = remaining;
    });
  });

  function sendBrief(change: OrbitChangeRequest, pinnedStage: number | null, codename: string | null, only: number | null = null) {
    const group = only === null ? pickedGroup : null;
    const vesselId = only ?? (group ? (group.vesselIds[0] ?? null) : (selected?.id ?? null));
    if (vesselId === null) {
      return;
    }
    lastRequest = { requestId: ++requestSeq, vesselId, groupId: group?.id ?? null, change, codename, pinnedStage: group ? null : pinnedStage };
    pendingBrief = connection?.send({ type: "brief", ...lastRequest }) ?? false;
    commands = track(commands, "brief", "composer", Date.now(), pendingBrief);
  }

  function command(kind: Exclude<CommandKind, "brief">, operationId: string, sent: boolean) {
    commands = track(commands, kind, operationId, Date.now(), sent);
  }

  function repin(stage: number | null) {
    if (briefing && lastRequest && stage !== lastRequest.pinnedStage) {
      sendBrief(lastRequest.change, stage, briefing.codename);
    }
  }

  function approve(codename: string) {
    if (briefing?.operationId) {
      command("approve", briefing.operationId, connection?.send({ type: "approve", operationId: briefing.operationId, codename }) ?? false);
    }
  }

  const approveAck = $derived(ackOf(commands, "approve", briefing?.operationId ?? null));
  $effect(() => {
    if (approveAck?.status !== "ok") {
      return;
    }
    const approved = approveAck.target;
    const close = setTimeout(() => {
      if (briefing?.operationId === approved) {
        briefing = null;
      }
    }, 700);
    return () => clearTimeout(close);
  });

  function scrubBriefing() {
    if (briefing?.operationId) {
      connection?.send({ type: "scrub", operationId: briefing.operationId });
    }
    briefing = null;
  }

  const linkLabel = $derived(link.status === "live" ? "LIVE" : link.status.toUpperCase());

  function raisePeriapsis(vesselId: number) {
    pickedGroupId = null;
    select(vesselId);
    sendBrief({ kind: "circularize", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: null, autoSystems: null, endOfLife: null }, null, null, vesselId);
  }

  function select(id: number) {
    pickedId = id;
    pickedCenter = null;
    selectedStage = null;
  }
</script>

<div class="fx fx-scan" aria-hidden="true"></div>
<div class="fx fx-vignette" aria-hidden="true"></div>
<canvas class="fx fx-grain" bind:this={grain} aria-hidden="true"></canvas>

<main data-link={link.status} data-clean={settings.clean}>
  <header>
    <h1 use:typewriter={{ text: "FLTCOM", clean: settings.clean }}>FLTCOM</h1>
    {#if link.status === "connecting"}
      <p class="status">ACQUIRING LINK</p>
    {:else if link.status === "live"}
      <p class="status">LINK {link.server.toUpperCase()}{fleet ? ` · ${fleet.vessels.length} VESSELS` : ""}</p>
    {:else if link.status === "stale"}
      <p class="status alert" role="alert">LINK {link.server.toUpperCase()} · TELEMETRY STALE · NO UPDATE SINCE UT {Math.floor(link.fleet.ut)}</p>
    {:else if link.status === "lost"}
      <p class="status alert" role="alert">LINK LOST · RETRYING</p>
    {:else if link.status === "denied"}
      <p class="status alert" role="alert">{DENIED[link.reason]}</p>
    {:else}
      <p class="status alert" role="alert" data-version-mismatch>
        PROTOCOL VERSION MISMATCH · DISPLAY v{PROTOCOL_VERSION}, SERVER {link.received === null ? "UNKNOWN" : `v${link.received}`} · UPDATE FLTCOM AND RELOAD
      </p>
    {/if}
    {#if fleet}
      <nav class="centers" aria-label="Map center">
        {#each centers as name (name)}
          <button type="button" class:on={name === center} aria-pressed={name === center} data-center-pick={name} onclick={() => (pickedCenter = name)}>{name.toUpperCase()}</button>
        {/each}
      </nav>
    {/if}
    <Effects onTestSound={() => sound.play("alert")} />
  </header>

  {#if fleet}
    <section class="stage-map">
      <MapView {fleet} {center} selectedId={selected?.id ?? null} burns={planned} {landings} {members} {operating} {models} clean={settings.clean} onselect={select} />
      <Banner vessel={selected} alarmed={alarm} onraise={raisePeriapsis} />
      {#if landingBody}
        <SurfaceMap body={landingBody} markers={fleet.markers?.items ?? []} pick={landing} onpick={(pick) => (landing = pick)} />
      {/if}
      <div class="corners panel">
        <div class="slot tl"><span class="key">Δv m/s</span><span class="value" data-readout="dv">{selected?.deltaV ? Math.round(selected.deltaV.total) : "—"}</span></div>
        <div class="slot tr"><Countdown ut={fleet.ut} burn={upcoming} /></div>
        <div class="slot bl"><span class="key">UT</span><span class="value" data-readout="ut">{kerbinDate(fleet.ut)}</span></div>
        <div class="slot br"><span class="key">CENTER</span><span class="value" data-readout="center">{center.toUpperCase()}</span></div>
      </div>
    </section>
    <aside class="side panel">
      {#if !briefing}
        <Dossier vessel={selected} {bodies} layout={selected ? (layouts.get(selected.id) ?? null) : null} bind:selectedStage />
      {/if}
      <Groups
        {groups}
        {vesselName}
        selectedVesselId={selected?.id ?? null}
        {pickedGroupId}
        onpick={(groupId) => (pickedGroupId = groupId)}
        onedit={(edit) => connection?.send({ type: "groupEdit", ...edit })}
      />
      <Composer vesselName={selected?.name ?? null} groupName={pickedGroup?.name ?? null} bodies={transferTargets} {onPad}
        targets={rendezvousTargets}
        busy={pendingBrief}
        ack={ackOf(commands, "brief", "composer")}
        onbrief={(change) => sendBrief(change, selectedStage, null)}
        bind:kind={composeKind}
        bind:site={landing.site}
        bind:latitudeDeg={landing.latitudeDeg}
        bind:longitudeDeg={landing.longitudeDeg}
        {orbitBody}
      />
      <Operations
        {missions}
        {vesselName}
        onscrub={(operationId) => command("scrub", operationId, connection?.send({ type: "scrub", operationId }) ?? false)}
        onresume={(operationId) => command("resume", operationId, connection?.send({ type: "resume", operationId }) ?? false)}
        ackOf={(kind, operationId) => ackOf(commands, kind, operationId)}
        onanswer={(promptId, accept) => connection?.send({ type: "answer", promptId, accept })}
      />
      <StatusCodes {fleet} {linkLabel} />
    </aside>
    <section class="list panel">
      <DataColumns vessels={fleet.vessels} {bodies} selectedId={selected?.id ?? null} stale={link.status !== "live"} onselect={select} />
    </section>
  {/if}
  {#if briefing}
    <Briefing
      {briefing}
      vessel={briefedVessel}
      {bodies}
      layout={briefedVessel ? (layouts.get(briefedVessel.id) ?? null) : null}
      ut={fleet?.ut ?? 0}
      bind:selectedStage
      onapprove={approve}
      onscrub={scrubBriefing}
      {approveAck}
      onpin={repin}
      {vesselName}
    />
  {/if}
</main>

<style>
  main {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 25rem;
    grid-template-rows: auto minmax(22rem, 1fr) minmax(8rem, 14rem);
    grid-template-areas:
      "header header"
      "map side"
      "list side";
    height: 100vh;
    min-height: 0;
    padding: 0.6rem 0.9rem 0.8rem;
    box-sizing: border-box;
    gap: 0.6rem 0.9rem;
  }

  header {
    grid-area: header;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem 1.6rem;
    padding-bottom: 0.45rem;
    border-bottom: 1px solid var(--rule);
  }

  h1 {
    flex: none;
    margin: 0;
    padding-right: 0.42em;
    color: var(--fg);
    font: 400 1.7rem/1 var(--font-ui);
    letter-spacing: 0.42em;
    white-space: nowrap;
  }

  .status {
    margin: 0;
    color: var(--dim);
    font-size: 0.78rem;
    letter-spacing: 0.22em;
  }

  .alert {
    color: var(--alert);
  }

  .centers {
    display: flex;
    flex-wrap: wrap;
    gap: 0.15rem 0.9rem;
    margin-left: auto;
  }

  .centers button {
    position: relative;
    padding: 0.1rem 0;
    border: 0;
    background: none;
    color: var(--dim);
    font: 0.74rem var(--font-ui);
    letter-spacing: 0.2em;
    cursor: pointer;
  }

  .centers button:hover {
    color: var(--fg);
  }

  .centers .on {
    color: var(--fg);
  }

  .centers button::after {
    position: absolute;
    right: 0;
    bottom: 0;
    left: 0;
    height: 1px;
    content: "";
    background: currentColor;
    box-shadow: 0 0 6px currentColor;
    transform: scaleX(0);
    transition: transform 160ms ease-out;
  }

  .centers button:hover::after {
    transform: scaleX(0.5);
  }

  .centers .on::after {
    transform: scaleX(1);
  }

  @media (prefers-reduced-motion: reduce) {
    .centers button::after {
      transition: none;
    }
  }

  .stage-map {
    grid-area: map;
    position: relative;
    display: grid;
    min-height: 0;
  }

  .corners {
    position: absolute;
    inset: 0;
    display: grid;
    grid-template-columns: 17rem 1fr 18rem;
    grid-template-rows: 4rem 1fr 4rem;
    padding: 0.7rem 0.9rem;
    pointer-events: none;
  }

  .slot {
    display: flex;
    flex-direction: column;
    gap: 0.15rem;
    min-width: 0;
  }

  .tl { grid-area: 1 / 1; }
  .tr { grid-area: 1 / 3; align-items: flex-end; text-align: right; }
  .bl { grid-area: 3 / 1; justify-content: flex-end; }
  .br { grid-area: 3 / 3; align-items: flex-end; justify-content: flex-end; }

  .key {
    color: var(--dim);
    font-size: 0.68rem;
    letter-spacing: 0.28em;
  }

  .value {
    color: var(--fg);
    font-size: 1.35rem;
    letter-spacing: 0.08em;
    white-space: nowrap;
    text-shadow: 0 0 6px var(--bg);
  }

  .side {
    grid-area: side;
    min-height: 0;
    overflow-y: auto;
    scrollbar-width: thin;
    scrollbar-color: var(--faint) transparent;
  }

  .list {
    grid-area: list;
    min-height: 0;
    overflow-y: auto;
    scrollbar-width: thin;
    scrollbar-color: var(--faint) transparent;
  }

  @media (max-width: 900px) {
    main {
      display: block;
      height: auto;
      padding: 0.75rem 16px;
    }

    h1 {
      font-size: 1.3rem;
    }

    .stage-map {
      height: 24rem;
      margin: 0.5rem 0;
    }

    .corners {
      grid-template-columns: 6rem 1fr 9rem;
      font-size: 0.8em;
    }

    .value {
      font-size: 1rem;
    }

    .side,
    .list {
      margin-bottom: 0.5rem;
      overflow: visible;
    }

    .centers {
      margin-left: 0;
    }
  }
</style>
