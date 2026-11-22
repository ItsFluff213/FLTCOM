import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { parseServerMessage } from "../../display/src/protocol.gen.ts";
import { renderFixtures } from "./fixtures.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
const rendered = renderFixtures();
const load = (name) => {
  const parsed = parseServerMessage(readFileSync(join(dir, `${name}.json`), "utf8"));
  assert.ok(parsed.ok, JSON.stringify(parsed.error));
  return parsed.message;
};

describe("telemetry fixtures", () => {
  it("match a fresh run of make-fixtures.mjs", () => {
    for (const [name, text] of Object.entries(rendered)) {
      assert.equal(readFileSync(join(dir, `${name}.json`), "utf8").replace(/\r\n/g, "\n"), text, `${name}.json is stale; run npm --prefix tools/mock-telemetry run fixtures`);
    }
  });

  it("hold the vessels the live lanes count", () => {
    assert.equal(load("kerbin-small").vessels.length, 7);
    assert.equal(load("fleet-200").vessels.length, 200);
    const lost = load("comms-lost").vessels.filter((vessel) => vessel.comms === "los");
    assert.deepEqual(lost.map((vessel) => vessel.registryCode), ["FLT-1108-PRB"]);
  });

  it("give every vessel a unique id and registry code", () => {
    const vessels = load("fleet-200").vessels;
    assert.equal(new Set(vessels.map((vessel) => vessel.id)).size, 200);
    assert.equal(new Set(vessels.map((vessel) => vessel.registryCode)).size, 200);
  });
});
