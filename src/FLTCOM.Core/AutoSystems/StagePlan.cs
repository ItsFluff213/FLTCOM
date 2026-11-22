using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Planning;

namespace FLTCOM.Core.AutoSystems
{
    /// <summary>One stage of the briefing's figures: the delta-v the operation draws from it and its full-throttle burn time.</summary>
    public sealed class StageFigure
    {
        public StageFigure(int stage, double assigned, double burnTime)
        {
            Stage = stage;
            Assigned = assigned;
            BurnTime = burnTime;
        }

        public int Stage { get; }

        public double Assigned { get; }

        public double BurnTime { get; }
    }

    /// <summary>What the stage plan needs to know about the operation.</summary>
    public sealed class StagePlanContext
    {
        public StagePlanContext(OrbitChangeKind kind, double atmosphereDepth, IReadOnlyList<StageFigure> figures, bool autoSystems = true)
        {
            Kind = kind;
            AtmosphereDepth = atmosphereDepth;
            Figures = figures ?? throw new ArgumentNullException(nameof(figures));
            AutoSystems = autoSystems;
        }

        public OrbitChangeKind Kind { get; }

        /// <summary>Height of the air at the body the operation starts at; 0 for a body without air.</summary>
        public double AtmosphereDepth { get; }

        public IReadOnlyList<StageFigure> Figures { get; }

        public bool AutoSystems { get; }
    }

    /// <summary>One line of the stage plan: what firing the stage does, and when it fires in this operation.</summary>
    public sealed class StageStep
    {
        public StageStep(int stage, string does, string when, bool fires, Severity? hazard = null, string? warning = null)
        {
            Stage = stage;
            Does = does;
            When = when;
            Fires = fires;
            Hazard = hazard;
            Warning = warning;
        }

        public int Stage { get; }

        public string Does { get; }

        public string When { get; }

        /// <summary>The auto-stager fires this stage to reach the burns.</summary>
        public bool Fires { get; }

        public Severity? Hazard { get; }

        public string? Warning { get; }

        public string Describe() => "stage " + Stage + ": " + Does + (When.Length > 0 ? ", " + When : "") + (Warning != null ? " (" + Warning + ")" : "");
    }

    /// <summary>
    /// Reads a stage stack and says what each stage does and when it fires. The operation fires every stage from the next
    /// one down to the lowest stage it draws delta-v from; the stages below it are kept. A hazard on a stage the operation
    /// fires is red, because the stager will hold there; on a kept stage it is amber.
    /// </summary>
    public static class StagePlanner
    {
        public static IReadOnlyList<StageStep> Plan(IReadOnlyList<StagePart> parts, int currentStage, StagePlanContext context)
        {
            int lowest = context.Figures.Where(f => f.Assigned > 0 && f.Stage < currentStage).Select(f => f.Stage).DefaultIfEmpty(currentStage).Min();
            var steps = new List<StageStep>();
            for (int s = currentStage - 1; s >= 0; s--)
            {
                bool fires = s >= lowest;
                var (hazard, warning) = Hazard(parts, s, fires, lowest, context);
                steps.Add(new StageStep(s, Does(parts, s), When(parts, s, currentStage, fires, context), fires, hazard, warning));
            }
            return steps;
        }

        private static string Does(IReadOnlyList<StagePart> parts, int s)
        {
            var dropped = parts.Where(p => p.DecoupledIn == s).ToList();
            var droppedEngines = dropped.Where(p => p.Is(PartRole.Engine) && p.Stage > s).ToList();
            var things = new List<string>();
            if (droppedEngines.Count > 0)
            {
                bool othersBurn = parts.Any(p => p.Is(PartRole.Engine) && p.Stage > s && p.DecoupledIn < s);
                things.Add(othersBurn ? "drop " + Count(droppedEngines.Count, "booster") : "drop the spent stage");
            }
            else if (dropped.Any(p => p.Is(PartRole.Tank)))
            {
                things.Add("drop " + Count(dropped.Count(p => p.Is(PartRole.Tank)), "tank"));
            }
            else if (dropped.Count > 0 || parts.Any(p => p.Stage == s && p.Is(PartRole.Decoupler)))
            {
                things.Add("separate");
            }
            int lit = parts.Count(p => p.Stage == s && p.Is(PartRole.Engine) && p.DecoupledIn != s);
            if (lit > 0)
            {
                things.Add("light " + Count(lit, "engine"));
            }
            if (parts.Any(p => p.Stage == s && p.Is(PartRole.Engine) && p.DecoupledIn == s))
            {
                things.Add("fire separation motors");
            }
            if (parts.Any(p => p.Stage == s && p.Is(PartRole.Clamp)))
            {
                things.Add("release the clamps");
            }
            if (parts.Any(p => p.Stage == s && p.Is(PartRole.Fairing)))
            {
                things.Add("jettison the fairing");
            }
            if (parts.Any(p => p.Stage == s && p.Is(PartRole.Parachute)))
            {
                things.Add("parachutes");
            }
            return things.Count > 0 ? string.Join(", ", things) : "nothing staged";
        }

        private static string When(IReadOnlyList<StagePart> parts, int s, int currentStage, bool fires, StagePlanContext context)
        {
            bool chutes = parts.Any(p => p.Stage == s && p.Is(PartRole.Parachute));
            if (!fires)
            {
                if (chutes && context.Kind == OrbitChangeKind.Land && context.AtmosphereDepth > 0)
                {
                    return "kept for landing";
                }
                if (parts.Any(p => p.Stage == s && p.Is(PartRole.Fairing)) && context.AutoSystems && context.Kind == OrbitChangeKind.Launch)
                {
                    return FairingRule.When(context.AtmosphereDepth);
                }
                return "kept";
            }
            if (context.Kind == OrbitChangeKind.Launch)
            {
                if (s == currentStage - 1)
                {
                    return "at liftoff";
                }
                double t = context.Figures.Where(f => f.Stage > s && f.Stage < currentStage).Sum(f => f.BurnTime);
                return "at " + Clock(t);
            }
            if (s == currentStage - 1 && !parts.Any(p => p.Engine == EngineState.Burning))
            {
                return "at the first burn";
            }
            return "when the stage above runs dry";
        }

        private static (Severity?, string?) Hazard(IReadOnlyList<StagePart> parts, int s, bool fires, int lowest, StagePlanContext context)
        {
            var dropped = parts.Where(p => p.DecoupledIn == s).ToList();
            string? warning =
                dropped.Any(p => p.Is(PartRole.Command)) && !parts.Any(p => p.Is(PartRole.Command) && p.DecoupledIn < s) ? "drops the probe core or command pod"
                : dropped.Any(p => p.Is(PartRole.Engine) && p.Stage < s) ? "drops an engine before it fires"
                : fires && parts.Any(p => p.Stage == s && p.Is(PartRole.Parachute))
                    ? context.AtmosphereDepth > 0 ? "opens parachutes before the burns of stage " + lowest : "opens parachutes in vacuum"
                : dropped.Any(p => p.Is(PartRole.Parachute) && p.Stage < s) ? "drops parachutes before they open"
                : null;
            return warning == null ? (null, null) : (fires ? Severity.Red : Severity.Amber, warning);
        }

        private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");

        public static string Clock(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0, seconds));
            return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
