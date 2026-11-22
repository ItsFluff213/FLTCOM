using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class SpaceRockTests
    {
        [Theory]
        [InlineData(0, SizeClass.A)]
        [InlineData(2, SizeClass.C)]
        [InlineData(4, SizeClass.E)]
        [InlineData(8, SizeClass.I)]
        public void AsteroidTakesTheSavedSizeIndex(int index, SizeClass expected)
        {
            SpaceObject rock = SpaceRock.Of(VesselType.SpaceObject, index, false)!;
            Assert.Equal(SpaceObjectKind.Asteroid, rock.Kind);
            Assert.Equal(expected, rock.SizeClass);
        }

        [Fact]
        public void CometModuleMakesAComet()
        {
            SpaceObject rock = SpaceRock.Of(VesselType.SpaceObject, 3, true)!;
            Assert.Equal(SpaceObjectKind.Comet, rock.Kind);
            Assert.Equal(SizeClass.D, rock.SizeClass);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(-1)]
        [InlineData(9)]
        public void UnknownSizeStaysNull(int? index)
        {
            Assert.Null(SpaceRock.Of(VesselType.SpaceObject, index, false)!.SizeClass);
        }

        [Fact]
        public void OnlySpaceObjectsAreRocks()
        {
            Assert.Null(SpaceRock.Of(VesselType.Debris, 2, false));
            Assert.Null(SpaceRock.Of(VesselType.Probe, 2, true));
        }
    }
}
