---
name: ksp-driver
description: Drive Kerbal Space Program 1.12.5 for FLTCOM tests. Turn test mode on, boot a fixture save with no clicks, read state, switch vessels, warp, screenshot, quit, and restore the player's mods. Use for any FLTCOM game lane or whenever a task needs the real game.
---

# Drive KSP for FLTCOM tests

Everything runs from the repository root in Windows PowerShell 5.1. Write each command as:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/<script>.ps1 <arguments>
```

The scripts find KSP through `-KspRoot <folder>`, then the `KSP_ROOT` environment variable, then the default Steam folder.

## Rules that protect the player's game

The KSP install is the player's own game, with their mods and saves. Break none of these rules.

1. Test mode moves every mod out of `GameData` into `<KSP>\FLTCOM-parked\GameData\`, and every save folder into `<KSP>\FLTCOM-parked\saves\`. It leaves loose files such as `steam_autocloud.vdf`, the stock `scenarios` and `training` folders, and the `saves/fltcom-*` fixtures in place. `off` puts them all back. Never delete, edit, or move anything in the install yourself. Only `ksp-testmode.ps1` moves mods and saves.
2. Never load, open, or change a save outside `saves/fltcom-*`. KSP deletes vessels whose parts belong to parked mods, so loading one of the player's saves in test mode would destroy their vessels. That is why test mode parks the player's saves too: while it is on, `saves/` holds no save of theirs. The harness only loads `saves/fltcom-<fixture>`, and the driver only copies fixtures from `tests/fixtures/saves/`.
3. Start KSP only through `ksp-driver.ps1 boot`, which runs `KSP_x64.exe` directly. Never start KSP through Steam while test mode is on. Steam Cloud syncs this game's saves and could push the parked saves' absence to the cloud.
4. A fixture folder carries a `.fltcom-fixture` marker, which `boot` and `make-fixture` write. `off` deletes only marked `saves/fltcom-*` folders, and `on` refuses to start while an unmarked one exists.
5. Never stop a KSP or CKAN process you did not start. `ksp-driver.ps1 kill` only kills the KSP that `boot` started.
6. Never write to the `CKAN` folder in the install.
7. Every session that turned test mode on ends with `ksp-testmode.ps1 off`, and `ksp-testmode.ps1 status` prints `off, 0 items parked`. That includes a session that failed. If `off` reports a problem, stop and report its output word for word. Do not fix it by hand.
8. Only one KSP runs at a time. Take the lease before you start and release it at the end.

## A session

```powershell
# 1. Take the lease. Exit 1 and "busy holder=<owner>" means wait a minute and retry.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-lock.ps1 acquire <your-name>

# 2. Run off first, in case a dead worker left test mode on.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off

# 3. Build, turn test mode on, and copy FLTCOM and the harness into GameData.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy

# 4. Boot a fixture. It returns "ksp ready scene=FLIGHT boot=<seconds>s" after about 80 s.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot lko-single

# 5. Act. See the command table below.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 state
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 screenshot lko -Out <folder>\lko.png

# 6. Quit KSP, turn test mode off, check it, and release the lease.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 status
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-lock.ps1 release <your-name>
```

`off` prints `test mode off, N items restored, M saves restored` and exits 0. It also restores `settings.cfg`, which KSP rewrites on exit, and removes `GameData/FLTCOM`, `GameData/FLTCOM.TestHarness`, and every marked `saves/fltcom-*` folder. It returns the player's saves only after every mod is back. If a save with a parked save's name appeared in `saves/` during test mode, `off` names it, changes nothing, and exits 1. If KSP will not quit, run `ksp-driver.ps1 kill`, then `off`.

A lease lasts 20 minutes. Run `ksp-lock.ps1 renew <your-name>` at least every 15 minutes during a long session.

## Driver commands

Every command except `boot` and `make-fixture` returns within 30 seconds. Add `-Json` to print the harness reply as JSON.

| Command | What it does |
| --- | --- |
| `deploy` | Copies `dist/GameData/FLTCOM` and `dist-test/GameData/FLTCOM.TestHarness` into `GameData`. Needs test mode on and KSP stopped. |
| `boot <fixture>` | Copies the fixture into `saves/fltcom-<fixture>/`, starts KSP, loads the save from the main menu, and returns at `ksp ready scene=FLIGHT`. |
| `boot -Menu` | Starts KSP and returns at `ksp ready scene=MAINMENU`. |
| `load <fixture>` | Swaps to another fixture without restarting KSP. Takes about 5 s. |
| `state` | Prints the scene, the save, the universal time, the active vessel, and one line per vessel: name, type, situation, body, orbit, signal, and distance from the active vessel. `*` marks the active vessel. A sandbox save also lists stock asteroids and comets. |
| `switch <vessel>` | Makes a vessel active, by name, id, or part of a name. A distant vessel reloads the flight scene. |
| `warp <ut>` | Warps to a universal time. `+<seconds>` and `+<n>orbit` are relative. It returns once the warp ends or after 25 s. Then run `state` to read the time. |
| `screenshot <slug> [-Out <file.png>]` | Saves a screenshot of the game window. The slug uses letters, digits, `-`, and `_`. |
| `log-tail [n]` | Prints the last n lines of `KSP.log`, 40 by default. |
| `quit` | Quits KSP and waits for the process to exit. |
| `kill` | Kills the KSP that `boot` started, and no other process. Use it only when `quit` fails. |
| `wait <id>` | Waits 30 s more for an answer that timed out. The timeout message names the id. |
| `make-fixture <name>` | Builds a fixture from `tests/fixtures/saves/recipes.cfg` in the running game and writes it to `tests/fixtures/saves/<name>/`. Start from `boot -Menu`. |

## Fixtures

`tests/fixtures/saves/README.md` lists every fixture and how it was made: `lko-single`, `lko-elliptic`, `fleet-mixed`, `comms-lost`, `mun-station`, `pad-kerbalx`, and `pad-twostage`.

## Diagnostics

- `KSP.log` in the KSP folder, or `ksp-driver.ps1 log-tail 80`. Harness lines start with `[FLTCOM.TestHarness]`.
- `GameData/FLTCOM/PluginData/fltcom.log`, once FLTCOM writes one.
- `GameData/FLTCOM.TestHarness/PluginData/harness/out.jsonl` holds every harness reply.
- A reply with `harness error:` names the exception the command threw. The game keeps running and takes the next command.
- `boot` fails at once if test mode is off, the harness is not deployed, or KSP or CKAN already runs.

## Variants

To test with one extra mod, add it after `on` and before `boot`. A parked mod moves back from `FLTCOM-parked`. Another mod is copied from a folder you name, and `off` removes the copy.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 variant add MechJeb2
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 variant add FerramAerospaceResearch -Source <folder>
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 variant remove MechJeb2
```
