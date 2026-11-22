using System;
using System.Collections.Generic;
using FLTCOM.Core.Propulsion;
using FLTCOM.PlanCli;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class DeltaVEstimatorTests
    {
        private const double G0 = DeltaVEstimator.G0;
        private static readonly Propellant[] Biprop = { new Propellant("LiquidFuel", 0.9), new Propellant("Oxidizer", 1.1) };

        private static PartSnapshot Tank(int group, double units, double dry = 0.25) =>
            new PartSnapshot("tank", dry, group, new[] { new PartResource("LiquidFuel", units * 0.45, 0.005), new PartResource("Oxidizer", units * 0.55, 0.005) });

        private static PartSnapshot Engine(int group, int stage, double thrust, double isp, double mass = 0.5) =>
            new PartSnapshot("engine", mass, group, Array.Empty<PartResource>(), new EngineData(stage, thrust, isp, Biprop));

        [Fact]
        public void TwoStageStackMatchesTheRocketEquation()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1, 1));

            double upperWet = 0.1 + 0.0625 + 0.5 + 0.13;
            double upperDry = upperWet - 0.5;
            double lowerWet = upperWet + 0.04 + 0.25 + 2.0 + 0.5;
            Assert.Equal(2, stages.Count);
            Assert.Equal(1, stages[0].Stage);
            Assert.Equal(345 * G0 * Math.Log(lowerWet / (lowerWet - 2.0)), stages[0].DeltaV, 6);
            Assert.Equal(0, stages[1].Stage);
            Assert.Equal(320 * G0 * Math.Log(upperWet / upperDry), stages[1].DeltaV, 6);
            Assert.Equal(60 / (lowerWet * G0), stages[0].Twr(G0), 6);
            Assert.Equal(2.0 / (60 / (345 * G0)), stages[0].BurnTime, 6);
        }

        [Fact]
        public void ParallelBoosterStageEndsWhenTheBoostersFlameOut()
        {
            var parts = new[]
            {
                new PartSnapshot("probe", 0.5, -1, Array.Empty<PartResource>()),
                Tank(-1, 800),
                Engine(-1, 1, 200, 300),
                Tank(0, 400),
                Engine(0, 1, 200, 300),
            };

            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(parts);

            double ve = 300 * G0;
            double m0 = 0.5 + 0.25 + 4 + 0.5 + 0.25 + 2 + 0.5;
            double m1 = m0 - 4;
            double m2 = m1 - 0.25 - 0.5;
            double m3 = m2 - 2;
            Assert.Equal(2, stages.Count);
            Assert.Equal(ve * Math.Log(m0 / m1), stages[0].DeltaV, 6);
            Assert.Equal(ve * Math.Log(m2 / m3), stages[1].DeltaV, 6);
            Assert.Equal(400, stages[0].Thrust, 6);
        }

        [Fact]
        public void EngineWithoutItsPropellantGivesNoDeltaV()
        {
            var parts = new[] { Engine(-1, 0, 60, 345), new PartSnapshot("mono", 0.1, -1, new[] { new PartResource("MonoPropellant", 50, 0.004) }) };

            Assert.Empty(DeltaVEstimator.Estimate(parts));
        }

        [Fact]
        public void BurnTimingCentresTheDeltaVOnTheNode()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1, 1));
            StageDeltaV s = stages[0];
            double ve = s.ExhaustVelocity;
            double flow = s.MassFlow;

            BurnTiming timing = BurnTiming.Compute(stages, new StageCursor(0, 0), 1000);

            Assert.True(timing.Fits);
            Assert.Equal(s.StartMass / flow * (1 - Math.Exp(-1000 / ve)), timing.Duration, 6);
            Assert.Equal(s.StartMass / flow * (1 - Math.Exp(-500 / ve)), timing.TimeToHalf, 6);
            Assert.True(timing.TimeToHalf > timing.Duration / 2);
        }

        [Fact]
        public void BurnTimingCarriesAcrossStaging()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1, 1));
            double total = stages[0].DeltaV + stages[1].DeltaV;

            BurnTiming all = BurnTiming.Compute(stages, new StageCursor(0, 0), total);
            BurnTiming tooMuch = BurnTiming.Compute(stages, new StageCursor(0, 0), total + 10);

            Assert.True(all.Fits);
            Assert.Equal(stages[0].BurnTime + stages[1].BurnTime, all.Duration, 6);
            Assert.False(tooMuch.Fits);
        }

        [Fact]
        public void AutomaticBurnsDrawTheBottomStageFirstAndSpillOver()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1, 1));
            double first = stages[0].DeltaV;

            StageAllocation a = StageAllocation.Allocate(stages, new[] { new BurnDemand(first - 100), new BurnDemand(300) });

            Assert.Equal(first, a.Stages[0].Assigned, 6);
            Assert.Equal(200, a.Stages[1].Assigned, 6);
            Assert.Equal(0, a.Burns[1].Start.StageIndex);
            Assert.Equal(first - 100, a.Burns[1].Start.Used, 6);
            Assert.Equal(0, a.Uncovered);
        }

        [Fact]
        public void APinnedStageThatCannotCoverItsBurnIsShort()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(0.05, 1));

            StageAllocation a = StageAllocation.Allocate(stages, new[] { new BurnDemand(500, pinnedStage: 1) });

            Assert.True(a.Stages[0].Short);
            Assert.False(a.Burns[0].Covered);
            Assert.False(a.Stages[1].Short);
        }

        [Fact]
        public void PinningAnUpperStageStagesAwayTheLowerOne()
        {
            IReadOnlyList<StageDeltaV> stages = DeltaVEstimator.Estimate(Fixtures.TwoStageProbe(1, 1));

            StageAllocation a = StageAllocation.Allocate(stages, new[] { new BurnDemand(100, pinnedStage: 0), new BurnDemand(100, pinnedStage: 1) });

            Assert.Equal(0, a.Stages[0].Assigned);
            Assert.Equal(100, a.Stages[1].Assigned, 6);
            Assert.True(a.Burns[0].Covered);
            Assert.False(a.Burns[1].Covered);
        }
    }
}
