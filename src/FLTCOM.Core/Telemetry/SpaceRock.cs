using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>
    /// What an asteroid or comet is on the wire. KSP keeps the size class as an UntrackedObjectClass, A to I,
    /// and a save stores it as that enum's index; a rock carrying ModuleComet is a comet.
    /// </summary>
    public static class SpaceRock
    {
        public static SpaceObject? Of(VesselType type, int? sizeIndex, bool hasCometModule)
        {
            if (type != VesselType.SpaceObject)
            {
                return null;
            }
            SizeClass? size = sizeIndex is int index && index >= (int)SizeClass.A && index <= (int)SizeClass.I ? (SizeClass)index : (SizeClass?)null;
            return new SpaceObject(hasCometModule ? SpaceObjectKind.Comet : SpaceObjectKind.Asteroid, size);
        }
    }
}
