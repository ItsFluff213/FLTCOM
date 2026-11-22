using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Planning;

namespace FLTCOM.Core.Missions
{
    /// <summary>A burn one vessel flies. Components in m/s at the node; times in universal seconds.</summary>
    public sealed class PlannedBurn
    {
        public PlannedBurn(string vesselId, string label, double nodeUt, double startUt, double duration, double prograde, double normal, double radial, int? pinnedStage = null, TwoBody.OrbitElements? orbitAfter = null, string? body = null, TwoBody.OrbitElements? expected = null, uint? releasePart = null)
        {
            ReleasePart = releasePart;
            Body = body;
            Expected = expected ?? orbitAfter;
            VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            NodeUt = nodeUt;
            StartUt = startUt;
            Duration = duration;
            Prograde = prograde;
            Normal = normal;
            Radial = radial;
            PinnedStage = pinnedStage;
            OrbitAfter = orbitAfter;
        }

        public string VesselId { get; }

        public string Label { get; }

        public double NodeUt { get; }

        public double StartUt { get; }

        public double Duration { get; }

        public double EndUt => StartUt + Duration;

        public double Prograde { get; }

        public double Normal { get; }

        public double Radial { get; }

        public int? PinnedStage { get; }

        /// <summary>The planned orbit after this burn; known for the last burn of an operation.</summary>
        public TwoBody.OrbitElements? OrbitAfter { get; }

        /// <summary>
        /// The orbit the plan expects after this burn, for every burn whose result the planner knows. Only shown to the operator:
        /// unlike <see cref="OrbitAfter"/> the runner never matches it to skip a burn. Not saved, so a reloaded operation keeps only <see cref="OrbitAfter"/>.
        /// </summary>
        public TwoBody.OrbitElements? Expected { get; }

        /// <summary>The body <see cref="OrbitAfter"/> goes round; null for the body the vessel orbits when it burns.</summary>
        public string? Body { get; }

        /// <summary>
        /// Set on a payload release step: the payload's command part, which the vessel lets go of instead of burning. A release
        /// step has no delta-v.
        /// </summary>
        public uint? ReleasePart { get; }

        public double DeltaV => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

        public PlannedBurn ForVessel(string vesselId) =>
            new PlannedBurn(vesselId, Label, NodeUt, StartUt, Duration, Prograde, Normal, Radial, PinnedStage, OrbitAfter, Body, Expected, ReleasePart);

        /// <summary>The same burn whole orbits later; the orbit after it moves with it.</summary>
        public PlannedBurn Later(double seconds) =>
            new PlannedBurn(VesselId, Label, NodeUt + seconds, StartUt + seconds, Duration, Prograde, Normal, Radial, PinnedStage, OrbitAfter?.Later(seconds), Body, Expected?.Later(seconds), ReleasePart);
    }

    /// <summary>Saved progress of an executing operation, enough to resume it after a scene reload.</summary>
    public sealed class BurnProgress
    {
        public BurnProgress(int burnIndex, double deltaVDone)
        {
            BurnIndex = burnIndex;
            DeltaVDone = deltaVDone;
        }

        public int BurnIndex { get; }

        public double DeltaVDone { get; }
    }

    /// <summary>
    /// One orbit change, possibly flown by several vessels. Immutable; every change returns a new operation.
    /// </summary>
    public sealed class Operation
    {
        public Operation(string id, string codename, OrbitChange change, IReadOnlyList<string> vesselIds, IReadOnlyList<PlannedBurn> burns, StepState state = StepState.Draft, BurnProgress? progress = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Codename = codename ?? throw new ArgumentNullException(nameof(codename));
            Change = change ?? throw new ArgumentNullException(nameof(change));
            VesselIds = vesselIds ?? throw new ArgumentNullException(nameof(vesselIds));
            Burns = burns ?? throw new ArgumentNullException(nameof(burns));
            State = state;
            Progress = progress;
        }

        public string Id { get; }

        public string Codename { get; }

        public OrbitChange Change { get; }

        public IReadOnlyList<string> VesselIds { get; }

        /// <summary>Burns of every vessel in the operation, in time order.</summary>
        public IReadOnlyList<PlannedBurn> Burns { get; }

        public StepState State { get; }

        public BurnProgress? Progress { get; }

        public bool IsGroup => VesselIds.Count > 1;

        public IEnumerable<PlannedBurn> BurnsOf(string vesselId) => Burns.Where(b => b.VesselId == vesselId);

        /// <summary>Applies <paramref name="e"/>. Returns the new operation, or null with <paramref name="error"/> set.</summary>
        public Operation? Apply(StepEvent e, out string? error)
        {
            StepTransition next = StepMachine.Apply(State, e);
            error = next.Error;
            if (!next.Ok)
            {
                return null;
            }
            BurnProgress? progress = next.State == StepState.Executing ? Progress ?? new BurnProgress(0, 0) : Progress;
            return new Operation(Id, Codename, Change, VesselIds, Burns, next.State, StepMachine.IsTerminal(next.State) ? null : progress);
        }

        public Operation? Rename(string codename, out string? error)
        {
            if (!StepMachine.IsEditable(State))
            {
                error = "the codename is fixed once the operation is approved";
                return null;
            }
            if (string.IsNullOrWhiteSpace(codename))
            {
                error = "the codename cannot be empty";
                return null;
            }
            error = null;
            return new Operation(Id, codename.Trim(), Change, VesselIds, Burns, State, Progress);
        }

        public Operation WithBurns(IReadOnlyList<PlannedBurn> burns) =>
            new Operation(Id, Codename, Change, VesselIds, burns.OrderBy(b => b.StartUt).ToList(), State, Progress);

        /// <summary>Hands the burns planned for a placeholder id, such as a payload not yet released, to the vessel it became.</summary>
        public Operation Rebind(string placeholderId, string vesselId) =>
            new Operation(Id, Codename, Change, VesselIds.Select(v => v == placeholderId ? vesselId : v).ToList(), Burns.Select(b => b.VesselId == placeholderId ? b.ForVessel(vesselId) : b).ToList(), State, Progress);

        public Operation WithProgress(BurnProgress progress) =>
            new Operation(Id, Codename, Change, VesselIds, Burns, State, progress);
    }
}
