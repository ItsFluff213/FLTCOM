using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Groups;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.Protocol;
using FLTCOM.Server;
using UnityEngine;
using CorePrompt = FLTCOM.Core.Execution.Prompt;
using OrbitChange = FLTCOM.Core.Planning.OrbitChange;

namespace FLTCOM.Execution
{
    /// <summary>
    /// The plugin's side of the mission runner: turns display messages into runner calls, briefs from live vessels,
    /// and publishes the operations and prompts. It lives for the whole session, across scenes.
    /// </summary>
    internal static class MissionDesk
    {
        private const float PublishInterval = 0.25f;

        private static int _published = -1;
        private static int _groupsPublished = -1;
        private static float _nextPublish;
        private static int _nextOperation = 1;

        public static MissionRunner Runner { get; } = new MissionRunner();

        public static FleetGroups Groups { get; } = new FleetGroups();

        public static HttpServer? Server { get; set; }

        public static void Handle(ClientMessage message)
        {
            try
            {
                string? error = message.Match(
                    hello => null,
                    ping => null,
                    brief => Brief(brief),
                    approve => Approve(approve),
                    scrub => Runner.Scrub(scrub.OperationId),
                    resume => Runner.Resume(resume.OperationId),
                    answer => Runner.Answer(answer.PromptId, answer.Accept),
                    edit => EditGroup(edit));
                if (error != null)
                {
                    PluginFiles.Log("mission: " + error);
                    ScreenMessages.PostScreenMessage("FLTCOM: " + error, 5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            catch (Exception e)
            {
                PluginFiles.Log("mission message failed: " + e);
            }
        }

        /// <summary>Sends the groups, the operations and the prompts when they changed, at most four times a second.</summary>
        public static void Publish()
        {
            if (Server != null && Groups.Revision != _groupsPublished)
            {
                _groupsPublished = Groups.Revision;
                Server.Send(new GroupRoster(Groups.All.Select(g => new Group(g.Id, g.Name, g.VesselIds.Select(Uint).ToList())).ToList()), retain: true);
            }
            if (Server == null || Runner.Revision == _published || Time.realtimeSinceStartup < _nextPublish)
            {
                return;
            }
            _published = Runner.Revision;
            _nextPublish = Time.realtimeSinceStartup + PublishInterval;
            Server.Send(new Missions(Planetarium.GetUniversalTime(), Runner.Operations.Select(Status).ToList(), Runner.Prompts.Select(Wire).ToList()), retain: true);
        }

        private static string? EditGroup(GroupEdit edit)
        {
            var ids = edit.VesselIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList();
            return edit.Action switch
            {
                GroupAction.Create => Groups.Create(edit.Name, ids, out _),
                GroupAction.Rename => Groups.Rename(edit.GroupId, edit.Name),
                GroupAction.Delete => Groups.Delete(edit.GroupId),
                GroupAction.Add => Groups.Add(edit.GroupId, ids),
                _ => Groups.Remove(edit.GroupId, ids),
            };
        }

        /// <summary>One briefing for every member of a group. A member that cannot fly turns the briefing red and says why.</summary>
        private static string? BriefGroup(BriefRequest request, FleetGroup group, string codename)
        {
            OrbitChange change = Change(request.Change, FlightGlobals.GetHomeBody());
            var gates = new List<Contingency>();
            var tasks = new List<VesselTask>();
            foreach (string id in group.VesselIds)
            {
                Vessel? vessel = VesselTasks.Find(id);
                string? refusal = vessel == null ? "vessel " + id + " is gone" : Refusal(vessel, change.Kind == OrbitChangeKind.Launch);
                if (refusal == null && !vessel!.loaded)
                {
                    Thruster? thruster = BackgroundBurn.Thruster(VesselTasks.Parts(vessel), out string? dry);
                    string? held = dry ?? Core.AutoSystems.BackgroundStaging.Refusal(VesselTasks.StageParts(vessel), thruster!.Stage, change.AutoSystems);
                    refusal = held == null ? null : vessel.vesselName + ": " + held;
                }
                if (refusal != null)
                {
                    gates.Add(new Contingency(Severity.Red, id, refusal));
                    continue;
                }
                tasks.Add(VesselTasks.Task(vessel!, null));
            }
            if (tasks.Count == 0)
            {
                string error = group.Name + " has no vessel that can fly: " + (gates.FirstOrDefault()?.Text ?? "it is empty");
                Server?.Send(Refused(request, codename, error), retain: false);
                return error;
            }
            Briefing briefing = BriefingBuilder.Build(codename, change, tasks, Planetarium.GetUniversalTime(), gates: gates.Concat(Reserved(change)).ToList(), target: TargetTask(change));
            AutoBriefing auto = AutoBriefing.Of(change, briefing);
            briefing = briefing.Adding(auto.Hazards);
            string? operationId = null;
            if (briefing.CanApprove && briefing.Burns.Count > 0)
            {
                operationId = "op-" + (_nextOperation++).ToString(CultureInfo.InvariantCulture) + "-" + group.Id;
                Runner.AddBriefed(operationId, codename, change, briefing);
                LandingReader.Predicted(operationId, briefing.Landing);
            }
            Server?.Send(Message(request, codename, operationId, Uint(tasks[0].VesselId), briefing, auto), retain: false);
            return null;
        }

        private static string? Brief(BriefRequest request)
        {
            string codename = string.IsNullOrWhiteSpace(request.Codename) ? Codename.Generate((ulong)DateTime.UtcNow.Ticks) : request.Codename!;
            if (request.Change.Kind == ChangeKind.Transfer && (request.GroupId != null || FlightGlobals.GetBodyByName(request.Change.Body ?? string.Empty) == null))
            {
                string error = request.GroupId != null ? "a transfer flies one vessel at a time" : "pick a body to transfer to";
                Server?.Send(Refused(request, codename, error), retain: false);
                return error;
            }
            if (request.GroupId != null)
            {
                FleetGroup? group = Groups.Find(request.GroupId);
                if (group == null)
                {
                    string error = "no group " + request.GroupId;
                    Server?.Send(Refused(request, codename, error), retain: false);
                    return error;
                }
                return BriefGroup(request, group, codename);
            }
            string vesselId = request.VesselId.ToString(CultureInfo.InvariantCulture);
            Vessel? vessel = VesselTasks.Find(vesselId);
            string? refusal = vessel == null ? "vessel " + vesselId + " is gone" : Refusal(vessel, request.Change.Kind == ChangeKind.Launch);
            if (refusal == null && request.Change.Kind == ChangeKind.Land && (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ActiveVessel != vessel))
            {
                refusal = "a landing is briefed and flown on the vessel you fly: switch to " + vessel!.vesselName + " first";
            }
            if (refusal != null)
            {
                Server?.Send(Refused(request, codename, refusal), retain: false);
                return refusal;
            }
            OrbitChange change = Change(request.Change, vessel!.mainBody);
            LogEstimate(vessel);
            double now = Planetarium.GetUniversalTime();
            var gates = new List<Contingency>();
            if (change.Kind == OrbitChangeKind.Dock)
            {
                change = PickPort(change, vessel, gates);
            }
            gates.AddRange(Reserved(change));
            Briefing briefing = BriefingBuilder.Build(codename, change, new[] { VesselTasks.Task(vessel!, request.PinnedStage, change) }, now, gates: gates, target: TargetTask(change));
            AutoBriefing auto = AutoBriefing.Of(change, briefing);
            briefing = briefing.Adding(auto.Hazards);
            string? operationId = null;
            if (briefing.CanApprove && briefing.Burns.Count > 0)
            {
                operationId = "op-" + (_nextOperation++).ToString(CultureInfo.InvariantCulture) + "-" + vesselId;
                Runner.AddBriefed(operationId, codename, change, briefing);
                LandingReader.Predicted(operationId, briefing.Landing);
            }
            Server?.Send(Message(request, codename, operationId, request.VesselId, briefing, auto), retain: false);
            return null;
        }

        /// <summary>
        /// Briefs and approves an operation the runner starts by itself, such as a carrier's flight home, with its first burn
        /// no earlier than <paramref name="notBefore"/>; returns why it could not.
        /// </summary>
        /// <param name="endUt">When its last planned burn ends; <paramref name="notBefore"/> when nothing was approved.</param>
        public static string? Chain(string codename, OrbitChange change, IReadOnlyList<VesselTask> tasks, string suffix, double notBefore, out double endUt)
        {
            endUt = notBefore;
            Briefing briefing = BriefingBuilder.Build(codename, change, tasks, Math.Max(Planetarium.GetUniversalTime(), notBefore));
            AutoBriefing auto = AutoBriefing.Of(change, briefing);
            briefing = briefing.Adding(auto.Hazards);
            if (!briefing.CanApprove || briefing.Burns.Count == 0)
            {
                return briefing.Contingencies.FirstOrDefault(c => c.Severity == Severity.Red)?.Text ?? "nothing to fly";
            }
            string operationId = "op-" + (_nextOperation++).ToString(CultureInfo.InvariantCulture) + "-" + suffix;
            Runner.AddBriefed(operationId, codename, change, briefing);
            LandingReader.Predicted(operationId, briefing.Landing);
            string? refused = Runner.Approve(operationId, codename, uplink: true);
            if (refused == null)
            {
                endUt = Math.Max(notBefore, briefing.Burns.Max(b => b.EndUt));
            }
            return refused;
        }

        private static BriefingMessage Message(BriefRequest request, string codename, string? operationId, uint vesselId, Briefing briefing, AutoBriefing auto) =>
            new BriefingMessage(
                request.RequestId,
                operationId,
                codename,
                vesselId,
                request.GroupId,
                request.Change,
                briefing.Change,
                briefing.Burns.Select(Wire).ToList(),
                briefing.RequiredDeltaV,
                briefing.AvailableDeltaV,
                double.IsInfinity(briefing.Margin) ? (double?)null : briefing.Margin,
                briefing.Stages.Select(s => new StageLine(Uint(s.VesselId), s.Stage, s.DeltaV, s.Assigned, s.Twr, s.BurnTime, s.Short)).ToList(),
                briefing.Contingencies.Select(c => new ContingencyNote(c.Severity == Severity.Red ? Alert.Red : Alert.Amber, Uint(c.VesselId), c.Text)).ToList(),
                operationId != null,
                null,
                Plot(briefing.Porkchop),
                auto.Plan.Concat(ReleaseLines(vesselId, briefing.Deployment)).ToList(),
                auto.Actions,
                Deployment(briefing.Deployment, vesselId));

        /// <summary>One stage-plan line per satellite release, in the order the carrier lets them go.</summary>
        private static IEnumerable<StagePlanLine> ReleaseLines(uint vesselId, Core.Constellation.DeployPlan? plan) =>
            plan == null || !plan.Ok
                ? Enumerable.Empty<StagePlanLine>()
                : plan.Releases.Select(r => new StagePlanLine(vesselId, 0, "release " + r.Name, Apsis(plan, r) + ", pass " + r.Pass.ToString(CultureInfo.InvariantCulture), false, null, null));

        private static string Apsis(Core.Constellation.DeployPlan plan, Core.Constellation.Release r) =>
            plan.Method == Core.Constellation.DeployMethod.CarrierPhasing ? "on the circle" : r.AtApoapsis ? "at apoapsis" : "at periapsis";

        private static Core.Protocol.Deployment? Deployment(Core.Constellation.DeployPlan? plan, uint vesselId)
        {
            CelestialBody? body = VesselTasks.Find(vesselId.ToString(CultureInfo.InvariantCulture))?.mainBody;
            if (plan == null || body == null)
            {
                return null;
            }
            Core.Constellation.ReturnPlan back = plan.Return ?? Core.Constellation.ReturnPlan.None;
            double now = Planetarium.GetUniversalTime();
            return new Core.Protocol.Deployment(
                plan.Altitude,
                Core.Constellation.ConstellationPlanner.SynchronousAltitude(VesselTasks.Body(body)),
                plan.SuggestedAltitude,
                (Core.Protocol.DeployMethod)(int)plan.Method,
                plan.Working == null ? null : Frame(body.bodyName, plan.Working, now),
                plan.Releases.Select(r => new DeployPayload(r.Payload.CoreId, r.Name, r.Payload.Mass * 1000, r.Payload.DeltaV, r.CircularizeDeltaV)).ToList(),
                plan.Releases.Select(r => new DeployRelease(r.Payload.CoreId, r.Name, r.Pass, r.ReleaseUt, r.NodeUt, Apsis(plan, r), r.SpacingError, Frame(body.bodyName, r.Final, r.NodeUt))).ToList(),
                new CarrierReturnPlan((CarrierReturnKind)(int)back.Kind, back.DeltaV, back.Available, back.Text, back.Fallback),
                new Core.Protocol.LaunchStagePlan(plan.LaunchStage.ComesHome, plan.LaunchStage.Name, plan.LaunchStage.DeltaV, plan.LaunchStage.Available, plan.LaunchStage.Text));
        }

        private static PorkchopPlot? Plot(PorkchopGrid? grid) =>
            grid == null
                ? null
                : new PorkchopPlot(grid.DepartureStart, grid.DepartureStep, grid.FlightStart, grid.FlightStep, grid.Columns, grid.Rows,
                    grid.DeltaV.Select(v => double.IsInfinity(v) || double.IsNaN(v) ? 0 : v).ToList(), grid.Best.DepartureUt, grid.Best.FlightTime, grid.Best.DeltaV);

        private static string? Approve(Core.Protocol.Approve approve)
        {
            Operation? op = Runner.Find(approve.OperationId);
            if (op != null && op.IsGroup)
            {
                var vessels = op.VesselIds.Select(id => (Id: id, Vessel: VesselTasks.Find(id))).ToList();
                string? gone = vessels.Where(v => v.Vessel == null).Select(v => v.Id).FirstOrDefault();
                if (gone != null)
                {
                    return "vessel " + gone + " is gone";
                }
                var silent = vessels.Where(v => !VesselTasks.HasLink(v.Vessel!)).Select(v => v.Vessel!.vesselName).ToList();
                if (silent.Count > 0)
                {
                    return "needs signal to upload: " + string.Join(", ", silent) + " cannot reach home";
                }
                return Runner.Approve(approve.OperationId, approve.Codename, uplink: true);
            }
            Vessel? vessel = op == null ? null : VesselTasks.Find(op.VesselIds[0]);
            if (op != null && vessel == null)
            {
                return "vessel " + op.VesselIds[0] + " is gone";
            }
            return Runner.Approve(approve.OperationId, approve.Codename, vessel != null && VesselTasks.HasLink(vessel));
        }

        /// <summary>Career and compatibility gates that keep a vessel from flying operations.</summary>
        public static string? Refusal(Vessel vessel, bool launch)
        {
            if (!ManeuverNodesUnlocked())
            {
                return "maneuver nodes are not unlocked yet: upgrade Mission Control and the Tracking Station";
            }
            if (launch && vessel.situation != Vessel.Situations.PRELAUNCH)
            {
                return vessel.vesselName + " is not on the launch pad: a launch flies a rocket waiting on the pad";
            }
            if (!launch && vessel.LandedOrSplashed)
            {
                return vessel.vesselName + " is on the ground: orbit changes need a vessel in flight";
            }
            if (!CanHoldDirection(vessel))
            {
                return vessel.vesselName + " cannot hold a maneuver direction: it needs a probe core or pilot with SAS level " + ManeuverSasLevel + (vessel.loaded ? " (has " + SasLevel(vessel) + ")" : "");
            }
            string? other = vessel.loaded ? ControlRelease.OtherPilot(vessel) : null;
            return other == null ? null : vessel.vesselName + " is under " + other + " control";
        }

        private static bool ManeuverNodesUnlocked()
        {
            if (HighLogic.CurrentGame?.Mode != Game.Modes.CAREER)
            {
                return true;
            }
            float missionControl = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl);
            float tracking = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation);
            return GameVariables.Instance.UnlockedFlightPlanning(missionControl) && GameVariables.Instance.GetOrbitDisplayMode(tracking) == GameVariables.OrbitDisplayMode.PatchedConics;
        }

        private const int ManeuverSasLevel = 3;

        private static bool FullSas =>
            HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX && HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().EnableFullSASInSandbox;

        private static int SasLevel(Vessel vessel) => Math.Max(vessel.VesselValues.AutopilotSkill.value, vessel.VesselValues.AutopilotSASSkill.value);

        private static bool CanHoldDirection(Vessel vessel)
        {
            if (vessel.loaded)
            {
                return FullSas || SasLevel(vessel) >= ManeuverSasLevel;
            }
            return vessel.protoVessel.protoPartSnapshots.Any(p => p.partInfo?.partPrefab?.FindModuleImplementing<ModuleCommand>() != null)
                && (FullSas || vessel.protoVessel.GetVesselCrew().Any(c => c.experienceTrait.Title == "Pilot") || vessel.protoVessel.protoPartSnapshots.Any(p => p.partInfo?.partPrefab?.FindModuleImplementing<ModuleSAS>() != null));
        }

        private static void LogEstimate(Vessel vessel)
        {
            double ours = DeltaVEstimator.Estimate(VesselTasks.Parts(vessel)).Sum(s => s.DeltaV);
            double? stock = vessel.loaded ? vessel.VesselDeltaV?.TotalDeltaVVac : null;
            PluginFiles.Log("delta-v " + vessel.vesselName + ": estimate " + ours.ToString("0", CultureInfo.InvariantCulture) + " m/s, stock vacuum " + (stock?.ToString("0", CultureInfo.InvariantCulture) ?? "n/a") + " m/s");
        }

        private static BriefingMessage Refused(BriefRequest request, string codename, string error) =>
            new BriefingMessage(request.RequestId, null, codename, request.VesselId, request.GroupId, request.Change, error, new List<BurnPlan>(), 0, 0, null, new List<StageLine>(), new List<ContingencyNote>(), false, error, null, new List<StagePlanLine>(), new List<AutoAction>(), null);

        private static OrbitChange Change(OrbitChangeRequest request, CelestialBody body) => Shape(request, body).WithAutoSystems(request.AutoSystems ?? true);

        private static OrbitChange Shape(OrbitChangeRequest request, CelestialBody body) => request.Kind switch
        {
            ChangeKind.Launch => OrbitChange.Launch(Target(request, body)),
            ChangeKind.Land => OrbitChange.Land(Landing(request.Landing)),
            ChangeKind.Circularize => OrbitChange.Circularize(),
            ChangeKind.ChangeApoapsis => OrbitChange.Apoapsis(request.Altitude ?? 0),
            ChangeKind.ChangePeriapsis => OrbitChange.Periapsis(request.Altitude ?? 0),
            ChangeKind.ChangeInclination => OrbitChange.Inclination(request.Inclination ?? 0),
            ChangeKind.CircularOrbit => OrbitChange.CircularAt(request.Altitude ?? 0),
            ChangeKind.RaiseApoapsis => OrbitChange.RaiseApoapsisBy(request.Altitude ?? 0),
            ChangeKind.SpreadEvenly => OrbitChange.Spread(request.Altitude ?? 0),
            ChangeKind.Transfer => OrbitChange.TransferTo(request.Body!, request.Altitude ?? 0, request.Inclination),
            ChangeKind.Rendezvous => OrbitChange.RendezvousWith(Meet(request)),
            ChangeKind.Dock => OrbitChange.DockWith(Meet(request)),
            ChangeKind.DeployConstellation => OrbitChange.Deploy(request.Altitude ?? 0, request.Inclination is double ring ? new RingPlane(ring, request.Lan ?? 0) : null, End(request.EndOfLife)),
            _ => OrbitChange.StandardLow(),
        };

        private static RendezvousTarget Meet(OrbitChangeRequest request)
        {
            string id = request.TargetVesselId?.ToString(CultureInfo.InvariantCulture) ?? "none";
            return new RendezvousTarget(id, VesselTasks.Find(id)?.vesselName ?? "the target", request.TargetPortId);
        }

        /// <summary>Settles the target port at the briefing, so two docks can share a station; a refusal briefs red.</summary>
        private static OrbitChange PickPort(OrbitChange change, Vessel chaser, List<Contingency> gates)
        {
            RendezvousTarget meet = change.Rendezvous!;
            if (VesselTasks.Find(meet.VesselId) is not Vessel target)
            {
                return change;
            }
            PortChoice choice = DockingPorts.Pick(chaser.vesselName, VesselPorts.Of(chaser), meet.Name, VesselPorts.Of(target), meet.PortId, VesselPorts.Toward(target, chaser));
            if (!choice.Ok)
            {
                gates.Add(new Contingency(Severity.Red, meet.VesselId, choice.Refusal!));
                return change;
            }
            bool rcs = chaser.loaded
                ? Guidance.Steering.HasRcs(chaser)
                : chaser.protoVessel.protoPartSnapshots.Any(p => p.partInfo?.partPrefab?.FindModuleImplementing<ModuleRCS>() != null);
            if (!rcs)
            {
                gates.Add(new Contingency(Severity.Amber, VesselTasks.Id(chaser), "no RCS: the dock stops within 50 m, " + Guidance.DockingController.NoRcs));
            }
            return OrbitChange.DockWith(new RendezvousTarget(meet.VesselId, meet.Name, choice.Target!.Id)).WithAutoSystems(change.AutoSystems);
        }

        private static VesselTask? TargetTask(OrbitChange change) =>
            change.Rendezvous is RendezvousTarget meet && VesselTasks.Find(meet.VesselId) is Vessel target ? VesselTasks.Task(target, null) : null;

        /// <summary>A rendezvous to a port another approved operation holds briefs red and names the holder.</summary>
        private static List<Contingency> Reserved(OrbitChange change)
        {
            var gates = new List<Contingency>();
            if (change.Rendezvous is RendezvousTarget meet && PortReservations.Holder(Runner.Operations, meet) is Operation holder)
            {
                gates.Add(new Contingency(Severity.Red, meet.VesselId, PortReservations.Refusal(holder, meet)));
            }
            return gates;
        }

        /// <summary>No apoapsis means the standard low orbit; no periapsis means a circular orbit.</summary>
        private static LaunchTarget Target(OrbitChangeRequest request, CelestialBody body)
        {
            double apoapsis = request.Altitude ?? StandardOrbit.LowOrbitAltitude(VesselTasks.Body(body));
            return new LaunchTarget(apoapsis, request.Periapsis ?? apoapsis, request.Inclination, request.Lan);
        }

        private static OrbitChangeRequest Wire(OrbitChange change) => change.Kind switch
        {
            OrbitChangeKind.Land => new OrbitChangeRequest(ChangeKind.Land, null, null, null, null, null, null, null, Wire(change.Landing!), change.AutoSystems, null),
            OrbitChangeKind.Circularize => new OrbitChangeRequest(ChangeKind.Circularize, null, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.ChangeApoapsis => new OrbitChangeRequest(ChangeKind.ChangeApoapsis, change.Target, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.ChangePeriapsis => new OrbitChangeRequest(ChangeKind.ChangePeriapsis, change.Target, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.ChangeInclination => new OrbitChangeRequest(ChangeKind.ChangeInclination, null, null, change.Target, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.CircularOrbit => new OrbitChangeRequest(ChangeKind.CircularOrbit, change.Target, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.RaiseApoapsis => new OrbitChangeRequest(ChangeKind.RaiseApoapsis, change.Target, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.SpreadEvenly => new OrbitChangeRequest(ChangeKind.SpreadEvenly, change.Target, null, null, null, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.Launch => new OrbitChangeRequest(ChangeKind.Launch, change.LaunchTarget!.Apoapsis, change.LaunchTarget.Periapsis, change.LaunchTarget.Inclination, change.LaunchTarget.Lan, null, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.Transfer => new OrbitChangeRequest(ChangeKind.Transfer, change.Target, null, change.ArrivalInclination, null, change.Destination, null, null, null, change.AutoSystems, null),
            OrbitChangeKind.Rendezvous => new OrbitChangeRequest(ChangeKind.Rendezvous, null, null, null, null, null, uint.TryParse(change.Rendezvous!.VesselId, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) ? id : (uint?)null, change.Rendezvous.PortId, null, change.AutoSystems, null),
            OrbitChangeKind.Dock => new OrbitChangeRequest(ChangeKind.Dock, null, null, null, null, null, uint.TryParse(change.Rendezvous!.VesselId, NumberStyles.None, CultureInfo.InvariantCulture, out uint dock) ? dock : (uint?)null, change.Rendezvous.PortId, null, change.AutoSystems, null),
            OrbitChangeKind.DeployConstellation => new OrbitChangeRequest(ChangeKind.DeployConstellation, change.Target, null, change.Ring!.IsEquatorial ? null : change.Ring.Inclination, change.Ring.IsEquatorial ? null : change.Ring.AscendingNode, null, null, null, null, change.AutoSystems, Wire(change.EndOfLife)),
            _ => new OrbitChangeRequest(ChangeKind.StandardLowOrbit, null, null, null, null, null, null, null, null, change.AutoSystems, null),
        };

        private static Core.Protocol.EndOfLife? Wire(Core.Planning.EndOfLife end) => end switch
        {
            Core.Planning.EndOfLife.BurnUp => Core.Protocol.EndOfLife.BurnUp,
            Core.Planning.EndOfLife.LandHome => Core.Protocol.EndOfLife.LandHome,
            Core.Planning.EndOfLife.Park => Core.Protocol.EndOfLife.Park,
            _ => null,
        };

        private static Core.Planning.EndOfLife End(Core.Protocol.EndOfLife? end) => end switch
        {
            Core.Protocol.EndOfLife.BurnUp => Core.Planning.EndOfLife.BurnUp,
            Core.Protocol.EndOfLife.LandHome => Core.Planning.EndOfLife.LandHome,
            Core.Protocol.EndOfLife.Park => Core.Planning.EndOfLife.Park,
            _ => Core.Planning.EndOfLife.Auto,
        };

        /// <summary>The pad and the runway take their coordinates from the core; only custom uses the request's.</summary>
        private static Core.Landing.LandingTarget Landing(Core.Protocol.LandingTarget? request) => request == null
            ? Core.Landing.LandingTarget.LaunchPad
            : Core.Landing.LandingTarget.Of((Core.Landing.LandingSiteKind)(int)request.Site, request.Latitude, request.Longitude);

        private static Core.Protocol.LandingTarget Wire(Core.Landing.LandingTarget target) =>
            new Core.Protocol.LandingTarget((Core.Protocol.LandingSite)(int)target.Site, target.Latitude, target.Longitude);

        private static Core.Protocol.LandingPrediction? Wire(Core.Landing.LandingPrediction? p) =>
            p == null ? null : new Core.Protocol.LandingPrediction(p.Latitude, p.Longitude, p.Ut);

        private static DescentStatus? Wire(Core.Landing.DescentReading? d) =>
            d == null ? null : new DescentStatus((DescentPhase)(int)d.Phase, d.Altitude, d.VerticalSpeed, double.IsInfinity(d.Miss) ? (double?)null : d.Miss);

        private static Core.Protocol.FlightAlarm? Wire(Core.Planning.FlightAlarm? a) =>
            a == null ? null : new Core.Protocol.FlightAlarm((Core.Protocol.AlarmKind)(int)a.Kind, a.Body, a.Text);

        private static BurnPlan Wire(PlannedBurn b) =>
            new BurnPlan(Uint(b.VesselId), b.Label, b.NodeUt, b.StartUt, b.Duration, b.Prograde, b.Normal, b.Radial, b.DeltaV, OrbitAfter(b));

        private static Core.Protocol.Orbit? OrbitAfter(PlannedBurn b)
        {
            Core.TwoBody.OrbitElements? o = b.Expected;
            string? body = b.Body ?? VesselTasks.Find(b.VesselId)?.mainBody.bodyName;
            return o == null || body == null ? null : Frame(body, o, b.NodeUt);
        }

        private static Core.Protocol.Orbit Frame(string body, Core.TwoBody.OrbitElements o, double ut) =>
            Core.Telemetry.OrbitFrame.AtTime(body, o.Mu, o.SemiMajorAxis, o.Eccentricity, o.Inclination * UtilMath.Rad2Deg, o.LongitudeOfAscendingNode * UtilMath.Rad2Deg, o.ArgumentOfPeriapsis * UtilMath.Rad2Deg, o.MeanAnomalyAtEpoch, o.Epoch, ut);

        private static OperationStatus Status(Operation op) =>
            new OperationStatus(op.Id, op.Codename, Wire(op.Change), op.Change.Describe(), (OperationState)(int)op.State, op.Burns.Select(Wire).ToList(), MissionRunner.BurnIndex(op), op.Progress?.DeltaVDone ?? 0, Runner.NoteOf(op.Id), Wire(LandingReader.Touchdown(op.Id)), Wire(Runner.AlarmOf(op.Id)), Wire(LandingReader.Descent(op.Id)));

        private static Core.Protocol.Prompt Wire(CorePrompt p) =>
            new Core.Protocol.Prompt(p.Id, Kind(p.Kind), p.OperationId, Uint(p.VesselId), p.Text, p.UntilUt);

        private static Core.Protocol.PromptKind Kind(Core.Execution.PromptKind kind) => kind switch
        {
            Core.Execution.PromptKind.Warp => Core.Protocol.PromptKind.Warp,
            Core.Execution.PromptKind.Fly => Core.Protocol.PromptKind.Fly,
            _ => Core.Protocol.PromptKind.Switch,
        };

        private static uint Uint(string vesselId) => uint.Parse(vesselId, CultureInfo.InvariantCulture);
    }
}
