using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.Scheduling;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>One vessel's part of an operation. PinnedStages, when given, pins burn i to a KSP stage number.</summary>
    public sealed class VesselTask
    {
        public VesselTask(string vesselId, string vesselName, CentralBody body, OrbitElements? orbit, IReadOnlyList<StageDeltaV> stages, IReadOnlyList<int?>? pinnedStages = null, LaunchSite? launchSite = null, SolarSystem? system = null, Patch? trajectory = null, LandingContext? landing = null, IReadOnlyList<Constellation.CarrierPart>? carrier = null)
        {
            Carrier = carrier;
            if (orbit == null && trajectory == null)
            {
                throw new ArgumentNullException(nameof(orbit), "a vessel needs its orbit or, on an escape path, its trajectory");
            }
            System = system;
            Trajectory = trajectory;
            LaunchSite = launchSite;
            Landing = landing;
            VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            VesselName = vesselName ?? throw new ArgumentNullException(nameof(vesselName));
            Body = body ?? throw new ArgumentNullException(nameof(body));
            _orbit = orbit;
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            PinnedStages = pinnedStages ?? Array.Empty<int?>();
        }

        public string VesselId { get; }

        public string VesselName { get; }

        public CentralBody Body { get; }

        private readonly OrbitElements? _orbit;

        /// <summary>The closed orbit around <see cref="Body"/>; a vessel on an escape path has none, and only a transfer plans from it.</summary>
        public OrbitElements Orbit => _orbit ?? throw new InvalidOperationException(VesselName + " is on an escape path; only a transfer can be planned from it");

        /// <summary>The bodies around, for a transfer; null when not read.</summary>
        public SolarSystem? System { get; }

        /// <summary>The vessel's state relative to <see cref="Body"/> now, closed orbit or not; null when not read.</summary>
        public Patch? Trajectory { get; }

        /// <summary>Stages in firing order, the bottom of the stack first.</summary>
        public IReadOnlyList<StageDeltaV> Stages { get; }

        public IReadOnlyList<int?> PinnedStages { get; }

        /// <summary>Where the vessel stands when it is on the ground; null in flight.</summary>
        public LaunchSite? LaunchSite { get; }

        /// <summary>What a landing needs beyond the orbit: the spinning body, its air, the drag and the target height.</summary>
        public LandingContext? Landing { get; }

        /// <summary>Every part, for payload detection; null when not read.</summary>
        public IReadOnlyList<Constellation.CarrierPart>? Carrier { get; }

        public double AvailableDeltaV => Stages.Sum(s => s.DeltaV);
    }

    public enum Severity
    {
        Red,
        Amber,
    }

    public sealed class Contingency
    {
        public Contingency(Severity severity, string vesselId, string text)
        {
            Severity = severity;
            VesselId = vesselId;
            Text = text;
        }

        public Severity Severity { get; }

        public string VesselId { get; }

        public string Text { get; }
    }

    public sealed class BriefedStage
    {
        public BriefedStage(string vesselId, StageUse use, double surfaceGravity)
        {
            VesselId = vesselId;
            Stage = use.Stage.Stage;
            DeltaV = use.Stage.DeltaV;
            Assigned = use.Assigned;
            Twr = use.Stage.Twr(surfaceGravity);
            BurnTime = use.Stage.BurnTime;
            Short = use.Short;
        }

        public string VesselId { get; }

        public int Stage { get; }

        public double DeltaV { get; }

        public double Assigned { get; }

        public double Twr { get; }

        public double BurnTime { get; }

        /// <summary>The stage cannot cover the burns pinned to it. Shown red.</summary>
        public bool Short { get; }
    }

    public sealed class Briefing
    {
        public Briefing(string operationName, string change, IReadOnlyList<PlannedBurn> burns, double requiredDeltaV, double availableDeltaV, double margin, IReadOnlyList<BriefedStage> stages, IReadOnlyList<Contingency> contingencies, PorkchopGrid? porkchop = null, LandingPrediction? landing = null, Constellation.DeployPlan? deployment = null)
        {
            Deployment = deployment;
            Porkchop = porkchop;
            OperationName = operationName;
            Change = change;
            Burns = burns;
            RequiredDeltaV = requiredDeltaV;
            AvailableDeltaV = availableDeltaV;
            Margin = margin;
            Stages = stages;
            Contingencies = contingencies;
            Landing = landing;
        }

        /// <summary>The window search behind a transfer between planets, null otherwise.</summary>
        public PorkchopGrid? Porkchop { get; }

        public string OperationName { get; }

        public string Change { get; }

        /// <summary>Every vessel's burns, in start-time order.</summary>
        public IReadOnlyList<PlannedBurn> Burns { get; }

        public double RequiredDeltaV { get; }

        public double AvailableDeltaV { get; }

        /// <summary>The smallest margin of any vessel: spare delta-v over required delta-v. Infinite when nothing burns.</summary>
        public double Margin { get; }

        public IReadOnlyList<BriefedStage> Stages { get; }

        public IReadOnlyList<Contingency> Contingencies { get; }

        /// <summary>Where a landing is predicted to touch down; null for every other kind.</summary>
        public LandingPrediction? Landing { get; }

        /// <summary>The releases and the carrier's return behind a constellation deployment, null otherwise.</summary>
        public Constellation.DeployPlan? Deployment { get; }

        public bool CanApprove => Contingencies.All(c => c.Severity != Severity.Red);

        public Briefing Adding(IEnumerable<Contingency> more) =>
            new Briefing(OperationName, Change, Burns, RequiredDeltaV, AvailableDeltaV, Margin, Stages, Contingencies.Concat(more).ToList(), Porkchop, Landing, Deployment);

        public string Format(double now)
        {
            var s = new StringBuilder();
            s.Append("OPERATION ").Append(OperationName.ToUpperInvariant()).Append("  ").Append(Change).Append('\n');
            s.Append("now ").Append(KerbinTime.Format(now)).Append('\n');
            s.Append('\n').Append("BURNS\n");
            foreach (PlannedBurn b in Burns)
            {
                s.Append("  ").Append(b.VesselId).Append("  ").Append(b.Label).Append('\n')
                    .Append("    node ").Append(KerbinTime.Format(b.NodeUt)).Append("  (T-").Append(KerbinTime.Span(b.NodeUt - now)).Append(")\n")
                    .Append("    ignite ").Append(KerbinTime.Format(b.StartUt)).Append("  burn ").Append(F(b.Duration, "0.0")).Append(" s\n")
                    .Append("    dv ").Append(F(b.DeltaV, "0.0")).Append(" m/s  (prograde ").Append(F(b.Prograde, "0.0"))
                    .Append(", normal ").Append(F(b.Normal, "0.0")).Append(", radial ").Append(F(b.Radial, "0.0")).Append(")\n");
            }
            s.Append('\n').Append("DELTA-V  required ").Append(F(RequiredDeltaV, "0.0")).Append(" m/s, available ")
                .Append(F(AvailableDeltaV, "0.0")).Append(" m/s, margin ")
                .Append(double.IsInfinity(Margin) ? "n/a" : F(Margin * 100, "0.0") + " %").Append('\n');
            s.Append('\n').Append("STAGES\n");
            foreach (BriefedStage st in Stages)
            {
                s.Append("  ").Append(st.VesselId).Append(" stage ").Append(st.Stage)
                    .Append("  dv ").Append(F(st.DeltaV, "0")).Append(" m/s  assigned ").Append(F(st.Assigned, "0"))
                    .Append(" m/s  TWR ").Append(F(st.Twr, "0.00")).Append("  burn ").Append(F(st.BurnTime, "0")).Append(" s")
                    .Append(st.Short ? "  RED" : string.Empty).Append('\n');
            }
            s.Append('\n').Append("CONTINGENCIES\n");
            if (Contingencies.Count == 0)
            {
                s.Append("  none\n");
            }
            foreach (Contingency c in Contingencies)
            {
                s.Append("  ").Append(c.Severity == Severity.Red ? "RED   " : "AMBER ").Append(c.VesselId).Append(": ").Append(c.Text).Append('\n');
            }
            s.Append('\n').Append(CanApprove ? "READY FOR APPROVAL" : "CANNOT APPROVE: clear the red contingencies").Append('\n');
            return s.ToString();
        }

        internal static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
    }

    public static class BriefingBuilder
    {
        public const double MarginFloor = 0.10;
        public const double Lead = 60;
        public const double LongBurnFraction = 0.1;

        /// <param name="gates">Contingencies the caller found before planning, such as a member without signal.</param>
        /// <param name="target">The vessel a rendezvous meets; null for every other kind, or when the target is gone.</param>
        public static Briefing Build(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, double now, double surfaceGravity = DeltaVEstimator.G0, IReadOnlyList<Contingency>? gates = null, VesselTask? target = null)
        {
            Briefing b = Plan(operationName, change, tasks, now, surfaceGravity, gates, target);
            if (change.Kind == OrbitChangeKind.Launch || change.Kind == OrbitChangeKind.Land)
            {
                return b;
            }
            var ground = FlightSafety.Check(tasks, b.Burns).ToList();
            return ground.Count == 0 ? b
                : new Briefing(b.OperationName, b.Change, b.Burns, b.RequiredDeltaV, b.AvailableDeltaV, b.Margin, b.Stages, b.Contingencies.Concat(ground).ToList(), b.Porkchop, b.Landing, b.Deployment);
        }

        private static Briefing Plan(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, double now, double surfaceGravity, IReadOnlyList<Contingency>? gates, VesselTask? target)
        {
            var burns = new List<PlannedBurn>();
            var stages = new List<BriefedStage>();
            var contingencies = new List<Contingency>(gates ?? Array.Empty<Contingency>());
            if (change.Kind == OrbitChangeKind.Launch)
            {
                return LaunchBriefing.Build(operationName, change, tasks, now, surfaceGravity, contingencies);
            }
            if (change.Kind == OrbitChangeKind.Transfer)
            {
                return TransferBriefing.Build(operationName, change, tasks, now, surfaceGravity, contingencies);
            }
            if (OrbitChange.Meets(change.Kind))
            {
                return RendezvousBriefing.Build(operationName, change, tasks, target, now, surfaceGravity, contingencies);
            }
            if (change.Kind == OrbitChangeKind.Land)
            {
                return LandingBriefing.Build(operationName, change, tasks, now, surfaceGravity, contingencies);
            }
            if (change.Kind == OrbitChangeKind.DeployConstellation)
            {
                return Constellation.DeployBriefing.Build(operationName, change, tasks[0], now + Lead, contingencies);
            }
            double required = 0;
            double available = 0;
            double margin = double.PositiveInfinity;
            bool group = tasks.Count > 1;

            IReadOnlyDictionary<string, OrbitPlan> plans = GroupPlanner.Plan(change, tasks, now + Lead);
            double late = tasks
                .Where(t => plans[t.VesselId].Ok && plans[t.VesselId].Burns.Count > 0)
                .Select(t => Time(t, plans[t.VesselId], group).Burns)
                .Where(b => b[0].StartUt < now + 10)
                .Select(b => b[0].NodeUt - b[0].StartUt)
                .DefaultIfEmpty(0)
                .Max();
            if (late > 0)
            {
                plans = GroupPlanner.Plan(change, tasks, now + Lead + late);
            }

            foreach (VesselTask task in tasks)
            {
                OrbitPlan plan = plans[task.VesselId];
                if (!plan.Ok)
                {
                    contingencies.Add(new Contingency(Severity.Red, task.VesselId, plan.Error!));
                    continue;
                }
                (List<PlannedBurn> vesselBurns, StageAllocation allocation) = Time(task, plan, group);

                double need = plan.TotalDeltaV;
                double have = task.AvailableDeltaV;
                required += need;
                available += have;
                burns.AddRange(vesselBurns);
                stages.AddRange(allocation.Stages.Select(u => new BriefedStage(task.VesselId, u, surfaceGravity)));

                if (vesselBurns.Count == 0)
                {
                    contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "the orbit already matches; nothing to burn"));
                    continue;
                }
                double vesselMargin = (have - need) / need;
                margin = Math.Min(margin, vesselMargin);
                if (allocation.Uncovered > 0)
                {
                    contingencies.Add(new Contingency(Severity.Red, task.VesselId, "short of delta-v by " + Briefing.F(allocation.Uncovered, "0.0") + " m/s"));
                }
                foreach (StageUse use in allocation.Stages.Where(u => u.Short))
                {
                    contingencies.Add(new Contingency(Severity.Red, task.VesselId, "stage " + use.Stage.Stage + " cannot cover its burns (short " + Briefing.F(use.Shortfall, "0.0") + " m/s)"));
                }
                for (int i = 0; i < vesselBurns.Count; i++)
                {
                    int? pin = vesselBurns[i].PinnedStage;
                    if (pin is int stage && !allocation.Burns[i].Covered && !allocation.Stages.Any(u => u.Stage.Stage == stage && u.Short))
                    {
                        contingencies.Add(new Contingency(Severity.Red, task.VesselId, "\"" + vesselBurns[i].Label + "\" is pinned to stage " + stage + ", which is gone by then"));
                    }
                    if (vesselBurns[i].Duration > LongBurnFraction * task.Orbit.Period)
                    {
                        contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "\"" + vesselBurns[i].Label + "\" burns " + Briefing.F(vesselBurns[i].Duration, "0") + " s, a long burn; expect a correction"));
                    }
                }
                if (vesselMargin < MarginFloor)
                {
                    contingencies.Add(new Contingency(Severity.Red, task.VesselId, "delta-v margin " + Briefing.F(vesselMargin * 100, "0.0") + " % is under 10 %"));
                }
            }

            burns.Sort((a, b) => a.StartUt.CompareTo(b.StartUt));
            var scheduled = burns.Select((b, i) => new ScheduledBurn(operationName, i, b)).ToList();
            foreach (BurnConflict conflict in Schedule.Conflicts(scheduled))
            {
                contingencies.Add(new Contingency(Severity.Amber, conflict.First.Burn.VesselId, "two burns at once: " + conflict.Describe()));
            }
            return new Briefing(operationName, change.Describe(), burns, required, available, margin, stages, contingencies);
        }

        /// <param name="everyOrbit">Keep the planned orbit after every burn, not only the last, so a hand-flown burn can be recognised.</param>
        internal static (List<PlannedBurn> Burns, StageAllocation Allocation) Time(VesselTask task, OrbitPlan plan, bool everyOrbit)
        {
            var demands = plan.Burns.Select((b, i) => new BurnDemand(b.DeltaV, i < task.PinnedStages.Count ? task.PinnedStages[i] : null)).ToList();
            StageAllocation allocation = StageAllocation.Allocate(task.Stages, demands);
            var burns = new List<PlannedBurn>(plan.Burns.Count);
            for (int i = 0; i < plan.Burns.Count; i++)
            {
                ImpulsiveBurn b = plan.Burns[i];
                BurnTiming timing = BurnTiming.Compute(task.Stages, allocation.Burns[i].Start, b.DeltaV);
                burns.Add(new PlannedBurn(task.VesselId, b.Label, b.NodeUt, b.NodeUt - timing.TimeToHalf, timing.Duration, b.Prograde, b.Normal, b.Radial, demands[i].PinnedStage, i == plan.Burns.Count - 1 ? plan.Final : everyOrbit ? b.OrbitAfter : null, b.Body, i == plan.Burns.Count - 1 ? plan.Final : b.OrbitAfter));
            }
            return (burns, allocation);
        }
    }

    /// <summary>Kerbin calendar: 6-hour days, 426-day years, both counted from 1.</summary>
    public static class KerbinTime
    {
        public const double Day = 6 * 3600;
        public const double Year = 426 * Day;

        public static string Format(double ut)
        {
            long t = (long)Math.Floor(ut);
            long year = t / (long)Year + 1;
            long rest = t % (long)Year;
            long day = rest / (long)Day + 1;
            return "Y" + year.ToString(CultureInfo.InvariantCulture) + " D" + day.ToString(CultureInfo.InvariantCulture) + " " + Clock(rest % (long)Day);
        }

        public static string Span(double seconds)
        {
            long t = (long)Math.Max(0, Math.Round(seconds));
            long days = t / (long)Day;
            return (days > 0 ? days.ToString(CultureInfo.InvariantCulture) + "d " : string.Empty) + Clock(t % (long)Day);
        }

        private static string Clock(long seconds) =>
            (seconds / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
