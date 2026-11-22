using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Constellation;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class RingPlaneTests
    {
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767, 84_159_286, 21_549.425);
        private static readonly RingPlane Polar = new RingPlane(Math.PI / 2, Angles.ToRadians(30));

        private static List<CarrierPart> Carrier(bool engines, double tank = 8)
        {
            var parts = new List<CarrierPart>
            {
                new CarrierPart(1, null, "probe core", CarrierPartKind.Command, 0.1, 0.1),
                new CarrierPart(2, 1, "tank", CarrierPartKind.None, tank, tank / 8),
                new CarrierPart(3, 2, "engine", CarrierPartKind.Engine, 0.5, 0.5, 60, 345),
                new CarrierPart(4, 1, "parachute", CarrierPartKind.Parachute, 0.1, 0.1),
            };
            for (uint k = 1; k <= 3; k++)
            {
                parts.Add(new CarrierPart(10 * k, 2, "separator", CarrierPartKind.Separator, 0.05, 0.05));
                parts.Add(new CarrierPart(10 * k + 1, 10 * k, "probe core", CarrierPartKind.Command, 0.1, 0.1));
                parts.Add(new CarrierPart(10 * k + 2, 10 * k + 1, "tank", CarrierPartKind.None, 0.5, 0.1));
                if (engines)
                {
                    parts.Add(new CarrierPart(10 * k + 3, 10 * k + 2, "engine", CarrierPartKind.Engine, 0.1, 0.1, 20, 300));
                }
            }
            return parts;
        }

        private static OrbitElements Equatorial(double altitude) => new OrbitElements(Kerbin.GravParameter, Kerbin.Radius + altitude, 0, 0, 0, 0, 0, 0);

        private static VesselTask Task(List<CarrierPart> parts) =>
            new VesselTask("100", "Ring Carrier", Kerbin, Equatorial(100_000), Array.Empty<StageDeltaV>(), carrier: parts);

        private static string Fingerprint(DeployPlan plan) =>
            string.Join(";", plan.CarrierBurns.Select(b => b.Label + "@" + N(b.NodeUt) + "=" + N(b.DeltaV)))
            + "|" + string.Join(";", plan.Releases.Select(r => N(r.ReleaseUt) + "/" + N(r.NodeUt) + "=" + N(r.CircularizeDeltaV)));

        private static string N(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Captured from the planner before rings existed: a 6 degree carrier flattens first, then deploys.</summary>
        [Theory]
        [InlineData(true, "inclination to 0 deg@531.127=229.696;apoapsis to 2000 km@1685.038=551.744;resonant orbit 2000 km x 3099.4 km@5248.827=498.534|23846.426/23938.12=97.61;42535.719/42627.413=97.61;61225.012/61316.706=97.61")]
        [InlineData(false, "inclination to 0 deg@531.127=229.696;apoapsis to 2000 km@1685.038=551.744;circularize at 2000 km@5248.827=400.924;phasing orbit before Golden Sat 2@5608.827=197.617;back on the circle for Golden Sat 2@14953.473=197.617;phasing orbit before Golden Sat 3@15073.473=197.617;back on the circle for Golden Sat 3@24418.12=197.617|5548.827/5548.827=0;15013.473/15013.473=0;24478.12/24478.12=0")]
        public void TheDefaultRequestPlansAsBefore(bool engines, string before)
        {
            var tilted = new OrbitElements(Kerbin.GravParameter, Kerbin.Radius + 120_000, 0.01, Angles.ToRadians(6), 0.7, 0.4, 1.1, 0);
            var parts = Carrier(engines).Where(p => p.Id != 4).ToList();

            DeployPlan plan = ConstellationPlanner.Plan("Golden", Kerbin, tilted, PayloadDetector.Scan(parts), 2_000_000, 0);

            Assert.Equal(before, Fingerprint(plan));
            Assert.Same(RingPlane.Equatorial, OrbitChange.Deploy(2_000_000).Ring);
            Assert.Equal("deploy constellation at 2000 km", OrbitChange.Deploy(2_000_000).Describe());
        }

        [Fact]
        public void APolarRingFromAnEquatorialCarrierPlansThePlaneChangeAndCountsItsDeltaV()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;
            double planeChange = Math.Sqrt(2) * Math.Sqrt(Kerbin.GravParameter / (Kerbin.Radius + 100_000));

            Briefing flat = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude), new[] { Task(Carrier(true)) }, 0);
            Briefing polar = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude, Polar), new[] { Task(Carrier(true, tank: 24)) }, 0);

            Assert.True(polar.CanApprove, string.Join("; ", polar.Contingencies.Select(c => c.Text)));
            PlannedBurn turn = polar.Burns.First();
            Assert.Equal("plane change into the polar ring, node 30 deg", turn.Label);
            Assert.Equal(planeChange, turn.DeltaV, 0);
            Assert.True(polar.RequiredDeltaV > flat.RequiredDeltaV + planeChange - 1, polar.RequiredDeltaV + " vs " + flat.RequiredDeltaV);
            Assert.Contains("polar ring, node 30 deg", polar.Change);
            OrbitElements ring = polar.Deployment!.Releases[0].Final;
            Assert.Equal(90, Angles.ToDegrees(ring.Inclination), 1);
            Assert.Equal(30, Angles.ToDegrees(ring.LongitudeOfAscendingNode), 1);

            Briefing shortOfFuel = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude, Polar), new[] { Task(Carrier(true)) }, 0);
            Assert.True(flat.CanApprove, string.Join("; ", flat.Contingencies.Select(c => c.Text)));
            Assert.False(shortOfFuel.CanApprove);
            Assert.Contains(shortOfFuel.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("propellant"));
        }

        [Fact]
        public void ThePlaneChangeTakesTheCheaperNode()
        {
            double rp = Kerbin.Radius + 100_000;
            double ra = Kerbin.Radius + 2_000_000;
            var elliptical = new OrbitElements(Kerbin.GravParameter, (rp + ra) / 2, (ra - rp) / (ra + rp), 0, 0, 0, Math.PI + 0.5, 0);

            DeployPlan plan = ConstellationPlanner.Plan("Golden", Kerbin, elliptical, PayloadDetector.Scan(Carrier(true)), 2_863_334, 0, new RingPlane(Math.PI / 2, 0));

            Assert.True(plan.Ok, plan.Error);
            ImpulsiveBurn turn = plan.CarrierBurns[0];
            Assert.StartsWith("plane change", turn.Label);
            Assert.InRange(turn.OrbitAfter!.StateAt(turn.NodeUt).Position.Magnitude, ra - 1000, ra + 1000);
            Assert.True(turn.NodeUt > elliptical.NextTimeAtTrueAnomaly(0, 0), "the periapsis node comes first but costs more");
        }

        [Theory]
        [InlineData(true, 90)]
        [InlineData(false, 90)]
        [InlineData(true, 63.4)]
        [InlineData(true, 180)]
        public void RingsInTiltedPlanesEndEvenlySpaced(bool engines, double inclinationDeg)
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;
            var ring = new RingPlane(Angles.ToRadians(inclinationDeg), Angles.ToRadians(30));

            DeployPlan plan = ConstellationPlanner.Plan("Ring Carrier", Kerbin, Equatorial(100_000), PayloadDetector.Scan(Carrier(engines)), altitude, 0, ring);

            Assert.True(plan.Ok, plan.Error);
            Assert.True(plan.MaxSpacingError < Angles.ToRadians(1), "spacing error " + Angles.ToDegrees(plan.MaxSpacingError) + " deg");
            double reference = plan.Releases.Max(r => r.NodeUt) + 1;
            var finals = plan.Releases.Select(r => r.Final).ToList();
            Vector3d normal = RingPlane.MeanNormal(finals);
            Assert.True(Vector3d.Dot(normal, ring.Normal) > Math.Cos(Angles.ToRadians(0.1)), "the ring lies in the requested plane");
            var angles = finals.Select(o => Angles.ToDegrees(RingPlane.ArgumentOfLatitude(o, reference, normal))).OrderBy(x => x).ToList();
            for (int k = 0; k < angles.Count; k++)
            {
                Assert.Equal(120, (angles[(k + 1) % angles.Count] - angles[k] + 360) % 360, 0);
            }
        }

        [Fact]
        public void AnInclinationOutsideZeroTo180IsRefused()
        {
            DeployPlan plan = ConstellationPlanner.Plan("Ring Carrier", Kerbin, Equatorial(100_000), PayloadDetector.Scan(Carrier(true)), 2_863_334, 0, new RingPlane(Angles.ToRadians(181), 0));

            Assert.False(plan.Ok);
            Assert.Contains("between 0 and 180", plan.Error);
        }

        [Fact]
        public void TheRingSurvivesASave()
        {
            var op = new Operation("op-1", "Polar Ring", OrbitChange.Deploy(2_863_334, Polar), new[] { "100" }, Array.Empty<PlannedBurn>(), StepState.Waiting);

            Mission back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Polar", new[] { op })));

            RingPlane ring = back.Operations[0].Change.Ring!;
            Assert.Equal(Polar.Inclination, ring.Inclination, 9);
            Assert.Equal(Polar.AscendingNode, ring.AscendingNode, 9);
            Assert.Same(RingPlane.Equatorial, MissionJson.Read(MissionJson.Write(new Mission("m-2", "Flat", new[] { new Operation("op-2", "Flat", OrbitChange.Deploy(2_863_334), new[] { "100" }, Array.Empty<PlannedBurn>(), StepState.Waiting) }))).Operations[0].Change.Ring);
        }
    }
}
