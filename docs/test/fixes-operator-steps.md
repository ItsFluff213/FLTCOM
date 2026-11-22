# Checking the M1 loose-end fixes in the game

You need KSP closed first. These steps use the play-test fleet (Kerbal X, Kerbal 2, probes and stations already in orbit).

## Get in and out

1. Quit KSP and CKAN.
2. In PowerShell, in the FLTCOM folder, run `tools/play-test.ps1`. It builds FLTCOM, sets up the test fleet and tells you when it is ready.
3. Start KSP and load the save it names.
4. When you are done, quit KSP and run `tools/play-test.ps1 -Off`. Your own mods and saves come back.

## A. Signal after a burn

1. Open the fleet display in your browser (the address the play-test script printed).
2. Note the signal for each vessel. Vessels with a relay link show a link, not the red "LOS" banner.
3. In the game, select any vessel and burn for about 10 seconds, then stop.
4. Look at the display for the next minute.
5. Pass: vessels that had a link still show a link, and the red "LOS" banner does not appear for every vessel at once.
6. Fail: every vessel shows LOS right after the burn. Write down how long it lasts and tell me.

## B. Delta-v of ships you are not flying

1. In the display, find FLT Kerbal X and FLT Kerbal 2 (both unloaded when you fly something else).
2. Switch to FLT Kerbal X and open the stock delta-v readout for the whole vessel (vacuum).
3. Switch away so it is unloaded and compare the display's number with the stock number.
4. Pass: within 5 percent.
5. Fail: write down both numbers.

## C. Maneuver Planner errors

1. Fly any vessel with the Maneuver Planner open on a node.
2. Let a different vessel burn in the background (use the display's burn button), and warp for a minute.
3. Pass: no red error counter at the bottom of the screen and no new `NullReferenceException` in `KSP.log` mentioning `TransferTypeSimple`.
4. Fail: send me KSP.log.

## D. Snapshot cost

1. Leave the display open for 2 minutes.
2. Search `KSP.log` for `snapshot ms p95`.
3. Pass: most lines are near 1 ms or below. Send me a few of the lines either way.
