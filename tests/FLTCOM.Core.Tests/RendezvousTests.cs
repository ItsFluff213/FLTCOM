using System;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class RendezvousTests
    {
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767, 84_159_286);
        private const double Mu = 3.5316e12;
        private const double Now = 1000;

        private static OrbitElements CircularAhead(double radius, double inclination, double lan, double lead) =>
            new OrbitElements(Mu, radius, 0, inclination, lan, 0, lead, Now);

        /// <summary>Flies every planned burn on two-body orbits, then measures the gap at the last node.</summary>
        private static (double Distance, double Speed, double LowestPeriapsis) Fly(OrbitPlan plan, OrbitElements chaser, OrbitElements target)
        {
            Assert.True(plan.Ok, plan.Error);
            double lowest = chaser.PeriapsisRadius;
            foreach (ImpulsiveBurn burn in plan.Burns)
            {
                StateVector s = chaser.StateAt(burn.NodeUt);
                Vector3d dv = s.Prograde * burn.Prograde + s.Normal * burn.Normal + s.RadialOut * burn.Radial;
                chaser = OrbitElements.FromState(Mu, new StateVector(s.Position, s.Velocity + dv), burn.NodeUt);
                lowest = Math.Min(lowest, chaser.PeriapsisRadius);
            }
            double end = plan.Burns.Count > 0 ? plan.Burns[plan.Burns.Count - 1].NodeUt : Now;
            StateVector c = chaser.StateAt(end);
            StateVector g = target.StateAt(end);
            return ((c.Position - g.Position).Magnitude, (c.Velocity - g.Velocity).Magnitude, lowest);
        }

        [Fact]
        public void ATargetThirtyKilometresAheadInTheSameOrbitClosesToUnder100MetresAnd1MetrePerSecond()
        {
            const double r = 700_000;
            OrbitElements chaser = CircularAhead(r, 0, 0, 0);
            OrbitElements target = CircularAhead(r, 0, 0, 30_000 / r);

            OrbitPlan plan = Rendezvous.Plan(chaser, target, Kerbin, Now, Now + 60);
            var (distance, speed, lowest) = Fly(plan, chaser, target);

            Assert.InRange(distance, 0, 100);
            Assert.InRange(speed, 0, 1);
            Assert.True(lowest > 670_000, "the phasing orbit dips into the atmosphere");
            Assert.Equal(Rendezvous.HoldLabel, plan.Burns.Last().Label);
            Assert.Contains(plan.Burns, b => b.Label == "phasing orbit");
        }

        [Fact]
        public void ACoplanarTargetHalfAnOrbitAheadIsReachedAboveTheAtmosphere()
        {
            OrbitElements chaser = CircularAhead(680_000, 0, 0, 0);
            OrbitElements target = CircularAhead(700_000, 0, 0, Math.PI);

            OrbitPlan plan = Rendezvous.Plan(chaser, target, Kerbin, Now, Now + 60);
            var (distance, speed, lowest) = Fly(plan, chaser, target);

            Assert.InRange(distance, 0, Rendezvous.HoldDistance);
            Assert.InRange(speed, 0, Rendezvous.HoldSpeed);
            Assert.True(lowest > 675_000, "the path dips into the atmosphere");
            Assert.True(plan.Burns.Last().NodeUt - Now < 14 * chaser.Period, "phasing took too long");
        }

        [Fact]
        public void AFiveDegreePlaneChangeMatchesPlanesFirstThenCloses()
        {
            OrbitElements chaser = CircularAhead(680_000, 0, 0, 0);
            OrbitElements target = CircularAhead(720_000, Angles.ToRadians(5), 0.4, 2.0);

            OrbitPlan plan = Rendezvous.Plan(chaser, target, Kerbin, Now, Now + 60);
            var (distance, speed, _) = Fly(plan, chaser, target);

            Assert.Equal("match the target plane", plan.Burns[0].Label);
            Assert.InRange(distance, 0, Rendezvous.HoldDistance);
            Assert.InRange(speed, 0, Rendezvous.HoldSpeed);
        }

        [Fact]
        public void AnOpenWindowCostsUnderOnePointThreeTimesTheHohmannTransfer()
        {
            const double r1 = 680_000, r2 = 760_000;
            double hohmann = Math.Sqrt(Mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1) + Math.Sqrt(Mu / r2) * (1 - Math.Sqrt(2 * r1 / (r1 + r2)));
            double transfer = Math.PI * Math.Sqrt(Math.Pow((r1 + r2) / 2, 3) / Mu);
            double lead = Math.PI - Math.Sqrt(Mu / (r2 * r2 * r2)) * transfer;
            OrbitElements chaser = CircularAhead(r1, 0, 0, 0);
            OrbitElements target = CircularAhead(r2, 0, 0, lead + 0.3);

            OrbitPlan plan = Rendezvous.Plan(chaser, target, Kerbin, Now, Now + 60);
            var (distance, _, _) = Fly(plan, chaser, target);

            Assert.InRange(distance, 0, Rendezvous.HoldDistance);
            Assert.Contains(plan.Burns, b => b.Label == "transfer to the target");
            Assert.InRange(plan.TotalDeltaV, hohmann, 1.3 * hohmann);
        }

        [Fact]
        public void AChaserAlreadyHoldingPlansNothing()
        {
            OrbitElements chaser = CircularAhead(700_000, 0, 0, 0);
            OrbitElements target = CircularAhead(700_000, 0, 0, 20 / 700_000.0);

            OrbitPlan plan = Rendezvous.Plan(chaser, target, Kerbin, Now, Now + 60);

            Assert.True(plan.Ok);
            Assert.Empty(plan.Burns);
        }

        private static VesselTask Task(string id, OrbitElements orbit, double deltaV) =>
            new VesselTask(id, id, Kerbin, orbit, new[] { new StageDeltaV(0, 10_000, 5_000, 60_000, deltaV, 300) });

        private static readonly OrbitChange MeetStation = OrbitChange.RendezvousWith(new RendezvousTarget("station", "Kerbin Station"));

        [Fact]
        public void TheBriefingTimesEveryBurnAndEndsHolding()
        {
            OrbitElements target = CircularAhead(700_000, 0, 0, 0.5);

            Briefing briefing = BriefingBuilder.Build("Op", MeetStation, new[] { Task("ship", CircularAhead(680_000, 0, 0, 0), 2_000) }, Now, target: Task("station", target, 0));

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            Assert.All(briefing.Burns, b => Assert.True(b.StartUt >= Now));
            Assert.Equal(Rendezvous.HoldLabel, briefing.Burns.Last().Label);
            Assert.Equal("rendezvous with Kerbin Station", briefing.Change);
        }

        [Fact]
        public void AnUnaffordableRendezvousIsRed()
        {
            OrbitElements target = CircularAhead(720_000, Angles.ToRadians(5), 0.4, 2.0);

            Briefing briefing = BriefingBuilder.Build("Op", MeetStation, new[] { Task("ship", CircularAhead(680_000, 0, 0, 0), 100) }, Now, target: Task("station", target, 0));

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("short of delta-v"));
        }

        [Fact]
        public void AMissingTargetIsRed()
        {
            Briefing briefing = BriefingBuilder.Build("Op", MeetStation, new[] { Task("ship", CircularAhead(680_000, 0, 0, 0), 2_000) }, Now);

            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("Kerbin Station"));
        }

        [Fact]
        public void ARendezvousSurvivesTheSave()
        {
            var change = OrbitChange.RendezvousWith(new RendezvousTarget("4242", "Kerbin Station", 99));
            var op = new Operation("op-1", "Bright Kite", change, new[] { "v1" }, new[] { new PlannedBurn("v1", Rendezvous.HoldLabel, 100, 90, 20, 5, 0, 0) }, StepState.Briefed, null);

            Operation back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Meet", new[] { op }))).Operations[0];

            Assert.Equal(OrbitChangeKind.Rendezvous, back.Change.Kind);
            Assert.Equal("4242", back.Change.Rendezvous!.VesselId);
            Assert.Equal("Kerbin Station", back.Change.Rendezvous.Name);
            Assert.Equal(99u, back.Change.Rendezvous.PortId);
        }

        [Fact]
        public void ADockFliesTheRendezvousThenDocksWithRcs()
        {
            var dock = OrbitChange.DockWith(new RendezvousTarget("station", "Kerbin Station", 7));

            Briefing briefing = BriefingBuilder.Build("Op", dock, new[] { Task("ship", CircularAhead(680_000, 0, 0, 0), 2_000) }, Now, target: Task("station", CircularAhead(700_000, 0, 0, 0.5), 0));

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            Assert.Equal(new[] { Rendezvous.HoldLabel, DockingApproach.Label }, briefing.Burns.Skip(briefing.Burns.Count - 2).Select(b => b.Label));
            Assert.True(briefing.Burns.Last().StartUt >= briefing.Burns[briefing.Burns.Count - 2].StartUt);
            Assert.Equal(0, briefing.Burns.Last().DeltaV);
            Assert.Equal("dock with Kerbin Station", briefing.Change);
        }

        [Fact]
        public void AChaserAlreadyHoldingOnlyDocks()
        {
            var dock = OrbitChange.DockWith(new RendezvousTarget("station", "Kerbin Station"));

            Briefing briefing = BriefingBuilder.Build("Op", dock, new[] { Task("ship", CircularAhead(700_000, 0, 0, 0), 2_000) }, Now, target: Task("station", CircularAhead(700_000, 0, 0, 20 / 700_000.0), 0));

            Assert.Equal(DockingApproach.Label, Assert.Single(briefing.Burns).Label);
        }

        [Fact]
        public void ADockSurvivesTheSaveAndHoldsItsPort()
        {
            var change = OrbitChange.DockWith(new RendezvousTarget("4242", "Kerbin Station", 99));
            var op = new Operation("op-1", "Bright Kite", change, new[] { "v1" }, new[] { DockingApproach.Burn("v1", 100) }, StepState.Executing, null);

            Operation back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Dock", new[] { op }))).Operations[0];

            Assert.Equal(OrbitChangeKind.Dock, back.Change.Kind);
            Assert.Equal(99u, back.Change.Rendezvous!.PortId);
            Assert.True(PortReservations.Holds(back));
        }

        [Fact]
        public void LambertReachesThePointInTheGivenTime()
        {
            OrbitElements orbit = CircularAhead(700_000, 0.2, 0.1, 0);
            StateVector a = orbit.StateAt(Now);
            StateVector b = orbit.StateAt(Now + 300);

            Vector3d? v = Rendezvous.Lambert(a.Position, b.Position, 300, Mu, a.Normal);

            Assert.NotNull(v);
            Assert.InRange((v!.Value - a.Velocity).Magnitude, 0, 0.01);
        }
    }
}
