import * as THREE from "three";
import { Line2 } from "three/examples/jsm/lines/Line2.js";
import { LineGeometry } from "three/examples/jsm/lines/LineGeometry.js";
import { LineMaterial } from "three/examples/jsm/lines/LineMaterial.js";
import { artUrl } from "../lib/connection.ts";
import type { Vec3 } from "../lib/kepler.ts";
import {
  between,
  cameraPosition,
  DRIFT_AFTER_MS,
  DRIFT_RADIANS_PER_SECOND,
  FOV_DEGREES,
  hiddenBehind,
  framing,
  pan,
  pickAt,
  rotate,
  shownRadius,
  unitsPerPixel,
  zoom,
  type Anchor,
  type View,
} from "./camera.ts";
import { fallbackGeometry, loadCraft, rockGeometry } from "./craftMesh.ts";
import { markerPx, MIN_SHARE, orbitStyle } from "./marks.ts";
import { tumbleOf, type Tumble } from "./rock.ts";
import { ROLE_COLOR, type Entity, type MapBody, type MapScene, type Placement } from "./Scene.ts";
import { drawnPoint, layoutSystem, type SystemLayout } from "./systemLayout.ts";

const BACKGROUND = 0x0c0a0a;
const GRID = 0x8d8a86;
const RING_DISTANCES = [1e5, 3e5, 1e6, 3e6, 1e7, 3e7, 1e8, 3e8, 1e9, 3e9, 1e10, 3e10, 1e11, 3e11];
const FLIGHT_MS = 1400;
const ROCK = 0x8a7b6a;
const COMET = 0xcfe2ff;
const LAYOUT_GAP_PX = 6;
/** Share of the smaller of width and twice the height the fully spread system may reach from its center. */
const LAYOUT_BUDGET = 0.46;

interface BodyObject {
  data: MapBody;
  spin: THREE.Group;
  mesh: THREE.Mesh<THREE.SphereGeometry, THREE.MeshStandardMaterial | THREE.MeshBasicMaterial>;
}

interface EntityObject {
  data: Entity;
  mesh: THREE.Mesh;
  model: string | null;
  tumble: Tumble | null;
  tail: THREE.Mesh | null;
}

interface OrbitObject {
  line: Line2;
  placement: Placement;
}

export interface MapRendererOptions {
  onanchors: (anchors: readonly Anchor[]) => void;
  onpick: (entity: Entity) => void;
  onhover?: (id: string | null) => void;
}

const TAIL_AXIS = new THREE.Vector3(0, -1, 0);

/** An open cone with its tip at the origin, flaring out along -y: a comet's tail, about seven rock radii long. */
function cometTail(): THREE.BufferGeometry {
  const geometry = new THREE.ConeGeometry(1.1, 7, 20, 1, true);
  geometry.translate(0, -3.5, 0);
  return geometry;
}


/** Draws a MapScene with three.js and turns pointer input into camera moves. One per map canvas. */
export class MapRenderer {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly camera = new THREE.PerspectiveCamera(FOV_DEGREES, 1, 1e-7, 100);
  private readonly world = new THREE.Scene();
  private readonly sunLight = new THREE.DirectionalLight(0xffffff, 2.6);
  private readonly starLight = new THREE.PointLight(0xffffff, 2.6, 0, 0);
  private readonly grid = new THREE.Group();
  private readonly orbitGroup = new THREE.Group();
  private readonly dropLines = new THREE.LineSegments(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: GRID, transparent: true, opacity: 0.22 }));
  private readonly bodies = new Map<string, BodyObject>();
  private readonly entities = new Map<string, EntityObject>();
  private readonly orbits = new Map<string, OrbitObject>();
  private readonly textures = new Map<string, THREE.Texture>();
  private readonly fallback = fallbackGeometry();
  private readonly burnGeometry = new THREE.OctahedronGeometry(1, 0);
  private readonly tailGeometry = cometTail();
  private unit = 1;
  private centerName = "";
  private scene: MapScene | null = null;
  private truePositions = new Map<string, Vec3>();
  private layout: SystemLayout = new Map();
  private hovered: string | null = null;
  private width = 1;
  private height = 1;
  private view: View = framing([]);
  private flight: { from: View; to: View; start: number } | null = null;
  private follow: string | null = null;
  private lastInput = -Infinity;
  private drift = true;
  private frame = 0;
  private last = 0;
  private drag: { button: number; x: number; y: number; moved: boolean } | null = null;
  private anchors: (Anchor & { kind: Entity["kind"] })[] = [];
  private readonly detach: () => void;

  private readonly canvas: HTMLCanvasElement;
  private readonly options: MapRendererOptions;

  constructor(canvas: HTMLCanvasElement, options: MapRendererOptions) {
    this.canvas = canvas;
    this.options = options;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, logarithmicDepthBuffer: true });
    this.renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
    this.renderer.setClearColor(BACKGROUND);
    this.camera.up.set(0, 0, 1);
    this.world.add(new THREE.AmbientLight(0xffffff, 0.025), this.sunLight, this.sunLight.target, this.starLight, this.grid, this.orbitGroup, this.dropLines);
    this.detach = this.listen();
    const tick = (now: number) => {
      this.frame = requestAnimationFrame(tick);
      this.draw(now);
    };
    this.frame = requestAnimationFrame(tick);
  }

  dispose(): void {
    cancelAnimationFrame(this.frame);
    this.detach();
    this.renderer.dispose();
  }

  setSize(width: number, height: number): void {
    this.width = width;
    this.height = height;
    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    for (const { line } of this.orbits.values()) {
      line.material.resolution.set(width, height);
    }
    this.camera.updateProjectionMatrix();
  }

  /** Idle drift turns the map slowly once nobody has touched it for a while; clean mode turns it off. */
  setDrift(on: boolean): void {
    this.drift = on;
  }

  setScene(scene: MapScene): void {
    const recentred = scene.center.name !== this.centerName;
    const wanted = recentred ? this.follow : null;
    this.unit = 1 / scene.extent;
    this.scene = scene;
    this.truePositions = new Map(scene.bodies.map((body) => [body.name, body.position]));
    if (recentred) {
      this.centerName = scene.center.name;
      this.buildGrid(scene);
      // Open on the own ships, not the farthest moon or an asteroid belt: a fleet in low orbit should fill the screen.
      const ships = scene.entities.filter((entity) => entity.kind === "vessel" && !entity.unowned);
      this.view = framing((ships.length > 0 ? ships : scene.entities).map((entity) => this.toWorld(entity.position)), this.width / this.height);
      this.flight = null;
      this.follow = null;
    }
    this.syncLights(scene);
    this.syncBodies(scene);
    this.syncOrbits(scene);
    this.syncEntities(scene);
    if (wanted && this.entities.has(wanted)) {
      this.flyTo(wanted);
    } else if (this.follow && this.flight) {
      const entity = this.entities.get(this.follow)?.data;
      if (entity) {
        this.flight.to = { ...this.flight.to, target: this.drawnWorld(entity, this.layoutAt(this.flight.to.distance)) };
      }
    }
  }

  flyTo(id: string): void {
    const body = id.startsWith("body:") ? this.bodies.get(id.slice("body:".length)) : undefined;
    const entity = this.entities.get(id)?.data ?? (body ? { frame: body.data.name, scaledBy: null, position: body.data.position } : undefined);
    if (!entity) {
      return;
    }
    const distance = body ? Math.max(body.data.radius * this.unit * 7, 0.004) : Math.min(this.view.distance, Math.max(0.05, 3 * this.toWorld(entity.position).length()));
    this.flight = { from: this.view, to: { ...this.view, target: this.drawnWorld(entity, this.layoutAt(distance)), distance }, start: performance.now() };
    this.follow = id;
  }

  private toWorld(p: Vec3): THREE.Vector3 {
    return new THREE.Vector3(p.x * this.unit, p.y * this.unit, p.z * this.unit);
  }

  /** The body layout for a camera this far from its target: moons spread out when zoomed out, true scale when close. */
  private layoutAt(distance: number): SystemLayout {
    if (!this.scene) {
      return new Map();
    }
    const h = this.height;
    return layoutSystem(this.scene.bodies, {
      metresPerPx: unitsPerPixel(distance, h) / this.unit,
      budgetPx: LAYOUT_BUDGET * Math.min(this.width, 2 * h),
      starPx: MIN_SHARE.star * h,
      planetPx: MIN_SHARE.body * h,
      moonPx: MIN_SHARE.moon * h,
      rockPx: (MIN_SHARE.vessel * h) / 2,
      gapPx: LAYOUT_GAP_PX,
    }, this.scene.rocks);
  }

  private drawnWorld(placed: Placement & { position: Vec3; id?: string }, layout: SystemLayout = this.layout): THREE.Vector3 {
    const rock = placed.id === undefined ? undefined : layout.get(placed.id);
    return this.toWorld(rock ? rock.position : drawnPoint(layout, this.truePositions, placed.frame, placed.scaledBy, placed.position));
  }

  private buildGrid(scene: MapScene): void {
    for (const child of [...this.grid.children]) {
      this.grid.remove(child);
      (child as THREE.Line).geometry.dispose();
    }
    const material = new THREE.LineBasicMaterial({ color: GRID, transparent: true, opacity: 0.13 });
    const edge = new THREE.LineBasicMaterial({ color: GRID, transparent: true, opacity: 0.3 });
    const circle = (radius: number, m: THREE.LineBasicMaterial) => {
      const points: THREE.Vector3[] = [];
      for (let i = 0; i <= 160; i++) {
        const a = (i / 160) * Math.PI * 2;
        points.push(new THREE.Vector3(Math.cos(a) * radius, Math.sin(a) * radius, 0));
      }
      this.grid.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), m));
    };
    for (const distance of RING_DISTANCES) {
      if (distance > scene.center.radius * 1.05 && distance < scene.extent * 0.98) {
        circle(distance / scene.extent, material);
      }
    }
    circle(1, edge);
    const spokes: THREE.Vector3[] = [];
    const inner = (scene.center.radius / scene.extent) * 1.05;
    for (let i = 0; i < 12; i++) {
      const a = (i / 12) * Math.PI * 2;
      spokes.push(new THREE.Vector3(Math.cos(a) * inner, Math.sin(a) * inner, 0), new THREE.Vector3(Math.cos(a), Math.sin(a), 0));
    }
    this.grid.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(spokes), material));
  }

  private syncLights(scene: MapScene): void {
    const sun = scene.sunDirection;
    this.sunLight.visible = sun !== null;
    this.starLight.visible = sun === null;
    if (sun) {
      this.sunLight.position.set(sun.x, sun.y, sun.z);
      this.sunLight.target.position.set(0, 0, 0);
    }
  }

  private texture(path: string): THREE.Texture {
    let texture = this.textures.get(path);
    if (!texture) {
      texture = new THREE.TextureLoader().setCrossOrigin("anonymous").load(artUrl(path, location));
      texture.name = path;
      texture.colorSpace = THREE.SRGBColorSpace;
      texture.anisotropy = 4;
      this.textures.set(path, texture);
    }
    return texture;
  }

  private syncBodies(scene: MapScene): void {
    const seen = new Set<string>();
    for (const data of scene.bodies) {
      seen.add(data.name);
      let object = this.bodies.get(data.name);
      if (!object) {
        const geometry = new THREE.SphereGeometry(1, 64, 40);
        geometry.rotateX(Math.PI / 2);
        const material = data.star
          ? new THREE.MeshBasicMaterial({ color: 0xfff4e0 })
          : new THREE.MeshStandardMaterial({ color: data.texture ? 0xffffff : 0x8d8a86, map: data.texture ? this.texture(data.texture) : null, roughness: 1, metalness: 0 });
        const mesh = new THREE.Mesh(geometry, material);
        const spin = new THREE.Group();
        spin.add(mesh);
        this.world.add(spin);
        object = { data, spin, mesh };
        this.bodies.set(data.name, object);
      }
      const material = object.mesh.material;
      if (material instanceof THREE.MeshStandardMaterial && data.texture && material.map?.name !== data.texture) {
        material.map = this.texture(data.texture);
        material.color.set(0xffffff);
        material.needsUpdate = true;
      }
      object.data = data;
      object.spin.position.copy(this.toWorld(data.position));
      object.spin.rotation.set(0, 0, data.rotationAngle);
    }
    for (const [name, object] of this.bodies) {
      if (!seen.has(name)) {
        this.world.remove(object.spin);
        object.mesh.geometry.dispose();
        object.mesh.material.dispose();
        this.bodies.delete(name);
      }
    }
  }

  private syncOrbits(scene: MapScene): void {
    const seen = new Set<string>();
    for (const orbit of scene.orbits) {
      seen.add(orbit.id);
      // Points relative to the frame body: the frame's drawn position and spread move the whole line each frame.
      const at = this.truePositions.get(orbit.frame) ?? { x: 0, y: 0, z: 0 };
      const flat: number[] = [];
      for (const p of orbit.points) {
        flat.push((p.x - at.x) * this.unit, (p.y - at.y) * this.unit, (p.z - at.z) * this.unit);
      }
      let object = this.orbits.get(orbit.id);
      if (!object) {
        object = { line: new Line2(new LineGeometry(), new LineMaterial({ transparent: true, depthWrite: false })), placement: orbit };
        object.line.material.resolution.set(this.width, this.height);
        this.orbits.set(orbit.id, object);
        this.orbitGroup.add(object.line);
      }
      object.placement = { frame: orbit.frame, scaledBy: orbit.scaledBy };
      const line = object.line;
      // A fresh geometry each update: replacing attributes in place would leave the old GPU buffers behind.
      const geometry = new LineGeometry();
      geometry.setPositions(flat);
      line.geometry.dispose();
      line.geometry = geometry;
      const style = orbitStyle(orbit);
      const material = line.material;
      material.color.set(ROLE_COLOR[orbit.role]);
      material.opacity = style.opacity;
      material.linewidth = style.width;
      material.dashed = style.dashed;
      if (style.dashed) {
        line.computeLineDistances();
        const distances = geometry.attributes["instanceDistanceEnd"] as THREE.InterleavedBufferAttribute | undefined;
        const length = distances && distances.count > 0 ? distances.getX(distances.count - 1) : 1;
        material.dashSize = length / 90;
        material.gapSize = length / 150;
      }
      line.renderOrder = orbit.role === "fleet" || orbit.role === "body" ? 0 : 1;
    }
    for (const [id, { line }] of this.orbits) {
      if (!seen.has(id)) {
        this.orbitGroup.remove(line);
        line.geometry.dispose();
        line.material.dispose();
        this.orbits.delete(id);
      }
    }
  }

  private syncEntities(scene: MapScene): void {
    const seen = new Set<string>();
    for (const data of scene.entities) {
      if (data.kind === "body") {
        seen.add(data.id);
        const known = this.entities.get(data.id);
        if (known) {
          known.data = data;
        } else {
          const marker = new THREE.Mesh();
          marker.visible = false;
          this.entities.set(data.id, { data, mesh: marker, model: null, tumble: null, tail: null });
        }
        continue;
      }
      seen.add(data.id);
      let object = this.entities.get(data.id);
      if (!object) {
        const rock = data.look !== "craft";
        const material =
          data.kind === "burn"
            ? new THREE.MeshBasicMaterial({ color: data.color })
            : rock
              ? new THREE.MeshStandardMaterial({ color: data.look === "comet" ? COMET : ROCK, emissive: data.look === "comet" ? COMET : ROCK, emissiveIntensity: 0.18, roughness: 1, metalness: 0, flatShading: true })
              : new THREE.MeshStandardMaterial({ color: data.color, emissive: data.color, emissiveIntensity: 0.35, roughness: 0.6, metalness: 0.2, flatShading: true });
        const mesh = new THREE.Mesh(data.kind === "burn" ? this.burnGeometry : rock ? rockGeometry(data.id) : this.fallback, material);
        // Ships and burns draw over planets, which the map enlarges past their true size, so a ship in low orbit never hides inside one.
        mesh.renderOrder = 10;
        mesh.onBeforeRender = (renderer) => renderer.clearDepth();
        this.world.add(mesh);
        let tail: THREE.Mesh | null = null;
        if (data.look === "comet") {
          tail = new THREE.Mesh(this.tailGeometry, new THREE.MeshBasicMaterial({ color: COMET, transparent: true, opacity: 0.22, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide }));
          tail.renderOrder = 9;
          this.world.add(tail);
        }
        object = { data, mesh, model: null, tumble: rock ? tumbleOf(data.id) : null, tail };
        this.entities.set(data.id, object);
      }
      object.data = data;
      const material = object.mesh.material as THREE.MeshBasicMaterial | THREE.MeshStandardMaterial;
      if (data.look === "craft") {
        material.color.set(data.color);
        if (material instanceof THREE.MeshStandardMaterial) {
          material.emissive.set(data.color);
        }
      }
      if (data.attitude && data.look === "craft") {
        object.mesh.quaternion.set(data.attitude.x, data.attitude.y, data.attitude.z, data.attitude.w);
      }
      if (data.kind === "vessel" && data.look === "craft" && data.model !== object.model) {
        object.model = data.model;
        const wanted = data.model;
        object.mesh.geometry = this.fallback;
        if (wanted) {
          const target = object;
          void loadCraft(wanted).then((geometry) => {
            if (geometry && target.model === wanted) {
              target.mesh.geometry = geometry;
            }
          });
        }
      }
    }
    for (const [id, object] of this.entities) {
      if (!seen.has(id)) {
        this.world.remove(object.mesh);
        (object.mesh.material as THREE.Material).dispose?.();
        if (object.data.look !== "craft") {
          object.mesh.geometry.dispose();
        }
        if (object.tail) {
          this.world.remove(object.tail);
          (object.tail.material as THREE.Material).dispose();
        }
        this.entities.delete(id);
      }
    }
  }

  private syncDropLines(): void {
    const points: number[] = [];
    for (const object of this.entities.values()) {
      if (object.data.kind === "burn") {
        continue;
      }
      const p = object.mesh.position;
      points.push(p.x, p.y, p.z, p.x, p.y, 0);
    }
    const attribute = this.dropLines.geometry.getAttribute("position") as THREE.BufferAttribute | undefined;
    if (attribute && attribute.array.length === points.length) {
      attribute.set(points);
      attribute.needsUpdate = true;
      this.dropLines.geometry.computeBoundingSphere();
      return;
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(points, 3));
    this.dropLines.geometry.dispose();
    this.dropLines.geometry = geometry;
  }

  /** Moves every body, orbit and ship to where the current layout draws it. */
  private place(now: number): void {
    const layout = this.layout;
    for (const [name, object] of this.bodies) {
      const drawn = layout.get(name);
      if (drawn) {
        object.spin.position.copy(this.toWorld(drawn.position));
        object.spin.scale.setScalar(drawn.radius * this.unit);
      }
    }
    for (const { line, placement } of this.orbits.values()) {
      const origin = layout.get(placement.frame)?.position ?? { x: 0, y: 0, z: 0 };
      line.position.copy(this.toWorld(origin));
      line.scale.setScalar(placement.scaledBy ? (layout.get(placement.scaledBy)?.scale ?? 1) : 1);
    }
    const star = this.scene?.bodies.find((body) => body.star);
    const starAt = star && layout.get(star.name) ? this.toWorld(layout.get(star.name)!.position) : null;
    const away = new THREE.Vector3();
    for (const object of this.entities.values()) {
      if (object.data.kind === "body") {
        continue;
      }
      object.mesh.position.copy(this.drawnWorld(object.data));
      if (object.tumble) {
        const { axis, speed } = object.tumble;
        object.mesh.quaternion.setFromAxisAngle(new THREE.Vector3(axis.x, axis.y, axis.z), (speed * now) / 1000);
      }
      if (object.tail) {
        const sun = this.scene?.sunDirection;
        if (starAt) {
          away.subVectors(object.mesh.position, starAt).normalize();
        } else if (sun) {
          away.set(-sun.x, -sun.y, -sun.z);
        }
        object.tail.position.copy(object.mesh.position);
        object.tail.quaternion.setFromUnitVectors(TAIL_AXIS, away);
      }
    }
    this.syncDropLines();
  }

  private draw(now: number): void {
    const dt = this.last ? Math.min(0.1, (now - this.last) / 1000) : 0;
    this.last = now;
    if (this.flight) {
      const t = (now - this.flight.start) / FLIGHT_MS;
      this.view = between(this.flight.from, this.flight.to, t);
      if (t >= 1) {
        this.flight = null;
      }
    } else if (this.drift && !this.drag && now - this.lastInput > DRIFT_AFTER_MS) {
      this.view = { ...this.view, azimuth: this.view.azimuth + DRIFT_RADIANS_PER_SECOND * dt };
    }
    this.layout = this.layoutAt(this.view.distance);
    const followed = this.follow && !this.flight ? this.entities.get(this.follow)?.data : undefined;
    if (followed) {
      this.view = { ...this.view, target: this.drawnWorld(followed) };
    }
    const eye = cameraPosition(this.view);
    this.camera.position.set(eye.x, eye.y, eye.z);
    this.camera.near = Math.max(1e-9, this.view.distance * 1e-4);
    this.camera.far = this.view.distance + 20;
    this.camera.updateProjectionMatrix();
    this.camera.lookAt(this.view.target.x, this.view.target.y, this.view.target.z);
    this.place(now);

    const distanceTo = (v: THREE.Vector3) => Math.max(1e-12, v.distanceTo(this.camera.position));
    const spheres: { center: Vec3; radius: number }[] = [];
    for (const object of this.bodies.values()) {
      spheres.push({ center: object.spin.position, radius: object.data.radius * this.unit });
    }
    for (const object of this.entities.values()) {
      if (object.data.kind === "body") {
        continue;
      }
      const geometry = object.mesh.geometry;
      if (!geometry.boundingSphere) {
        geometry.computeBoundingSphere();
      }
      const rock = this.layout.get(object.data.id);
      const radius = rock ? rock.radius * this.unit : shownRadius(0, distanceTo(object.mesh.position), this.height, markerPx(object.data, this.height) / 2);
      object.mesh.scale.setScalar(radius / Math.max(1e-6, geometry.boundingSphere!.radius));
      object.tail?.scale.setScalar(object.mesh.scale.x * geometry.boundingSphere!.radius);
      if (object.data.kind === "burn") {
        object.mesh.rotation.z += dt * 0.8;
      }
    }
    this.renderer.render(this.world, this.camera);
    this.emitAnchors(eye, spheres);
  }

  private emitAnchors(eye: Vec3, spheres: readonly { center: Vec3; radius: number }[]): void {
    const v = new THREE.Vector3();
    const anchors: (Anchor & { kind: Entity["kind"] })[] = [];
    const center = { kind: "body" as const, id: `body:${this.centerName}`, frame: this.centerName, scaledBy: null, position: { x: 0, y: 0, z: 0 } };
    const marks = this.entities.has(center.id) ? [...this.entities.values()].map((object) => object.data) : [...[...this.entities.values()].map((object) => object.data), center];
    for (const data of marks) {
      const world = this.drawnWorld(data);
      v.copy(world).project(this.camera);
      const inFront = v.z > -1 && v.z < 1;
      const behind = inFront && spheres.some((s) => hiddenBehind(eye, world, s.center, s.radius));
      const visible = inFront && (data.kind !== "body" || !behind);
      anchors.push({
        id: data.id,
        kind: data.kind,
        x: (v.x * 0.5 + 0.5) * this.width,
        y: (-v.y * 0.5 + 0.5) * this.height,
        depth: world.distanceTo(this.camera.position),
        visible,
        behind,
      });
    }
    this.anchors = anchors;
    this.options.onanchors(anchors);
  }

  private listen(): () => void {
    const canvas = this.canvas;
    const touched = () => {
      this.lastInput = performance.now();
    };
    const down = (event: PointerEvent) => {
      touched();
      this.drag = { button: event.button, x: event.clientX, y: event.clientY, moved: false };
      canvas.setPointerCapture(event.pointerId);
    };
    const move = (event: PointerEvent) => {
      if (!this.drag) {
        const box = canvas.getBoundingClientRect();
        const hit = pickAt(this.anchors, event.clientX - box.left, event.clientY - box.top, 14, (anchor) => anchor.kind === "vessel");
        const hovered = hit?.id ?? null;
        if (hovered !== this.hovered) {
          this.hovered = hovered;
          this.options.onhover?.(hovered);
        }
        return;
      }
      touched();
      const dx = event.clientX - this.drag.x;
      const dy = event.clientY - this.drag.y;
      if (!this.drag.moved && Math.hypot(dx, dy) < 4) {
        return;
      }
      this.drag.moved = true;
      this.drag.x = event.clientX;
      this.drag.y = event.clientY;
      this.flight = null;
      if (this.drag.button === 2) {
        this.follow = null;
        this.view = pan(this.view, dx, dy, this.height);
      } else {
        this.view = rotate(this.view, dx, dy);
      }
    };
    const up = (event: PointerEvent) => {
      const drag = this.drag;
      this.drag = null;
      if (drag && !drag.moved && drag.button === 0) {
        const box = canvas.getBoundingClientRect();
        const hit = pickAt(this.anchors, event.clientX - box.left, event.clientY - box.top, 14, (anchor) => anchor.kind === "vessel");
        const entity = hit ? this.entities.get(hit.id)?.data : undefined;
        if (entity) {
          this.flyTo(entity.id);
          this.options.onpick(entity);
        }
      }
    };
    const wheel = (event: WheelEvent) => {
      event.preventDefault();
      touched();
      if (this.flight) {
        this.view = this.flight.to;
        this.flight = null;
      }
      this.view = zoom(this.view, event.deltaY);
    };
    const menu = (event: Event) => event.preventDefault();
    const leave = () => {
      if (this.hovered !== null) {
        this.hovered = null;
        this.options.onhover?.(null);
      }
    };
    canvas.addEventListener("pointerdown", down);
    canvas.addEventListener("pointermove", move);
    canvas.addEventListener("pointerleave", leave);
    canvas.addEventListener("pointerup", up);
    canvas.addEventListener("pointercancel", up);
    canvas.addEventListener("wheel", wheel, { passive: false });
    canvas.addEventListener("contextmenu", menu);
    return () => {
      canvas.removeEventListener("pointerdown", down);
      canvas.removeEventListener("pointermove", move);
      canvas.removeEventListener("pointerleave", leave);
      canvas.removeEventListener("pointerup", up);
      canvas.removeEventListener("pointercancel", up);
      canvas.removeEventListener("wheel", wheel);
      canvas.removeEventListener("contextmenu", menu);
    };
  }
}
