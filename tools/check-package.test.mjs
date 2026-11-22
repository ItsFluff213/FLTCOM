import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { after, describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { checkPackage } from "./check-package.mjs";

const scratch = mkdtempSync(join(tmpdir(), "fltcom-pkg-"));
after(() => rmSync(scratch, { recursive: true, force: true }));

let counter = 0;
function makePackage(extra = {}) {
  const root = join(scratch, `case-${counter++}`, "GameData", "FLTCOM");
  const files = {
    "FLTCOM.dll": "dll",
    "FLTCOM.version": "{}",
    "web/index.html": "<html></html>",
    "web/fonts/Plex/Plex-Regular.woff2": "font",
    "web/fonts/Plex/OFL.txt": "license",
    ...extra,
  };
  for (const [rel, content] of Object.entries(files)) {
    if (content === null) {
      continue;
    }
    const full = join(root, rel);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}

function omit(...names) {
  return Object.fromEntries(names.map((name) => [name, null]));
}

describe("checkPackage", () => {
  it("refuses a file that embeds the builder's home folder", () => {
    const homes = ["C:\\Users\\builder"];
    const leaked = Buffer.from("x C:\\Users\\builder\\src\\obj\\FLTCOM.pdb x", "utf16le");
    assert.deepEqual(checkPackage(makePackage({ "FLTCOM.dll": leaked }), { homes }), ["contains the builder's home folder path: FLTCOM.dll"]);
    assert.deepEqual(checkPackage(makePackage({ "web/app.js": "/_/src/x.ts" }), { homes }), []);
  });

  it("accepts a complete package", () => {
    assert.deepEqual(checkPackage(makePackage()), []);
  });

  it("rejects a nested GameData folder", () => {
    const root = makePackage({ "GameData/FLTCOM/FLTCOM.dll": "dll" });
    const errors = checkPackage(root);
    assert.ok(errors.some((error) => error.includes("nested GameData") && error.includes("GameData")));
  });

  it("rejects a package placed under GameData/GameData", () => {
    const root = join(scratch, "double", "GameData", "GameData", "FLTCOM");
    mkdirSync(root, { recursive: true });
    writeFileSync(join(root, "FLTCOM.dll"), "dll");
    writeFileSync(join(root, "FLTCOM.version"), "{}");
    const errors = checkPackage(root);
    assert.ok(errors.some((error) => error.startsWith("nested GameData") && error.includes(root)));
  });

  it("rejects a missing FLTCOM.dll", () => {
    const errors = checkPackage(makePackage(omit("FLTCOM.dll")));
    assert.deepEqual(errors, ["missing FLTCOM.dll"]);
  });

  it("rejects a missing FLTCOM.version", () => {
    const errors = checkPackage(makePackage(omit("FLTCOM.version")));
    assert.deepEqual(errors, ["missing FLTCOM.version"]);
  });

  it("rejects 0Harmony.dll", () => {
    const errors = checkPackage(makePackage({ "0Harmony.dll": "dll" }));
    assert.equal(errors.length, 1);
    assert.match(errors[0], /0Harmony\.dll/);
  });

  it("rejects the test harness dll", () => {
    const errors = checkPackage(makePackage({ "FLTCOM.TestHarness.dll": "dll" }));
    assert.equal(errors.length, 1);
    assert.match(errors[0], /FLTCOM.TestHarness/);
  });

  it("rejects a test harness folder inside the package", () => {
    const errors = checkPackage(makePackage({ "FLTCOM.TestHarness/PluginData/harness/boot.cfg": "fixture = x" }));
    assert.ok(errors.length >= 1);
    assert.ok(errors.every((error) => /FLTCOM.TestHarness/.test(error)));
  });

  it("rejects ModuleManager dlls", () => {
    const errors = checkPackage(makePackage({ "ModuleManager.4.2.3.dll": "dll" }));
    assert.equal(errors.length, 1);
    assert.match(errors[0], /ModuleManager\.4\.2\.3\.dll/);
  });

  it("rejects a font without OFL.txt", () => {
    const errors = checkPackage(makePackage({ "web/fonts/Plex/OFL.txt": null }));
    assert.equal(errors.length, 1);
    assert.match(errors[0], /Plex-Regular\.woff2/);
  });

  it("rejects a version file claiming KSP below 1.12", () => {
    for (const key of ["KSP_VERSION", "KSP_VERSION_MIN", "KSP_VERSION_MAX"]) {
      const errors = checkPackage(makePackage({ "FLTCOM.version": JSON.stringify({ [key]: "1.8" }) }));
      assert.equal(errors.length, 1, key);
      assert.match(errors[0], new RegExp(key));
    }
  });

  it("accepts a version file claiming KSP 1.12.5", () => {
    const body = JSON.stringify({ KSP_VERSION: "1.12.5", KSP_VERSION_MIN: "1.12.5", KSP_VERSION_MAX: "1.12.5" });
    assert.deepEqual(checkPackage(makePackage({ "FLTCOM.version": body })), []);
  });

  it("rejects an unreadable version file", () => {
    assert.equal(checkPackage(makePackage({ "FLTCOM.version": "not json" })).length, 1);
  });

  it("reports a missing package folder", () => {
    assert.equal(checkPackage(join(scratch, "nope")).length, 1);
  });
});

describe("check-package command", () => {
  const script = join(dirname(fileURLToPath(import.meta.url)), "check-package.mjs");

  it("exits 0 for a good package", () => {
    const result = spawnSync(process.execPath, [script, makePackage()], { encoding: "utf8" });
    assert.equal(result.status, 0);
  });

  it("exits non-zero and names the problem for a bad package", () => {
    const result = spawnSync(process.execPath, [script, makePackage({ "0Harmony.dll": "dll" })], { encoding: "utf8" });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /0Harmony\.dll/);
  });
});
