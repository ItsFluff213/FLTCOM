using FLTCOM.Core.Execution;

namespace FLTCOM.Execution
{
    /// <summary>Time warp: the warp the operator said yes to, and the player's own warp while guidance idles.</summary>
    internal static class WarpDriver
    {
        public static void WarpTo(double ut)
        {
            if (ut > Planetarium.GetUniversalTime())
            {
                TimeWarp.fetch.WarpTo(ut);
            }
        }

        /// <summary>
        /// Leaves the player's own time warp alone while guidance idles until <paramref name="idleUntil"/>, stepping it down so
        /// the lead before the next ignition is flown at real time; with no idle stretch ahead, drops to real time.
        /// </summary>
        public static void Allow(double idleUntil)
        {
            double left = idleUntil - MissionRunner.WarpLead - Planetarium.GetUniversalTime();
            int index = TimeWarp.CurrentRateIndex;
            if (left <= 0)
            {
                Stop();
            }
            else if (index > 0 && TimeWarp.CurrentRate * 2 > left)
            {
                TimeWarp.SetRate(index - 1, true);
            }
        }

        /// <summary>Drops to real time before a burn.</summary>
        public static void Stop()
        {
            if (TimeWarp.CurrentRateIndex != 0)
            {
                TimeWarp.SetRate(0, true);
            }
        }
    }
}
