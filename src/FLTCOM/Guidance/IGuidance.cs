namespace FLTCOM.Guidance
{
    public enum GuidanceStatus
    {
        Running,
        Done,
        Failed,
    }

    /// <summary>Flies the active vessel through its fly-by-wire hook until the job is done.</summary>
    public interface IGuidance
    {
        Vessel Vessel { get; }

        /// <summary>Delta-v flown so far, in m/s.</summary>
        double DeltaVDone { get; }

        /// <summary>
        /// Until when the engines stay off with nothing to steer, so the player may time-warp: past for guidance that is
        /// burning or about to, infinite when only a change of state (not a clock) ends the quiet stretch.
        /// </summary>
        double IdleUntil { get; }

        /// <summary>Why the guidance stopped, once it is Done or Failed.</summary>
        string Note { get; }

        /// <summary>Runs once per physics frame.</summary>
        GuidanceStatus Tick();

        /// <summary>Closes the throttle and lets go of the vessel.</summary>
        void Stop();
    }
}
