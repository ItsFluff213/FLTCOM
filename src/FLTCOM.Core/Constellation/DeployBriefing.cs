using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;

namespace FLTCOM.Core.Constellation
{
    /// <summary>
    /// Briefs a constellation deployment on one carrier: its entry burns, one release step per payload, each payload's own
    /// circularization under a placeholder id until it is released, then the return or a disposal orbit.
    /// </summary>
    public static class DeployBriefing
    {
        public static Briefing Build(string operationName, OrbitChange change, VesselTask task, double earliestUt, List<Contingency> contingencies)
        {
            if (task.Carrier == null)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "the carrier's parts were not read, so no payload could be found"));
                return Empty(operationName, change, contingencies, null);
            }
            if (task.Orbit == null || task.LaunchSite != null)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, task.VesselName + " must be in orbit around " + task.Body.Name + " to deploy"));
                return Empty(operationName, change, contingencies, null);
            }
            LaunchStagePlan launch = LaunchStagePlan.For(task.VesselName, task.Body, task.Orbit, task.Carrier);
            CarrierScan scan = launch.Stage is not LaunchStage stage ? PayloadDetector.Scan(task.Carrier)
                : launch.ComesHome ? PayloadDetector.Scan(stage.Upper)
                : Attached(PayloadDetector.Scan(stage.Upper), stage);
            double startUt = launch.ComesHome ? earliestUt + ConstellationPlanner.SplitLead : earliestUt;
            DeployPlan plan = ConstellationPlanner.Plan(task.VesselName, task.Body, task.Orbit, scan, change.Target, startUt, change.Ring);
            plan.LaunchStage = launch;
            if (!plan.Ok)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, plan.Error!));
                return Empty(operationName, change, contingencies, plan);
            }

            var burns = new List<PlannedBurn>();
            if (launch.ComesHome)
            {
                burns.Add(new PlannedBurn(task.VesselId, ConstellationPlanner.ReleasePrefix + launch.Name, earliestUt, earliestUt, 0, 0, 0, 0, releasePart: launch.Stage!.CoreId));
            }
            else if (launch.Stage != null)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "the launch stage " + launch.Text));
            }
            double carrierPropellant = scan.Carrier.Propellant;
            double mass = scan.Carrier.Mass + scan.Payloads.Sum(p => p.Mass);
            double used = 0;
            double required = 0;
            var events = plan.CarrierBurns.Select(b => (Ut: b.NodeUt, Burn: (ImpulsiveBurn?)b, Release: (Release?)null))
                .Concat(plan.Releases.Select(r => (Ut: r.ReleaseUt, Burn: (ImpulsiveBurn?)null, Release: (Release?)r)))
                .OrderBy(e => e.Ut)
                .ToList();
            foreach (var e in events)
            {
                if (e.Burn is ImpulsiveBurn b)
                {
                    burns.Add(CarrierBurn(task.VesselId, b, scan.Carrier, mass));
                    double propellant = scan.Carrier.PropellantFor(mass, b.DeltaV);
                    used += propellant;
                    mass -= propellant;
                    required += b.DeltaV;
                }
                else if (e.Release is Release r)
                {
                    string where = plan.Method == DeployMethod.Resonant ? (r.AtApoapsis ? " at apoapsis" : " at periapsis") : " on the circle";
                    burns.Add(new PlannedBurn(task.VesselId, ConstellationPlanner.ReleasePrefix + r.Name + where + ", pass " + r.Pass, r.ReleaseUt, r.ReleaseUt, 0, 0, 0, 0, releasePart: r.Payload.CoreId));
                    mass -= r.Payload.Mass;
                    if (r.Circularize is ImpulsiveBurn own)
                    {
                        double duration = ConstellationPlanner.BurnTime(r.Payload.Own, r.Payload.Mass, own.DeltaV);
                        burns.Add(new PlannedBurn(r.Payload.PlaceholderId, own.Label, own.NodeUt, own.NodeUt - duration / 2, duration, own.Prograde, own.Normal, own.Radial, orbitAfter: r.Final, body: task.Body.Name));
                        Budget(contingencies, r.Payload.PlaceholderId, r.Name, r.Payload.DeltaV, own.DeltaV);
                    }
                }
            }

            double margin = carrierPropellant > 0 && used > 0 ? carrierPropellant / used - 1 : used > 0 ? -1 : double.PositiveInfinity;
            if (used > carrierPropellant)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "the carrier needs " + Briefing.F(used, "0.00") + " t of propellant to place every payload and has " + Briefing.F(carrierPropellant, "0.00") + " t"));
            }
            else if (margin < BriefingBuilder.MarginFloor)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "the carrier keeps only " + Briefing.F(margin * 100, "0") + " % of spare propellant"));
            }

            double left = new Drive(mass, scan.Carrier.DryMass, scan.Carrier.Thrust, scan.Carrier.Isp).DeltaV;
            double lastUt = burns.Max(b => b.EndUt);
            bool lowSide = plan.Method == DeployMethod.CarrierPhasing || plan.Working!.ApoapsisRadius <= task.Body.Radius + plan.Altitude + 1;
            ReturnPlan back = CarrierReturn.Plan(task.Body, task.System, plan.CarrierAfter!, Math.Max(0, left), scan.CarrierHasChutes, task.Body.Radius + plan.Altitude, lowSide, lastUt + ConstellationPlanner.ReturnDelay, change.EndOfLife);
            plan.Return = back;
            foreach (ImpulsiveBurn b in back.Burns)
            {
                burns.Add(CarrierBurn(task.VesselId, b, scan.Carrier, mass));
                mass -= scan.Carrier.PropellantFor(mass, b.DeltaV);
            }
            double stepUt = burns.Max(b => b.EndUt) + ConstellationPlanner.ReturnDelay;
            burns.Add(new PlannedBurn(task.VesselId, back.GoesHome ? ConstellationPlanner.ReturnLabel : ConstellationPlanner.SpacingLabel, stepUt, stepUt, 0, 0, 0, 0));
            if (back.Fallback)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, back.Text));
            }
            if (plan.MaxSpacingError > ConstellationPlanner.SpacingTolerance)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "spacing off by up to " + Briefing.F(Angles(plan.MaxSpacingError), "0.0") + " deg; a spread-evenly trim follows"));
            }
            double payloadMargin = plan.Releases.Where(r => r.Circularize != null).Select(r => r.Payload.DeltaV / r.CircularizeDeltaV - 1).DefaultIfEmpty(double.PositiveInfinity).Min();
            return new Briefing(
                operationName,
                change.Describe(),
                burns.OrderBy(b => b.StartUt).ToList(),
                required + back.DeltaV,
                new Drive(scan.Carrier.Mass + scan.Payloads.Sum(p => p.Mass), scan.Carrier.DryMass + scan.Payloads.Sum(p => p.Mass), scan.Carrier.Thrust, scan.Carrier.Isp).DeltaV,
                Math.Min(margin, payloadMargin),
                Array.Empty<BriefedStage>(),
                contingencies,
                deployment: plan);
        }

        private static PlannedBurn CarrierBurn(string vesselId, ImpulsiveBurn b, Drive carrier, double mass)
        {
            double duration = ConstellationPlanner.BurnTime(carrier, mass, b.DeltaV);
            return new PlannedBurn(vesselId, b.Label, b.NodeUt, b.NodeUt - duration / 2, duration, b.Prograde, b.Normal, b.Radial, expected: b.OrbitAfter);
        }

        /// <summary>A launch stage that cannot come home rides along as dead weight on the transfer stage.</summary>
        private static CarrierScan Attached(CarrierScan upper, LaunchStage stage) =>
            new CarrierScan(
                upper.Payloads,
                upper.Uncontrolled,
                new Drive(upper.Carrier.Mass + stage.Own.Mass, upper.Carrier.DryMass + stage.Own.Mass, upper.Carrier.Thrust, upper.Carrier.Isp),
                upper.CarrierHasChutes || stage.Parts.Any(p => p.Is(CarrierPartKind.Parachute)),
                upper.FairingIds);

        private static void Budget(List<Contingency> contingencies, string vesselId, string name, double has, double needs)
        {
            if (has < needs)
            {
                contingencies.Add(new Contingency(Severity.Red, vesselId, name + " has " + Briefing.F(has, "0") + " m/s of its own and needs " + Briefing.F(needs, "0") + " m/s to circularize"));
            }
            else if (has < needs * (1 + BriefingBuilder.MarginFloor))
            {
                contingencies.Add(new Contingency(Severity.Amber, vesselId, name + " keeps only " + Briefing.F(has - needs, "0") + " m/s after circularizing"));
            }
        }

        private static double Angles(double radians) => TwoBody.Angles.ToDegrees(radians);

        private static Briefing Empty(string operationName, OrbitChange change, List<Contingency> contingencies, DeployPlan? plan) =>
            new Briefing(operationName, change.Describe(), Array.Empty<PlannedBurn>(), 0, 0, double.PositiveInfinity, Array.Empty<BriefedStage>(), contingencies, deployment: plan);
    }
}
