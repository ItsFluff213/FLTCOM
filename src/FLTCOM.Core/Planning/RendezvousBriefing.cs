using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;

namespace FLTCOM.Core.Planning
{
    /// <summary>Briefs one vessel meeting another in the same sphere of influence.</summary>
    public static class RendezvousBriefing
    {
        internal static Briefing Build(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, VesselTask? target, double now, double surfaceGravity, List<Contingency> contingencies)
        {
            string chaserId = tasks.Count > 0 ? tasks[0].VesselId : string.Empty;
            string? invalid = tasks.Count != 1 ? "a rendezvous flies one vessel at a time"
                : target == null ? change.Rendezvous!.Name + " is not in the fleet"
                : target.VesselId == chaserId ? "a vessel cannot meet itself"
                : tasks[0].LaunchSite != null ? "launch first; a rendezvous starts in orbit"
                : target.Body.Name != tasks[0].Body.Name ? target.VesselName + " orbits " + target.Body.Name + "; a rendezvous needs both vessels around the same body"
                : null;
            if (invalid != null)
            {
                contingencies.Add(new Contingency(Severity.Red, chaserId, invalid));
                return Empty(operationName, change, contingencies);
            }
            VesselTask task = tasks[0];
            OrbitPlan plan = Rendezvous.Plan(task.Orbit, target!.Orbit, task.Body, now, now + BriefingBuilder.Lead);
            if (!plan.Ok)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, plan.Error!));
                return Empty(operationName, change, contingencies);
            }
            (List<PlannedBurn> burns, StageAllocation allocation) = BriefingBuilder.Time(task, plan, everyOrbit: false);
            if (burns.Count > 0 && burns[0].StartUt < now + 10)
            {
                plan = Rendezvous.Plan(task.Orbit, target.Orbit, task.Body, now, now + BriefingBuilder.Lead + burns[0].NodeUt - burns[0].StartUt);
                (burns, allocation) = BriefingBuilder.Time(task, plan, everyOrbit: false);
            }
            if (change.Kind == OrbitChangeKind.Dock)
            {
                PlannedBurn? last = burns.Count == 0 ? null : burns[burns.Count - 1];
                burns.Add(DockingApproach.Burn(task.VesselId, last == null ? now + BriefingBuilder.Lead : last.StartUt + last.Duration + Rendezvous.BurnGap));
            }
            else if (burns.Count == 0)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "already holding beside " + target.VesselName + "; nothing to burn"));
            }

            double need = plan.TotalDeltaV;
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

        private static Briefing Empty(string operationName, OrbitChange change, List<Contingency> contingencies) =>
            new Briefing(operationName, change.Describe(), Array.Empty<PlannedBurn>(), 0, 0, double.PositiveInfinity, Array.Empty<BriefedStage>(), contingencies);
    }
}
