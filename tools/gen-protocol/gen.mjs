import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { emitCSharp } from "./emit-csharp.mjs";
import { emitTypeScript } from "./emit-typescript.mjs";
import { readSchema } from "./schema.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
export const SCHEMA = "protocol/fltcom.schema.json";

export const OUTPUTS = [
  { path: "display/src/protocol.gen.ts", emit: emitTypeScript },
  { path: "src/FLTCOM.Core/Protocol/Protocol.g.cs", emit: emitCSharp },
];

export function generate(schemaText) {
  const protocol = readSchema(JSON.parse(schemaText));
  return OUTPUTS.map(({ path, emit }) => ({ path, text: emit(protocol) }));
}

export function findStale(base, files) {
  return files
    .filter(({ path, text }) => {
      try {
        return readFileSync(join(base, path), "utf8").replace(/\r\n/g, "\n") !== text;
      } catch {
        return true;
      }
    })
    .map(({ path }) => path);
}

function main() {
  let files;
  try {
    files = generate(readFileSync(join(root, SCHEMA), "utf8"));
  } catch (error) {
    console.error(`${SCHEMA}: ${error.message}`);
    process.exit(1);
  }

  if (!process.argv.includes("--check")) {
    for (const { path, text } of files) {
      writeFileSync(join(root, path), text);
      console.log(`wrote ${path}`);
    }
    return;
  }

  const stale = findStale(root, files);
  for (const path of stale) {
    console.error(`stale generated file: ${path} (run node tools/gen-protocol/gen.mjs)`);
  }
  if (stale.length > 0) {
    process.exit(1);
  }
  console.log("generated protocol files are current");
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
