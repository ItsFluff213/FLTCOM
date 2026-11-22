import { cpSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const target = process.argv[2];
if (!target) {
  console.error("usage: node tools/copy-fonts.mjs <path to GameData/FLTCOM>");
  process.exit(2);
}

const fonts = join(resolve(target), "web", "fonts");
mkdirSync(fonts, { recursive: true });
cpSync(join(root, "assets", "fonts"), fonts, { recursive: true, filter: (source) => !source.endsWith("SOURCES.md") });
console.log(`fonts copied to ${fonts}`);
