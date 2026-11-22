using System;
using System.Linq;
using Xunit;

namespace FLTCOM.Server.Tests
{
    public class ConnectionTests
    {
        private static readonly byte[] Hello = Frame.EncodeText("hello");

        private static string[] SentTexts(WebSocketConnection conn) =>
            ServerFrames.ReadAll(FrameTests.Drain(conn)).Select(frame => frame.Opcode == Opcode.Close ? "close " + frame.CloseCode : frame.Text).ToArray();

        [Fact]
        public void OnlyTheNewestWaitingFleetIsSent()
        {
            var conn = new WebSocketConnection(Limits.Default);
            conn.SendFleet(Frame.EncodeText("fleet 1"));
            conn.SendFleet(Frame.EncodeText("fleet 2"));
            conn.SendFleet(Frame.EncodeText("fleet 3"));

            Assert.Equal(new[] { "fleet 3" }, SentTexts(conn));
        }

        [Fact]
        public void AFleetAlreadyGoingOutIsFinishedBeforeTheNextOne()
        {
            var conn = new WebSocketConnection(Limits.Default);
            conn.SendFleet(Frame.EncodeText("fleet 1"));
            ArraySegment<byte> first = conn.PeekOutbound();
            conn.Advance(first.Count - 3);
            conn.SendFleet(Frame.EncodeText("fleet 2"));
            conn.SendFleet(Frame.EncodeText("fleet 3"));
            conn.SendControl(Hello);

            ArraySegment<byte> rest = conn.PeekOutbound();
            Assert.Equal("t 1", System.Text.Encoding.ASCII.GetString(rest.Array!, rest.Offset, rest.Count));
            conn.Advance(rest.Count);
            Assert.Equal(new[] { "hello", "fleet 3" }, SentTexts(conn));
        }

        [Fact]
        public void CloseDropsTheWaitingFleetAndKeepsQueuedControlFrames()
        {
            var conn = new WebSocketConnection(Limits.Default);
            conn.SendFleet(Frame.EncodeText("fleet 1"));
            conn.SendControl(Frame.EncodeText("bye"));

            conn.Close(CloseCodes.GoingAway, "shutdown");
            conn.Close(CloseCodes.Normal, "a second close is ignored");
            conn.SendControl(Hello);
            conn.SendFleet(Frame.EncodeText("fleet 2"));

            Assert.Equal(ConnState.Closing, conn.State);
            Assert.Equal(new[] { "bye", "close 1001" }, SentTexts(conn));
            Assert.Equal(ConnState.Closed, conn.State);
            Assert.Equal("shutdown", conn.ServerCloseReason);
        }

        [Fact]
        public void RepliesToAPingFloodGoOutAsOneSend()
        {
            var conn = new WebSocketConnection(Limits.Default);
            byte[] pings = Enumerable.Repeat(ClientFrame.Build(Opcode.Ping, Array.Empty<byte>()), 1000).SelectMany(frame => frame).ToArray();
            conn.Receive(pings, 0, pings.Length);

            ArraySegment<byte> first = conn.PeekOutbound();
            Assert.Equal(1000 * 2, first.Count);
            Assert.Equal(1000, ServerFrames.ReadAll(first.ToArray()).Count(frame => frame.Opcode == Opcode.Pong));
        }

        [Fact]
        public void QueuedControlBytesCountsWhatHasNotStartedToGoOut()
        {
            var conn = new WebSocketConnection(Limits.Default);
            byte[] ping = ClientFrame.Build(Opcode.Ping, new byte[100]);
            for (int i = 0; i < 50; i++)
            {
                conn.Receive(ping, 0, ping.Length);
            }

            Assert.Equal(50 * (102 + 64), conn.QueuedControlBytes);
            FrameTests.Drain(conn);
            Assert.Equal(0, conn.QueuedControlBytes);
        }
    }
}
