# preview-polish operator steps: a rocket on the pad shows its position, rocks come in sizes

Saves used: `fltcom-pad-kerbalx` (the stock Kerbal X on the launch pad, from the `pad-kerbalx` recipe) and `fltcom-crowded-system` (from the `crowded-system` recipe, which also holds the stock asteroids and comets a new sandbox game adds). Both recipes are in `tests/fixtures/saves/recipes.cfg`. If either save is missing, build it once as the "Build the save" section of `docs/test/map-declutter-operator-steps.md` describes, with the recipe name in place of `crowded-system`.

Nothing here changes your ships. You only look and click.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam).

## Test A: the dossier of a rocket on the pad
1. Resume Game, pick `fltcom-pad-kerbalx`, and wait until you sit on the launch pad.
2. Open the address the script printed (default http://127.0.0.1:47800/) in a browser. Click FLT Kerbal X in the list at the bottom.
   Pass: in the DOSSIER on the right the row under BODY is called POSITION and reads about `0.10°S 74.56°W`. It does not say AP × PE.
3. Click any ship that is in orbit, if the save has one, or come back to this step in Test B.
   Pass: for an orbiting ship the same row is called AP × PE and shows two heights in km.
4. Press Escape in KSP and quit to the main menu.

## Test B: asteroids and comets by size
1. Resume Game, pick `fltcom-crowded-system`, and wait until you are flying FLT Mun Lander 1.
2. In the browser click SUN at the top and scroll the mouse wheel out until you see the brown rocks near Kerbin's orbit.
   Pass: the rocks are not all one size. Some are clearly small and some clearly large. A comet is pale blue with a faint tail.
3. In KSP open the Tracking Station. Click a few asteroids in its list on the left and read each one's size class (a letter A to E) in the info panel. Pick the smallest class you find and the largest.
4. In the browser click each of those two rocks in the list at the bottom, one after the other, and look at the green-framed rock on the map.
   Pass: the rock with the larger letter is drawn larger. Class A is the smallest, class E the largest.
5. If the Tracking Station lists a comet, click it in the browser list.
   Pass: it is drawn pale blue with a tail, even if its name does not contain the word "Comet".
6. Click the orbiting FLT Mun Lander 1 in the list.
   Pass: its DOSSIER row is called AP × PE (this finishes Test A step 3).

## Finish
1. Quit KSP.
2. In the PowerShell window run `tools/play-test.ps1 -Off`.
   Pass: it prints `play-test: your mods and saves are back.`

Tell the agent which step failed and what you saw instead.
