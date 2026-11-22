<script lang="ts">
  import * as THREE from "three";
  import { fallbackGeometry, loadCraft, rockGeometry } from "../map/craftMesh.ts";
  import type { Quat } from "../protocol.gen.ts";
  import { telescopeEye } from "./telescope.ts";

  interface Props {
    model: string | null;
    attitude: Quat | null;
    clean: boolean;
    /** Seed of the rock to show for an asteroid or comet, which has no craft model. */
    rock?: string | null;
  }

  let { model, attitude, clean, rock = null }: Props = $props();

  const SIZE = 168;

  let canvas: HTMLCanvasElement;
  let grain: HTMLCanvasElement;
  let mesh: THREE.Mesh | null = null;
  let loaded = $state(false);

  $effect(() => {
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(1);
    renderer.setSize(SIZE, SIZE, false);
    renderer.setClearColor(0x0c0a0a);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(22, 1, 0.1, 50);
    camera.up.set(0, 0, 1);
    const key = new THREE.DirectionalLight(0xffffff, 4.2);
    key.position.set(1, 0.4, 0.6);
    scene.add(key, new THREE.AmbientLight(0xffffff, 0.12));
    mesh = new THREE.Mesh(fallbackGeometry(), new THREE.MeshStandardMaterial({ color: 0xd8d5d0, roughness: 0.55, metalness: 0.15, flatShading: true }));
    scene.add(mesh);

    const noise = grain.getContext("2d");
    const image = noise?.createImageData(SIZE / 2, SIZE / 2);
    const start = performance.now();
    let frame = 0;
    let tick = 0;
    const draw = (now: number) => {
      frame = requestAnimationFrame(draw);
      const seconds = (now - start) / 1000;
      const eye = telescopeEye(mesh!.quaternion, clean ? 0 : seconds);
      camera.position.set(eye.x * 6, eye.y * 6, eye.z * 6);
      camera.lookAt(0, 0, 0);
      renderer.render(scene, camera);
      if (noise && image && !clean && tick++ % 3 === 0) {
        for (let i = 0; i < image.data.length; i += 4) {
          const v = Math.random() * 255;
          image.data[i] = image.data[i + 1] = image.data[i + 2] = v;
          image.data[i + 3] = 255;
        }
        noise.putImageData(image, 0, 0);
      }
    };
    frame = requestAnimationFrame(draw);
    return () => {
      cancelAnimationFrame(frame);
      renderer.dispose();
      mesh = null;
    };
  });

  $effect(() => {
    const target = mesh;
    const wanted = model;
    loaded = false;
    if (target && rock) {
      const geometry = rockGeometry(rock).scale(0.6, 0.6, 0.6);
      target.geometry = geometry;
      loaded = true;
      return () => {
        geometry.dispose();
        target.geometry = fallbackGeometry();
      };
    }
    if (!target || !wanted) {
      return;
    }
    let live = true;
    void loadCraft(wanted).then((geometry) => {
      if (live && geometry) {
        target.geometry = geometry;
        loaded = true;
      }
    });
    return () => {
      live = false;
      target.geometry = fallbackGeometry();
    };
  });

  $effect(() => {
    if (mesh && attitude) {
      mesh.quaternion.set(attitude.x, attitude.y, attitude.z, attitude.w);
    }
  });
</script>

<figure class="telescope" class:clean data-loaded={loaded}>
  <canvas bind:this={canvas} width={SIZE} height={SIZE} style:width="{SIZE}px" style:height="{SIZE}px"></canvas>
  <canvas class="grain" bind:this={grain} width={SIZE / 2} height={SIZE / 2} aria-hidden="true"></canvas>
  <figcaption>{loaded ? "LONG RANGE OPTICS" : "NO MODEL"}</figcaption>
</figure>

<style>
  .telescope {
    position: relative;
    width: 168px;
    margin: 0;
    overflow: hidden;
    border: 1px solid var(--rule);
    background: #0c0a0a;
  }

  canvas {
    display: block;
    filter: grayscale(1) contrast(1.35) brightness(1.25) blur(0.7px);
  }

  .grain {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    opacity: 0.22;
    mix-blend-mode: screen;
    image-rendering: pixelated;
    filter: none;
  }

  .telescope::after {
    position: absolute;
    inset: 0;
    background: radial-gradient(circle at center, transparent 45%, rgb(0 0 0 / 0.7) 100%);
    content: "";
    pointer-events: none;
  }

  .clean canvas {
    filter: grayscale(1);
  }

  .clean .grain {
    display: none;
  }

  figcaption {
    position: absolute;
    bottom: 3px;
    left: 5px;
    z-index: 1;
    color: var(--dim);
    font-size: 9px;
    letter-spacing: 0.08em;
  }
</style>
