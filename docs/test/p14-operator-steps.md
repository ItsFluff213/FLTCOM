# P14 operator steps: dock with RCS

Saves used:
- `fltcom-lko-docking`: new. It is built from the `lko-docking` recipe in `tests/fixtures/saves/recipes.cfg` (see "Build the new save" below) and holds three craft:
  - FLT Dock Chaser and FLT Dock Station, both Space Station Core, Kerbin 100 km. The station is about 2 km ahead.
  - FLT Dock Probe (Orbiter 1A), about 3 km ahead.
- `fltcom-lko-rendezvous`: from P13. FLT Chaser and FLT Target Far, half an orbit apart.
- `fltcom-fleet-demo`: already built. It includes FLT Pad Kerbal X on the pad and FLT Station at 250 km.

A dock runs in three parts:
1. The rendezvous from P13 brings the chaser within 50 m.
2. The chaser switches to "control from here" on its docking port, turns the port to face the target port, and moves on RCS only.
3. The operation reads DONE with a note like "docked Clamp-O-Tron Docking Port to Clamp-O-Tron Docking Port on FLT Dock Station".

Not sure yet: which ports and RCS the stock Space Station Core and Orbiter 1A craft carry. If a step below needs a part the craft does not have, write that down and skip the step.

## Build the new save (once)
1. Close KSP and CKAN. In a PowerShell window in the repo, run these one line at a time:
   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture lko-docking
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
   ```
   Pass: `tests/fixtures/saves/lko-docking/persistent.sfs` now exists. Tell the agent it is there.
2. If P13's `fltcom-lko-rendezvous` was never built, build it the same way with `lko-rendezvous` in place of `lko-docking`.

## Set up (every session)
1. Close KSP and CKAN. Run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Choose Resume Game and pick the save named in the test.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

Every time FLTCOM asks "warp?", answer yes unless the test says otherwise.

## Test A: a plain dock, then one vessel (save `fltcom-lko-docking`)
1. Select FLT Dock Chaser. In COMPOSE choose DOCK WITH, pick FLT DOCK STATION and leave TARGET PORT on AUTO. Press BRIEF.
   Pass:
   - The briefing lists the rendezvous burns.
   - It then lists "kill relative velocity" and, last, "dock with RCS".
   - It is green or amber, with no "no RCS" line.
2. Approve and watch the game.
   Pass:
   - The chaser closes to within 50 m and stops.
   - It switches its control point to its docking port (the navball jumps) and turns to face the station.
   - It moves sideways onto the line out of the station's port, then creeps in. The last metres are slow, well under 0.5 m/s.
   - The ports latch. The operation reads DONE with a "docked ..." note.
3. Look at the browser map and fleet list.
   Pass: FLT Dock Chaser and FLT Dock Station now show as one vessel (one entry, one icon), not two.
4. Write down the time from "holding ... m" to DONE, and the monopropellant used (the chaser's monopropellant before and after, from the right-click menu or the fleet panel).

## Test B: a port on the far side (save `fltcom-lko-docking`, freshly loaded)
1. Brief as in Test A, but open TARGET PORT and pick a port that faces away from the chaser. A port on the station's far end will do. If the list shows only one port, skip this test and say so.
2. Approve and watch.
   Pass:
   - Within 50 m, the chaser first moves out to the side, well clear of the station.
   - It then goes around and lines up on the far port without touching the station anywhere.
   - It docks to the port you picked.

## Test C: a port that does not fit (save `fltcom-lko-docking`, freshly loaded)
1. Select FLT Dock Chaser, choose DOCK WITH, pick FLT DOCK STATION, and open TARGET PORT. If a port of a different size from the chaser's is listed (for example SIZE2 against SIZE1), pick it and press BRIEF.
   Pass: the briefing is red with "size mismatch: ..." and APPROVE is not offered.
2. Choose DOCK WITH, pick FLT DOCK PROBE, and press BRIEF.
   Pass: if the probe has no docking port, the briefing is red with "FLT Dock Probe has no docking port". If it does have one, write down the port's size.

## Test D: no RCS (save `fltcom-lko-docking`, freshly loaded)
1. In the game, right-click each RCS thruster block on FLT Dock Chaser and disable it (or empty its monopropellant tank).
2. Brief a dock of FLT Dock Chaser with FLT DOCK STATION.
   Pass: the briefing shows an amber line "no RCS: the dock stops within 50 m, CONTINGENCY, NO RCS, MANUAL DOCKING". APPROVE is still offered.
3. Approve and let it fly.
   Pass:
   - The chaser stops within 50 m of the station.
   - The message ">> CONTINGENCY, NO RCS, MANUAL DOCKING <<" shows in the game.
   - The operation reads ABORTED (released) with that note, and you can fly by hand.

## Test E: picking the port yourself (save `fltcom-lko-docking`, freshly loaded)
1. Brief a dock with FLT DOCK STATION and pick a TARGET PORT other than the first one in the list. Approve.
   Pass: it docks to that port, not another. Check by right-clicking the station's ports afterwards: only the picked one says it is docked.
2. Before it docks, select FLT Dock Probe (or any other vessel) and brief a dock with FLT DOCK STATION on that same port.
   Pass: the briefing is red with "that port on FLT Dock Station is reserved by operation" followed by the first operation's codename.

## Test F: a translation key takes back control (save `fltcom-lko-docking`, freshly loaded)
1. Run Test A. When the chaser is within 20 m of the station, press I, J, K, L, H or N (RCS translation).
   Pass:
   - The chaser stops firing its RCS at once.
   - ">> AUTONOMOUS CONTROL RELEASED <<" appears.
   - The operation reads ABORTED with "pilot input", and you can fly it by hand.

## Test G: ten docks in a row (save `fltcom-lko-docking`)
1. Run Test A ten times, loading the save fresh each time. For each run, write down:
   - docked or not;
   - minutes from "holding" to DONE;
   - the monopropellant used.
   Pass: at least 9 of 10 dock, the median time is under 8 minutes, and the median monopropellant is under 30 units. Put the numbers in the table in `docs/decisions/mechjeb-adapter.md`, or send them to the agent.

## Test H: launch, rendezvous and dock (save `fltcom-fleet-demo`)
1. Select FLT Pad Kerbal X and brief a launch to the standard low orbit. Approve it and wait for DONE.
2. Select FLT Pad Kerbal X in orbit and brief a dock with FLT STATION.
   Pass: if either craft has no docking port, the briefing is red and names it. Write down which one, and stop. Otherwise it flies the rendezvous and docks, and the operation reads DONE.

## Test I: undock keeps both names (after Test A or E)
1. In the game, right-click the docked port on the chaser and choose Undock. Back away a few metres by hand.
   Pass:
   - The browser shows two vessels again: FLT Dock Chaser and FLT Dock Station.
   - Their registry codes (FLT-xxxx-...) are the same as before the dock.
   - Write down both codes from before and after.
   - The codes come from KSP's own vessel id, and undocking may give the undocked vessel a new one. If a code changed, report which vessel's code changed.

## Test J: read the decision
1. Open `docs/decisions/mechjeb-adapter.md`.
   Pass: it explains why FLTCOM docks with its own controller and when a MechJeb option would be added. Tell the agent if anything in it is unclear.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.

## What to report
For each test, report:
- pass or fail;
- the DONE or ABORTED note;
- the time and monopropellant figures;
- anything odd, especially which way the chaser moved when it pressed RCS. If it moved away from where it should go, an RCS axis sign is wrong.

If anything failed, copy the last 40 lines of `GameData/FLTCOM/PluginData/fltcom.log`.
