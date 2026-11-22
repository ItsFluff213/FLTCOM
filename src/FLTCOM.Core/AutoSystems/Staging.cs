using System.Collections.Generic;
using System.Linq;

namespace FLTCOM.Core.AutoSystems
{
    /// <summary>What the next staging would do to the vessel, as the auto-stager sees it.</summary>
    public sealed class StagingView
    {
        public StagingView(int burningEngines, int droppedBurning, int droppedSpent, bool dropsEmptyTanks, bool releasesClamps, bool enginesRemain, bool firesChutes = false, bool dropsCommand = false)
        {
            BurningEngines = burningEngines;
            DroppedBurning = droppedBurning;
            DroppedSpent = droppedSpent;
            DropsEmptyTanks = dropsEmptyTanks;
            ReleasesClamps = releasesClamps;
            EnginesRemain = enginesRemain;
            FiresChutes = firesChutes;
            DropsCommand = dropsCommand;
        }

        /// <summary>What firing the stage after <paramref name="currentStage"/> would do to the parts still attached.</summary>
        public static StagingView Of(IReadOnlyList<StagePart> parts, int currentStage)
        {
            int next = currentStage - 1;
            var dropped = next >= 0 ? parts.Where(p => p.DecoupledIn == next).ToList() : new List<StagePart>();
            var engines = parts.Where(p => p.Is(PartRole.Engine)).ToList();
            var tanks = dropped.Where(p => p.Is(PartRole.Tank)).ToList();
            return new StagingView(
                engines.Count(e => e.Engine == EngineState.Burning),
                dropped.Count(e => e.Engine == EngineState.Burning),
                dropped.Count(e => e.Engine == EngineState.Spent),
                tanks.Count > 0 && !dropped.Any(p => p.Is(PartRole.Engine)) && tanks.All(t => !t.FuelLeft),
                parts.Any(p => p.Stage == next && p.Is(PartRole.Clamp)),
                engines.Any(e => e.Engine == EngineState.Unlit && e.Stage < currentStage),
                parts.Any(p => p.Stage == next && p.Is(PartRole.Parachute)),
                dropped.Any(p => p.Is(PartRole.Command)) && !parts.Any(p => p.Is(PartRole.Command) && p.DecoupledIn != next));
        }

        /// <summary>Ignited engines that have not flamed out.</summary>
        public int BurningEngines { get; }

        /// <summary>Burning engines the next staging would drop.</summary>
        public int DroppedBurning { get; }

        /// <summary>Flamed-out engines the next staging would drop.</summary>
        public int DroppedSpent { get; }

        /// <summary>The next staging drops tanks with nothing left in them and no engine.</summary>
        public bool DropsEmptyTanks { get; }

        public bool ReleasesClamps { get; }

        /// <summary>Some engine is still waiting for a later stage.</summary>
        public bool EnginesRemain { get; }

        /// <summary>The next stage opens parachutes.</summary>
        public bool FiresChutes { get; }

        /// <summary>The next stage drops the last part that can command the vessel.</summary>
        public bool DropsCommand { get; }
    }

    public static class StagingRule
    {
        /// <summary>
        /// Stage on flameout or empty tanks, to light the first engines or to drop the clamps; never while a dropped engine
        /// burns, never to drop the last command part, and never to open parachutes a landing has not armed.
        /// </summary>
        public static bool ShouldStage(StagingView view, bool chutesArmed = false)
        {
            if (Held(view, chutesArmed) != null || view.DroppedBurning > 0)
            {
                return false;
            }
            if (view.BurningEngines == 0)
            {
                return view.EnginesRemain;
            }
            return view.ReleasesClamps || view.DroppedSpent > 0 || view.DropsEmptyTanks;
        }

        /// <summary>Why the next stage must not fire whatever the engines do, or null when it may.</summary>
        public static string? Held(StagingView view, bool chutesArmed = false) =>
            view.DropsCommand ? "the next stage would drop the probe core or command pod"
            : view.FiresChutes && !chutesArmed ? "the next stage holds parachutes"
            : null;
    }
}
