using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Constellation;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class ConstellationTests
    {
        private const string Name = "Relay Carrier";
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767, 84_159_286, 21_549.425);
        private static readonly CentralBody Mun = new CentralBody("Mun", 6.5138398e10, 200_000, 0, 7_061, 2_429_559.1, 138_984.38);
        private static readonly CentralBody Minmus = new CentralBody("Minmus", 1.7658e9, 60_000, 0, 5_725, 2_247_428.4, 40_400);

        /// <summary>
        /// A probe core on a tank and engine; each satellite hangs from its own separator (or docking port) on the tank:
        /// probe core, small tank, small engine. Ids: carrier 1..4, satellite k from 10k.
        /// </summary>
        private static List<CarrierPart> Carrier(int satellites, bool engines = true, bool dockingPort = false, bool chutes = true, bool cores = true, double tank = 8)
        {
            var parts = new List<CarrierPart>
            {
                new CarrierPart(1, null, "probe core", CarrierPartKind.Command, 0.1, 0.1),
                new CarrierPart(2, 1, "tank", CarrierPartKind.None, tank, tank / 8),
                new CarrierPart(3, 2, "engine", CarrierPartKind.Engine, 0.5, 0.5, 60, 345),
            };
            if (chutes)
            {
                parts.Add(new CarrierPart(4, 1, "parachute", CarrierPartKind.Parachute, 0.1, 0.1));
            }
            for (uint k = 1; k <= satellites; k++)
            {
                uint id = 10 * k;
                parts.Add(new CarrierPart(id, 2, dockingPort ? "docking port" : "separator", dockingPort ? CarrierPartKind.DockingPort : CarrierPartKind.Separator, 0.05, 0.05));
                if (cores)
                {
                    parts.Add(new CarrierPart(id + 1, id, "sat core", CarrierPartKind.Command, 0.1, 0.1));
                }
                parts.Add(new CarrierPart(id + 2, cores ? id + 1 : id, "sat tank", CarrierPartKind.None, 0.225, 0.025));
                if (engines)
                {
                    parts.Add(new CarrierPart(id + 3, id + 2, "sat engine", CarrierPartKind.Engine, 0.02, 0.02, 2, 315));
                }
            }
            return parts;
        }

        private static OrbitElements Circular(CentralBody body, double altitude) =>
            new OrbitElements(body.GravParameter, body.Radius + altitude, 0, 0, 0, 0, 0, 0);

        private static VesselTask Task(CentralBody body, double altitude, List<CarrierPart> parts) =>
            new VesselTask("100", Name, body, Circular(body, altitude), Array.Empty<StageDeltaV>(), carrier: parts);

        [Fact]
        public void FindsThreeSatellitesWithTheirOwnDeltaV()
        {
            CarrierScan scan = PayloadDetector.Scan(Carrier(3));

            Assert.Equal(new[] { 1, 2, 3 }, scan.Payloads.Select(p => p.Number));
            Assert.Equal(new uint[] { 11, 21, 31 }, scan.Payloads.Select(p => p.CoreId));
            Assert.All(scan.Payloads, p => Assert.Equal(0.345, p.Mass, 6));
            Assert.All(scan.Payloads, p => Assert.InRange(p.DeltaV, 2_600, 2_750));
            Assert.Equal(0.1 + 8 + 0.5 + 0.1 + 3 * 0.05, scan.Carrier.Mass, 6);
            Assert.True(scan.CarrierHasChutes);
            Assert.Equal("Relay Carrier Sat 2", Payload.NameOf(Name, 2));
        }

        [Fact]
        public void PayloadsWithoutEnginesAreStillPayloads()
        {
            CarrierScan scan = PayloadDetector.Scan(Carrier(3, engines: false));

            Assert.Equal(3, scan.Payloads.Count);
            Assert.All(scan.Payloads, p => Assert.False(p.Own.CanBurn));
        }

        [Fact]
        public void APayloadBehindADockingPortIsFound()
        {
            CarrierScan scan = PayloadDetector.Scan(Carrier(1, dockingPort: true));

            Payload payload = Assert.Single(scan.Payloads);
            Assert.True(payload.ViaDockingPort);
            Assert.Equal(10u, payload.SeparatorId);
        }

        [Fact]
        public void SatellitesWithoutProbeCoresAreRefusedWithTheFix()
        {
            DeployPlan plan = ConstellationPlanner.Plan(Name, Kerbin, Circular(Kerbin, 100_000), PayloadDetector.Scan(Carrier(3, cores: false)), 2_863_334, 0);

            Assert.False(plan.Ok);
            Assert.Contains("Add a probe core, or a pod with its pilot aboard", plan.Error);
        }

        [Fact]
        public void KerbinSynchronousOrbitIsTheStockAltitude()
        {
            Assert.Equal(2_863_334, ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value, 0);
            Assert.InRange(ConstellationPlanner.SynchronousAltitude(Minmus)!.Value, 357_800, 358_100);
        }

        [Theory]
        [InlineData("Kerbin", 100_000)]
        [InlineData("Minmus", 30_000)]
        public void ReleasesOnePayloadPerPassOfAResonantOrbit(string bodyName, double parking)
        {
            CentralBody body = bodyName == "Kerbin" ? Kerbin : Minmus;
            double altitude = ConstellationPlanner.SynchronousAltitude(body)!.Value;
            double radius = body.Radius + altitude;
            double period = 2 * Math.PI * Math.Sqrt(radius * radius * radius / body.GravParameter);

            DeployPlan plan = ConstellationPlanner.Plan(Name, body, Circular(body, parking), PayloadDetector.Scan(Carrier(3)), altitude, 0);

            Assert.True(plan.Ok, plan.Error);
            Assert.Equal(DeployMethod.Resonant, plan.Method);
            double ratio = plan.Working!.Period / period;
            Assert.True(Math.Abs(ratio - 2.0 / 3) < 1e-6 || Math.Abs(ratio - 4.0 / 3) < 1e-6, "period ratio " + ratio);
            Assert.True(plan.Working.PeriapsisRadius > body.Radius + body.SafeFloor);
            Assert.Equal(3, plan.Releases.Count);
            for (int k = 1; k < 3; k++)
            {
                Assert.Equal(plan.Working.Period, plan.Releases[k].NodeUt - plan.Releases[k - 1].NodeUt, 3);
                Assert.True(plan.Releases[k].ReleaseUt < plan.Releases[k].NodeUt);
            }
            Assert.All(plan.Releases, r =>
            {
                Assert.InRange(r.Final.SemiMajorAxis, radius - 10, radius + 10);
                Assert.True(r.Final.Eccentricity < 1e-4);
            });
        }

        [Fact]
        public void TheCheaperValidResonanceWins()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;
            DeployPlan plan = ConstellationPlanner.Plan(Name, Kerbin, Circular(Kerbin, 100_000), PayloadDetector.Scan(Carrier(3)), altitude, 0);
            double radius = Kerbin.Radius + altitude;

            double inside = Cost(radius, 2.0 / 3);
            double outside = Cost(radius, 4.0 / 3);
            bool chosenInside = plan.Working!.ApoapsisRadius <= radius + 1;
            Assert.Equal(inside < outside, chosenInside);

            double Cost(double r, double k)
            {
                double mu = Kerbin.GravParameter;
                double r0 = Kerbin.Radius + 100_000;
                double a = r * Math.Pow(k, 2.0 / 3);
                double raise = Math.Sqrt(mu * (2 / r0 - 2 / (r0 + r))) - Math.Sqrt(mu / r0);
                double atR = Math.Sqrt(mu * (2 / r - 2 / (r0 + r)));
                double shape = Math.Abs(Math.Sqrt(mu * (2 / r - 1 / a)) - atR);
                double circularize = 3 * Math.Abs(Math.Sqrt(mu / r) - Math.Sqrt(mu * (2 / r - 1 / a)));
                return raise + shape + circularize;
            }
        }

        [Fact]
        public void TheMunIsRefusedWithTheHighestValidAltitude()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Mun)!.Value;

            DeployPlan plan = ConstellationPlanner.Plan(Name, Mun, Circular(Mun, 30_000), PayloadDetector.Scan(Carrier(3)), altitude, 0);

            Assert.False(plan.Ok);
            Assert.Contains("outside Mun's sphere of influence", plan.Error);
            Assert.Equal(2_108_000, plan.SuggestedAltitude!.Value);
            Assert.True(ConstellationPlanner.Plan(Name, Mun, Circular(Mun, 30_000), PayloadDetector.Scan(Carrier(3)), plan.SuggestedAltitude.Value, 0).Ok);
            Assert.Contains("safe floor", ConstellationPlanner.Refusal(Mun, 5_000));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SatellitesEndEvenlySpaced(bool engines)
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;

            DeployPlan plan = ConstellationPlanner.Plan(Name, Kerbin, Circular(Kerbin, 100_000), PayloadDetector.Scan(Carrier(3, engines: engines)), altitude, 0);

            Assert.True(plan.Ok, plan.Error);
            Assert.Equal(engines ? DeployMethod.Resonant : DeployMethod.CarrierPhasing, plan.Method);
            Assert.True(plan.MaxSpacingError < Angles.ToRadians(0.5), "spacing error " + Angles.ToDegrees(plan.MaxSpacingError) + " deg");
            double reference = plan.Releases.Max(r => r.NodeUt) + 1;
            var longitudes = plan.Releases.Select(r => r.Final.TrueLongitudeAt(reference)).OrderBy(x => x).ToList();
            for (int k = 1; k < 3; k++)
            {
                Assert.Equal(120, Angles.ToDegrees(longitudes[k] - longitudes[k - 1]), 0);
            }
        }

        [Fact]
        public void SpacingErrorsMeasureFlownOrbits()
        {
            var ring = Enumerable.Range(0, 3).Select(k => new OrbitElements(Kerbin.GravParameter, 3_463_334, 0, 0, 0, 0, k * 2 * Math.PI / 3 + (k == 2 ? 0.1 : 0), 0)).ToList();

            IReadOnlyList<double> errors = ConstellationPlanner.SpacingErrors(ring, 0, 1);

            Assert.Equal(0, errors[1], 6);
            Assert.Equal(0.1, errors[2], 6);
        }

        [Fact]
        public void BriefingWithChutesFliesTheCarrierHome()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;

            Briefing b = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude, null, EndOfLife.LandHome), new[] { Task(Kerbin, 100_000, Carrier(3)) }, 0);

            Assert.True(b.CanApprove, string.Join("; ", b.Contingencies.Select(c => c.Text)));
            Assert.Equal(ReturnKind.Land, b.Deployment!.Return.Kind);
            Assert.Equal(3, b.Burns.Count(ConstellationPlanner.IsRelease));
            Assert.Equal(new[] { "11", "21", "31" }, b.Burns.Where(x => x.Label.StartsWith("circularize")).Select(x => x.VesselId));
            Assert.Equal(ConstellationPlanner.ReturnLabel, b.Burns.Last().Label);
            Assert.Contains(b.Burns, x => x.Label == "release Relay Carrier Sat 2 at apoapsis, pass 2" || x.Label == "release Relay Carrier Sat 2 at periapsis, pass 2");
            Assert.All(b.Burns.Where(ConstellationPlanner.IsRelease), r => Assert.True(b.Burns.Single(x => x.VesselId == r.ReleasePart!.Value.ToString()).StartUt > r.StartUt));
        }

        [Fact]
        public void BriefingWithoutChutesOffersADisposalOrbit()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;

            Briefing b = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude, null, EndOfLife.LandHome), new[] { Task(Kerbin, 100_000, Carrier(3, chutes: false)) }, 0);

            Assert.True(b.CanApprove);
            Assert.Equal(ReturnKind.Disposal, b.Deployment!.Return.Kind);
            Contingency amber = Assert.Single(b.Contingencies, c => c.Severity == Severity.Amber);
            Assert.Contains("no parachutes", amber.Text);
            Assert.DoesNotContain(b.Burns, x => x.Label == ConstellationPlanner.ReturnLabel);
            Assert.Equal(ConstellationPlanner.SpacingLabel, b.Burns.Last().Label);
            OrbitElements disposal = b.Burns[b.Burns.Count - 2].Expected!;
            Assert.True(Math.Abs(disposal.SemiMajorAxis - (Kerbin.Radius + altitude)) > 100_000);
        }

        [Fact]
        public void ACarrierShortOfPropellantIsRed()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;

            Briefing b = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude), new[] { Task(Kerbin, 100_000, Carrier(3, tank: 0.5)) }, 0);

            Assert.False(b.CanApprove);
            Assert.Contains(b.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("propellant"));
        }

        [Fact]
        public void AReleasedPayloadTakesOverItsBurnsAndTheStepSurvivesASave()
        {
            double altitude = ConstellationPlanner.SynchronousAltitude(Kerbin)!.Value;
            Briefing b = BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude), new[] { Task(Kerbin, 100_000, Carrier(3)) }, 0);
            var op = new Operation("op-1", "Ring", OrbitChange.Deploy(altitude), new[] { "100" }, b.Burns);

            Operation rebound = op.Rebind("21", "200");
            Operation reloaded = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Ring", new[] { rebound }))).Operations[0];

            Assert.Contains(reloaded.Burns, x => x.VesselId == "200" && x.Label == "circularize Relay Carrier Sat 2");
            Assert.DoesNotContain(reloaded.Burns, x => x.VesselId == "21");
            Assert.Equal(new uint[] { 11, 21, 31 }, reloaded.Burns.Where(ConstellationPlanner.IsRelease).Select(x => x.ReleasePart!.Value));
            Assert.Equal(OrbitChangeKind.DeployConstellation, reloaded.Change.Kind);
            Assert.True(MissionJson.Read(MissionJson.Write(new Mission("m-2", "Home", new[] { new Operation("op-2", "Home", OrbitChange.HomeTo("Kerbin", 100_000), new[] { "100" }, Array.Empty<PlannedBurn>()) }))).Operations[0].Change.ThenLand);
        }
    }
}
