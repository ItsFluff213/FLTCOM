---
name: verify-display
description: Drive the FLTCOM browser display on mock telemetry and capture proof. Build the display, serve a telemetry fixture from the mock server, open the page in headless Microsoft Edge, wait for an element, read values from the page, and save a PNG. Use for any FLTCOM display lane, a display screenshot, or checking what the display shows. Needs no game.
---

# Verify the FLTCOM display

The display is a web page. The mock server replays a recorded fleet at 2 Hz on `127.0.0.1:47801` and serves the page from the same port. It uses 47801 so it never takes the game's port 47800. `tools/verify/display-shot.mjs` opens a URL in headless Edge, waits for a CSS selector, and saves a screenshot. Run every command from the repository root. Nothing here touches KSP.

## 1. Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
```

Wait for `FLTCOM build ok`. The page lands in `dist/GameData/FLTCOM/web/`. Without a KSP install the full build fails on the plugin. For display-only work, build just the page:

```powershell
npm --prefix display ci --no-audit --no-fund   # only when display/node_modules is missing
npm --prefix display run build
node tools/copy-fonts.mjs dist/GameData/FLTCOM
```

The page build is done when Vite prints `built in` and copy-fonts prints `fonts copied to`. Vite's warning that `./fonts/fonts.css` does not exist at build time is expected: copy-fonts puts it there.

## 2. Start the mock server

It runs until stopped, so start it in the background. Start `node` directly rather than through `npm start`, so the process you get back is the server itself:

```powershell
$server = Start-Process node -ArgumentList 'tools/mock-telemetry/server.mjs', '--fixture', 'kerbin-small' -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\fltcom-mock.out.txt" -RedirectStandardError "$env:TEMP\fltcom-mock.err.txt"
```

In a bash tool, run `node tools/mock-telemetry/server.mjs --fixture kerbin-small` as a background command instead. `npm --prefix tools/mock-telemetry start -- --fixture kerbin-small` does the same in a terminal, but it starts a cmd, npm and node tree.

Poll the output file until it holds the line `mock telemetry listening on 127.0.0.1:47801`. The line before it, `fleet message bytes=<n> vessels=<n>`, is the size of one `fleet` message.

Fixtures live in `tests/fixtures/telemetry/`. `kerbin-small` has 7 vessels around Kerbin and its moons. `fleet-200` has 200 vessels. `comms-lost` has a probe, `FLT-1108-PRB`, with no signal.

Options: `--port <n>` serves on another port, and `--protocol-version <n>` stamps every message with another version, which the display must refuse. `--freeze-after <seconds>` stops sending `fleet` messages after that long but keeps the socket open and answers pings, like KSP frozen in a scene load. `port <n> busy` and a non-zero exit mean something already listens there. Stop it or pick another port.

## 3. Shoot the page

```powershell
node tools/verify/display-shot.mjs --url http://127.0.0.1:47801/ --selector "[data-vessel]" --out <dir>/list-kerbin.png
```

It creates the folder of `--out` if needed, prints `selector <css> found` and `saved <path>`, and exits 0. When the selector never appears it prints `selector <css> not found within <ms> ms`, still saves the screenshot, and exits 1. Page errors print as `page exception:` or `console.error:` lines.

| Option | Default | Use |
| --- | --- | --- |
| `--width`, `--height` | 1280, 800 | Viewport in CSS pixels. Use 390 and 844 for a phone. |
| `--mobile` | off | Emulate a touch phone viewport. |
| `--timeout` | 20000 | Milliseconds to wait for the selector. |
| `--eval <js>` | none | Evaluate an expression after the selector appears and print `eval <json>`. |
| `--sample-ms <ms>` with `--every <ms>` | 0, 500 | Repeat `--eval` for that long, one line per sample. |
| `--snap-at <ms>` | none | While sampling, also save `<out>-at<ms>ms.png` that far into the run, to catch a passing state. |
| `--edge <path>` | auto | Path to `msedge.exe` if Edge is not in a standard place. `FLTCOM_EDGE` works too. |

## 4. What the page exposes

| Selector or call | Meaning |
| --- | --- |
| `main[data-link]` | Link state: `connecting`, `live`, `stale`, `lost`, `versionMismatch`, or `denied`. `stale` means the socket is up but no `fleet` came for four snapshot intervals. |
| `[data-vessel]` | One list row per vessel (an `li`); click its `button` to select the vessel. The attribute holds the registry code, such as `FLT-1101-STN`. |
| `[data-label]` | A map label, `vessel:<id>` or `body:<name>`, with `data-role` (`fleet`, `selected`, `alert`, `body`). `.labels` carries `data-overlaps`, `data-shown` and `data-hidden` from the declutter pass. |
| `[data-center-pick=Kerbin]` | Header button that centers the map on a body, `Sun` for the whole system. `.map[data-center]` names the current center. |
| `[data-banner=comms-lost]`, `[data-dossier-code]` | The comms-lost banner, and the dossier of the selected vessel. |
| `[data-banner=impact]`, `[data-banner=landing]`, `[data-raise-periapsis]` | The red alarm banner of an operation on impact course or headed for a hard landing; RAISE PERIAPSIS opens a circularize briefing for that vessel. |
| `[data-blueprint]`, `[data-stage=N]` | The craft blueprint and one clickable stage; `data-selected-stage` on the blueprint holds the choice. `[data-blueprint-none]` shows when no layout arrived. |
| `[data-setting=clean]` | Clean-mode checkbox (also `intensity`, `muted`). `html[data-clean]` is `true` when effects are off; the choice persists in `localStorage` key `fltcom.settings`. |
| `[data-vessel] [data-comms]` | Comms state of that vessel: `link`, `los`, `none`, or `off`. The cell shows `LINK`, `LOS`, `—`, or `OFF`. |
| `[data-vessel] [data-anomaly]` | Mean anomaly in degrees, or `—` for a vessel on a surface. |
| `[data-compose-kind]`, `[data-compose-brief]` | Composer controls (`[data-compose-altitude]` km, `[data-compose-inclination]` deg). BRIEF sends a `brief` and opens `[data-briefing]` (`data-blocked` when approval is off) with `[data-approve]`, `[data-scrub]`, `[data-codename]`, `[data-required]`, `[data-margin]`, `[data-stage-line]`, `[data-severity]`, and `[data-final-orbit]` (`data-low` when the periapsis is under the body's safe floor). |
| `[data-operation]`, `[data-step]`, `[data-prompt]` | One operation (`data-state`), its timeline steps (`data-step` is `current`, `missed`, `conflict`, `hold`, ...), and a warp prompt with `[data-answer=yes|no]`; `[data-resume]` leaves Hold. `[data-countdown-t]` holds the seconds to the next burn. |
| `GET /mock/hold`, `/mock/miss`, `/mock/replan`, `/mock/align`, `/mock/impact` | Mock server controls: hold every live operation (a game load), miss the next burn, re-plan a missed one, move the second operation's burn onto the first's (a conflict), put the first live single-vessel operation on impact course (hold plus alarm). |
| `[data-version-mismatch]` | Present only when the server speaks another protocol version. No rows show then. |
| `performance.getEntriesByName("fltcom:apply")` | One measure per `fleet` message: parse plus render time in milliseconds. |

The header reads `LINK LOST · RETRYING` while the server is gone, and `TELEMETRY STALE` while it is connected but silent. The page pings every 2 seconds, drops a link whose pong is 3 seconds late, retries every second, and shows the list again on its own.

Examples:

```powershell
# count rows
node tools/verify/display-shot.mjs --url http://127.0.0.1:47801/ --selector "[data-vessel]" --out shot.png --eval "document.querySelectorAll('[data-vessel]').length"
# watch one value for ten seconds
node tools/verify/display-shot.mjs --url http://127.0.0.1:47801/ --selector "[data-vessel]" --out shot.png --eval "document.querySelector('[data-vessel] [data-anomaly]').textContent" --sample-ms 10000 --every 500
# horizontal scroll on a phone
node tools/verify/display-shot.mjs --url http://127.0.0.1:47801/ --selector "[data-vessel]" --out phone.png --width 390 --height 844 --mobile --eval "document.documentElement.scrollWidth > innerWidth"
```

## 5. Stop the server

Stop the process you started, by its id. If you started it through `npm`, stop the whole tree:

```powershell
Stop-Process -Id $server.Id          # started with node directly
taskkill /PID <pid> /T /F            # started through npm or cmd
```

Then check that nothing still listens. `TIME_WAIT` rows on the port are closed connections, not a server:

```powershell
netstat -ano | Select-String LISTENING | Select-String ':47801'   # prints nothing when stopped
```

## 6. Look at the PNG

Open every screenshot you save and check it shows what the lane claims. A saved file is not proof by itself.
