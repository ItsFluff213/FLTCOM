using System;
using System.Collections.Generic;

namespace FLTCOM.Core.Propulsion
{
    /// <summary>Where a burn starts in the stage stack: index into the firing-order stage list and delta-v that stage already spent.</summary>
    public readonly struct StageCursor
    {
        public StageCursor(int stageIndex, double used)
        {
            StageIndex = stageIndex;
            Used = used;
        }

        public int StageIndex { get; }

        public double Used { get; }
    }

    public readonly struct BurnTiming
    {
        public BurnTiming(double duration, double timeToHalf, bool fits)
        {
            Duration = duration;
            TimeToHalf = timeToHalf;
            Fits = fits;
        }

        /// <summary>Seconds of engine time for the whole burn, staging counted as instant.</summary>
        public double Duration { get; }

        /// <summary>Seconds from ignition until half the delta-v is spent. Ignite this long before the node.</summary>
        public double TimeToHalf { get; }

        public bool Fits { get; }

        /// <summary>
        /// Integrates the rocket equation through <paramref name="stages"/> (firing order) from <paramref name="start"/>.
        /// Mass falls during the burn, so the half-delta-v point comes after half the burn time.
        /// </summary>
        public static BurnTiming Compute(IReadOnlyList<StageDeltaV> stages, StageCursor start, double deltaV)
        {
            double full = TimeFor(stages, start, deltaV, out bool fits);
            double half = TimeFor(stages, start, deltaV / 2, out _);
            return new BurnTiming(full, half, fits);
        }

        private static double TimeFor(IReadOnlyList<StageDeltaV> stages, StageCursor start, double deltaV, out bool fits)
        {
            double need = deltaV;
            double time = 0;
            double used = start.Used;
            for (int i = start.StageIndex; i < stages.Count && need > 0; i++)
            {
                StageDeltaV stage = stages[i];
                double take = Math.Min(need, stage.DeltaV - used);
                if (take > 0)
                {
                    double ve = stage.ExhaustVelocity;
                    double m0 = stage.StartMass * Math.Exp(-used / ve);
                    double m1 = m0 * Math.Exp(-take / ve);
                    time += (m0 - m1) / stage.MassFlow;
                    need -= take;
                }
                used = 0;
            }
            fits = need <= 1e-6;
            return time;
        }
    }

    /// <summary>One burn's demand on the stage stack. PinnedStage is a KSP stage number, or null to draw automatically.</summary>
    public readonly struct BurnDemand
    {
        public BurnDemand(double deltaV, int? pinnedStage = null)
        {
            DeltaV = deltaV;
            PinnedStage = pinnedStage;
        }

        public double DeltaV { get; }

        public int? PinnedStage { get; }
    }

    public sealed class StageUse
    {
        public StageUse(StageDeltaV stage)
        {
            Stage = stage;
        }

        public StageDeltaV Stage { get; }

        public double Assigned { get; internal set; }

        /// <summary>Delta-v this stage was asked for but could not give.</summary>
        public double Shortfall { get; internal set; }

        public bool Short => Shortfall > 0;
    }

    public sealed class BurnAllocation
    {
        public BurnAllocation(StageCursor start, bool covered)
        {
            Start = start;
            Covered = covered;
        }

        public StageCursor Start { get; }

        public bool Covered { get; }
    }

    public sealed class StageAllocation
    {
        public StageAllocation(IReadOnlyList<StageUse> stages, IReadOnlyList<BurnAllocation> burns, double uncovered)
        {
            Stages = stages;
            Burns = burns;
            Uncovered = uncovered;
        }

        public IReadOnlyList<StageUse> Stages { get; }

        public IReadOnlyList<BurnAllocation> Burns { get; }

        /// <summary>Delta-v of automatic burns that no stage can give.</summary>
        public double Uncovered { get; }

        /// <summary>
        /// Assigns burns, in time order, to stages in firing order (the bottom of the stack first). Automatic
        /// burns spill into the next stage; a pinned burn stages away everything below its stage and must fit in it.
        /// </summary>
        public static StageAllocation Allocate(IReadOnlyList<StageDeltaV> stages, IReadOnlyList<BurnDemand> burns)
        {
            var uses = new List<StageUse>(stages.Count);
            foreach (StageDeltaV stage in stages)
            {
                uses.Add(new StageUse(stage));
            }
            var allocations = new List<BurnAllocation>(burns.Count);
            int index = 0;
            double used = 0;
            double uncovered = 0;
            foreach (BurnDemand burn in burns)
            {
                if (burn.PinnedStage is int pinned)
                {
                    int target = uses.FindIndex(u => u.Stage.Stage == pinned);
                    if (target < 0 || target < index)
                    {
                        allocations.Add(new BurnAllocation(new StageCursor(Math.Max(target, index), used), false));
                        continue;
                    }
                    if (target > index)
                    {
                        index = target;
                        used = 0;
                    }
                    StageUse use = uses[index];
                    double left = use.Stage.DeltaV - used;
                    bool fits = burn.DeltaV <= left;
                    allocations.Add(new BurnAllocation(new StageCursor(index, used), fits));
                    double take = Math.Min(burn.DeltaV, left);
                    use.Assigned += take;
                    use.Shortfall += burn.DeltaV - take;
                    used += take;
                    continue;
                }

                while (index < uses.Count && used >= uses[index].Stage.DeltaV)
                {
                    index++;
                    used = 0;
                }
                allocations.Add(new BurnAllocation(new StageCursor(index, used), true));
                double need = burn.DeltaV;
                while (need > 0 && index < uses.Count)
                {
                    StageUse use = uses[index];
                    double take = Math.Min(need, use.Stage.DeltaV - used);
                    use.Assigned += take;
                    used += take;
                    need -= take;
                    if (need > 0)
                    {
                        index++;
                        used = 0;
                    }
                }
                if (need > 1e-6)
                {
                    allocations[allocations.Count - 1] = new BurnAllocation(allocations[allocations.Count - 1].Start, false);
                    uncovered += need;
                }
            }
            return new StageAllocation(uses, allocations, uncovered);
        }
    }
}
