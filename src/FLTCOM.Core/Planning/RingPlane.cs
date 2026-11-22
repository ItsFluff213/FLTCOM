using System;
using System.Collections.Generic;
using System.Globalization;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>The plane a constellation ring lies in: an inclination and the longitude of its ascending node, radians.</summary>
    public sealed class RingPlane
    {
        public static readonly RingPlane Equatorial = new RingPlane(0, 0);

        public RingPlane(double inclination, double ascendingNode)
        {
            Inclination = inclination;
            AscendingNode = inclination == 0 ? 0 : Angles.Wrap(ascendingNode);
        }

        public double Inclination { get; }

        /// <summary>Meaningless on the equator, where it is zero.</summary>
        public double AscendingNode { get; }

        /// <summary>The default: the carrier flattens onto the equator first, as it always has.</summary>
        public bool IsEquatorial => Inclination == 0;

        public Vector3d Normal => LaunchPlane.PlaneNormal(Inclination, AscendingNode);

        public string? Refusal => Inclination < 0 || Inclination > Math.PI ? "a ring's inclination must be between 0 and 180 degrees" : null;

        public string Describe() =>
            IsEquatorial ? "equatorial ring"
            : Math.Abs(Inclination - Math.PI / 2) < 1e-9 ? "polar ring, node " + Deg(AscendingNode) + " deg"
            : "ring inclined " + Deg(Inclination) + " deg, node " + Deg(AscendingNode) + " deg";

        /// <summary>
        /// Angle of the vessel in the ring plane with <paramref name="normal"/>, from the plane's ascending node in the
        /// direction of motion: its argument of latitude. On the equator the node is the reference x axis.
        /// </summary>
        public static double ArgumentOfLatitude(OrbitElements orbit, double ut, Vector3d normal)
        {
            Vector3d node = Vector3d.Cross(new Vector3d(0, 0, 1), normal);
            if (node.Magnitude < 1e-12)
            {
                node = Vector3d.Cross(new Vector3d(0, 1, 0), normal);
            }
            return Vector3d.AngleAbout(node, orbit.StateAt(ut).Position, normal);
        }

        /// <summary>The plane a set of orbits shares: the mean of their orbit normals.</summary>
        public static Vector3d MeanNormal(IEnumerable<OrbitElements> orbits)
        {
            var sum = new Vector3d(0, 0, 0);
            foreach (OrbitElements o in orbits)
            {
                StateVector s = o.StateAtTrueAnomaly(0);
                sum = sum + Vector3d.Cross(s.Position, s.Velocity).Normalized;
            }
            return sum.Normalized;
        }

        private static string Deg(double radians) => Angles.ToDegrees(radians).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
