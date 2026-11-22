## Land at a chosen spot (P17)

**Depends on.** P10 (launch), on the `look-polish` trunk.

**Scope.** Rockets and capsules in orbit land at the KSC launch pad by default, at the KSC runway, or at any latitude and longitude on the body they orbit. Spaceplane runway landings (gliding approach and wheels down) are a later phase.

**Files.**

- [ ] Create `src/FLTCOM.Core/Landing/LandingBody.cs` (the rotating body, its atmosphere and the target sites), `ReentryPredictor.cs` (port of MechJeb `ReentrySimulation`), `DescentPolicy.cs` (ports of the MechJeb descent speed policies), `LandingGuide.cs` (port of the MechJeb landing autopilot steps) and `LandingPlanner.cs` (the deorbit burn timed for the target).
- [ ] Create `src/FLTCOM.Core/Planning/LandingBriefing.cs` and add `OrbitChangeKind.Land` with a `LandingTarget` to `OrbitChange`.
- [ ] Create `src/FLTCOM/Guidance/LandingGuidance.cs` and `src/FLTCOM/Execution/LandingReader.cs`; edit `Runner.cs` and `MissionDesk.cs` to fly and brief a landing.
- [ ] Edit `protocol/fltcom.schema.json`: `ChangeKind` gains `land`; `OrbitChangeRequest.landing` and `OperationStatus.landingPrediction` are new nullable fields. Regenerate with `tools/gen-protocol`.
- [ ] Edit `display/src/briefing/Composer.svelte` to add the Land operation with a target picker (Launch pad, Runway, Custom lat/lon), and the map to draw the predicted landing point.
- [ ] Edit `tools/mock-telemetry/missions.mjs` so the mock server briefs a landing.

**Build.**

- [ ] Plan the deorbit burn: scan the coming orbits for the burn time whose predicted landing falls on the target, then fix the cross-range with a normal component.
- [ ] Predict the landing by integrating the descent with gravity, stock drag (drag area times dynamic pressure), parachute drag once open, and the powered braking the guide will fly.
- [ ] Correct the course after the deorbit burn while the vessel is still above the air, with the correction found from the predictor's own sensitivities.
- [ ] Arm the parachutes once the surface speed and the height are safe for them.
- [ ] Brake with a suicide burn that keeps a throttle margin on bodies without air and for rockets without parachutes; kill the horizontal speed, touch down under 2 m/s and hold the vessel upright with SAS.
- [ ] Refuse the landing in the briefing (red contingency) when delta-v is short, and abort to orbit before the deorbit burn when it has become short since.

**You see.**

- [ ] A capsule in low Kerbin orbit deorbits, comes down on its parachutes and lands near the launch pad; a Mun lander lands on the Mun at the chosen point.

**Verify, unit.**

- [ ] `tests/FLTCOM.Core.Tests/LandingTests.cs`: the pure-core powered descent on an airless Mun-like body lands under 2 m/s within 200 m of the target; the re-entry predictor on Kerbin matches a fine fixed-step reference trajectory within 2 km; the briefing turns red when delta-v is short and refuses a target on another body. Run `dotnet test tests/FLTCOM.Core.Tests`.
- [ ] `display/src/briefing/Composer.test.ts`: the Land option sends the chosen site, and a custom site sends latitude and longitude in radians.

**Verify, live.** The operator flies these by hand from `docs/test/p17-operator-steps.md`; agents never launch KSP.

- [ ] Lane 1. Land a capsule from low Kerbin orbit at the launch pad. Pass when it lands within 5 km of the pad, upright.
- [ ] Lane 2. Land the Mun lander at a custom point. Pass when it touches down under 3 m/s within 500 m.
- [ ] Lane 3. Brief a landing for a vessel without enough delta-v. Pass when the briefing shows a red contingency and cannot be approved.
- [ ] Lane 4. Watch the map during the descent. Pass when the predicted landing point is drawn and moves onto the target after the correction.
