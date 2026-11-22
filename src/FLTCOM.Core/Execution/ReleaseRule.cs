using System;
using System.Globalization;

namespace FLTCOM.Core.Execution
{
    /// <summary>What the pilot's controls and the vessel showed in one physics frame of a guided burn.</summary>
    public readonly struct ControlSample
    {
        /// <param name="time">Real seconds, so a held key is timed the same at any warp.</param>
        /// <param name="input">The largest attitude or translation deflection away from trim, 0 to 1.</param>
        /// <param name="spin">Degrees per second.</param>
        public ControlSample(double time, double input, bool abort, double spin)
        {
            Time = time;
            Input = input;
            Abort = abort;
            Spin = spin;
        }

        public double Time { get; }

        public double Input { get; }

        public bool Abort { get; }

        public double Spin { get; }
    }

    /// <summary>
    /// Decides when the pilot takes a guided vessel back. The Abort group lets go at once; stick or key input lets go
    /// only when held for <see cref="HoldSeconds"/>, so a tap, a mistyped camera or map key, or one frame of joystick
    /// noise never ends a burn; a spin lets go only when it lasts <see cref="SpinHoldSeconds"/>, so one physics frame
    /// that reads a wild rate never leaves a descending lander with nobody flying it.
    /// </summary>
    public sealed class ReleaseRule
    {
        public const double Deadband = 0.05;

        /// <summary>
        /// A key tap lasts about a tenth of a second and an accidental press well under half; a pilot who means to
        /// steer holds the key for seconds, since a turn takes that long. One second separates the two, and SAS undoes
        /// a second of stray input.
        /// </summary>
        public const double HoldSeconds = 1;

        /// <summary>Degrees per second. Well above any slew SAS flies on purpose, well below a tumble.</summary>
        public const double SpinLimit = 60;

        public const double SpinHoldSeconds = 0.5;

        private double? _heldSince;
        private double? _spinningSince;

        /// <summary>The reason to let go, or null to keep flying. Feed every frame of one burn in order.</summary>
        public string? Why(ControlSample sample)
        {
            if (sample.Abort)
            {
                return "abort";
            }
            _spinningSince = sample.Spin > SpinLimit ? _spinningSince ?? sample.Time : (double?)null;
            if (sample.Time - _spinningSince >= SpinHoldSeconds)
            {
                return "the vessel is spinning at " + sample.Spin.ToString("0", CultureInfo.InvariantCulture) + " deg/s; FLTCOM cut the throttle and let go";
            }
            if (Math.Abs(sample.Input) <= Deadband)
            {
                _heldSince = null;
                return null;
            }
            _heldSince ??= sample.Time;
            return sample.Time - _heldSince.Value >= HoldSeconds ? "pilot input held for " + HoldSeconds.ToString("0", CultureInfo.InvariantCulture) + " s" : null;
        }
    }
}
