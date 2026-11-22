using System.Collections.Generic;
using System.Globalization;
using FLTCOM.Core.Planning;

namespace FLTCOM.Core.AutoSystems
{
    /// <summary>Where the guided flight is: climbing from the ground, flying a burn in space, coming down, or settled in orbit after a launch.</summary>
    public enum AutoPhase
    {
        Ascent,
        Burn,
        Descent,
        Orbit,
    }

    public enum GearCommand
    {
        None,
        Deploy,
        Retract,
        Brake,
    }

    /// <summary>What the gear rule already did on this flight, so each command fires once.</summary>
    public enum GearState
    {
        Untouched,
        Deployed,
        Retracted,
        Braked,
    }

    public static class GearRule
    {
        public const double DeployHeight = 1000;
        public const double DeployLead = 15;
        public const double RetractHeight = 500;
        public const double RetractClimb = 100;

        /// <summary>
        /// Down when the ground is 15 s or 1000 m away, whichever comes first; brakes at touchdown. Up after a launch above
        /// 500 m, or sooner when climbing faster than 100 m/s.
        /// </summary>
        public static GearCommand Decide(AutoPhase phase, double heightAboveGround, double verticalSpeed, bool landed, GearState state)
        {
            switch (phase)
            {
                case AutoPhase.Descent when landed:
                    return state == GearState.Braked ? GearCommand.None : GearCommand.Brake;
                case AutoPhase.Descent when state == GearState.Untouched && verticalSpeed < 0:
                    return heightAboveGround <= DeployHeight || heightAboveGround / -verticalSpeed <= DeployLead ? GearCommand.Deploy : GearCommand.None;
                case AutoPhase.Ascent when state == GearState.Untouched && !landed:
                    return heightAboveGround > RetractHeight || verticalSpeed > RetractClimb ? GearCommand.Retract : GearCommand.None;
                default:
                    return GearCommand.None;
            }
        }
    }

    public static class FairingRule
    {
        /// <summary>Share of the air's height above which a fairing goes: 63 km on Kerbin.</summary>
        public const double HeightShare = 0.9;

        public static double Height(double atmosphereDepth) => HeightShare * atmosphereDepth;

        /// <summary>Jettison near the top of the air during a launch, before the circularizing burn; at once where there is no air.</summary>
        public static bool ShouldJettison(double atmosphereDepth, double altitude) => atmosphereDepth <= 0 || altitude >= Height(atmosphereDepth);

        /// <summary>
        /// A fairing still shut around its payload: not yet deployed and with panels left. The base part keeps its fairing
        /// module after the panels go, so counting modules alone would jettison and report the same fairing every burn.
        /// </summary>
        /// <param name="deployable">The stock fairing still waits in flight for its deploy.</param>
        /// <param name="sections">The panel sections it still carries.</param>
        public static bool Closed(bool deployable, int sections) => deployable && sections > 0;

        public static string When(double atmosphereDepth) =>
            atmosphereDepth > 0 ? "above " + (Height(atmosphereDepth) / 1000).ToString("0", CultureInfo.InvariantCulture) + " km" : "before the first burn";
    }

    public enum DeployCommand
    {
        Hold,
        Retract,
        Extend,
    }

    /// <summary>Solar panels, antennas and radiators that unfold.</summary>
    public static class DeployableRule
    {
        /// <summary>
        /// Folded for an ascent and a descent through air, unfolded again once a launch leaves the vessel in a stable orbit.
        /// A part that cannot fold back is never touched: unfolded in the air it would break.
        /// </summary>
        public static DeployCommand Decide(AutoPhase phase, bool retractable, bool breakable)
        {
            if (!retractable)
            {
                return DeployCommand.Hold;
            }
            return phase switch
            {
                AutoPhase.Ascent or AutoPhase.Descent => breakable ? DeployCommand.Retract : DeployCommand.Hold,
                AutoPhase.Orbit => DeployCommand.Extend,
                _ => DeployCommand.Hold,
            };
        }

        /// <summary>An orbit is stable once its periapsis clears the air and the terrain.</summary>
        public static bool StableOrbit(double periapsisAltitude, double safeFloor) => periapsisAltitude > safeFloor;
    }

    /// <summary>The parts the automatic systems can act on, counted for the briefing.</summary>
    public sealed class VesselKit
    {
        public VesselKit(int gear, int fairings, int retractable, int oneShot)
        {
            Gear = gear;
            Fairings = fairings;
            Retractable = retractable;
            OneShot = oneShot;
        }

        public int Gear { get; }

        public int Fairings { get; }

        /// <summary>Panels, antennas and radiators that fold away and would break in the air.</summary>
        public int Retractable { get; }

        /// <summary>Panels, antennas and radiators that cannot fold back once open.</summary>
        public int OneShot { get; }
    }

    /// <summary>The automatic actions an operation will take, one briefing line each.</summary>
    public static class AutoPlan
    {
        public static IReadOnlyList<string> Actions(OrbitChangeKind kind, VesselKit kit, double atmosphereDepth, bool on)
        {
            if (!on)
            {
                return new[] { "auto systems off: no gear, fairing or panel actions; staging only as before (ascent, landing, a flamed-out node burn)" };
            }
            var lines = new List<string> { "stage in every burn: drop spent boosters and empty tanks, never parachutes or the probe core" };
            if (kind == OrbitChangeKind.Launch)
            {
                if (kit.Retractable > 0)
                {
                    lines.Add("retract " + Things(kit.Retractable) + " before liftoff, extend them in a stable orbit");
                }
                if (kit.Gear > 0)
                {
                    lines.Add("retract landing gear above " + GearRule.RetractHeight.ToString("0", CultureInfo.InvariantCulture) + " m or when climbing fast");
                }
                if (kit.Fairings > 0)
                {
                    lines.Add("jettison " + Count(kit.Fairings, "fairing") + " " + FairingRule.When(atmosphereDepth));
                }
            }
            if (kind == OrbitChangeKind.Land)
            {
                if (kit.Retractable > 0)
                {
                    lines.Add("retract " + Things(kit.Retractable) + " before the descent");
                }
                if (kit.Gear > 0)
                {
                    lines.Add("lower landing gear " + GearRule.DeployLead.ToString("0", CultureInfo.InvariantCulture) + " s or " + GearRule.DeployHeight.ToString("0", CultureInfo.InvariantCulture) + " m above the ground, brakes on at touchdown");
                }
            }
            if (kit.OneShot > 0 && (kind == OrbitChangeKind.Launch || kind == OrbitChangeKind.Land))
            {
                lines.Add("leave " + Things(kit.OneShot) + " that cannot fold back alone");
            }
            return lines;
        }

        private static string Things(int n) => n + (n == 1 ? " panel, antenna or radiator" : " panels, antennas and radiators");

        private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");
    }
}
