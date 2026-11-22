using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using FLTCOM.PlanCli;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class BackgroundBurnTests
    {
        private static readonly OrbitElements Circle = Fixtures.Orbit(700, 700, 0);

        [Fact]
        public void TheNextStageFiresWithItsRealThrustAndFlow()
        {
            Thruster thruster = BackgroundBurn.Thruster(Fixtures.TwoStageProbe(1.0, 1.0), out string? refusal)!;

            Assert.Null(refusal);
            Assert.Equal(60, thruster.Thrust);
            Assert.Equal(345 * DeltaVEstimator.G0, thruster.ExhaustVelocity, 6);
            Assert.Equal(1.0, thruster.Shares.Values.Sum(), 9);
            Assert.True(thruster.Shares[(0, "Oxidizer")] > thruster.Shares[(0, "LiquidFuel")]);
        }

        [Fact]
        public void NoEngineAndNoFuelAreRefusedByName()
        {
            var probe = Fixtures.TwoStageProbe(1.0, 1.0);

            Assert.Null(BackgroundBurn.Thruster(probe.Where(p => p.Engine == null).ToList(), out string? noEngine));
            Assert.Equal("no engine", noEngine);
            Assert.Null(BackgroundBurn.Thruster(Fixtures.TwoStageProbe(0, 0), out string? noFuel));
            Assert.Equal("no fuel", noFuel);
        }

        [Fact]
        public void APrograde100MetreBurnFliesItsDeltaVAndBurnsTheRocketEquationMass()
        {
            Thruster thruster = BackgroundBurn.Thruster(Fixtures.TwoStageProbe(1.0, 1.0), out _)!;
            double node = Fixtures.Epoch + 600;
            var burn = new PlannedBurn("relay-1", "raise", node, node - 20, 40, 100, 0, 0);
            Vector3d direction = BackgroundBurn.Direction(Circle, burn);

            BurnStep step = BackgroundBurn.Step(Circle, direction, thruster, burn.StartUt, burn.StartUt + 600, 100);

            Assert.True(step.Done);
            Assert.False(step.OutOfFuel);
            Assert.Equal(100, step.DeltaV, 6);
            Assert.Equal(thruster.Mass * (1 - Math.Exp(-100 / thruster.ExhaustVelocity)), step.MassUsed, 9);
            double v = Circle.SpeedAtRadius(Circle.SemiMajorAxis);
            double impulsiveApoapsis = OrbitElements.FromState(Circle.Mu, new StateVector(new Vector3d(Circle.SemiMajorAxis, 0, 0), new Vector3d(0, v + 100, 0)), 0).ApoapsisRadius;
            Assert.InRange(step.Orbit.ApoapsisRadius / impulsiveApoapsis, 0.995, 1.005);
            Assert.True(step.Ut < burn.StartUt + 600);
        }

        [Fact]
        public void ABurnSplitIntoFramesEndsWhereOneLongStepEnds()
        {
            Thruster thruster = BackgroundBurn.Thruster(Fixtures.TwoStageProbe(1.0, 1.0), out _)!;
            var burn = new PlannedBurn("relay-1", "raise", Fixtures.Epoch + 600, Fixtures.Epoch + 580, 40, 80, 0, 0);
            Vector3d direction = BackgroundBurn.Direction(Circle, burn);
            BurnStep whole = BackgroundBurn.Step(Circle, direction, thruster, burn.StartUt, burn.StartUt + 600, 80);

            OrbitElements orbit = Circle;
            double ut = burn.StartUt;
            double flown = 0;
            double mass = thruster.Mass;
            while (flown < 80 - 1e-6)
            {
                var now = new Thruster(thruster.Stage, thruster.Thrust, thruster.MassFlow, mass, thruster.Propellant - (thruster.Mass - mass), thruster.Shares);
                BurnStep frame = BackgroundBurn.Step(orbit, direction, now, ut, ut + 0.37, 80 - flown);
                orbit = frame.Orbit;
                ut = frame.Ut;
                flown += frame.DeltaV;
                mass -= frame.MassUsed;
            }

            Assert.InRange(orbit.SemiMajorAxis - whole.Orbit.SemiMajorAxis, -50, 50);
            Assert.Equal(whole.MassUsed, thruster.Mass - mass, 6);
        }

        [Fact]
        public void AnEmptyStageStopsTheBurnShort()
        {
            Thruster thruster = BackgroundBurn.Thruster(Fixtures.TwoStageProbe(0.02, 0), out _)!;
            var burn = new PlannedBurn("relay-1", "raise", Fixtures.Epoch + 600, Fixtures.Epoch + 580, 40, 2000, 0, 0);

            BurnStep step = BackgroundBurn.Step(Circle, BackgroundBurn.Direction(Circle, burn), thruster, burn.StartUt, burn.StartUt + 3600, 2000);

            Assert.True(step.OutOfFuel);
            Assert.False(step.Done);
            Assert.Equal(thruster.Propellant, step.MassUsed, 9);
            Assert.True(step.DeltaV < 2000);
        }

        [Fact]
        public void ABurnThatNeedsBothStagesStagesOnRailsAndCompletes()
        {
            var stages = DeltaVEstimator.Estimate(Probe());
            double need = stages[0].DeltaV + stages[1].DeltaV / 2;

            Flight flight = Fly(need, autoSystems: true);

            Assert.Null(flight.Stop);
            Assert.Equal(need, flight.Flown, 3);
            Assert.Equal(new[] { 1, 0 }, flight.Stages);
            Assert.All(flight.Parts.Where(p => p.DecoupledIn == 0).SelectMany(p => p.Resources), r => Assert.True(r.Amount < 1e-6, r.Name + " left in the spent stage"));
            Assert.InRange(UpperFuel(flight.Parts) / UpperFuel(Probe()), 0.3, 0.7);
        }

        [Fact]
        public void WithAutoSystemsOffTheBurnStopsAtTheDryStageWithTheShortfall()
        {
            var stages = DeltaVEstimator.Estimate(Probe());
            double need = stages[0].DeltaV + stages[1].DeltaV / 2;

            Flight flight = Fly(need, autoSystems: false);

            Assert.Contains("AUTO SYSTEMS is off", flight.Stop);
            Assert.Equal(stages[0].DeltaV, flight.Flown, 3);
            Assert.Equal(new[] { 1 }, flight.Stages);
            Assert.Equal(UpperFuel(Probe()), UpperFuel(flight.Parts), 9);
        }

        [Fact]
        public void WithNoStageLeftTheBurnStopsOutOfFuel()
        {
            var stages = DeltaVEstimator.Estimate(Probe());

            Flight flight = Fly(stages.Sum(s => s.DeltaV) + 500, autoSystems: true);

            Assert.Equal("no fuel", flight.Stop);
            Assert.Equal(stages.Sum(s => s.DeltaV), flight.Flown, 3);
        }

        [Fact]
        public void BackgroundStagingObeysTheStagersHolds()
        {
            var coreBelow = ProbeStages.Select(p => p.Is(PartRole.Command) ? new StagePart(p.Name, p.Roles, p.Stage, 0) : p).ToList();
            var chuteNext = ProbeStages.Append(new StagePart("parachuteSingle", PartRole.Parachute, 0, -1)).ToList();

            Assert.Null(BackgroundStaging.Refusal(ProbeStages, 0, autoSystems: true));
            Assert.Null(BackgroundStaging.Refusal(ProbeStages, 1, autoSystems: false));
            Assert.Contains("probe core", BackgroundStaging.Refusal(coreBelow, 0, autoSystems: true));
            Assert.Contains("parachutes", BackgroundStaging.Refusal(chuteNext, 0, autoSystems: true));
        }

        [Fact]
        public void OnlyASpentEngineOrTankStillAttachedMeansStaging()
        {
            var releasedPayload = new[]
            {
                new StagePart("probeCoreOcto", PartRole.Command, -1, -1),
                new StagePart("ionEngine", PartRole.Engine, 0, -1),
                new StagePart("Decoupler.1", PartRole.Decoupler, 3, 3),
            };

            Assert.False(BackgroundStaging.Needed(ProbeStages, 1));
            Assert.True(BackgroundStaging.Needed(ProbeStages, 0));
            Assert.False(BackgroundStaging.Needed(releasedPayload, 0));
            Assert.Null(BackgroundStaging.Refusal(releasedPayload, 0, autoSystems: false));
        }

        /// <summary>The two-stage probe with enough fuel for about 280 m/s per stage, so the whole burn stays on a closed orbit.</summary>
        private static IReadOnlyList<PartSnapshot> Probe() => Fixtures.TwoStageProbe(0.05, 0.05);

        private static readonly IReadOnlyList<StagePart> ProbeStages = new[]
        {
            new StagePart("probeCoreOcto", PartRole.Command, -1, -1),
            new StagePart("fuelTankSmallFlat", PartRole.Tank, -1, -1),
            new StagePart("liquidEngineMini.v2", PartRole.Engine, 0, -1),
            new StagePart("Decoupler.1", PartRole.Decoupler, 0, 0),
            new StagePart("fuelTankSmall", PartRole.Tank, -1, 0),
            new StagePart("liquidEngine3.v2", PartRole.Engine, 1, 0),
        };

        private sealed class Flight
        {
            public double Flown;
            public string? Stop;
            public List<int> Stages = new List<int>();
            public IReadOnlyList<PartSnapshot> Parts = Array.Empty<PartSnapshot>();
        }

        /// <summary>Flies the probe frame by frame as the background executor does, draining its tanks after each frame.</summary>
        private static Flight Fly(double deltaV, bool autoSystems)
        {
            var flight = new Flight { Parts = Probe() };
            var burn = new PlannedBurn("relay-1", "raise", Fixtures.Epoch + 600, Fixtures.Epoch + 580, 40, deltaV, 0, 0);
            Vector3d direction = BackgroundBurn.Direction(Circle, burn);
            OrbitElements orbit = Circle;
            double ut = burn.StartUt;
            for (double now = ut + 0.5; now < ut + 20_000; now += 0.5)
            {
                BurnFrame frame = BackgroundBurn.Frame(flight.Parts, ProbeStages, autoSystems, orbit, direction, ut, now, deltaV - flight.Flown);
                if (frame.Stop != null)
                {
                    flight.Stop = frame.Stop;
                    return flight;
                }
                if (!flight.Stages.Contains(frame.Thruster!.Stage))
                {
                    flight.Stages.Add(frame.Thruster.Stage);
                }
                flight.Flown += frame.Step!.DeltaV;
                orbit = frame.Step.Orbit;
                ut = frame.Step.Ut;
                flight.Parts = Drained(flight.Parts, frame.Withdrawals);
                if (frame.Step.Done)
                {
                    return flight;
                }
            }
            throw new InvalidOperationException("the burn never ended");
        }

        private static IReadOnlyList<PartSnapshot> Drained(IReadOnlyList<PartSnapshot> parts, IReadOnlyList<Withdrawal> taken) =>
            parts.Select((p, i) => new PartSnapshot(
                p.Name,
                p.DryMass,
                p.DecoupledIn,
                p.Resources.Select(r => new PartResource(r.Name, r.Amount - taken.Where(w => w.Part == i && w.Resource == r.Name).Sum(w => w.Units), r.Density)).ToList(),
                p.Engine)).ToList();

        private static double UpperFuel(IReadOnlyList<PartSnapshot> parts) =>
            parts.Where(p => p.DecoupledIn == -1).SelectMany(p => p.Resources).Where(r => r.Density > 0).Sum(r => r.Amount);
    }
}
