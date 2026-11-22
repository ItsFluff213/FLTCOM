using System;
using System.Linq;
using FLTCOM.Core.Execution;
using UnityEngine;

namespace FLTCOM.Execution
{
    /// <summary>Notices when the player takes the vessel back (the stock Abort group, held pilot input, or the HALT hotkey, Alt+H) or the vessel spins out of hand.</summary>
    internal static class ControlRelease
    {
        public const string Banner = ">> AUTONOMOUS CONTROL RELEASED <<";

        public static bool HaltPressed() =>
            (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.H);

        /// <summary>
        /// This frame as <see cref="ReleaseRule"/> reads it. Stock input adds trim to pitch, yaw and roll, so trim is
        /// taken off again: a trimmed vessel is not a pilot at the stick. Translation counts only with RCS on, since
        /// without it the keys move nothing.
        /// </summary>
        public static ControlSample Sample(Vessel vessel)
        {
            FlightCtrlState input = FlightInputHandler.state;
            double attitude = Math.Max(Math.Abs(input.pitch - input.pitchTrim), Math.Max(Math.Abs(input.yaw - input.yawTrim), Math.Abs(input.roll - input.rollTrim)));
            double translation = vessel.ActionGroups[KSPActionGroup.RCS] ? Math.Max(Math.Abs(input.X), Math.Max(Math.Abs(input.Y), Math.Abs(input.Z))) : 0;
            return new ControlSample(Time.realtimeSinceStartup, Math.Max(attitude, translation), vessel.ActionGroups[KSPActionGroup.Abort], vessel.angularVelocity.magnitude * Mathf.Rad2Deg);
        }

        public static void Show() => ScreenMessages.PostScreenMessage(Banner, 6f, ScreenMessageStyle.UPPER_CENTER);

        /// <summary>The name of another autopilot mod hooked into the vessel's controls, or null.</summary>
        public static string? OtherPilot(Vessel vessel)
        {
            Delegate?[] hooks = { vessel.OnFlyByWire, vessel.OnAutopilotUpdate, vessel.OnPreAutopilotUpdate, vessel.OnPostAutopilotUpdate };
            string? owner = hooks
                .Where(h => h != null)
                .SelectMany(h => h!.GetInvocationList())
                .Select(d => d.Method.DeclaringType?.Assembly.GetName().Name ?? "")
                .FirstOrDefault(n => n.StartsWith("MechJeb", StringComparison.OrdinalIgnoreCase) || n.StartsWith("kOS", StringComparison.OrdinalIgnoreCase));
            return owner == null ? null : owner.StartsWith("kOS", StringComparison.OrdinalIgnoreCase) ? "kOS" : "MechJeb";
        }
    }
}
