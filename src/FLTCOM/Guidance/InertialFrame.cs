using System;
using CoreSite = FLTCOM.Core.Planning.LaunchSite;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Guidance
{
    /// <summary>
    /// The core's right-handed inertial frame: X towards the reference direction orbits measure their LAN from, Z along
    /// the spin axis. It is the game's Planetarium frame, so it stays put while the world frame turns with the body.
    /// </summary>
    internal static class InertialFrame
    {
        public static CoreVector ToCore(Vector3d world) =>
            new CoreVector(Vector3d.Dot(world, Planetarium.right), Vector3d.Dot(world, Planetarium.forward), Vector3d.Dot(world, Planetarium.up));

        public static Vector3d ToWorld(CoreVector v) => Planetarium.right * v.X + Planetarium.forward * v.Y + Planetarium.up * v.Z;

        public static CoreVector Position(Vessel vessel) => ToCore(vessel.CoMD - vessel.mainBody.position);

        public static CoreVector Velocity(Vessel vessel) => ToCore(vessel.obt_velocity);

        public static CoreSite Site(Vessel vessel, double ut)
        {
            CoreVector r = Position(vessel);
            return new CoreSite(vessel.latitude * UtilMath.Deg2Rad, Math.Atan2(r.Y, r.X), vessel.altitude, vessel.mainBody.rotationPeriod, ut);
        }
    }
}
