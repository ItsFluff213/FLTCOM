# P15 operator steps: groups and background burns

Save used: `fltcom-relay-ring` (already built from the `relay-ring` recipe in `tests/fixtures/saves/recipes.cfg`; the play-test script installs it). It holds one ship you fly (FLT Orbiter) and four ion relays (FLT Relay 1 to 4) in one loose Kerbin orbit, far away.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-relay-ring`.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test A: spread four relays evenly (background burns)
1. In the GROUPS panel type a name in "NEW GROUP", select FLT Relay 1 first, and create the group. Add FLT Relay 2, 3 and 4 as members. The group shows as one expandable row and its members light up on the map.
2. With the group selected, choose "SPREAD EVENLY AROUND ONE ORBIT", set a target orbit (for example 900 km) and open the briefing. Each relay should show its own burns and fuel budget.
3. Click APPROVE.
4. One warp prompt appears for the whole group ("4 ships, N burns ...: warp through all?"). Accept it once. Stay on FLT Orbiter; do not switch ships.
5. Let it run until the missions list shows all burns done.

Pass:
- the four relays end within 2 degrees of equal spacing around the orbit (90 degrees apart),
- each ends within 1% of the target orbit,
- each relay has less fuel than before.

If a burn is missed or the game drops out of warp, note what the prompt said; that is expected behaviour for anything unexpected, and it must ask again rather than carry on.

## Test B: the ship you fly
1. Make a plan for FLT Orbiter (a single ship change is fine) and approve it.
2. Watch the countdown in the display. When the burn is due, FLTCOM waits and shows "LET FLTCOM FLY IT".
3. Pass: nothing happens to your ship until you click it (or you fly the burn by hand). If you do neither, the burn is marked missed and re-planned. FLTCOM never takes the ship without the click.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
