using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>
    /// Briefs a launch. On the ground: when to lift off, the ascent and an estimate of the burn that finishes the orbit.
    /// In the air, after the ascent: the finishing burns planned from the orbit actually reached.
    /// </summary>
    public static class LaunchBriefing
    {
        public const string AscentLabel = "ascent";

        /// <summary>Rough time from lift-off to the edge of the atmosphere, used only to place the estimated finishing burn.</summary>
        private const double AscentClimb = 240;

        public static bool IsAscent(PlannedBurn burn) => burn.Label == AscentLabel;

        internal static Briefing Build(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, double now, double surfaceGravity, List<Contingency> contingencies)
        {
            LaunchTarget target = change.LaunchTarget!;
            if (tasks.Count != 1)
            {
                contingencies.Add(new Contingency(Severity.Red, tasks.Count > 0 ? tasks[0].VesselId : string.Empty, "a launch flies one vessel at a time"));
                return Empty(operationName, change, contingencies);
            }
            VesselTask task = tasks[0];
            string? invalid = Check(task.Body, target);
            if (invalid != null)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, invalid));
                return Empty(operationName, change, contingencies);
            }
            List<PlannedBurn> burns;
            StageAllocation allocation;
            double need;
            if (task.LaunchSite is LaunchSite site)
            {
                LaunchPlane plane = LaunchWindow.Find(site, target.Inclination, target.Lan, now + BriefingBuilder.Lead);
                if (plane.Note != null)
                {
                    contingencies.Add(new Contingency(Severity.Amber, task.VesselId, plane.Note));
                }
                AscentBudget budget = AscentBudget.Estimate(task.Body, site.At(plane.LaunchUt), plane, target);
                (burns, allocation) = Ground(task, plane, budget, target);
                need = budget.Total;
            }
            else
            {
                OrbitPlan plan = OrbitPlanner.Plan(change, task.Orbit, task.Body, now);
                if (!plan.Ok)
                {
                    contingencies.Add(new Contingency(Severity.Red, task.VesselId, plan.Error!));
                    return Empty(operationName, change, contingencies);
                }
                (burns, allocation) = BriefingBuilder.Time(task, plan, everyOrbit: false);
                burns = burns.Select(b => b.StartUt < now ? Restart(b, now) : b).ToList();
                need = plan.TotalDeltaV;
            }

            double have = task.AvailableDeltaV;
            double margin = burns.Count == 0 ? double.PositiveInfinity : (have - need) / need;
            if (allocation.Uncovered > 0)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "short of delta-v by " + Briefing.F(allocation.Uncovered, "0.0") + " m/s"));
            }
            if (burns.Count > 0 && margin < BriefingBuilder.MarginFloor)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "delta-v margin " + Briefing.F(margin * 100, "0.0") + " % is under 10 %"));
            }
            var stages = allocation.Stages.Select(u => new BriefedStage(task.VesselId, u, surfaceGravity)).ToList();
            return new Briefing(operationName, change.Describe(), burns, need, have, margin, stages, contingencies);
        }

        private static (List<PlannedBurn>, StageAllocation) Ground(VesselTask task, LaunchPlane plane, AscentBudget budget, LaunchTarget target)
        {
            var demands = new List<BurnDemand> { new BurnDemand(budget.Ascent, null), new BurnDemand(budget.Finish, null) };
            StageAllocation allocation = StageAllocation.Allocate(task.Stages, demands);
            BurnTiming ascent = BurnTiming.Compute(task.Stages, allocation.Burns[0].Start, budget.Ascent);
            BurnTiming finish = BurnTiming.Compute(task.Stages, allocation.Burns[1].Start, budget.Finish);
            double finishNode = plane.LaunchUt + Math.Max(AscentClimb, ascent.Duration) + finish.TimeToHalf;
            var burns = new List<PlannedBurn>
            {
                new PlannedBurn(task.VesselId, AscentLabel, plane.LaunchUt, plane.LaunchUt, ascent.Duration, budget.Ascent, 0, 0),
                new PlannedBurn(task.VesselId, "finish the orbit (estimate)", finishNode, finishNode - finish.TimeToHalf, finish.Duration, budget.Finish, 0, 0, expected: Aimed(task.Body, plane, target, finishNode)),
            };
            return (burns, allocation);
        }

        /// <summary>A finishing burn whose ideal start has passed starts now; the trim that follows mops up the error.</summary>
        private static PlannedBurn Restart(PlannedBurn b, double now) =>
            new PlannedBurn(b.VesselId, b.Label, Math.Max(b.NodeUt, now), now, b.Duration, b.Prograde, b.Normal, b.Radial, b.PinnedStage, b.OrbitAfter, b.Body, b.Expected);

        private static OrbitElements Aimed(CentralBody body, LaunchPlane plane, LaunchTarget target, double ut)
        {
            double rp = body.Radius + target.Periapsis;
            double ra = body.Radius + target.Apoapsis;
            return new OrbitElements(body.GravParameter, (rp + ra) / 2, (ra - rp) / (ra + rp), plane.Inclination, plane.Lan, 0, 0, ut);
        }

        private static string? Check(CentralBody body, LaunchTarget target)
        {
            double floor = body.SafeFloor;
            if (target.Periapsis <= floor)
            {
                return "the periapsis must be above " + Briefing.F(floor / 1000, "0.#") + " km to stay up";
            }
            if (body.Radius + target.Apoapsis >= body.SphereOfInfluence)
            {
                return "apoapsis would leave " + body.Name + "'s sphere of influence";
            }
            if (target.Inclination is double inc && (inc < 0 || inc > Math.PI))
            {
                return "inclination must be between 0 and 180 degrees";
            }
            return null;
        }

        private static Briefing Empty(string operationName, OrbitChange change, List<Contingency> contingencies) =>
            new Briefing(operationName, change.Describe(), Array.Empty<PlannedBurn>(), 0, 0, double.PositiveInfinity, Array.Empty<BriefedStage>(), contingencies);
    }
}
