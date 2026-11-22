using System.Collections.Generic;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class CommsFilterTests
    {
        private static List<(uint, CommsState, double?)> Fleet(CommsState a, CommsState b, CommsState c) =>
            new List<(uint, CommsState, double?)>
            {
                (1, a, a == CommsState.Link ? 0.8 : (double?)null),
                (2, b, b == CommsState.Link ? 0.5 : (double?)null),
                (3, c, null),
            };

        [Fact]
        public void OneVesselLosingSignalIsBelieved()
        {
            var filter = new CommsFilter();
            filter.Filter(0, Fleet(CommsState.Link, CommsState.Link, CommsState.None));
            var after = filter.Filter(1, Fleet(CommsState.Link, CommsState.Los, CommsState.None));
            Assert.Equal(CommsState.Los, after[1].State);
        }

        [Fact]
        public void EveryVesselLosingSignalAtOnceKeepsTheLastLink()
        {
            var filter = new CommsFilter();
            filter.Filter(0, Fleet(CommsState.Link, CommsState.Link, CommsState.None));
            var after = filter.Filter(10, Fleet(CommsState.Los, CommsState.Los, CommsState.None));
            Assert.Equal((CommsState.Link, (double?)0.8), after[0]);
            Assert.Equal((CommsState.Link, (double?)0.5), after[1]);
            Assert.Equal(CommsState.None, after[2].State);
        }

        [Fact]
        public void TheHoldEndsAfterTwoMinutes()
        {
            var filter = new CommsFilter();
            filter.Filter(0, Fleet(CommsState.Link, CommsState.Link, CommsState.None));
            filter.Filter(10, Fleet(CommsState.Los, CommsState.Los, CommsState.None));
            var late = filter.Filter(10 + CommsFilter.MaxHoldSeconds + 1, Fleet(CommsState.Los, CommsState.Los, CommsState.None));
            Assert.Equal(CommsState.Los, late[0].State);
            Assert.Equal(CommsState.Los, late[1].State);
        }

        [Fact]
        public void ARealLinkEndsTheHold()
        {
            var filter = new CommsFilter();
            filter.Filter(0, Fleet(CommsState.Link, CommsState.Link, CommsState.None));
            filter.Filter(10, Fleet(CommsState.Los, CommsState.Los, CommsState.None));
            var back = filter.Filter(20, Fleet(CommsState.Link, CommsState.Los, CommsState.None));
            Assert.Equal(CommsState.Link, back[0].State);
            Assert.Equal(CommsState.Los, back[1].State);
        }

        [Fact]
        public void AFleetThatNeverHadALinkStaysLost()
        {
            var filter = new CommsFilter();
            var first = filter.Filter(0, Fleet(CommsState.Los, CommsState.Los, CommsState.None));
            Assert.Equal(CommsState.Los, first[0].State);
        }
    }
}
