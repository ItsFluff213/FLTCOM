using System;

namespace FLTCOM.Core.Planning
{
    /// <summary>The body an orbit is around, as the planner needs it. Distances in metres.</summary>
    public sealed class CentralBody
    {
        public CentralBody(string name, double gravParameter, double radius, double atmosphereHeight, double highestTerrainAltitude, double sphereOfInfluence = double.PositiveInfinity, double rotationPeriod = 0)
        {
            RotationPeriod = rotationPeriod;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            GravParameter = gravParameter;
            Radius = radius;
            AtmosphereHeight = atmosphereHeight;
            HighestTerrainAltitude = highestTerrainAltitude;
            SphereOfInfluence = sphereOfInfluence;
        }

        public string Name { get; }

        public double GravParameter { get; }

        public double Radius { get; }

        public double AtmosphereHeight { get; }

        public double HighestTerrainAltitude { get; }

        /// <summary>Radius of the sphere of influence; infinite for the Sun.</summary>
        public double SphereOfInfluence { get; }

        /// <summary>Altitude a periapsis must clear to stay up: the top of the atmosphere or the highest terrain, whichever is higher.</summary>
        public double SafeFloor => Math.Max(AtmosphereHeight, HighestTerrainAltitude);

        /// <summary>Sidereal day in seconds; 0 when not read.</summary>
        public double RotationPeriod { get; }

        public static CentralBody From(Protocol.Body body) =>
            new CentralBody(body.Name, body.GravParameter, body.Radius, body.AtmosphereHeight, body.HighestTerrainAltitude, body.SphereOfInfluence ?? double.PositiveInfinity, body.RotationPeriod);
    }

    public static class StandardOrbit
    {
        public const double Clearance = 10_000;

        /// <summary>Altitude of the standard low orbit: 10 km above the atmosphere, or above the highest terrain on an airless body.</summary>
        public static double LowOrbitAltitude(CentralBody body) =>
            Math.Max(body.AtmosphereHeight, body.HighestTerrainAltitude) + Clearance;
    }
}
