using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Constellation;
using FLTCOM.Core.Groups;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Guidance;

namespace FLTCOM.Execution
{
    /// <summary>
    /// A release step: opens the carrier's fairings, fires the outermost separator above the payload's command part, then
    /// waits for the payload to become a vessel of its own, names it and wakes its engines.
    /// </summary>
    internal sealed class PayloadRelease : IGuidance
    {
        private const double FairingWait = 3;
        private const double Timeout = 20;

        private readonly uint _core;
        private readonly string _name;
        private double _waitUntil = double.NegativeInfinity;
        private double _releasedAt = -1;
        private bool _started;

        public PayloadRelease(Vessel carrier, uint core, string name)
        {
            Vessel = carrier;
            _core = core;
            _name = name;
        }

        public Vessel Vessel { get; }

        public double DeltaVDone => 0;

        public double IdleUntil => double.NegativeInfinity;

        public string Note { get; private set; } = string.Empty;

        /// <summary>The payload once it flies on its own.</summary>
        public Vessel? Released { get; private set; }

        public GuidanceStatus Tick()
        {
            double now = Planetarium.GetUniversalTime();
            if (!_started)
            {
                _started = true;
                var fairings = Vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleProceduralFairing>()).Where(f => FairingRule.Closed(f.CanMove, f.xSections.Count)).ToList();
                foreach (ModuleProceduralFairing fairing in fairings)
                {
                    fairing.DeployFairing();
                }
                if (fairings.Count > 0)
                {
                    _waitUntil = now + FairingWait;
                }
            }
            if (now < _waitUntil)
            {
                return GuidanceStatus.Running;
            }
            if (_releasedAt < 0)
            {
                Part? core = Vessel.parts.FirstOrDefault(p => p.persistentId == _core);
                if (core == null)
                {
                    return Found(Holding()) ? GuidanceStatus.Done : Fail("the payload's command part is no longer on " + Vessel.vesselName);
                }
                Part? separator = Separator(core);
                if (separator == null)
                {
                    return Fail("nothing separates " + _name + " from " + Vessel.vesselName);
                }
                Separate(separator);
                _releasedAt = now;
                return GuidanceStatus.Running;
            }
            if (Found(Holding()))
            {
                return GuidanceStatus.Done;
            }
            return now - _releasedAt > Timeout ? Fail(_name + " did not come free of " + Vessel.vesselName) : GuidanceStatus.Running;
        }

        public void Stop()
        {
        }

        private Vessel? Holding() =>
            FlightGlobals.Vessels.FirstOrDefault(v => v != null && v != Vessel && v.loaded && v.parts.Any(p => p.persistentId == _core));

        private bool Found(Vessel? payload)
        {
            if (payload == null)
            {
                return false;
            }
            payload.vesselName = _name;
            foreach (ModuleEngines engine in payload.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Where(e => !e.EngineIgnited))
            {
                engine.Activate();
            }
            Released = payload;
            Note = "released " + _name;
            return true;
        }

        private GuidanceStatus Fail(string why)
        {
            Note = why;
            return GuidanceStatus.Failed;
        }

        /// <summary>The separating part nearest the root on the way up from the core: the one payload detection split at.</summary>
        private static Part? Separator(Part core)
        {
            Part? found = null;
            for (Part? p = core.parent; p != null; p = p.parent)
            {
                if (Releases(p))
                {
                    found = p;
                }
            }
            return found;
        }

        private static bool Releases(Part p) =>
            p.FindModulesImplementing<ModuleDecouple>().Any(d => !d.isDecoupled)
            || p.FindModulesImplementing<ModuleAnchoredDecoupler>().Any(d => !d.isDecoupled)
            || p.FindModuleImplementing<ModuleDockingNode>() != null;

        private static void Separate(Part p)
        {
            if (p.FindModulesImplementing<ModuleDecouple>().FirstOrDefault(d => !d.isDecoupled) is ModuleDecouple decouple)
            {
                decouple.Decouple();
            }
            else if (p.FindModulesImplementing<ModuleAnchoredDecoupler>().FirstOrDefault(d => !d.isDecoupled) is ModuleAnchoredDecoupler anchored)
            {
                anchored.Decouple();
            }
            else if (p.FindModuleImplementing<ModuleDockingNode>() is ModuleDockingNode port)
            {
                if (port.otherNode != null && port.vesselInfo != null)
                {
                    port.Undock();
                }
                else
                {
                    port.Decouple();
                }
            }
        }
    }

    /// <summary>
    /// The step after the last release: measures the constellation's spacing and, past the tolerance, sets up a spread-evenly
    /// trim; then, when it flies home, plans the carrier's way home from where it really is, and last the launch stage's
    /// landing, queued after both. All are briefed and approved here, under the operator's approval of the deployment,
    /// once the deployment is done.
    /// </summary>
    internal sealed class ReturnHome : IGuidance
    {
        private readonly Operation _op;

        public ReturnHome(Vessel carrier, Operation op)
        {
            Vessel = carrier;
            _op = op;
        }

        public Vessel Vessel { get; }

        public double DeltaVDone => 0;

        public double IdleUntil => double.NegativeInfinity;

        public string Note { get; private set; } = "payloads out";

        public GuidanceStatus Tick() => GuidanceStatus.Done;

        public void Stop()
        {
        }

        /// <summary>Called once the deployment is complete, so the carrier is free for its next operation.</summary>
        public string Follow()
        {
            var notes = new List<string>();
            double after = Planetarium.GetUniversalTime();
            var cores = new HashSet<uint>(_op.Burns.Where(ConstellationPlanner.IsSatelliteRelease).Select(b => b.ReleasePart!.Value));
            var satellites = cores.Select(Holder).Where(v => v != null && v != Vessel).Select(v => v!).Distinct().ToList();
            if (satellites.Count >= 2)
            {
                var orbits = satellites.Select(v => VesselTasks.Orbit(v.orbit)).ToList();
                double error = new[] { 1, -1 }.Min(d => ConstellationPlanner.SpacingErrors(orbits, after, d).Max(e => System.Math.Abs(e)));
                string spacing = "spacing within " + (error * 180 / System.Math.PI).ToString("0.0", CultureInfo.InvariantCulture) + " deg";
                if (error > ConstellationPlanner.SpacingTolerance)
                {
                    string? made = MissionDesk.Groups.Create(Vessel.vesselName + " constellation", satellites.Select(VesselTasks.Id), out FleetGroup? group);
                    string? trim = made ?? MissionDesk.Chain(_op.Codename + " trim", OrbitChange.Spread(_op.Change.Target), satellites.Select(v => VesselTasks.Task(v, null)).ToList(), group!.Id, after, out after);
                    spacing += trim == null ? ", spread-evenly trim approved" : ", trim not flown: " + trim;
                }
                notes.Add(spacing);
            }
            if (_op.Burns.Any(b => b.Label == ConstellationPlanner.ReturnLabel))
            {
                CelestialBody home = FlightGlobals.GetHomeBody();
                OrbitChange change = Vessel.mainBody == home
                    ? OrbitChange.Land(Core.Landing.LandingTarget.LaunchPad)
                    : OrbitChange.HomeTo(home.bodyName, StandardOrbit.LowOrbitAltitude(VesselTasks.Body(home)));
                string? back = MissionDesk.Chain(_op.Codename + " home", change, new[] { VesselTasks.Task(Vessel, null, change) }, VesselTasks.Id(Vessel), after, out after);
                notes.Add(back == null ? "carrier return approved: " + change.Describe() : "carrier return not flown: " + back + "; brief it by hand");
            }
            if (_op.Burns.FirstOrDefault(b => ConstellationPlanner.IsRelease(b) && !ConstellationPlanner.IsSatelliteRelease(b)) is PlannedBurn split
                && Holder(split.ReleasePart!.Value) is Vessel stage && stage != Vessel)
            {
                OrbitChange land = OrbitChange.Land(Core.Landing.LandingTarget.LaunchPad);
                string? down = MissionDesk.Chain(stage.vesselName, land, new[] { VesselTasks.Task(stage, null, land) }, VesselTasks.Id(stage), after, out _);
                notes.Add(down == null ? "launch stage return approved" : "launch stage return not flown: " + down + "; brief it by hand");
            }
            return string.Join("; ", notes);
        }

        private static Vessel? Holder(uint core) =>
            FlightGlobals.Vessels.FirstOrDefault(v => v != null && (v.loaded ? v.parts.Any(p => p.persistentId == core) : v.protoVessel.protoPartSnapshots.Any(p => p.persistentId == core)));
    }
}
