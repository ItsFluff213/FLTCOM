<script lang="ts">
  import { untrack } from "svelte";
  import { ROLE_COLOR } from "../map/Scene.ts";
  import { orbitStyle } from "../map/marks.ts";
  import type { Body, BriefingMessage, Vessel } from "../protocol.gen.ts";
  import { draw, maneuverPreview, type Stroke } from "./maneuverPreview.ts";

  interface Props {
    briefing: BriefingMessage;
    vessel: Vessel | null;
    bodies: ReadonlyMap<string, Body>;
    ut: number;
  }

  let { briefing, vessel, bodies, ut }: Props = $props();

  const W = 560;
  const H = 210;
  const ROW = "[data-timeline] > li[data-step]";

  // Drawn once per briefing: the fleet ticks twice a second but the plan only changes with a new briefing.
  const drawing = $derived.by(() => {
    const plan = briefing;
    return untrack(() => {
      const preview = maneuverPreview(plan, vessel, bodies, ut);
      return preview && draw(preview, W, H);
    });
  });

  interface Line extends Stroke {
    key: string;
    index: number | null;
    color: string;
    width: number;
    opacity: number;
    dashed: boolean;
  }

  const lines = $derived.by((): Line[] => {
    if (!drawing) {
      return [];
    }
    const now = orbitStyle({ id: "vessel", role: "selected" });
    const after = orbitStyle({ id: "after", role: "planned" });
    return [
      ...drawing.ring.map((stroke, i) => ({ ...stroke, key: `ring:${i}`, index: null, color: ROLE_COLOR.planned, width: after.width / 3, opacity: 0.55, dashed: true })),
      ...drawing.current.map((stroke, i) => ({ ...stroke, key: `now:${i}`, index: null, color: ROLE_COLOR.selected, width: now.width / 2, opacity: now.opacity * 0.7, dashed: false })),
      ...drawing.burns.flatMap((burn) =>
        burn.strokes.map((stroke, i) => ({
          ...stroke,
          key: `${burn.index}:${i}`,
          index: burn.index,
          color: burn.final && drawing.low ? ROLE_COLOR.alert : ROLE_COLOR.planned,
          width: after.width / 2,
          opacity: burn.final ? 1 : after.opacity * 0.8,
          dashed: !burn.final && after.dashed,
        })),
      ),
    ];
  });

  let hot = $state<number | null>(null);
  let root = $state<HTMLElement>();
  const rows = () => [...(root?.closest("[data-briefing]")?.querySelectorAll<HTMLElement>(ROW) ?? [])];

  $effect(() => {
    const card = root?.closest<HTMLElement>("[data-briefing]");
    if (!card) {
      return;
    }
    const over = (event: PointerEvent) => {
      const row = event.target instanceof Element ? event.target.closest<HTMLElement>(ROW) : null;
      if (row) {
        hot = rows().indexOf(row);
      } else if (!(event.target instanceof Node && root?.contains(event.target))) {
        hot = null;
      }
    };
    card.addEventListener("pointerover", over);
    return () => card.removeEventListener("pointerover", over);
  });

  $effect(() => {
    void drawing;
    const lit = hot;
    rows().forEach((row, i) => row.toggleAttribute("data-preview-hot", i === lit));
  });

  const hotLabel = $derived(hot === null ? null : (drawing?.burns.find((burn) => burn.index === hot)?.label ?? null));
  const markColor = (final: boolean) => (final && drawing?.low ? ROLE_COLOR.alert : ROLE_COLOR.planned);
</script>

{#snippet strokes(behind: boolean)}
  {#each lines.filter((line) => line.behind === behind) as line (line.key)}
    <path
      d={line.d}
      stroke={line.color}
      stroke-width={line.width}
      stroke-opacity={behind ? line.opacity * 0.45 : line.opacity}
      stroke-dasharray={line.dashed ? "7 5" : undefined}
      class:dim={hot !== null && line.index !== hot}
      class:lit={hot !== null && line.index === hot}
      data-preview-leg={line.index ?? "now"}
    />
  {/each}
{/snippet}

{#if drawing}
  <figure class="preview" bind:this={root} data-maneuver-preview data-low={drawing.low ? "" : undefined} data-hot={hot ?? undefined}>
    <svg viewBox="0 0 {W} {H}" role="img" aria-label="Planned maneuver, {drawing.burns.length} burn{drawing.burns.length === 1 ? '' : 's'}">
      {#if drawing.center.floor > drawing.center.r + 0.5}
        <circle class="floor" cx={W / 2} cy={H / 2} r={drawing.center.floor} stroke={ROLE_COLOR.alert} data-preview-floor />
      {/if}
      {@render strokes(true)}
      <circle cx={W / 2} cy={H / 2} r={drawing.center.r} fill="var(--bg)" />
      <circle cx={W / 2} cy={H / 2} r={drawing.center.r} fill={drawing.center.color} fill-opacity="0.22" stroke={ROLE_COLOR.body} stroke-width="1" data-preview-body />
      {@render strokes(false)}
      {#each drawing.bodies as body (body.name)}
        <circle cx={body.x} cy={body.y} r={body.r} fill={body.color} fill-opacity="0.5" stroke={ROLE_COLOR.body} stroke-width="1" />
        <text class="name" fill={ROLE_COLOR.body} x={body.x + body.r + 4} y={body.y - body.r - 2}>{body.name.toUpperCase()}</text>
      {/each}
      {#each drawing.slots as slot, i (i)}
        <rect class="slot" x={slot.x - 4} y={slot.y - 4} width="8" height="8" transform="rotate(45 {slot.x.toFixed(1)} {slot.y.toFixed(1)})" stroke={ROLE_COLOR.planned} data-preview-slot={i + 1} />
      {/each}
      {#if drawing.touchdown}
        <path class="touchdown" d="M{drawing.touchdown.x - 5},{drawing.touchdown.y - 5}l10,10m0,-10l-10,10" stroke={ROLE_COLOR.planned} data-preview-touchdown />
      {/if}
      {#each drawing.burns as burn (burn.index)}
        {#if burn.mark}
          <g
            class="mark"
            class:dim={hot !== null && burn.index !== hot}
            class:lit={burn.index === hot}
            transform="translate({burn.mark.x.toFixed(1)} {burn.mark.y.toFixed(1)})"
            role="presentation"
            onpointerenter={() => (hot = burn.index)}
            onpointerleave={() => (hot = null)}
            data-preview-burn={burn.index}
          >
            <circle r="7" stroke={markColor(burn.final)} />
            <text y="3.2" fill={markColor(burn.final)}>{burn.index + 1}</text>
          </g>
        {/if}
      {/each}
    </svg>
    <figcaption style:color={ROLE_COLOR.planned}>
      {#if hotLabel !== null && hot !== null}
        <span data-preview-caption>BURN {hot + 1} · {hotLabel.toUpperCase()}</span>
      {:else}
        <span><i class="swatch" style:border-top-color={ROLE_COLOR.selected}></i>NOW</span>
        <span><i class="swatch after" style:border-top-color={ROLE_COLOR.planned}></i>AFTER A BURN</span>
        {#if drawing.slots.length > 0}<span><i class="swatch slot-key" style:border-color={ROLE_COLOR.planned}></i>SATELLITE SLOTS</span>{/if}
        <span style:color={drawing.low ? ROLE_COLOR.alert : null}><i class="swatch" style:border-top-color={drawing.low ? ROLE_COLOR.alert : ROLE_COLOR.planned}></i>{drawing.low ? "FINAL · UNDER THE SAFE FLOOR" : "FINAL"}</span>
      {/if}
    </figcaption>
  </figure>
{/if}

<style>
  .slot {
    fill: var(--bg);
    stroke-width: 1.5;
  }

  .swatch.slot-key {
    width: 0.45rem;
    height: 0.45rem;
    border: 1.5px solid;
    transform: rotate(45deg);
  }

  .preview {
    margin: 0.3rem 0 0;
  }

  svg {
    display: block;
    width: 100%;
    height: auto;
    overflow: visible;
  }

  path {
    fill: none;
    stroke-linejoin: round;
    stroke-linecap: round;
    transition:
      stroke-opacity 120ms,
      stroke-width 120ms,
      opacity 120ms;
  }

  .floor {
    fill: none;
    stroke-width: 1;
    stroke-opacity: 0.55;
    stroke-dasharray: 2 4;
  }

  .touchdown {
    stroke-width: 2;
  }

  .mark {
    cursor: default;
    transition: opacity 120ms;
  }

  .mark circle {
    fill: var(--bg);
    stroke-width: 1.5;
  }

  .mark.lit circle {
    stroke-width: 3;
  }

  .mark text {
    font-size: 9px;
    font-weight: 700;
    text-anchor: middle;
  }

  .name {
    font-size: 9px;
    letter-spacing: 0.12em;
  }

  .dim {
    opacity: 0.3;
  }

  path.lit {
    stroke-width: 3;
  }

  figcaption {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem 1.1rem;
    min-height: 1.1rem;
    font-size: 0.7rem;
    letter-spacing: 0.08em;
  }

  figcaption span {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
  }

  .swatch {
    display: inline-block;
    width: 1.4rem;
    border-top: 2px solid;
  }

  .swatch.after {
    border-top-style: dashed;
  }

  :global([data-timeline] > li[data-preview-hot]) {
    background: rgb(255 216 74 / 0.12);
  }

  @media (prefers-reduced-motion: reduce) {
    path,
    .mark {
      transition: none;
    }
  }
</style>
