import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { MAX_DEPTH, PROTOCOL_VERSION, parseServerMessage } from "./protocol.gen.ts";

const corpus: { name: string; text: string; expect: string }[] = JSON.parse(
  readFileSync(new URL("../../tests/fixtures/protocol/parse-corpus.json", import.meta.url), "utf8"),
);
const fleetText = readFileSync(new URL("../../tests/fixtures/telemetry/kerbin-small.json", import.meta.url), "utf8");

describe("parseServerMessage", () => {
  it.each(corpus.map((entry) => [entry.name, entry] as const))("parses the shared corpus like the plugin: %s", (_, entry) => {
    const parsed = parseServerMessage(entry.text);
    expect(parsed.ok ? "accept" : parsed.error.kind).toBe(entry.expect);
  });

  it("rejects deep nesting before parsing it", () => {
    const deep = "[".repeat(10000) + "]".repeat(10000);
    const parsed = parseServerMessage(`{"protocolVersion": ${PROTOCOL_VERSION},"type":"pong","nonce":${deep}}`);
    expect(parsed).toEqual({ ok: false, error: { kind: "malformed", path: "$", expected: `JSON nested at most ${MAX_DEPTH} deep` } });
  });

  it("ignores brackets inside strings when it measures depth", () => {
    const name = "[".repeat(MAX_DEPTH + 1) + '\\"{';
    const parsed = parseServerMessage(`{"protocolVersion": ${PROTOCOL_VERSION},"type":"hello","server":"${name}","saveName":null,"snapshotInterval":0.5}`);
    expect(parsed.ok).toBe(true);
  });

  it("rejects a fleet with two vessels of one id", () => {
    const fleet = JSON.parse(fleetText);
    fleet.vessels[3].id = fleet.vessels[1].id;
    const parsed = parseServerMessage(JSON.stringify(fleet));
    expect(parsed).toEqual({ ok: false, error: { kind: "malformed", path: "$.vessels[3].id", expected: "unique value" } });
  });
});
