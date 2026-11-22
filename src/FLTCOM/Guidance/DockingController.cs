using System;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Execution;
using UnityEngine;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// Docks a chaser holding near its target: controls from the chaser's port, turns it to face the target port, and
    /// flies the <see cref="DockingApproach"/> corridor on RCS translation alone. Without RCS it stops where the
    /// rendezvous left it and hands docking to the pilot.
    /// </summary>
    public sealed class DockingController : IGuidance
    {
        public const string NoRcs = "CONTINGENCY, NO RCS, MANUAL DOCKING";
        private const double RcsGain = 4;
        private const double DriftLimit = 3 * DockingApproach.StartDistance;
        private const float GiveUpSeconds = 1200f;

        private readonly Vessel _vessel;
        private readonly RendezvousTarget _target;
        private readonly float _deadline;
        private Part? _previousReference;
        private ModuleDockingNode? _chaserPort;
        private ModuleDockingNode? _targetPort;
        private Vector3 _translation;
        private Vector3d? _lastRelative;

        public DockingController(Vessel vessel, RendezvousTarget target)
        {
            _vessel = vessel;
            _target = target;
            _deadline = Time.time + GiveUpSeconds;
            _vessel.OnFlyByWire += Drive;
        }

        public Vessel Vessel => _vessel;

        public double DeltaVDone { get; private set; }

        public double IdleUntil => double.NegativeInfinity;

        public string Note { get; private set; } = string.Empty;

        /// <summary>The ports have latched; the chaser may already be part of the target vessel.</summary>
        public bool HasDocked => _chaserPort != null && Docked(_chaserPort);

        public GuidanceStatus Tick()
        {
            if (HasDocked)
            {
                return Finish(GuidanceStatus.Done, "docked " + Title(_chaserPort!) + " to " + (_targetPort != null ? Title(_targetPort) : "the port") + " on " + _target.Name);
            }
            Vessel? target = VesselTasks.Find(_target.VesselId);
            if (target == null || !target.loaded)
            {
                return Finish(GuidanceStatus.Failed, target == null ? "lost the target" : _target.Name + " is out of physics range");
            }
            RelativeVelocityKiller.Passive(target);
            if (!Steering.HasRcs(_vessel))
            {
                ScreenMessages.PostScreenMessage(">> " + NoRcs + " <<", 10f, ScreenMessageStyle.UPPER_CENTER);
                return Finish(GuidanceStatus.Failed, NoRcs);
            }
            if (_chaserPort == null || _targetPort == null)
            {
                string? refusal = TakePorts(target);
                if (refusal != null)
                {
                    return Finish(GuidanceStatus.Failed, refusal);
                }
            }
            if (Time.time > _deadline)
            {
                return Finish(GuidanceStatus.Failed, "not docked after " + (GiveUpSeconds / 60).ToString("0") + " min");
            }

            Transform face = _targetPort!.nodeTransform;
            Vector3d offset = _chaserPort!.nodeTransform.position - face.position;
            Vector3d relative = _vessel.obt_velocity - target.obt_velocity;
            if (_lastRelative is Vector3d last)
            {
                DeltaVDone += (relative - last).magnitude;
            }
            _lastRelative = relative;
            if (offset.magnitude > DriftLimit)
            {
                return Finish(GuidanceStatus.Failed, "drifted " + offset.magnitude.ToString("0") + " m from " + _target.Name);
            }

            Steering.Hold(_vessel, -face.forward);
            double misalignment = Vector3.Angle(_chaserPort.nodeTransform.forward, -face.forward);
            Vector3d center = (Vector3d)target.CoM - (Vector3d)face.position;
            double radius = target.parts.Max(p => (p.transform.position - target.CoM).magnitude) + 1;
            DockingCommand command = DockingApproach.Command(InPort(face, offset), InPort(face, center), radius, RcsAcceleration(), misalignment);
            Vector3d wanted = face.right * (float)command.Velocity.X + face.up * (float)command.Velocity.Y + face.forward * (float)command.Velocity.Z;
            Steering.Rcs(_vessel, true);
            _translation = Steering.Translation(_vessel, wanted - relative, RcsGain);
            return GuidanceStatus.Running;
        }

        public void Stop()
        {
            _translation = Vector3.zero;
            _vessel.OnFlyByWire -= Drive;
            if (_previousReference != null && _previousReference.vessel == _vessel && !HasDocked)
            {
                _vessel.SetReferenceTransform(_previousReference);
            }
        }

        private string? TakePorts(Vessel target)
        {
            PortChoice choice = DockingPorts.Pick(_vessel.vesselName, VesselPorts.Of(_vessel), _target.Name, VesselPorts.Of(target), _target.PortId, VesselPorts.Toward(target, _vessel));
            if (!choice.Ok)
            {
                return choice.Refusal;
            }
            _chaserPort = VesselPorts.Node(_vessel, choice.Chaser!.Id);
            _targetPort = VesselPorts.Node(target, choice.Target!.Id);
            if (_chaserPort == null || _targetPort == null)
            {
                return "could not find the picked ports";
            }
            _previousReference = _vessel.GetReferenceTransformPart();
            _chaserPort.MakeReferenceTransform();
            return null;
        }

        private static bool Docked(ModuleDockingNode port) => port.otherNode != null || port.state.StartsWith("Docked", StringComparison.Ordinal);

        private static string Title(ModuleDockingNode port) => port.part.partInfo?.title ?? port.part.name;

        /// <summary>A world vector in the frame of <paramref name="frame"/>: x right, y up, z forward (out of a port face).</summary>
        private static CoreVector InPort(Transform frame, Vector3d world) =>
            new CoreVector(Vector3d.Dot(world, frame.right), Vector3d.Dot(world, frame.up), Vector3d.Dot(world, frame.forward));

        /// <summary>A cautious per-axis RCS acceleration: about a sixth of all nozzles push along any one axis.</summary>
        private double RcsAcceleration()
        {
            double thrust = _vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleRCS>())
                .Where(r => r.rcsEnabled && r.moduleIsEnabled)
                .Sum(r => (double)r.thrusterPower * r.thrusterTransforms.Count);
            return _vessel.totalMass > 0 ? thrust / _vessel.totalMass / 6 : 0;
        }

        private GuidanceStatus Finish(GuidanceStatus status, string note)
        {
            Note = note;
            Stop();
            return status;
        }

        private void Drive(FlightCtrlState state)
        {
            state.mainThrottle = 0;
            state.X = _translation.x;
            state.Y = _translation.y;
            state.Z = _translation.z;
        }
    }
}
