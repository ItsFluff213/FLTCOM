<script lang="ts" module>
  import type { Body, ChangeKind, DockingPortInfo, EndOfLife, LandingSite, OrbitChangeRequest } from "../protocol.gen.ts";

  export const KINDS: readonly { kind: ChangeKind; label: string; needs: "altitude" | "inclination" | "body" | "target" | "landing" | null; groupOnly?: true; vesselOnly?: true }[] = [
    { kind: "circularize", label: "CIRCULARIZE", needs: null },
    { kind: "changeApoapsis", label: "CHANGE APOAPSIS", needs: "altitude" },
    { kind: "changePeriapsis", label: "CHANGE PERIAPSIS", needs: "altitude" },
    { kind: "circularOrbit", label: "CIRCULAR ORBIT AT ALTITUDE", needs: "altitude" },
    { kind: "changeInclination", label: "CHANGE INCLINATION", needs: "inclination" },
    { kind: "standardLowOrbit", label: "STANDARD LOW ORBIT", needs: null },
    { kind: "raiseApoapsis", label: "RAISE APOAPSIS BY", needs: "altitude" },
    { kind: "spreadEvenly", label: "SPREAD EVENLY AROUND ONE ORBIT", needs: "altitude", groupOnly: true },
    { kind: "transfer", label: "TRANSFER TO BODY", needs: "body", vesselOnly: true },
    { kind: "rendezvous", label: "RENDEZVOUS WITH", needs: "target", vesselOnly: true },
    { kind: "dock", label: "DOCK WITH", needs: "target", vesselOnly: true },
    { kind: "land", label: "LAND", needs: "landing", vesselOnly: true },
    { kind: "deployConstellation", label: "DEPLOY CONSTELLATION", needs: "altitude", vesselOnly: true },
  ];

  /** The altitude in km where one orbit takes one turn of the body (geostationary at Kerbin), or null for a body that does not turn. */
  export function synchronousKm(body: Pick<Body, "gravParameter" | "radius" | "rotationPeriod">): number | null {
    if (!(body.rotationPeriod > 0)) {
      return null;
    }
    return Math.round((Math.cbrt((body.gravParameter * body.rotationPeriod ** 2) / (4 * Math.PI ** 2)) - body.radius) / 1000);
  }

  /** A transfer (needs "body") ends in a circular orbit altitudeKm above body, inclined inclinationDeg there; a blank inclination takes the pass as it comes. */
  export function toRequest(kind: ChangeKind, altitudeKm: number, inclinationDeg: number | null, body: string | null = null): OrbitChangeRequest | null {
    const needs = KINDS.find((entry) => entry.kind === kind)?.needs;
    const usesAltitude = needs === "altitude" || needs === "body";
    const inclined = inclinationDeg !== null && Number.isFinite(inclinationDeg);
    if ((usesAltitude && !(altitudeKm > 0)) || (needs === "inclination" && !inclined) || (needs === "body" && !body) || needs === "target") {
      return null;
    }
    if (needs === "body" && inclined && (inclinationDeg < 0 || inclinationDeg > 180)) {
      return null;
    }
    return {
      kind,
      altitude: usesAltitude ? altitudeKm * 1000 : null,
      periapsis: null,
      inclination: (needs === "inclination" || needs === "body") && inclined ? (inclinationDeg * Math.PI) / 180 : null,
      lan: null,
      body: needs === "body" ? body : null,
      targetVesselId: null,
      targetPortId: null,
      landing: null,
      autoSystems: null,
      endOfLife: null,
    };
  }

  export const RING_PRESETS: readonly { label: string; inclinationDeg: number }[] = [
    { label: "EQUATORIAL", inclinationDeg: 0 },
    { label: "POLAR", inclinationDeg: 90 },
  ];

  /** What the transfer stage does once its satellites are out; AUTO (null) burns it up where there is air and delta-v, else parks it. */
  export const END_OF_LIFE: readonly { endOfLife: EndOfLife | null; label: string }[] = [
    { endOfLife: null, label: "AUTO" },
    { endOfLife: "burnUp", label: "BURN UP" },
    { endOfLife: "landHome", label: "LAND HOME" },
    { endOfLife: "park", label: "PARK" },
  ];

  /** A constellation ring altitudeKm up, inclined ringInclinationDeg (0 to 180) with its ascending node at ringNodeDeg; 0 is the equatorial default and sends no plane. */
  export function toDeployRequest(altitudeKm: number, ringInclinationDeg: number | null, ringNodeDeg: number | null, endOfLife: EndOfLife | null = null): OrbitChangeRequest | null {
    const base = toRequest("deployConstellation", altitudeKm, null);
    const request = base === null ? null : { ...base, endOfLife };
    const inclination = ringInclinationDeg ?? 0;
    if (request === null || !Number.isFinite(inclination) || inclination < 0 || inclination > 180) {
      return null;
    }
    const node = ringNodeDeg !== null && Number.isFinite(ringNodeDeg) ? ringNodeDeg : 0;
    return inclination === 0 ? request : { ...request, inclination: (inclination * Math.PI) / 180, lan: (node * Math.PI) / 180 };
  }

  export interface RendezvousChoice {
    readonly id: number;
    readonly name: string;
    readonly ports?: readonly DockingPortInfo[];
  }

  /** The ports the operator may pick on a dock target: free ones only; an empty list leaves the choice to the plugin. */
  export const freePortsOf = (targets: readonly RendezvousChoice[], targetId: number | null): DockingPortInfo[] =>
    targets.find((choice) => choice.id === targetId)?.ports?.filter((port) => port.free) ?? [];

  /** A rendezvous or dock with targetId; a dock may name the target's port, null leaves the pick to the plugin. */
  export function toMeetRequest(kind: "rendezvous" | "dock", targetId: number | null, portId: number | null = null): OrbitChangeRequest | null {
    if (targetId === null) {
      return null;
    }
    return { kind, altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: targetId, targetPortId: kind === "dock" ? portId : null, landing: null, autoSystems: null, endOfLife: null };
  }

  const blank = (value: number | null | undefined): value is null | undefined => value === null || value === undefined || !Number.isFinite(value);

  /** A launch from the pad: apoapsis and periapsis in km, inclination and LAN in degrees; blank periapsis means circular, blank angles mean due east and no wait. */
  export function toLaunchRequest(apoapsisKm: number | null, periapsisKm: number | null, inclinationDeg: number | null, lanDeg: number | null): OrbitChangeRequest | null {
    if (blank(apoapsisKm) || !(apoapsisKm > 0) || (!blank(periapsisKm) && !(periapsisKm > 0))) {
      return null;
    }
    if (!blank(lanDeg) && blank(inclinationDeg)) {
      return null;
    }
    if (!blank(inclinationDeg) && (inclinationDeg < 0 || inclinationDeg > 180)) {
      return null;
    }
    const rad = (deg: number) => (deg * Math.PI) / 180;
    return {
      kind: "launch",
      altitude: apoapsisKm * 1000,
      periapsis: blank(periapsisKm) ? null : periapsisKm * 1000,
      inclination: blank(inclinationDeg) ? null : rad(inclinationDeg),
      lan: blank(lanDeg) ? null : rad(lanDeg),
      body: null,
      targetVesselId: null,
      targetPortId: null,
      landing: null,
      autoSystems: null,
      endOfLife: null,
    };
  }

  export const LANDING_SITES: readonly { site: LandingSite; label: string }[] = [
    { site: "launchPad", label: "LAUNCH PAD" },
    { site: "runway", label: "RUNWAY" },
    { site: "custom", label: "CUSTOM LAT/LON" },
  ];

  /** A landing at the pad, the runway, or custom coordinates in degrees (latitude -90..90, longitude -180..180, east positive). */
  export function toLandRequest(site: LandingSite, latitudeDeg: number | null, longitudeDeg: number | null): OrbitChangeRequest | null {
    const rad = (deg: number) => (deg * Math.PI) / 180;
    let latitude = 0;
    let longitude = 0;
    if (site === "custom") {
      if (blank(latitudeDeg) || blank(longitudeDeg) || Math.abs(latitudeDeg) > 90 || Math.abs(longitudeDeg) > 180) {
        return null;
      }
      latitude = rad(latitudeDeg);
      longitude = rad(longitudeDeg);
    }
    return { kind: "land", altitude: null, periapsis: null, inclination: null, lan: null, body: null, targetVesselId: null, targetPortId: null, landing: { site, latitude, longitude }, autoSystems: null, endOfLife: null };
  }
</script>

<script lang="ts">
  import type { Tracked } from "../lib/acks.ts";
  interface Props {
    vesselName: string | null;
    groupName?: string | null;
    /** The selected vessel stands on the launch pad: the only operation offered is a launch. */
    onPad?: boolean;
    /** Bodies a transfer can go to. */
    bodies?: readonly string[];
    /** Vessels the selected one can meet: in orbit around the same body. */
    targets?: readonly RendezvousChoice[];
    busy: boolean;
    /** The answer to the last BRIEF: confirmed, or refused with the reason. */
    ack?: Tracked | null;
    onbrief: (change: OrbitChangeRequest) => void;
    /** The chosen operation; bound so the page can open the surface map while it is LAND. */
    kind?: ChangeKind;
    /** The landing target; bound so a click on the surface map fills it. */
    site?: LandingSite;
    latitudeDeg?: number | null;
    longitudeDeg?: number | null;
    /** The body the selected vessel orbits: a constellation is deployed around it. */
    orbitBody?: Body | null;
  }

  let {
    vesselName,
    groupName = null,
    onPad = false,
    bodies = [],
    targets = [],
    busy,
    ack = null,
    onbrief,
    kind = $bindable("circularize"),
    site = $bindable("launchPad"),
    latitudeDeg = $bindable(null),
    longitudeDeg = $bindable(null),
    orbitBody = null,
  }: Props = $props();

  let altitudeKm = $state(100);
  let inclinationDeg = $state(0);
  let apoapsisKm = $state<number | null>(80);
  let periapsisKm = $state<number | null>(null);
  let launchInclinationDeg = $state<number | null>(null);
  let lanDeg = $state<number | null>(null);
  let body = $state<string | null>(null);
  let arrivalInclinationDeg = $state<number | null>(null);
  let ringInclinationDeg = $state<number | null>(0);
  let ringNodeDeg = $state<number | null>(0);
  let endOfLife = $state<EndOfLife | null>(null);
  let targetId = $state<number | null>(null);
  let portId = $state<number | null>(null);
  let autoSystems = $state(true);

  const launching = $derived(onPad && groupName === null);
  const kinds = $derived(KINDS.filter((entry) => (groupName === null ? !entry.groupOnly : !entry.vesselOnly)));
  $effect(() => {
    if (body === null || !bodies.includes(body)) {
      body = bodies[0] ?? null;
    }
  });
  $effect(() => {
    if (targetId !== null && !targets.some((choice) => choice.id === targetId)) {
      targetId = null;
    }
  });
  const freePorts = $derived(freePortsOf(targets, targetId));
  $effect(() => {
    if (portId !== null && !freePorts.some((port) => port.id === portId)) {
      portId = null;
    }
  });
  $effect(() => {
    if (!kinds.some((entry) => entry.kind === kind)) {
      kind = "circularize";
    }
  });
  const synchronous = $derived(orbitBody ? synchronousKm(orbitBody) : null);
  let filledFor: string | null = null;
  $effect(() => {
    const key = kind === "deployConstellation" && orbitBody ? orbitBody.name : null;
    if (key !== null && key !== filledFor && synchronous !== null) {
      altitudeKm = synchronous;
    }
    filledFor = key;
  });
  const target = $derived(groupName ?? vesselName);
  const needs = $derived(KINDS.find((entry) => entry.kind === kind)?.needs ?? null);
  const request = $derived(
    launching
      ? toLaunchRequest(apoapsisKm, periapsisKm, launchInclinationDeg, lanDeg)
      : kind === "rendezvous" || kind === "dock"
        ? toMeetRequest(kind, targetId, portId)
        : kind === "land"
          ? toLandRequest(site, latitudeDeg, longitudeDeg)
          : kind === "deployConstellation"
            ? toDeployRequest(altitudeKm, ringInclinationDeg, ringNodeDeg, endOfLife)
            : toRequest(kind, altitudeKm, needs === "body" ? arrivalInclinationDeg : inclinationDeg, body),
  );
  const ringName = $derived(RING_PRESETS.find((preset) => preset.inclinationDeg === (ringInclinationDeg ?? 0))?.label ?? `INCLINED ${ringInclinationDeg} DEG`);
</script>

<section class="composer" aria-label="Compose orbit change" data-composer>
  <h3>{groupName !== null ? "COMPOSE GROUP CHANGE" : launching ? "COMPOSE LAUNCH" : "COMPOSE ORBIT CHANGE"}</h3>
  <p class="who" data-compose-target>{target ? target.toUpperCase() : "NO VESSEL SELECTED"}</p>
  {#if groupName !== null}
    <p class="hint" data-compose-hint>{kind === "spreadEvenly" ? "ONE CIRCULAR ORBIT, SHIPS EQUALLY SPACED AROUND IT" : "THE SAME CHANGE FOR EVERY SHIP, EACH WITH ITS OWN BURNS"}</p>
  {/if}
  {#if launching}
    <p class="hint" data-compose-hint>LAUNCH TO ORBIT. BLANK PERIAPSIS: CIRCULAR. BLANK INCLINATION: DUE EAST. A LAN WAITS FOR THE PLANE.</p>
    <div class="pair">
      <label>
        <span>APOAPSIS KM</span>
        <input type="number" min="1" step="1" bind:value={apoapsisKm} data-launch-apoapsis />
      </label>
      <label>
        <span>PERIAPSIS KM</span>
        <input type="number" min="1" step="1" placeholder="= APOAPSIS" bind:value={periapsisKm} data-launch-periapsis />
      </label>
      <label>
        <span>INCLINATION DEG</span>
        <input type="number" min="0" max="180" step="0.1" placeholder="EAST" bind:value={launchInclinationDeg} data-launch-inclination />
      </label>
      <label>
        <span>LAN DEG</span>
        <input type="number" min="0" max="360" step="0.1" placeholder="ANY" bind:value={lanDeg} data-launch-lan />
      </label>
    </div>
  {:else}
    <label>
      <span>OPERATION</span>
      <select bind:value={kind} data-compose-kind>
        {#each kinds as entry (entry.kind)}
          <option value={entry.kind}>{entry.label}</option>
        {/each}
      </select>
    </label>
    {#if kind === "deployConstellation"}
      <p class="hint" data-compose-hint>
        AROUND {orbitBody ? orbitBody.name.toUpperCase() : "THE BODY IT ORBITS"}, {ringName}, EVENLY SPACED.
        {synchronous !== null ? `SYNCHRONOUS ORBIT ${synchronous.toLocaleString("en-US")} KM.` : ""}
        THE BRIEFING COUNTS THE SATELLITES ON THE CARRIER, EACH WITH A PROBE CORE OR CREWED POD, ANY PLANE CHANGE, AND ITS END OF LIFE. AUTO BURNS THE TRANSFER STAGE UP WHERE THERE IS AIR, ELSE PARKS IT.
      </p>
      <div class="presets" role="group" aria-label="Ring plane">
        {#each RING_PRESETS as preset (preset.label)}
          <button type="button" class="preset" aria-pressed={(ringInclinationDeg ?? 0) === preset.inclinationDeg} onclick={() => (ringInclinationDeg = preset.inclinationDeg)} data-ring-preset={preset.label}>{preset.label}</button>
        {/each}
      </div>
      <div class="pair">
        <label>
          <span>RING INCLINATION DEG</span>
          <input type="number" min="0" max="180" step="0.1" bind:value={ringInclinationDeg} data-ring-inclination />
        </label>
        <label>
          <span>ASCENDING NODE DEG</span>
          <input type="number" min="0" max="360" step="0.1" disabled={(ringInclinationDeg ?? 0) === 0} bind:value={ringNodeDeg} data-ring-node />
        </label>
      </div>
      <span class="hint">END OF LIFE</span>
      <div class="presets" role="group" aria-label="End of life" data-end-of-life>
        {#each END_OF_LIFE as choice (choice.label)}
          <button type="button" class="preset" aria-pressed={endOfLife === choice.endOfLife} onclick={() => (endOfLife = choice.endOfLife)} data-end-of-life-choice={choice.label}>{choice.label}</button>
        {/each}
      </div>
    {/if}
    {#if needs === "body"}
      <label>
        <span>BODY</span>
        <select bind:value={body} data-compose-body>
          {#each bodies as name (name)}
            <option value={name}>{name.toUpperCase()}</option>
          {/each}
        </select>
      </label>
    {/if}
    {#if needs === "altitude" || needs === "body"}
      <label>
        <span>{kind === "raiseApoapsis" ? "RAISE BY KM" : kind === "transfer" ? "ORBIT ALTITUDE KM" : "ALTITUDE KM"}</span>
        <input type="number" min="1" step="1" bind:value={altitudeKm} data-compose-altitude />
      </label>
      {#if needs === "body"}
        <label>
          <span>INCLINATION DEG THERE</span>
          <input type="number" min="0" max="180" step="0.1" placeholder="AS IT COMES" bind:value={arrivalInclinationDeg} data-compose-arrival-inclination />
        </label>
      {/if}
    {:else if needs === "inclination"}
      <label>
        <span>INCLINATION DEG</span>
        <input type="number" min="0" max="180" step="0.1" bind:value={inclinationDeg} data-compose-inclination />
      </label>
    {:else if needs === "target"}
      <label>
        <span>TARGET VESSEL</span>
        <select bind:value={targetId} data-compose-rendezvous-target>
          <option value={null}>{targets.length === 0 ? "NOTHING ELSE IN THIS ORBIT" : "PICK A TARGET"}</option>
          {#each targets as choice (choice.id)}
            <option value={choice.id}>{choice.name.toUpperCase()}</option>
          {/each}
        </select>
      </label>
      {#if kind === "dock" && targetId !== null}
        <label>
          <span>TARGET PORT</span>
          <select bind:value={portId} data-compose-dock-port>
            <option value={null}>{freePorts.length === 0 ? "NO FREE PORT KNOWN" : "AUTO"}</option>
            {#each freePorts as port, index (port.id)}
              <option value={port.id}>{index + 1}. {port.name.toUpperCase()} ({port.size.toUpperCase()})</option>
            {/each}
          </select>
        </label>
      {/if}
    {:else if needs === "landing"}
      <p class="hint" data-compose-hint>CLICK THE SURFACE MAP OR ONE OF ITS MARKERS, OR PICK A SITE HERE</p>
      <label>
        <span>LAND AT</span>
        <select bind:value={site} data-land-site>
          {#each LANDING_SITES as entry (entry.site)}
            <option value={entry.site}>{entry.label}</option>
          {/each}
        </select>
      </label>
      {#if site === "custom"}
        <div class="pair">
          <label>
            <span>LATITUDE DEG</span>
            <input type="number" min="-90" max="90" step="0.01" placeholder="N +" bind:value={latitudeDeg} data-land-latitude />
          </label>
          <label>
            <span>LONGITUDE DEG</span>
            <input type="number" min="-180" max="180" step="0.01" placeholder="E +" bind:value={longitudeDeg} data-land-longitude />
          </label>
        </div>
      {/if}
    {/if}
  {/if}
  <label class="switch ctl" title="Stage in every burn, work landing gear, fairings, solar panels, antennas and radiators">
    <input type="checkbox" bind:checked={autoSystems} data-compose-auto />
    <span class="track" aria-hidden="true"><span class="knob"></span></span>
    <span>AUTO SYSTEMS</span>
    {#key autoSystems}<span class="state" class:off={!autoSystems}>{autoSystems ? "ON" : "OFF"}</span>{/key}
  </label>
  <button type="button" class="ctl" data-compose-brief data-ack={ack?.status === "pending" ? undefined : ack?.status} disabled={!target || !request || busy} onclick={() => request && onbrief({ ...request, autoSystems })}>{busy ? "PLANNING..." : launching ? "BRIEF LAUNCH" : "BRIEF"}</button>
  {#if ack?.status === "refused"}
    <p class="refusal" role="status" data-refusal="brief">{ack.reason?.toUpperCase()}</p>
  {/if}
</section>

<style>
  .composer {
    display: grid;
    gap: 0.35rem;
    padding: 0.5rem 0.75rem;
    border-top: 1px solid var(--rule);
  }

  h3 {
    margin: 0;
    font-family: var(--font-ui);
    padding-left: 0.45rem;
    border-left: 2px solid var(--fg);
    font-size: 0.8rem;
    letter-spacing: 0.32em;
  }

  .who {
    margin: 0;
    color: #39ff88;
    font-size: 0.8rem;
  }

  .hint {
    margin: 0;
    font-size: 0.65rem;
    opacity: 0.75;
  }

  .pair {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0.35rem;
  }

  label {
    display: grid;
    gap: 0.1rem;
    font-size: 0.65rem;
    letter-spacing: 0.12em;
    opacity: 0.9;
  }

  .switch {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    cursor: pointer;
  }

  .switch {
    padding: 0.2rem 0.3rem;
    color: #39ff88;
  }

  .switch > span:not(.track, .state) {
    color: var(--fg);
  }

  .switch input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }

  .track {
    position: relative;
    flex: none;
    width: 1.9rem;
    height: 0.9rem;
    border: 1px solid currentColor;
  }

  .track::before {
    position: absolute;
    inset: 0;
    content: "";
    background: currentColor;
    opacity: 0.18;
    transition: opacity 160ms ease-out;
  }

  .knob {
    position: absolute;
    top: 2px;
    left: 2px;
    width: calc(0.9rem - 6px);
    height: calc(0.9rem - 6px);
    background: currentColor;
    box-shadow: 0 0 6px currentColor;
    transform: translateX(calc(1.9rem - 0.9rem));
    transition: transform 160ms cubic-bezier(0.3, 1.4, 0.5, 1);
  }

  .switch:has(input:not(:checked)) {
    color: var(--dim);
  }

  .switch:has(input:not(:checked)) .knob {
    box-shadow: none;
    transform: none;
  }

  .switch:has(input:not(:checked)) .track::before {
    opacity: 0;
  }

  .switch .state {
    display: inline-block;
    margin-left: auto;
    animation: flip 220ms ease-out;
  }

  .switch .state.off {
    color: var(--dim);
  }

  @keyframes flip {
    from {
      opacity: 0;
      transform: translateY(-4px);
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .knob,
    .track::before,
    .switch .state {
      animation: none;
      transition: none;
    }
  }

  select,
  input,
  button {
    min-height: 1.8rem;
    border: 1px solid var(--rule);
    background: #151211;
    color: inherit;
    font: 0.8rem var(--font-ui);
  }

  button {
    cursor: pointer;
    letter-spacing: 0.15em;
    color: #ffd84a;
    border-color: #ffd84a;
  }

  button:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }

  .presets {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0.35rem;
  }

  .preset {
    color: var(--dim);
    border-color: var(--rule);
  }

  .preset[aria-pressed="true"] {
    color: #39ff88;
    border-color: #39ff88;
    box-shadow: 0 0 6px rgb(57 255 136 / 35%);
  }

  input:disabled {
    opacity: 0.4;
  }
</style>
