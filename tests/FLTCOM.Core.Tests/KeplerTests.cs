using System;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class KeplerTests
    {
        private const double Mu = 3.5316e12;

        [Fact]
        public void PropagationAgreesWithTheElementsOnAnEllipse()
        {
            var orbit = new OrbitElements(Mu, 2_000_000, 0.3, 0.4, 1.0, 2.0, 0.5, 100);
            StateVector start = orbit.StateAt(100);
            foreach (double dt in new[] { 60.0, 1_000, orbit.Period * 0.77, orbit.Period * 3.2, -900 })
            {
                StateVector moved = Kepler.Propagate(Mu, start, dt);
                StateVector expected = orbit.StateAt(100 + dt);
                Assert.True((moved.Position - expected.Position).Magnitude < 1e-3, "position at dt " + dt);
                Assert.True((moved.Velocity - expected.Velocity).Magnitude < 1e-6, "velocity at dt " + dt);
            }
        }

        [Fact]
        public void AHyperbolaKeepsItsEnergyAndComesBack()
        {
            var start = new StateVector(new Vector3d(700_000, 0, 0), new Vector3d(0, 3_600, 300));
            double energy = Energy(start);
            StateVector out1 = Kepler.Propagate(Mu, start, 20_000);
            StateVector back = Kepler.Propagate(Mu, out1, -20_000);
            Assert.Equal(energy, Energy(out1), 6);
            Assert.True((back.Position - start.Position).Magnitude < 1e-2);
            Assert.True(Kepler.TimeToPeriapsis(Mu, out1) < 0);
        }

        [Fact]
        public void HyperbolicElementsMatchPropagation()
        {
            const double a = -5_000_000, e = 1.4, inc = 0.2, lan = 0.7, argPe = 1.1;
            StateVector at0 = Kepler.FromElements(Mu, a, e, inc, lan, argPe, -0.8, 0, 0);
            StateVector later = Kepler.FromElements(Mu, a, e, inc, lan, argPe, -0.8, 0, 5_000);
            StateVector moved = Kepler.Propagate(Mu, at0, 5_000);
            Assert.True((moved.Position - later.Position).Magnitude < 1, "positions differ by " + (moved.Position - later.Position).Magnitude);
            Assert.Equal(a, Kepler.SemiMajorAxis(Mu, at0), 0);
            Assert.Equal(-a * (e - 1), Kepler.PeriapsisRadius(Mu, at0), 0);
        }

        [Fact]
        public void TimeToPeriapsisOnAnEllipseIsTheRestOfTheOrbit()
        {
            var orbit = new OrbitElements(Mu, 2_000_000, 0.2, 0, 0, 0, 1.0, 0);
            double toPe = Kepler.TimeToPeriapsis(Mu, orbit.StateAt(0));
            Assert.Equal(orbit.Period * (1 - 1.0 / (2 * Math.PI)), toPe, 3);
        }

        private static double Energy(StateVector s) => Vector3d.Dot(s.Velocity, s.Velocity) / 2 - Mu / s.Position.Magnitude;
    }
}
