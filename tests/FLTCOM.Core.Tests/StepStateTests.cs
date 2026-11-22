using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class StepStateTests
    {
        public static IEnumerable<object[]> AllowedTransitions =>
            StepMachine.Allowed.Select(t => new object[] { t.Key.Item1, t.Key.Item2, t.Value });

        public static IEnumerable<object[]> RefusedTransitions =>
            from StepState s in Enum.GetValues(typeof(StepState))
            from StepEvent e in Enum.GetValues(typeof(StepEvent))
            where !StepMachine.Allowed.Any(t => t.Key.Item1 == s && t.Key.Item2 == e)
            select new object[] { s, e };

        [Theory]
        [MemberData(nameof(AllowedTransitions))]
        public void AllowedTransitionMovesToItsState(StepState from, StepEvent e, StepState to)
        {
            StepTransition result = StepMachine.Apply(from, e);

            Assert.True(result.Ok);
            Assert.Equal(to, result.State);
        }

        [Theory]
        [MemberData(nameof(RefusedTransitions))]
        public void RefusedTransitionReturnsAnErrorAndKeepsTheState(StepState from, StepEvent e)
        {
            StepTransition result = StepMachine.Apply(from, e);

            Assert.False(result.Ok);
            Assert.Equal(from, result.State);
            Assert.Contains(e.ToString().ToLowerInvariant(), result.Error);
        }

        [Fact]
        public void TheHappyPathReachesDone()
        {
            StepState state = StepState.Draft;
            foreach (StepEvent e in new[] { StepEvent.Brief, StepEvent.Approve, StepEvent.Upload, StepEvent.Arm, StepEvent.Ignite, StepEvent.Complete })
            {
                state = StepMachine.Apply(state, e).State;
            }

            Assert.Equal(StepState.Done, state);
        }

        [Fact]
        public void TerminalStatesAcceptNoEvent()
        {
            foreach (StepState terminal in new[] { StepState.Done, StepState.Scrubbed, StepState.Aborted })
            {
                Assert.All(Enum.GetValues(typeof(StepEvent)).Cast<StepEvent>(), e => Assert.False(StepMachine.Apply(terminal, e).Ok));
            }
        }

        [Fact]
        public void IgnitingStartsProgressAndHoldKeepsItForResume()
        {
            var op = new Operation("op-1", "Amber Falcon", OrbitChange.Circularize(), new[] { "v1" }, Array.Empty<PlannedBurn>(), StepState.Waiting);

            Operation executing = op.Apply(StepEvent.Ignite, out _)!.WithProgress(new BurnProgress(0, 12.5));
            Operation held = executing.Apply(StepEvent.Hold, out _)!;
            Operation resumed = held.Apply(StepEvent.Resume, out _)!;

            Assert.Equal(StepState.Waiting, resumed.State);
            Assert.Equal(12.5, resumed.Progress!.DeltaVDone);
        }

        [Fact]
        public void CodenameIsEditableOnlyBeforeApproval()
        {
            var op = new Operation("op-1", "Amber Falcon", OrbitChange.Circularize(), new[] { "v1" }, Array.Empty<PlannedBurn>(), StepState.Briefed);

            Assert.Equal("Polar Heron", op.Rename(" Polar Heron ", out _)!.Codename);
            Assert.Null(op.Rename(" ", out string? empty));
            Assert.NotNull(empty);
            Operation approved = op.Apply(StepEvent.Approve, out _)!;
            Assert.Null(approved.Rename("Polar Heron", out string? error));
            Assert.Contains("approved", error);
        }

        [Fact]
        public void CodenamesAreTwoWordsAndDeterministic()
        {
            string first = Codename.Generate(42);

            Assert.Equal(first, Codename.Generate(42));
            Assert.Equal(2, first.Split(' ').Length);
            Assert.True(Enumerable.Range(0, 50).Select(i => Codename.Generate((ulong)i)).Distinct().Count() > 40);
        }
    }
}
