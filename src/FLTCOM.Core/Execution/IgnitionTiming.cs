using System;
using FLTCOM.Core.Missions;

namespace FLTCOM.Core.Execution
{
    /// <summary>How early a vessel starts turning toward a burn, and how late a burn may light before it is planned again.</summary>
    public static class IgnitionTiming
    {
        /// <summary>Seconds. Enough for a nimble craft to turn and settle.</summary>
        public const double TurnFloor = 60;

        /// <summary>Seconds. A craft slower than this is caught by the late-ignition re-plan instead.</summary>
        public const double TurnCap = 300;

        /// <summary>Stock SAS slews slower than an ideal full-torque turn and overshoots before it settles.</summary>
        private const double SasSlack = 2;

        private const double Settle = 15;

        public const double LateFloor = 5;

        public const double LateShare = 0.1;

        /// <summary>
        /// Seconds before ignition to start turning: a half turn at full torque, accelerate then brake, slowed for SAS.
        /// </summary>
        /// <param name="angularAcceleration">The weakest of pitch and yaw, rad/s²; null when it could not be read.</param>
        public static double TurnLead(double? angularAcceleration)
        {
            if (angularAcceleration == null || double.IsNaN(angularAcceleration.Value))
            {
                return TurnFloor;
            }
            if (angularAcceleration.Value <= 0)
            {
                return TurnCap;
            }
            double turn = 2 * Math.Sqrt(Math.PI / angularAcceleration.Value);
            return Math.Min(TurnCap, Math.Max(TurnFloor, SasSlack * turn + Settle));
        }

        /// <summary>How far past its start a burn may light and still be flown as planned.</summary>
        public static double LateTolerance(double duration) => Math.Max(LateFloor, LateShare * duration);

        public static bool IsLate(PlannedBurn burn, double now) => now - burn.StartUt > LateTolerance(burn.Duration);
    }
}
