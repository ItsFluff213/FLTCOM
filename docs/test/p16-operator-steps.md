# P16 operator steps: the new look (3D map, planets, ship models, telescope)

Save used: `fltcom-fleet-mixed` (already built from the `fleet-mixed` recipe in `tests/fixtures/saves/recipes.cfg`; the play-test script installs it, nothing to build). It holds FLT Orbiter (the ship you fly), FLT Station, FLT Survey Relay, FLT Mun Probe and FLT Lost Probe, which has no signal.

Nothing here changes your ships. You only look, click and turn the camera.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-fleet-mixed`, and wait until you are flying FLT Orbiter.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser. Wait about 30 seconds: the planet pictures and ship shapes are made once in the background.

## Test A: colours and the map
1. Look at the whole page.
   Pass: the background is near-black and neutral, text and ordinary ships are white or grey, and there is no blue tint anywhere except on ships that are in a running operation.
2. Click FLT Station in the list.
   Pass: its row, label and orbit turn green; FLT Lost Probe is red; every other ship is grey.
3. On the map, hold the left mouse button and drag: the map turns and tilts. Hold the right button and drag: the map slides. Turn the mouse wheel: it zooms in and out.
4. Leave the mouse alone for about 10 seconds.
   Pass: the map starts to turn very slowly by itself, and stops the moment you touch it. With CLEAN ticked it never turns by itself.
5. Click a ship's label or the ship itself on the map.
   Pass: the camera flies smoothly to it and keeps following it.

## Test B: planets
1. Zoom in on Kerbin (click KERBIN at the top, then scroll in).
   Pass: Kerbin is a ball with its real surface (oceans, continents, ice caps), lit on one side with a dark night side. The lit side faces the Sun.
2. Compare with the game's map view: find the Space Center's continent on both.
   Pass: the continents sit in the same places. If the picture is turned the wrong way round or mirrored, write down which way, that is a one-line fix.
3. Click MUN and MINMUS at the top.
   Pass: each shows its own grey or mint surface, lit the same way. A plain grey ball means the picture did not arrive; note which body.

## Test C: ship models
1. Zoom in close on FLT Orbiter on the map.
   Pass: it is a small 3D shape of the real craft (not a cone), always at least a few pixels big even when far away.
2. In the game, turn FLT Orbiter (press W/A/D for a few seconds), then let go.
   Pass: within a few seconds the small model on the map turns the same way.

## Test D: telescope view
1. Select FLT Orbiter. Scroll the right column down to BLUEPRINT.
   Pass: beside or under the blueprint is a small square showing the ship in grey, grainy and slightly blurred, drifting slowly; the caption reads LONG RANGE OPTICS.
2. Turn the ship in the game again.
   Pass: the telescope picture turns with it.
3. Tick CLEAN.
   Pass: the grain and blur go away and the picture stops drifting.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
