using System;
using System.Collections.Generic;
using FLTCOM.Core.Missions;

namespace FLTCOM.Core.Scheduling
{
    public sealed class ScheduledBurn
    {
        public ScheduledBurn(string operationId, int burnIndex, PlannedBurn burn)
        {
            OperationId = operationId;
            BurnIndex = burnIndex;
            Burn = burn;
        }

        public string OperationId { get; }

        public int BurnIndex { get; }

        public PlannedBurn Burn { get; }
    }

    /// <summary>Two burns of the same vessel whose burn windows overlap.</summary>
    public sealed class BurnConflict
    {
        public BurnConflict(ScheduledBurn first, ScheduledBurn second)
        {
            First = first;
            Second = second;
        }

        public ScheduledBurn First { get; }

        public ScheduledBurn Second { get; }

        public string Describe() =>
            First.Burn.VesselId + ": \"" + First.Burn.Label + "\" (" + First.OperationId + ") overlaps \"" + Second.Burn.Label + "\" (" + Second.OperationId + ")";
    }

    public sealed class ScheduleTick
    {
        public ScheduleTick(IReadOnlyList<ScheduledBurn> ordered, IReadOnlyList<BurnConflict> conflicts, ScheduledBurn? next, SwitchDecision decision)
        {
            Ordered = ordered;
            Conflicts = conflicts;
            Next = next;
            Decision = decision;
        }

        public IReadOnlyList<ScheduledBurn> Ordered { get; }

        public IReadOnlyList<BurnConflict> Conflicts { get; }

        /// <summary>The earliest burn not yet over.</summary>
        public ScheduledBurn? Next { get; }

        public SwitchDecision Decision { get; }
    }

    /// <summary>
    /// Orders every uploaded burn in the fleet by start time. Many vessels may burn at once; only two burns of one
    /// vessel at once is a conflict.
    /// </summary>
    public static class Schedule
    {
        public const double SwitchLead = 120;

        public static IReadOnlyList<ScheduledBurn> Collect(IEnumerable<Operation> operations)
        {
            var burns = new List<ScheduledBurn>();
            foreach (Operation op in operations)
            {
                if (op.State != StepState.Uploaded && op.State != StepState.Waiting && op.State != StepState.Executing)
                {
                    continue;
                }
                int first = op.Progress?.BurnIndex ?? 0;
                for (int i = first; i < op.Burns.Count; i++)
                {
                    burns.Add(new ScheduledBurn(op.Id, i, op.Burns[i]));
                }
            }
            return burns;
        }

        public static IReadOnlyList<BurnConflict> Conflicts(IReadOnlyList<ScheduledBurn> ordered)
        {
            var conflicts = new List<BurnConflict>();
            var latestByVessel = new Dictionary<string, ScheduledBurn>();
            foreach (ScheduledBurn burn in ordered)
            {
                if (latestByVessel.TryGetValue(burn.Burn.VesselId, out ScheduledBurn? previous) && burn.Burn.StartUt < previous.Burn.EndUt)
                {
                    conflicts.Add(new BurnConflict(previous, burn));
                }
                if (previous == null || burn.Burn.EndUt > previous.Burn.EndUt)
                {
                    latestByVessel[burn.Burn.VesselId] = burn;
                }
            }
            return conflicts;
        }

        public static ScheduleTick Tick(IReadOnlyList<ScheduledBurn> burns, double now, ActiveView view)
        {
            var ordered = new List<ScheduledBurn>(burns);
            ordered.Sort((a, b) => a.Burn.StartUt.CompareTo(b.Burn.StartUt));
            ScheduledBurn? next = ordered.Find(b => b.Burn.EndUt > now);
            SwitchDecision decision = next == null || next.Burn.StartUt - now > SwitchLead
                ? SwitchDecision.Stay
                : SwitchPolicy.Decide(view, next.Burn.VesselId);
            return new ScheduleTick(ordered, Conflicts(ordered), next, decision);
        }
    }
}
