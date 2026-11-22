using System.Linq;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class LandingSafetyTests
    {
        private const double G0 = 9.80665;
        private static readonly LandingTarget Target = LandingTarget.At(Angles.ToRadians(3), Angles.ToRadians(40));
        private static readonly OrbitElements Orbit = OrbitElements.Circular(LandingTests.Mun.Mu, LandingTests.Mun.Radius + 20_000, Angles.ToRadians(6), 0);

        private static LandingPlan Plan(double thrust)
        {
            LandingPlan plan = LandingPlanner.Plan(LandingTests.Mun, new LandingCraft(3_000, 0, thrust, exhaustVelocity: 320 * G0), Orbit, Target, 1_500, 100);
            Assert.True(plan.Ok, plan.Error);
            return plan;
        }

        [Fact]
        public void APoweredDescentWithFuelToBrakeRaisesNoAlarm()
        {
            LandingPrediction prediction = Plan(20_000).Prediction!;

            Assert.InRange(prediction.TouchdownSpeed, 0, FlightSafety.SafeTouchdownSpeed);
            Assert.Null(FlightSafety.Descent("Mun", prediction, prediction.DeltaVExpended + 50));
        }

        [Fact]
        public void ADescentShortOfDeltaVToBrakeIsALandingAlarm()
        {
            LandingPrediction prediction = Plan(20_000).Prediction!;

            FlightAlarm? alarm = FlightSafety.Descent("Mun", prediction, prediction.DeltaVExpended - 100);

            Assert.NotNull(alarm);
            Assert.Equal(AlarmKind.Landing, alarm!.Kind);
            Assert.StartsWith("braking needs", alarm.Text);
        }

        [Fact]
        public void ADescentWithNothingToBrakeIsPredictedTooFast()
        {
            StateVector s = Orbit.StateAt(0);
            var dropped = new StateVector(s.Position, s.Velocity * 0.9);
            var craft = new LandingCraft(3_000, 0, 0);
            LandingPrediction prediction = ReentryPredictor.Predict(LandingTests.Mun, craft, dropped, 0, ReentrySettings.For(LandingTests.Mun, craft, 1_500));

            FlightAlarm? alarm = FlightSafety.Descent("Mun", prediction, 0);

            Assert.True(prediction.Landed);
            Assert.True(prediction.TouchdownSpeed > 100, prediction.TouchdownSpeed.ToString("0"));
            Assert.NotNull(alarm);
            Assert.StartsWith("touchdown predicted at", alarm!.Text);
        }

        /// <summary>P17: a landing that has become short of delta-v since its briefing aborts before the deorbit and stays in orbit.</summary>
        [Fact]
        public void ALandingShortOfDeltaVBeforeItsDeorbitHoldsInOrbit()
        {
            var land = OrbitChange.Land(LandingTarget.At(Angles.ToRadians(2), Angles.ToRadians(30)));
            Briefing briefed = BriefingBuilder.Build("Touchdown", land, new[] { LandingTests.MunLander(fuelTonnes: 1.5) }, 0);
            Assert.True(briefed.CanApprove);
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Touchdown", land, briefed);
            Assert.Null(runner.Approve("op-1", "Touchdown", uplink: true));
            Briefing now = BriefingBuilder.Build("Touchdown", land, new[] { LandingTests.MunLander(fuelTonnes: 0.2) }, 0);

            FlightAlarm? abort = FlightSafety.BeforeDeorbit("Mun", now);
            runner.Alarm("op-1", abort!);

            Assert.Equal(AlarmKind.Landing, abort!.Kind);
            Assert.EndsWith("the vessel stays in orbit", abort.Text);
            Assert.Equal(StepState.Hold, runner.Find("op-1")!.State);
            PlannedBurn deorbit = briefed.Burns[0];
            var view = new ActiveView(GameScene.Flight, deorbit.VesselId, ActiveVesselCondition.StableOrbit, false);
            Assert.Equal(RunnerActionKind.Idle, runner.Next(deorbit.StartUt + 1, view).Kind);
        }

        [Fact]
        public void TheIntendedLandingIsNoImpactCourse()
        {
            Briefing b = BriefingBuilder.Build("Touchdown", OrbitChange.Land(LandingTarget.At(Angles.ToRadians(2), Angles.ToRadians(30))), new[] { LandingTests.MunLander(fuelTonnes: 1.5) }, 0);

            Assert.True(b.CanApprove);
            Assert.DoesNotContain(b.Contingencies, c => c.Text.Contains("impact course") || c.Text.Contains("safe floor"));
            Assert.Null(FlightSafety.BeforeDeorbit("Mun", b));
        }
    }
}
