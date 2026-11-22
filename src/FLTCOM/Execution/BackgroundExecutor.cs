using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.TwoBody;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Execution
{
    /// <summary>
    /// Flies a group burn of a vessel nobody is flying: each frame it integrates the burn up to now with the saved
    /// stage's thrust, writes the new orbit to the vessel and drains the burned propellant from its saved parts.
    /// With AUTO SYSTEMS on, a dry stage hands over to the next one that has engines.
    /// </summary>
    internal sealed class BackgroundExecutor
    {
        private string? _operation;
        private int _burn = -1;
        private CoreVector _direction;
        private double _lastUt;
        private int _stage = int.MaxValue;
        private string? _toldScope;

        public void Tick(Operation op, int burnIndex, PlannedBurn burn)
        {
            if (op.State != StepState.Executing)
            {
                Reset();
                return;
            }
            Vessel? vessel = VesselTasks.Find(burn.VesselId);
            if (vessel == null)
            {
                Stop(op, "vessel " + burn.VesselId + " is gone");
                return;
            }
            if (vessel.loaded)
            {
                Stop(op, vessel.vesselName + " is within physics range of the active vessel: it cannot burn on rails; switch to it or fly away");
                return;
            }
            double now = Planetarium.GetUniversalTime();
            if (_operation != op.Id || _burn != burnIndex)
            {
                _operation = op.Id;
                _burn = burnIndex;
                _direction = BackgroundBurn.Direction(VesselTasks.Orbit(vessel.orbit), burn);
                _lastUt = now;
                _stage = int.MaxValue;
                PluginFiles.Log("background burn " + op.Codename + " #" + burnIndex + " on " + vessel.vesselName + ": " + burn.Label + ", " + Text(burn.DeltaV) + " m/s");
                if (op.Change.AutoSystems && _toldScope != op.Id)
                {
                    _toldScope = op.Id;
                    PluginFiles.Log(op.Codename + ": " + BackgroundStaging.Scope);
                    MissionDesk.Runner.Note(op.Id, BackgroundStaging.Scope);
                }
            }
            double done = op.Progress?.DeltaVDone ?? 0;
            BurnFrame frame = BackgroundBurn.Frame(VesselTasks.Parts(vessel), VesselTasks.StageParts(vessel), op.Change.AutoSystems, VesselTasks.Orbit(vessel.orbit), _direction, _lastUt, now, burn.DeltaV - done);
            if (frame.Stop != null)
            {
                Stop(op, vessel.vesselName + ": " + frame.Stop + " after " + Text(done) + " of " + Text(burn.DeltaV) + " m/s");
                return;
            }
            Thruster thruster = frame.Thruster!;
            if (frame.Staged && thruster.Stage != _stage)
            {
                string note = vessel.vesselName + " staged on rails into stage " + thruster.Stage + " after " + Text(done) + " m/s; the spent stage stays attached until you stage it in flight";
                PluginFiles.Log("background burn " + op.Codename + " #" + burnIndex + ": " + note);
                MissionDesk.Runner.Note(op.Id, note);
            }
            _stage = thruster.Stage;
            BurnStep step = frame.Step!;
            if (step.DeltaV > 0)
            {
                Apply(vessel, step.Orbit);
                Drain(vessel, frame.Withdrawals);
            }
            _lastUt = step.Ut;
            MissionDesk.Runner.Progress(op.Id, done + step.DeltaV);
            if (step.Done)
            {
                global::Orbit o = vessel.orbit;
                PluginFiles.Log("background burn " + op.Codename + " #" + burnIndex + " done: pe=" + o.PeA.ToString("0", CultureInfo.InvariantCulture) + " ap=" + o.ApA.ToString("0", CultureInfo.InvariantCulture));
                MissionDesk.Runner.BurnDone(op.Id, vessel.vesselName + ": \"" + burn.Label + "\" flown on rails");
                Reset();
            }
        }

        public void Reset()
        {
            _operation = null;
            _burn = -1;
        }

        private void Stop(Operation op, string why)
        {
            PluginFiles.Log("background burn stopped: " + why);
            WarpDriver.Stop();
            MissionDesk.Runner.Interrupt(op.Id, why);
            MissionDesk.Runner.Hold(op.Id, why);
            Reset();
        }

        private static void Apply(Vessel vessel, OrbitElements o)
        {
            vessel.orbit.SetOrbit(
                o.Inclination * UtilMath.Rad2Deg,
                o.Eccentricity,
                o.SemiMajorAxis,
                o.LongitudeOfAscendingNode * UtilMath.Rad2Deg,
                o.ArgumentOfPeriapsis * UtilMath.Rad2Deg,
                o.MeanAnomalyAtEpoch,
                o.Epoch,
                vessel.mainBody);
            vessel.orbit.Init();
            vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime());
            vessel.orbitDriver.updateFromParameters();
            vessel.protoVessel.orbitSnapShot = new OrbitSnapshot(vessel.orbit);
            RefreshPlan(vessel);
        }

        /// <summary>Rebuilds the vessel's patched-conic flight plan so the stock Maneuver Planner reads the new orbit, not the one before the burn.</summary>
        private static void RefreshPlan(Vessel vessel)
        {
            PatchedConicSolver? solver = vessel.patchedConicSolver;
            if (solver == null)
            {
                return;
            }
            try
            {
                solver.UpdateFlightPlan();
            }
            catch (Exception e)
            {
                PluginFiles.Log("flight plan refresh failed on " + vessel.vesselName + ": " + e.Message);
            }
        }

        /// <summary>Takes each withdrawal from its saved part; the indices follow <see cref="VesselTasks.Parts"/>, which reads the saved parts in order.</summary>
        private static void Drain(Vessel vessel, IReadOnlyList<Withdrawal> withdrawals)
        {
            List<ProtoPartSnapshot> parts = vessel.protoVessel.protoPartSnapshots;
            foreach (Withdrawal w in withdrawals)
            {
                foreach (ProtoPartResourceSnapshot tank in parts[w.Part].resources.Where(r => r.resourceName == w.Resource))
                {
                    tank.amount = Math.Max(0, tank.amount - w.Units);
                }
            }
        }

        private static string Text(double metresPerSecond) => metresPerSecond.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
