using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Tests
{
    /// <summary>The stock KSP 1.12 bodies the transfer tests fly between: values from the game's body definitions, angles in degrees there.</summary>
    internal static class StockSystem
    {
        public const double SunMu = 1.1723328e18;
        public const double KerbinMu = 3.5316e12;

        public static readonly SolarSystem System = new SolarSystem(new[]
        {
            new SystemBody(new CentralBody("Sun", SunMu, 261_600_000, 600_000, 0), null, null),
            Body("Kerbin", KerbinMu, 600_000, 70_000, 6_767, 84_159_286, "Sun", SunMu, 13_599_840_256, 0, 0, 0, 0, 3.14),
            Body("Mun", 6.5138398e10, 200_000, 0, 7_061, 2_429_559.1, "Kerbin", KerbinMu, 12_000_000, 0, 0, 0, 0, 1.7),
            Body("Minmus", 1.7658e9, 60_000, 0, 5_725, 2_247_428.4, "Kerbin", KerbinMu, 47_000_000, 0, 6, 78, 38, 0.9),
            Body("Duna", 3.0136321e11, 320_000, 50_000, 8_264, 47_921_949, "Sun", SunMu, 20_726_155_264, 0.051, 0.06, 135.5, 0, 3.14),
            Body("Eve", 8.1717302e12, 700_000, 90_000, 7_526, 85_109_365, "Sun", SunMu, 9_832_684_544, 0.01, 2.1, 15, 0, 3.14),
        });

        /// <summary>A circular equatorial orbit around Kerbin at <paramref name="altitude"/>, at the angle <paramref name="longitude"/> radians at <paramref name="ut"/>.</summary>
        public static Patch KerbinOrbit(double altitude, double ut, double longitude = 0) =>
            new Patch("Kerbin", new OrbitElements(KerbinMu, 600_000 + altitude, 0, 0, 0, 0, longitude, ut).StateAt(ut), ut);

        private static SystemBody Body(string name, double mu, double radius, double atmosphere, double terrain, double soi, string parent, double parentMu, double a, double e, double inc, double lan, double argPe, double meanAnomaly) =>
            new SystemBody(
                new CentralBody(name, mu, radius, atmosphere, terrain, soi),
                parent,
                new OrbitElements(parentMu, a, e, Angles.ToRadians(inc), Angles.ToRadians(lan), Angles.ToRadians(argPe), meanAnomaly, 0));
    }
}
