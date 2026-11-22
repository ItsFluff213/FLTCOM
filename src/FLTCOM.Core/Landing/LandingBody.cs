using System;
using System.Collections.Generic;
using System.Globalization;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    /// <summary>Air density by altitude, sampled at a fixed step from sea level to the top of the atmosphere. kg/m^3 and metres.</summary>
    public sealed class Atmosphere
    {
        private readonly double[] _density;

        public Atmosphere(double height, IReadOnlyList<double> density)
        {
            if (density.Count < 2)
            {
                throw new ArgumentException("an atmosphere needs at least two density samples", nameof(density));
            }
            Height = height;
            _density = new double[density.Count];
            for (int i = 0; i < density.Count; i++)
            {
                _density[i] = Math.Max(density[i], 1e-12);
            }
            Step = height / (density.Count - 1);
        }

        public double Height { get; }

        public double Step { get; }

        public double SeaLevelDensity => _density[0];

        /// <summary>Log-linear between samples; zero at and above the top.</summary>
        public double Density(double altitude)
        {
            if (altitude >= Height)
            {
                return 0;
            }
            double x = Math.Max(0, altitude) / Step;
            int i = Math.Min((int)x, _density.Length - 2);
            double f = x - i;
            return Math.Exp(Math.Log(_density[i]) * (1 - f) + Math.Log(_density[i + 1]) * f);
        }

        public static Atmosphere Exponential(double seaLevelDensity, double scaleHeight, double height, double step = 1000)
        {
            int n = (int)Math.Ceiling(height / step) + 1;
            var samples = new double[n];
            for (int i = 0; i < n; i++)
            {
                samples[i] = seaLevelDensity * Math.Exp(-i * height / (n - 1) / scaleHeight);
            }
            return new Atmosphere(height, samples);
        }
    }

    /// <summary>
    /// A body as the landing needs it: the planner's central body plus its spin and air. The inertial frame is the
    /// core's (Z along the spin axis); <paramref name="meridian"/> is the celestial longitude of longitude 0 at <paramref name="ut"/>.
    /// </summary>
    public sealed class LandingBody
    {
        public LandingBody(CentralBody body, double rotationPeriod, double meridian, double ut, Atmosphere? atmosphere)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            RotationPeriod = rotationPeriod;
            Meridian = meridian;
            Ut = ut;
            Atmosphere = atmosphere;
        }

        public CentralBody Body { get; }

        public double RotationPeriod { get; }

        public double Meridian { get; }

        public double Ut { get; }

        public Atmosphere? Atmosphere { get; }

        public double Radius => Body.Radius;

        public double Mu => Body.GravParameter;

        public double AtmosphereHeight => Atmosphere?.Height ?? 0;

        public double SurfaceGravity => Mu / (Radius * Radius);

        /// <summary>Signed spin rate in rad/s, the same convention as <see cref="LaunchSite.RotationRate"/>.</summary>
        public double RotationRate => Angles.TwoPi / RotationPeriod;

        public Vector3d Spin => new Vector3d(0, 0, RotationRate);

        public double MeridianAt(double ut) => Meridian + RotationRate * (ut - Ut);

        public Vector3d Surface(double latitude, double longitude, double altitude, double ut)
        {
            double r = Radius + altitude;
            double lon = longitude + MeridianAt(ut);
            return new Vector3d(r * Math.Cos(latitude) * Math.Cos(lon), r * Math.Cos(latitude) * Math.Sin(lon), r * Math.Sin(latitude));
        }

        public double Latitude(Vector3d position) => Math.Asin(Math.Max(-1, Math.Min(1, position.Z / position.Magnitude)));

        public double Longitude(Vector3d position, double ut)
        {
            double lon = Math.Atan2(position.Y, position.X) - MeridianAt(ut);
            return Angles.Wrap(lon + Math.PI) - Math.PI;
        }

        /// <summary>Velocity relative to the ground (and the air, which turns with it).</summary>
        public Vector3d AirVelocity(Vector3d position, Vector3d velocity) => velocity - Vector3d.Cross(Spin, position);

        public double Density(double altitude) => Atmosphere?.Density(altitude) ?? 0;
    }

    public enum LandingSiteKind
    {
        LaunchPad,
        Runway,
        Custom,
    }

    /// <summary>Where to land. Angles in radians, longitude east-positive. The pad and the runway are on the home body.</summary>
    public sealed class LandingTarget
    {
        public LandingTarget(LandingSiteKind site, double latitude, double longitude)
        {
            Site = site;
            Latitude = latitude;
            Longitude = longitude;
        }

        public LandingSiteKind Site { get; }

        public double Latitude { get; }

        public double Longitude { get; }

        /// <summary>KSC launch pad, the stock position MechJeb ships in LandingSites.cfg.</summary>
        public static LandingTarget LaunchPad { get; } = new LandingTarget(LandingSiteKind.LaunchPad, Angles.ToRadians(-0.09694444), Angles.ToRadians(-74.5575));

        /// <summary>Middle of the KSC runway, between the two threshold points in MechJeb's LandingSites.cfg.</summary>
        public static LandingTarget Runway { get; } = new LandingTarget(LandingSiteKind.Runway, Angles.ToRadians(-0.0493916), Angles.ToRadians(-74.60864));

        public static LandingTarget At(double latitude, double longitude) => new LandingTarget(LandingSiteKind.Custom, latitude, longitude);

        public static LandingTarget Of(LandingSiteKind site, double latitude, double longitude) => site switch
        {
            LandingSiteKind.LaunchPad => LaunchPad,
            LandingSiteKind.Runway => Runway,
            _ => At(latitude, longitude),
        };

        public string Describe() => Site switch
        {
            LandingSiteKind.LaunchPad => "land at the launch pad",
            LandingSiteKind.Runway => "land on the runway",
            _ => "land at " + Coordinates(Latitude, Longitude),
        };

        public static string Coordinates(double latitude, double longitude) =>
            Math.Abs(Angles.ToDegrees(latitude)).ToString("0.00", CultureInfo.InvariantCulture) + (latitude < 0 ? " S " : " N ")
            + Math.Abs(Angles.ToDegrees(longitude)).ToString("0.00", CultureInfo.InvariantCulture) + (longitude < 0 ? " W" : " E");
    }

    /// <summary>
    /// The vessel as the descent sees it. SI units: kg, N, m^2. Drag areas are drag coefficient times area, so the drag
    /// force is 0.5 * density * airspeed^2 * area. ChuteArea is what the parachutes add once fully open.
    /// </summary>
    public sealed class LandingCraft
    {
        public LandingCraft(double mass, double dragArea, double maxThrust, double chuteArea = 0, double chuteDeployAltitude = 1000, double exhaustVelocity = 0)
        {
            Mass = mass;
            DragArea = dragArea;
            MaxThrust = maxThrust;
            ChuteArea = chuteArea;
            ChuteDeployAltitude = chuteDeployAltitude;
            ExhaustVelocity = exhaustVelocity;
        }

        public double Mass { get; }

        public double DragArea { get; }

        public double MaxThrust { get; }

        public double ChuteArea { get; }

        /// <summary>Height above the ground where the armed parachutes open fully.</summary>
        public double ChuteDeployAltitude { get; }

        public bool HasChutes => ChuteArea > 0;

        public double MaxAccel => Mass > 0 ? MaxThrust / Mass : 0;

        /// <summary>Engine exhaust speed in m/s, for the mass a burn leaves; zero when unknown.</summary>
        public double ExhaustVelocity { get; }

        public LandingCraft AfterBurn(double deltaV) =>
            ExhaustVelocity > 0 ? new LandingCraft(Mass * Math.Exp(-deltaV / ExhaustVelocity), DragArea, MaxThrust, ChuteArea, ChuteDeployAltitude, ExhaustVelocity) : this;
    }

    /// <summary>
    /// What the briefing needs to land a vessel that the orbit and the stages do not say: the spinning body and its air,
    /// the vessel's mass and drag, its parachutes, the ground height at the target, and whether this is the home planet.
    /// </summary>
    public sealed class LandingContext
    {
        public LandingContext(LandingBody body, double mass, double dragArea, double chuteArea, double chuteDeployAltitude, double targetAltitude, bool homeBody)
        {
            Body = body;
            Mass = mass;
            DragArea = dragArea;
            ChuteArea = chuteArea;
            ChuteDeployAltitude = chuteDeployAltitude;
            TargetAltitude = targetAltitude;
            HomeBody = homeBody;
        }

        public LandingBody Body { get; }

        /// <summary>Kilograms.</summary>
        public double Mass { get; }

        public double DragArea { get; }

        public double ChuteArea { get; }

        public double ChuteDeployAltitude { get; }

        public double TargetAltitude { get; }

        public bool HomeBody { get; }
    }
}
