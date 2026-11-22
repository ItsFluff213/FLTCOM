using System;
using System.Collections.Generic;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.PlanCli
{
    /// <summary>Named planning fixtures built from stock part data (KSP 1.12).</summary>
    public static class Fixtures
    {
        public const double Epoch = 5184000;

        public static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_764.1, 84_159_286);

        public static IReadOnlyCollection<string> Names => new[] { "lko-elliptic", "lko-short", "lko-100-equatorial" };

        public static VesselTask Load(string name) => name switch
        {
            "lko-elliptic" => new VesselTask("lko-elliptic", "Kestrel Probe", Kerbin, Orbit(80, 150, 0.3), DeltaVEstimator.Estimate(TwoStageProbe(1.0, 1.0))),
            "lko-short" => new VesselTask("lko-short", "Dry Probe", Kerbin, Orbit(80, 150, 0.3), DeltaVEstimator.Estimate(TwoStageProbe(0.05, 0.05))),
            "lko-100-equatorial" => new VesselTask("lko-100-equatorial", "Plane Probe", Kerbin, Orbit(100, 100, 0), DeltaVEstimator.Estimate(TwoStageProbe(1.0, 1.0))),
            _ => throw new ArgumentException("unknown fixture " + name + "; known: " + string.Join(", ", Names)),
        };

        public static OrbitElements Orbit(double periapsisKm, double apoapsisKm, double inclinationDeg)
        {
            double rp = Kerbin.Radius + periapsisKm * 1000;
            double ra = Kerbin.Radius + apoapsisKm * 1000;
            return new OrbitElements(Kerbin.GravParameter, (rp + ra) / 2, (ra - rp) / (ra + rp), Angles.ToRadians(inclinationDeg), 0, 0, 1.0, Epoch);
        }

        /// <summary>OKTO probe on a Spark with an FL-T100, above a Terrier with an FL-T400 dropped by a TD-12 in stage 0.</summary>
        public static IReadOnlyList<PartSnapshot> TwoStageProbe(double lowerFuelFraction, double upperFuelFraction)
        {
            const double density = 0.005;
            var biprop = new[] { new Propellant("LiquidFuel", 0.9), new Propellant("Oxidizer", 1.1) };
            return new[]
            {
                new PartSnapshot("probeCoreOcto", 0.1, -1, new[] { new PartResource("ElectricCharge", 10, 0) }),
                new PartSnapshot("fuelTankSmallFlat", 0.0625, -1, new[] { new PartResource("LiquidFuel", 45 * upperFuelFraction, density), new PartResource("Oxidizer", 55 * upperFuelFraction, density) }),
                new PartSnapshot("liquidEngineMini.v2", 0.13, -1, Array.Empty<PartResource>(), new EngineData(0, 20, 320, biprop)),
                new PartSnapshot("Decoupler.1", 0.04, 0, Array.Empty<PartResource>()),
                new PartSnapshot("fuelTankSmall", 0.25, 0, new[] { new PartResource("LiquidFuel", 180 * lowerFuelFraction, density), new PartResource("Oxidizer", 220 * lowerFuelFraction, density) }),
                new PartSnapshot("liquidEngine3.v2", 0.5, 0, Array.Empty<PartResource>(), new EngineData(1, 60, 345, biprop)),
            };
        }
    }
}
