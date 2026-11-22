# P12 operator steps: transfer to another planet

Saves used:
- `fltcom-kerbin-transfer`: FLT Transfer, a full Kerbal X in a 100 km circle around Kerbin. The recipe is `kerbin-transfer` in `tests/fixtures/saves/recipes.cfg` (added in P11). If it is not in the Resume Game list yet, ask for it to be built (`tools/ksp-driver.ps1 make-fixture kerbin-transfer`) before you start.
- `fltcom-lko-single`: FLT Orbiter in a 100 km orbit (Tests 5 and 7).

How a planet transfer flies: FLTCOM first searches the next months for the cheapest departure day (the "porkchop" plot in the briefing). The first burn, LEAVE FOR <PLANET>, usually comes many days later, so FLTCOM asks to warp to it. After that burn the ship leaves Kerbin's sphere of influence and coasts around the Sun for months. Halfway there may be a small COURSE CORRECTION. Then comes CAPTURE AT <PLANET>, and maybe one or two small trim burns. FLTCOM asks before every time warp. Say WARP each time it asks, unless a step says otherwise.

A Kerbin day is 6 hours. The waits are long (Duna: about 230 days to the window, then about 270 days of flight), so expect several warp prompts. Warp is the slow part. Each prompt says how far it warps.

FLTCOM writes what it does to `GameData/FLTCOM/PluginData/fltcom.log` in the KSP folder. Lines with `transfer`, `course correction`, `sphere of influence`, `warp` and `burn` are the ones these steps ask about.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-kerbin-transfer`. You are flying FLT Transfer.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test 1: the porkchop plot
1. In the display click FLT Transfer. In OPERATION pick TRANSFER TO BODY. Pick DUNA under BODY, type 100 in ORBIT ALTITUDE KM, and leave INCLINATION DEG THERE blank.
2. Click BRIEF. The briefing lists LEAVE FOR DUNA, maybe COURSE CORRECTION, then CAPTURE AT DUNA. Under the burn timeline there is a new box, TRANSFER WINDOWS. It is a colour plot with a green circle and a dashed cross marking the best window, plus a line "BEST: DEPART T+…D · …D FLIGHT · … m/s".
3. Take `porkchop.png` of the briefing. Write down the BEST line, REQUIRED, and the time shown for LEAVE FOR DUNA. The contingencies (amber) also have a line "the Duna window: leave at Y… D…".

Pass: the plot shows a bright patch (the cheap windows) with the circle inside it. The BEST departure day and the LEAVE FOR DUNA time agree within one day. REQUIRED is roughly 1,650 to 1,850 m/s. Click SCRUB to close the briefing.

## Test 2: Kerbin to Duna, 100 km orbit
1. Brief the same transfer as in Test 1 and click APPROVE. Leave the keyboard and mouse alone in KSP from here on. Say WARP whenever asked.
2. FLTCOM warps to the window and burns LEAVE FOR DUNA. Right after the burn, open the KSP map view: the predicted path should leave Kerbin and show an encounter with Duna (or come close to Duna, if a COURSE CORRECTION is planned).
3. Centre the display's map on the SUN. Take `helio-leg.png`. A dashed yellow arc should run from Kerbin's orbit out to Duna's.
4. When the ship leaves Kerbin's sphere of influence, the display's map centre switches to the SUN by itself. Let the coast run and say WARP.
5. If there is a COURSE CORRECTION, write down its delta-v from the briefing and take `planet-correction.png` while it runs.
6. When the ship enters Duna's sphere of influence, the map centre switches to DUNA. At the closest point FLTCOM burns retrograde (CAPTURE AT DUNA). Wait until the operation shows DONE. Take `duna-orbit.png` of the orbit info.

Pass: the ship orbits Duna. Both apoapsis and periapsis are within 5 km of 100 km. The arc in step 3 joins Kerbin's orbit to Duna's orbit.

## Test 3: waiting from the Space Center
1. Quit to the main menu and resume `fltcom-kerbin-transfer` again. Brief the Test 1 transfer (Duna, 100 km) and click APPROVE.
2. As soon as it is approved, press Esc and go to the Space Center (choose to leave the flight).
3. The display asks "warp to Y… D… here, then switch to vessel … for "leave for Duna" (… to ignition)". Take `wait-ksc-ask.png`. Say WARP.
4. KSP warps at the Space Center. Do nothing. About 10 minutes of game time before the burn, FLTCOM switches to FLT Transfer by itself.
5. In flight it asks once more to warp to the burn. Say WARP. Let it fly LEAVE FOR DUNA. Take `wait-ksc-burn.png` once the burn is running or done.
6. You may SCRUB the operation after the burn. Test 2 already covers the rest of the trip.

Pass: there was a warp prompt at the Space Center before any switch. The switch to FLT Transfer happened on its own shortly before the burn. A second warp prompt came in flight, and the burn flew.

Also try the "no" path: repeat steps 1 and 2, but answer NO in step 3. FLTCOM should switch to FLT Transfer right away and ask to warp in flight instead. It should not ask the Space Center question again.

## Test 4: Kerbin to Eve, 150 km orbit
1. Quit to the main menu and resume `fltcom-kerbin-transfer`.
2. Click FLT Transfer, TRANSFER TO BODY, BODY EVE, ORBIT ALTITUDE KM 150, then BRIEF. Eve's capture is expensive. Write down REQUIRED and AVAILABLE.
3. If the briefing is red because the ship cannot afford it, take `eve-short.png` and go on to Test 5. Otherwise APPROVE, say WARP each time, and let it fly to DONE. Take `eve-orbit.png`.

Pass: the ship ends in an Eve orbit with both apsides within 5 km of 150 km, or step 3 shows a red briefing whose numbers you wrote down.

## Test 5: a planet transfer the ship cannot afford
1. Quit to the main menu and resume `fltcom-lko-single`.
2. Click FLT Orbiter, TRANSFER TO BODY, BODY DUNA, ORBIT ALTITUDE KM 100, then BRIEF. Take `planet-short.png`.

Pass: this is P11's Test 8 again. It no longer says that only moons can be reached; it now briefs a Duna transfer (LEAVE FOR DUNA … CAPTURE AT DUNA, with the TRANSFER WINDOWS plot). If REQUIRED is above AVAILABLE, there is a red line ("short of delta-v by …" or "delta-v margin … is under 10 %") and APPROVE is disabled. If it is green because Orbiter 1A can afford it, write down REQUIRED and AVAILABLE.

## Test 6: a moon of another planet
1. Still in `fltcom-lko-single`, click FLT Orbiter, TRANSFER TO BODY, BODY IKE, then BRIEF.

Pass: a red line says that only Kerbin's own moons and the other planets of the Sun can be reached.

## Test 7: taking control back during the ejection
1. Quit to the main menu, resume `fltcom-kerbin-transfer`, then brief and approve the Test 1 transfer.
2. Say WARP until the LEAVE FOR DUNA burn has started (engine running). Then click SCRUB on the operation in the display.
3. Take `eject-abort.png`.

Pass: the engine stops, the ship answers your keys again, and the operation shows ABORTED. The ship stays on whatever orbit the part-burn left it on. FLTCOM does nothing more.

## Perf: delta-v of the Duna transfer
Use the Test 2 flight. Write down REQUIRED from the briefing, and the delta-v actually used (the stock delta-v readout in the staging panel, before LEAVE FOR DUNA and after DONE). For reference: from 100 km, community delta-v maps give about 1,060 m/s to leave for Duna and about 610 m/s to capture into a low Duna orbit.

Pass: the delta-v used is within 5 % of the briefing's REQUIRED.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
