// Attribution: textbook vector algebra for the two-body formulas in OrbitElements.cs.
// Written for FLTCOM from Vallado, "Fundamentals of Astrodynamics and Applications", 4th ed.
// Not ported from MechJebLib: MechJeb is GPL-3.0 and FLTCOM is MIT.
using System;

namespace FLTCOM.Core.TwoBody
{
    /// <summary>A body-centred inertial vector. z points along the body's north pole.</summary>
    public readonly struct Vector3d
    {
        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d UnitX => new Vector3d(1, 0, 0);

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Magnitude => Math.Sqrt(Dot(this, this));

        public Vector3d Normalized => this / Magnitude;

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);

        public static Vector3d operator *(double s, Vector3d a) => a * s;

        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vector3d Cross(Vector3d a, Vector3d b) =>
            new Vector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>Rotates <paramref name="v"/> by <paramref name="angle"/> radians about the unit <paramref name="axis"/> (Rodrigues).</summary>
        public static Vector3d Rotate(Vector3d v, Vector3d axis, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return v * c + Cross(axis, v) * s + axis * (Dot(axis, v) * (1 - c));
        }

        /// <summary>Signed angle from <paramref name="from"/> to <paramref name="to"/> about <paramref name="axis"/>, in [0, 2π).</summary>
        public static double AngleAbout(Vector3d from, Vector3d to, Vector3d axis) =>
            Angles.Wrap(Math.Atan2(Dot(axis, Cross(from, to)), Dot(from, to)));
    }

    public static class Angles
    {
        public const double TwoPi = 2 * Math.PI;

        public static double Wrap(double radians)
        {
            double r = radians % TwoPi;
            return r < 0 ? r + TwoPi : r;
        }

        public static double ToRadians(double degrees) => degrees * Math.PI / 180;

        public static double ToDegrees(double radians) => radians * 180 / Math.PI;
    }
}
