# P17 operator steps: land at a chosen spot

Two new saves are used. They are built once from recipes in `tests/fixtures/saves/recipes.cfg`:

- `land-kerbin`: FLT Lander K (the stock Orbiter 1A: capsule, parachute, engine, 1 pilot) in a circular 80 km Kerbin orbit, due east.
- `land-mun`: FLT Lander M (the stock Two-Stage Lander, 1 pilot) in a circular 20 km Mun orbit.

## Build the two saves (once, about 5 minutes)
1. Close KSP and CKAN. In a PowerShell window in the repo run these one at a time, waiting for each to finish:
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1` (ends with `FLTCOM build ok`)
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu` (KSP opens by itself and stops at the main menu; do not click anything)
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture land-kerbin`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture land-mun`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off`
   Pass: the folders `tests/fixtures/saves/land-kerbin` and `tests/fixtures/saves/land-mun` now exist, and the last command reports test mode off.

## Set up
1. Close KSP. Run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-land-kerbin`, and wait until you fly FLT Lander K.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test A: back to the launch pad (Kerbin, parachute)
1. Select FLT Lander K. In COMPOSE ORBIT CHANGE pick OPERATION: LAND.
   Pass: a LAND AT list appears with LAUNCH PAD already chosen, and RUNWAY and CUSTOM LAT/LON below it.
2. Click BRIEF.
   Pass: the briefing shows two burns, `deorbit` (tens of m/s) and `descent`, and no red line. On the map a yellow `touchdown T+...` mark sits on the Space Center.
3. Approve. When FLTCOM asks to warp, answer yes. When it asks who flies, choose LET FLTCOM FLY IT.
   Pass: the ship turns and burns retrograde, then coasts down. Do not touch the controls.
4. Watch the descent in the game (it takes about 10 to 20 minutes; you may use physics warp by hand).
   Pass: below about 25 km and 250 m/s the parachute opens by itself; the engine may fire short bursts to correct the course, and near the ground fires to slow down.
5. Wait for touchdown.
   Pass: the ship comes to rest upright with the throttle at zero, SAS on, and the operation reads done with a note like `landed 800 m from the target at 4.0 m/s`. Write the distance down. Under 2 km is a pass; the launch pad itself is the goal.
6. Fail notes to write down: where it landed (in the game, hover over the ship in map view), whether the parachute opened, and the last FLTCOM line in the operation's note.

## Test B: the runway and custom coordinates
1. Quit to the main menu, Resume Game, pick `fltcom-land-kerbin` again.
2. Compose LAND, choose RUNWAY, and BRIEF.
   Pass: the touchdown mark sits slightly west of the pad, on the runway.
3. Choose CUSTOM LAT/LON. Leave both boxes empty.
   Pass: BRIEF is greyed out. Type latitude 0 and longitude -80, then BRIEF.
   Pass: the mark moves west of the Space Center, on the coast. Scrub it (no need to fly).

## Test C: a powered landing on the Mun
1. Quit to the main menu, Resume Game, pick `fltcom-land-mun`, and fly FLT Lander M.
2. Compose LAND, choose LAUNCH PAD, BRIEF.
   Pass: a red line says the launch pad and the runway are on the home planet; pick coordinates on Mun. Approve is not offered.
3. Choose CUSTOM LAT/LON, type latitude 3 and longitude 40, BRIEF, approve, let FLTCOM fly it.
   Pass: the deorbit burn, then a long braking burn that slows the lander as it nears the ground, then a slow final drop.
4. Pass: the lander touches down upright, under 2 m/s, and the note gives a distance from the target under 1 km.

## Test D: hands off
1. In `fltcom-land-kerbin`, start a landing as in Test A. During the coast tap W once.
   Pass: FLTCOM lets go at once and the operation reads hold with a note saying control was released (pilot input). Alt+H does the same.

## Test E: pick the spot on the surface map
1. In `fltcom-land-kerbin`, fly FLT Lander K. Plant a flag somewhere near the Space Center (go EVA, right-click the kerbal, Plant Flag), then board again. Open the map view, click a spot on Kerbin's surface, and add a custom waypoint there with the stock waypoint tool (the pin button at the bottom right of the map view). Name it `My Spot`.
2. In the browser, select FLT Lander K and compose LAND.
   Pass: the big map turns into a flat map of Kerbin titled `SURFACE · KERBIN`, with a grid and labels such as `30N` and `90W`. A white square marks the launch pad and the runway, a white flag marks your flag, a filled blue diamond marks `My Spot`, and any contract sites are hollow blue diamonds. Ships standing on the ground are green dots. A legend under the map names each kind.
3. Rest the mouse on each marker.
   Pass: a small box shows its name, its kind and its coordinates, such as `MY SPOT · WAYPOINT · 0.12 S 76.30 W`. The flag's coordinates match the ones the game shows when you hover over the flag in map view.
4. Click the launch pad square.
   Pass: a yellow crosshair sits on the pad, the map header reads `TARGET LAUNCH PAD`, and LAND AT on the right shows LAUNCH PAD.
5. Click `My Spot`, then click an empty patch of land.
   Pass: each click moves the crosshair there. LAND AT switches to CUSTOM LAT/LON and the two boxes fill with the clicked coordinates. Under the map the coordinates follow the mouse.
6. Type latitude 0 and longitude -80 in the boxes instead.
   Pass: the crosshair jumps to that spot. Choose RUNWAY in the list. Pass: the crosshair moves onto the runway.
7. Pick `My Spot` again and click BRIEF.
   Pass: the briefing's first line reads `LAND AT` followed by the waypoint's coordinates. Scrub it.
8. Pick another operation, such as CIRCULARIZE.
   Pass: the surface map goes away and the 3D map is back.

## Test F: the landing waypoint in the game
1. In `fltcom-land-kerbin`, compose LAND on FLT Lander K with CUSTOM LAT/LON 0 and -80. BRIEF, but do not approve yet.
   Pass: the game's map view shows no new waypoint.
2. Approve.
   Pass: within a second, a waypoint named `FLTCOM <codename> landing` (the codename is the one in the briefing) appears in the game's map view at 0 N 80 W. In flight, the navball shows its marker. Your own `My Spot` waypoint is still there, unchanged.
3. Press F5 to quicksave, then F9 to load it.
   Pass: there is still exactly one `FLTCOM ... landing` waypoint, not two.
4. Scrub the operation in the browser.
   Pass: within a second the `FLTCOM ... landing` waypoint is gone from the map view. `My Spot` stays.
5. Repeat Test A (land at the launch pad) to the end.
   Pass: a `FLTCOM ... landing` waypoint sits on the pad during the landing and goes away when the operation reads done.
6. Delete `My Spot` yourself in the map view, then compose and approve another landing.
   Pass: FLTCOM adds only its own waypoint and never brings `My Spot` back.

## Test G: no spin on the way down (Mun)
1. In `fltcom-land-mun`, fly FLT Lander M. Compose LAND, CUSTOM LAT/LON, type latitude 3 and longitude 40, BRIEF, approve, and let FLTCOM fly it. Do not touch the keys.
   Pass: after the deorbit burn the lander turns once to point back along its path and stays there. During the long braking burn the nose stays within a few degrees of that direction, with no quick flips. Near the ground it turns upright in one smooth move. At no point does it roll or tumble.
2. Repeat step 1 with your own lander in a higher Mun orbit (about 130 km). Pick the spot by clicking the surface map, not 0 N 0 E.
   Pass: the same smooth descent and a soft touchdown. `KSP.log` has no `control released` line.
3. Spin guard. Quicksave (F5) during any FLTCOM burn. Set SAS off and hold Q (or E) until the craft rolls faster than about one turn every six seconds.
   Pass: holding a key already releases control as pilot input, so this step mainly shows the throttle drops to zero and the `>> AUTONOMOUS CONTROL RELEASED <<` banner appears. If a craft ever spins by itself during guidance, the operation turns red ABORTED with `the vessel is spinning at N deg/s; FLTCOM cut the throttle and let go`, the throttle is zero, and you can fly it yourself. Load the quicksave (F9) afterwards.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
