// Ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/MechJebModuleLandingAutopilot.cs (ComputeCourseCorrection), modified for FLTCOM: the
// sensitivities come from re-running the full re-entry prediction with the velocity nudged along track and across it,
// instead of from Kepler orbits, so they hold while the braking burn runs; no KSP types.
// Original notice: Copyright (C) the MechJeb2 contributors. Licensed under the GNU General Public License version 3.
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    /// <summary>How far a predicted landing misses the target, and the burn that removes the miss.</summary>
    public static class LandingAim
    {
        /// <summary>Predicted landing point minus target, on the ground as it stands at <paramref name="ut"/>.</summary>
        public static Vector3d Miss(LandingBody body, LandingPrediction prediction, LandingTarget target, double targetAltitude, double ut) =>
            body.Surface(prediction.Latitude, prediction.Longitude, targetAltitude, ut) - body.Surface(target.Latitude, target.Longitude, targetAltitude, ut);

        /// <summary>
        /// The horizontal inertial delta-v that cancels <paramref name="miss"/>, by least squares over a nudge along the
        /// ground track and one across it; zero when a nudged descent no longer lands.
        /// </summary>
        public static Vector3d CourseCorrection(LandingBody body, LandingCraft craft, ReentrySettings settings, StateVector state, double ut, Vector3d miss, LandingTarget target, double targetAltitude)
        {
            Vector3d up = state.Position.Normalized;
            Vector3d track = Horizontal(body.AirVelocity(state.Position, state.Velocity), up);
            Vector3d along = track.Magnitude > 1e-3 ? track.Normalized : Vector3d.Cross(new Vector3d(0, 0, 1), up).Normalized;
            Vector3d across = Vector3d.Cross(up, along);
            Vector3d? da = Nudged(along);
            Vector3d? dn = Nudged(across);
            if (da is not Vector3d a || dn is not Vector3d n)
            {
                return Zero;
            }
            double a11 = Vector3d.Dot(a, a);
            double a12 = Vector3d.Dot(a, n);
            double a22 = Vector3d.Dot(n, n);
            double det = a11 * a22 - a12 * a12;
            if (det < 1e-9 * a11 * a22 || det <= 0)
            {
                return Zero;
            }
            double b1 = -Vector3d.Dot(a, miss);
            double b2 = -Vector3d.Dot(n, miss);
            double x = (b1 * a22 - b2 * a12) / det;
            double y = (a11 * b2 - a12 * b1) / det;
            return along * x + across * y;

            Vector3d? Nudged(Vector3d direction)
            {
                LandingPrediction p = ReentryPredictor.Predict(body, craft, new StateVector(state.Position, state.Velocity + direction), ut, settings);
                return p.Landed ? Miss(body, p, target, targetAltitude, ut) - miss : null;
            }
        }

        public static Vector3d Horizontal(Vector3d v, Vector3d up) => v - up * Vector3d.Dot(v, up);

        private static Vector3d Zero => new Vector3d(0, 0, 0);
    }
}
