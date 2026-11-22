using System;
using System.Collections.Generic;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    public class AscentTests
    {
        private const double G0 = 9.80665;
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767);
        private const double KerbinDay = 21_549.425;
        private static readonly double KscLatitude = Angles.ToRadians(-0.0972);

        private readonly ITestOutputHelper _out;

        public AscentTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void KerbalXLikeRocketReaches80KmCircularWithinTwoKm()
        {
            Flight flight = Fly(LaunchTarget.Circular(80_000), KerbalXLike(), out OrbitElements orbit);

            _out.WriteLine($"ap {(orbit.ApoapsisRadius - Kerbin.Radius) / 1000:0.00} km, pe {(orbit.PeriapsisRadius - Kerbin.Radius) / 1000:0.00} km, dv {flight.DeltaVUsed:0} m/s, ascent {flight.AscentDeltaV:0}");
            Assert.InRange(orbit.ApoapsisRadius - Kerbin.Radius, 78_000, 82_000);
            Assert.InRange(orbit.PeriapsisRadius - Kerbin.Radius, 78_000, 82_000);
            Assert.True(flight.DeltaVUsed < 3_500, "used " + flight.DeltaVUsed + " m/s");
            Assert.True(flight.MaxDynamicPressure < 40_000, "max q " + flight.MaxDynamicPressure);
        }

        [Fact]
        public void CustomEllipseLandsBothApsides()
        {
            Flight flight = Fly(new LaunchTarget(150_000, 120_000), KerbalXLike(), out OrbitElements orbit);

            _out.WriteLine($"ap {(orbit.ApoapsisRadius - Kerbin.Radius) / 1000:0.00} km, pe {(orbit.PeriapsisRadius - Kerbin.Radius) / 1000:0.00} km, dv {flight.DeltaVUsed:0}");
            Assert.InRange(orbit.ApoapsisRadius - Kerbin.Radius, 148_000, 152_000);
            Assert.InRange(orbit.PeriapsisRadius - Kerbin.Radius, 118_000, 122_000);
        }

        [Fact]
        public void FortyFiveDegreesWithALanLandsInThePlane()
        {
            double inc = Angles.ToRadians(45), lan = Angles.ToRadians(120);
            Flight flight = Fly(LaunchTarget.Circular(80_000, inc, lan), KerbalXLike(), out OrbitElements orbit);

            _out.WriteLine($"i {Angles.ToDegrees(orbit.Inclination):0.000} lan {Angles.ToDegrees(orbit.LongitudeOfAscendingNode):0.000} wait {flight.LaunchUt:0} s dv {flight.DeltaVUsed:0}");
            Assert.InRange(Angles.ToDegrees(orbit.Inclination), 44.8, 45.2);
            Assert.InRange(Angles.ToDegrees(orbit.LongitudeOfAscendingNode), 119, 121);
            Assert.InRange(orbit.PeriapsisRadius - Kerbin.Radius, 78_000, 82_000);
        }

        [Fact]
        public void BudgetCoversWhatTheFlightUses()
        {
            var site = new LaunchSite(KscLatitude, 0, 70, KerbinDay, 0);
            LaunchPlane plane = LaunchWindow.Find(site, null, null, 0);
            AscentBudget budget = AscentBudget.Estimate(Kerbin, site, plane, LaunchTarget.Circular(80_000));
            Flight flight = Fly(LaunchTarget.Circular(80_000), KerbalXLike(), out _);

            _out.WriteLine($"budget {budget.Total:0} flight {flight.DeltaVUsed:0}");
            Assert.InRange(budget.Total, flight.DeltaVUsed, flight.DeltaVUsed + 600);
        }

        [Fact]
        public void PitchProgramStartsVerticalAndEndsFlat()
        {
            var site = new LaunchSite(KscLatitude, 0, 70, KerbinDay, 0);
            var guide = new AscentGuide(Kerbin, LaunchTarget.Circular(80_000), LaunchWindow.Find(site, null, null, 0), site.RotationRate, 70);

            Assert.Equal(90, guide.FlightPathAngle(500, 50));
            Assert.InRange(guide.FlightPathAngle(20_000, 600), 30, 60);
            Assert.Equal(0, guide.FlightPathAngle(60_000, 2000));
        }

        [Theory]
        [InlineData(0, 0, 0, false, false, true, true)]
        [InlineData(0, 0, 0, false, false, false, false)]
        [InlineData(1, 0, 0, false, true, true, true)]
        [InlineData(2, 0, 1, false, false, true, true)]
        [InlineData(2, 1, 1, false, false, true, false)]
        [InlineData(1, 0, 0, true, false, true, true)]
        [InlineData(1, 1, 0, true, false, true, false)]
        [InlineData(3, 0, 0, false, false, true, false)]
        public void StagingNeverDropsABurningEngine(int burning, int droppedBurning, int droppedSpent, bool emptyTanks, bool clamps, bool enginesRemain, bool expected)
        {
            Assert.Equal(expected, StagingRule.ShouldStage(new StagingView(burning, droppedBurning, droppedSpent, emptyTanks, clamps, enginesRemain)));
        }

        private sealed class Stage
        {
            public Stage(double thrustVac, double ispSea, double ispVac, double fuel, double dropped)
            {
                ThrustVac = thrustVac;
                IspSea = ispSea;
                IspVac = ispVac;
                Fuel = fuel;
                Dropped = dropped;
            }

            public double ThrustVac { get; }

            public double IspSea { get; }

            public double IspVac { get; }

            public double Fuel { get; set; }

            /// <summary>Dry mass this stage leaves behind when it separates.</summary>
            public double Dropped { get; }
        }

        private sealed class Flight
        {
            public double DeltaVUsed { get; set; }

            public double AscentDeltaV { get; set; }

            public double MaxDynamicPressure { get; set; }

            public double LaunchUt { get; set; }
        }

        /// <summary>About the stock Kerbal X: 130 t on the pad, lift-off thrust-to-weight near 1.6, three stages.</summary>
        private static List<Stage> KerbalXLike() => new List<Stage>
        {
            new Stage(2_300_000, 280, 305, 56_000, 8_000),
            new Stage(1_500_000, 285, 310, 36_000, 4_000),
            new Stage(250_000, 90, 350, 9_000, 0),
        };

        private const double Payload = 17_000;

        private static Flight Fly(LaunchTarget target, List<Stage> stages, out OrbitElements final)
        {
            const double dt = 0.02;
            const double cdA = 2.0;
            const double scaleHeight = 5_600;
            const double padAltitude = 70;
            double mu = Kerbin.GravParameter;
            var flight = new Flight();

            var site = new LaunchSite(KscLatitude, Angles.ToRadians(30), padAltitude, KerbinDay, 0);
            LaunchPlane plane = LaunchWindow.Find(site, target.Inclination, target.Lan, 10);
            flight.LaunchUt = plane.LaunchUt;
            LaunchSite pad = site.At(plane.LaunchUt);
            var omega = new Vector3d(0, 0, pad.RotationRate);
            Vector3d r = pad.Position(Kerbin.Radius);
            Vector3d v = pad.Velocity(Kerbin.Radius);
            double mass = Payload;
            foreach (Stage s in stages)
            {
                mass += s.Fuel + s.Dropped;
            }
            int current = 0;
            var guide = new AscentGuide(Kerbin, target, plane, pad.RotationRate, padAltitude);

            double t = plane.LaunchUt;
            for (; t < plane.LaunchUt + 900; t += dt)
            {
                double altitude = r.Magnitude - Kerbin.Radius;
                Assert.True(altitude > 0, "crashed at t+" + (t - plane.LaunchUt));
                double pressure = altitude < Kerbin.AtmosphereHeight ? Math.Exp(-altitude / scaleHeight) : 0;
                Vector3d surface = v - Vector3d.Cross(omega, r);
                double q = 0.5 * 1.225 * pressure * Vector3d.Dot(surface, surface);
                flight.MaxDynamicPressure = Math.Max(flight.MaxDynamicPressure, q);
                AscentCommand cmd = guide.Step(new AscentInput(t, altitude, q, new StateVector(r, v)));
                if (cmd.Phase == AscentPhase.Done)
                {
                    break;
                }
                while (current < stages.Count && stages[current].Fuel <= 0)
                {
                    mass -= stages[current].Dropped;
                    current++;
                }
                Assert.True(current < stages.Count, "out of fuel during the ascent");
                Stage stage = stages[current];
                double isp = stage.IspVac + (stage.IspSea - stage.IspVac) * pressure;
                double flow = cmd.Throttle * stage.ThrustVac / (stage.IspVac * G0);
                double thrust = flow * isp * G0;
                Vector3d direction = Enu.At(r).FromLocal(cmd.Direction).Normalized;
                Vector3d gravity = r * (-mu / Math.Pow(r.Magnitude, 3));
                Vector3d drag = surface.Magnitude > 0 ? surface.Normalized * (-q * cdA / mass) : new Vector3d(0, 0, 0);
                Vector3d accel = direction * (thrust / mass) + gravity + drag;
                if (altitude <= padAltitude + 0.5 && Vector3d.Dot(accel, r.Normalized) + Vector3d.Dot(omega, omega) * r.Magnitude <= 0)
                {
                    accel = Vector3d.Cross(omega, Vector3d.Cross(omega, r));
                }
                v += accel * dt;
                r += v * dt;
                flight.DeltaVUsed += thrust / mass * dt;
                mass -= flow * dt;
                stage.Fuel -= flow * dt;
            }
            flight.AscentDeltaV = flight.DeltaVUsed;
            Assert.Equal(AscentPhase.Done, guide.Phase);

            OrbitElements orbit = OrbitElements.FromState(mu, new StateVector(r, v), t);
            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.Launch(target), orbit, Kerbin, t);
            Assert.True(plan.Ok, plan.Error);
            foreach (ImpulsiveBurn burn in plan.Burns)
            {
                flight.DeltaVUsed += burn.DeltaV;
            }
            final = plan.Final!;
            return flight;
        }
    }
}
