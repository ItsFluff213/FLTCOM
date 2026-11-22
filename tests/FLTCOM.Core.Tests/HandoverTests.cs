using System;
using FLTCOM.Core.Landing;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    [CollectionDefinition(nameof(HandoverTests), DisableParallelization = true)]
    public class HandoverFlights
    {
    }

    /// <summary>
    /// The operator's Mun lander (18 t, 245 kN, one stage) handed from the deorbit burn to descent guidance on a
    /// 19 km circular orbit, aiming at 6.33 N. The nose is flown at a finite rate the way stock SAS turns it. Each
    /// flight takes seconds of CPU, so they run alone and never slow the timed snapshot test.
    /// </summary>
    [Collection(nameof(HandoverTests))]
    public class HandoverTests
    {
        private const double G0 = 9.80665;
        private const double Isp = 315;
        private const double Thrust = 245_000;
        private const double Ground = 4_341;
        private const double Tick = 0.02;
        private const double NoseRate = 15 * Math.PI / 180;
        private const double MissTolerance = 100;
        private readonly ITestOutputHelper _out;

        public HandoverTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Theory]
        [InlineData(10, 1, 2)]
        [InlineData(30, 1, 0)]
        public void TheDescentTakesOverAtABoundedRateAndLandsOnTheTarget(double inclination, double offTrack, double burnError)
        {
            LandingBody body = LandingTests.Mun;
            OrbitElements orbit = OrbitElements.Circular(body.Mu, body.Radius + 19_000, Angles.ToRadians(inclination), 0);
            LandingTarget track = OnGroundTrack(body, orbit, Angles.ToRadians(6.33 - offTrack));
            LandingTarget target = LandingTarget.At(Angles.ToRadians(6.33), track.Longitude);
            double mass = 18_000;
            LandingPlan plan = LandingPlanner.Plan(body, new LandingCraft(mass, 3.91, Thrust, 6_057, 1_000, Isp * G0), orbit, target, Ground, 0);
            Assert.True(plan.Ok, plan.Error);
            StateVector s = orbit.StateAt(plan.BurnUt);
            Vector3d x = s.Position;
            Vector3d v = s.Velocity + s.Prograde * plan.Prograde + s.Normal * plan.Normal + s.RadialOut * plan.Radial + s.Normal * burnError;
            mass /= Math.Exp(plan.DeorbitDeltaV / (Isp * G0));
            var guide = new LandingGuide(body, target, Ground, 3.91, 6_057, 1_000, 20);

            Vector3d nose = (v - s.Velocity).Normalized;
            Vector3d command = nose;
            double ut = plan.BurnUt;
            double firstJump = double.NaN;
            while (x.Magnitude - body.Radius > Ground)
            {
                Assert.True(ut < plan.BurnUt + 3_600, "no touchdown within an hour; phase " + guide.Phase);
                LandingCommand c = guide.Step(new LandingInput(ut, x, v, mass, Thrust, nose, Ground, false, false));
                Assert.Equal(LandingStatus.Running, c.Status);
                if (double.IsNaN(firstJump))
                {
                    firstJump = Degrees(nose, c.Direction);
                }
                Vector3d next = AttitudeSlew.Toward(command, c.Direction, Tick);
                Assert.True(Degrees(command, next) <= Angles.ToDegrees(AttitudeSlew.MaxRate * Tick) + 1e-6, $"the command turned {Degrees(command, next):0.00} degrees in one tick");
                command = next;
                nose = AttitudeSlew.Toward(nose, command, Tick * NoseRate / AttitudeSlew.MaxRate);
                double r = x.Magnitude;
                v += (x * (-body.Mu / (r * r * r)) + nose * (c.Throttle * Thrust / mass)) * Tick;
                x += v * Tick;
                mass -= c.Throttle * Thrust / (Isp * G0) * Tick;
                ut += Tick;
            }
            double miss = (x - body.Surface(target.Latitude, target.Longitude, Ground, ut)).Magnitude;
            double speed = body.AirVelocity(x, v).Magnitude;
            _out.WriteLine($"guidance asked for {firstJump:0.0} degrees at handover; touchdown {miss:0} m from the target at {speed:0.00} m/s after {ut - plan.BurnUt:0} s");
            Assert.True(firstJump > 5, $"the replay should hand over off the descent's attitude; it asked for only {firstJump:0.0} degrees");
            Assert.True(miss < MissTolerance, $"touchdown {miss:0} m from the target");
            Assert.True(speed < 2, $"touchdown at {speed:0.00} m/s");
        }

        /// <summary>A spot at <paramref name="latitude"/> that the orbit flies over on its third pass.</summary>
        private static LandingTarget OnGroundTrack(LandingBody body, OrbitElements orbit, double latitude)
        {
            double u = Math.Asin(Math.Sin(latitude) / Math.Sin(orbit.Inclination));
            double ut = 2 * orbit.Period + u / Angles.TwoPi * orbit.Period;
            Vector3d p = orbit.StateAt(ut).Position;
            return LandingTarget.At(body.Latitude(p), body.Longitude(p, ut));
        }

        private static double Degrees(Vector3d a, Vector3d b) => Angles.ToDegrees(Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(a.Normalized, b.Normalized)))));
    }
}
