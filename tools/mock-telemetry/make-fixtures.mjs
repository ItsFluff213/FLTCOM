import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderFixtures } from "./fixtures.mjs";

const dir = resolve(dirname(fileURLToPath(import.meta.url)), "../../tests/fixtures/telemetry");
mkdirSync(dir, { recursive: true });
for (const [name, text] of Object.entries(renderFixtures())) {
  writeFileSync(join(dir, `${name}.json`), text);
  console.log(`wrote tests/fixtures/telemetry/${name}.json (${Buffer.byteLength(text)} bytes)`);
}
