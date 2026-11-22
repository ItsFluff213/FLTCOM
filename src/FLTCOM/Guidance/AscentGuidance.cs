using System;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Planning;
using FLTCOM.Execution;
using UnityEngine;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Flies the ascent from the pad to the edge of the atmosphere: <see cref="AscentGuide"/> picks throttle and
    /// direction each physics frame, stock SAS holds the direction, and <see cref="AutoStager"/> stages. The burns that
    /// finish the orbit are flown afterwards by the node executor.
    /// </summary>
    public sealed class AscentGuidance : IGuidance
    {
        private const double TimeLimit = 900;

        private readonly Vessel _vessel;
        private readonly AscentGuide _guide;
        private readonly AutoStager _stager;
        private readonly SystemsPilot? _systems;
        private readonly double _deadline;
        private float _throttle;

        public AscentGuidance(Vessel vessel, LaunchTarget target, bool autoSystems)
        {
            _vessel = vessel;
            double now = Planetarium.GetUniversalTime();
            Core.Planning.LaunchSite site = InertialFrame.Site(vessel, now);
            LaunchPlane plane = LaunchWindow.Find(site, target.Inclination, null, now);
            if (target.Lan is double lan)
            {
                plane = new LaunchPlane(now, plane.Inclination, lan, plane.North, null);
            }
            _guide = new AscentGuide(VesselTasks.Body(vessel.mainBody), target, plane, site.RotationRate, vessel.altitude);
            _stager = new AutoStager(vessel);
            _systems = autoSystems ? new SystemsPilot(vessel, AutoPhase.Ascent, fairings: true) : null;
            _deadline = now + TimeLimit;
            CoreVector east = Enu.At(InertialFrame.Position(vessel)).ToLocal(InertialFrame.Velocity(vessel));
            PluginFiles.Log("ascent " + vessel.vesselName + ": inc " + (plane.Inclination * UtilMath.Rad2Deg).ToString("0.00") + " LAN " + (plane.Lan * UtilMath.Rad2Deg).ToString("0.00") + "; pad velocity east " + east.X.ToString("0.0") + " m/s (positive for a prograde spin)");
            _vessel.OnFlyByWire += Drive;
        }

        public Vessel Vessel => _vessel;

        public double DeltaVDone { get; private set; }

        /// <summary>Coasting up through the air with the engines off: the guide only steers prograde until space.</summary>
        public double IdleUntil => _guide.Phase == AscentPhase.Coast && _throttle == 0 ? double.PositiveInfinity : double.NegativeInfinity;

        public string Note { get; private set; } = "";

        public GuidanceStatus Tick()
        {
            double now = Planetarium.GetUniversalTime();
            if (now > _deadline)
            {
                return Finish(GuidanceStatus.Failed, "the ascent took longer than " + TimeLimit.ToString("0") + " s");
            }
            _systems?.Tick();
            var state = new Core.TwoBody.StateVector(InertialFrame.Position(_vessel), InertialFrame.Velocity(_vessel));
            AscentCommand command = _guide.Step(new AscentInput(now, _vessel.altitude, _vessel.dynamicPressurekPa * 1000, state));
            if (command.Phase == AscentPhase.Done)
            {
                return Finish(GuidanceStatus.Done, "ascent done above " + (_vessel.altitude / 1000).ToString("0.0") + " km, apoapsis " + (_vessel.orbit.ApA / 1000).ToString("0.0") + " km after " + _stager.Stagings + " stagings");
            }
            _throttle = (float)command.Throttle;
            Steering.Hold(_vessel, InertialFrame.ToWorld(Enu.At(state.Position).FromLocal(command.Direction)));
            if (_throttle > 0 && _stager.Tick())
            {
                return Finish(GuidanceStatus.Failed, "no thrust left and nothing to stage, apoapsis " + (_vessel.orbit.ApA / 1000).ToString("0.0") + " km");
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

        private void Drive(FlightCtrlState state)
        {
            state.mainThrottle = _throttle;
        }
    }
}
