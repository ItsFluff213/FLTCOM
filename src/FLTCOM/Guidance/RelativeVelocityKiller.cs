using System;
using FLTCOM.Core.Planning;
using UnityEngine;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Closed-loop end of a rendezvous: nulls the chaser's velocity relative to the target and drifts it in until it holds
    /// within 50 m at under 0.2 m/s. The main engine takes errors above 1 m/s, RCS translation the rest; without RCS
    /// the main engine finishes at low throttle. The target is left passive under its own SAS.
    /// </summary>
    public sealed class RelativeVelocityKiller : IGuidance
    {
        private const double EngineError = 1;
        private const double PointAngle = 5;
        private const double RcsGain = 2;
        private const double ApproachGain = 1.0 / 60;
        private const double MaxApproachSpeed = 2;
        private const float SettleSeconds = 3f;
        private const float GiveUpSeconds = 900f;

        private readonly Vessel _vessel;
        private readonly string _targetId;
        private float _throttle;
        private Vector3 _translation;
        private float _settledSince = -1;
        private readonly float _deadline;
        private Vector3d? _lastRelative;

        public RelativeVelocityKiller(Vessel vessel, string targetId)
        {
            _vessel = vessel;
            _targetId = targetId;
            _deadline = Time.time + GiveUpSeconds;
            _vessel.OnFlyByWire += Drive;
        }

        public Vessel Vessel => _vessel;

        public double DeltaVDone { get; private set; }

        public double IdleUntil => double.NegativeInfinity;

        public string Note { get; private set; } = string.Empty;

        public GuidanceStatus Tick()
        {
            Vessel? target = Execution.VesselTasks.Find(_targetId);
            if (target == null)
            {
                return Finish(GuidanceStatus.Failed, "lost the target");
            }
            Passive(target);
            Vector3d offset = target.GetWorldPos3D() - _vessel.GetWorldPos3D();
            Vector3d relative = _vessel.obt_velocity - target.obt_velocity;
            if (_lastRelative is Vector3d last)
            {
                DeltaVDone += (relative - last).magnitude;
            }
            _lastRelative = relative;
            double distance = offset.magnitude;
            double speed = relative.magnitude;

            if (distance <= Rendezvous.HoldDistance && speed <= Rendezvous.HoldSpeed)
            {
                _throttle = 0;
                _translation = Vector3.zero;
                if (_settledSince < 0)
                {
                    _settledSince = Time.time;
                }
                return Time.time - _settledSince >= SettleSeconds
                    ? Finish(GuidanceStatus.Done, "holding " + distance.ToString("0") + " m from " + target.vesselName + " at " + speed.ToString("0.00") + " m/s")
                    : GuidanceStatus.Running;
            }
            _settledSince = -1;
            if (Time.time > _deadline)
            {
                return Finish(GuidanceStatus.Failed, "still " + distance.ToString("0") + " m out at " + speed.ToString("0.00") + " m/s after " + (GiveUpSeconds / 60).ToString("0") + " min");
            }

            Vector3d wantedVelocity = distance > Rendezvous.HoldDistance
                ? offset.normalized * Math.Min((distance - Rendezvous.AimDistance) * ApproachGain, MaxApproachSpeed)
                : Vector3d.zero;
            Vector3d change = wantedVelocity - relative;
            bool rcs = Steering.HasRcs(_vessel);
            double acceleration = Steering.MaxAcceleration(_vessel);
            if (!rcs && acceleration <= 0)
            {
                return Finish(GuidanceStatus.Failed, "no thrust or RCS left " + distance.ToString("0") + " m from the target");
            }

            if ((change.magnitude > EngineError || !rcs) && acceleration > 0)
            {
                Steering.Rcs(_vessel, false);
                _translation = Vector3.zero;
                Steering.Hold(_vessel, change);
                _throttle = Steering.OffAxis(_vessel, change) < PointAngle ? (float)Math.Min(change.magnitude / acceleration, 1) : 0;
            }
            else
            {
                _throttle = 0;
                Steering.Rcs(_vessel, true);
                _translation = Steering.Translation(_vessel, change, RcsGain);
            }
            return GuidanceStatus.Running;
        }

        public void Stop()
        {
            _throttle = 0;
            _translation = Vector3.zero;
            _vessel.OnFlyByWire -= Drive;
            FlightInputHandler.state.mainThrottle = 0;
            _vessel.ctrlState.mainThrottle = 0;
        }

        /// <summary>A loaded target that is not being flown holds still under its own SAS and never fires an engine.</summary>
        public static void Passive(Vessel target)
        {
            if (target.loaded && !target.packed && target != FlightGlobals.ActiveVessel && !target.ActionGroups[KSPActionGroup.SAS])
            {
                target.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }
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
            state.X = _translation.x;
            state.Y = _translation.y;
            state.Z = _translation.z;
        }
    }
}
