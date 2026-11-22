# P13 operator steps: rendezvous

Saves used:
- `fltcom-lko-rendezvous`: new, built from the `lko-rendezvous` recipe in `tests/fixtures/saves/recipes.cfg` (see "Build the new save" below). FLT Chaser (Orbiter 1A) at 100 km, and three stations to meet: FLT Target Near (30 km ahead), FLT Target Far (half an orbit ahead) and FLT Target Tilted (120 km, 5° tilted).
- `fltcom-mun-station`: already built. FLT Mun Probe and FLT Mun Station around the Mun.
- `fltcom-fleet-demo`: already built. FLT Pad Kerbal X on the pad and FLT Station at 250 km, among others.

A rendezvous ends when the chaser holds within 50 m of the target at under 0.2 m/s. Docking is P14.

## Build the new save (once)
1. Close KSP and CKAN. In a PowerShell window in the repo run, one line at a time:
   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture lko-rendezvous
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
   ```
   Pass: `tests/fixtures/saves/lko-rendezvous/persistent.sfs` now exists. Commit it to the `p13-rendezvous` branch or tell the agent it is there.

## Set up (every session)
1. Close KSP and CKAN. Run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game and pick the save named in the test.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

Every time FLTCOM asks "warp?", answer yes unless the test says otherwise. Answering no or nothing means it waits in real time.

## Test A: target 30 km ahead (save `fltcom-lko-rendezvous`)
1. Select FLT Chaser. In COMPOSE choose RENDEZVOUS WITH, pick FLT TARGET NEAR, press BRIEF.
   Pass: the briefing lists a few small burns (a phasing orbit, then "match velocity" or "close in", last "kill relative velocity"), all green or amber, a total of a few m/s to a few tens of m/s.
2. Approve and let it fly. Watch the game.
   Pass: when the station comes into view (within about 2 km) its SAS light turns on and it never fires an engine. The chaser closes, slows, turns RCS on and stops. The operation reads DONE with a note like "holding 38 m from FLT Target Near at 0.08 m/s".
3. Check the distance in the game: right-click the station, "Set as Target", read the target distance and relative speed on the navball.
   Pass: under 50 m and under 0.2 m/s.

## Test B: target half an orbit ahead (save `fltcom-lko-rendezvous`, freshly loaded)
1. Rendezvous FLT Chaser with FLT TARGET FAR. Brief, approve, let it fly.
   Pass: the briefing shows a phasing orbit. During the flight the chaser's lowest point never drops under 70 km (watch the periapsis on the map view). It ends holding within 50 m.
2. Write down the briefing's total delta-v.
   Pass: under 80 m/s (the ideal for half an orbit spread over a few phasing orbits is about 60 m/s).

## Test C: a 5° plane difference (save `fltcom-lko-rendezvous`, freshly loaded)
1. Rendezvous FLT Chaser with FLT TARGET TILTED.
   Pass: the first burn is "match the target plane" (about 190 to 210 m/s). It ends holding within 50 m.

## Test D: the port is taken (save `fltcom-lko-rendezvous`, freshly loaded)
1. Rendezvous FLT Chaser with FLT TARGET NEAR, brief and approve. Do not wait for it.
2. Select FLT Target Far and brief a rendezvous with FLT TARGET NEAR.
   Pass: the briefing is red and says "FLT Target Near is reserved by operation" followed by the first operation's codename. APPROVE is not offered.
3. Scrub the first operation, then brief the second again.
   Pass: it is no longer red for that reason.

## Test E: abort on the final approach (save `fltcom-lko-rendezvous`, freshly loaded)
1. Run Test A again. When the station is under 1 km away, press Backspace (the Abort action group).
   Pass: the chaser stops thrusting at once (throttle zero, no RCS puffs), the message ">> AUTONOMOUS CONTROL RELEASED <<" appears, the operation reads ABORTED, and you can fly the ship by hand.
2. Load the save again and run Test A once more; this time press I, J, K or L (RCS translation) during the approach.
   Pass: same as above; the note says "pilot input".

## Test F: the map (save `fltcom-lko-rendezvous`, freshly loaded)
1. Start Test A and watch the browser map while it flies.
   Pass: both FLT Chaser and FLT Target Near are on the map, and the planned burns show on the chaser's orbit. Note what the closest-approach information shows and whether it updates after each burn.

## Test G: not enough fuel
1. Load `fltcom-lko-rendezvous`. In the game, right-click FLT Chaser's fuel tank and drag its liquid fuel and oxidizer down to almost nothing. Then rendezvous FLT Chaser with FLT TARGET TILTED.
   Pass: the briefing is red with "short of delta-v by ..." or "delta-v margin ... is under 10 %", and APPROVE is not offered.

## Test H: around the Mun (save `fltcom-mun-station`)
1. Select FLT Mun Probe. Rendezvous with FLT MUN STATION. Approve and let it fly (ion engine: the burns are long, that is expected).
   Pass: it ends holding within 50 m at under 0.2 m/s; the station holds SAS and never fires an engine.

## Test I: launch, then rendezvous (save `fltcom-fleet-demo`)
1. Select FLT Pad Kerbal X and brief a launch to the standard low orbit (blank fields except apoapsis 100 km). Approve and wait until that operation is DONE.
2. Select FLT Pad Kerbal X again, now in orbit. Rendezvous with FLT STATION. Approve and let it fly.
   Pass: it ends holding within 50 m of FLT Station at under 0.2 m/s, and the chaser never dips into the atmosphere on the way.

## Test J: a drifting approach gets corrected (save `fltcom-lko-rendezvous`, freshly loaded)
1. Brief Test B (FLT TARGET FAR) and approve. After the first burn finishes, read the operation's note in the browser.
   Pass: it says how many burns were "re-planned from both orbits". The rendezvous still ends within 50 m. If a note instead says "brief it again", copy the note into your report.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.

## What to report
For each test: pass or fail, the briefing's total delta-v, the final distance and speed from the DONE note, and anything odd. Copy the last 40 lines of `GameData/FLTCOM/PluginData/fltcom.log` if anything failed.
