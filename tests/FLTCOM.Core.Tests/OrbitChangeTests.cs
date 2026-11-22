using System;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using FLTCOM.PlanCli;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class OrbitChangeTests
    {
        private static readonly CentralBody Kerbin = Fixtures.Kerbin;
        private const double Mu = 3.5316e12;

        [Fact]
        public void HohmannRaiseFrom80To250KmMatchesTheTextbook()
        {
            double r1 = 680_000, r2 = 850_000;
            double dv1 = Math.Sqrt(Mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1);
            double dv2 = Math.Sqrt(Mu / r2) * (1 - Math.Sqrt(2 * r1 / (r1 + r2)));

            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.CircularAt(250_000), OrbitElements.Circular(Mu, r1, 0, 0), Kerbin, 0);

            Assert.True(plan.Ok);
            Assert.Equal(2, plan.Burns.Count);
            Assert.InRange(plan.Burns[0].Prograde, dv1 - 0.1, dv1 + 0.1);
            Assert.InRange(plan.Burns[1].Prograde, dv2 - 0.1, dv2 + 0.1);
            double transferHalfPeriod = Math.PI * Math.Sqrt(Math.Pow((r1 + r2) / 2, 3) / Mu);
            Assert.InRange(plan.Burns[1].NodeUt - plan.Burns[0].NodeUt, transferHalfPeriod - 0.5, transferHalfPeriod + 0.5);
            Assert.InRange(plan.Final!.PeriapsisRadius, r2 - 1, r2 + 1);
            Assert.InRange(plan.Final.ApoapsisRadius, r2 - 1, r2 + 1);
        }

        [Fact]
        public void CircularizeLkoEllipticMatchesAHandCalculation()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");
            double ra = 750_000, a = 715_000;
            double hand = Math.Sqrt(Mu / ra) - Math.Sqrt(Mu * (2 / ra - 1 / a));

            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.Circularize(), task.Orbit, Kerbin, Fixtures.Epoch);

            ImpulsiveBurn burn = Assert.Single(plan.Burns);
            Assert.InRange(burn.DeltaV, hand - 0.5, hand + 0.5);
            Assert.InRange(task.Orbit.StateAt(burn.NodeUt).Position.Magnitude, ra - 1, ra + 1);
            Assert.True(burn.NodeUt > Fixtures.Epoch);
        }

        [Fact]
        public void InclinationChangeBurnsAtANodeWithTheTextbookDeltaV()
        {
            OrbitElements orbit = OrbitElements.Circular(Mu, 700_000, 0, 0);
            double target = Angles.ToRadians(10);
            double formula = 2 * Math.Sqrt(Mu / 700_000) * Math.Sin(target / 2);

            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.Inclination(target), orbit, Kerbin, 100);

            ImpulsiveBurn burn = Assert.Single(plan.Burns);
            Assert.InRange(burn.DeltaV, formula - 0.1, formula + 0.1);
            Assert.InRange(plan.Final!.Inclination, target - 1e-6, target + 1e-6);
            Vector3d node = plan.Final.StateAt(burn.NodeUt).Position;
            Assert.InRange(Math.Abs(node.Z), 0, 1e-3);
        }

        [Fact]
        public void InclinationChangeOnAnInclinedOrbitBurnsAtItsNode()
        {
            var orbit = new OrbitElements(Mu, 720_000, 0.01, Angles.ToRadians(20), 1.0, 2.0, 0.5, 0);

            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.Inclination(Angles.ToRadians(5)), orbit, Kerbin, 0);

            ImpulsiveBurn burn = Assert.Single(plan.Burns);
            Assert.InRange(Math.Abs(orbit.StateAt(burn.NodeUt).Position.Z), 0, 1e-3);
            Assert.InRange(Angles.ToDegrees(plan.Final!.Inclination), 5 - 1e-6, 5 + 1e-6);
        }

        [Fact]
        public void StandardLowOrbitEndsCircularEquatorialAboveTheAtmosphere()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");

            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.StandardLow(), task.Orbit, Kerbin, Fixtures.Epoch);

            Assert.True(plan.Ok);
            Assert.InRange(plan.Final!.Inclination, 0, 1e-6);
            Assert.InRange(plan.Final.PeriapsisRadius - Kerbin.Radius, 79_999, 80_001);
            Assert.InRange(plan.Final.ApoapsisRadius - Kerbin.Radius, 79_999, 80_001);
        }

        [Theory]
        [InlineData("Kerbin", 70_000, 6_764.1, 80_000)]
        [InlineData("Mun", 0, 7_061, 17_061)]
        [InlineData("Eve", 90_000, 7_526, 100_000)]
        [InlineData("Minmus", 0, 5_725, 15_725)]
        public void StandardLowOrbitAltitudeFollowsTheRule(string name, double atmosphere, double terrain, double expected)
        {
            var body = new CentralBody(name, 1, 1, atmosphere, terrain);

            Assert.Equal(expected, StandardOrbit.LowOrbitAltitude(body), 6);
        }

        [Fact]
        public void PlannerRefusesImpossibleChanges()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");

            Assert.False(OrbitPlanner.Plan(OrbitChange.Apoapsis(10_000), task.Orbit, Kerbin, 0).Ok);
            Assert.False(OrbitPlanner.Plan(OrbitChange.Periapsis(500_000), task.Orbit, Kerbin, 0).Ok);
            Assert.False(OrbitPlanner.Plan(OrbitChange.Apoapsis(1e12), task.Orbit, Kerbin, 0).Ok);
        }

        [Fact]
        public void StateVectorsRoundTripThroughElements()
        {
            var orbit = new OrbitElements(Mu, 900_000, 0.2, 0.4, 1.1, 2.2, 0.3, 50);

            OrbitElements back = OrbitElements.FromState(Mu, orbit.StateAt(1234), 1234);

            Assert.Equal(orbit.SemiMajorAxis, back.SemiMajorAxis, 3);
            Assert.Equal(orbit.Eccentricity, back.Eccentricity, 9);
            Assert.Equal(orbit.Inclination, back.Inclination, 9);
            Assert.Equal(orbit.LongitudeOfAscendingNode, back.LongitudeOfAscendingNode, 9);
            Assert.Equal(orbit.ArgumentOfPeriapsis, back.ArgumentOfPeriapsis, 9);
            Assert.Equal(orbit.StateAt(5000).Position.X, back.StateAt(5000).Position.X, 3);
        }

        [Theory]
        [InlineData(100, 1.9, CorrectionVerdict.FlyInside)]
        [InlineData(100, 2.1, CorrectionVerdict.NeedsBriefing)]
        [InlineData(500, 4.9, CorrectionVerdict.FlyInside)]
        [InlineData(500, 5.1, CorrectionVerdict.NeedsBriefing)]
        public void CorrectionsUnderTheThresholdFlyInsideTheOperation(double burn, double residual, CorrectionVerdict expected)
        {
            Assert.Equal(expected, CorrectionRule.Classify(burn, residual));
        }

        [Fact]
        public void MissedBurnReplansToTheNextWindowWhenDeltaVFits()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");
            double late = Fixtures.Epoch + 5000;

            ReplanResult fits = MissedBurn.Replan(OrbitChange.Circularize(), task.Orbit, Kerbin, 100, late);
            ReplanResult missed = MissedBurn.Replan(OrbitChange.Circularize(), task.Orbit, Kerbin, 10, late);

            Assert.False(fits.Missed);
            Assert.True(fits.Plan!.Burns[0].NodeUt > late);
            Assert.True(missed.Missed);
            Assert.Contains("m/s", missed.Reason);
        }
    }
}
