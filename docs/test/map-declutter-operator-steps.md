# map-declutter operator steps: a readable system map, moons around their planet, rocks that look like rocks

Save used: `fltcom-crowded-system`, built from the `crowded-system` recipe in `tests/fixtures/saves/recipes.cfg`. It holds 15 ships (eight around Kerbin, landers and probes at the Mun and Minmus, four ships at Jool and its moons) and two pieces of debris. You fly FLT Mun Lander 1. A new sandbox game also adds its own stock asteroids and comets, so the save has those too. Your own crowded career save works as well; use it in place of `fltcom-crowded-system` below.

Nothing here changes your ships. You only look, scroll and click.

## Build the save (once)
The save does not exist yet. The test harness builds it inside KSP from the recipe; you start and stop the game.
1. Close KSP and CKAN. In a PowerShell window in the repo run these one at a time and wait for each to finish:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture crowded-system
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
   ```
   Pass: a folder `tests/fixtures/saves/crowded-system` now holds a `persistent.sfs`.

## Set up
1. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-crowded-system`, and wait until you are flying FLT Mun Lander 1.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser, full screen. Wait about 30 seconds for the planet pictures and ship shapes.

## Test A: the whole system, zoomed out
1. Click SUN at the top. Scroll the mouse wheel all the way out.
   Pass: the whole system fits on the map. No two planets touch, and no planet sits on another planet's orbit line.
2. Look at Jool.
   Pass: its five moons (Laythe, Vall, Tylo, Bop, Pol) each sit on their own ring around Jool, outside Jool's ball, innermost Laythe, outermost Pol. No moon is drawn inside or on top of Jool, and no two moon rings cross except Bop's and Pol's tilted ones.
3. Look at Kerbin.
   Pass: the Mun and Minmus sit on their own rings outside Kerbin's ball. Instead of a pile of names, Kerbin shows one boxed tag like "KERBIN · 8 SHIPS".
4. Look at all the names on the map.
   Pass: no two names overlap. FLT Mun Lander 1 (green, selected) always has its name.
5. Look at the asteroids and debris (small brown rocks and grey ship dots).
   Pass: they have no names while zoomed out, and the asteroids sit near Kerbin's orbit, not inside Moho's or Eve's.
6. Point the mouse at one asteroid without clicking.
   Pass: its name and corner marks appear; move the mouse away and they go again.

## Test B: zooming in
1. Click the "KERBIN · 8 SHIPS" tag.
   Pass: the map flies to Kerbin.
2. Scroll in slowly toward Kerbin.
   Pass: the moons glide inward to their true distances with no jump, and the tag splits into the separate ship names as the ships spread apart.
3. Click JOOL at the top, then scroll in and out.
   Pass: the moons always stay outside Jool's ball, and every ship around a moon moves with its moon.

## Test C: asteroids and comets up close
1. Click SUN at the top, then click a stock asteroid ("Ast. ...") in the list, then click its rock on the map and scroll all the way in.
   Pass: it is a lumpy, grey-brown rock with flat faces and a few dents, slowly tumbling. Its corner marks are still around it. The telescope picture beside the dossier shows the same kind of rock, not a ship.
2. Click a second asteroid the same way.
   Pass: it has a different shape and tumbles at a different speed.
3. Only if the list has a comet (its name contains "Comet" or starts with "C/"): click it on the map and scroll in.
   Pass: the rock is bluish-white with a faint glowing tail that points away from the Sun. Skip this step if there is no comet.
4. Click SUN at the top and scroll all the way out, then slowly back in toward Kerbin.
   Pass: at every zoom each asteroid is smaller than the planet ball it is near, no asteroid sits on or inside a planet ball or on a moon, and no two asteroids sit on top of each other. Bigger asteroid classes still look bigger. As you close in they glide to their true places with no jump.
5. Only if an asteroid orbits Kerbin itself (the list shows KERBIN as its body): click KERBIN at the top and scroll out and in.
   Pass: that asteroid stays outside Kerbin's ball and off the Mun's and Minmus's orbit circles, and moves with Kerbin's spread-out layout like the moons do.

## Test D: nothing else changed
1. Click KERBIN at the top, click ships in the list and on the map, drag to turn the map, right-drag to pan.
   Pass: ship colours, the green selected orbit, the corner marks, turning, panning and the center buttons work as before.

## Clean up
Quit KSP, then run `tools/play-test.ps1 -Off`.
