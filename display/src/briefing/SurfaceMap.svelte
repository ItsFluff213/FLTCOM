<script lang="ts">
  import { artUrl } from "../lib/connection.ts";
  import type { Body, SurfaceMarker } from "../protocol.gen.ts";
  import { formatSpot, fromPoint, markersOn, pickMarker, pickedSpot, toPoint, type LandingPick, type MapMarker } from "./surfaceMap.ts";

  interface Props {
    body: Body;
    markers: readonly SurfaceMarker[];
    pick: LandingPick;
    onpick: (pick: LandingPick) => void;
  }

  let { body, markers, pick, onpick }: Props = $props();

  const KIND_LABEL: Record<SurfaceMarker["kind"], string> = {
    site: "KSC SITE",
    waypoint: "WAYPOINT",
    contract: "CONTRACT",
    flag: "FLAG",
    vessel: "LANDED CRAFT",
  };
  const LATITUDES = [-60, -30, 0, 30, 60];
  const LONGITUDES = [-150, -120, -90, -60, -30, 0, 30, 60, 90, 120, 150];

  let hovered = $state<MapMarker | null>(null);
  let cursor = $state<{ latitudeDeg: number; longitudeDeg: number } | null>(null);
  let imageFailed = $state(false);

  const shown = $derived(markersOn(markers, body.name));
  const kinds = $derived([...new Set(shown.map((marker) => marker.kind))]);
  const spot = $derived(pickedSpot(pick));
  const spotPoint = $derived(spot ? toPoint(spot.latitudeDeg, spot.longitudeDeg) : null);
  const texture = $derived(body.texture && !imageFailed ? artUrl(body.texture, location) : null);
  $effect(() => {
    void body.texture;
    imageFailed = false;
  });

  function pointOf(event: MouseEvent): { x: number; y: number } {
    const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
    return { x: (event.clientX - box.left) / box.width, y: (event.clientY - box.top) / box.height };
  }

  function clickMap(event: MouseEvent) {
    const { x, y } = pointOf(event);
    onpick({ site: "custom", ...fromPoint(x, y) });
  }

  function moveOnMap(event: MouseEvent) {
    const { x, y } = pointOf(event);
    cursor = fromPoint(x, y);
  }

  const at = (point: { x: number; y: number }) => `left: ${point.x * 100}%; top: ${point.y * 100}%`;
</script>

<section class="surface panel" aria-label="Surface map" data-surface-map={body.name}>
  <header>
    <h3>SURFACE · {body.name.toUpperCase()}</h3>
    <p class="target" data-surface-target>
      {#if spot}
        TARGET <strong>{pick.site === "launchPad" ? "LAUNCH PAD" : pick.site === "runway" ? "RUNWAY" : formatSpot(spot.latitudeDeg, spot.longitudeDeg)}</strong>
      {:else}
        CLICK THE MAP TO PICK THE LANDING SPOT
      {/if}
    </p>
  </header>
  <div class="fit">
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="map" style:--body-color={body.color} onclick={clickMap} onmousemove={moveOnMap} onmouseleave={() => (cursor = null)} data-surface-canvas>
      {#if texture}
        <img src={texture} alt="" draggable="false" crossorigin="anonymous" onerror={() => (imageFailed = true)} />
      {/if}
      <svg viewBox="0 0 360 180" preserveAspectRatio="none" aria-hidden="true">
        {#each LATITUDES as latitude (latitude)}
          <line class:equator={latitude === 0} x1="0" x2="360" y1={90 - latitude} y2={90 - latitude} />
        {/each}
        {#each LONGITUDES as longitude (longitude)}
          <line class:equator={longitude === 0} x1={longitude + 180} x2={longitude + 180} y1="0" y2="180" />
        {/each}
      </svg>
      {#each LATITUDES as latitude (latitude)}
        <span class="tick lat" style:top="{((90 - latitude) / 180) * 100}%">{latitude === 0 ? "0" : `${Math.abs(latitude)}${latitude < 0 ? "S" : "N"}`}</span>
      {/each}
      {#each LONGITUDES as longitude (longitude)}
        <span class="tick lon" style:left="{((longitude + 180) / 360) * 100}%">{longitude === 0 ? "0" : `${Math.abs(longitude)}${longitude < 0 ? "W" : "E"}`}</span>
      {/each}
      {#if spotPoint}
        <div class="cross" style={at(spotPoint)} data-surface-crosshair>
          <span class="h"></span><span class="v"></span>
        </div>
      {/if}
      {#each shown as marker (marker.id)}
        <button
          type="button"
          class="marker {marker.kind}"
          style={at(toPoint(marker.latitudeDeg, marker.longitudeDeg))}
          aria-label="{KIND_LABEL[marker.kind]} {marker.name}, {formatSpot(marker.latitudeDeg, marker.longitudeDeg)}"
          data-marker-id={marker.id}
          onclick={(event) => {
            event.stopPropagation();
            onpick(pickMarker(marker));
          }}
          onmouseenter={() => (hovered = marker)}
          onmouseleave={() => (hovered = null)}
          onfocus={() => (hovered = marker)}
          onblur={() => (hovered = null)}
        ></button>
      {/each}
      {#if hovered}
        {@const point = toPoint(hovered.latitudeDeg, hovered.longitudeDeg)}
        <div class="tip" class:left={point.x > 0.7} class:below={point.y < 0.2} style={at(point)} role="tooltip" data-surface-tip>
          <strong>{hovered.name.toUpperCase()}</strong>
          <span>{KIND_LABEL[hovered.kind]} · {formatSpot(hovered.latitudeDeg, hovered.longitudeDeg)}</span>
        </div>
      {/if}
    </div>
  </div>
  <footer>
    <ul class="legend">
      {#each kinds as kind (kind)}
        <li><span class="marker {kind} key"></span>{KIND_LABEL[kind]}</li>
      {/each}
      {#if kinds.length === 0}
        <li class="none">NO MARKERS ON {body.name.toUpperCase()}</li>
      {/if}
    </ul>
    <span class="cursor" data-surface-cursor>{cursor ? formatSpot(cursor.latitudeDeg, cursor.longitudeDeg) : ""}</span>
  </footer>
</section>

<style>
  .surface {
    position: absolute;
    inset: 0;
    z-index: 2;
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
    padding: 0.6rem 0.8rem;
    background: var(--bg);
  }

  header,
  footer {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: 0.25rem 1rem;
  }

  h3 {
    margin: 0;
    padding-left: 0.45rem;
    border-left: 2px solid var(--fg);
    font: 0.8rem var(--font-ui);
    letter-spacing: 0.32em;
  }

  .target {
    margin: 0;
    color: var(--dim);
    font-size: 0.72rem;
    letter-spacing: 0.18em;
  }

  .target strong {
    color: var(--role-planned);
    font-weight: normal;
  }

  .fit {
    flex: 1;
    min-height: 0;
    display: grid;
    place-items: center;
    container-type: size;
  }

  .map {
    position: relative;
    width: min(100cqw, 200cqh);
    aspect-ratio: 2 / 1;
    overflow: hidden;
    border: 1px solid var(--rule);
    background: color-mix(in srgb, var(--body-color) 22%, var(--bg));
    cursor: crosshair;
  }

  img,
  svg {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    pointer-events: none;
    user-select: none;
  }

  img {
    object-fit: fill;
    filter: saturate(0.55) brightness(0.8);
  }

  line {
    stroke: var(--fg);
    stroke-opacity: 0.18;
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
  }

  line.equator {
    stroke-opacity: 0.4;
  }

  .tick {
    position: absolute;
    padding: 0 0.15rem;
    color: var(--dim);
    background: color-mix(in srgb, var(--bg) 75%, transparent);
    font-size: 0.6rem;
    letter-spacing: 0.08em;
    pointer-events: none;
  }

  .tick.lat {
    left: 0.2rem;
    transform: translateY(-110%);
  }

  .tick.lon {
    bottom: 0.15rem;
    transform: translateX(0.15rem);
  }

  .cross {
    position: absolute;
    width: 0;
    height: 0;
    pointer-events: none;
  }

  .cross .h,
  .cross .v {
    position: absolute;
    background: var(--role-planned);
    box-shadow: 0 0 4px var(--role-planned);
  }

  .cross .h {
    left: -1.1rem;
    width: 2.2rem;
    top: -0.5px;
    height: 1px;
  }

  .cross .v {
    top: -1.1rem;
    height: 2.2rem;
    left: -0.5px;
    width: 1px;
  }

  .cross::after {
    content: "";
    position: absolute;
    left: -0.45rem;
    top: -0.45rem;
    width: 0.9rem;
    height: 0.9rem;
    border: 1px solid var(--role-planned);
    border-radius: 50%;
  }

  .marker {
    --size: 0.7rem;
    position: absolute;
    width: var(--size);
    height: var(--size);
    min-height: 0;
    margin: calc(var(--size) / -2) 0 0 calc(var(--size) / -2);
    padding: 0;
    border: 1px solid var(--bg);
    background: var(--fg);
    cursor: pointer;
    transition: scale 120ms ease-out;
  }

  .map .marker:hover,
  .map .marker:focus-visible {
    outline: 1px solid var(--fg);
    outline-offset: 2px;
    scale: 1.45;
  }

  .map .marker:active {
    scale: 1.15;
  }

  @media (prefers-reduced-motion: reduce) {
    .marker {
      transition: none;
    }
  }

  .marker.site {
    background: var(--fg);
  }

  .marker.waypoint {
    background: var(--role-operation);
    transform: rotate(45deg);
  }

  .marker.contract {
    background: transparent;
    border: 2px solid var(--role-operation);
    transform: rotate(45deg);
  }

  .marker.flag {
    background: var(--fg);
    clip-path: polygon(0 0, 100% 30%, 15% 60%, 15% 100%, 0 100%);
    border: 0;
  }

  .marker.vessel {
    background: var(--role-selected);
    border-radius: 50%;
  }

  .tip {
    position: absolute;
    z-index: 1;
    display: grid;
    gap: 0.1rem;
    padding: 0.3rem 0.45rem;
    border: 1px solid var(--rule);
    background: color-mix(in srgb, var(--bg) 88%, transparent);
    font-size: 0.65rem;
    letter-spacing: 0.1em;
    white-space: nowrap;
    pointer-events: none;
    transform: translate(0.7rem, -120%);
  }

  .tip.left {
    transform: translate(calc(-100% - 0.7rem), -120%);
  }

  .tip.below {
    transform: translate(0.7rem, 0.7rem);
  }

  .tip.left.below {
    transform: translate(calc(-100% - 0.7rem), 0.7rem);
  }

  .tip span {
    color: var(--dim);
  }

  .legend {
    display: flex;
    flex-wrap: wrap;
    gap: 0.2rem 0.9rem;
    margin: 0;
    padding: 0;
    list-style: none;
    color: var(--dim);
    font-size: 0.62rem;
    letter-spacing: 0.16em;
  }

  .legend li {
    display: flex;
    align-items: center;
    gap: 0.4rem;
  }

  .marker.key {
    position: static;
    --size: 0.55rem;
    margin: 0;
    cursor: default;
  }

  .cursor {
    color: var(--dim);
    font-size: 0.65rem;
    letter-spacing: 0.12em;
    min-width: 10rem;
    text-align: right;
  }
</style>
