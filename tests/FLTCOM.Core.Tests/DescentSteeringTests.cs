using System;
using FLTCOM.Core.Landing;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    /// <summary>
    /// The descent's attitude command, flown by a vessel that turns at a finite rate the way stock SAS does. The command
    /// must move smoothly inside each phase: a direction that jumps from one tick to the next makes SAS chase it, and a
    /// direction that reverses every prediction spins the vessel.
    /// </summary>
    public class DescentSteeringTests
    {
        private const double G0 = 9.80665;
        private const double Tick = 0.02;
        private const double TurnRate = 20 * Math.PI / 180;
        private const double MaxStepDegrees = 30;
        private const double MaxSwingDegrees = 45;
        private readonly ITestOutputHelper _out;

        public DescentSteeringTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Theory]
        [InlineData(132_000, 0, 0, 60_000, 5_000, 2_631, 0)]
        [InlineData(132_000, 0, 0, 60_000, 5_000, 2_631, 3)]
        [InlineData(20_000, 3, 40, 20_000, 3_000, 1_500, 0)]
        public void TheAttitudeCommandNeverJumpsWithinAPhase(double orbitAltitude, double latitude, double longitude, double thrust, double mass, double ground, double burnError)
        {
            LandingBody body = LandingTests.Mun;
            LandingTarget target = LandingTarget.At(Angles.ToRadians(latitude), Angles.ToRadians(longitude));
            const double isp = 345;
            OrbitElements orbit = OrbitElements.Circular(body.Mu, body.Radius + orbitAltitude, Angles.ToRadians(3), 0);
            LandingPlan plan = LandingPlanner.Plan(body, new LandingCraft(mass, 4.32, thrust, 6_057, 1_000, isp * G0), orbit, target, ground, 0);
            Assert.True(plan.Ok, plan.Error);
            StateVector s = orbit.StateAt(plan.BurnUt);
            Vector3d x = s.Position;
            Vector3d v = s.Velocity + s.Prograde * plan.Prograde + s.Normal * plan.Normal + s.RadialOut * plan.Radial + s.Normal * burnError;
            mass /= Math.Exp(plan.DeorbitDeltaV / (isp * G0));
            var guide = new LandingGuide(body, target, ground, 4.32, 6_057, 1_000, 20);

            Vector3d nose = (v - s.Velocity).Normalized;
            Vector3d? last = null;
            var second = new System.Collections.Generic.Queue<(double Ut, Vector3d Direction)>();
            LandingPhase phase = guide.Phase;
            double worst = 0;
            double ut = plan.BurnUt;
            while (x.Magnitude - body.Radius > ground)
            {
                Assert.True(ut < plan.BurnUt + 3_600, "no touchdown within an hour; phase " + guide.Phase);
                LandingCommand c = guide.Step(new LandingInput(ut, x, v, mass, thrust, nose, ground, false, false));
                Assert.Equal(LandingStatus.Running, c.Status);
                if (last is Vector3d previous && guide.Phase == phase)
                {
                    double step = Degrees(previous, c.Direction);
                    Assert.True(step <= MaxStepDegrees, $"{guide.Phase}: the command jumped {step:0} degrees in one tick, {ut - plan.BurnUt:0.00} s after the burn");
                    worst = Math.Max(worst, step);
                    while (second.Count > 0 && ut - second.Peek().Ut > 1)
                    {
                        second.Dequeue();
                    }
                    double swing = second.Count > 0 ? Degrees(second.Peek().Direction, c.Direction) : 0;
                    Assert.True(swing <= MaxSwingDegrees, $"{guide.Phase}: the command swung {swing:0} degrees within a second, {ut - plan.BurnUt:0.00} s after the burn");
                }
                else
                {
                    second.Clear();
                }
                second.Enqueue((ut, c.Direction));
                phase = guide.Phase;
                last = c.Direction;
                nose = TurnToward(nose, c.Direction, TurnRate * Tick);
                double r = x.Magnitude;
                v += (x * (-body.Mu / (r * r * r)) + nose * (c.Throttle * thrust / mass)) * Tick;
                x += v * Tick;
                mass -= c.Throttle * thrust / (isp * G0) * Tick;
                ut += Tick;
            }
            double speed = body.AirVelocity(x, v).Magnitude;
            _out.WriteLine($"touchdown {speed:0.00} m/s after {ut - plan.BurnUt:0} s, correction {guide.CorrectionSpent:0.0} m/s, largest step {worst:0.0} degrees");
            Assert.True(speed < 2, $"touchdown at {speed:0.00} m/s");
        }

        private static Vector3d TurnToward(Vector3d nose, Vector3d wanted, double most)
        {
            double off = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(nose, wanted))));
            if (off < 1e-9)
            {
                return nose;
            }
            Vector3d axis = Vector3d.Cross(nose, wanted);
            axis = axis.Magnitude > 1e-12 ? axis.Normalized : Vector3d.Cross(nose, Math.Abs(nose.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0)).Normalized;
            return Vector3d.Rotate(nose, axis, Math.Min(off, most)).Normalized;
        }

        private static double Degrees(Vector3d a, Vector3d b) => Angles.ToDegrees(Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(a.Normalized, b.Normalized)))));
    }
}
