using System;
using System.Collections.Generic;

namespace FLTCOM.Core.Missions
{
    public enum StepState
    {
        Draft,
        Briefed,
        Approved,
        Uploaded,
        Waiting,
        Executing,
        Hold,
        Done,
        Missed,
        Scrubbed,
        Aborted,
    }

    public enum StepEvent
    {
        Brief,
        Edit,
        Approve,
        Upload,
        Arm,
        Ignite,
        Hold,
        Resume,
        Complete,
        Miss,
        Replan,
        Scrub,
        Abort,
    }

    public readonly struct StepTransition
    {
        private StepTransition(StepState state, string? error)
        {
            State = state;
            Error = error;
        }

        /// <summary>The new state, or the unchanged state when the event is not allowed.</summary>
        public StepState State { get; }

        public string? Error { get; }

        public bool Ok => Error == null;

        internal static StepTransition To(StepState state) => new StepTransition(state, null);

        internal static StepTransition Refused(StepState state, StepEvent e) =>
            new StepTransition(state, "cannot " + e.ToString().ToLowerInvariant() + " an operation that is " + state.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// The operation state machine. Hold resumes to Waiting so a burn interrupted mid-flight is re-timed for the
    /// delta-v still owed. Missed re-plans to Waiting when the next window still fits the delta-v.
    /// </summary>
    public static class StepMachine
    {
        private static readonly Dictionary<(StepState, StepEvent), StepState> Table = new Dictionary<(StepState, StepEvent), StepState>
        {
            [(StepState.Draft, StepEvent.Brief)] = StepState.Briefed,
            [(StepState.Draft, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Briefed, StepEvent.Brief)] = StepState.Briefed,
            [(StepState.Briefed, StepEvent.Edit)] = StepState.Draft,
            [(StepState.Briefed, StepEvent.Approve)] = StepState.Approved,
            [(StepState.Briefed, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Approved, StepEvent.Edit)] = StepState.Draft,
            [(StepState.Approved, StepEvent.Upload)] = StepState.Uploaded,
            [(StepState.Approved, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Uploaded, StepEvent.Arm)] = StepState.Waiting,
            [(StepState.Uploaded, StepEvent.Hold)] = StepState.Hold,
            [(StepState.Uploaded, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Waiting, StepEvent.Ignite)] = StepState.Executing,
            [(StepState.Waiting, StepEvent.Hold)] = StepState.Hold,
            [(StepState.Waiting, StepEvent.Miss)] = StepState.Missed,
            [(StepState.Waiting, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Executing, StepEvent.Complete)] = StepState.Done,
            [(StepState.Executing, StepEvent.Arm)] = StepState.Waiting,
            [(StepState.Executing, StepEvent.Hold)] = StepState.Hold,
            [(StepState.Executing, StepEvent.Abort)] = StepState.Aborted,
            [(StepState.Executing, StepEvent.Miss)] = StepState.Missed,
            [(StepState.Hold, StepEvent.Resume)] = StepState.Waiting,
            [(StepState.Hold, StepEvent.Scrub)] = StepState.Scrubbed,
            [(StepState.Hold, StepEvent.Abort)] = StepState.Aborted,
            [(StepState.Missed, StepEvent.Replan)] = StepState.Waiting,
            [(StepState.Missed, StepEvent.Scrub)] = StepState.Scrubbed,
        };

        public static IEnumerable<KeyValuePair<(StepState, StepEvent), StepState>> Allowed => Table;

        public static StepTransition Apply(StepState state, StepEvent e) =>
            Table.TryGetValue((state, e), out StepState next) ? StepTransition.To(next) : StepTransition.Refused(state, e);

        public static bool IsTerminal(StepState state) =>
            state == StepState.Done || state == StepState.Scrubbed || state == StepState.Aborted;

        /// <summary>The operator may still rename or re-plan an operation in these states.</summary>
        public static bool IsEditable(StepState state) =>
            state == StepState.Draft || state == StepState.Briefed;

        internal static StepState Parse(string text) =>
            Enum.TryParse(text, ignoreCase: false, out StepState state) && Enum.IsDefined(typeof(StepState), state)
                ? state
                : throw new FormatException("unknown step state " + text);
    }
}
