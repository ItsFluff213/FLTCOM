# look-2 operator steps: dashboard look, bigger ships, thicker orbit lines

Save used: `fltcom-fleet-mixed` (already built from the `fleet-mixed` recipe in `tests/fixtures/saves/recipes.cfg`; the play-test script installs it, nothing to build). It holds FLT Orbiter (the ship you fly), FLT Station, FLT Survey Relay, FLT Mun Probe and FLT Lost Probe, which has no signal.

Nothing here changes your ships. You only look and click.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-fleet-mixed`, and wait until you are flying FLT Orbiter.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser, full screen. Wait about 30 seconds for the planet pictures and ship shapes.

## Test A: ships and orbit lines on the map
1. Click KERBIN at the top. Look at the map without zooming.
   Pass: every ship has a small square of four corner marks around it, and every ship's shape is easy to see (roughly a finger-tip wide on a laptop screen).
2. Look at the orbit lines.
   Pass: ship orbits are clearly thicker than a hair line; moon orbits are thinner and dimmer than ship orbits.
3. Click FLT Station in the list.
   Pass: its orbit is green and the thickest line on the map; its corner marks are green and a little larger than the others.
4. Scroll the mouse wheel all the way out, then all the way in.
   Pass: at every zoom the ships and their corner marks stay the same size on the screen, and the lines keep their thickness.
5. Turn the map (left-drag) until a ship passes behind Kerbin.
   Pass: that ship's corner marks turn dashed and its name dims; it never disappears.
6. Only if an operation is listed under OPERATIONS with a burn still to come: look at that ship on the map.
   Pass: its orbit after the burn is a yellow dashed line, about as thick as the ship orbits. Skip this step if there is no such operation.

## Test B: names on the map
1. With KERBIN as center, look at the ship and moon names.
   Pass: names are in a bold typewriter letter, in capitals, next to their ship but outside its corner marks; no two names overlap, and no name covers the numbers in the four corners of the map (delta-v, next burn, UT, center).

## Test C: the dashboard
1. Look at the whole page.
   Pass: the title FLTCOM at the top left is complete (all six letters), widely spaced; panels have thin grey frames with brighter corner marks; headings (DOSSIER, BLUEPRINT, GROUPS, ...) start with a short white bar and are widely spaced capitals.
2. Look at the right column without scrolling.
   Pass: the grey grainy telescope picture of the selected ship sits beside the dossier facts, near the top, and is visible without scrolling.
3. Look at the top right of the map.
   Pass: NEXT BURN shows a large clock when a burn is planned, with the ship's name under it in yellow.
4. Click FLT Lost Probe.
   Pass: a red ">> COMMUNICATIONS LOST <<" bar shows over the map with the probe's code and name under it, blinking slowly; with CLEAN ticked it stops blinking.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
