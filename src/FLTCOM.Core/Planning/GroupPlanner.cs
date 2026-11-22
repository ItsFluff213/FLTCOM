using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>
    /// Plans one orbit change for every vessel of a group. Each vessel's burns are moved by whole orbits until no two
    /// vessels burn at once. Spread evenly first brings every vessel to the circular orbit, then gives each a phasing
    /// orbit whose period makes it lose or gain exactly the angle to its slot over a whole number of revolutions.
    /// </summary>
    public static class GroupPlanner
    {
        /// <summary>Clear time between the end of one vessel's burn and the start of another's.</summary>
        public const double Gap = 30;

        public const int MaxShifts = 12;

        public const int MaxPhasingOrbits = 30;

        /// <summary>A phasing orbit keeps this far above the atmosphere or the highest terrain.</summary>
        public const double SafeAltitude = 10_000;

        private const double PhasingDeltaVGuess = 100;
        private const double NegligiblePhase = 1e-5;
        private static readonly double MissTolerance = Angles.ToRadians(0.5);

        public static IReadOnlyDictionary<string, OrbitPlan> Plan(OrbitChange change, IReadOnlyList<VesselTask> tasks, double earliestUt)
        {
            var plans = tasks.ToDictionary(t => t.VesselId, t => OrbitPlanner.Plan(change, t.Orbit, t.Body, earliestUt));
            if (tasks.Count < 2)
            {
                return plans;
            }
            var busy = new List<Window>();
            foreach (VesselTask task in tasks.Where(t => plans[t.VesselId].Ok && plans[t.VesselId].Burns.Count > 0).OrderBy(t => plans[t.VesselId].Burns[0].NodeUt))
            {
                OrbitPlan plan = plans[task.VesselId];
                int shifts = 0;
                while (shifts < MaxShifts && Windows(task, Later(plan, shifts * task.Orbit.Period)).Any(w => w.Overlaps(busy)))
                {
                    shifts++;
                }
                plan = Later(plan, shifts == MaxShifts ? 0 : shifts * task.Orbit.Period);
                plans[task.VesselId] = plan;
                busy.AddRange(Windows(task, plan));
            }
            if (change.Kind == OrbitChangeKind.SpreadEvenly)
            {
                Spread(change, tasks, plans, busy, earliestUt);
            }
            return plans;
        }

        /// <summary>Burn time from the first stage that has delta-v; zero without one.</summary>
        public static double EstimatedDuration(VesselTask task, double deltaV)
        {
            var stage = task.Stages.FirstOrDefault(s => s.DeltaV > 0 && s.Thrust > 0);
            return stage == null ? 0 : deltaV * stage.StartMass / stage.Thrust;
        }

        /// <summary>
        /// How far to move a vessel's remaining burns after it missed one: whole periods of the orbit it is on, so the
        /// chain still fits, clear of the other vessels' burns. For a spread, <paramref name="slotMeanMotion"/> is the
        /// target circle's and the shift that drifts the vessel least from its slot wins.
        /// </summary>
        public static (double Seconds, double PhaseError)? MissedShift(IReadOnlyList<Missions.PlannedBurn> remaining, string vesselId, double period, double? slotMeanMotion, double earliestUt)
        {
            var own = remaining.Where(b => b.VesselId == vesselId).ToList();
            var others = remaining.Where(b => b.VesselId != vesselId).Select(b => new Window(b.StartUt - Gap, b.EndUt + Gap)).ToList();
            (double Seconds, double PhaseError)? best = null;
            for (int k = 1; k <= MaxShifts * 4; k++)
            {
                double shift = k * period;
                if (own.Count == 0 || own[0].StartUt + shift < earliestUt || own.Any(b => new Window(b.StartUt + shift, b.EndUt + shift).Overlaps(others)))
                {
                    continue;
                }
                double error = slotMeanMotion is double n ? Math.Abs(WrapPi(n * shift)) : 0;
                if (best == null || error < best.Value.PhaseError - NegligiblePhase)
                {
                    best = (shift, error);
                }
                if (error < MissTolerance)
                {
                    break;
                }
            }
            return best;
        }

        /// <summary>Whether a vessel's orbit is the one a burn should have left it on: the player flew it by hand.</summary>
        public static bool Matches(OrbitElements actual, OrbitElements planned) =>
            Math.Abs(actual.SemiMajorAxis - planned.SemiMajorAxis) < 0.005 * planned.SemiMajorAxis
            && Math.Abs(actual.Eccentricity - planned.Eccentricity) < 0.005
            && Math.Abs(actual.Inclination - planned.Inclination) < Angles.ToRadians(0.2);

        private static void Spread(OrbitChange change, IReadOnlyList<VesselTask> tasks, Dictionary<string, OrbitPlan> plans, List<Window> busy, double earliestUt)
        {
            var members = tasks.Where(t => plans[t.VesselId].Ok).ToList();
            if (members.Count < 2)
            {
                return;
            }
            CentralBody body = members[0].Body;
            double radius = body.Radius + change.Target;
            double n = Math.Sqrt(body.GravParameter / (radius * radius * radius));

            var slots = new List<(VesselTask Task, OrbitElements Circle, double Ut)>();
            foreach (VesselTask task in members.OrderBy(t => Free(plans[t.VesselId], earliestUt)))
            {
                OrbitPlan plan = plans[task.VesselId];
                double half = Gap + EstimatedDuration(task, PhasingDeltaVGuess) / 2;
                double ut = Window.FirstFree(Free(plan, earliestUt) + half, half, busy);
                busy.Add(new Window(ut - half, ut + half));
                slots.Add((task, plan.Final!, ut));
            }

            Vector3d normal = RingPlane.MeanNormal(slots.Select(s => s.Circle));
            double[] lambda = slots.Select(s => Angles.Wrap(RingPlane.ArgumentOfLatitude(s.Circle, s.Ut, normal) - n * (s.Ut - earliestUt))).ToArray();
            int count = slots.Count;
            int[] rank = new int[count];
            int[] order = Enumerable.Range(0, count).OrderBy(i => lambda[i]).ToArray();
            for (int r = 0; r < count; r++)
            {
                rank[order[r]] = r;
            }
            double sin = 0;
            double cos = 0;
            for (int i = 0; i < count; i++)
            {
                double a = lambda[i] - Angles.TwoPi * rank[i] / count;
                sin += Math.Sin(a);
                cos += Math.Cos(a);
            }
            double origin = Math.Atan2(sin, cos);

            for (int i = 0; i < count; i++)
            {
                (VesselTask task, OrbitElements circle, double ut) = slots[i];
                double ahead = WrapPi(lambda[i] - origin - Angles.TwoPi * rank[i] / count);
                string slot = (rank[i] + 1).ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture);
                plans[task.VesselId] = Phase(task, plans[task.VesselId], circle, ut, ahead, slot, busy);
            }
        }

        /// <summary>
        /// A vessel <paramref name="ahead"/> radians ahead of its slot flies m revolutions of a phasing orbit with period
        /// T + ahead / (n m): the slot gains n m (Tp - T) = ahead on it, and it circularizes where it started.
        /// </summary>
        private static OrbitPlan Phase(VesselTask task, OrbitPlan plan, OrbitElements circle, double ut, double ahead, string slot, List<Window> busy)
        {
            if (Math.Abs(ahead) < NegligiblePhase)
            {
                return plan;
            }
            CentralBody body = task.Body;
            double mu = circle.Mu;
            double floor = body.Radius + body.SafeFloor + SafeAltitude;
            StateVector state = circle.StateAt(ut);
            double r = state.Position.Magnitude;
            string degrees = Angles.ToDegrees(-ahead).ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
            string? refusal = null;
            for (int m = 1; m <= MaxPhasingOrbits; m++)
            {
                double period = circle.Period + ahead / (circle.MeanMotion * m);
                double a = Math.Pow(mu * Math.Pow(period / Angles.TwoPi, 2), 1.0 / 3);
                double other = 2 * a - r;
                if (other < floor || other >= body.SphereOfInfluence)
                {
                    refusal = "cannot spread evenly: every phasing orbit would dip under " + Km(floor - body.Radius) + " or leave " + body.Name + "'s sphere of influence";
                    continue;
                }
                var burns = plan.Burns.ToList();
                OrbitElements orbit = circle;
                string? error = OrbitPlanner.Apply(burns, ref orbit, ut, state, state.Prograde * Math.Sqrt(mu * (2 / r - 1 / a)), "phase " + degrees + " deg for slot " + slot);
                if (error != null)
                {
                    return OrbitPlan.Failure(error);
                }
                double back = ut + m * orbit.Period;
                double half = Gap + EstimatedDuration(task, burns[burns.Count - 1].DeltaV) / 2;
                var window = new Window(back - half, back + half);
                if (window.Overlaps(busy) && m < MaxPhasingOrbits)
                {
                    continue;
                }
                StateVector end = orbit.StateAt(back);
                error = OrbitPlanner.Apply(burns, ref orbit, back, end, end.Prograde * Math.Sqrt(mu / end.Position.Magnitude), "circularize in slot " + slot);
                if (error != null)
                {
                    return OrbitPlan.Failure(error);
                }
                busy.Add(window);
                return OrbitPlan.Success(burns, orbit);
            }
            return OrbitPlan.Failure(refusal ?? "cannot spread evenly: no phasing orbit fits between the other burns");
        }

        private static double Free(OrbitPlan plan, double earliestUt) => plan.Burns.Count > 0 ? plan.Burns[plan.Burns.Count - 1].NodeUt : earliestUt;

        private static OrbitPlan Later(OrbitPlan plan, double seconds) =>
            seconds == 0 ? plan : OrbitPlan.Success(plan.Burns.Select(b => b.Later(seconds)).ToList(), plan.Final!.Later(seconds));

        private static IEnumerable<Window> Windows(VesselTask task, OrbitPlan plan) =>
            plan.Burns.Select(b =>
            {
                double half = Gap + EstimatedDuration(task, b.DeltaV) / 2;
                return new Window(b.NodeUt - half, b.NodeUt + half);
            });

        private static double WrapPi(double radians)
        {
            double w = Angles.Wrap(radians);
            return w > Math.PI ? w - Angles.TwoPi : w;
        }

        private static string Km(double metres) => (metres / 1000).ToString("0", CultureInfo.InvariantCulture) + " km";

        private readonly struct Window
        {
            public Window(double start, double end)
            {
                Start = start;
                End = end;
            }

            public double Start { get; }

            public double End { get; }

            public bool Overlaps(IEnumerable<Window> others)
            {
                double start = Start;
                double end = End;
                return others.Any(o => start < o.End && o.Start < end);
            }

            /// <summary>The earliest centre at or after <paramref name="ut"/> whose window of +-<paramref name="half"/> is clear.</summary>
            public static double FirstFree(double ut, double half, List<Window> busy)
            {
                for (int guard = 0; guard <= busy.Count; guard++)
                {
                    var window = new Window(ut - half, ut + half);
                    Window? hit = busy.Where(o => window.Start < o.End && o.Start < window.End).Select(o => (Window?)o).FirstOrDefault();
                    if (hit == null)
                    {
                        return ut;
                    }
                    ut = hit.Value.End + half;
                }
                return ut;
            }
        }
    }
}
