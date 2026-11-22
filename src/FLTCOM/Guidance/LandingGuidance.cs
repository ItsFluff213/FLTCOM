using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Execution;
using UnityEngine;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Flies the descent after the deorbit burn: <see cref="LandingGuide"/> picks throttle and direction each physics
    /// frame (corrections, braking, touchdown), stock SAS holds the direction as <see cref="AttitudeSlew"/> walks toward it, parachutes are armed when the guide says
    /// it is safe, and <see cref="AutoStager"/> stages a spent engine. On touchdown it cuts the throttle and holds upright.
    /// </summary>
    public sealed class LandingGuidance : IGuidance
    {
        private const double Overtime = 900;

        private readonly Vessel _vessel;
        private readonly LandingGuide _guide;
        private readonly AutoStager _stager;
        private readonly SystemsPilot? _systems;
        private readonly double _deadline;
        private float _throttle;
        private bool _chutesArmed;
        private bool _hasChutes;
        private CoreVector? _attitude;

        private LandingGuidance(Vessel vessel, LandingContext context, LandingTarget target, double deadline, bool autoSystems)
        {
            _vessel = vessel;
            _guide = new LandingGuide(context.Body, target, context.TargetAltitude, context.DragArea, context.ChuteArea, context.ChuteDeployAltitude, LandingBriefing.CorrectionReserve);
            _stager = new AutoStager(vessel);
            _systems = autoSystems ? new SystemsPilot(vessel, AutoPhase.Descent, fairings: false) : null;
            _deadline = deadline;
            PluginFiles.Log("descent " + vessel.vesselName + ": " + target.Describe());
            _vessel.OnFlyByWire += Drive;
        }

        /// <summary>The descent guidance, or null when the vessel is not loaded.</summary>
        public static LandingGuidance? Create(Vessel vessel, LandingTarget target, PlannedBurn burn, bool autoSystems)
        {
            LandingContext? context = LandingReader.Context(vessel, target);
            return context == null ? null : new LandingGuidance(vessel, context, target, burn.StartUt + burn.Duration + Overtime, autoSystems);
        }

        public Vessel Vessel => _vessel;

        public double DeltaVDone { get; private set; }

        /// <summary>
        /// Coasting with the engines off: until the braking burn starts or the air is reached, and for a vessel on parachutes,
        /// until it slows toward the speed where they are armed.
        /// </summary>
        public double IdleUntil
        {
            get
            {
                if (_throttle > 0 || _guide.Phase != LandingPhase.Coast)
                {
                    return double.NegativeInfinity;
                }
                double now = Planetarium.GetUniversalTime();
                if (_guide.Prediction is { FreefallEndUt: double coastEnd } && coastEnd > now)
                {
                    return coastEnd;
                }
                bool slowing = _vessel.srfSpeed < 1.2 * LandingGuide.ChuteArmSpeed && _vessel.altitude < 0.6 * _vessel.mainBody.atmosphereDepth;
                return _hasChutes && !_chutesArmed && _vessel.mainBody.atmosphere && !slowing ? double.PositiveInfinity : double.NegativeInfinity;
            }
        }

        public string Note { get; private set; } = "";

        /// <summary>The latest predicted touchdown.</summary>
        public LandingPrediction? Prediction => _guide.Prediction;

        public DescentReading? Reading { get; private set; }

        public GuidanceStatus Tick()
        {
            double now = Planetarium.GetUniversalTime();
            if (now > _deadline)
            {
                return Finish(GuidanceStatus.Failed, "the descent took too long; phase " + _guide.Phase);
            }
            List<ModuleParachute> chutes = _vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleParachute>()).ToList();
            _hasChutes = chutes.Count > 0;
            bool open = chutes.Any(c => c.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED || c.deploymentState == ModuleParachute.deploymentStates.DEPLOYED);
            double ground = _vessel.mainBody.ocean ? Mathf.Max(0, (float)_vessel.terrainAltitude) : _vessel.terrainAltitude;
            var input = new LandingInput(
                now,
                InertialFrame.Position(_vessel),
                InertialFrame.Velocity(_vessel),
                _vessel.totalMass * 1000,
                MaxThrust(),
                InertialFrame.ToCore(_vessel.ReferenceTransform.up),
                ground,
                _vessel.LandedOrSplashed,
                open);
            LandingCommand command = _guide.Step(input);
            Reading = new DescentReading(_guide.Phase, _vessel.altitude - ground, _vessel.verticalSpeed, _guide.Miss);
            _systems?.Tick();
            if (command.ArmChutes && !_chutesArmed)
            {
                _chutesArmed = true;
                foreach (ModuleParachute chute in chutes.Where(c => c.deploymentState == ModuleParachute.deploymentStates.STOWED))
                {
                    chute.Deploy();
                }
                PluginFiles.Log("descent " + _vessel.vesselName + ": parachutes armed at " + _vessel.altitude.ToString("0") + " m, " + _vessel.srfSpeed.ToString("0") + " m/s");
            }
            if (command.Status == LandingStatus.Done)
            {
                _throttle = 0;
                CoreVector up = InertialFrame.Position(_vessel).Normalized;
                Steering.Hold(_vessel, InertialFrame.ToWorld(up));
                return Finish(GuidanceStatus.Done, "landed " + (_guide.Miss < double.PositiveInfinity ? _guide.Miss.ToString("0") + " m from the target" : "") + " at " + _vessel.srfSpeed.ToString("0.0") + " m/s; SAS holds upright");
            }
            if (command.Status == LandingStatus.Failed)
            {
                return Finish(GuidanceStatus.Failed, _guide.Note ?? "the descent failed");
            }
            _throttle = (float)command.Throttle;
            if (command.Direction.Magnitude > 0)
            {
                _attitude = AttitudeSlew.Toward(_attitude ?? input.Forward, command.Direction, TimeWarp.fixedDeltaTime);
                Steering.Hold(_vessel, InertialFrame.ToWorld(_attitude.Value));
            }
            if (_throttle > 0 && _stager.Tick(_chutesArmed) && !open)
            {
                return Finish(GuidanceStatus.Failed, "no thrust left and nothing to stage, " + (_vessel.altitude / 1000).ToString("0.0") + " km up");
            }
            double mass = _vessel.totalMass;
            if (mass > 0)
            {
                double thrust = _vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Sum(e => (double)e.finalThrust);
                DeltaVDone += thrust / mass * TimeWarp.fixedDeltaTime;
            }
            return GuidanceStatus.Running;
        }

        public void Stop()
        {
            _throttle = 0;
            _vessel.OnFlyByWire -= Drive;
            FlightInputHandler.state.mainThrottle = 0;
            _vessel.ctrlState.mainThrottle = 0;
        }

        private GuidanceStatus Finish(GuidanceStatus status, string note)
        {
            Note = note;
            Stop();
            return status;
        }

        /// <summary>Thrust of the engines that can fire now, in newtons, with their thrust limiters.</summary>
        private double MaxThrust() =>
            _vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                .Where(e => e.EngineIgnited && !e.flameout && e.isOperational)
                .Sum(e => (double)e.maxThrust * e.thrustPercentage / 100) * 1000;

        private void Drive(FlightCtrlState state)
        {
            state.mainThrottle = _throttle;
        }
    }
}
