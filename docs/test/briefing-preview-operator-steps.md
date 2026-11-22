# Briefing preview operator steps: see the planned maneuver on the briefing card

The briefing card now has a PLAN PREVIEW under CONTINGENCIES. It draws the body, its safe floor as a thin dotted red ring, the ship's orbit now in green, and each burn as a numbered circle. The number matches the row in BURN TIMELINE. The orbit after an earlier burn is dashed yellow and the final orbit is solid yellow (red when it dips under the safe floor).

Nothing in these steps approves a burn. Scrub every briefing when you are done looking.

## Set up
1. Close KSP. Run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game and pick `fltcom-mun-station`.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser at full width.

## Test A: circular orbit at the Mun
1. Select FLT Mun Probe. In COMPOSE ORBIT CHANGE pick CIRCULAR ORBIT AT ALTITUDE, type 19, and click BRIEF.
   Pass: PLAN PREVIEW shows the Mun with the green orbit around it, circle 1 on a dashed yellow ellipse and circle 2 on a solid yellow circle close to the Mun.
2. Hold the mouse over the second row of BURN TIMELINE.
   Pass: the row lights up, circle 2 and its orbit get thicker, the others fade, and the line under the picture reads `BURN 2 · ...` with the row's name.
3. Hold the mouse over circle 1 in the picture.
   Pass: the first row of BURN TIMELINE lights up.
4. Compare with the map: open the map behind the card (scrub first if it covers it) and brief the same change again.
   Pass: the burn points and orbits sit in the same places as the map's planned orbits, seen from a different angle.
5. Scrub. Compose CIRCULAR ORBIT AT ALTITUDE again with 3 and BRIEF.
   Pass: the final orbit and circle 2 are red and the line under the picture reads `FINAL · UNDER THE SAFE FLOOR`. Scrub.
6. Check that the card does not scroll further than before: the APPROVE and SCRUB buttons stay where they were on earlier briefings.

## Test B: land on the Mun
1. Select FLT Mun Probe, pick LAND, choose CUSTOM LAT/LON, type 2 and 40, and BRIEF.
   Pass: a yellow line runs from circle 1 down to the Mun's surface, circle 2 sits where the descent starts, and a yellow × marks the target. Scrub.

## Test C: Kerbin to the Mun
1. Quit to the main menu, Resume Game and pick `fltcom-lko-single`.
2. Select FLT Orbiter, pick TRANSFER TO BODY, choose MUN, and BRIEF.
   Pass: the picture is centred on Kerbin, a dashed ellipse reaches out to the Mun (drawn small and labelled MUN), circle 1 is on the green orbit and circle 2 is at the Mun, with a small solid orbit round it. Scrub.

## Test D: a launch
1. Quit to the main menu, Resume Game and pick `fltcom-pad-kerbalx`.
2. Select FLT Kerbal X, pick LAUNCH to 80 km, and BRIEF LAUNCH.
   Pass: a yellow curve climbs from the pad on Kerbin to the target orbit, which is drawn solid above the dotted red atmosphere ring. Scrub.

## Leave
1. Quit KSP. Run `tools/play-test.ps1 -Off`.

Fail notes to write down: which test, which briefing, and what the picture showed instead (a screenshot of the card is best).
