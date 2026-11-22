using System;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Missions;
using UnityEngine;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Flies one planned burn on the active vessel through a stock maneuver node: SAS points at the node, the throttle
    /// falls off over the last second and a half of thrust, and the burn stops under 0.1 m/s left. A flamed-out stage
    /// is dropped and the burn goes on with the next one. With auto systems on, the auto-stager drops spent boosters and
    /// empty tanks during the burn, and a launch jettisons its fairings before burning.
    /// </summary>
    public sealed class NodeExecutor : IGuidance
    {
        public const double Tolerance = 0.1;
        private const double FinalPhase = 1.5;
        private const double IgniteAngle = 5;
        private const double CutAngle = 15;
        private const float StageCooldown = 1f;
        private const int MaxStagings = 3;

        private readonly Vessel _vessel;
        private readonly PlannedBurn _burn;
        private readonly AutoStager? _stager;
        private readonly SystemsPilot? _systems;
        private ManeuverNode? _node;
        private Vector3d _initialDirection;
        private double _initialDeltaV;
        private float _throttle;
        private bool _burning;
        private bool _holdingAttitude;
        private float _nextStaging;
        private int _stagings;
        private double _deadline;

        /// <param name="launch">The burn finishes a launch, so fairings go before it.</param>
        public NodeExecutor(Vessel vessel, PlannedBurn burn, bool autoSystems, bool launch)
        {
            _vessel = vessel;
            _burn = burn;
            _stager = autoSystems ? new AutoStager(vessel) : null;
            _systems = autoSystems && launch ? new SystemsPilot(vessel, AutoPhase.Burn, fairings: true) : null;
            _vessel.OnFlyByWire += Drive;
        }

        public Vessel Vessel => _vessel;

        public PlannedBurn Burn => _burn;

        public double DeltaVDone { get; private set; }

        /// <summary>The throttle has opened on this burn; until then the vessel is still turning toward it.</summary>
        public bool Ignited { get; private set; }

        public double IdleUntil => double.NegativeInfinity;

        public string Note { get; private set; } = "";

        public double Remaining => _node == null ? _burn.DeltaV : _node.GetBurnVector(_vessel.orbit).magnitude;

        /// <summary>Places the node if needed and points the vessel at it with the throttle closed.</summary>
        public void Point()
        {
            EnsureNode();
            _systems?.Tick();
            _throttle = 0;
            HoldAttitude();
        }

        public GuidanceStatus Tick()
        {
            EnsureNode();
            _systems?.Tick();
            Vector3d burnVector = _node!.GetBurnVector(_vessel.orbit);
            if (!_burning)
            {
                _burning = true;
                _initialDirection = burnVector.normalized;
                _initialDeltaV = burnVector.magnitude;
                _deadline = Planetarium.GetUniversalTime() + 2 * _burn.Duration + 60;
            }
            HoldAttitude();

            double remaining = burnVector.magnitude;
            DeltaVDone = Math.Max(0, _initialDeltaV - remaining);
            if (remaining < Tolerance || Vector3d.Dot(burnVector, _initialDirection) < 0)
            {
                return Finish(GuidanceStatus.Done, "burn complete, " + remaining.ToString("0.00") + " m/s left");
            }
            if (Planetarium.GetUniversalTime() > _deadline)
            {
                return Finish(GuidanceStatus.Failed, "burn overran its time with " + remaining.ToString("0.0") + " m/s left");
            }

            double acceleration = MaxAcceleration();
            if (acceleration <= 0)
            {
                _throttle = 1;
                if (_stager == null)
                {
                    return StageOrFail(remaining);
                }
                return _stager.Tick() ? Finish(GuidanceStatus.Failed, "no thrust left with " + remaining.ToString("0.0") + " m/s to go and nothing safe to stage") : GuidanceStatus.Running;
            }

            if (!_holdingAttitude && remaining < acceleration * FinalPhase)
            {
                _holdingAttitude = true;
                HoldAttitude();
            }

            Vector3d forward = _vessel.ReferenceTransform.up;
            double angle = Vector3d.Angle(forward, burnVector);
            if (_holdingAttitude && Vector3d.Dot(burnVector, forward) < Tolerance)
            {
                return Finish(GuidanceStatus.Done, "burn complete, " + remaining.ToString("0.00") + " m/s left");
            }
            double pointing = angle < IgniteAngle ? 1 : angle < CutAngle && DeltaVDone > 0 ? 0.3 : 0;
            if (_holdingAttitude)
            {
                pointing = 1;
            }
            _throttle = (float)(pointing * Math.Max(0.02, Math.Min(1, remaining / (acceleration * FinalPhase))));
            if (_throttle > 0)
            {
                _stager?.Tick();
            }
            return GuidanceStatus.Running;
        }

        public void Stop()
        {
            _throttle = 0;
            _vessel.OnFlyByWire -= Drive;
            FlightInputHandler.state.mainThrottle = 0;
            _vessel.ctrlState.mainThrottle = 0;
            _node?.RemoveSelf();
            _node = null;
            if (_vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist)
            {
                _vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }

        private GuidanceStatus Finish(GuidanceStatus status, string note)
        {
            Note = note;
            Stop();
            return status;
        }

        private GuidanceStatus StageOrFail(double remaining)
        {
            if (Time.time < _nextStaging)
            {
                return GuidanceStatus.Running;
            }
            if (_stagings >= MaxStagings || KSP.UI.Screens.StageManager.CurrentStage <= 0)
            {
                return Finish(GuidanceStatus.Failed, "no thrust left with " + remaining.ToString("0.0") + " m/s to go");
            }
            _stagings++;
            _nextStaging = Time.time + StageCooldown;
            KSP.UI.Screens.StageManager.ActivateNextStage();
            return GuidanceStatus.Running;
        }

        private double MaxAcceleration()
        {
            double thrust = _vessel.parts
                .SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                .Where(e => e.EngineIgnited && e.isOperational && !e.flameout)
                .Sum(e => (double)e.maxThrust * e.thrustPercentage / 100);
            double mass = _vessel.totalMass;
            return mass > 0 ? thrust / mass : 0;
        }

        /// <summary>
        /// Every frame, so a stray SAS key (T, F) or a click on another SAS mode cannot turn the vessel off its burn:
        /// SAS stays on, pointing at the node, or holding still for the last second and a half.
        /// </summary>
        private void HoldAttitude()
        {
            if (!_vessel.ActionGroups[KSPActionGroup.SAS])
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                _vessel.Autopilot.Enable();
            }
            VesselAutopilot.AutopilotMode mode = _holdingAttitude ? VesselAutopilot.AutopilotMode.StabilityAssist : VesselAutopilot.AutopilotMode.Maneuver;
            if (_vessel.Autopilot.Mode != mode && _vessel.Autopilot.CanSetMode(mode))
            {
                _vessel.Autopilot.SetMode(mode);
            }
        }

        private void EnsureNode()
        {
            if (_node != null && _vessel.patchedConicSolver.maneuverNodes.Contains(_node))
            {
                return;
            }
            PatchedConicSolver solver = _vessel.patchedConicSolver;
            _node = solver.AddManeuverNode(_burn.NodeUt);
            _node.OnGizmoUpdated(new Vector3d(_burn.Radial, _burn.Normal, _burn.Prograde), _burn.NodeUt);
        }

        private void Drive(FlightCtrlState state)
        {
            state.mainThrottle = _throttle;
            Ignited |= _throttle > 0;
        }
    }
}
