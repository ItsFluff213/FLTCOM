using System;
using System.Linq;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    public class LandingTests
    {
        private const double G0 = 9.80665;
        private static readonly CentralBody MunBody = new CentralBody("Mun", 6.5138398e10, 200_000, 0, 7_061);
        private static readonly CentralBody KerbinBody = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767);
        private readonly ITestOutputHelper _out;

        public LandingTests(ITestOutputHelper output)
        {
            _out = output;
        }

        internal static LandingBody Mun => new LandingBody(MunBody, 138_984.38, 0.3, 0, null);

        internal static LandingBody Kerbin => new LandingBody(KerbinBody, 21_549.425, 1.0, 0, Atmosphere.Exponential(1.225, 5_600, 70_000));

        [Fact]
        public void PoweredDescentOnAnAirlessBodyTouchesDownSoftlyOnTheTarget()
        {
            LandingBody body = Mun;
            LandingTarget target = LandingTarget.At(Angles.ToRadians(3), Angles.ToRadians(40));
            const double ground = 1_500;
            const double thrust = 20_000;
            const double isp = 320;
            double mass = 3_000;
            OrbitElements orbit = OrbitElements.Circular(body.Mu, body.Radius + 20_000, Angles.ToRadians(6), 0);

            LandingPlan plan = LandingPlanner.Plan(body, new LandingCraft(mass, 0, thrust, exhaustVelocity: isp * G0), orbit, target, ground, 100);
            Assert.True(plan.Ok, plan.Error);
            _out.WriteLine($"deorbit at {plan.BurnUt:0} s, {plan.DeorbitDeltaV:0.0} m/s, braking {plan.Prediction!.DeltaVExpended:0} m/s, miss {plan.Miss:0} m");

            StateVector s = orbit.StateAt(plan.BurnUt);
            Vector3d velocity = s.Velocity + s.Prograde * plan.Prograde + s.Normal * plan.Normal + s.RadialOut * plan.Radial;
            mass /= Math.Exp(plan.DeorbitDeltaV / (isp * G0));
            var guide = new LandingGuide(body, target, ground, 0, 0, 1000, 20);

            Touchdown t = Fly(body, guide, s.Position, velocity, plan.BurnUt, mass, thrust, isp, ground, target);
            _out.WriteLine($"touchdown {t.Speed:0.00} m/s, {t.Distance:0} m from the target, correction {guide.CorrectionSpent:0.0} m/s, fuel left {t.Mass:0} kg");
            Assert.True(t.Speed < 2, $"touchdown at {t.Speed:0.00} m/s");
            Assert.True(t.Distance < 200, $"landed {t.Distance:0} m from the target");
        }

        [Fact]
        public void ReentryPredictionOnKerbinMatchesAFineReferenceTrajectory()
        {
            LandingBody body = Kerbin;
            var craft = new LandingCraft(2_000, 1.2, 0, chuteArea: 500, chuteDeployAltitude: 1_000);
            OrbitElements orbit = OrbitElements.Circular(body.Mu, body.Radius + 80_000, Angles.ToRadians(10), 0);
            StateVector s = orbit.StateAt(300);
            double rp = body.Radius + 21_000;
            double r = s.Position.Magnitude;
            double v = Math.Sqrt(body.Mu * (2 / r - 2 / (r + rp)));
            var start = new StateVector(s.Position, s.Prograde * v);
            const double ground = 70;

            LandingPrediction p = ReentryPredictor.Predict(body, craft, start, 300, ReentrySettings.For(body, craft, ground));
            Assert.Equal(LandingOutcome.Landed, p.Outcome);
            (Vector3d x, double t) = Reference(body, craft, start, 300, ground);
            double distance = (body.Surface(p.Latitude, p.Longitude, ground, 0) - body.Surface(body.Latitude(x), body.Longitude(x, t), ground, 0)).Magnitude;
            _out.WriteLine($"predicted {Angles.ToDegrees(p.Latitude):0.000} {Angles.ToDegrees(p.Longitude):0.000} at {p.Ut:0}; reference at {t:0}; apart {distance:0} m");
            Assert.True(distance < 2_000, $"prediction is {distance:0} m from the reference");
        }

        [Fact]
        public void DeorbitFromLowKerbinOrbitAimsAtTheLaunchPad()
        {
            LandingBody body = Kerbin;
            var craft = new LandingCraft(2_000, 1.2, 0, chuteArea: 500);
            OrbitElements orbit = OrbitElements.Circular(body.Mu, body.Radius + 80_000, Angles.ToRadians(1), 0);

            LandingPlan plan = LandingPlanner.Plan(body, craft, orbit, LandingTarget.LaunchPad, 70, 100);

            Assert.True(plan.Ok, plan.Error);
            _out.WriteLine($"deorbit at {plan.BurnUt:0} s, {plan.DeorbitDeltaV:0.0} m/s, miss {plan.Miss:0} m");
            Assert.True(plan.Miss < 500, $"misses by {plan.Miss:0} m");
            Assert.InRange(plan.DeorbitDeltaV, 50, 200);
        }

        [Fact]
        public void LandingBriefingPlansADeorbitThenTheDescent()
        {
            Briefing b = BriefingBuilder.Build("Touchdown", OrbitChange.Land(LandingTarget.At(Angles.ToRadians(2), Angles.ToRadians(30))), new[] { MunLander(fuelTonnes: 1.5) }, 0);

            Assert.DoesNotContain(b.Contingencies, c => c.Severity == Severity.Red);
            Assert.Equal(new[] { LandingBriefing.DeorbitLabel, LandingBriefing.DescentLabel }, b.Burns.Select(x => x.Label));
            Assert.True(b.Burns[1].StartUt >= b.Burns[0].StartUt + b.Burns[0].Duration);
            Assert.NotNull(b.Landing);
            Assert.True(b.RequiredDeltaV > b.Burns[1].Prograde);
        }

        [Fact]
        public void TheDeorbitBurnShowsADescentOrbitThatMeetsTheGround()
        {
            Briefing b = BriefingBuilder.Build("Touchdown", OrbitChange.Land(LandingTarget.At(Angles.ToRadians(2), Angles.ToRadians(30))), new[] { MunLander(fuelTonnes: 1.5) }, 0);

            OrbitElements descent = b.Burns[0].Expected!;
            Assert.Null(b.Burns[0].OrbitAfter);
            Assert.True(descent.PeriapsisRadius < MunBody.Radius, "periapsis " + descent.PeriapsisRadius);
        }

        [Fact]
        public void LandingShortOfDeltaVIsRedAndStaysInOrbit()
        {
            Briefing b = BriefingBuilder.Build("Touchdown", OrbitChange.Land(LandingTarget.At(Angles.ToRadians(2), Angles.ToRadians(30))), new[] { MunLander(fuelTonnes: 0.2) }, 0);

            Contingency red = Assert.Single(b.Contingencies, c => c.Severity == Severity.Red);
            Assert.StartsWith("abort: short of delta-v", red.Text);
            Assert.False(b.CanApprove);
        }

        [Fact]
        public void LaunchPadIsOnlyOnTheHomePlanet()
        {
            Briefing b = BriefingBuilder.Build("Touchdown", OrbitChange.Land(LandingTarget.LaunchPad), new[] { MunLander(fuelTonnes: 1.5) }, 0);

            Contingency red = Assert.Single(b.Contingencies);
            Assert.Contains("home planet", red.Text);
            Assert.Empty(b.Burns);
        }

        [Fact]
        public void LandingTargetSurvivesTheMissionSave()
        {
            var op = new Operation("op-1", "Touchdown", OrbitChange.Land(LandingTarget.At(0.1, -0.2)), new[] { "v-1" }, Array.Empty<PlannedBurn>());
            Mission back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Home", new[] { op })));

            LandingTarget t = back.Operations[0].Change.Landing!;
            Assert.Equal(LandingSiteKind.Custom, t.Site);
            Assert.Equal(0.1, t.Latitude);
            Assert.Equal(-0.2, t.Longitude);
            Assert.Equal(LandingSiteKind.LaunchPad, new OrbitChange(OrbitChangeKind.Land).Landing!.Site);
        }

        internal static VesselTask MunLander(double fuelTonnes)
        {
            const double dry = 1.5;
            const double thrust = 20;
            const double ve = 320 * G0;
            double start = dry + fuelTonnes;
            var stage = new StageDeltaV(0, start, dry, thrust, ve * Math.Log(start / dry), fuelTonnes / (thrust / ve));
            OrbitElements orbit = OrbitElements.Circular(MunBody.GravParameter, MunBody.Radius + 20_000, Angles.ToRadians(6), 0);
            var context = new LandingContext(Mun, start * 1000, 0, 0, 1000, 1_500, homeBody: false);
            return new VesselTask("v-1", "Lander", MunBody, orbit, new[] { stage }, landing: context);
        }

        private readonly struct Touchdown
        {
            public Touchdown(double speed, double distance, double mass)
            {
                Speed = speed;
                Distance = distance;
                Mass = mass;
            }

            public double Speed { get; }

            public double Distance { get; }

            public double Mass { get; }
        }

        private static Touchdown Fly(LandingBody body, LandingGuide guide, Vector3d x, Vector3d v, double ut, double mass, double thrust, double isp, double ground, LandingTarget target)
        {
            Vector3d forward = v.Normalized * -1;
            double end = ut + 7_200;
            while (ut < end)
            {
                LandingCommand c = guide.Step(new LandingInput(ut, x, v, mass, thrust, forward, ground, false, false));
                Assert.NotEqual(LandingStatus.Failed, c.Status);
                forward = c.Direction;
                double dt = guide.Phase is LandingPhase.Brake or LandingPhase.Final ? 0.05 : 0.2;
                Vector3d push = c.Direction * (c.Throttle * thrust / mass);
                (x, v) = Rk4(body, x, v, push, dt, 0, 0);
                mass -= c.Throttle * thrust / (isp * G0) * dt;
                ut += dt;
                if (x.Magnitude - body.Radius <= ground)
                {
                    double speed = body.AirVelocity(x, v).Magnitude;
                    double distance = (x.Normalized * (body.Radius + ground) - body.Surface(target.Latitude, target.Longitude, ground, ut)).Magnitude;
                    return new Touchdown(speed, distance, mass);
                }
            }
            throw new Xunit.Sdk.XunitException("no touchdown within two hours; phase " + guide.Phase);
        }

        private static (Vector3d, double) Reference(LandingBody body, LandingCraft craft, StateVector start, double ut, double ground)
        {
            Vector3d x = start.Position;
            Vector3d v = start.Velocity;
            const double dt = 0.02;
            var none = new Vector3d(0, 0, 0);
            while (true)
            {
                double altitude = x.Magnitude - body.Radius;
                double area = craft.DragArea + (altitude - ground < craft.ChuteDeployAltitude ? craft.ChuteArea : 0);
                (Vector3d nx, Vector3d nv) = Rk4(body, x, v, none, dt, area, craft.Mass);
                if (nx.Magnitude - body.Radius <= ground)
                {
                    double f = (x.Magnitude - body.Radius - ground) / (x.Magnitude - nx.Magnitude);
                    return (x + (nx - x) * f, ut + dt * f);
                }
                x = nx;
                v = nv;
                ut += dt;
            }
        }

        private static (Vector3d, Vector3d) Rk4(LandingBody body, Vector3d x, Vector3d v, Vector3d push, double dt, double area, double mass)
        {
            Vector3d A(Vector3d p, Vector3d u)
            {
                double r = p.Magnitude;
                Vector3d a = p * (-body.Mu / (r * r * r)) + push;
                double rho = body.Density(r - body.Radius);
                if (rho > 0 && area > 0)
                {
                    Vector3d air = body.AirVelocity(p, u);
                    a += air * (-0.5 * rho * air.Magnitude * area / mass);
                }
                return a;
            }

            Vector3d k1v = A(x, v);
            Vector3d k1x = v;
            Vector3d k2v = A(x + k1x * (dt / 2), v + k1v * (dt / 2));
            Vector3d k2x = v + k1v * (dt / 2);
            Vector3d k3v = A(x + k2x * (dt / 2), v + k2v * (dt / 2));
            Vector3d k3x = v + k2v * (dt / 2);
            Vector3d k4v = A(x + k3x * dt, v + k3v * dt);
            Vector3d k4x = v + k3v * dt;
            return (x + (k1x + k2x * 2 + k3x * 2 + k4x) * (dt / 6), v + (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6));
        }
    }
}
