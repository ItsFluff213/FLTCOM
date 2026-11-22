using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;

namespace FLTCOM.Core.Planning
{
    /// <summary>Briefs a flight to another body: the ejection, any mid-course correction and the capture, timed on the vessel's stages.</summary>
    public static class TransferBriefing
    {
        internal static Briefing Build(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, double now, double surfaceGravity, List<Contingency> contingencies)
        {
            if (tasks.Count != 1)
            {
                contingencies.Add(new Contingency(Severity.Red, tasks.Count > 0 ? tasks[0].VesselId : string.Empty, "a transfer flies one vessel at a time"));
                return Empty(operationName, change, contingencies);
            }
            VesselTask task = tasks[0];
            if (task.System == null || task.Trajectory == null)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "the solar system was not read; cannot plan a transfer"));
                return Empty(operationName, change, contingencies);
            }
            if (task.LaunchSite != null)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "launch to orbit first; a transfer leaves from orbit"));
                return Empty(operationName, change, contingencies);
            }
            TransferPlan transfer = TransferPlanner.Plan(task.System, task.Trajectory.Value, change.Destination!, change.Target, now + BriefingBuilder.Lead, change.ArrivalInclination);
            if (!transfer.Plan.Ok)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, transfer.Plan.Error!));
                return Empty(operationName, change, contingencies);
            }
            contingencies.AddRange(transfer.Notes.Select(n => new Contingency(Severity.Amber, task.VesselId, n)));
            (List<PlannedBurn> burns, StageAllocation allocation) = BriefingBuilder.Time(task, transfer.Plan, everyOrbit: true);
            double need = transfer.Plan.TotalDeltaV;
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
            if (burns.Count > 0 && burns[0].StartUt < now)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "\"" + burns[0].Label + "\" should already have started; brief again for the next window"));
            }
            var stages = allocation.Stages.Select(u => new BriefedStage(task.VesselId, u, surfaceGravity)).ToList();
            return new Briefing(operationName, change.Describe(), burns, need, have, margin, stages, contingencies, transfer.Porkchop);
        }

        private static Briefing Empty(string operationName, OrbitChange change, List<Contingency> contingencies) =>
            new Briefing(operationName, change.Describe(), new List<PlannedBurn>(), 0, 0, double.PositiveInfinity, new List<BriefedStage>(), contingencies);
    }
}
