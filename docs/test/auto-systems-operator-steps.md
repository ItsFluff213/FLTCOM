# Auto systems operator steps: staging plan, landing gear, fairings and panels

Saves used: `fltcom-pad-kerbalx` (the stock Kerbal X on the launch pad) and `fltcom-land-mun` (the stock Two-Stage Lander in a 20 km Mun orbit). The play-test script installs both. Test D builds one small rocket from stock parts in the VAB; nothing needs downloading.

FLTCOM writes what it does to `GameData/FLTCOM/PluginData/fltcom.log` in the KSP folder. Lines starting `auto systems`, `auto-staging` and `burn` are the ones these steps ask about.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the FLTCOM folder run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-pad-kerbalx`. You start on the pad in FLT Kerbal X.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test A: the stage plan and a launch with auto systems on
1. In the display click FLT Kerbal X in the vessel list. In COMPOSE LAUNCH, check that the AUTO SYSTEMS box is ticked and reads ON.
2. Click BRIEF LAUNCH.
   Pass: the briefing has a STAGE PLAN with one line per stage. The top line ends `AT LIFTOFF` and lights the first engines; later lines say what each stage drops or lights and at what time (for example `DROP 2 BOOSTERS, AT 1:05`); the parachute stage ends with `KEPT`. AUTOMATIC ACTIONS starts with `STAGE IN EVERY BURN: DROP SPENT BOOSTERS AND EMPTY TANKS, NEVER PARACHUTES OR THE PROBE CORE`. If the craft has landing legs it also lists `RETRACT LANDING GEAR ABOVE 500 M OR WHEN CLIMBING FAST`. No stage line is red.
3. In the DOSSIER column on the left, look at TYPE, SITUATION and COMMS.
   Pass: every value fits on one line (for example `LINK 82%`, not `LINK` over `82%`, and no word split in two).
4. Take `autosys-briefing.png` of the briefing.
5. Click APPROVE. Leave the keyboard and mouse alone in KSP until the operation shows done.
   Pass: boosters fall away only after their engines stop; any landing legs stay folded; the parachute never opens. `fltcom.log` has an `auto-staging FLT Kerbal X into stage ...` line for each staging, and, if the craft has legs, `auto systems FLT Kerbal X: landing gear up at ... m`.
6. Fail notes: the stage plan text, the staging the game did, and the `auto-staging` and `auto systems` lines of `fltcom.log`.

## Test B: staging in a node burn, never into the parachutes
1. With FLT Kerbal X in orbit after Test A, compose CHANGE APOAPSIS to 2000 km and BRIEF.
   Pass: the STAGE PLAN shows which stages fire `WHEN THE STAGE ABOVE RUNS DRY`, and the parachute stage reads `KEPT`.
2. APPROVE and let FLTCOM fly the burn.
   Pass: when a stage runs dry FLTCOM drops it and the burn goes on. The parachute never opens. If the only stage left would open the parachutes, the burn stops and `fltcom.log` says `auto-staging FLT Kerbal X holds at stage ...: the next stage holds parachutes`.
3. Fail notes: what was staged, and the `auto-staging` lines.

## Test C: landing gear on the Mun
1. Quit to the main menu, Resume Game, pick `fltcom-land-mun`, and fly FLT Lander M.
2. In the display select FLT Lander M, compose LAND, keep CUSTOM LAT/LON or any site the list offers, and BRIEF.
   Pass: AUTOMATIC ACTIONS lists `LOWER LANDING GEAR 15 S OR 1000 M ABOVE THE GROUND, BRAKES ON AT TOUCHDOWN`.
3. APPROVE and watch the descent.
   Pass: the legs come down about 1 km above the ground (or 15 seconds before touchdown on a fast descent), not before. After touchdown the BRAKES light is on in the action-group bar. `fltcom.log` has `landing gear down at ...` and `brakes on at touchdown`.
4. Fail notes: the height the legs came down at, and the `auto systems` lines.

## Test D: fairing and panels on a launch
1. Go to the Space Center and open the VAB. Build this from stock parts, top to bottom: a Probodobodyne OKTO2 probe core; an AE-FF1 Airstream Protective Shell under it, with its shell built up and closed around the probe core; an FL-T400 fuel tank with two OX-4L 1x6 solar panels on its sides; an LV-909 engine; a TD-12 decoupler; two FL-T800 fuel tanks; an LV-T45 engine. Keep the stages the VAB makes (LV-T45, then decoupler and LV-909, with the fairing in a stage of its own).
2. Launch it to the pad. Right-click each solar panel and click Extend Solar Panel, so the panels stand open.
3. In the display select the rocket, leave AUTO SYSTEMS ON, and BRIEF LAUNCH.
   Pass: the STAGE PLAN shows the fairing stage as `JETTISON THE FAIRING, ABOVE 63 KM`. AUTOMATIC ACTIONS lists `RETRACT 2 PANELS ...` and `JETTISON 1 FAIRING ABOVE 63 KM`.
4. APPROVE and watch.
   Pass: the panels fold before liftoff; the decoupler fires only when the LV-T45 runs dry; the fairing comes off at about 63 km, before the finishing burn; once the periapsis is above 70 km the panels open again. `fltcom.log` has `retracted 2 panels ...`, `jettisoned 1 fairing at 63.x km`, and `extended 2 panels ... in a stable orbit`.
5. Fail notes: the altitude of the fairing jettison, and the `auto systems` lines.

## Test E: switched off
1. Back on the pad (Test D's rocket, or Kerbal X from the `fltcom-pad-kerbalx` save), untick AUTO SYSTEMS so it reads OFF, and BRIEF LAUNCH.
   Pass: AUTOMATIC ACTIONS has one line, `AUTO SYSTEMS OFF: NO GEAR, FAIRING OR PANEL ACTIONS; STAGING ONLY AS BEFORE ...`.
2. APPROVE and watch.
   Pass: FLTCOM stages the ascent as before, but it does not fold panels, move the gear or jettison the fairing. `fltcom.log` has no `auto systems` lines for this flight.

## Leave
1. Quit KSP and run `tools/play-test.ps1 -Off`. Your own mods and saves come back.
