using System.Collections.Generic;
using System.Linq;

namespace FLTCOM.Core.AutoSystems
{
    /// <summary>
    /// Staging for a vessel flown on rails. Only staging carries over from the auto systems: panels, gear, fairings and
    /// chutes need a loaded vessel. The spent stages stay in the save; the burn flies on the mass without them, as the planner does.
    /// </summary>
    public static class BackgroundStaging
    {
        public const string Scope = "auto systems: staging only in background";

        /// <summary>
        /// Firing <paramref name="target"/>'s engines leaves a spent stage behind: the save still holds an engine or tank that a
        /// decoupler at or after <paramref name="target"/> drops. A leftover decoupler alone does not count.
        /// </summary>
        public static bool Needed(IReadOnlyList<StagePart> parts, int target) =>
            parts.Any(p => p.DecoupledIn >= target && (p.Is(PartRole.Engine) || p.Is(PartRole.Tank)));

        /// <summary>
        /// Why the vessel may not stage on rails so that <paramref name="target"/>'s engines fire, or null when it may or need not.
        /// Each staging on the way obeys <see cref="StagingRule.Held"/>.
        /// </summary>
        public static string? Refusal(IReadOnlyList<StagePart> parts, int target, bool autoSystems)
        {
            if (!Needed(parts, target))
            {
                return null;
            }
            if (!autoSystems)
            {
                return "the engines of stage " + target + " need staging and AUTO SYSTEMS is off";
            }
            var present = parts.ToList();
            for (int stage = parts.Max(p => p.DecoupledIn) + 1; stage > target; stage--)
            {
                string? held = StagingRule.Held(StagingView.Of(present, stage));
                if (held != null)
                {
                    return "cannot stage on rails: " + held;
                }
                present.RemoveAll(p => p.DecoupledIn == stage - 1);
            }
            return null;
        }
    }
}
