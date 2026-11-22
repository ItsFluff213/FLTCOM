# P10 operator steps: launch rockets to orbit

Saves used: `fltcom-pad-kerbalx` (FLT Kerbal X on the launch pad) and `fltcom-lko-single` (FLT Orbiter already in orbit). Both are built fixtures in `tests/fixtures/saves/`; the play-test script installs them, nothing needs building in the game.

FLTCOM writes what it does to `GameData/FLTCOM/PluginData/fltcom.log` in the KSP folder. Lines starting `ascent`, `auto-staging` and `burn` are the ones these steps ask about.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-pad-kerbalx`. You start on the pad in FLT Kerbal X.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test 1: launch to the standard low orbit (lane 1, with lane 4 watched at the same time)
1. In the display click FLT Kerbal X in the vessel list. The composer heading reads COMPOSE LAUNCH and shows APOAPSIS KM 80, the other fields blank.
2. Click BRIEF LAUNCH. The briefing lists two burns, ASCENT and FINISH THE ORBIT (ESTIMATE), and a delta-v budget with no red line.
3. Click APPROVE. Leave the keyboard and mouse alone in KSP from here on.
4. About a minute later the rocket lights its engines, leaves the clamps and climbs straight up, then leans east. Watch the staging (that is lane 4): every booster pair should fall away only after its engines stop, never while they still burn. Take `staging.png` when a pair separates.
5. Above 70 km the engines stop. The display then replaces the estimate with one or more finishing burns; accept the warp prompt if one appears. FLTCOM flies the burn at apoapsis on its own.
6. When the operation shows done, take `launch-lko.png` of the orbit info (map view, or the vessel's dossier in the display).

Pass: apoapsis and periapsis both within 2 km of 80 km; no booster dropped with a burning engine (the log's `auto-staging` lines show `burning` engines and `spent dropped` counts); `fltcom.log` has a line `ascent ... pad velocity east` with a positive number. Write down the delta-v used (the stock delta-v readout before and after, or the briefing's required figure versus what is left). The perf rule is under 3,500 m/s from the pad to orbit.

If the rocket turns west or south instead of east, stop (press Alt+H) and send the log: the `pad velocity east` number tells which way the frame is wrong.

## Test 2: a custom 150 km by 120 km orbit (lane 2)
1. Quit to the main menu and resume `fltcom-pad-kerbalx` again (a fresh rocket on the pad).
2. Click FLT Kerbal X, type APOAPSIS KM 150 and PERIAPSIS KM 120, BRIEF LAUNCH, APPROVE.
3. Let it fly to the end. Take `launch-custom.png`.

Pass: apoapsis within 2 km of 150 km and periapsis within 2 km of 120 km.

## Test 3: 45 degrees with a LAN (lane 3)
1. Resume `fltcom-pad-kerbalx` again.
2. Click FLT Kerbal X, APOAPSIS KM 80, INCLINATION DEG 45, LAN DEG 120, BRIEF LAUNCH. The ASCENT burn now starts some time later: FLTCOM waits for the pad to pass under that plane. APPROVE and accept the warp prompt.
3. Let it fly to the end. Take `launch-incl.png` showing inclination and LAN (map view, orbit info).

Pass: inclination within 0.2 degrees of 45, LAN within 1 degree of 120.

## Test 4: taking control back (lane 5)
1. Resume `fltcom-pad-kerbalx`, brief and approve the 80 km launch as in Test 1.
2. Once the rocket leans over (about 10 km up), press and hold S (pitch) for a moment.
3. Take `ascent-release.png`.

Pass: the screen shows `>> AUTONOMOUS CONTROL RELEASED <<`, the display marks the operation released, and the rocket answers your keys; FLTCOM no longer throttles or steers. Fly it or abort as you like.

## Test 5: not enough delta-v (lane 6)
1. Resume `fltcom-pad-kerbalx`. Click FLT Kerbal X, type APOAPSIS KM 30000 (leave the rest blank), BRIEF LAUNCH.
2. Take `launch-short.png` of the briefing.

Pass: the briefing shows a red contingency ("short of delta-v by ..." or "delta-v margin ... is under 10 %") and APPROVE is disabled. If it is still green, raise the apoapsis (for example 60000) until REQUIRED is above AVAILABLE; it must turn red then.

## Test 6: no launch for a ship in orbit (lane 7)
1. Quit to the main menu, resume `fltcom-lko-single`.
2. In the display click FLT Orbiter. Take `launch-refused.png` of the composer.

Pass: the composer reads COMPOSE ORBIT CHANGE with the usual list (CIRCULARIZE and so on) and offers no launch fields.

## Test 7 (optional): with FAR (lane 10)
Only if you want FAR support checked. Quit KSP, run `tools/ksp-testmode.ps1 variant add FerramAerospaceResearch`, start KSP, repeat Test 1. Take `launch-far.png`. Pass: both apsides within 5 km of 80 km. Afterwards quit and run `tools/ksp-testmode.ps1 variant remove FerramAerospaceResearch`.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
