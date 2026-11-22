# Flashy controls operator steps: livelier buttons and burn feedback

Save used: `fltcom-fleet-mixed` (the play-test script installs it, nothing to build). Only the browser dashboard changed; the mod itself did not.

## Set up
1. Close KSP and CKAN. In a PowerShell window in the repo run `tools/play-test.ps1`.
2. Start `KSP_x64.exe` directly (not through Steam). Resume Game, pick `fltcom-fleet-mixed`, and wait until you are flying FLT Orbiter.
3. Open the address the script printed (default http://127.0.0.1:47800/) in a browser.

## Test A: buttons react
1. Move the mouse over BRIEF, the AUTO SYSTEMS switch, the centre buttons (SUN, KERBIN, MUN, ...) and the markers on a surface map.
   Pass: each one glows softly or grows a little under the mouse. Nothing jumps or changes size around it.
2. Click the AUTO SYSTEMS switch twice.
   Pass: the knob slides across and the ON/OFF word flips each time.
3. Select FLT Orbiter, compose RAISE APOAPSIS BY 100 km and click BRIEF.
   Pass: BRIEF flashes green and the briefing opens.

## Test B: hold to approve
1. In the briefing, click HOLD TO APPROVE once, quickly.
   Pass: nothing is approved. The button only starts to fill and empties again.
2. Press and hold HOLD TO APPROVE with the mouse for about one second.
   Pass: a green fill runs left to right in just over half a second, the button reads UPLINKING..., then APPROVED, and the briefing closes by itself.
3. Brief another change for FLT Station. Click HOLD TO APPROVE so it has focus, let go, then press and hold the Space key (or Enter).
   Pass: the same fill runs and it approves. Letting go of the key early cancels it.
4. In OPERATIONS, hold HOLD TO SCRUB on the FLT Station operation.
   Pass: it fills red, then the operation is scrubbed.

## Test C: burn feedback (top right of the map)
1. Watch the NEXT BURN corner for the FLT Orbiter operation. Warp if FLTCOM asks.
   Pass: a small chip says COASTING, then ALIGNING in the last minute. A yellow ring fills up as the burn gets close.
2. Watch the last 10 seconds.
   Pass: the ring and the time pulse yellow.
3. At T-0.
   Pass: the chip says BURNING, a framed IGNITION word blinks for about two seconds, and a glowing bar fills with "x / y m/s" under it.
4. If FLTCOM re-plans the burn or puts it on hold, the chip says RE-PLANNED or HELD (HELD also shows AWAITING RESUME).

## Test D: a refused command
1. While the FLT Orbiter operation is still waiting or burning, brief a second change for FLT Orbiter and hold HOLD TO APPROVE.
   Pass: the button reads UPLINKING..., then after about 4 seconds it shakes, turns red, and red text under it says KSP DID NOT TAKE THE APPROVAL · THE GAME SCREEN SAYS WHY. The briefing stays open. In KSP, the message on the game screen gives the reason.
2. Hold HOLD TO SCRUB in the briefing.
   Pass: the briefing closes.

## Test E: reduced motion
1. In Windows Settings, Accessibility, Visual effects, turn Animation effects off. Reload the dashboard.
   Pass: the glows and colours still show, but nothing shakes, pulses, slides or blinks. The hold fill still fills.
2. Turn Animation effects back on.

## Leave
1. Quit KSP. Run `tools/play-test.ps1 -Off`.
   Pass: the script reports that the normal setup is back.
