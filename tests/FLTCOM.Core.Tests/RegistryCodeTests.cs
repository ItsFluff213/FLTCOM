using System;
using System.Linq;
using FLTCOM.Core.Telemetry;
using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class RegistryCodeTests
    {
        [Theory]
        [InlineData(VesselType.Probe, "FLT-0042-PRB")]
        [InlineData(VesselType.Station, "FLT-0042-STN")]
        [InlineData(VesselType.Relay, "FLT-0042-RLY")]
        [InlineData(VesselType.Ship, "FLT-0042-SHP")]
        [InlineData(VesselType.Lander, "FLT-0042-LDR")]
        [InlineData(VesselType.Debris, "FLT-0042-DBR")]
        [InlineData(VesselType.Flag, "FLT-0042-FLG")]
        public void CodeIsAgencyThenLastFourDigitsThenTypeTag(VesselType type, string expected)
        {
            Assert.Equal(expected, RegistryCode.For("FLT", 3_000_000_042u, type));
        }

        [Fact]
        public void EveryVesselTypeHasAThreeLetterTag()
        {
            foreach (VesselType type in Enum.GetValues(typeof(VesselType)).Cast<VesselType>())
            {
                Assert.Matches("^[A-Z]{3}$", RegistryCode.Tag(type));
            }
        }

        [Fact]
        public void APersistentIdChangeAfterDockingChangesTheCode()
        {
            string before = RegistryCode.For("FLT", 1234, VesselType.Station);
            string after = RegistryCode.For("FLT", 98765, VesselType.Station);

            Assert.Equal("FLT-1234-STN", before);
            Assert.Equal("FLT-8765-STN", after);
        }

        [Fact]
        public void AgencyPrefixIsTrimmedAndUpperCase()
        {
            Assert.Equal("KSC-0007-SHP", RegistryCode.For(" ksc ", 7, VesselType.Ship));
        }
    }
}
