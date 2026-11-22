<script lang="ts">
  import { untrack } from "svelte";
  import type { Fleet } from "../protocol.gen.ts";
  import type { Anchor } from "./camera.ts";
  import Labels from "./Labels.svelte";
  import { MapRenderer } from "./MapRenderer.ts";
  import { buildScene, type Entity, type PlannedBurn, type PlannedLanding } from "./Scene.ts";

  interface Props {
    fleet: Fleet;
    center: string;
    selectedId: number | null;
    burns?: readonly PlannedBurn[];
    landings?: readonly PlannedLanding[];
    members?: ReadonlySet<number>;
    operating?: ReadonlySet<number>;
    models?: ReadonlyMap<number, string | null>;
    clean: boolean;
    onselect: (vesselId: number) => void;
  }

  let { fleet, center, selectedId, burns = [], landings = [], members = new Set(), operating = new Set(), models = new Map(), clean, onselect }: Props = $props();

  let box: HTMLDivElement;
  let canvas: HTMLCanvasElement;
  let width = $state(800);
  let height = $state(600);
  let anchors = $state.raw<ReadonlyMap<string, Anchor>>(new Map());
  let hovered = $state<string | null>(null);
  let map: MapRenderer | null = null;

  const scene = $derived(buildScene({ fleet, center, selectedId, burns, landings, members, operating, models }));

  /** Labels follow the 3D map; a frame where nothing moved half a pixel leaves them alone. */
  function moved(next: readonly Anchor[]): boolean {
    if (next.length !== anchors.size) {
      return true;
    }
    return next.some((anchor) => {
      const known = anchors.get(anchor.id);
      return !known || known.visible !== anchor.visible || known.behind !== anchor.behind || Math.abs(known.x - anchor.x) > 0.5 || Math.abs(known.y - anchor.y) > 0.5;
    });
  }

  function pick(entity: Entity): void {
    if (entity.kind === "vessel" && entity.vesselId !== null) {
      onselect(entity.vesselId);
    }
  }

  $effect(() => {
    const renderer = new MapRenderer(canvas, {
      onanchors: (next) => {
        if (moved(next)) {
          anchors = new Map(next.map((anchor) => [anchor.id, anchor]));
        }
      },
      onpick: pick,
      onhover: (id) => {
        hovered = id;
      },
    });
    map = renderer;
    untrack(() => {
      renderer.setSize(width, height);
      renderer.setScene(scene);
    });
    return () => {
      map = null;
      renderer.dispose();
    };
  });

  $effect(() => {
    const observer = new ResizeObserver(([entry]) => {
      width = Math.max(120, Math.floor(entry!.contentRect.width));
      height = Math.max(120, Math.floor(entry!.contentRect.height));
    });
    observer.observe(box);
    return () => observer.disconnect();
  });

  $effect(() => {
    map?.setSize(width, height);
  });

  $effect(() => {
    const start = performance.now();
    map?.setScene(scene);
    performance.measure("fltcom:draw", { start });
  });

  $effect(() => {
    map?.setDrift(!clean);
  });
</script>

<div class="map" bind:this={box} data-center={scene.center.name}>
  <canvas bind:this={canvas} style:width="{width}px" style:height="{height}px"></canvas>
  <Labels
    entities={scene.entities}
    {anchors}
    {width}
    {height}
    {hovered}
    onchip={(chip) => map?.flyTo(`body:${chip.body}`)}
    onselect={(entity) => {
      map?.flyTo(entity.id);
      pick(entity);
    }}
  />
</div>

<style>
  .map {
    position: relative;
    min-height: 0;
    overflow: hidden;
    background: #0c0a0a;
  }

  canvas {
    display: block;
    touch-action: none;
  }
</style>
