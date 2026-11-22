# Background auto systems: operator steps

What changed: when a group burn runs on a ship you are not flying (on rails) and the stage it burns on runs dry, FLTCOM now moves on to the next stage with engines, as long as AUTO SYSTEMS is ON. With AUTO SYSTEMS OFF the burn stops at the dry stage and says how far it got. On rails FLTCOM only stages: it does not touch panels, gear, fairings or parachutes, and the mission note says so.

One thing to know before you start: the spent stage stays attached to the ship in the save. The burn is flown as if it had been dropped, the same way the briefing counts delta-v. When you next fly that ship, press space once to drop it.

Save used: `fltcom-bg-two-stage`, new, built from the `bg-two-stage` recipe in `tests/fixtures/saves/recipes.cfg`. It holds three ships in one 100 km Kerbin orbit, far apart:
- FLT Orbiter, the ship you fly.
- FLT Single, an Orbiter 1A with one stage.
- FLT Two Stage, a Kerbal 1 with 10% fuel in every tank, so its first stages run dry early.

## Build the save (once)
The test harness builds it inside KSP from the recipe; you start and stop the game.
1. Close KSP and CKAN. In a PowerShell window in the repo run these one at a time and wait for each to finish:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture bg-two-stage
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
   ```

## Set up
1. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-bg-two-stage`.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.
4. Read how much delta-v FLT Two Stage has per stage: in the display, click FLT Two Stage in the vessel list and look at its delta-v by stage. The first row is the stage that fires first (the side boosters). Write down that first number and the total.
5. Press F5 in KSP to quicksave, so Test B can start from the same point.

## Test A: AUTO SYSTEMS ON, the second ship stages on rails
1. In the GROUPS panel make a group named "Pair" with FLT Single and FLT Two Stage.
2. With the group selected, choose "RAISE APOAPSIS BY" and enter an amount whose burn needs more than FLT Two Stage's first stage but less than its total. About 300 km is a good first try. Leave AUTO SYSTEMS ON and click BRIEF.
3. In the briefing, check that FLT Two Stage's burn is larger than the first-stage number you wrote down and that no red line says it is short of fuel. If it is short, BRIEF again with a smaller amount; if the burn fits in the first stage, use a larger one.
4. Click APPROVE. Accept the warp prompt once. Stay on FLT Orbiter.
5. While FLT Two Stage burns, watch the missions list.

Pass:
- the mission note shows `auto systems: staging only in background` when the first burn starts,
- during FLT Two Stage's burn the note changes to `FLT Two Stage staged on rails into stage N after X m/s; the spent stage stays attached until you stage it in flight`,
- both burns finish and the missions list shows them done, with no hold,
- afterwards, switch to FLT Two Stage (map view, select it, "Switch To"). Its apoapsis matches the target and the side boosters are empty. Press space once per spent stage (watch the staging list in the lower left; stop before the parachute): each empty stage drops away and nothing explodes.

## Test B: AUTO SYSTEMS OFF, the burn stops at the dry stage
1. Press F9 in KSP to load the quicksave from step 5 of Set up. Wait for the display to show the three ships again.
2. Repeat Test A steps 1 to 3, but switch AUTO SYSTEMS to OFF before clicking BRIEF. Click APPROVE and accept the warp prompt.

Pass:
- FLT Single's burn finishes,
- FLT Two Stage's burn stops when its first stage is dry: the mission is put on hold, warp stops, and the note reads like `FLT Two Stage: the engines of stage N need staging and AUTO SYSTEMS is off after X of Y m/s`, where X is about the first-stage number you wrote down and Y is the full burn,
- FLT Two Stage's upper tanks are still full when you switch to it.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.

## If something looks wrong
Copy the lines that contain `background burn` or `auto systems` from `GameData/FLTCOM/PluginData/fltcom.log` into your report, with the note text you saw.
