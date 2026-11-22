# Deploy constellation operator steps: evenly spaced synchronous satellites, the launch stage coming home and the transfer stage burning up

Save used: `fltcom-pad-kerbalx`, which the play-test script installs. You build one carrier with three satellites from stock parts in the VAB; nothing needs downloading.

FLTCOM writes what it does to `GameData/FLTCOM/PluginData/fltcom.log` in the KSP folder. The lines these steps ask about start with `burn` and name the operation, such as `burn SILENT HERON #3: released FLT Carrier Sat 1`.

## What FLTCOM counts as a satellite
A satellite is any part group beyond a decoupler, separator or docking port that carries its own probe core, or a crewed pod with its pilot aboard. An empty pod does not count. Without one it is cargo, not a satellite, and the briefing does not list it. Without parachutes on the carrier, the briefing cannot land it at Kerbin and offers a disposal orbit instead.

## Build the carrier (VAB, stock parts only)
1. Each satellite, three times: a Probodobodyne OKTO, an Oscar-B fuel tank, an LV-1 "Ant" engine, a Z-100 battery and an OX-STAT solar panel. Attach it to the carrier with a TR-18A stack decoupler, or radially with a TT-38K separator. Put each decoupler or separator in its own stage.
2. The carrier: a Probodobodyne OKTO2 or HECS core, two FL-T400 tanks, an LV-909 "Terrier", and a Mk16 parachute on top.
3. The launcher: any stock booster that puts the whole stack in an 80 km Kerbin orbit with at least 1,800 m/s left in the carrier (a Kerbal X lower stack works).
4. Save the craft as `FLT Carrier` and launch it.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the FLTCOM folder run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-pad-kerbalx`, go to the VAB and build the carrier above.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.
4. Launch FLT Carrier to an 80 km orbit, with FLTCOM's LAUNCH operation or by hand.

## Test A: the Composer option
1. In the display click FLT Carrier. In COMPOSE ORBIT CHANGE pick DEPLOY CONSTELLATION.
   Pass: ALTITUDE KM fills in with 2863, and the line above it reads `AROUND KERBIN, EQUATORIAL, EVENLY SPACED. SYNCHRONOUS ORBIT 2,863 KM.`
2. Type 90000 into ALTITUDE KM and click BRIEF.
   Pass: the briefing refuses the altitude, says it lies outside Kerbin's sphere of influence and names the highest valid altitude. APPROVE is off.
3. Type 30 and BRIEF.
   Pass: the briefing refuses it as under the safe floor.
4. Fail notes: the text of the briefing's error line.

## Test B: the briefing
1. Pick DEPLOY CONSTELLATION again, keep 2863, click LAND HOME under END OF LIFE, and BRIEF. Tests B to E fly the carrier home; Test F covers the default, which burns it up.
   Pass, all of these:
   - The operation line reads `DEPLOY CONSTELLATION AT 2,863 KM`.
   - BURN TIMELINE: raise apoapsis to 2863 km, then lower periapsis to the resonant orbit, then for each satellite a `release FLT Carrier Sat k at apoapsis, pass k` row followed by that satellite's `circularize` row, and last `fly home`.
   - STAGE PLAN has one line per satellite, such as `RELEASE FLT CARRIER SAT 2, AT APOAPSIS, PASS 2`.
   - DEPLOYMENT shows `RING 2863.0 km · SYNCHRONOUS` and a table with each satellite's mass, own Δv, circularize Δv, release pass and spacing. No spacing is amber.
   - STAGES has three rows. TRANSFER STAGE reads `LAND AT HOME` with its Δv as `N OF M m/s`. LAUNCH STAGE reads `STAYS` and says why, because this carrier was launched without a launch stage of its own. The last row is `3 SATELLITES`.
   - PLAN PREVIEW draws the dashed ring, the resonant orbit inside it, and three diamond SATELLITE SLOTS a third of the ring apart.
2. Take `deploy-briefing.png` of the briefing.
3. Fail notes: which line is missing or reads differently.

## Test C: the deployment and the way home
1. Click APPROVE. Leave the keyboard and mouse alone in KSP until the operation shows done. Accept the warp prompts.
   Pass: at each apoapsis one satellite separates, FLTCOM switches to it, and it circularizes itself on the ring. The vessel list then shows `FLT Carrier Sat 1`, `Sat 2` and `Sat 3`.
2. When the last satellite is out, watch the carrier.
   Pass: the operation ends with a note `spacing within ... deg; carrier return approved: ...`, a second operation named `<codename> HOME` appears in OPERATIONS, and the carrier deorbits, reenters, opens its parachute and lands. If the spacing is off by more than a degree, the note also says `spread-evenly trim approved` and a third operation trims the satellites.
3. In the display, look at the three satellites' MEAN AN. column.
   Pass: they are about 120 degrees apart.
4. Fail notes: the step the operation stopped on, the satellite names in the vessel list, and the operation note, and the `burn` lines of `fltcom.log`.

## Test D: a carrier that cannot come home
1. Revert to launch, remove the Mk16 parachute in the VAB, launch again to 80 km, and brief DEPLOY CONSTELLATION at 2863 with LAND HOME.
   Pass: the STAGES row TRANSFER STAGE reads `PARK` in amber, CONTINGENCIES has an amber line saying the carrier parks in a disposal orbit, and the timeline has no `fly home` row.
2. Remove the OKTO from one satellite and brief again.
   Pass: DEPLOYMENT lists two satellites, not three.
3. Fail notes: the STAGES and CONTINGENCIES text.

## Test E: a polar ring
1. Revert to the VAB, put the full carrier back (parachute and three OKTOs), and launch it to an 80 km equatorial orbit as before. Click FLT Carrier, pick DEPLOY CONSTELLATION, click LAND HOME, and click POLAR.
   Pass: POLAR lights green, RING INCLINATION DEG reads 90, ASCENDING NODE DEG can be typed in, and the line above reads `AROUND KERBIN, POLAR, EVENLY SPACED.` Click EQUATORIAL: inclination goes back to 0 and ASCENDING NODE DEG greys out.
2. Click POLAR, type 30 into ASCENDING NODE DEG, keep 2863 and click BRIEF.
   Pass: the line under OPERATION includes `POLAR RING, NODE 30 DEG`, the first BURN TIMELINE row is `plane change into the polar ring, node 30 deg` at about 3,200 m/s, CONTINGENCIES has a red propellant line, and APPROVE is off. Turning an 80 km orbit by 90 degrees costs more than this carrier holds.
3. Type 181 into RING INCLINATION DEG.
   Pass: BRIEF greys out. Type 90 again.
4. Revert to launch. With the carrier on the pad, in COMPOSE ORBIT CHANGE set APOAPSIS KM 80, PERIAPSIS KM 80, INCLINATION DEG 90 and LAN DEG 30, then BRIEF LAUNCH and APPROVE.
   Pass: the carrier reaches an 80 km orbit over the poles.
5. Pick DEPLOY CONSTELLATION, click POLAR, type 30 into ASCENDING NODE DEG, and BRIEF.
   Pass: the plane change row is small (under about 100 m/s) or missing, there is no red contingency, and APPROVE is on.
6. Click APPROVE and leave KSP alone until the operation shows done. Accept the warp prompts.
   Pass: three satellites separate and circularize at 2,863 km. In the map their ring passes over both poles, and the operation note reads `spacing within ... deg` with a number under 1. The carrier then flies home as in Test C.
7. Fail notes: the plane change row's Δv, the CONTINGENCIES text, the operation note, and the `burn` lines of `fltcom.log`.

## Test F: the launch stage comes home, the transfer stage burns up
This test starts its own session. If KSP is still running from the tests above, quit it and run `tools/play-test.ps1 -Off` first.
1. Close KSP and CKAN. In a PowerShell window in the FLTCOM folder run `tools/play-test.ps1`. Start `KSP_x64.exe` directly, Resume Game, pick `fltcom-pad-kerbalx`, go to the VAB and open the display at the address the script printed.
2. Build `FLT Stack` in three parts, stock parts only:
   - Three satellites, each as in the carrier build above (OKTO, Oscar-B, Ant, battery, solar panel, on its own decoupler or separator).
   - The transfer stage they sit on: a Probodobodyne OKTO2 core, two FL-T400 tanks and an LV-909 "Terrier". No parachute.
   - The launch stage under it, below a TR-18A stack decoupler: a Probodobodyne HECS core, a Rockomax X200-32 tank, an LV-T45 "Swivel" and two radial Mk16 parachutes on the tank. The launch stage must reach orbit itself; side boosters are fine and drop away as usual.
3. Launch FLT Stack to an 80 km Kerbin orbit with the launch stage still attached and at least 150 m/s left in it.
4. Click FLT Stack, pick DEPLOY CONSTELLATION, keep 2863 and keep END OF LIFE on AUTO. BRIEF.
   Pass, all of these:
   - STAGES has three rows. LAUNCH STAGE reads `RETURNS HOME: SPLITS OFF IN THE PARKING ORBIT, ...` with its deorbit Δv as `N OF M m/s`. TRANSFER STAGE reads `BURN UP: PERIAPSIS TO ...` with its Δv. The last row is `3 SATELLITES · CIRCULARIZE ON THE RING`.
   - The first BURN TIMELINE row is `release FLT Stack Launch Stage`. Near the end there is a `burn up: ...` row, and the last row is `check spacing`.
   - REQUIRED in DELTA-V BUDGET includes the burn-up.
5. Click PARK, then LAND HOME, then AUTO under END OF LIFE, and BRIEF after each.
   Pass: TRANSFER STAGE reads `PARK`, then `LAND AT HOME` (amber `PARK` if the transfer stage lacks the Δv or a parachute), then `BURN UP` again.
6. Take `deploy-stages.png` of the STAGES section with AUTO selected.
7. Click APPROVE. Leave the keyboard and mouse alone in KSP until the operation shows done. Accept the warp prompts.
   Pass: first the launch stage separates and shows in the vessel list as `FLT Stack Launch Stage`, still in orbit. It stays there while the satellites go out, one per apoapsis. Each satellite circularizes on the ring.
8. Watch the transfer stage after the last satellite.
   Pass: it lowers its periapsis into the atmosphere, and the operation ends with a note that includes `launch stage return approved`. A landing operation for `FLT Stack Launch Stage` appears in OPERATIONS. It starts only after the deployment is done, never between two releases.
9. Watch the launch stage.
   Pass: it deorbits, reenters, opens its parachutes and lands near the launch site. The transfer stage reenters and is destroyed.
10. Revert to the VAB and replace the HECS on the launch stage with a Mk1 Command Pod. Put Jebediah in the pod in the crew tab, launch to 80 km as before, and brief DEPLOY CONSTELLATION.
    Pass: LAUNCH STAGE reads `RETURNS HOME`, the same as with the probe core.
11. Revert to the VAB, empty the Mk1 pod in the crew tab, launch again and brief.
    Pass: LAUNCH STAGE reads `STAYS: A LAUNCH STAGE COMES HOME ONLY WITH ITS OWN PROBE CORE OR CREWED POD, ...` and there is no `release FLT Stack Launch Stage` row.
12. Fail notes: the STAGES text, the first and last BURN TIMELINE rows, the operation note, the vessel list after step 7, where the launch stage landed, and the `burn` lines of `fltcom.log`.
13. Quit KSP and run `tools/play-test.ps1 -Off`.

## Out of scope
Side boosters that drop away during the ascent do not fly back, and nothing lands on its engines. KSP deletes a craft that is unloaded low in the atmosphere, so only stages that reach orbit come home: the launch stage, and the carrier when you pick LAND HOME.

## Clean up
1. Quit KSP and run `tools/play-test.ps1 -Off`. Your own mods and saves come back.
