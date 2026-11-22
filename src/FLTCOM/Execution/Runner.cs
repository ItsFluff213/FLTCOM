using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FLTCOM.Core.Constellation;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.Scheduling;
using FLTCOM.Guidance;
using UnityEngine;

namespace FLTCOM.Execution
{
    /// <summary>Asks the mission runner what to do each physics frame and does it in the game.</summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public sealed class Runner : MonoBehaviour
    {
        private const double TrimFloor = 0.5;
        private const float SwitchRetry = 10f;
        private const float WatchInterval = 1f;

        private static readonly HashSet<string> Trimmed = new HashSet<string>();

        private IGuidance? _executor;
        private ReleaseRule _release = new ReleaseRule();
        private string? _executorOperation;
        private int _executorBurn = -1;
        private float _nextSwitch;
        private readonly BackgroundExecutor _background = new BackgroundExecutor();
        private readonly HashSet<string> _silent = new HashSet<string>();
        private readonly HashSet<string> _lowFuel = new HashSet<string>();
        private string? _lastActive;
        private readonly Dictionary<string, string> _soi = new Dictionary<string, string>();
        private float _nextWatch;
        private float _nextTurnLead;
        private double _turnLead = IgnitionTiming.TurnFloor;
        private float _nextCountdown;
        private float _nextDescentCheck;
        private readonly HashSet<string> _deorbitChecked = new HashSet<string>();

        private static bool InGame =>
            HighLogic.CurrentGame != null
            && (HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.SPACECENTER);

        public void Update()
        {
            if (!InGame)
            {
                return;
            }
            if (_executor != null && _executorOperation != null && ControlRelease.HaltPressed())
            {
                Release(_executorOperation, "HALT key");
            }
            MissionDesk.Publish();
        }

        public void FixedUpdate()
        {
            if (!InGame)
            {
                return;
            }
            try
            {
                ActiveView view = View();
                Watch(view);
                FollowSoi();
                Act(MissionDesk.Runner.Next(Planetarium.GetUniversalTime(), view));
            }
            catch (Exception e)
            {
                PluginFiles.Log("runner failed: " + e);
                if (_executorOperation != null)
                {
                    MissionDesk.Runner.Hold(_executorOperation, "runner error: " + e.Message);
                }
                DropExecutor();
            }
        }

        public void OnDestroy()
        {
            if (_executor != null && _executorOperation != null && MissionDesk.Runner.Find(_executorOperation)?.State == StepState.Executing)
            {
                MissionDesk.Runner.Hold(_executorOperation, "left the flight scene during the burn");
            }
            DropExecutor();
        }

        private void Act(RunnerAction action)
        {
            Operation? op = action.Operation;
            PlannedBurn? burn = action.Burn;
            if (op == null || burn == null)
            {
                DropExecutor();
                _background.Reset();
                return;
            }
            if (action.Kind != RunnerActionKind.Background)
            {
                _background.Reset();
            }
            switch (action.Kind)
            {
                case RunnerActionKind.Background:
                    DropExecutor();
                    _background.Tick(op, action.BurnIndex, burn);
                    break;
                case RunnerActionKind.Await:
                    DropExecutor();
                    Countdown(burn);
                    break;
                case RunnerActionKind.Switch:
                    DropExecutor();
                    if (Time.realtimeSinceStartup >= _nextSwitch)
                    {
                        _nextSwitch = Time.realtimeSinceStartup + SwitchRetry;
                        string? error = VesselSwitcher.Switch(burn.VesselId);
                        if (error != null)
                        {
                            MissionDesk.Runner.Hold(op.Id, error);
                        }
                    }
                    break;
                case RunnerActionKind.Warp:
                    string? low = op.IsGroup ? LowFuel(op, action.BurnIndex) : null;
                    if (low != null)
                    {
                        MissionDesk.Runner.Interrupt(op.Id, low);
                        break;
                    }
                    WarpDriver.WarpTo(action.UntilUt!.Value);
                    break;
                case RunnerActionKind.Coast:
                case RunnerActionKind.Align:
                    if (action.Kind == RunnerActionKind.Align)
                    {
                        WarpDriver.Stop();
                    }
                    if (LaunchBriefing.IsAscent(burn) || LandingBriefing.IsDescent(burn) || ConstellationPlanner.IsStep(burn))
                    {
                        DropExecutor();
                        break;
                    }
                    (Executor(op, action.BurnIndex, burn) as NodeExecutor)?.Point();
                    break;
                case RunnerActionKind.Fly:
                    Fly(op, action.BurnIndex, burn);
                    break;
                case RunnerActionKind.Replan:
                    DropExecutor();
                    Replan(op);
                    break;
                default:
                    DropExecutor();
                    break;
            }
        }

        private IGuidance? Executor(Operation op, int burnIndex, PlannedBurn burn)
        {
            if (_executor is DockingController { HasDocked: true } && _executorOperation == op.Id && _executorBurn == burnIndex)
            {
                return _executor;
            }
            Vessel? active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (active == null || VesselTasks.Id(active) != burn.VesselId)
            {
                DropExecutor();
                return null;
            }
            if (_executor != null && (_executorOperation != op.Id || _executorBurn != burnIndex || _executor.Vessel != active))
            {
                DropExecutor();
            }
            if (_executor == null)
            {
                _executor = LandingBriefing.IsDescent(burn) && op.Change.Landing is Core.Landing.LandingTarget landing
                    ? (IGuidance?)LandingGuidance.Create(active, landing, burn, op.Change.AutoSystems)
                    : LaunchBriefing.IsAscent(burn) && op.Change.LaunchTarget is LaunchTarget target
                    ? new AscentGuidance(active, target, op.Change.AutoSystems)
                    : burn.Label == Rendezvous.HoldLabel && op.Change.Rendezvous is RendezvousTarget meet
                    ? new RelativeVelocityKiller(active, meet.VesselId)
                    : burn.Label == DockingApproach.Label && op.Change.Rendezvous is RendezvousTarget dock
                    ? new DockingController(active, dock)
                    : burn.ReleasePart is uint core
                    ? new PayloadRelease(active, core, ReleaseName(burn.Label))
                    : ConstellationPlanner.IsStep(burn)
                    ? new ReturnHome(active, op)
                    : (IGuidance)new NodeExecutor(active, burn, op.Change.AutoSystems, op.Change.Kind == OrbitChangeKind.Launch);
                _executorOperation = op.Id;
                _executorBurn = burnIndex;
            }
            return _executor;
        }

        private static readonly Regex ReleaseStep = new Regex("^" + ConstellationPlanner.ReleasePrefix + "(.+) (at apoapsis|at periapsis|on the circle), pass [0-9]+$");

        /// <summary>The satellite's name from its release step's label, as the deployment briefing wrote it.</summary>
        private static string ReleaseName(string label)
        {
            Match match = ReleaseStep.Match(label);
            return match.Success ? match.Groups[1].Value : label.Substring(ConstellationPlanner.ReleasePrefix.Length);
        }

        private void Fly(Operation op, int burnIndex, PlannedBurn burn)
        {
            IGuidance? executor = Executor(op, burnIndex, burn);
            if (executor == null)
            {
                WarpDriver.Stop();
                MissionDesk.Runner.Hold(op.Id, "the burn's vessel is not the active vessel");
                return;
            }
            WarpDriver.Allow(executor.IdleUntil);
            string? why = _release.Why(ControlRelease.Sample(executor.Vessel));
            string? other = ControlRelease.OtherPilot(executor.Vessel);
            if (why != null || other != null)
            {
                Release(op.Id, why ?? other + " took control");
                return;
            }
            double now = Planetarium.GetUniversalTime();
            if (executor is NodeExecutor { Ignited: false } && MissionDesk.Runner.Late(op.Id, now))
            {
                DropExecutor();
                Replan(MissionDesk.Runner.Find(op.Id)!, "pointing took " + (now - burn.StartUt).ToString("0", CultureInfo.InvariantCulture) + " s");
                return;
            }
            if (op.Change.Rendezvous is RendezvousTarget passive && VesselTasks.Find(passive.VesselId) is Vessel target)
            {
                RelativeVelocityKiller.Passive(target);
            }
            switch (executor.Tick())
            {
                case GuidanceStatus.Running:
                    MissionDesk.Runner.Progress(op.Id, executor.DeltaVDone);
                    if (executor is LandingGuidance descent)
                    {
                        LandingReader.Descending(op.Id, descent);
                        WatchDescent(op, descent);
                    }
                    break;
                case GuidanceStatus.Done:
                    if (op.Change.AutoSystems && op.Change.Kind == OrbitChangeKind.Launch && executor is NodeExecutor)
                    {
                        SystemsPilot.InOrbit(executor.Vessel);
                    }
                    PluginFiles.Log("burn " + op.Codename + " #" + burnIndex + ": " + executor.Note + "; e=" + executor.Vessel.orbit.eccentricity.ToString("0.00000") + " pe=" + executor.Vessel.orbit.PeA.ToString("0") + " ap=" + executor.Vessel.orbit.ApA.ToString("0"));
                    if (executor is PayloadRelease release)
                    {
                        if (release.Released is Vessel satellite)
                        {
                            MissionDesk.Runner.Rebind(op.Id, burn.ReleasePart!.Value.ToString(CultureInfo.InvariantCulture), VesselTasks.Id(satellite));
                        }
                        MissionDesk.Runner.BurnDone(op.Id, executor.Note);
                    }
                    else if (executor is ReturnHome home)
                    {
                        MissionDesk.Runner.BurnDone(op.Id, executor.Note);
                        MissionDesk.Runner.Note(op.Id, home.Follow());
                    }
                    else if (executor is LandingGuidance)
                    {
                        MissionDesk.Runner.BurnDone(op.Id, executor.Note);
                    }
                    else if (executor is AscentGuidance)
                    {
                        AfterAscent(op, executor);
                    }
                    else if (op.Change.Kind == OrbitChangeKind.Transfer && executor.Vessel.mainBody.bodyName != op.Change.Destination)
                    {
                        AfterTransferBurn(op, executor);
                    }
                    else if (executor is NodeExecutor && op.Change.Kind != OrbitChangeKind.Land && !ConstellationPlanner.IsBurnUp(burn) && ImpactBefore(executor.Vessel, burnIndex + 1 < op.Burns.Count ? op.Burns[burnIndex + 1].StartUt : double.PositiveInfinity) is ImpactCourse impact)
                    {
                        RaiseAlarm(op, impact.Alarm(), executor.Note + "; ");
                    }
                    else
                    {
                        AfterBurn(op, burnIndex, executor);
                    }
                    if (op.Change.ThenLand && MissionDesk.Runner.Find(op.Id)?.State == StepState.Done && executor.Vessel.mainBody == FlightGlobals.GetHomeBody())
                    {
                        LandHome(op, executor.Vessel);
                    }
                    DropExecutor();
                    break;
                default:
                    PluginFiles.Log("burn " + op.Codename + " #" + burnIndex + " failed: " + executor.Note);
                    MissionDesk.Runner.Release(op.Id, executor.Note);
                    DropExecutor();
                    break;
            }
        }

        /// <summary>A carrier that came home by transfer goes on to land; when that cannot be briefed, the operation says why.</summary>
        private static void LandHome(Operation op, Vessel carrier)
        {
            OrbitChange land = OrbitChange.Land(Core.Landing.LandingTarget.LaunchPad);
            string? refused = MissionDesk.Chain(op.Codename + " landing", land, new[] { VesselTasks.Task(carrier, null, land) }, VesselTasks.Id(carrier), 0, out _);
            MissionDesk.Runner.Note(op.Id, refused == null ? "home: landing approved" : "home, landing not flown: " + refused + "; brief it by hand");
        }

        /// <summary>After the last burn, flies one small trim inside the operation when the orbit still misses its target.</summary>
        private static void AfterBurn(Operation op, int burnIndex, IGuidance executor)
        {
            string note = executor.Note;
            if (op.Change.Rendezvous != null)
            {
                AfterRendezvousBurn(op, burnIndex, executor);
                return;
            }
            if (burnIndex == op.Burns.Count - 1 && !op.IsGroup && !Trimmed.Contains(op.Id))
            {
                Briefing? trim = Plan(op, executor.Vessel);
                double residual = trim?.RequiredDeltaV ?? 0;
                double planned = op.Burns.Sum(b => b.DeltaV);
                if (trim != null && residual >= TrimFloor && trim.Burns.Count > 0 && trim.CanApprove)
                {
                    if (CorrectionRule.Classify(planned, residual) == CorrectionVerdict.FlyInside)
                    {
                        Trimmed.Add(op.Id);
                        MissionDesk.Runner.AddCorrection(op.Id, trim.Burns, note + "; trim burn of " + residual.ToString("0.0") + " m/s added");
                        return;
                    }
                    note += "; residual " + residual.ToString("0.0") + " m/s needs a new briefing";
                }
            }
            MissionDesk.Runner.BurnDone(op.Id, note);
        }

        /// <summary>
        /// On the way to another body, what is left is planned again from the path actually flown. A course correction within
        /// the correction rule flies inside the operation; a larger one waits for a new briefing.
        /// </summary>
        private static void AfterTransferBurn(Operation op, IGuidance executor)
        {
            string note = executor.Note;
            Briefing? rest = Plan(op, executor.Vessel);
            string? stop = rest == null ? "the rest of the transfer could not be planned"
                : !rest.CanApprove ? rest.Contingencies.FirstOrDefault(c => c.Severity == Severity.Red)?.Text ?? "the rest of the transfer cannot be approved"
                : null;
            PlannedBurn? first = rest?.Burns.FirstOrDefault();
            double planned = op.Burns.Sum(b => b.DeltaV);
            if (stop == null && first != null && first.Label == TransferPlanner.CorrectionLabel && CorrectionRule.Classify(planned, first.DeltaV) == CorrectionVerdict.NeedsBriefing)
            {
                stop = "a course correction of " + first.DeltaV.ToString("0.0", CultureInfo.InvariantCulture) + " m/s needs a new briefing";
            }
            if (stop != null)
            {
                MissionDesk.Runner.BurnDone(op.Id, note);
                MissionDesk.Runner.Hold(op.Id, stop);
                return;
            }
            string added = first != null && first.Label == TransferPlanner.CorrectionLabel ? "; course correction of " + first.DeltaV.ToString("0.0", CultureInfo.InvariantCulture) + " m/s added" : "; the rest re-planned from the path flown";
            MissionDesk.Runner.AddCorrection(op.Id, rest!.Burns, note + added);
        }

        /// <summary>A transfer coasting into a new sphere of influence re-times what is left on the game's own patch.</summary>
        private void FollowSoi()
        {
            foreach (Operation op in MissionDesk.Runner.Operations.Where(o => o.Change.Kind == OrbitChangeKind.Transfer && o.State == StepState.Executing && o.Id != _executorOperation).ToList())
            {
                Vessel? vessel = VesselTasks.Find(op.VesselIds[0]);
                if (vessel == null)
                {
                    continue;
                }
                string body = vessel.mainBody.bodyName;
                if (_soi.TryGetValue(op.Id, out string was) && was != body)
                {
                    Briefing? rest = Plan(op, vessel);
                    if (rest != null && rest.CanApprove && rest.Burns.Count > 0 && rest.Burns[0].Label != TransferPlanner.CorrectionLabel)
                    {
                        MissionDesk.Runner.Revise(op.Id, rest.Burns, "entered " + body + "'s sphere of influence; the rest re-timed");
                    }
                    else
                    {
                        MissionDesk.Runner.Note(op.Id, "entered " + body + "'s sphere of influence");
                    }
                }
                _soi[op.Id] = body;
            }
        }

        /// <summary>
        /// A rendezvous re-plans from where both vessels really are after every burn. Burns that cost no more than the rest
        /// of the approved plan plus the correction allowance fly inside the operation; anything more needs a new briefing.
        /// </summary>
        private static void AfterRendezvousBurn(Operation op, int burnIndex, IGuidance executor)
        {
            if (executor is RelativeVelocityKiller || executor is DockingController)
            {
                MissionDesk.Runner.BurnDone(op.Id, executor.Note);
                return;
            }
            Briefing? plan = Plan(op, executor.Vessel);
            if (plan == null || !plan.CanApprove)
            {
                MissionDesk.Runner.Release(op.Id, executor.Note + "; " + (plan?.Contingencies.FirstOrDefault(c => c.Severity == Severity.Red)?.Text ?? "the rest of the rendezvous could not be planned"));
                ControlRelease.Show();
                return;
            }
            double approved = op.Burns.Skip(burnIndex + 1).Sum(b => b.DeltaV);
            double extra = plan.RequiredDeltaV - approved;
            if (CorrectionRule.Classify(op.Burns.Sum(b => b.DeltaV), extra) == CorrectionVerdict.NeedsBriefing)
            {
                MissionDesk.Runner.Release(op.Id, executor.Note + "; the rest now needs " + plan.RequiredDeltaV.ToString("0.0", CultureInfo.InvariantCulture) + " m/s against " + approved.ToString("0.0", CultureInfo.InvariantCulture) + " m/s approved; brief it again");
                ControlRelease.Show();
                return;
            }
            MissionDesk.Runner.AddCorrection(op.Id, plan.Burns, executor.Note + "; " + plan.Burns.Count + " burns of " + plan.RequiredDeltaV.ToString("0.0", CultureInfo.InvariantCulture) + " m/s re-planned from both orbits");
        }

        /// <summary>Out of the atmosphere: the estimated finishing burn makes way for burns planned from the orbit reached.</summary>
        private static void AfterAscent(Operation op, IGuidance ascent)
        {
            Briefing? plan = Plan(op, ascent.Vessel);
            if (plan == null || !plan.CanApprove)
            {
                MissionDesk.Runner.Release(op.Id, ascent.Note + "; " + (plan?.Contingencies.FirstOrDefault(c => c.Severity == Severity.Red)?.Text ?? "the finishing burns could not be planned"));
                ControlRelease.Show();
                return;
            }
            string note = plan.Burns.Count == 0 ? "; orbit reached" : "; " + plan.Burns.Count + (plan.Burns.Count == 1 ? " finishing burn" : " finishing burns") + " of " + plan.RequiredDeltaV.ToString("0", CultureInfo.InvariantCulture) + " m/s planned from the orbit reached";
            MissionDesk.Runner.AddCorrection(op.Id, plan.Burns, ascent.Note + note);
        }

        /// <summary>Plans a missed burn again from where the vessel is now, toward the same final orbit.</summary>
        /// <param name="why">How the window was missed, for the note; null when the burn simply came and went.</param>
        private static void Replan(Operation op, string? why = null)
        {
            if (op.IsGroup)
            {
                ReplanInGroup(op, why);
                return;
            }
            Vessel? vessel = VesselTasks.Find(op.VesselIds[0]);
            Briefing? plan = vessel == null ? null : Plan(op, vessel);
            if (plan == null || !plan.CanApprove || plan.Burns.Count == 0)
            {
                string missed = "MISSED: " + (why == null ? "" : why + "; ") + (plan?.Contingencies.FirstOrDefault()?.Text ?? "no new window");
                MissionDesk.Runner.Note(op.Id, missed);
                Announce(op, missed, why);
                return;
            }
            string note = (why ?? "missed the window") + "; re-planned to " + KerbinTime.Format(plan.Burns[0].StartUt);
            MissionDesk.Runner.Replanned(op.Id, plan.Burns, note);
            Announce(op, note, why);
        }

        /// <summary>A late ignition is news in the game and the log too, not only on the dashboard.</summary>
        private static void Announce(Operation op, string note, string? why)
        {
            if (why == null)
            {
                return;
            }
            PluginFiles.Log("late burn " + op.Codename + ": " + note);
            ScreenMessages.PostScreenMessage("FLTCOM " + op.Codename + ": " + note, 8f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// A group burn passed unflown. If the player flew it by hand the orbit already matches and it counts as flown;
        /// otherwise that vessel's remaining burns move whole orbits later, the rest of the group keeps its schedule.
        /// </summary>
        private static void ReplanInGroup(Operation op, string? why)
        {
            WarpDriver.Stop();
            int index = MissionRunner.BurnIndex(op);
            PlannedBurn burn = op.Burns[index];
            Vessel? vessel = VesselTasks.Find(burn.VesselId);
            if (vessel == null)
            {
                MissionDesk.Runner.Note(op.Id, "MISSED: vessel " + burn.VesselId + " is gone");
                return;
            }
            if (burn.OrbitAfter != null && GroupPlanner.Matches(VesselTasks.Orbit(vessel.orbit), burn.OrbitAfter))
            {
                MissionDesk.Runner.FlownByHand(op.Id, vessel.vesselName + ": \"" + burn.Label + "\" flown by hand");
                return;
            }
            double? slot = null;
            if (op.Change.Kind == OrbitChangeKind.SpreadEvenly)
            {
                double r = vessel.mainBody.Radius + op.Change.Target;
                slot = Math.Sqrt(vessel.mainBody.gravParameter / (r * r * r));
            }
            double now = Planetarium.GetUniversalTime();
            var shift = GroupPlanner.MissedShift(op.Burns.Skip(index).ToList(), burn.VesselId, vessel.orbit.period, slot, now + MissionRunner.MinWarp);
            if (shift == null)
            {
                MissionDesk.Runner.Note(op.Id, "MISSED: no free window for " + vessel.vesselName);
                return;
            }
            int orbits = (int)Math.Round(shift.Value.Seconds / vessel.orbit.period);
            string note = vessel.vesselName + (why == null ? " missed \"" + burn.Label + "\": moved " : ": " + why + ", \"" + burn.Label + "\" moved ") + orbits.ToString(CultureInfo.InvariantCulture) + (orbits == 1 ? " orbit" : " orbits") + " later";
            double degrees = shift.Value.PhaseError * UtilMath.Rad2Deg;
            if (degrees >= 0.5)
            {
                note += ", it will end " + degrees.ToString("0.0", CultureInfo.InvariantCulture) + " deg off its slot";
            }
            MissionDesk.Runner.Delay(op.Id, burn.VesselId, shift.Value.Seconds, note);
            Announce(op, note, why);
        }

        /// <summary>The impact course the vessel coasts on now, when it hits before <paramref name="untilUt"/>.</summary>
        private static ImpactCourse? ImpactBefore(Vessel vessel, double untilUt)
        {
            if (vessel.LandedOrSplashed)
            {
                return null;
            }
            ImpactCourse? impact = FlightSafety.Impact(VesselTasks.System(), VesselTasks.Trajectory(vessel.orbit, Planetarium.GetUniversalTime()));
            return impact != null && impact.Ut < untilUt ? impact : null;
        }

        private static void RaiseAlarm(Operation op, FlightAlarm alarm, string context = "")
        {
            WarpDriver.Stop();
            MissionDesk.Runner.Alarm(op.Id, alarm);
            PluginFiles.Log("ALARM " + op.Codename + ": " + context + alarm.Text);
        }

        /// <summary>
        /// Before every burn of a single-vessel operation: a coast that hits the ground first raises the alarm, and a burn
        /// that no longer fits the path really flown is planned again from it, or holds when that cannot be done safely.
        /// </summary>
        private void Guard()
        {
            double now = Planetarium.GetUniversalTime();
            SolarSystem? system = null;
            foreach (Operation op in MissionDesk.Runner.Operations.Where(o => o.State == StepState.Waiting && !o.IsGroup).ToList())
            {
                int index = MissionRunner.BurnIndex(op);
                PlannedBurn burn = op.Burns[index];
                Vessel? vessel = VesselTasks.Find(burn.VesselId);
                if (vessel == null || vessel.LandedOrSplashed || burn.StartUt <= now)
                {
                    continue;
                }
                if (op.Change.Kind == OrbitChangeKind.Land)
                {
                    GuardDeorbit(op, index, burn, vessel, now);
                    continue;
                }
                if (LaunchBriefing.IsAscent(burn) || burn.Label == Rendezvous.HoldLabel || burn.Label == DockingApproach.Label)
                {
                    continue;
                }
                system ??= VesselTasks.System();
                Patch path = VesselTasks.Trajectory(vessel.orbit, now);
                if (FlightSafety.Impact(system, path) is ImpactCourse impact && impact.Ut < burn.StartUt)
                {
                    RaiseAlarm(op, impact.Alarm(), vessel.vesselName + " before \"" + burn.Label + "\": ");
                    continue;
                }
                if (op.Change.Rendezvous != null || op.Change.Kind == OrbitChangeKind.DeployConstellation)
                {
                    continue;
                }
                string? stale = FlightSafety.Stale(system, path, burn, index + 1 < op.Burns.Count ? op.Burns[index + 1].StartUt : double.PositiveInfinity);
                if (stale != null)
                {
                    Rework(op, index, burn, vessel, system, path, "\"" + burn.Label + "\" " + stale);
                }
            }
        }

        private void Rework(Operation op, int index, PlannedBurn burn, Vessel vessel, SolarSystem system, Patch path, string why)
        {
            Briefing? plan = Plan(op, vessel);
            var burns = plan?.Burns ?? new List<PlannedBurn>();
            string? stop = plan == null ? "it could not be planned again"
                : !plan.CanApprove ? plan.Contingencies.First(c => c.Severity == Severity.Red).Text
                : burns.Count == 0 ? "nothing is left to burn"
                : FlightSafety.Stale(system, path, burns[0], burns.Count > 1 ? burns[1].StartUt : double.PositiveInfinity) is string still ? "the new plan " + still
                : CorrectionRule.Classify(op.Burns.Sum(b => b.DeltaV), plan.RequiredDeltaV - op.Burns.Skip(index).Sum(b => b.DeltaV)) == CorrectionVerdict.NeedsBriefing ? "the new plan needs " + plan.RequiredDeltaV.ToString("0.0", CultureInfo.InvariantCulture) + " m/s; brief it again"
                : null;
            if (_executorOperation == op.Id)
            {
                DropExecutor();
            }
            PluginFiles.Log("stale burn " + op.Codename + " #" + index + ": " + why + (stop == null ? "; re-planned" : "; holding: " + stop));
            if (stop != null)
            {
                WarpDriver.Stop();
                MissionDesk.Runner.Hold(op.Id, "STALE BURN: " + why + "; " + stop);
                return;
            }
            MissionDesk.Runner.Revise(op.Id, burns, why + "; re-planned from the path flown");
        }

        /// <summary>P17: shortly before the deorbit burn the landing is planned again; if it can no longer be flown the vessel stays in orbit.</summary>
        private void GuardDeorbit(Operation op, int index, PlannedBurn burn, Vessel vessel, double now)
        {
            if (burn.Label != LandingBriefing.DeorbitLabel || burn.StartUt - now > MissionRunner.FlyAsk || !_deorbitChecked.Add(op.Id + "/" + index + "/" + burn.StartUt.ToString("0", CultureInfo.InvariantCulture)))
            {
                return;
            }
            Briefing? plan = Plan(op, vessel);
            FlightAlarm? abort = plan == null
                ? new FlightAlarm(AlarmKind.Landing, vessel.mainBody.bodyName, "the landing could not be planned again; the vessel stays in orbit")
                : FlightSafety.BeforeDeorbit(vessel.mainBody.bodyName, plan);
            if (abort != null)
            {
                RaiseAlarm(op, abort, vessel.vesselName + " before the deorbit: ");
            }
        }

        /// <summary>Once a second during the descent: red while the touchdown is predicted too hard or the fuel cannot brake it; guidance flies on.</summary>
        private void WatchDescent(Operation op, LandingGuidance descent)
        {
            if (Time.realtimeSinceStartup < _nextDescentCheck)
            {
                return;
            }
            _nextDescentCheck = Time.realtimeSinceStartup + WatchInterval;
            double left = VesselTasks.Stages(descent.Vessel).Sum(s => s.DeltaV);
            FlightAlarm? alarm = FlightSafety.Descent(descent.Vessel.mainBody.bodyName, descent.Prediction, left);
            if (alarm != null && MissionDesk.Runner.AlarmOf(op.Id)?.Text != alarm.Text)
            {
                PluginFiles.Log("ALARM " + op.Codename + ": " + alarm.Text);
            }
            MissionDesk.Runner.Warn(op.Id, alarm);
        }

        /// <summary>Drops out of warp and asks again when the player changes ship or a group vessel loses its signal.</summary>
        private void Watch(ActiveView view)
        {
            var warping = MissionDesk.Runner.Operations.Where(o => o.IsGroup && MissionDesk.Runner.WarpsThrough(o.Id)).ToList();
            if (view.ActiveVesselId != _lastActive)
            {
                string? from = _lastActive;
                _lastActive = view.ActiveVesselId;
                if (from != null && view.ActiveVesselId != null)
                {
                    string name = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.vesselName : view.ActiveVesselId;
                    Interrupt(warping, "you switched to " + name);
                }
            }
            if (Time.realtimeSinceStartup < _nextWatch)
            {
                return;
            }
            _nextWatch = Time.realtimeSinceStartup + WatchInterval;
            Guard();
            foreach (Operation op in MissionDesk.Runner.Operations.Where(o => o.IsGroup && (o.State == StepState.Waiting || o.State == StepState.Executing)))
            {
                foreach (string id in op.VesselIds)
                {
                    Vessel? vessel = VesselTasks.Find(id);
                    bool silent = vessel != null && !VesselTasks.HasLink(vessel);
                    if (silent && _silent.Add(id))
                    {
                        Interrupt(warping.Where(o => o.Id == op.Id), "lost signal to " + vessel!.vesselName);
                    }
                    else if (!silent)
                    {
                        _silent.Remove(id);
                    }
                }
            }
        }

        private static void Interrupt(IEnumerable<Operation> operations, string why)
        {
            foreach (Operation op in operations)
            {
                WarpDriver.Stop();
                MissionDesk.Runner.Interrupt(op.Id, why);
                PluginFiles.Log("warp interrupted for " + op.Codename + ": " + why);
            }
        }

        /// <summary>The first vessel, once per operation, that has less delta-v left than its remaining burns need.</summary>
        private string? LowFuel(Operation op, int index)
        {
            foreach (var burns in op.Burns.Skip(index).GroupBy(b => b.VesselId))
            {
                Vessel? vessel = VesselTasks.Find(burns.Key);
                if (vessel == null || vessel.loaded)
                {
                    continue;
                }
                double need = burns.Sum(b => b.DeltaV);
                double have = DeltaVEstimator.Estimate(VesselTasks.Parts(vessel)).Sum(s => s.DeltaV);
                if (have < need && _lowFuel.Add(op.Id + "/" + burns.Key))
                {
                    return "low fuel: " + vessel.vesselName + " has " + have.ToString("0", CultureInfo.InvariantCulture) + " m/s for " + need.ToString("0", CultureInfo.InvariantCulture) + " m/s of burns";
                }
            }
            return null;
        }

        /// <summary>The player flies this vessel: show the time to ignition and never touch the controls.</summary>
        private void Countdown(PlannedBurn burn)
        {
            if (Time.realtimeSinceStartup < _nextCountdown)
            {
                return;
            }
            _nextCountdown = Time.realtimeSinceStartup + 1f;
            double lead = burn.StartUt - Planetarium.GetUniversalTime();
            string when = lead > 0 ? "T-" + KerbinTime.Span(lead) : "now";
            ScreenMessages.PostScreenMessage("FLTCOM: \"" + burn.Label + "\" " + when + ". Fly it yourself, or click LET FLTCOM FLY IT in the display", 1.2f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static Briefing? Plan(Operation op, Vessel vessel)
        {
            if (op.Change.Kind == OrbitChangeKind.DeployConstellation)
            {
                return null;
            }
            try
            {
                VesselTask? target = op.Change.Rendezvous is RendezvousTarget meet && VesselTasks.Find(meet.VesselId) is Vessel found ? VesselTasks.Task(found, null) : null;
                return BriefingBuilder.Build(op.Codename, op.Change, new[] { VesselTasks.Task(vessel, null, op.Change) }, Planetarium.GetUniversalTime(), target: target);
            }
            catch (Exception e)
            {
                PluginFiles.Log("re-plan of " + op.Codename + " failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Lets go of the vessel. One left falling (in a descent, or coasting into the ground) holds with the red impact
        /// alarm and a large on-screen order to take the controls, so the release cannot pass unnoticed.
        /// </summary>
        private void Release(string operationId, string why)
        {
            Vessel? vessel = _executor?.Vessel;
            bool descending = _executor is LandingGuidance;
            DropExecutor();
            PluginFiles.Log("control released: " + why);
            ImpactCourse? impact = vessel == null ? null : ImpactBefore(vessel, double.PositiveInfinity);
            if (vessel != null && (descending || impact != null))
            {
                string body = vessel.mainBody.bodyName;
                var alarm = new FlightAlarm(AlarmKind.Impact, body, "released " + (impact?.Describe() ?? "in the descent to " + body) + ": " + why);
                WarpDriver.Stop();
                MissionDesk.Runner.Alarm(operationId, alarm);
                ScreenMessages.PostScreenMessage("<b><size=150%>" + vessel.vesselName.ToUpperInvariant() + " RELEASED ON IMPACT COURSE WITH " + body.ToUpperInvariant() + "\nTAKE THE CONTROLS</size></b>", 15f, ScreenMessageStyle.UPPER_CENTER, Color.red);
                PluginFiles.Log("ALARM " + vessel.vesselName + ": " + alarm.Text);
                return;
            }
            MissionDesk.Runner.Release(operationId, why);
            ControlRelease.Show();
        }

        private void DropExecutor()
        {
            _release = new ReleaseRule();
            _executor?.Stop();
            _executor = null;
            _executorOperation = null;
            _executorBurn = -1;
        }

        private ActiveView View()
        {
            GameScene scene = HighLogic.LoadedScene == GameScenes.FLIGHT ? GameScene.Flight
                : HighLogic.LoadedScene == GameScenes.TRACKSTATION ? GameScene.TrackingStation
                : HighLogic.LoadedScene == GameScenes.SPACECENTER ? GameScene.SpaceCenter
                : GameScene.Other;
            Vessel? active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (active == null)
            {
                return new ActiveView(scene, null, ActiveVesselCondition.StableOrbit, false);
            }
            CelestialBody body = active.mainBody;
            double floor = body.atmosphere ? body.atmosphereDepth : body.pqsController != null ? body.pqsController.mapMaxHeight : 0;
            ActiveVesselCondition condition = active.LandedOrSplashed ? ActiveVesselCondition.Landed
                : active.situation == Vessel.Situations.ORBITING && active.orbit.PeA > floor ? ActiveVesselCondition.StableOrbit
                : ActiveVesselCondition.Unstable;
            bool burning = _executor == null && active.ctrlState.mainThrottle > 0.01f;
            if (Time.realtimeSinceStartup >= _nextTurnLead)
            {
                _nextTurnLead = Time.realtimeSinceStartup + WatchInterval;
                _turnLead = IgnitionTiming.TurnLead(AngularAcceleration(active));
            }
            return new ActiveView(scene, VesselTasks.Id(active), condition, burning, _turnLead);
        }

        /// <summary>The weaker of pitch and yaw, rad/s², from the torque SAS can command and the vessel's moment of inertia; null on rails.</summary>
        private static double? AngularAcceleration(Vessel vessel)
        {
            Vector3 inertia = vessel.MOI;
            if (vessel.packed || inertia.x <= 0 || inertia.z <= 0)
            {
                return null;
            }
            Vector3 up = Vector3.zero;
            Vector3 down = Vector3.zero;
            foreach (ITorqueProvider provider in vessel.parts.SelectMany(p => p.FindModulesImplementing<ITorqueProvider>()))
            {
                provider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                up += new Vector3(Math.Abs(pos.x), Math.Abs(pos.y), Math.Abs(pos.z));
                down += new Vector3(Math.Abs(neg.x), Math.Abs(neg.y), Math.Abs(neg.z));
            }
            return Math.Min(Math.Min(up.x, down.x) / inertia.x, Math.Min(up.z, down.z) / inertia.z);
        }
    }
}
