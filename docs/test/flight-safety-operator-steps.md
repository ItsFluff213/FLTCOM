# Flight safety operator steps

What changed: FLTCOM runs one operation per vessel. Before each burn it checks the vessel against the path it really flies. A burn that no longer fits is planned again from that path. A coast that would hit the ground holds the operation and shows a red banner. The banner offers RAISE PERIAPSIS, which you still approve yourself. A briefing shows the final orbit, and one that ends under the body's safe floor cannot be approved. Landings check before the deorbit and all the way down. Guidance lets you time-warp while the engines are idle.

Saves used: `fltcom-lko-single` (FLT Orbiter, a 100 km Kerbin orbit) and `fltcom-land-mun` (FLT Lander M, the stock Two-Stage Lander, in a 20 km Mun orbit). The play-test script installs both. There is nothing to build.

## Set up
1. Close KSP. In PowerShell, in the FLTCOM folder, run `tools/play-test.ps1` and wait until it says it is ready.
2. Start `KSP_x64.exe` directly, not through Steam. Click Resume Game.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.
4. When you are done with every test, quit KSP and run `tools/play-test.ps1 -Off`.

## Test 1: one operation per vessel
1. Load `fltcom-land-mun`. Select FLT Lander M. Compose CHANGE APOAPSIS to 40 km, then BRIEF and APPROVE. Answer no to the warp prompt.
2. Compose CIRCULARIZE for the same ship, then BRIEF and APPROVE.
   Pass: the second approval is refused with a note like `operation ... is still waiting on this vessel: let it finish or scrub it first`. Only one operation is live.
3. Scrub the first operation, then approve the second briefing again.
   Pass: it is accepted now.

## Test 2: a burn that no longer fits is planned again
1. Load `fltcom-lko-single`. Select FLT Orbiter. Compose CIRCULAR ORBIT AT ALTITUDE 250 km, then BRIEF and APPROVE. Let FLTCOM fly it.
   The plan has two burns: raising the apoapsis, then circularizing at about 250 km.
2. After the first burn ends, before the second, press F5 to quicksave. Then burn prograde by hand for 2 to 3 seconds, so the apoapsis rises by tens of km.
   Pass: within a second or two the operation's note reads `... re-planned from the path flown`. The second burn moves to the new apoapsis, and its delta-v changes.
3. Press F9 to reload the quicksave. Burn by hand again, this time much harder, until the apoapsis is above 1,000 km.
   Pass: the operation holds with a note starting `STALE BURN:` and a reason, such as the new plan needing more delta-v than the old one. Nothing flies until you resume or scrub.

## Test 3: impact alarm and RAISE PERIAPSIS
1. Load `fltcom-land-mun`. Select FLT Lander M. Compose CHANGE APOAPSIS to 60 km, then BRIEF and APPROVE. Answer no to the warp prompt.
2. In the game, burn retrograde by hand until the map shows the Mun periapsis under the surface. Stop as soon as it does.
   Pass: within about a second time warp drops to 1x and the operation goes to HOLD. A red banner reads `>> IMPACT COURSE <<` with `FLT LANDER M · ON IMPACT COURSE WITH MUN, PERIAPSIS -... KM`, and an alert sounds.
3. Click RAISE PERIAPSIS on the banner.
   Pass: a CIRCULARIZE briefing opens for FLT Lander M. Its FINAL ORBIT shows a periapsis above the floor. Nothing has flown yet.
4. APPROVE it and let FLTCOM fly it.
   Pass: the held operation is aborted with the note `superseded by ...`, and the banner goes away. The recovery burns at apoapsis and the periapsis ends above the surface.
   If the vessel would hit before apoapsis, the briefing is red with `on impact course with Mun before "circularize"`. Then fly the recovery yourself: burn radial out or prograde.

## Test 4: the briefing shows the final orbit
1. In `fltcom-land-mun`, compose CHANGE PERIAPSIS to 3 km for FLT Lander M and click BRIEF.
   Pass: FINAL ORBIT shows the PE in red with `UNDER ... km FLOOR` (the top of the highest Mun terrain, about 7 km). The contingencies show `the final orbit's periapsis, 3.0 km, is under Mun's safe floor of ... km`. APPROVE is disabled.
2. Change it to 15 km and BRIEF again.
   Pass: the PE is not red and APPROVE is enabled.

## Test 5: time warp while guidance coasts
1. Load `fltcom-land-mun`. Compose LAND at custom coordinates, for example 2 and 30, then BRIEF and APPROVE. Let FLTCOM fly it.
2. After the deorbit burn ends, press `.` to raise time warp. Rails warp works out of an atmosphere.
   Pass: warp stays on during the coast. About a minute before the braking burn, FLTCOM steps it down to 1x on its own, and the burn starts at 1x.
3. While the engine is braking, try to warp.
   Pass: warp falls back to 1x right away.
4. Prompted warp before a burn still works as before. Answer yes and the warp stops shortly before the burn.

## Test 6: landing safety
1. Load `fltcom-land-mun` and press F5 to quicksave. Compose LAND as in Test 5, then BRIEF and APPROVE. Answer no to the warp prompt.
2. Before the deorbit burn, right-click the descent stage's fuel tanks and switch off the flow of liquid fuel and oxidizer, which cuts the delta-v. Do this more than 2 minutes before the deorbit, then wait.
   Pass: shortly before the deorbit the operation holds without burning. A red banner reads `>> HARD LANDING <<` with the reason, such as a stage that cannot cover its burn. The ship stays in orbit. This is the abort to orbit.
3. Turn the fuel flow back on, then press RESUME.
   Pass: the banner clears, and the landing goes on.
4. Press F9 to reload. Approve the landing again and let FLTCOM fly it. During the coast after the deorbit, switch the fuel flow off.
   Pass: within a second the banner reads `>> HARD LANDING <<` with `braking needs ... m/s, ... m/s left` or `touchdown predicted at ... m/s, over the safe 10 m/s`. Guidance keeps flying, because dropping it mid-descent would only make things worse.
5. Switch the fuel flow back on.
   Pass: the banner clears within a second or two and the landing finishes upright.
6. Watch the whole landing.
   Pass: there is no `IMPACT COURSE` banner at any point, because the planned landing is not an impact course.

Fail notes to write down: the exact banner and note text, the periapsis the game shows, and the lines of `GameData/FLTCOM/PluginData/fltcom.log` mentioning `ALARM` or `stale burn`.

## Burn control
What changed: a running burn lets go only on Abort (Backspace), Alt+H, a spin over 60 deg/s, or steering held for about a second. A tap of a key, trim, the translation keys with RCS off, a SAS button or a dashboard click no longer stops it. FLTCOM starts turning a slow ship earlier. A burn that is still not lit well after its start time (5 s, or a tenth of the burn) is planned again from where the ship is, with a note. A fairing is jettisoned and logged once per flight.

Saves used: `fltcom-lko-single` (FLT Orbiter in a 100 km Kerbin orbit) and Test D's fairing rocket from the auto systems steps. Set up as above.

## Test 7: stray keys never stop a burn
1. Load `fltcom-lko-single`. Compose CHANGE APOAPSIS to 400 km for FLT Orbiter, then BRIEF and APPROVE. Answer no to the warp prompt.
2. Once the engine is lit, tap each of these briefly, one at a time: W, A, Q, H, N, T (SAS off), F, R (RCS on and off), the SAS RADIAL button, Alt+W (pitch trim), then Alt+X (clear trim).
   Pass: the throttle stays up the whole time and the burn goes on. After T or a SAS button, SAS comes back on in maneuver mode within a frame or two.
3. Hold S down for two seconds.
   Pass: FLTCOM lets go after about a second with `pilot input held for 1 s` in the note and in `fltcom.log` (`control released: ...`).
4. Approve the burn again and press Backspace (Abort) while it burns.
   Pass: FLTCOM lets go at once with `abort`.

## Test 8: dashboard clicks never stop a burn
1. With a burn running as in Test 7, brief a second operation for another vessel and APPROVE it, click SCRUB on that second one twice, click RESUME, and reload the browser tab.
   Pass: the running burn never stops. The second SCRUB adds no `cannot scrub` note.
2. Click SCRUB on the running operation.
   Pass: only this stops the burn.

## Test 9: a slow turn re-plans the burn
1. Load `fltcom-lko-single`. Point the ship well away from prograde, for example radial in, then turn SAS and RCS off. Right-click the probe core or pod and switch its reaction wheel off (Wheel state: Disabled).
2. Compose CHANGE APOAPSIS to 400 km, BRIEF and APPROVE, and answer yes to the warp prompt.
   Pass: the warp stops about 5 minutes before the burn instead of 1 minute, because a ship with no torque gets the longest turn lead.
3. Wait past the burn time without turning the ship.
   Pass: about 5 s after the planned start an on-screen message and a timeline note read `pointing took N s; re-planned to <time>`. The operation waits for that new time and the engine never lights pointing the wrong way. `fltcom.log` has `late burn ...`.
4. Switch the reaction wheel back on.
   Pass: the re-planned burn turns, lights and reaches the 400 km apoapsis.

## Test 10: a fairing is reported once
1. Fly Test D of the auto systems steps (the fairing rocket) to orbit.
   Pass: `fltcom.log` has exactly one `jettisoned 1 fairing` line for the whole flight, ascent and finishing burn together.

Fail notes for Tests 7 to 10: the key or click that stopped the burn, the note text, and the `control released`, `late burn` and `auto systems` lines of `fltcom.log`.

## Test 11: a Mun landing keeps flying from the deorbit burn to the ground
1. Load `fltcom-land-mun` and fly FLT Lander M. In the display compose LAND on the Mun at a custom spot about a quarter orbit ahead (click it on the surface map), then BRIEF and APPROVE. Answer yes to the warp prompt.
   Pass: the operation card shows a green `DEORBIT BURN` block with the target spot while the deorbit burn runs.
2. Watch the ship when the deorbit burn ends and the descent starts.
   Pass: the nose swings over to the descent attitude smoothly over a few seconds, at about 10 degrees per second. It does not snap round and spin. `fltcom.log` has no `control released` line.
3. Watch the card through the descent.
   Pass: the block moves through `COURSE CORRECTION` or `COAST`, then `BRAKING BURN`, `FINAL DESCENT` and `TOUCHDOWN`. ALT and V/S count down, `TOUCHDOWN T-` counts down, and `PREDICTED` sits beside `TARGET` with a `MISS` that shrinks. The ship lands upright within about 100 m of the spot you clicked.

## Test 12: a released descent is a red alarm, not a quiet note
1. Load `fltcom-land-mun` again and approve the same landing. When the descent has started (the card shows `COAST` or `BRAKING BURN`), press Backspace (Abort).
   Pass: a large red message on the KSP screen reads `FLT LANDER M RELEASED ON IMPACT COURSE WITH MUN` and `TAKE THE CONTROLS`. Time warp stops. The display shows the red IMPACT COURSE banner, and the card shows a red `RELEASED` block with `TAKE THE CONTROLS`. The operation is on HOLD, not ABORTED.
2. Click RESUME.
   Pass: FLTCOM takes the descent back and lands, or reports why it cannot.
3. Approve a landing again. During the descent tap W once, briefly.
   Pass: nothing lets go. A single tap or a one-frame wobble never ends a descent; only a spin over 60 degrees per second that lasts half a second does.

## Test 13: a crewed ship out of contact is not abandoned
1. In `fltcom-land-mun`, with CommNet on, warp FLT Lander M (crew 1) round to the far side of the Mun until it loses the link.
   Pass: the COMMS column of the fleet list reads `LOS · CREW FLYING` in the normal colour, not a red `LOS`. An uncrewed probe out of contact still reads a red `LOS`.

Fail notes for Tests 11 to 13: the card text when it went wrong, the screen message, and the `control released`, `ALARM` and `landing` lines of `fltcom.log`.
