// TimeToPlane and MinimumTimeToPlane are ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJebLib/Functions/Astro.cs
// Original notice:
//   Copyright Lamont Granquist, Sebastien Gaggini and the MechJeb contributors
//   SPDX-License-Identifier: LicenseRef-PD-hp OR Unlicense OR CC0-1.0 OR 0BSD OR MIT-0 OR MIT OR LGPL-2.1+
using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>
    /// A launch site at one instant. Angles in radians. CelestialLongitude is measured in the inertial frame the same
    /// way an orbit's longitude of the ascending node is. A negative rotation period means the body spins backwards.
    /// </summary>
    public sealed class LaunchSite
    {
        public LaunchSite(double latitude, double celestialLongitude, double altitude, double rotationPeriod, double ut)
        {
            Latitude = latitude;
            CelestialLongitude = Angles.Wrap(celestialLongitude);
            Altitude = altitude;
            RotationPeriod = rotationPeriod;
            Ut = ut;
        }

        public double Latitude { get; }

        public double CelestialLongitude { get; }

        /// <summary>Above sea level, in metres.</summary>
        public double Altitude { get; }

        public double RotationPeriod { get; }

        public double Ut { get; }

        /// <summary>Signed spin rate in rad/s, positive for a body that turns the same way as a prograde equatorial orbit.</summary>
        public double RotationRate => Angles.TwoPi / RotationPeriod;

        public LaunchSite At(double ut) => new LaunchSite(Latitude, CelestialLongitude + RotationRate * (ut - Ut), Altitude, RotationPeriod, ut);

        /// <summary>The site's inertial position from the body centre.</summary>
        public Vector3d Position(double bodyRadius)
        {
            double r = bodyRadius + Altitude;
            return new Vector3d(r * Math.Cos(Latitude) * Math.Cos(CelestialLongitude), r * Math.Cos(Latitude) * Math.Sin(CelestialLongitude), r * Math.Sin(Latitude));
        }

        /// <summary>The inertial velocity the site carries from the body's spin.</summary>
        public Vector3d Velocity(double bodyRadius) => Vector3d.Cross(new Vector3d(0, 0, RotationRate), Position(bodyRadius));
    }

    /// <summary>When to launch and into which plane. Angles in radians.</summary>
    public sealed class LaunchPlane
    {
        public LaunchPlane(double launchUt, double inclination, double lan, bool north, string? note)
        {
            LaunchUt = launchUt;
            Inclination = inclination;
            Lan = lan;
            North = north;
            Note = note;
        }

        public double LaunchUt { get; }

        public double Inclination { get; }

        public double Lan { get; }

        /// <summary>The ascent heads north of east (true) or south of east (false).</summary>
        public bool North { get; }

        /// <summary>Why the plane differs from the one asked for, or null.</summary>
        public string? Note { get; }

        /// <summary>Unit normal of the target plane: the direction of the angular momentum of an orbit in it.</summary>
        public Vector3d Normal => PlaneNormal(Inclination, Lan);

        public static Vector3d PlaneNormal(double inclination, double lan) =>
            new Vector3d(Math.Sin(lan) * Math.Sin(inclination), -Math.Cos(lan) * Math.Sin(inclination), Math.Cos(inclination));

        /// <summary>The inertial heading at <paramref name="site"/> along the plane, in radians clockwise from north.</summary>
        public double Heading(LaunchSite site)
        {
            Vector3d r = site.Position(1);
            Vector3d along = Vector3d.Cross(Normal, r);
            Enu enu = Enu.At(r);
            return Angles.Wrap(Math.Atan2(Vector3d.Dot(along, enu.East), Vector3d.Dot(along, enu.North)));
        }
    }

    /// <summary>The local east, north and up unit vectors at a point in the body's inertial frame.</summary>
    public readonly struct Enu
    {
        private Enu(Vector3d east, Vector3d north, Vector3d up)
        {
            East = east;
            North = north;
            Up = up;
        }

        public Vector3d East { get; }

        public Vector3d North { get; }

        public Vector3d Up { get; }

        public static Enu At(Vector3d position)
        {
            Vector3d up = position.Normalized;
            Vector3d east = Vector3d.Cross(new Vector3d(0, 0, 1), up);
            east = east.Magnitude < 1e-9 ? Vector3d.UnitX : east.Normalized;
            return new Enu(east, Vector3d.Cross(up, east), up);
        }

        public Vector3d ToLocal(Vector3d v) => new Vector3d(Vector3d.Dot(v, East), Vector3d.Dot(v, North), Vector3d.Dot(v, Up));

        public Vector3d FromLocal(Vector3d local) => East * local.X + North * local.Y + Up * local.Z;
    }

    public static class LaunchWindow
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Picks the launch time and plane. With no inclination the rocket flies due east into the lowest inclination
        /// the site allows; with an inclination but no LAN it launches at <paramref name="earliestUt"/> heading north;
        /// with both it waits for the site to pass under the plane, north- or southbound, whichever comes first.
        /// </summary>
        public static LaunchPlane Find(LaunchSite site, double? inclination, double? lan, double earliestUt)
        {
            LaunchSite now = site.At(earliestUt);
            double lowest = Math.Abs(now.Latitude);
            double inc = inclination ?? lowest;
            string? note = null;
            if (inc < lowest - Eps)
            {
                note = "the lowest inclination from this site is " + Deg(lowest) + " deg";
                inc = lowest;
            }
            else if (inc > Math.PI - lowest + Eps)
            {
                note = "the highest inclination from this site is " + Deg(Math.PI - lowest) + " deg";
                inc = Math.PI - lowest;
            }

            if (lan is double target)
            {
                (double wait, bool north) = MinimumTimeToPlane(now.RotationPeriod, now.Latitude, now.CelestialLongitude, target, inc);
                return new LaunchPlane(earliestUt + wait, inc, Angles.Wrap(target), north, note);
            }
            return new LaunchPlane(earliestUt, inc, LanThroughSite(now.Latitude, now.CelestialLongitude, inc, north: true), true, note);
        }

        /// <summary>The LAN of the plane at <paramref name="inc"/> that passes through the site right now.</summary>
        public static double LanThroughSite(double latitude, double celestialLongitude, double inc, bool north) =>
            Math.Abs(Math.Sin(inc)) < Eps ? 0 : Angles.Wrap(celestialLongitude - AngleEastOfAscendingNode(latitude, inc, north));

        /// <summary>Seconds until the site passes under the plane, and whether the ascent then heads north. Radians in.</summary>
        public static (double Time, bool North) MinimumTimeToPlane(double rotationPeriod, double latitude, double celestialLongitude, double lan, double inc)
        {
            double north = TimeToPlane(rotationPeriod, latitude, celestialLongitude, lan, Math.Abs(inc));
            double south = TimeToPlane(rotationPeriod, latitude, celestialLongitude, lan, -Math.Abs(inc));
            return north <= south ? (north, true) : (south, false);
        }

        /// <summary>
        /// Seconds until a rocket on the ground at the site is in the plane given by <paramref name="lan"/> and
        /// <paramref name="inc"/>. A negative inclination asks for the southbound pass. Radians in.
        /// </summary>
        public static double TimeToPlane(double rotationPeriod, double latitude, double celestialLongitude, double lan, double inc)
        {
            if (Math.Abs(Math.Abs(latitude) - Math.PI / 2) < Eps)
            {
                return 0;
            }
            if (Math.Abs(inc) < Eps || Math.Abs(Math.Abs(inc) - Math.PI) < Eps)
            {
                return 0;
            }
            double lanNow = celestialLongitude - AngleEastOfAscendingNode(latitude, Math.Abs(inc), inc >= 0);
            double lanDiff = lan - lanNow;
            if (rotationPeriod < 0)
            {
                lanDiff = -lanDiff;
            }
            return Angles.Wrap(lanDiff) / Angles.TwoPi * Math.Abs(rotationPeriod);
        }

        /// <summary>Napier's rules: how far east of the ascending node the site sits when the plane passes over it.</summary>
        private static double AngleEastOfAscendingNode(double latitude, double inc, bool north)
        {
            double angle = SafeAsin(Math.Tan(latitude) / Math.Tan(inc));
            return north ? angle : Math.PI - angle;
        }

        private static double SafeAsin(double x) => Math.Asin(Math.Max(-1, Math.Min(1, x)));

        private static string Deg(double radians) => Angles.ToDegrees(radians).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
