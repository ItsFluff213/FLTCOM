using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    public enum AlarmKind
    {
        /// <summary>The path comes down under the safe floor where no landing is planned.</summary>
        Impact,
        /// <summary>A planned landing would come down too hard, or cannot be flown.</summary>
        Landing,
    }

    /// <summary>A red alert on an operation: the vessel is about to hit the ground unplanned or too hard.</summary>
    public sealed class FlightAlarm
    {
        public FlightAlarm(AlarmKind kind, string body, string text)
        {
            Kind = kind;
            Body = body;
            Text = text;
        }

        public AlarmKind Kind { get; }

        public string Body { get; }

        public string Text { get; }
    }

    /// <summary>A path that comes down under a body's safe floor (<see cref="CentralBody.SafeFloor"/>).</summary>
    public sealed class ImpactCourse
    {
        public ImpactCourse(string body, double periapsisAltitude, double floor, double ut)
        {
            Body = body;
            PeriapsisAltitude = periapsisAltitude;
            Floor = floor;
            Ut = ut;
        }

        public string Body { get; }

        /// <summary>Metres above sea level; negative under it.</summary>
        public double PeriapsisAltitude { get; }

        public double Floor { get; }

        /// <summary>When the path first drops under the floor.</summary>
        public double Ut { get; }

        public string Describe() => "on impact course with " + Body + ", periapsis " + Briefing.F(PeriapsisAltitude / 1000, "0.0") + " km";

        public FlightAlarm Alarm() => new FlightAlarm(AlarmKind.Impact, Body, Describe());
    }

    /// <summary>Whether a path, or a burn flown from it, ends in the ground.</summary>
    public static class FlightSafety
    {
        private const int Patches = 4;
        private const double Horizon = 1e8;

        /// <summary>Fastest touchdown, m/s, that landing legs and parachute landings take without breaking.</summary>
        public const double SafeTouchdownSpeed = 10;

        /// <summary>The first body on the coast from <paramref name="from"/> whose periapsis lies under its safe floor, or null.</summary>
        public static ImpactCourse? Impact(SolarSystem system, Patch from)
        {
            Patch p = from;
            for (int i = 0; i < Patches; i++)
            {
                CentralBody body = system[p.Body].Body;
                double mu = body.GravParameter;
                double toPe = Kepler.TimeToPeriapsis(mu, p.State);
                Patch next = PatchedConics.Step(system, p, p.Ut + (toPe >= 0 ? toPe : Horizon), out bool changed);
                if (changed)
                {
                    p = next;
                    continue;
                }
                double floor = body.Radius + body.SafeFloor;
                double rp = Kepler.PeriapsisRadius(mu, p.State);
                if (toPe < 0 || rp >= floor)
                {
                    return null;
                }
                return new ImpactCourse(body.Name, rp - body.Radius, body.SafeFloor, Crossing(mu, p, toPe, floor));
            }
            return null;
        }

        /// <summary>
        /// Why <paramref name="burn"/>, flown from where the vessel really is, no longer does what it was planned to do, or null
        /// when it still fits: it would end on an impact course before <paramref name="nextUt"/>, or away from its planned orbit.
        /// </summary>
        public static string? Stale(SolarSystem system, Patch now, PlannedBurn burn, double nextUt)
        {
            Patch after = TransferPlanner.AfterBurn(system, now, new ImpulsiveBurn(burn.Label, burn.NodeUt, burn.Prograde, burn.Normal, burn.Radial));
            if (Impact(system, after) is ImpactCourse impact && impact.Ut < nextUt)
            {
                return "would put the vessel " + impact.Describe();
            }
            if (burn.OrbitAfter is OrbitElements planned && (burn.Body ?? after.Body) == after.Body)
            {
                CentralBody body = system[after.Body].Body;
                OrbitElements flown = OrbitElements.FromState(body.GravParameter, after.State, after.Ut);
                bool closed = planned.Eccentricity < 1;
                if (closed != flown.Eccentricity < 1
                    || Off(flown.PeriapsisRadius, planned.PeriapsisRadius, body)
                    || (closed && Off(flown.ApoapsisRadius, planned.ApoapsisRadius, body)))
                {
                    return "would end at periapsis " + Km(flown.PeriapsisRadius - body.Radius) + " km instead of the planned " + Km(planned.PeriapsisRadius - body.Radius) + " km";
                }
            }
            return null;
        }

        /// <summary>A landing re-planned just before its deorbit burn that can no longer be approved stays in orbit: the first red reason.</summary>
        public static FlightAlarm? BeforeDeorbit(string body, Briefing replan) =>
            replan.CanApprove ? null : new FlightAlarm(AlarmKind.Landing, body, replan.Contingencies.First(c => c.Severity == Severity.Red).Text);

        /// <summary>A descent in flight that will come down too hard, or has too little delta-v left to brake.</summary>
        public static FlightAlarm? Descent(string body, LandingPrediction? prediction, double deltaVLeft)
        {
            if (prediction == null || !prediction.Landed)
            {
                return null;
            }
            if (prediction.TouchdownSpeed > SafeTouchdownSpeed)
            {
                return new FlightAlarm(AlarmKind.Landing, body, "touchdown predicted at " + Briefing.F(prediction.TouchdownSpeed, "0") + " m/s, over the safe " + Briefing.F(SafeTouchdownSpeed, "0") + " m/s");
            }
            if (prediction.DeltaVExpended > deltaVLeft)
            {
                return new FlightAlarm(AlarmKind.Landing, body, "braking needs " + Briefing.F(prediction.DeltaVExpended, "0") + " m/s, " + Briefing.F(deltaVLeft, "0") + " m/s left");
            }
            return null;
        }

        /// <summary>Red contingencies for burns that would fly a vessel into the ground: an impact before its first burn, or a final orbit under the floor.</summary>
        internal static IEnumerable<Contingency> Check(IReadOnlyList<VesselTask> tasks, IReadOnlyList<PlannedBurn> burns)
        {
            foreach (VesselTask task in tasks)
            {
                var own = burns.Where(b => b.VesselId == task.VesselId).ToList();
                if (own.Count == 0)
                {
                    continue;
                }
                if (task.System != null && task.Trajectory is Patch now && Impact(task.System, now) is ImpactCourse impact && impact.Ut < own[0].StartUt)
                {
                    yield return new Contingency(Severity.Red, task.VesselId, impact.Describe() + " before \"" + own[0].Label + "\"");
                }
                PlannedBurn last = own[own.Count - 1];
                CentralBody? body = last.Body == null || last.Body == task.Body.Name ? task.Body
                    : task.System != null && task.System.Contains(last.Body) ? task.System[last.Body].Body
                    : null;
                if (last.OrbitAfter is OrbitElements final && body != null && final.PeriapsisRadius < body.Radius + body.SafeFloor)
                {
                    yield return new Contingency(Severity.Red, task.VesselId, "the final orbit's periapsis, " + Km(final.PeriapsisRadius - body.Radius) + " km, is under " + body.Name + "'s safe floor of " + Km(body.SafeFloor) + " km");
                }
            }
        }

        private static bool Off(double flown, double planned, CentralBody body) =>
            Math.Abs(flown - planned) > Math.Max(5_000, 0.1 * (planned - body.Radius));

        private static string Km(double metres) => Briefing.F(metres / 1000, "0.0");

        /// <summary>The first time the coast drops to <paramref name="floor"/>: radius only falls between apoapsis and periapsis.</summary>
        private static double Crossing(double mu, Patch p, double toPe, double floor)
        {
            double a = Kepler.SemiMajorAxis(mu, p.State);
            double lo = a > 0 ? Math.Max(0, toPe - Math.PI * Math.Sqrt(a * a * a / mu)) : 0;
            double hi = toPe;
            if (Kepler.Propagate(mu, p.State, lo).Position.Magnitude <= floor)
            {
                return p.Ut + lo;
            }
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) / 2;
                if (Kepler.Propagate(mu, p.State, mid).Position.Magnitude <= floor)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            return p.Ut + hi;
        }
    }
}
