// Ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/MechJebModuleLandingAutopilot.cs (SafeDescentSpeedPolicy, PoweredCoastDescentSpeedPolicy,
// DecelerationEndAltitude, UseAtmosphereToBrake), modified for FLTCOM: SI units, no KSP types.
// Original notice: Copyright (C) the MechJeb2 contributors. Licensed under the GNU General Public License version 3.
using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    /// <summary>The fastest the vessel may move at a point and still stop before the ground.</summary>
    public interface IDescentSpeedPolicy
    {
        double MaxAllowedSpeed(Vector3d position, Vector3d surfaceVelocity);
    }

    /// <summary>The max safe speed if the whole velocity pointed straight down: a suicide burn with a 10 % speed margin.</summary>
    public sealed class SafeDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly double _terrainRadius;
        private readonly double _g;
        private readonly double _thrust;

        public SafeDescentSpeedPolicy(double terrainRadius, double g, double thrust)
        {
            _terrainRadius = terrainRadius;
            _g = g;
            _thrust = thrust;
        }

        public double MaxAllowedSpeed(Vector3d position, Vector3d surfaceVelocity)
        {
            double altitude = position.Magnitude - _terrainRadius;
            return 0.9 * Math.Sqrt(2 * Math.Max(0, _thrust - _g) * Math.Max(0, altitude));
        }
    }

    /// <summary>Coast through the air, then brake below the end of the deceleration with 80 % of the spare thrust.</summary>
    public sealed class PoweredCoastDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly double _terrainRadius;
        private readonly double _g;
        private readonly double _thrust;

        public PoweredCoastDescentSpeedPolicy(double terrainRadius, double g, double thrust)
        {
            _terrainRadius = terrainRadius;
            _g = g;
            _thrust = thrust;
        }

        public double MaxAllowedSpeed(Vector3d position, Vector3d surfaceVelocity)
        {
            if (_terrainRadius < position.Magnitude)
            {
                return double.MaxValue;
            }
            double vSpeed = Vector3d.Dot(surfaceVelocity, position.Normalized);
            double toF = (vSpeed + Math.Sqrt(vSpeed * vSpeed + 2 * _g * (position.Magnitude - _terrainRadius))) / _g;
            return 0.8 * (_thrust - _g) * toF;
        }
    }

    /// <summary>Where braking ends and which speed policy brakes, from the vessel and the air at the landing site.</summary>
    public static class DescentRules
    {
        private const double MinAtmosphereDecelerationEnd = 50;
        private const double VacuumDecelerationEnd = 200;

        /// <summary>The distance over which drag slows the vessel at the landing site, in metres.</summary>
        public static double DragLength(LandingBody body, LandingCraft craft, double landingAltitude, bool chutes)
        {
            double density = body.Density(landingAltitude);
            double area = craft.DragArea + (chutes ? craft.ChuteArea : 0);
            return density <= 0 || area <= 0 ? double.MaxValue : craft.Mass / (0.5 * density * area);
        }

        /// <summary>The air is thick enough to slow the vessel to near its terminal speed before the ground.</summary>
        public static bool UseAtmosphereToBrake(LandingBody body, LandingCraft craft, double landingAltitude) =>
            body.AtmosphereHeight > 0 && DragLength(body, craft, landingAltitude, chutes: true) < 0.7 * body.AtmosphereHeight;

        /// <summary>Altitude above sea level where the braking burn ends and the final descent begins.</summary>
        public static double DecelerationEndAltitude(LandingBody body, LandingCraft craft, double landingAltitude)
        {
            if (!UseAtmosphereToBrake(body, craft, landingAltitude))
            {
                return VacuumDecelerationEnd + landingAltitude;
            }
            return Math.Max(1.1 * DragLength(body, craft, landingAltitude, chutes: true), MinAtmosphereDecelerationEnd) + landingAltitude;
        }

        /// <summary>The braking policy, or null for a vessel without thrust.</summary>
        public static IDescentSpeedPolicy? Policy(LandingBody body, LandingCraft craft, double landingAltitude)
        {
            if (craft.MaxAccel <= 0)
            {
                return null;
            }
            double end = body.Radius + DecelerationEndAltitude(body, craft, landingAltitude);
            return UseAtmosphereToBrake(body, craft, landingAltitude)
                ? new PoweredCoastDescentSpeedPolicy(end, body.SurfaceGravity, craft.MaxAccel)
                : new SafeDescentSpeedPolicy(end, body.SurfaceGravity, craft.MaxAccel);
        }
    }
}
