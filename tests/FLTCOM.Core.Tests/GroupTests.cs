using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Groups;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using FLTCOM.PlanCli;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class GroupTests
    {
        private static VesselTask Relay(int i, double meanAnomaly, double periapsisKm = 640, double apoapsisKm = 760, double inclinationDeg = 0.5)
        {
            OrbitElements o = Fixtures.Orbit(periapsisKm, apoapsisKm, inclinationDeg);
            var orbit = new OrbitElements(o.Mu, o.SemiMajorAxis, o.Eccentricity, o.Inclination, 0.3, 0.2, meanAnomaly, Fixtures.Epoch);
            return new VesselTask("relay-" + i, "Relay " + i, Fixtures.Kerbin, orbit, DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1.0, 1.0)));
        }

        private static IReadOnlyList<VesselTask> Cluster(double inclinationDeg = 0.5) => new[] { Relay(1, 0.10, inclinationDeg: inclinationDeg), Relay(2, 0.35, inclinationDeg: inclinationDeg), Relay(3, 0.50, inclinationDeg: inclinationDeg), Relay(4, 0.95, inclinationDeg: inclinationDeg) };

        [Fact]
        public void GroupsAreCreatedRenamedEditedAndDeleted()
        {
            var groups = new FleetGroups();

            Assert.Null(groups.Create("Relays", new[] { "1", "2" }, out FleetGroup? relays));
            Assert.NotNull(groups.Create("relays", new[] { "3" }, out _));
            Assert.NotNull(groups.Create(" ", new[] { "3" }, out _));
            Assert.Null(groups.Add(relays!.Id, new[] { "2", "3" }));
            Assert.Null(groups.Remove(relays.Id, new[] { "1" }));
            Assert.Null(groups.Rename(relays.Id, "Keostationary"));

            FleetGroup edited = Assert.Single(groups.All);
            Assert.Equal("Keostationary", edited.Name);
            Assert.Equal(new[] { "2", "3" }, edited.VesselIds);

            Assert.Null(groups.Delete(relays.Id));
            Assert.Empty(groups.All);
            Assert.NotNull(groups.Delete(relays.Id));
        }

        [Fact]
        public void GroupsSurviveTheSaveRoundTrip()
        {
            var groups = new FleetGroups();
            groups.Create("Relays", new[] { "11", "12", "13" }, out _);
            groups.Create("Tugs \"heavy\"", new[] { "21" }, out _);

            IReadOnlyList<FleetGroup> back = FleetGroups.Read(FleetGroups.Write(groups.All));
            var loaded = new FleetGroups();
            loaded.Load(back);

            Assert.Equal(groups.All.Select(g => (g.Id, g.Name, string.Join(",", g.VesselIds))), loaded.All.Select(g => (g.Id, g.Name, string.Join(",", g.VesselIds))));
            Assert.Null(loaded.Create("Third", new[] { "31" }, out FleetGroup? third));
            Assert.DoesNotContain(groups.All, g => g.Id == third!.Id);
        }

        [Fact]
        public void TheSameChangeRaisesEveryApoapsisByTheSameAmount()
        {
            IReadOnlyList<VesselTask> tasks = Cluster();

            var plans = GroupPlanner.Plan(OrbitChange.RaiseApoapsisBy(50_000), tasks, Fixtures.Epoch);

            foreach (VesselTask task in tasks)
            {
                OrbitPlan plan = plans[task.VesselId];
                Assert.True(plan.Ok, plan.Error);
                Assert.Equal(task.Orbit.ApoapsisRadius + 50_000, plan.Final!.ApoapsisRadius, 0);
            }
        }

        [Fact]
        public void TheSameOrbitPutsEveryVesselOnOneCircle()
        {
            IReadOnlyList<VesselTask> tasks = Cluster();
            double radius = Fixtures.Kerbin.Radius + 700_000;

            var plans = GroupPlanner.Plan(OrbitChange.CircularAt(700_000), tasks, Fixtures.Epoch);

            foreach (OrbitPlan plan in plans.Values)
            {
                Assert.True(plan.Ok, plan.Error);
                Assert.Equal(radius, plan.Final!.SemiMajorAxis, 0);
                Assert.True(plan.Final.Eccentricity < 1e-6);
            }
        }

        [Theory]
        [InlineData(0.10, 0.35, 0.50, 0.95)]
        [InlineData(0.00, 0.01, 0.02, 0.03)]
        [InlineData(5.90, 0.20, 3.00, 3.30)]
        public void SpreadEvenlyLeavesTheGroupEquallySpacedOnTheTargetCircle(double m1, double m2, double m3, double m4)
        {
            var tasks = new[] { Relay(1, m1), Relay(2, m2), Relay(3, m3), Relay(4, m4) };
            double radius = Fixtures.Kerbin.Radius + 700_000;

            var plans = GroupPlanner.Plan(OrbitChange.Spread(700_000), tasks, Fixtures.Epoch);

            Assert.All(plans.Values, p => Assert.True(p.Ok, p.Error));
            double after = plans.Values.Max(p => p.Burns.Max(b => b.NodeUt)) + 1;
            var longitudes = plans.Values.Select(p => Angles.ToDegrees(p.Final!.TrueLongitudeAt(after))).OrderBy(x => x).ToList();
            for (int i = 0; i < longitudes.Count; i++)
            {
                double gap = (longitudes[(i + 1) % longitudes.Count] - longitudes[i] + 360) % 360;
                Assert.InRange(gap, 89.9, 90.1);
            }
            foreach (OrbitPlan plan in plans.Values)
            {
                Assert.InRange(plan.Final!.SemiMajorAxis / radius, 0.9999, 1.0001);
                Assert.True(plan.Final.Eccentricity < 1e-4, "e = " + plan.Final.Eccentricity);
            }
        }

        [Fact]
        public void SpreadEvenlyPhasesAboveTheAtmosphere()
        {
            var tasks = new[] { Relay(1, 0.0, 80, 90), Relay(2, 0.05, 80, 90), Relay(3, 0.1, 80, 90) };

            var plans = GroupPlanner.Plan(OrbitChange.Spread(85_000), tasks, Fixtures.Epoch);

            foreach (OrbitPlan plan in plans.Values.Where(p => p.Ok))
            {
                foreach (ImpulsiveBurn burn in plan.Burns.Where(b => b.OrbitAfter != null))
                {
                    Assert.True(burn.OrbitAfter!.PeriapsisRadius >= Fixtures.Kerbin.Radius + Fixtures.Kerbin.AtmosphereHeight, burn.Label);
                }
            }
        }

        [Fact]
        public void GroupBurnsNeverOverlapAcrossVessels()
        {
            Briefing briefing = BriefingBuilder.Build("Even Ring", OrbitChange.Spread(700_000), Cluster(), Fixtures.Epoch);

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            Assert.Equal(4, briefing.Burns.Select(b => b.VesselId).Distinct().Count());
            var burns = briefing.Burns.OrderBy(b => b.StartUt).ToList();
            for (int i = 1; i < burns.Count; i++)
            {
                if (burns[i].VesselId != burns[i - 1].VesselId)
                {
                    Assert.True(burns[i].StartUt >= burns[i - 1].EndUt, burns[i - 1].Label + " overlaps " + burns[i].Label);
                }
            }
            Assert.Equal(burns.Select(b => b.StartUt), briefing.Burns.Select(b => b.StartUt));
            Assert.All(briefing.Burns, b => Assert.NotNull(b.OrbitAfter));
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(90)]
        [InlineData(179.5)]
        public void ASpreadFlownOnRailsEndsEvenlySpacedOnTheTargetOrbit(double inclinationDeg)
        {
            IReadOnlyList<VesselTask> tasks = Cluster(inclinationDeg);
            Briefing briefing = BriefingBuilder.Build("Even Ring", OrbitChange.Spread(700_000), tasks, Fixtures.Epoch);
            double radius = Fixtures.Kerbin.Radius + 700_000;
            var finals = new List<OrbitElements>();

            foreach (VesselTask task in tasks)
            {
                OrbitElements orbit = task.Orbit;
                var parts = Fixtures.TwoStageProbe(1.0, 1.0);
                Execution.Thruster thruster = Execution.BackgroundBurn.Thruster(parts, out _)!;
                double mass = thruster.Mass;
                foreach (PlannedBurn burn in briefing.Burns.Where(b => b.VesselId == task.VesselId))
                {
                    var now = new Execution.Thruster(thruster.Stage, thruster.Thrust, thruster.MassFlow, mass, thruster.Propellant - (thruster.Mass - mass), thruster.Shares);
                    Execution.BurnStep step = Execution.BackgroundBurn.Step(orbit, Execution.BackgroundBurn.Direction(orbit, burn), now, burn.StartUt, burn.StartUt + 4 * burn.Duration + 60, burn.DeltaV);
                    Assert.True(step.Done, burn.Label);
                    orbit = step.Orbit;
                    mass -= step.MassUsed;
                }
                finals.Add(orbit);
            }

            double after = briefing.Burns.Max(b => b.EndUt) + 1;
            Vector3d normal = RingPlane.MeanNormal(finals);
            var longitudes = finals.Select(o => Angles.ToDegrees(RingPlane.ArgumentOfLatitude(o, after, normal))).OrderBy(x => x).ToList();
            for (int i = 0; i < longitudes.Count; i++)
            {
                double gap = (longitudes[(i + 1) % longitudes.Count] - longitudes[i] + 360) % 360;
                Assert.InRange(gap, 89.8, 90.2);
            }
            Assert.All(finals, o => Assert.InRange(o.SemiMajorAxis / radius, 0.999, 1.001));
            Assert.All(finals, o => Assert.InRange(o.ApoapsisRadius / radius, 0.99, 1.01));
        }

        [Fact]
        public void AMissedBurnMovesWholeOrbitsAndKeepsItsSlot()
        {
            var remaining = new[]
            {
                new PlannedBurn("a", "phase a", 1000, 990, 20, 10, 0, 0),
                new PlannedBurn("b", "phase b", 1300, 1290, 20, 10, 0, 0),
                new PlannedBurn("a", "circularize a", 9000, 8990, 20, -10, 0, 0),
            };
            double period = 2000;

            var plain = GroupPlanner.MissedShift(remaining, "a", period, null, 1100);
            var slotted = GroupPlanner.MissedShift(remaining, "a", period, Angles.TwoPi / (period * 1.25), 1100);

            Assert.Equal(period, plain!.Value.Seconds);
            Assert.Equal(5 * period, slotted!.Value.Seconds);
            Assert.True(slotted.Value.PhaseError < 1e-9);
        }

        [Fact]
        public void AnOrbitCloseToThePlannedOneCountsAsFlown()
        {
            OrbitElements planned = Fixtures.Orbit(700, 700, 0.5);
            OrbitElements near = Fixtures.Orbit(698, 703, 0.55);
            OrbitElements far = Fixtures.Orbit(640, 760, 0.5);

            Assert.True(GroupPlanner.Matches(near, planned));
            Assert.False(GroupPlanner.Matches(far, planned));
        }

        [Fact]
        public void ABriefingGateBlocksApproval()
        {
            var gate = new Contingency(Severity.Red, "relay-2", "relay-2: no engine");

            Briefing briefing = BriefingBuilder.Build("Gated Ring", OrbitChange.Spread(700_000), Cluster(), Fixtures.Epoch, gates: new[] { gate });

            Assert.False(briefing.CanApprove);
            Assert.Equal(gate.Text, briefing.Contingencies[0].Text);
        }

        [Fact]
        public void PlannedOrbitsSurviveTheMissionJson()
        {
            Briefing briefing = BriefingBuilder.Build("Even Ring", OrbitChange.Spread(700_000), Cluster(), Fixtures.Epoch);
            var op = new Operation("op-1", "Even Ring", OrbitChange.Spread(700_000), briefing.Burns.Select(b => b.VesselId).Distinct().ToList(), briefing.Burns, StepState.Waiting);

            Mission back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Ring", new[] { op })));

            PlannedBurn burn = back.Operations[0].Burns.Last();
            Assert.Equal(briefing.Burns.Last().OrbitAfter!.SemiMajorAxis, burn.OrbitAfter!.SemiMajorAxis);
            Assert.Equal(OrbitChangeKind.SpreadEvenly, back.Operations[0].Change.Kind);
        }
    }
}
