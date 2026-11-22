# Decision: an optional MechJeb docking adapter

Status: decided for P14, to revisit after the operator's docking runs.

## Question
FLTCOM docks with its own controller (`src/FLTCOM/Guidance/DockingController.cs`). Should it also offer MechJeb's
docking autopilot behind `IGuidance` when MechJeb is installed?

## What FLTCOM ships
- The approach is a port of MechJeb2's docking autopilot (MechJebModuleDockingAutopilot.cs, GPL-3.0, notice kept in
  `src/FLTCOM.Core/Planning/DockingApproach.cs`): clear the target's side when behind the port, back out to 10 m,
  line up on the port axis, then close inside a 5° cone at speeds that fall with distance and never exceed what the
  RCS can stop.
- It is pure and unit-tested in `tests/FLTCOM.Core.Tests/DockingPortsTests.cs`. A point-mass simulation at 0.15 m/s²
  of RCS docks from 50 m off-axis in about 2 minutes on 4 m/s of RCS delta-v, and goes around a far-side port without
  coming within 14 m of the target's centre.
- The plugin side (control from the port, SAS on the port axis, RCS translation) is not yet flown in KSP.

## Success rate
Pending: the operator's ten docks in a row (`docs/test/p14-operator-steps.md`, Test G). Fill in here:

| Run | Docked | 50 m to docked | Monopropellant used |
|---|---|---|---|
| 1 to 10 | _/10 | median _ min | median _ units |

The bar: at least 9 of 10 docked, a median under 8 minutes and a median under 30 units of monopropellant.

## Decision
No MechJeb adapter now.
- FLTCOM's controller already uses MechJeb's approach, so an adapter would add a second way to fly the same corridor,
  not a better one.
- An adapter means a soft dependency on MechJeb's internals (module lookup by reflection, its own target and
  "control from here" state). Those change between MechJeb releases, and a CKAN mod should not break when another mod
  updates.
- MechJeb's input hooks would fight FLTCOM's. `ControlRelease.OtherPilot` already releases an operation when MechJeb
  or kOS takes the vessel, and an adapter would have to undo that.

Revisit if the operator's runs miss the bar. In that case, add `MechJebDocking : IGuidance` behind a runtime check for
the MechJeb assembly. It is chosen per operation and never required, and the built-in controller stays the default.
