// The rendezvous decision tree (match planes, transfer or phasing orbit, match velocities at closest approach,
// close in on a timed intercept, then hold), the closing-time rule and the stop-short-of-closest-approach timing
// are ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/MechJebModuleRendezvousAutopilot.cs
// Original notice: Copyright the MechJeb contributors; MechJeb2 is licensed GPL-3.0, as is FLTCOM.
// The Lambert solver is the universal-variable method of Curtis, "Orbital Mechanics for Engineering Students", algorithm 5.2.
using System;
using System.Collections.Generic;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>The vessel a rendezvous meets and, for a docking, the port on it.</summary>
    public sealed class RendezvousTarget
    {
        public RendezvousTarget(string vesselId, string name, uint? portId = null)
        {
            VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PortId = portId;
        }

        public string VesselId { get; }

        public string Name { get; }

        public uint? PortId { get; }
    }

    /// <summary>Where two vessels pass closest within a time span.</summary>
    public readonly struct Approach
    {
        public Approach(double time, double distance, double relativeSpeed)
        {
            Time = time;
            Distance = distance;
            RelativeSpeed = relativeSpeed;
        }

        public double Time { get; }

        public double Distance { get; }

        public double RelativeSpeed { get; }
    }

    /// <summary>Plans the impulsive burns that bring a chaser to a hold beside a target in the same sphere of influence.</summary>
    public static class Rendezvous
    {
        public const double HoldDistance = 50;
        public const double HoldSpeed = 0.2;
        public const string HoldLabel = "kill relative velocity";

        /// <summary>Every closing burn aims to stop this far from the target, inside <see cref="HoldDistance"/>.</summary>
        public const double AimDistance = 30;

        public const int MaxPhasingOrbits = 12;

        /// <summary>Each orbit spent waiting counts as this much delta-v when choosing a window or phasing orbit.</summary>
        public const double WaitCost = 1;

        /// <summary>Closer than this, the chaser flies straight at the target on a timed intercept.</summary>
        public const double CloseInRange = 3000;
        public const double MaxClosingSpeed = 100;

        /// <summary>Time between one burn's node and the next, for the runner to warp, turn and settle.</summary>
        public const double BurnGap = 60;

        public const double MinClosingTime = 120;

        /// <summary>A transfer that misses the target by more than this is not a window.</summary>
        public const double WindowMiss = 2000;

        /// <summary>Phasing periapsis stays this high above the atmosphere and terrain.</summary>
        public const double FloorMargin = 5000;

        private static readonly double PlaneTolerance = Angles.ToRadians(0.05);
        private const int MaxSteps = 30;
        private const int SamplesPerOrbit = 360;

        /// <summary>
        /// Plans from the chaser's orbit at <paramref name="now"/>. No node falls before <paramref name="earliestUt"/>.
        /// Already holding plans no burns.
        /// </summary>
        public static OrbitPlan Plan(OrbitElements chaser, OrbitElements target, CentralBody body, double now, double earliestUt)
        {
            var burns = new List<ImpulsiveBurn>();
            double floor = body.Radius + Math.Max(body.AtmosphereHeight, body.HighestTerrainAltitude);
            for (int step = 0; step < MaxSteps; step++)
            {
                int before = burns.Count;
                string? error = Step(burns, ref chaser, target, floor + FloorMargin, body.SphereOfInfluence, now, earliestUt, out bool arrived);
                if (error != null)
                {
                    return OrbitPlan.Failure(error);
                }
                if (arrived)
                {
                    if (burns.Count > 0)
                    {
                        ImpulsiveBurn last = burns[burns.Count - 1];
                        burns[burns.Count - 1] = new ImpulsiveBurn(HoldLabel, last.NodeUt, last.Prograde, last.Normal, last.Radial, last.OrbitAfter);
                    }
                    return OrbitPlan.Success(burns, chaser);
                }
                if (burns.Count == before)
                {
                    return OrbitPlan.Failure("the rendezvous plan stalled " + Describe(chaser, target, now));
                }
                if (chaser.PeriapsisRadius < floor)
                {
                    return OrbitPlan.Failure("the rendezvous path would dip below " + Briefing.F((floor - body.Radius) / 1000, "0.#") + " km");
                }
                now = burns[burns.Count - 1].NodeUt;
                earliestUt = Math.Max(earliestUt, now + BurnGap);
            }
            return OrbitPlan.Failure("the rendezvous did not converge " + Describe(chaser, target, now));
        }

        public static double RelativeInclination(OrbitElements a, OrbitElements b) =>
            Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(NormalOf(a), NormalOf(b)))));

        /// <summary>Closest approach between <paramref name="from"/> and <paramref name="to"/>, sampled then refined.</summary>
        public static Approach ClosestApproach(OrbitElements chaser, OrbitElements target, double from, double to)
        {
            int samples = Math.Max(60, (int)Math.Ceiling((to - from) / chaser.Period * SamplesPerOrbit));
            double step = (to - from) / samples;
            int best = 0;
            double bestDistance = double.PositiveInfinity;
            for (int i = 0; i <= samples; i++)
            {
                double d = Distance(chaser, target, from + i * step);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            double t = GoldenMin(u => Distance(chaser, target, u), Math.Max(from, from + (best - 1) * step), Math.Min(to, from + (best + 1) * step));
            StateVector c = chaser.StateAt(t);
            StateVector g = target.StateAt(t);
            return new Approach(t, (c.Position - g.Position).Magnitude, (c.Velocity - g.Velocity).Magnitude);
        }

        private static string? Step(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, double floor, double soi, double now, double earliest, out bool arrived)
        {
            arrived = false;
            StateVector c = chaser.StateAt(now);
            StateVector g = target.StateAt(now);
            double distance = (c.Position - g.Position).Magnitude;
            double speed = (c.Velocity - g.Velocity).Magnitude;
            if (distance <= HoldDistance && speed <= HoldSpeed)
            {
                arrived = true;
                return null;
            }
            if (distance <= HoldDistance)
            {
                return Match(burns, ref chaser, target, earliest);
            }

            double span = 1.5 * chaser.Period;
            Approach ca = ClosestApproach(chaser, target, earliest, earliest + span);
            bool passing = ca.Time < earliest + span - 1;
            double closing = ClosingTime(distance, chaser.Period);
            if (passing && ca.Distance <= HoldDistance && ca.Time <= earliest + 2 * closing)
            {
                return MatchAt(burns, ref chaser, target, ca, earliest);
            }
            if (distance < CloseInRange)
            {
                return Intercept(burns, ref chaser, target, earliest, closing, "close in");
            }
            if (passing && ca.Distance < chaser.SemiMajorAxis / 25)
            {
                return ca.Distance <= WindowMiss
                    ? MatchAt(burns, ref chaser, target, ca, earliest)
                    : Intercept(burns, ref chaser, target, earliest, ca.Time - earliest, "correct the approach");
            }
            if (RelativeInclination(chaser, target) > PlaneTolerance)
            {
                return OrbitPlanner.MatchPlane(burns, ref chaser, earliest, NormalOf(target));
            }
            if (FindTransfer(chaser, target, earliest) is double window)
            {
                return Transfer(burns, ref chaser, target, window, earliest);
            }
            return Phase(burns, ref chaser, target, floor, soi, earliest);
        }

        /// <summary>Matches the target's velocity at <paramref name="ut"/>.</summary>
        private static string? Match(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, double ut) =>
            OrbitPlanner.Apply(burns, ref chaser, ut, chaser.StateAt(ut), target.StateAt(ut).Velocity, "match velocity");

        /// <summary>Matches velocity just before closest approach, so the chaser stops about <see cref="AimDistance"/> short.</summary>
        private static string? MatchAt(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, Approach ca, double earliest)
        {
            double lead = ca.RelativeSpeed > 0 ? Math.Sqrt(Math.Max(0, AimDistance * AimDistance - ca.Distance * ca.Distance)) / ca.RelativeSpeed : 0;
            if (ca.RelativeSpeed > 10)
            {
                lead += 1;
            }
            return Match(burns, ref chaser, target, Math.Max(earliest, ca.Time - lead));
        }

        /// <summary>Flies straight at a point <see cref="AimDistance"/> short of where the target will be after <paramref name="flight"/> seconds.</summary>
        private static string? Intercept(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, double ut, double flight, string label)
        {
            StateVector c = chaser.StateAt(ut);
            StateVector g = target.StateAt(ut);
            Vector3d away = (c.Position - g.Position).Normalized;
            Vector3d aim = target.StateAt(ut + flight).Position + away * AimDistance;
            Vector3d? v = Lambert(c.Position, aim, flight, chaser.Mu, Vector3d.Cross(c.Position, c.Velocity));
            return v is Vector3d velocity
                ? OrbitPlanner.Apply(burns, ref chaser, ut, c, velocity, label)
                : "no intercept found for the final approach";
        }

        private static double ClosingTime(double distance, double period)
        {
            double closingSpeed = Math.Max(0.01, Math.Min(distance / 200, MaxClosingSpeed));
            return Math.Min(Math.Max(distance / closingSpeed, MinClosingTime), period / 4);
        }

        /// <summary>
        /// The first prograde or retrograde burn within <see cref="MaxPhasingOrbits"/> chaser orbits that puts the opposite side
        /// of the orbit on the target's path just as the target gets there. Null when no such window opens.
        /// </summary>
        private static double? FindTransfer(OrbitElements chaser, OrbitElements target, double earliest)
        {
            int samples = MaxPhasingOrbits * SamplesPerOrbit;
            double step = chaser.Period / SamplesPerOrbit;
            double[] miss = new double[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                miss[i] = TransferTo(chaser, target, earliest + i * step).Miss;
            }
            double? best = null;
            double bestCost = double.PositiveInfinity;
            for (int i = 1; i < samples; i++)
            {
                if (miss[i] > miss[i - 1] || miss[i] > miss[i + 1] || miss[i] > chaser.SemiMajorAxis / 25)
                {
                    continue;
                }
                double t = GoldenMin(u => TransferTo(chaser, target, u).Miss, earliest + (i - 1) * step, earliest + (i + 1) * step);
                TransferLeg leg = TransferTo(chaser, target, t);
                double cost = leg.Cost + WaitCost * (t - earliest) / chaser.Period;
                if (leg.Miss < WindowMiss && cost < bestCost)
                {
                    best = t;
                    bestCost = cost;
                }
            }
            return best;
        }

        private static string? Transfer(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, double ut, double earliest)
        {
            TransferLeg leg = TransferTo(chaser, target, ut);
            StateVector c = chaser.StateAt(ut);
            if ((leg.Velocity - c.Velocity).Magnitude < OrbitPlanner.NegligibleDeltaV)
            {
                Approach arrival = ClosestApproach(chaser, target, Math.Max(earliest, leg.Arrival - chaser.Period / 8), leg.Arrival + chaser.Period / 8);
                return MatchAt(burns, ref chaser, target, arrival, earliest);
            }
            return OrbitPlanner.Apply(burns, ref chaser, ut, c, leg.Velocity, "transfer to the target");
        }

        private readonly struct TransferLeg
        {
            public TransferLeg(Vector3d velocity, double arrival, double miss, double cost)
            {
                Velocity = velocity;
                Arrival = arrival;
                Miss = miss;
                Cost = cost;
            }

            public Vector3d Velocity { get; }

            public double Arrival { get; }

            public double Miss { get; }

            public double Cost { get; }
        }

        /// <summary>A horizontal burn at <paramref name="ut"/> whose opposite apsis lies on the target's orbit.</summary>
        private static TransferLeg TransferTo(OrbitElements chaser, OrbitElements target, double ut)
        {
            StateVector c = chaser.StateAt(ut);
            double r1 = c.Position.Magnitude;
            Vector3d up = c.Position / r1;
            Vector3d along = Vector3d.Cross(c.Normal, up).Normalized;
            double r2 = RadiusToward(target, up * -1);
            double mu = chaser.Mu;
            double flight = Math.PI * Math.Sqrt(Math.Pow((r1 + r2) / 2, 3) / mu);
            Vector3d depart = along * Math.Sqrt(2 * mu * r2 / (r1 * (r1 + r2)));
            Vector3d arrive = along * -Math.Sqrt(2 * mu * r1 / (r2 * (r1 + r2)));
            StateVector g = target.StateAt(ut + flight);
            double miss = (g.Position - up * -r2).Magnitude;
            double cost = (depart - c.Velocity).Magnitude + (g.Velocity - arrive).Magnitude;
            return new TransferLeg(depart, ut + flight, miss, cost);
        }

        /// <summary>
        /// When no transfer window opens soon: a horizontal burn to an orbit that returns to this point after k orbits just as a
        /// transfer from here would meet the target. Picks the cheapest k and direction whose orbit clears <paramref name="floor"/>.
        /// </summary>
        private static string? Phase(List<ImpulsiveBurn> burns, ref OrbitElements chaser, OrbitElements target, double floor, double soi, double ut)
        {
            StateVector c = chaser.StateAt(ut);
            double mu = chaser.Mu;
            double r1 = c.Position.Magnitude;
            Vector3d up = c.Position / r1;
            Vector3d along = Vector3d.Cross(c.Normal, up).Normalized;
            Vector3d opposite = up * -1;
            double r2 = RadiusToward(target, opposite);
            double halfTransfer = Math.PI * Math.Sqrt(Math.Pow((r1 + r2) / 2, 3) / mu);
            double targetThere = target.NextTimeAtTrueAnomaly(TrueAnomalyToward(target, opposite), ut) - ut;
            double transferSpeed = Math.Sqrt(2 * mu * r2 / (r1 * (r1 + r2)));

            Vector3d? best = null;
            double bestCost = double.PositiveInfinity;
            for (int k = 1; k <= MaxPhasingOrbits; k++)
            {
                double m0 = Math.Round((k * chaser.Period - targetThere + halfTransfer) / target.Period);
                for (double m = Math.Max(0, m0 - 1); m <= m0 + 1; m++)
                {
                    double period = (targetThere + m * target.Period - halfTransfer) / k;
                    if (period <= 0)
                    {
                        continue;
                    }
                    double a = Math.Pow(mu * Math.Pow(period / Angles.TwoPi, 2), 1.0 / 3);
                    double other = 2 * a - r1;
                    if (Math.Min(r1, other) < floor || Math.Max(r1, other) >= soi)
                    {
                        continue;
                    }
                    double speed = Math.Sqrt(mu * (2 / r1 - 1 / a));
                    double cost = (along * speed - c.Velocity).Magnitude + Math.Abs(transferSpeed - speed) + WaitCost * k * period / chaser.Period;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = along * speed;
                    }
                }
            }
            return best is Vector3d v
                ? OrbitPlanner.Apply(burns, ref chaser, ut, c, v, "phasing orbit")
                : "no phasing orbit within " + MaxPhasingOrbits + " orbits clears the atmosphere";
        }

        private static Vector3d NormalOf(OrbitElements orbit)
        {
            StateVector s = orbit.StateAtTrueAnomaly(0);
            return Vector3d.Cross(s.Position, s.Velocity).Normalized;
        }

        private static double TrueAnomalyToward(OrbitElements orbit, Vector3d direction)
        {
            Vector3d n = NormalOf(orbit);
            Vector3d inPlane = direction - n * Vector3d.Dot(direction, n);
            return Angles.Wrap(Vector3d.AngleAbout(orbit.StateAtTrueAnomaly(0).Position, inPlane, n));
        }

        private static double RadiusToward(OrbitElements orbit, Vector3d direction)
        {
            double nu = TrueAnomalyToward(orbit, direction);
            double e = orbit.Eccentricity;
            return orbit.SemiMajorAxis * (1 - e * e) / (1 + e * Math.Cos(nu));
        }

        private static double Distance(OrbitElements a, OrbitElements b, double ut) => (a.StateAt(ut).Position - b.StateAt(ut).Position).Magnitude;

        private static string Describe(OrbitElements chaser, OrbitElements target, double ut) =>
            Briefing.F(Distance(chaser, target, ut) / 1000, "0.###") + " km from the target";

        private static double GoldenMin(Func<double, double> f, double lo, double hi)
        {
            const double Ratio = 0.6180339887498949;
            double x1 = hi - Ratio * (hi - lo);
            double x2 = lo + Ratio * (hi - lo);
            double f1 = f(x1);
            double f2 = f(x2);
            for (int i = 0; i < 80 && hi - lo > 1e-4; i++)
            {
                if (f1 < f2)
                {
                    hi = x2;
                    x2 = x1;
                    f2 = f1;
                    x1 = hi - Ratio * (hi - lo);
                    f1 = f(x1);
                }
                else
                {
                    lo = x1;
                    x1 = x2;
                    f1 = f2;
                    x2 = lo + Ratio * (hi - lo);
                    f2 = f(x2);
                }
            }
            return (lo + hi) / 2;
        }

        /// <summary>The departure velocity that carries a body from <paramref name="r1"/> to <paramref name="r2"/> in <paramref name="flight"/> seconds the short way round <paramref name="normal"/>.</summary>
        public static Vector3d? Lambert(Vector3d r1, Vector3d r2, double flight, double mu, Vector3d normal)
        {
            double m1 = r1.Magnitude;
            double m2 = r2.Magnitude;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(r1, r2) / (m1 * m2))));
            if (Vector3d.Dot(Vector3d.Cross(r1, r2), normal) < 0)
            {
                angle = Angles.TwoPi - angle;
            }
            if (1 - Math.Cos(angle) < 1e-12)
            {
                return null;
            }
            double a = Math.Sin(angle) * Math.Sqrt(m1 * m2 / (1 - Math.Cos(angle)));
            double Y(double z) => m1 + m2 + a * (z * StumpffS(z) - 1) / Math.Sqrt(StumpffC(z));
            double F(double z)
            {
                double y = Y(z);
                return Math.Pow(y / StumpffC(z), 1.5) * StumpffS(z) + a * Math.Sqrt(y) - Math.Sqrt(mu) * flight;
            }

            double hi = 4 * Math.PI * Math.PI - 1e-6;
            double lo = -4 * Math.PI * Math.PI;
            if (a > 0 && Y(lo) < 0)
            {
                double yLo = lo;
                double yHi = hi;
                for (int i = 0; i < 200; i++)
                {
                    double mid = (yLo + yHi) / 2;
                    if (Y(mid) < 0)
                    {
                        yLo = mid;
                    }
                    else
                    {
                        yHi = mid;
                    }
                }
                lo = yHi;
            }
            if (F(lo) > 0 || F(hi) < 0)
            {
                return null;
            }
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                if (F(mid) < 0)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            double yz = Y((lo + hi) / 2);
            double f = 1 - yz / m1;
            double g = a * Math.Sqrt(yz / mu);
            return (r2 - r1 * f) / g;
        }

        private static double StumpffS(double z)
        {
            if (Math.Abs(z) < 1e-3)
            {
                return 1.0 / 6 - z / 120 + z * z / 5040;
            }
            if (z > 0)
            {
                double s = Math.Sqrt(z);
                return (s - Math.Sin(s)) / (s * s * s);
            }
            double h = Math.Sqrt(-z);
            return (Math.Sinh(h) - h) / (h * h * h);
        }

        private static double StumpffC(double z)
        {
            if (Math.Abs(z) < 1e-3)
            {
                return 0.5 - z / 24 + z * z / 720;
            }
            return z > 0 ? (1 - Math.Cos(Math.Sqrt(z))) / z : (Math.Cosh(Math.Sqrt(-z)) - 1) / -z;
        }
    }
}
