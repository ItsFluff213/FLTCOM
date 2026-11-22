import * as THREE from "three";
import { artUrl } from "../lib/connection.ts";
import { rockShape } from "./rock.ts";

export interface CraftModelDocument {
  positions: number[];
  indices: number[];
}

export function isCraftModel(value: unknown): value is CraftModelDocument {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { positions, indices } = value as Record<string, unknown>;
  return (
    Array.isArray(positions) &&
    Array.isArray(indices) &&
    positions.length % 3 === 0 &&
    indices.length % 3 === 0 &&
    positions.every((n) => typeof n === "number" && Number.isFinite(n)) &&
    indices.every((n) => Number.isInteger(n) && n >= 0 && n < positions.length / 3)
  );
}

/** A craft geometry centred on its bounds and scaled to a unit bounding radius, so the map sizes every ship alike. */
export function unitGeometry(model: CraftModelDocument): THREE.BufferGeometry {
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.Float32BufferAttribute(model.positions, 3));
  geometry.setIndex(model.indices);
  geometry.computeBoundingSphere();
  const sphere = geometry.boundingSphere!;
  geometry.translate(-sphere.center.x, -sphere.center.y, -sphere.center.z);
  const k = sphere.radius > 0 ? 1 / sphere.radius : 1;
  geometry.scale(k, k, k);
  geometry.computeVertexNormals();
  geometry.computeBoundingSphere();
  return geometry;
}

/** The lumpy, flat-shaded rock an asteroid or comet is drawn as, the same for the same seed. Unit radius. */
export function rockGeometry(seed: string): THREE.BufferGeometry {
  const shape = rockShape(seed);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.Float32BufferAttribute(shape.positions, 3));
  geometry.setIndex(shape.indices);
  geometry.computeVertexNormals();
  geometry.computeBoundingSphere();
  return geometry;
}

/** A plain dart pointing along the craft's long axis (+z), for ships whose model has not arrived. */
export function fallbackGeometry(): THREE.BufferGeometry {
  const geometry = new THREE.ConeGeometry(0.38, 2, 6);
  geometry.rotateX(Math.PI / 2);
  geometry.scale(0.5, 0.5, 0.5);
  return geometry;
}

const cache = new Map<string, Promise<THREE.BufferGeometry | null>>();

const RETRY_MS = 1000;
const ATTEMPTS = 30;

/** The plugin names a design's model before it has built it, so a 404 means "not yet": ask again until it arrives. */
async function fetchCraft(path: string, wait: (ms: number) => Promise<void>): Promise<THREE.BufferGeometry | null> {
  for (let attempt = 0; attempt < ATTEMPTS; attempt++) {
    try {
      const response = await fetch(artUrl(path, location));
      if (response.ok) {
        const json: unknown = await response.json();
        return isCraftModel(json) && json.indices.length > 0 ? unitGeometry(json) : null;
      }
      if (response.status !== 404) {
        return null;
      }
    } catch {
      return null;
    }
    await wait(RETRY_MS);
  }
  return null;
}

/** Fetches a craft model once per design; every vessel built alike shares the geometry. */
export function loadCraft(
  path: string,
  wait: (ms: number) => Promise<void> = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
): Promise<THREE.BufferGeometry | null> {
  let pending = cache.get(path);
  if (!pending) {
    pending = fetchCraft(path, wait);
    cache.set(path, pending);
  }
  return pending;
}
