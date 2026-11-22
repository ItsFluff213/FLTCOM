using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    /// <summary>
    /// Walks an attitude command toward the direction guidance wants by the shortest arc, no faster than
    /// <see cref="MaxRate"/>. Stock SAS answers a sudden jump in its locked rotation with full torque from every wheel,
    /// thruster and gimbal; the deorbit burn hands over pointing along its burn vector, tens of degrees from the surface
    /// retrograde the descent wants, so an unslewed handover spins the lander up.
    /// </summary>
    public static class AttitudeSlew
    {
        /// <summary>Radians per second.</summary>
        public const double MaxRate = 10 * Math.PI / 180;

        public static Vector3d Toward(Vector3d from, Vector3d to, double dt)
        {
            Vector3d a = from.Normalized;
            Vector3d b = to.Normalized;
            double off = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(a, b))));
            double most = MaxRate * dt;
            if (off <= most)
            {
                return b;
            }
            Vector3d axis = Vector3d.Cross(a, b);
            if (axis.Magnitude < 1e-9)
            {
                axis = Vector3d.Cross(a, Math.Abs(a.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0));
            }
            return Vector3d.Rotate(a, axis.Normalized, most).Normalized;
        }
    }
}
