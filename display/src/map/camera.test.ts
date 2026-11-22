import { describe, expect, it } from "vitest";
import { between, framing, cameraPosition, hiddenBehind, HOME_VIEW, MAX_DISTANCE, MAX_ELEVATION, MIN_DISTANCE, pan, pickAt, rotate, shownRadius, unitsPerPixel, zoom } from "./camera.ts";

const length = (v: { x: number; y: number; z: number }) => Math.hypot(v.x, v.y, v.z);

describe("map camera", () => {
  it("sits at its distance from the target, above the plane when raised", () => {
    const view = { ...HOME_VIEW, target: { x: 1, y: 2, z: 3 } };
    const eye = cameraPosition(view);
    expect(length({ x: eye.x - 1, y: eye.y - 2, z: eye.z - 3 })).toBeCloseTo(view.distance, 9);
    expect(eye.z).toBeGreaterThan(3);
  });

  it("keeps small things at a minimum size on screen and big things at true size", () => {
    const perPixel = unitsPerPixel(1, 600);
    expect(shownRadius(1e-9, 1, 600, 6)).toBeCloseTo(6 * perPixel, 12);
    expect(shownRadius(0.5, 1, 600, 6)).toBe(0.5);
    expect(unitsPerPixel(2, 600)).toBeCloseTo(2 * perPixel, 12);
  });

  it("tilts no further than straight down and zooms within bounds", () => {
    expect(rotate(HOME_VIEW, 0, 10_000).elevation).toBe(MAX_ELEVATION);
    expect(rotate(HOME_VIEW, 0, -10_000).elevation).toBe(-MAX_ELEVATION);
    expect(rotate(HOME_VIEW, 100, 0).azimuth).toBeLessThan(HOME_VIEW.azimuth);
    expect(zoom(HOME_VIEW, 1e6).distance).toBe(MAX_DISTANCE);
    expect(zoom(HOME_VIEW, -1e6).distance).toBe(MIN_DISTANCE);
    expect(zoom(HOME_VIEW, -100).distance).toBeLessThan(HOME_VIEW.distance);
  });

  it("pans the target across the screen, never toward the camera", () => {
    const moved = pan(HOME_VIEW, 100, 0, 600);
    const shift = { x: moved.target.x, y: moved.target.y, z: moved.target.z };
    expect(length(shift)).toBeCloseTo(100 * unitsPerPixel(HOME_VIEW.distance, 600), 9);
    const eye = cameraPosition(HOME_VIEW);
    expect(shift.x * eye.x + shift.y * eye.y + shift.z * eye.z).toBeCloseTo(0, 9);
  });

  it("flies from one view to another, ending exactly there", () => {
    const to = { target: { x: 0.3, y: 0, z: 0 }, distance: 0.01, azimuth: 2, elevation: 0.2 };
    expect(between(HOME_VIEW, to, 0)).toEqual(HOME_VIEW);
    const end = between(HOME_VIEW, to, 1);
    expect(end.distance).toBeCloseTo(0.01, 12);
    expect(end.target).toEqual(to.target);
    const middle = between(HOME_VIEW, to, 0.5).distance;
    expect(middle).toBeLessThan(HOME_VIEW.distance);
    expect(middle).toBeGreaterThan(0.01);
  });

  it("picks the nearest visible thing under the pointer, preferring ships", () => {
    const anchors = [
      { id: "body:Mun", x: 100, y: 100, depth: 1, visible: true, kind: "body" },
      { id: "vessel:1", x: 106, y: 100, depth: 1, visible: true, kind: "vessel" },
      { id: "vessel:2", x: 101, y: 100, depth: 1, visible: false, kind: "vessel" },
    ];
    expect(pickAt(anchors, 101, 100)?.id).toBe("body:Mun");
    expect(pickAt(anchors, 101, 100, 14, (a) => a.kind === "vessel")?.id).toBe("vessel:1");
    expect(pickAt(anchors, 300, 300)).toBeNull();
  });

  it("hides what sits behind a planet but not what is in front of it", () => {
    const eye = { x: 10, y: 0, z: 0 };
    const planet = { x: 0, y: 0, z: 0 };
    expect(hiddenBehind(eye, { x: -3, y: 0, z: 0 }, planet, 1)).toBe(true);
    expect(hiddenBehind(eye, { x: 3, y: 0, z: 0 }, planet, 1)).toBe(false);
    expect(hiddenBehind(eye, { x: -3, y: 2, z: 0 }, planet, 1)).toBe(false);
    expect(hiddenBehind(eye, planet, planet, 1)).toBe(false);
  });
});

describe("opening view", () => {
  it("frames the farthest thing on the map, and no more than the whole map", () => {
    const near = framing([{ x: 0.1, y: 0, z: 0 }]);
    const far = framing([{ x: 0.1, y: 0, z: 0 }, { x: 0, y: 0.6, z: 0 }]);
    expect(far.distance).toBeGreaterThan(near.distance * 5);
    expect(framing([{ x: 40, y: 0, z: 0 }]).distance).toBe(framing([{ x: 1, y: 0, z: 0 }]).distance);
  });
});

describe("opening view on a wide screen", () => {
  it("comes in closer when the map is wider than tall, but not past the angled orbit's height", () => {
    const square = framing([{ x: 0.5, y: 0, z: 0 }], 1);
    expect(framing([{ x: 0.5, y: 0, z: 0 }], 2).distance).toBeCloseTo(square.distance / 2);
    expect(framing([{ x: 0.5, y: 0, z: 0 }], 10).distance).toBeCloseTo(square.distance * Math.sin(square.elevation));
  });
});

describe("opening view with one far probe", () => {
  it("frames the bulk of the fleet, not the one ship far out", () => {
    const fleet = Array.from({ length: 9 }, () => ({ x: 0.01, y: 0, z: 0 }));
    expect(framing([...fleet, { x: 0.9, y: 0, z: 0 }]).distance).toBe(framing(fleet).distance);
  });
});
