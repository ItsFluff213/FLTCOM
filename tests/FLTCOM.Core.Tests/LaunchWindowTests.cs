using System;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class LaunchWindowTests
    {
        private const double KerbinDay = 21_549.425;
        private static readonly double KscLatitude = Angles.ToRadians(-0.0972);

        [Fact]
        public void EquatorialLaunchGoesNowAndDueEast()
        {
            var site = new LaunchSite(KscLatitude, Angles.ToRadians(200), 70, KerbinDay, 1_000);

            LaunchPlane plane = LaunchWindow.Find(site, null, null, 1_060);

            Assert.Equal(1_060, plane.LaunchUt);
            Assert.Equal(Math.Abs(KscLatitude), plane.Inclination, 9);
            Assert.Null(plane.Note);
            Assert.InRange(Angles.ToDegrees(plane.Heading(site.At(plane.LaunchUt))), 89.8, 90.2);
            AssertSiteInPlane(site.At(plane.LaunchUt), plane);
        }

        [Fact]
        public void ZeroInclinationFromOffTheEquatorIsRaisedToTheLatitude()
        {
            var site = new LaunchSite(Angles.ToRadians(-6), 0, 70, KerbinDay, 0);

            LaunchPlane plane = LaunchWindow.Find(site, 0, null, 0);

            Assert.Equal(6, Angles.ToDegrees(plane.Inclination), 6);
            Assert.Contains("lowest inclination", plane.Note);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(250)]
        public void FortyFiveDegreesWithALanWaitsForThePlaneToPassOverhead(double lanDegrees)
        {
            var site = new LaunchSite(KscLatitude, Angles.ToRadians(30), 70, KerbinDay, 0);
            double lan = Angles.ToRadians(lanDegrees);

            LaunchPlane plane = LaunchWindow.Find(site, Angles.ToRadians(45), lan, 60);

            Assert.InRange(plane.LaunchUt, 60, 60 + KerbinDay / 2 + 1);
            Assert.Equal(lan, plane.Lan, 9);
            AssertSiteInPlane(site.At(plane.LaunchUt), plane);
            double heading = Angles.ToDegrees(plane.Heading(site.At(plane.LaunchUt)));
            Assert.InRange(heading, plane.North ? 0 : 90, plane.North ? 90 : 180);
        }

        [Fact]
        public void TheSoonerOfTheTwoPassesIsChosen()
        {
            double lat = KscLatitude, lon = Angles.ToRadians(30), lan = Angles.ToRadians(90), inc = Angles.ToRadians(45);

            double north = LaunchWindow.TimeToPlane(KerbinDay, lat, lon, lan, inc);
            double south = LaunchWindow.TimeToPlane(KerbinDay, lat, lon, lan, -inc);
            (double soonest, bool goNorth) = LaunchWindow.MinimumTimeToPlane(KerbinDay, lat, lon, lan, inc);

            Assert.Equal(Math.Min(north, south), soonest, 9);
            Assert.Equal(north <= south, goNorth);
            Assert.InRange(Math.Abs(north - south), KerbinDay / 2 - 200, KerbinDay / 2 + 200);
        }

        private static void AssertSiteInPlane(LaunchSite site, LaunchPlane plane)
        {
            double offPlane = Vector3d.Dot(site.Position(1), plane.Normal);
            Assert.InRange(offPlane, -1e-6, 1e-6);
        }
    }
}
