# P11 operator steps: transfer to the Mun and Minmus

Saves used:
- `fltcom-kerbin-transfer`: FLT Transfer, a full Kerbal X in a 100 km circle around Kerbin, with plenty of delta-v for a moon. The recipe is `kerbin-transfer` in `tests/fixtures/saves/recipes.cfg`. **This save is new.** If it is not in the Resume Game list yet, ask for it to be built (`tools/ksp-driver.ps1 make-fixture kerbin-transfer`) before you start, or use `fltcom-lko-single` (FLT Orbiter) and expect a red "short of delta-v" line if Orbiter 1A cannot afford the trip.
- `fltcom-lko-single`: FLT Orbiter in a 100 km orbit (Test 7).
- `fltcom-pad-kerbalx`: FLT Kerbal X on the launch pad (Test 9).

How a transfer flies: one LEAVE FOR <MOON> burn in Kerbin orbit, a long coast, sometimes a small COURSE CORRECTION burn halfway, then CAPTURE AT <MOON> at the closest point to the moon, and sometimes one or two small trim burns that make the orbit round. FLTCOM asks before every time warp. Say WARP each time it asks, unless a step says otherwise.

FLTCOM writes what it does to `GameData/FLTCOM/PluginData/fltcom.log` in the KSP folder. Lines with `transfer`, `course correction`, `sphere of influence` and `burn` are the ones these steps ask about.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-kerbin-transfer`. You are flying FLT Transfer.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test 1: Mun, 50 km orbit
1. In the display click FLT Transfer. In OPERATION pick TRANSFER TO BODY. Pick MUN under BODY, type 50 in ORBIT ALTITUDE KM, leave INCLINATION DEG THERE blank.
2. Click BRIEF. The briefing lists LEAVE FOR MUN and CAPTURE AT MUN, maybe a trim burn, and no red line. Write down the REQUIRED delta-v (that is the perf number).
3. Click APPROVE. Leave the keyboard and mouse alone in KSP from here on. Say WARP whenever asked.
4. FLTCOM burns prograde at the departure point. The display's map now shows the path to the Mun.
5. When the ship enters the Mun's sphere of influence, look at the display: the operation keeps going and the map centre switches to the MUN. Take `soi-change.png`.
6. At the closest point to the Mun FLTCOM burns retrograde (CAPTURE AT MUN). Wait until the operation shows DONE. Take `mun-orbit.png` of the orbit info (map view in KSP, or the vessel's dossier in the display).

Pass: the ship orbits the Mun; apoapsis and periapsis are both within 3 km of 50 km; the display switched to the Mun on its own in step 5.

## Test 2: Minmus, 30 km orbit
1. Quit to the main menu and resume `fltcom-kerbin-transfer` again.
2. Click FLT Transfer, TRANSFER TO BODY, BODY MINMUS, ORBIT ALTITUDE KM 30, inclination blank. BRIEF. Minmus is tilted, so the briefing usually has a COURSE CORRECTION burn between leaving and capture.
3. APPROVE, say WARP each time, let it fly to DONE.
4. When the COURSE CORRECTION burn is about to start, take `correction.png` of the briefing or operation. After the correction, look at the predicted periapsis at Minmus in KSP's map view and write it down.
5. At DONE take `minmus-orbit.png`.

Pass: after the correction the predicted Minmus periapsis is within 5 km of 30 km; at the end both apsides are within 3 km of 30 km.

## Test 3: Mun, near-polar orbit
1. Resume `fltcom-kerbin-transfer` again.
2. Click FLT Transfer, TRANSFER TO BODY, BODY MUN, ORBIT ALTITUDE KM 50, INCLINATION DEG THERE 80. BRIEF. The briefing has a COURSE CORRECTION of roughly 50 m/s: that is the turn into the polar path.
3. APPROVE, say WARP each time, let it fly to DONE. Take `mun-polar.png` of the orbit info with the inclination showing.

Pass: inclination within 2 degrees of 80; both apsides within 3 km of 50 km.

## Test 4: coasting while you are in the Tracking Station
1. Resume `fltcom-kerbin-transfer`. Brief and approve the Test 1 transfer (Mun, 50 km).
2. Once the LEAVE FOR MUN burn is done, press Esc and go to the Tracking Station. Say WARP when the display asks.
3. Do nothing. Before the capture FLTCOM brings you back to FLT Transfer by itself and flies the capture.
4. Take `coast-ts.png` once the capture burn is running or done.

Pass: FLTCOM switched back to FLT Transfer from the Tracking Station on its own and the operation reaches DONE in Mun orbit.

## Test 5: no signal during the coast
FLT Transfer only has the small antenna in its capsule, so it loses its signal to Kerbin a few thousand km out, well before the Mun. That is the test.
1. Use the flight from Test 1 or Test 4. During the coast watch the vessel's signal in the display (the dossier, or the red COMMUNICATIONS LOST bar).
2. Take `transfer-no-link.png` while the signal is lost and the operation still shows as running.

Pass: the signal is lost during the coast, and the operation still flies the capture and reaches DONE. If the signal never drops, write that down instead.

## Test 6: taking control back during the capture
1. Resume `fltcom-kerbin-transfer`, brief and approve the Test 1 transfer.
2. When the CAPTURE AT MUN burn has started (engine running near the Mun), click SCRUB on the operation in the display.
3. Take `capture-abort.png`.

Pass: the engine stops, the ship answers your keys again, and the operation shows ABORTED.

## Test 7: a transfer the ship cannot afford
1. Quit to the main menu, resume `fltcom-lko-single`.
2. Click FLT Orbiter, TRANSFER TO BODY, BODY MUN, ORBIT ALTITUDE KM 50, BRIEF. Take `transfer-short.png` of the briefing.

Pass: if REQUIRED is above AVAILABLE, the briefing shows a red line ("short of delta-v by ..." or "delta-v margin ... is under 10 %") and APPROVE is disabled. If it is green because Orbiter 1A can afford it, try again with BODY MINMUS and ORBIT ALTITUDE KM 2000; that needs more and must turn red.

## Test 8: a body that cannot be reached yet
1. Still in `fltcom-lko-single`, click FLT Orbiter, TRANSFER TO BODY, BODY DUNA, BRIEF.

Pass (for this PR): a red line says that only Kerbin's own moons can be reached for now. P12 replaces this with a planet transfer.

## Test 9: from the launch pad to the Mun
1. Quit to the main menu, resume `fltcom-pad-kerbalx`.
2. Click FLT Kerbal X, APOAPSIS KM 80, BRIEF LAUNCH, APPROVE. Let it reach orbit (the operation shows DONE).
3. Click FLT Kerbal X again, TRANSFER TO BODY, BODY MUN, ORBIT ALTITUDE KM 50, BRIEF. If the briefing is red because the fuel left after launch is not enough, take `launch-to-mun.png` of it and stop here; write down REQUIRED and AVAILABLE.
4. Otherwise APPROVE and let it fly to DONE. Take `launch-to-mun.png`.

Pass: the ship ends in Mun orbit with both apsides within 3 km of 50 km, or step 3 shows a red briefing whose numbers you wrote down.

## Perf: delta-v of the Mun transfer
Fly Test 1 three times (resume the save each time). Each time write down REQUIRED from the briefing and the delta-v actually used (stock delta-v readout in the staging panel before and after). For reference, the ideal from 100 km is about 860 m/s to leave plus about 280 m/s to capture into 50 km.

Pass: the middle of the three totals is within 5 % of the briefing's REQUIRED.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
