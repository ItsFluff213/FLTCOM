import { spawn } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { parseArgs } from "node:util";

const USAGE = `usage: node tools/verify/display-shot.mjs --url <url> --selector <css> --out <file.png>
  [--width 1280] [--height 800] [--mobile] [--timeout 20000]
  [--eval <js expression>] [--sample-ms <total ms> --every <ms> [--snap-at <ms>]] [--edge <path to msedge>]`;

const { values: args } = parseArgs({
  options: {
    url: { type: "string" },
    selector: { type: "string" },
    out: { type: "string" },
    width: { type: "string", default: "1280" },
    height: { type: "string", default: "800" },
    mobile: { type: "boolean", default: false },
    timeout: { type: "string", default: "20000" },
    eval: { type: "string" },
    "sample-ms": { type: "string", default: "0" },
    every: { type: "string", default: "500" },
    "snap-at": { type: "string" },
    edge: { type: "string" },
  },
});

if (!args.url || !args.selector || !args.out) {
  console.error(USAGE);
  process.exit(2);
}

function findEdge() {
  const candidates = [
    args.edge,
    process.env.FLTCOM_EDGE,
    process.env["ProgramFiles(x86)"] && join(process.env["ProgramFiles(x86)"], "Microsoft/Edge/Application/msedge.exe"),
    process.env.ProgramFiles && join(process.env.ProgramFiles, "Microsoft/Edge/Application/msedge.exe"),
    process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, "Microsoft/Edge/Application/msedge.exe"),
    "/usr/bin/microsoft-edge",
    "/opt/microsoft/msedge/msedge",
  ];
  const found = candidates.find((path) => path && existsSync(path));
  if (!found) {
    console.error("Microsoft Edge not found. Pass --edge <path> or set FLTCOM_EDGE.");
    process.exit(2);
  }
  return found;
}

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

async function waitForFile(path, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (existsSync(path)) {
      const text = readFileSync(path, "utf8");
      if (text.includes("\n")) {
        return text;
      }
    }
    await sleep(50);
  }
  throw new Error(`Edge did not open its debugging port within ${timeoutMs} ms`);
}

function connect(url) {
  const socket = new WebSocket(url);
  const pending = new Map();
  const listeners = [];
  let nextId = 1;
  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.id !== undefined) {
      const call = pending.get(message.id);
      pending.delete(message.id);
      if (message.error) {
        call.reject(new Error(`${call.method}: ${message.error.message}`));
      } else {
        call.resolve(message.result);
      }
    } else {
      for (const listener of listeners) {
        listener(message);
      }
    }
  });
  return new Promise((ready, fail) => {
    socket.addEventListener("error", () => fail(new Error(`cannot connect to ${url}`)));
    socket.addEventListener("open", () =>
      ready({
        send(method, params = {}, sessionId) {
          const id = nextId++;
          socket.send(JSON.stringify({ id, method, params, sessionId }));
          return new Promise((resolveCall, reject) => pending.set(id, { resolve: resolveCall, reject, method }));
        },
        on(listener) {
          listeners.push(listener);
        },
        close() {
          socket.close();
        },
      }),
    );
  });
}

const timeoutMs = Number(args.timeout);
const profile = mkdtempSync(join(tmpdir(), "fltcom-edge-"));
const edge = spawn(
  findEdge(),
  [
    "--headless=new",
    "--use-angle=swiftshader",
    "--enable-unsafe-swiftshader",
    "--remote-debugging-port=0",
    `--user-data-dir=${profile}`,
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-extensions",
    `--window-size=${args.width},${args.height}`,
    "about:blank",
  ],
  { stdio: "ignore" },
);

let exitCode = 0;
let cdp;
try {
  const [port, path] = (await waitForFile(join(profile, "DevToolsActivePort"), 15000)).split("\n");
  cdp = await connect(`ws://127.0.0.1:${port}${path}`);
  const { targetId } = await cdp.send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: true });
  const page = (method, params) => cdp.send(method, params, sessionId);

  cdp.on((message) => {
    if (message.sessionId !== sessionId) {
      return;
    }
    if (message.method === "Runtime.exceptionThrown") {
      console.log(`page exception: ${message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text}`);
    }
    if (message.method === "Runtime.consoleAPICalled" && ["error", "warning"].includes(message.params.type)) {
      console.log(`console.${message.params.type}: ${message.params.args.map((arg) => arg.value ?? arg.description).join(" ")}`);
    }
  });
  await page("Runtime.enable");
  await page("Page.enable");
  await page("Emulation.setDeviceMetricsOverride", {
    width: Number(args.width),
    height: Number(args.height),
    deviceScaleFactor: 1,
    mobile: args.mobile,
  });
  await page("Page.navigate", { url: args.url });

  const evaluate = async (expression) => {
    const { result, exceptionDetails } = await page("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
    if (exceptionDetails) {
      throw new Error(`eval failed: ${exceptionDetails.exception?.description ?? exceptionDetails.text}`);
    }
    return result.value;
  };

  const deadline = Date.now() + timeoutMs;
  let found = false;
  while (Date.now() < deadline) {
    found = await evaluate(`document.querySelector(${JSON.stringify(args.selector)}) !== null`).catch(() => false);
    if (found) {
      break;
    }
    await sleep(100);
  }
  if (!found) {
    console.log(`selector ${args.selector} not found within ${timeoutMs} ms`);
    exitCode = 1;
  } else {
    console.log(`selector ${args.selector} found`);
  }

  const save = async (path) => {
    const { data } = await page("Page.captureScreenshot", { format: "png" });
    const out = resolve(path);
    mkdirSync(dirname(out), { recursive: true });
    writeFileSync(out, Buffer.from(data, "base64"));
    console.log(`saved ${out}`);
  };

  if (found && args.eval) {
    const sampleStart = Date.now();
    const sampleMs = Number(args["sample-ms"]);
    let snapAt = args["snap-at"] === undefined ? null : Number(args["snap-at"]);
    do {
      console.log(`eval ${JSON.stringify(await evaluate(args.eval))}`);
      if (snapAt !== null && Date.now() - sampleStart >= snapAt) {
        await save(args.out.replace(/\.png$/i, "") + `-at${snapAt}ms.png`);
        snapAt = null;
      }
      if (sampleMs > 0) {
        await sleep(Number(args.every));
      }
    } while (Date.now() < sampleStart + sampleMs);
  }

  await save(args.out);
} catch (error) {
  console.error(error.message);
  exitCode = 1;
} finally {
  cdp?.close();
  edge.kill();
  await new Promise((done) => (edge.exitCode !== null ? done() : edge.once("exit", done)));
  rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
process.exit(exitCode);
