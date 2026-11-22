using System;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>
    /// KSP keeps a vessel's rotation relative to its body in Unity's left-handed frame, y up. The protocol wants the
    /// rotation from the craft frame with y and z swapped to the right-handed orbit frame, z north, which is the
    /// body frame turned by the body's rotation angle about its axis.
    /// </summary>
    public static class Attitude
    {
        public static Quat ToOrbitFrame(Quat surfaceRelative, double rotationAngle)
        {
            double half = rotationAngle / 2;
            return Normalize(Multiply(new Quat(0, 0, Math.Sin(half), Math.Cos(half)), Swizzle(surfaceRelative)));
        }

        /// <summary>The same rotation seen with y and z swapped on both sides. The swap is a mirror, so the angle flips.</summary>
        public static Quat Swizzle(Quat q) => new Quat(-q.X, -q.Z, -q.Y, q.W);

        public static Quat Multiply(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static Vec3 Rotate(Quat q, Vec3 v)
        {
            double tx = 2 * (q.Y * v.Z - q.Z * v.Y);
            double ty = 2 * (q.Z * v.X - q.X * v.Z);
            double tz = 2 * (q.X * v.Y - q.Y * v.X);
            return new Vec3(
                v.X + q.W * tx + (q.Y * tz - q.Z * ty),
                v.Y + q.W * ty + (q.Z * tx - q.X * tz),
                v.Z + q.W * tz + (q.X * ty - q.Y * tx));
        }

        private static Quat Normalize(Quat q)
        {
            double length = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            return length == 0 ? new Quat(0, 0, 0, 1) : new Quat(q.X / length, q.Y / length, q.Z / length, q.W / length);
        }
    }
}
