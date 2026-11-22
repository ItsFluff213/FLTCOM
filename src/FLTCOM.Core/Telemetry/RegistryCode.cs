using System;
using System.Globalization;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>
    /// The fleet registry code, such as FLT-0042-PRB: agency prefix, the last four digits of the vessel's
    /// persistentId, and a type tag. It derives from the current persistentId on every snapshot, so a docking or
    /// undocking that changes the id changes the code with it.
    /// </summary>
    public static class RegistryCode
    {
        public const string DefaultAgency = "FLT";

        public static string For(string agency, uint persistentId, VesselType type)
        {
            if (string.IsNullOrWhiteSpace(agency))
            {
                throw new ArgumentException("agency prefix is empty", nameof(agency));
            }
            return agency.Trim().ToUpperInvariant() + "-" + (persistentId % 10000).ToString("D4", CultureInfo.InvariantCulture) + "-" + Tag(type);
        }

        public static string Tag(VesselType type)
        {
            switch (type)
            {
                case VesselType.Station: return "STN";
                case VesselType.Relay: return "RLY";
                case VesselType.Lander: return "LDR";
                case VesselType.Probe: return "PRB";
                case VesselType.Ship: return "SHP";
                case VesselType.Debris: return "DBR";
                case VesselType.Rover: return "RVR";
                case VesselType.Base: return "BAS";
                case VesselType.Plane: return "PLN";
                case VesselType.Eva: return "EVA";
                case VesselType.Flag: return "FLG";
                case VesselType.SpaceObject: return "AST";
                case VesselType.DeployedScienceController:
                case VesselType.DeployedSciencePart:
                case VesselType.DeployedGroundPart:
                    return "SCI";
                case VesselType.DroppedPart: return "DRP";
                case VesselType.Unknown: return "UNK";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
