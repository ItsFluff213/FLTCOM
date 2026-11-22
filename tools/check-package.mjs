import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { basename, join, relative, resolve, sep } from "node:path";
import { homedir } from "node:os";
import { fileURLToPath } from "node:url";

const FONT_EXTENSIONS = new Set([".ttf", ".otf", ".woff", ".woff2"]);

function walk(dir) {
  const files = [];
  const folders = [];
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      folders.push(full);
      const inner = walk(full);
      files.push(...inner.files);
      folders.push(...inner.folders);
    } else {
      files.push(full);
    }
  }
  return { files, folders };
}

function extensionOf(name) {
  const dot = name.lastIndexOf(".");
  return dot < 0 ? "" : name.slice(dot).toLowerCase();
}

const MIN_KSP = [1, 12];
const KSP_VERSION_KEYS = ["KSP_VERSION", "KSP_VERSION_MIN", "KSP_VERSION_MAX"];

function belowMinimum(text) {
  const parts = String(text).split(".").map(Number);
  if (parts.some((part) => !Number.isInteger(part))) {
    return false;
  }
  for (let i = 0; i < MIN_KSP.length; i++) {
    const part = parts[i] ?? 0;
    if (part !== MIN_KSP[i]) {
      return part < MIN_KSP[i];
    }
  }
  return false;
}

function checkVersionFile(file) {
  let data;
  try {
    data = JSON.parse(readFileSync(file, "utf8"));
  } catch (error) {
    return [`FLTCOM.version is not valid JSON: ${error.message}`];
  }
  const errors = [];
  for (const key of KSP_VERSION_KEYS) {
    if (key in data && (typeof data[key] !== "string" || belowMinimum(data[key]))) {
      errors.push(`FLTCOM.version ${key} ${JSON.stringify(data[key])} is below KSP 1.12`);
    }
  }
  return errors;
}

function homeNeedles(homes) {
  const spellings = new Set();
  for (const home of homes) {
    if (home && home.length >= 4) {
      spellings.add(home).add(home.replaceAll("\\", "/")).add(home.replaceAll("/", "\\"));
    }
  }
  return [...spellings].flatMap((text) => [Buffer.from(text, "utf8"), Buffer.from(text, "utf16le")]);
}

export function checkPackage(packageDir, { homes = [homedir(), process.env.USERPROFILE, process.env.HOME] } = {}) {
  const root = resolve(packageDir);
  const errors = [];
  if (!existsSync(root) || !statSync(root).isDirectory()) {
    return [`package folder not found: ${packageDir}`];
  }

  const ancestors = root.split(sep).filter((part) => part.toLowerCase() === "gamedata");
  if (ancestors.length > 1) {
    errors.push(`nested GameData: ${root}`);
  }

  const { files, folders } = walk(root);
  for (const folder of folders) {
    if (basename(folder).toLowerCase() === "gamedata") {
      errors.push(`nested GameData: ${relative(root, folder)}`);
    }
  }
  for (const path of [root, ...folders, ...files]) {
    if (basename(path).toLowerCase().includes("testharness")) {
      errors.push(`forbidden FLTCOM.TestHarness: ${relative(root, path) || basename(path)}`);
    }
  }

  const names = files.map((file) => ({ file, rel: relative(root, file), name: basename(file) }));
  if (!names.some((entry) => entry.rel === "FLTCOM.dll")) {
    errors.push("missing FLTCOM.dll");
  }
  if (!names.some((entry) => entry.rel === "FLTCOM.version")) {
    errors.push("missing FLTCOM.version");
  }
  const versionFile = names.find((entry) => entry.rel === "FLTCOM.version");
  if (versionFile) {
    errors.push(...checkVersionFile(versionFile.file));
  }
  const needles = homeNeedles(homes);
  for (const entry of names) {
    const bytes = readFileSync(entry.file);
    if (needles.some((needle) => bytes.includes(needle))) {
      errors.push(`contains the builder's home folder path: ${entry.rel}`);
    }
    const lower = entry.name.toLowerCase();
    if (lower === "0harmony.dll") {
      errors.push(`forbidden 0Harmony.dll: ${entry.rel}`);
    }
    if (lower.startsWith("modulemanager") && lower.endsWith(".dll")) {
      errors.push(`forbidden ModuleManager dll: ${entry.rel}`);
    }
    if (FONT_EXTENSIONS.has(extensionOf(entry.name))) {
      const license = join(entry.file, "..", "OFL.txt");
      if (!existsSync(license)) {
        errors.push(`font without OFL.txt: ${entry.rel}`);
      }
    }
  }
  return errors;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const target = process.argv[2];
  if (!target) {
    console.error("usage: node tools/check-package.mjs <path to GameData/FLTCOM>");
    process.exit(2);
  }
  const errors = checkPackage(target);
  if (errors.length > 0) {
    for (const error of errors) {
      console.error(`package check failed: ${error}`);
    }
    process.exit(1);
  }
  console.log(`package ok: ${target}`);
}
