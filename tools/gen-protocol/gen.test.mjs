import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { after, describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { findStale, generate, SCHEMA } from "./gen.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const schemaText = readFileSync(join(root, SCHEMA), "utf8");
const scratch = mkdtempSync(join(tmpdir(), "fltcom-gen-"));
after(() => rmSync(scratch, { recursive: true, force: true }));

function withDefs(defs, maxDepth = 64) {
  return JSON.stringify({ "x-protocolVersion": 1, "x-maxDepth": maxDepth, $defs: defs });
}

const message = (tag, extra = {}) => ({
  type: "object",
  properties: { protocolVersion: { type: "integer", format: "int32" }, type: { const: tag }, ...extra },
  required: ["protocolVersion", "type", ...Object.keys(extra)],
  additionalProperties: false,
});

const union = (...names) => ({ oneOf: names.map((name) => ({ $ref: `#/$defs/${name}` })), discriminator: { propertyName: "type" }, "x-messages": true });

describe("gen-protocol", () => {
  it("leaves the committed files current", () => {
    const result = spawnSync(process.execPath, [join(root, "tools/gen-protocol/gen.mjs"), "--check"], { encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
  });

  it("generates the same text twice", () => {
    assert.deepEqual(generate(schemaText), generate(schemaText));
  });

  it("names each stale file", () => {
    const files = generate(schemaText);
    for (const { path, text } of files) {
      mkdirSync(dirname(join(scratch, path)), { recursive: true });
      writeFileSync(join(scratch, path), text.replace(/\n/g, "\r\n"));
    }
    assert.deepEqual(findStale(scratch, files), []);
    writeFileSync(join(scratch, files[1].path), "// edited\n");
    assert.deepEqual(findStale(scratch, files), ["src/FLTCOM.Core/Protocol/Protocol.g.cs"]);
  });

  it("accepts a minimal protocol", () => {
    const files = generate(withDefs({ Up: union("Hi"), Hi: message("hi", { at: { type: "number", "x-unit": "s" } }) }));
    assert.match(files[0].text, /export function parseUp\(text: string\): Parsed<Up>/);
    assert.match(files[1].text, /public static Up ReadUp\(string json\)/);
  });

  const rejections = [
    ["an optional property", { Up: union("Hi"), Hi: { ...message("hi", { at: { type: "string" } }), required: ["protocolVersion", "type"] } }, /every property must be required/],
    ["a number without a unit", { Up: union("Hi"), Hi: message("hi", { at: { type: "number" } }) }, /needs "x-unit"/],
    ["an unknown keyword", { Up: union("Hi"), Hi: message("hi", { at: { type: "string", pattern: "x" } }) }, /unsupported keyword "pattern"/],
    ["a message without protocolVersion", { Up: union("Hi"), Hi: { type: "object", properties: { type: { const: "hi" } }, required: ["type"], additionalProperties: false } }, /needs "protocolVersion"/],
    ["two messages with one tag", { Up: union("Hi", "Ho"), Hi: message("hi"), Ho: message("hi") }, /duplicate tag "hi"/],
    ["anyOf that is not nullability", { Up: union("Hi"), Hi: message("hi", { at: { anyOf: [{ type: "string" }, { type: "boolean" }] } }) }, /anyOf is only for nullability/],
    ["an open object", { Up: union("Hi"), Hi: { ...message("hi"), additionalProperties: true } }, /"additionalProperties": false is required/],
    ["an integer without a format", { Up: union("Hi"), Hi: message("hi", { n: { type: "integer" } }) }, /integer needs "format"/],
    ["a dangling reference", { Up: union("Hi"), Hi: message("hi", { n: { $ref: "#/$defs/Nope" } }) }, /unknown \$ref Nope/],
    [
      "a unique key the items lack",
      { Up: union("Hi"), Hi: message("hi", { items: { type: "array", items: { $ref: "#/$defs/Item" }, "x-uniqueBy": "id" } }), Item: { type: "object", properties: { name: { type: "string" } }, required: ["name"], additionalProperties: false } },
      /"x-uniqueBy" names a string or integer property/,
    ],
    ["a unique key on scalar items", { Up: union("Hi"), Hi: message("hi", { tags: { type: "array", items: { type: "string" }, "x-uniqueBy": "id" } }) }, /"x-uniqueBy" names/],
  ];
  for (const [what, defs, error] of rejections) {
    it(`rejects ${what}`, () => {
      assert.throws(() => generate(withDefs(defs)), error);
    });
  }

  it("rejects a protocol without a nesting limit", () => {
    assert.throws(() => generate(withDefs({ Up: union("Hi"), Hi: message("hi") }, null)), /x-maxDepth: must be a positive integer/);
  });
});
