import { describe, expect, it } from "vitest";
import type { Body } from "../protocol.gen.ts";
import { centerChoices } from "./Scene.ts";

const body = (name: string, parent: string | null, semiMajorAxis = 0) =>
  ({ name, orbit: parent === null ? null : { referenceBody: parent, semiMajorAxis } }) as unknown as Body;

const system = [
  body("Sun", null),
  body("Duna", "Sun", 20_726_155_264),
  body("Kerbin", "Sun", 13_599_840_256),
  body("Moho", "Sun", 5_263_138_304),
  body("Mun", "Kerbin", 12_000_000),
  body("Minmus", "Kerbin", 47_000_000),
  body("Ike", "Duna", 3_200_000),
];

describe("centerChoices", () => {
  it("offers every planet by distance from the star, even with no ship there", () => {
    expect(centerChoices(system, [], "Sun")).toEqual(["Sun", "Moho", "Kerbin", "Duna"]);
  });

  it("adds the moons of the planet in view, also when a moon is the centre", () => {
    expect(centerChoices(system, [], "Kerbin")).toEqual(["Sun", "Moho", "Kerbin", "Duna", "Mun", "Minmus"]);
    expect(centerChoices(system, [], "Ike")).toEqual(["Sun", "Moho", "Kerbin", "Duna", "Ike"]);
  });

  it("keeps any body a ship is at", () => {
    expect(centerChoices(system, ["Mun"], "Duna")).toEqual(["Sun", "Moho", "Kerbin", "Duna", "Ike", "Mun"]);
  });
});
