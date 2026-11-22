# Fixture saves

Each folder holds one `persistent.sfs` for KSP 1.12.5. The test harness made every save in stock KSP with only FLTCOM and the harness loaded. No save needs a human to make it.

## How a fixture is made

`tools/ksp-driver.ps1 make-fixture <name>` reads the recipe of that name from `recipes.cfg` and does this inside the running game:

1. It creates a new stock sandbox game named `fltcom-<name>` with the default difficulty and the default flag, and opens the Space Center.
2. For each `VESSEL` node, it loads a stock craft from the KSP folder (`craft = Ships/VAB/...`) or makes a single stock part (`part = ...`). It puts the vessel on the recipe's orbit the way the stock set-orbit cheat does, and seats `crew` kerbals in the first crewed command part.
3. For a `LAUNCH` node, it puts the craft on the launch pad with the stock launch path and its default crew instead.
4. It opens the flight scene on the `focus` vessel (or the launched craft), waits for physics to settle, and saves with `GamePersistence.SaveGame`.
5. The driver copies `saves/fltcom-<name>/persistent.sfs` back into this folder.

To remake a fixture, turn test mode on, deploy, and boot to the main menu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 on
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 deploy
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 boot -Menu
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 make-fixture lko-single
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-driver.ps1 quit
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ksp-testmode.ps1 off
```

A new sandbox game also spawns stock asteroids and comets, so `state` lists them next to the fixture vessels.

## The fixtures

Altitudes are above the surface.

| Fixture | Opens on | Vessels |
| --- | --- | --- |
| `lko-single` | FLT Orbiter | FLT Orbiter (Orbiter 1A, 1 crew), Kerbin 100 km circular, equatorial. |
| `lko-elliptic` | FLT Elliptic | FLT Elliptic (Orbiter 1A, 1 crew), Kerbin 90 km by 1,200 km, 3° inclination. |
| `fleet-mixed` | FLT Orbiter | FLT Orbiter (Orbiter 1A) Kerbin 110 km. FLT Station (Space Station Core) Kerbin 250 km. FLT Survey Relay (Viewmatic Survey Satellite) Kerbin 600 km polar. FLT Mun Probe (Ion-Powered Space Probe) Mun 60 km. FLT Lost Probe (OKTO2 alone) Minmus 50 km, no signal. |
| `fleet-demo` | FLT Pad Kerbal X | The play-test fleet, 15 vessels, each with a probe core or a pilot. Kerbin: FLT Orbiter 100 km, FLT Station 250 km, FLT Survey Relay 600 km polar, FLT Keosync 2,863 km, FLT Eccentric 90 km by 3,000 km, FLT Low Fuel 180 km with 8% propellant, multi-stage FLT Kerbal X 120 km, FLT Kerbal 2 140 km and FLT Z-MAP 300 km. Mun: FLT Mun Lander, FLT Mun Station, FLT Mun Probe. Minmus: FLT Minmus Probe, FLT Lost Probe (OKTO2 alone, no signal). FLT Pad Kerbal X on the launch pad. |
| `comms-lost` | FLT Lost Probe | FLT Lost Probe (OKTO2 alone) Minmus 50 km. Its internal antenna cannot reach Kerbin, so it has no signal. FLT Orbiter (Orbiter 1A) Kerbin 100 km. |
| `mun-station` | FLT Mun Station | FLT Mun Station (Space Station Core, 1 crew) Mun 100 km. FLT Mun Probe (Ion-Powered Space Probe) Mun 150 km. |
| `relay-ring` | FLT Orbiter | FLT Orbiter (Orbiter 1A, 1 crew) Kerbin 100 km. FLT Relay 1 to 4 (Viewmatic Survey Satellite, ion engine) in one loose Kerbin orbit, periapsis 690 to 708 km, apoapsis 735 to 760 km, bunched on one side of Kerbin and far from FLT Orbiter. |
| `pad-kerbalx` | FLT Kerbal X | Kerbal X on the launch pad, stock crew. |
| `pad-twostage` | FLT Two Stage | Kerbal 1 on the launch pad, stock crew. Boosters, a core stage, and an upper stage, a different layout from Kerbal X. |
| `lko-rendezvous` | FLT Chaser | FLT Chaser (Orbiter 1A, 1 crew) Kerbin 100 km circular, equatorial. Three Space Station Core targets, 1 crew each: FLT Target Near 30 km ahead in the same orbit, FLT Target Far half an orbit ahead in the same orbit, FLT Target Tilted 120 km at 5° inclination. |
| `lko-docking` | FLT Dock Chaser | Two Space Station Core craft, 1 crew each, Kerbin 100 km circular, equatorial: FLT Dock Chaser and FLT Dock Station about 2 km ahead in the same orbit. FLT Dock Probe (Orbiter 1A, 1 crew) about 3 km ahead, for a vessel the chaser cannot dock to if Orbiter 1A carries no port. |
