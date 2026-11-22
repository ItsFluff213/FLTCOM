using CommNet;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Snapshot
{
    internal static class CommsReader
    {
        public static (CommsState State, double? Strength) Read(global::Vessel vessel, FLTCOM.Core.Protocol.VesselType type)
        {
            if (type == FLTCOM.Core.Protocol.VesselType.Debris || type == FLTCOM.Core.Protocol.VesselType.Flag || type == FLTCOM.Core.Protocol.VesselType.SpaceObject || type == FLTCOM.Core.Protocol.VesselType.DroppedPart)
            {
                return (CommsState.None, null);
            }
            if (!CommNetScenario.CommNetEnabled)
            {
                return (CommsState.Off, null);
            }
            CommNetVessel? connection = vessel.connection;
            if (connection == null)
            {
                return (CommsState.None, null);
            }
            return connection.IsConnectedHome
                ? (CommsState.Link, UnityEngine.Mathf.Clamp01((float)connection.SignalStrength))
                : (CommsState.Los, null);
        }
    }
}
