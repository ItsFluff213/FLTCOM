namespace FLTCOM.Core.Landing
{
    /// <summary>Where a descent stands now, for the display: the guide's phase, height over the ground, sink rate and predicted miss.</summary>
    public sealed class DescentReading
    {
        public DescentReading(LandingPhase phase, double altitude, double verticalSpeed, double miss)
        {
            Phase = phase;
            Altitude = altitude;
            VerticalSpeed = verticalSpeed;
            Miss = miss;
        }

        public LandingPhase Phase { get; }

        /// <summary>Metres above the ground under the vessel.</summary>
        public double Altitude { get; }

        /// <summary>m/s, negative while coming down.</summary>
        public double VerticalSpeed { get; }

        /// <summary>Metres from the predicted touchdown to the target; infinity before the first prediction lands.</summary>
        public double Miss { get; }
    }
}
