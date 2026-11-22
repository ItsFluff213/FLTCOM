// The docking approach (clear the target's side when behind the port, back out to a safe distance, line up on the
// port axis, then close along a corridor with a speed limit set by the braking distance) and its constants are ported
// from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/MechJebModuleDockingAutopilot.cs
// Original notice: Copyright the MechJeb contributors; MechJeb2 is licensed GPL-3.0, as is FLTCOM.
using System;
using FLTCOM.Core.Missions;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    public enum DockingPhase
    {
        /// <summary>Behind the port plane and beside the target: move away from the port axis.</summary>
        ClearSide,
        /// <summary>Off the corridor and closer than the safe distance: move out along the port axis.</summary>
        BackOut,
        /// <summary>At a safe distance but off the corridor: move sideways onto the port axis.</summary>
        LineUp,
        /// <summary>Inside the approach cone: close on the port.</summary>
        Approach,
    }

    public readonly struct DockingCommand
    {
        public DockingCommand(DockingPhase phase, Vector3d velocity)
        {
            Phase = phase;
            Velocity = velocity;
        }

        public DockingPhase Phase { get; }

        /// <summary>The wanted velocity of the chaser's port relative to the target port, in the target port frame.</summary>
        public Vector3d Velocity { get; }
    }

    /// <summary>
    /// The RCS approach in the target port's frame: z points out of the port face, x and y lie across it. Speeds fall with
    /// distance and never exceed what the RCS can stop in the distance left.
    /// </summary>
    public static class DockingApproach
    {
        public const string Label = "dock with RCS";
        public const double CorridorRadius = 1;
        public const double AlignedAngle = 5;
        public const double SafeDistance = 10;
        public const double SpeedLimit = 1;
        public const double ManeuverSpeedLimit = 2;
        public const double MinSpeed = 0.1;
        public const double SlowdownDistance = 20;
        public const double LateralGain = 0.2;
        public const double MaxLateralSpeed = 0.5;
        public const double StartDistance = 50;

        private static readonly double ConeSlope = Math.Tan(AlignedAngle * Math.PI / 180);

        /// <summary>The planned step that hands a rendezvous holding within 50 m to the docking controller.</summary>
        public static PlannedBurn Burn(string vesselId, double startUt) => new PlannedBurn(vesselId, Label, startUt, startUt, 0, 0, 0, 0);

        /// <summary>The corridor's half-width at <paramref name="z"/> metres out from the port.</summary>
        public static double Cone(double z) => CorridorRadius + Math.Max(z, 0) * ConeSlope;

        /// <summary>
        /// The velocity to fly from <paramref name="position"/> (the chaser port in the target port frame). The target is a
        /// sphere of <paramref name="radius"/> around <paramref name="center"/>; <paramref name="braking"/> is the RCS
        /// acceleration in m/s². Until the ports are within <paramref name="misalignment"/> of <see cref="AlignedAngle"/>
        /// degrees the chaser never closes inside the safe distance.
        /// </summary>
        public static DockingCommand Command(Vector3d position, Vector3d center, double radius, double braking, double misalignment = 0)
        {
            double z = position.Z;
            double lateral = Math.Sqrt(position.X * position.X + position.Y * position.Y);
            Vector3d side = lateral > 1e-6 ? new Vector3d(position.X / lateral, position.Y / lateral, 0) : new Vector3d(1, 0, 0);
            double centerLateral = Math.Sqrt(center.X * center.X + center.Y * center.Y);
            double clear = centerLateral + radius + SafeDistance;

            if (z < 0 && lateral < clear)
            {
                return new DockingCommand(DockingPhase.ClearSide, side * Speed(clear - lateral, braking, ManeuverSpeedLimit));
            }
            if (z < 0 || (z < SafeDistance && lateral > Cone(z)))
            {
                return new DockingCommand(DockingPhase.BackOut, new Vector3d(0, 0, Speed(SafeDistance - z, braking, ManeuverSpeedLimit)));
            }
            if (lateral > Cone(z))
            {
                return new DockingCommand(DockingPhase.LineUp, side * -Speed(lateral, braking, ManeuverSpeedLimit));
            }
            double closing = misalignment > AlignedAngle && z < SafeDistance ? 0 : Speed(z, braking, SpeedLimit);
            double across = Math.Min(lateral * LateralGain, MaxLateralSpeed);
            return new DockingCommand(DockingPhase.Approach, new Vector3d(0, 0, -closing) - side * across);
        }

        private static double Speed(double distance, double braking, double limit)
        {
            if (distance <= 0)
            {
                return 0;
            }
            double falling = Math.Max(MinSpeed, limit * distance / SlowdownDistance);
            return Math.Min(Math.Min(falling, limit), Math.Sqrt(2 * distance * Math.Max(braking, 0.01)));
        }
    }
}
