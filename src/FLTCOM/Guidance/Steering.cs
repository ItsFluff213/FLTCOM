using System.Linq;
using UnityEngine;

namespace FLTCOM.Guidance
{
    /// <summary>Attitude, thrust and RCS helpers shared by the guidance modes.</summary>
    internal static class Steering
    {
        /// <summary>Stock SAS in stability assist holds whatever rotation it is locked to.</summary>
        public static void Hold(Vessel vessel, Vector3d direction)
        {
            HoldRotation(vessel, Rotation(vessel, direction));
        }

        public static void HoldRotation(Vessel vessel, Quaternion rotation)
        {
            if (!vessel.ActionGroups[KSPActionGroup.SAS])
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                vessel.Autopilot.Enable();
            }
            if (vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist)
            {
                vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
            vessel.Autopilot.SAS.LockRotation(rotation);
        }

        /// <summary>
        /// The vessel rotation that points its nose along <paramref name="direction"/> by the shortest turn from where it
        /// points now, so the roll turns with the nose. Projecting the current roll axis onto the new heading instead
        /// asks for a half turn of roll as soon as the heading is more than 90 degrees away, and flips it back as the
        /// nose comes round.
        /// </summary>
        public static Quaternion Rotation(Vessel vessel, Vector3d direction)
        {
            Quaternion now = vessel.ReferenceTransform.rotation;
            return Quaternion.FromToRotation(vessel.ReferenceTransform.up, (Vector3)direction) * now;
        }

        /// <summary>Degrees between the vessel's nose and <paramref name="direction"/>.</summary>
        public static double OffAxis(Vessel vessel, Vector3d direction) => Vector3d.Angle(vessel.ReferenceTransform.up, direction);

        public static double MaxAcceleration(Vessel vessel)
        {
            double thrust = vessel.parts
                .SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                .Where(e => e.EngineIgnited && e.isOperational && !e.flameout)
                .Sum(e => (double)e.maxThrust * e.thrustPercentage / 100);
            double mass = vessel.totalMass;
            return mass > 0 ? thrust / mass : 0;
        }

        /// <summary>The vessel has an enabled RCS thruster with propellant to burn.</summary>
        public static bool HasRcs(Vessel vessel) =>
            vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleRCS>()).Any(r => r.rcsEnabled && r.moduleIsEnabled && r.propellants.All(p => Available(vessel, p.id) > 0));

        private static double Available(Vessel vessel, int resource)
        {
            vessel.GetConnectedResourceTotals(resource, out double amount, out _);
            return amount;
        }

        /// <summary>
        /// Translation command toward <paramref name="wanted"/>, a world-frame velocity change; <paramref name="gain"/> is
        /// full thrust per m/s. The axis mapping (y and z swapped into the stock controls) is ported from MechJeb2,
        /// https://github.com/MuMech/MechJeb2, MechJeb2/MechJebModuleThrustController.cs; Copyright the MechJeb
        /// contributors; MechJeb2 is licensed GPL-3.0, as is FLTCOM.
        /// </summary>
        public static Vector3 Translation(Vessel vessel, Vector3d wanted, double gain)
        {
            Vector3d local = Quaternion.Inverse(vessel.ReferenceTransform.rotation) * (Vector3)wanted;
            return new Vector3(
                Mathf.Clamp((float)(local.x * gain), -1, 1),
                Mathf.Clamp((float)(local.z * gain), -1, 1),
                Mathf.Clamp((float)(local.y * gain), -1, 1));
        }

        public static void Rcs(Vessel vessel, bool on)
        {
            if (vessel.ActionGroups[KSPActionGroup.RCS] != on)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, on);
            }
        }
    }
}
