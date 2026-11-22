using System;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class DockingPortsTests
    {
        private static readonly DockingPort[] Chaser = { new DockingPort(11, "Clamp-O-Tron", "size1", true, new Vector3d(0, 0, 1)) };

        [Fact]
        public void ASizeMismatchIsRefusedAndNamesBothSizes()
        {
            var target = new[] { new DockingPort(21, "Clamp-O-Tron Sr.", "size2", true) };

            PortChoice choice = DockingPorts.Pick("Chaser", Chaser, "Station", target);

            Assert.False(choice.Ok);
            Assert.Contains("size2", choice.Refusal);
            Assert.Contains("size1", choice.Refusal);
        }

        [Fact]
        public void AnOverriddenPortOfTheWrongSizeIsRefused()
        {
            var target = new[] { new DockingPort(21, "Clamp-O-Tron", "size1", true), new DockingPort(22, "Clamp-O-Tron Jr.", "size0", true) };

            Assert.Contains("size mismatch", DockingPorts.Pick("Chaser", Chaser, "Station", target, targetOverride: 22).Refusal);
            Assert.Equal(21u, DockingPorts.Pick("Chaser", Chaser, "Station", target, targetOverride: 21).Target!.Id);
        }

        [Fact]
        public void AnOccupiedPortIsSkippedAndRefusedWhenPicked()
        {
            var target = new[] { new DockingPort(21, "Clamp-O-Tron", "size1", false), new DockingPort(22, "Clamp-O-Tron", "size1", true) };

            Assert.Equal(22u, DockingPorts.Pick("Chaser", Chaser, "Station", target).Target!.Id);
            Assert.Contains("occupied", DockingPorts.Pick("Chaser", Chaser, "Station", target, targetOverride: 21).Refusal);
            Assert.Contains("every port", DockingPorts.Pick("Chaser", Chaser, "Station", new[] { target[0] }).Refusal);
        }

        [Fact]
        public void ThePortFacingTheChaserWins()
        {
            var target = new[]
            {
                new DockingPort(21, "Clamp-O-Tron", "size1", true, new Vector3d(0, 0, -1)),
                new DockingPort(22, "Clamp-O-Tron", "size1", true, new Vector3d(1, 0, 0)),
                new DockingPort(23, "Clamp-O-Tron", "size1", true, new Vector3d(0, 0, 1)),
            };

            Assert.Equal(23u, DockingPorts.Pick("Chaser", Chaser, "Station", target, toChaser: new Vector3d(0.2, 0, 5)).Target!.Id);
            Assert.Equal(22u, DockingPorts.Pick("Chaser", Chaser, "Station", target, toChaser: new Vector3d(5, 0.3, 0)).Target!.Id);
        }

        [Fact]
        public void AVesselWithoutPortsIsRefused()
        {
            Assert.Contains("Chaser has no docking port", DockingPorts.Pick("Chaser", Array.Empty<DockingPort>(), "Station", Chaser).Refusal);
        }
    }

    public class DockingApproachTests
    {
        private const double Rcs = 0.15;
        private const double Step = 0.1;
        private static readonly Vector3d Center = new Vector3d(0, 0, -4);
        private const double Radius = 4;

        private sealed class Run
        {
            public double Seconds;
            public double ContactSpeed;
            public double ContactLateral;
            public double WorstConeExcess = double.NegativeInfinity;
            public double ClosestToTarget = double.PositiveInfinity;
            public double DeltaV;
        }

        /// <summary>A point mass with RCS on every axis, flying the commanded velocity until its port touches the target port.</summary>
        private static Run Fly(Vector3d start, Vector3d velocity)
        {
            var run = new Run();
            Vector3d position = start;
            while (run.Seconds < 1800)
            {
                DockingCommand command = DockingApproach.Command(position, Center, Radius, Rcs);
                if (command.Phase == DockingPhase.Approach)
                {
                    run.WorstConeExcess = Math.Max(run.WorstConeExcess, Lateral(position) - DockingApproach.Cone(position.Z) - 0.5);
                }
                else
                {
                    run.ClosestToTarget = Math.Min(run.ClosestToTarget, (position - Center).Magnitude);
                }
                Vector3d error = command.Velocity - velocity;
                var push = new Vector3d(Clamp(error.X), Clamp(error.Y), Clamp(error.Z));
                velocity += push * Step;
                run.DeltaV += push.Magnitude * Step;
                position += velocity * Step;
                run.Seconds += Step;
                if (position.Z <= 0.05 && Lateral(position) < 1)
                {
                    run.ContactSpeed = -velocity.Z;
                    run.ContactLateral = Lateral(position);
                    return run;
                }
            }
            throw new Xunit.Sdk.XunitException("never docked; ended at " + position.X.ToString("0.0") + ", " + position.Y.ToString("0.0") + ", " + position.Z.ToString("0.0"));
        }

        private static double Clamp(double wanted) => Math.Max(-Rcs, Math.Min(Rcs, wanted / 1.0));

        private static double Lateral(Vector3d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);

        [Fact]
        public void AnOffAxisApproachFrom50MetresStaysInsideTheConeAndTouchesGently()
        {
            Run run = Fly(new Vector3d(12, -8, 45), new Vector3d(0.1, 0, -0.1));

            Assert.True(run.WorstConeExcess <= 0, "left the cone by " + run.WorstConeExcess.ToString("0.00") + " m");
            Assert.InRange(run.ContactSpeed, 0.02, 0.3);
            Assert.InRange(run.ContactLateral, 0, 0.3);
            Assert.True(run.Seconds < 8 * 60, "took " + run.Seconds.ToString("0") + " s");
        }

        [Fact]
        public void AFarSidePortIsFlownAroundWithoutContact()
        {
            Run run = Fly(new Vector3d(1, 0, -40), default);

            Assert.True(run.ClosestToTarget > Radius + 2, "came within " + run.ClosestToTarget.ToString("0.0") + " m of the target's centre");
            Assert.True(run.WorstConeExcess <= 0);
            Assert.InRange(run.ContactSpeed, 0.02, 0.3);
        }

        [Fact]
        public void ACloseOffAxisStartBacksOutBeforeLiningUp()
        {
            Assert.Equal(DockingPhase.BackOut, DockingApproach.Command(new Vector3d(6, 0, 3), Center, Radius, Rcs).Phase);
            Assert.Equal(DockingPhase.LineUp, DockingApproach.Command(new Vector3d(6, 0, 12), Center, Radius, Rcs).Phase);
            Assert.Equal(DockingPhase.Approach, DockingApproach.Command(new Vector3d(0.5, 0, 12), Center, Radius, Rcs).Phase);
        }

        [Fact]
        public void AMisalignedChaserWaitsOutsideTheSafeDistance()
        {
            DockingCommand command = DockingApproach.Command(new Vector3d(0, 0, 8), Center, Radius, Rcs, misalignment: 20);

            Assert.Equal(0, command.Velocity.Z);
        }
    }
}
