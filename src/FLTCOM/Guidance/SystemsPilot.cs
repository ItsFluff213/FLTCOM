using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Execution;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Works landing gear, fairings, solar panels, antennas and radiators during one guided flight, as the rules in
    /// <see cref="GearRule"/>, <see cref="FairingRule"/> and <see cref="DeployableRule"/> decide. Logs every action.
    /// </summary>
    internal sealed class SystemsPilot
    {
        private readonly Vessel _vessel;
        private readonly AutoPhase _phase;
        private readonly bool _fairings;
        private readonly bool _hasGear;
        private GearState _gear = GearState.Untouched;
        private bool _fairingsGone;

        /// <param name="fairings">Jettison fairings on this flight: true for the ascent and burns of a launch.</param>
        public SystemsPilot(Vessel vessel, AutoPhase phase, bool fairings)
        {
            _vessel = vessel;
            _phase = phase;
            _fairings = fairings;
            _hasGear = Gear(vessel).Any();
            Fold(vessel, phase);
        }

        public void Tick()
        {
            if (_hasGear)
            {
                double height = _vessel.heightFromTerrain >= 0 ? _vessel.heightFromTerrain : _vessel.altitude - _vessel.terrainAltitude;
                switch (GearRule.Decide(_phase, height, _vessel.verticalSpeed, _vessel.LandedOrSplashed, _gear))
                {
                    case GearCommand.Deploy:
                        SetGear(true);
                        _gear = GearState.Deployed;
                        Log("landing gear down at " + height.ToString("0") + " m, " + _vessel.verticalSpeed.ToString("0") + " m/s");
                        break;
                    case GearCommand.Retract:
                        SetGear(false);
                        _gear = GearState.Retracted;
                        Log("landing gear up at " + height.ToString("0") + " m");
                        break;
                    case GearCommand.Brake:
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                        _gear = GearState.Braked;
                        Log("brakes on at touchdown");
                        break;
                }
            }
            if (_fairings && !_fairingsGone && FairingRule.ShouldJettison(_vessel.mainBody.atmosphere ? _vessel.mainBody.atmosphereDepth : 0, _vessel.altitude))
            {
                _fairingsGone = true;
                List<ModuleProceduralFairing> fairings = _vessel.parts
                    .SelectMany(p => p.FindModulesImplementing<ModuleProceduralFairing>())
                    .Where(f => FairingRule.Closed(f.CanMove, f.xSections.Count))
                    .ToList();
                foreach (ModuleProceduralFairing fairing in fairings)
                {
                    fairing.DeployFairing();
                }
                if (fairings.Count > 0)
                {
                    Log("jettisoned " + fairings.Count + " fairing" + (fairings.Count == 1 ? "" : "s") + " at " + (_vessel.altitude / 1000).ToString("0.0") + " km");
                }
            }
        }

        /// <summary>Unfolds the panels, antennas and radiators once a launch has left the vessel in a stable orbit.</summary>
        public static void InOrbit(Vessel vessel)
        {
            if (!DeployableRule.StableOrbit(vessel.orbit.PeA, VesselTasks.Body(vessel.mainBody).SafeFloor))
            {
                return;
            }
            Fold(vessel, AutoPhase.Orbit);
        }

        /// <summary>The parts the briefing counts: gear, fairings, and panels that can or cannot fold back.</summary>
        public static VesselKit Kit(IEnumerable<Part> prefabs)
        {
            var parts = prefabs.ToList();
            var deployables = parts.SelectMany(p => p.FindModulesImplementing<ModuleDeployablePart>()).ToList();
            return new VesselKit(
                parts.Count(p => p.FindModuleImplementing<ModuleWheels.ModuleWheelDeployment>() != null),
                parts.Count(p => p.FindModuleImplementing<ModuleProceduralFairing>() != null),
                deployables.Count(d => d.retractable && d.isBreakable),
                deployables.Count(d => !d.retractable));
        }

        private static void Fold(Vessel vessel, AutoPhase phase)
        {
            int retracted = 0;
            int extended = 0;
            foreach (ModuleDeployablePart part in vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleDeployablePart>()))
            {
                switch (DeployableRule.Decide(phase, part.retractable, part.isBreakable))
                {
                    case DeployCommand.Retract when part.deployState == ModuleDeployablePart.DeployState.EXTENDED:
                        part.Retract();
                        retracted++;
                        break;
                    case DeployCommand.Extend when part.deployState == ModuleDeployablePart.DeployState.RETRACTED:
                        part.Extend();
                        extended++;
                        break;
                }
            }
            if (retracted > 0)
            {
                PluginFiles.Log("auto systems " + vessel.vesselName + ": retracted " + retracted + " panels, antennas and radiators for the " + (phase == AutoPhase.Ascent ? "ascent" : "descent"));
            }
            if (extended > 0)
            {
                PluginFiles.Log("auto systems " + vessel.vesselName + ": extended " + extended + " panels, antennas and radiators in a stable orbit");
            }
        }

        private void SetGear(bool down)
        {
            _vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, down);
            foreach (ModuleWheels.ModuleWheelDeployment wheel in Gear(_vessel))
            {
                wheel.ActionToggle(down ? KSPActionType.Activate : KSPActionType.Deactivate);
            }
        }

        private static IEnumerable<ModuleWheels.ModuleWheelDeployment> Gear(Vessel vessel) =>
            vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleWheels.ModuleWheelDeployment>());

        private void Log(string what) => PluginFiles.Log("auto systems " + _vessel.vesselName + ": " + what);
    }
}
