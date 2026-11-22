using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;

namespace FLTCOM.Core.Execution
{
    public enum PromptKind
    {
        Warp,
        Switch,
        /// <summary>Let FLTCOM fly the burn of the vessel the player is flying.</summary>
        Fly,
    }

    /// <summary>A question the runner will not act on until the operator answers yes.</summary>
    public sealed class Prompt
    {
        public Prompt(uint id, PromptKind kind, string operationId, int burnIndex, string vesselId, string text, double? untilUt)
        {
            Id = id;
            Kind = kind;
            OperationId = operationId;
            BurnIndex = burnIndex;
            VesselId = vesselId;
            Text = text;
            UntilUt = untilUt;
        }

        public uint Id { get; }

        public PromptKind Kind { get; }

        public string OperationId { get; }

        public int BurnIndex { get; }

        public string VesselId { get; }

        public string Text { get; }

        public double? UntilUt { get; }
    }

    public enum RunnerActionKind
    {
        /// <summary>Nothing to do this frame.</summary>
        Idle,
        /// <summary>Make the burn's vessel active.</summary>
        Switch,
        /// <summary>Time warp to UntilUt.</summary>
        Warp,
        /// <summary>The burn is ahead in real time: show its node, keep the throttle closed.</summary>
        Coast,
        /// <summary>Point along the burn, throttle closed; ignition is closer than the vessel's turn time.</summary>
        Align,
        /// <summary>Burn now.</summary>
        Fly,
        /// <summary>The burn window passed: plan the remaining change again from the current orbit.</summary>
        Replan,
        /// <summary>A group vessel the player is not flying: burn it on rails once it is due, coast until then.</summary>
        Background,
        /// <summary>The player flies this group vessel: count down and touch nothing until they let FLTCOM fly it.</summary>
        Await,
    }

    public sealed class RunnerAction
    {
        public static readonly RunnerAction Idle = new RunnerAction(RunnerActionKind.Idle, null, -1, null);

        public RunnerAction(RunnerActionKind kind, Operation? operation, int burnIndex, double? untilUt)
        {
            Kind = kind;
            Operation = operation;
            BurnIndex = burnIndex;
            UntilUt = untilUt;
        }

        public RunnerActionKind Kind { get; }

        public Operation? Operation { get; }

        public int BurnIndex { get; }

        public PlannedBurn? Burn => Operation != null && BurnIndex >= 0 && BurnIndex < Operation.Burns.Count ? Operation.Burns[BurnIndex] : null;

        public double? UntilUt { get; }
    }

    /// <summary>
    /// Every operation the fleet knows and the prompts waiting on the operator. It decides what the game should do
    /// next; the plugin does it and reports back. Nothing here touches KSP, so every rule is unit-testable.
    /// </summary>
    public sealed class MissionRunner
    {
        /// <summary>Warp stops this long before ignition (ignition is already half the burn before the node).</summary>
        public const double WarpLead = 60;

        /// <summary>A warp shorter than this is not worth asking for.</summary>
        public const double WarpWorth = 30;

        /// <summary>Not worth asking to warp when ignition is closer than this.</summary>
        public const double MinWarp = WarpLead + WarpWorth;

        /// <summary>The least time to turn toward a burn; a slow-turning vessel gets its <see cref="ActiveView.TurnLead"/>.</summary>
        public const double AlignLead = IgnitionTiming.TurnFloor;

        /// <summary>Ignition this late counts as a missed window.</summary>
        public const double MissGrace = 10;

        /// <summary>The player is asked to hand over the vessel they fly this long before ignition.</summary>
        public const double FlyAsk = AlignLead + 60;

        /// <summary>A burn on rails needs no time to turn, so warp stops just short of it.</summary>
        public const double BackgroundWarpLead = 10;

        /// <summary>A burn further off than this is waited for from the Space Center or Tracking Station, where warp is fastest, not in flight.</summary>
        public const double BaseWait = KerbinTime.Day;

        /// <summary>A wait at base ends this long before ignition, to switch to the vessel and turn it.</summary>
        public const double SwitchLead = 600;

        private readonly List<Operation> _operations = new List<Operation>();
        private readonly Dictionary<string, string> _notes = new Dictionary<string, string>();

        private readonly Dictionary<string, FlightAlarm> _alarms = new Dictionary<string, FlightAlarm>();
        private readonly List<Prompt> _prompts = new List<Prompt>();
        private readonly Dictionary<(string, int, PromptKind), bool> _answers = new Dictionary<(string, int, PromptKind), bool>();
        private readonly HashSet<(string, int)> _warped = new HashSet<(string, int)>();
        private readonly Dictionary<(string, int), bool> _waited = new Dictionary<(string, int), bool>();
        private readonly HashSet<string> _warpConsent = new HashSet<string>();
        private uint _nextPrompt = 1;

        public IReadOnlyList<Operation> Operations => _operations;

        public IReadOnlyList<Prompt> Prompts => _prompts;

        /// <summary>Counts every change, so the plugin publishes only when something moved.</summary>
        public int Revision { get; private set; }

        public string? NoteOf(string operationId) => _notes.TryGetValue(operationId, out string note) ? note : null;

        /// <summary>The red alert on the operation, until it is resumed, cleared or ends.</summary>
        public FlightAlarm? AlarmOf(string operationId) => _alarms.TryGetValue(operationId, out FlightAlarm alarm) ? alarm : null;

        public Operation? Find(string operationId) => _operations.FirstOrDefault(o => o.Id == operationId);

        public Operation AddBriefed(string id, string codename, OrbitChange change, Briefing briefing)
        {
            var vessels = change.Kind == OrbitChangeKind.DeployConstellation
                ? briefing.Burns.Take(1).Select(b => b.VesselId).ToList()
                : briefing.Burns.Select(b => b.VesselId).Distinct().ToList();
            var op = new Operation(id, codename, change, vessels, briefing.Burns, StepState.Briefed);
            _operations.RemoveAll(o => o.State == StepState.Briefed && o.VesselIds.SequenceEqual(vessels));
            _operations.Add(op);
            Changed();
            return op;
        }

        /// <param name="uplink">The vessel has a CommNet link, or CommNet is off.</param>
        public string? Approve(string operationId, string codename, bool uplink)
        {
            Operation? op = Find(operationId);
            if (op == null)
            {
                return "no operation " + operationId;
            }
            if (op.State != StepState.Briefed)
            {
                return "operation " + op.Codename + " is " + op.State + ", not briefed";
            }
            if (op.Burns.Count == 0)
            {
                return "operation " + op.Codename + " has nothing to burn";
            }
            if (!uplink)
            {
                return "no link: the vessel has no CommNet connection to upload operation " + op.Codename;
            }
            if (op.Change.Rendezvous is RendezvousTarget wanted && PortReservations.Holder(_operations, wanted, op.Id) is Operation holder)
            {
                return PortReservations.Refusal(holder, wanted);
            }
            var others = _operations.Where(o => o.Id != op.Id && o.VesselIds.Intersect(op.VesselIds).Any()).ToList();
            if (others.FirstOrDefault(o => o.State == StepState.Uploaded || o.State == StepState.Waiting || o.State == StepState.Executing) is Operation flying)
            {
                return "operation " + flying.Codename + " is still " + flying.State.ToString().ToLowerInvariant() + " on this vessel: let it finish or scrub it first";
            }
            if (codename != op.Codename)
            {
                op = op.Rename(codename, out string? renameError) ?? throw new InvalidOperationException(renameError);
            }
            foreach (Operation stopped in others)
            {
                if ((stopped.Apply(StepEvent.Abort, out _) ?? stopped.Apply(StepEvent.Scrub, out _)) is Operation ended && stopped.State != StepState.Briefed)
                {
                    Put(ended, "superseded by " + op.Codename);
                }
            }
            op = Step(Step(Step(op, StepEvent.Approve), StepEvent.Upload), StepEvent.Arm);
            Put(op, "uploaded");
            return null;
        }

        public string? Scrub(string operationId)
        {
            Operation? op = Find(operationId);
            if (op == null)
            {
                return "no operation " + operationId;
            }
            if (op.State == StepState.Scrubbed || op.State == StepState.Aborted)
            {
                return null;
            }
            StepEvent e = op.State == StepState.Executing ? StepEvent.Abort : StepEvent.Scrub;
            Operation? next = op.Apply(e, out string? error);
            if (next == null)
            {
                return error;
            }
            Put(next, e == StepEvent.Abort ? "aborted by the operator" : "scrubbed");
            return null;
        }

        public string? Resume(string operationId)
        {
            Operation? op = Find(operationId);
            if (op == null)
            {
                return "no operation " + operationId;
            }
            Operation? next = op.Apply(StepEvent.Resume, out string? error);
            if (next == null)
            {
                return error;
            }
            _alarms.Remove(operationId);
            Put(next, "resumed");
            return null;
        }

        public string? Answer(uint promptId, bool accept)
        {
            Prompt? prompt = _prompts.FirstOrDefault(p => p.Id == promptId);
            if (prompt == null)
            {
                return "no open prompt " + promptId;
            }
            _prompts.Remove(prompt);
            _answers[(prompt.OperationId, prompt.BurnIndex, prompt.Kind)] = accept;
            if (accept && prompt.Kind == PromptKind.Warp && Find(prompt.OperationId)?.IsGroup == true)
            {
                _warpConsent.Add(prompt.OperationId);
            }
            Changed();
            return null;
        }

        /// <summary>Replaces every operation with saved ones and holds each that was running: after any load, nothing fires until the operator resumes it.</summary>
        public void Load(IEnumerable<Operation> saved)
        {
            _operations.Clear();
            _prompts.Clear();
            _answers.Clear();
            _warped.Clear();
            _waited.Clear();
            _warpConsent.Clear();
            _notes.Clear();
            _alarms.Clear();
            foreach (Operation op in saved)
            {
                Operation? held = op.Apply(StepEvent.Hold, out _);
                _operations.Add(held ?? op);
                if (held != null)
                {
                    _notes[op.Id] = "game loaded: holding until resumed";
                }
            }
            Changed();
        }

        public RunnerAction Next(double now, ActiveView view)
        {
            Operation? flying = _operations.FirstOrDefault(o => o.State == StepState.Executing);
            if (flying != null)
            {
                int at = BurnIndex(flying);
                PlannedBurn current = flying.Burns[at];
                if (!flying.IsGroup)
                {
                    return new RunnerAction(RunnerActionKind.Fly, flying, at, null);
                }
                if (view.ActiveVesselId != current.VesselId)
                {
                    return new RunnerAction(RunnerActionKind.Background, flying, at, null);
                }
                if (Answered(flying.Id, at, PromptKind.Fly) == true)
                {
                    return new RunnerAction(RunnerActionKind.Fly, flying, at, null);
                }
                ForgetWarp(flying.Id);
                Put(Step(flying, StepEvent.Hold), "you switched to " + current.VesselId + " during its burn: FLTCOM let go; fly it by hand or resume");
                return RunnerAction.Idle;
            }

            Operation? op = _operations
                .Where(o => o.State == StepState.Waiting && BurnIndex(o) < o.Burns.Count)
                .OrderBy(o => o.Burns[BurnIndex(o)].StartUt)
                .FirstOrDefault();
            DropStalePrompts(op);
            if (op == null)
            {
                return RunnerAction.Idle;
            }
            int index = BurnIndex(op);
            PlannedBurn burn = op.Burns[index];

            bool resumedMidBurn = op.IsGroup && view.ActiveVesselId != burn.VesselId && (op.Progress?.DeltaVDone ?? 0) > 0;
            if (now > burn.StartUt + MissGrace && !resumedMidBurn)
            {
                ForgetWarp(op.Id);
                Put(Step(op, StepEvent.Miss), "missed the window at " + KerbinTime.Format(burn.StartUt));
                return new RunnerAction(RunnerActionKind.Replan, Find(op.Id), index, null);
            }
            if (op.IsGroup)
            {
                return NextInGroup(op, index, burn, now, view, resumedMidBurn);
            }

            if (view.ActiveVesselId != burn.VesselId)
            {
                if (WaitAtBase(op, index, burn, now, view) is RunnerAction wait)
                {
                    return wait;
                }
                switch (SwitchPolicy.Decide(view, burn.VesselId))
                {
                    case SwitchDecision.Switch:
                        return new RunnerAction(RunnerActionKind.Switch, op, index, null);
                    case SwitchDecision.Ask:
                        if (Answered(op.Id, index, PromptKind.Switch) == true)
                        {
                            return new RunnerAction(RunnerActionKind.Switch, op, index, null);
                        }
                        Ask(PromptKind.Switch, op, index, "switch to vessel " + burn.VesselId + " for \"" + burn.Label + "\"; the active vessel is not in a stable orbit", null);
                        return RunnerAction.Idle;
                    default:
                        return RunnerAction.Idle;
                }
            }

            double lead = burn.StartUt - now;
            double turn = TurnLead(view);
            if (lead > turn + WarpWorth && !_warped.Contains((op.Id, index)))
            {
                bool? answer = Answered(op.Id, index, PromptKind.Warp);
                if (answer == true)
                {
                    _warped.Add((op.Id, index));
                    return new RunnerAction(RunnerActionKind.Warp, op, index, burn.StartUt - turn);
                }
                if (answer == null)
                {
                    Ask(PromptKind.Warp, op, index, "warp to " + KerbinTime.Format(burn.StartUt - turn) + " for \"" + burn.Label + "\" (" + KerbinTime.Span(lead) + " to ignition)", burn.StartUt - turn);
                }
            }

            if (lead > turn)
            {
                return new RunnerAction(RunnerActionKind.Coast, op, index, null);
            }
            if (lead > 0)
            {
                return new RunnerAction(RunnerActionKind.Align, op, index, null);
            }
            Put(Step(op, StepEvent.Ignite), "burning \"" + burn.Label + "\"");
            return new RunnerAction(RunnerActionKind.Fly, Find(op.Id), index, null);
        }

        /// <summary>
        /// Group operations ask once whether to warp through all their burns. Every burn that is not the player's burns
        /// in the background; the player's own burn waits for their "let FLTCOM fly it".
        /// </summary>
        private RunnerAction NextInGroup(Operation op, int index, PlannedBurn burn, double now, ActiveView view, bool resumedMidBurn)
        {
            double lead = burn.StartUt - now;
            double turn = TurnLead(view);
            double stop = view.ActiveVesselId == burn.VesselId ? turn : BackgroundWarpLead;
            if (lead > Math.Max(MinWarp, stop + WarpWorth) && !_warped.Contains((op.Id, index)))
            {
                double until = burn.StartUt - stop;
                if (_warpConsent.Contains(op.Id))
                {
                    _warped.Add((op.Id, index));
                    return new RunnerAction(RunnerActionKind.Warp, op, index, until);
                }
                if (Answered(op.Id, index, PromptKind.Warp) == null)
                {
                    Ask(PromptKind.Warp, op, index, WarpThroughAll(op, index, now), until);
                }
            }

            if (view.ActiveVesselId != burn.VesselId)
            {
                if (lead > 0 && !resumedMidBurn)
                {
                    return new RunnerAction(RunnerActionKind.Background, op, index, null);
                }
                Put(Step(op, StepEvent.Ignite), "burning \"" + burn.Label + "\" in the background");
                return new RunnerAction(RunnerActionKind.Background, Find(op.Id), index, null);
            }

            if (Answered(op.Id, index, PromptKind.Fly) != true)
            {
                if (lead <= FlyAsk - AlignLead + turn)
                {
                    Ask(PromptKind.Fly, op, index, "you are flying this vessel: let FLTCOM fly \"" + burn.Label + "\" at " + KerbinTime.Format(burn.StartUt) + ", or fly it yourself", null);
                }
                return new RunnerAction(RunnerActionKind.Await, op, index, null);
            }
            if (lead > turn)
            {
                return new RunnerAction(RunnerActionKind.Coast, op, index, null);
            }
            if (lead > 0)
            {
                return new RunnerAction(RunnerActionKind.Align, op, index, null);
            }
            Put(Step(op, StepEvent.Ignite), "burning \"" + burn.Label + "\"");
            return new RunnerAction(RunnerActionKind.Fly, Find(op.Id), index, null);
        }

        /// <summary>Something unexpected happened to a group operation: stop warping it and ask again before the next warp.</summary>
        public void Interrupt(string operationId, string why)
        {
            if (Find(operationId) == null)
            {
                return;
            }
            ForgetWarp(operationId);
            _notes[operationId] = why;
            Changed();
        }

        /// <summary>Whether the operator agreed to warp through every burn of the operation.</summary>
        public bool WarpsThrough(string operationId) => _warpConsent.Contains(operationId);

        /// <summary>A missed burn flies whole orbits later: the vessel's remaining burns move by <paramref name="seconds"/> and the operation waits again.</summary>
        public void Delay(string operationId, string vesselId, double seconds, string note)
        {
            Operation op = Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId);
            int index = BurnIndex(op);
            var later = op.Burns.Skip(index).Select(b => b.VesselId == vesselId ? b.Later(seconds) : b).OrderBy(b => b.StartUt);
            var burns = op.Burns.Take(index).Concat(later).ToList();
            Put(Step(new Operation(op.Id, op.Codename, op.Change, op.VesselIds, burns, op.State, new BurnProgress(index, 0)), StepEvent.Replan), note);
        }

        /// <summary>
        /// The burn under way has not lit, because the vessel is still turning, and is past its start by more than
        /// <see cref="IgnitionTiming.LateTolerance"/>: flown now it would miss its orbit, so its window counts as missed and
        /// it waits for a re-plan. True when that happened. The caller knows whether the engines have lit. A launch's
        /// finishing burn is never held back: a missed one would leave the vessel falling back into the air.
        /// </summary>
        public bool Late(string operationId, double now)
        {
            Operation? op = Find(operationId);
            if (op?.State != StepState.Executing || op.Change.Kind == OrbitChangeKind.Launch || !IgnitionTiming.IsLate(op.Burns[BurnIndex(op)], now))
            {
                return false;
            }
            ForgetWarp(op.Id);
            Put(Step(op, StepEvent.Miss), "not lit " + KerbinTime.Span(now - op.Burns[BurnIndex(op)].StartUt) + " after its start");
            return true;
        }

        /// <summary>The player flew the missed burn by hand and the orbit matches the plan: count it as flown.</summary>
        public void FlownByHand(string operationId, string note)
        {
            Operation op = Step(Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId), StepEvent.Replan);
            int next = BurnIndex(op) + 1;
            op = next < op.Burns.Count
                ? op.WithProgress(new BurnProgress(next, 0))
                : Step(Step(op, StepEvent.Ignite), StepEvent.Complete);
            Put(op, note);
        }

        public void Progress(string operationId, double deltaVDone)
        {
            Operation? op = Find(operationId);
            if (op?.State != StepState.Executing)
            {
                return;
            }
            Put(op.WithProgress(new BurnProgress(BurnIndex(op), deltaVDone)), null);
        }

        /// <summary>The current burn is flown. Arms the next burn, or finishes the operation.</summary>
        public void BurnDone(string operationId, string note)
        {
            Operation op = Executing(operationId);
            int next = BurnIndex(op) + 1;
            if (next < op.Burns.Count)
            {
                Put(Step(op.WithProgress(new BurnProgress(next, 0)), StepEvent.Arm), note);
                return;
            }
            Put(Step(op, StepEvent.Complete), note);
        }

        /// <summary>Replaces the burns after the current one with <paramref name="corrections"/> and arms the first; with none, the operation is complete.</summary>
        public void AddCorrection(string operationId, IReadOnlyList<PlannedBurn> corrections, string note)
        {
            Operation op = Executing(operationId);
            int next = BurnIndex(op) + 1;
            var burns = op.Burns.Take(next).Concat(corrections).ToList();
            var armed = new Operation(op.Id, op.Codename, op.Change, op.VesselIds, burns, op.State, new BurnProgress(next, 0));
            Put(Step(armed, corrections.Count > 0 ? StepEvent.Arm : StepEvent.Complete), note);
        }

        /// <summary>The pilot, the Abort group or the HALT key took control back mid-burn.</summary>
        public void Release(string operationId, string why)
        {
            Operation? op = Find(operationId);
            if (op == null)
            {
                return;
            }
            Operation? next = op.Apply(StepEvent.Abort, out _) ?? op.Apply(StepEvent.Hold, out _);
            if (next != null)
            {
                Put(next, "control released: " + why);
            }
        }

        /// <summary>The game stopped flying the operation (a scene change, a lost vessel): it waits for the operator's resume.</summary>
        public void Hold(string operationId, string why)
        {
            Operation? next = Find(operationId)?.Apply(StepEvent.Hold, out _);
            if (next != null)
            {
                Put(next, why);
            }
        }

        /// <summary>
        /// Raises <paramref name="alarm"/> and holds the operation, past a burn just flown, until the operator resumes it or
        /// approves a recovery that supersedes it.
        /// </summary>
        public void Alarm(string operationId, FlightAlarm alarm)
        {
            Operation? op = Find(operationId);
            if (op == null || StepMachine.IsTerminal(op.State))
            {
                return;
            }
            int next = BurnIndex(op) + 1;
            if (op.State == StepState.Executing && next < op.Burns.Count)
            {
                op = op.WithProgress(new BurnProgress(next, 0));
            }
            ForgetWarp(operationId);
            _alarms[operationId] = alarm;
            Put(op.Apply(StepEvent.Hold, out _) ?? op, (alarm.Kind == AlarmKind.Impact ? "IMPACT: " : "LANDING: ") + alarm.Text);
        }

        /// <summary>Raises or clears a red alert without holding: a descent under way is safer flown on than dropped.</summary>
        public void Warn(string operationId, FlightAlarm? alarm)
        {
            if (AlarmOf(operationId)?.Text == alarm?.Text || Find(operationId) == null)
            {
                return;
            }
            if (alarm == null)
            {
                _alarms.Remove(operationId);
            }
            else
            {
                _alarms[operationId] = alarm;
                _notes[operationId] = "LANDING: " + alarm.Text;
            }
            Changed();
        }

        /// <summary>A missed burn got a new window: the remaining burns are replaced and the operation waits again.</summary>
        public void Replanned(string operationId, IReadOnlyList<PlannedBurn> remaining, string note)
        {
            Operation op = Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId);
            int index = BurnIndex(op);
            var burns = op.Burns.Take(index).Concat(remaining).ToList();
            var replanned = new Operation(op.Id, op.Codename, op.Change, op.VesselIds, burns, op.State, new BurnProgress(index, 0));
            Put(Step(replanned, StepEvent.Replan), note);
        }

        /// <summary>The remaining burns of a running operation, re-planned from where the vessel really is: same state, new burns.</summary>
        public void Revise(string operationId, IReadOnlyList<PlannedBurn> remaining, string note)
        {
            Operation op = Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId);
            int index = BurnIndex(op);
            var burns = op.Burns.Take(index).Concat(remaining).ToList();
            Put(new Operation(op.Id, op.Codename, op.Change, op.VesselIds, burns, op.State, new BurnProgress(index, 0)), note);
        }

        /// <summary>A released payload became <paramref name="vesselId"/>: its burns, planned under its placeholder id, are now that vessel's.</summary>
        public void Rebind(string operationId, string placeholderId, string vesselId)
        {
            Operation op = Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId);
            Put(op.Rebind(placeholderId, vesselId), null);
        }

        public void Note(string operationId, string note)
        {
            if (NoteOf(operationId) != note)
            {
                _notes[operationId] = note;
                Changed();
            }
        }

        public static int BurnIndex(Operation op) => op.Progress?.BurnIndex ?? 0;

        private static double TurnLead(ActiveView view) => Math.Max(AlignLead, view.TurnLead);

        private Operation Executing(string operationId)
        {
            Operation op = Find(operationId) ?? throw new InvalidOperationException("no operation " + operationId);
            return op.State == StepState.Executing ? op : throw new InvalidOperationException("operation " + op.Codename + " is " + op.State + ", not executing");
        }

        /// <summary>
        /// From the Space Center or Tracking Station, a burn more than <see cref="BaseWait"/> away is waited for there: ask to warp to
        /// <see cref="SwitchLead"/> before ignition, and only then switch to the vessel. A no switches now, to wait in flight instead. The
        /// answer is used once, so the warp in flight that follows is asked again. Null when the switch should go ahead.
        /// </summary>
        private RunnerAction? WaitAtBase(Operation op, int index, PlannedBurn burn, double now, ActiveView view)
        {
            double until = burn.StartUt - SwitchLead;
            if (_waited.TryGetValue((op.Id, index), out bool warping))
            {
                return warping && now < until - MinWarp ? RunnerAction.Idle : null;
            }
            bool atBase = view.Scene == GameScene.SpaceCenter || view.Scene == GameScene.TrackingStation;
            if (!atBase || burn.StartUt - now <= BaseWait)
            {
                return null;
            }
            bool? answer = Answered(op.Id, index, PromptKind.Warp);
            if (answer == null)
            {
                Ask(PromptKind.Warp, op, index, "warp to " + KerbinTime.Format(until) + " here, then switch to vessel " + burn.VesselId + " for \"" + burn.Label + "\" (" + KerbinTime.Span(burn.StartUt - now) + " to ignition)", until);
                return RunnerAction.Idle;
            }
            _answers.Remove((op.Id, index, PromptKind.Warp));
            _waited[(op.Id, index)] = answer.Value;
            if (answer == false)
            {
                return null;
            }
            return new RunnerAction(RunnerActionKind.Warp, op, index, until);
        }

        private void ForgetWarp(string operationId)
        {
            _warpConsent.Remove(operationId);
            _warped.RemoveWhere(w => w.Item1 == operationId);
            foreach (var key in _waited.Keys.Where(k => k.Item1 == operationId).ToList())
            {
                _waited.Remove(key);
            }
            foreach (var key in _answers.Keys.Where(k => k.Item1 == operationId && k.Item3 == PromptKind.Warp).ToList())
            {
                _answers.Remove(key);
            }
        }

        private static string WarpThroughAll(Operation op, int index, double now)
        {
            var left = op.Burns.Skip(index).ToList();
            int ships = left.Select(b => b.VesselId).Distinct().Count();
            double span = left.Max(b => b.EndUt) - now;
            return Count(ships, "ship") + ", " + Count(left.Count, "burn") + " over " + KerbinTime.Span(span) + ": warp through all?";
        }

        private static string Count(int n, string noun) => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? string.Empty : "s");

        private bool? Answered(string operationId, int burnIndex, PromptKind kind) =>
            _answers.TryGetValue((operationId, burnIndex, kind), out bool accept) ? accept : (bool?)null;

        private void Ask(PromptKind kind, Operation op, int burnIndex, string text, double? untilUt)
        {
            if (_prompts.Any(p => p.Kind == kind && p.OperationId == op.Id && p.BurnIndex == burnIndex))
            {
                return;
            }
            _prompts.Add(new Prompt(_nextPrompt++, kind, op.Id, burnIndex, op.Burns[burnIndex].VesselId, text, untilUt));
            Changed();
        }

        private void DropStalePrompts(Operation? due)
        {
            int removed = _prompts.RemoveAll(p => due == null || p.OperationId != due.Id || p.BurnIndex != BurnIndex(due));
            if (removed > 0)
            {
                Changed();
            }
        }

        private static Operation Step(Operation op, StepEvent e) =>
            op.Apply(e, out string? error) ?? throw new InvalidOperationException(error);

        private void Put(Operation op, string? note)
        {
            int i = _operations.FindIndex(o => o.Id == op.Id);
            if (i < 0)
            {
                _operations.Add(op);
            }
            else
            {
                _operations[i] = op;
            }
            if (note != null)
            {
                _notes[op.Id] = note;
            }
            if (StepMachine.IsTerminal(op.State))
            {
                _alarms.Remove(op.Id);
            }
            Changed();
        }

        private void Changed() => Revision++;
    }
}
