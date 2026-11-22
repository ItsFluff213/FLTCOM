using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FLTCOM.Server.Tests
{
    public class FrameTests
    {
        private const int MaxMessage = 1 << 20;

        private sealed record Outcome(List<string> Texts, ConnState State, int CloseCode, bool ClosedByServer, List<ServerFrame> Sent);

        private static Outcome Feed(byte[] input, bool byteAtATime)
        {
            var conn = new WebSocketConnection(Limits.Default);
            if (byteAtATime)
            {
                for (int i = 0; i < input.Length; i++)
                {
                    conn.Receive(input, i, 1);
                }
            }
            else
            {
                conn.Receive(input, 0, input.Length);
            }
            var texts = new List<string>();
            for (ConnEvent e = conn.TakeEvent(); e.Kind == ConnEventKind.Text; e = conn.TakeEvent())
            {
                texts.Add(e.Text);
            }
            List<ServerFrame> sent = ServerFrames.ReadAll(Drain(conn));
            return new Outcome(texts, conn.State, conn.State == ConnState.Open ? 0 : conn.CloseCode, conn.ServerCloseReason != null, sent);
        }

        internal static byte[] Drain(WebSocketConnection conn)
        {
            var sent = new MemoryStream();
            while (conn.HasOutbound)
            {
                ArraySegment<byte> next = conn.PeekOutbound();
                sent.Write(next.Array!, next.Offset, next.Count);
                conn.Advance(next.Count);
            }
            return sent.ToArray();
        }

        private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

        private static void AssertServerClosed(Outcome outcome, int code)
        {
            Assert.Empty(outcome.Texts);
            Assert.Equal(ConnState.Closed, outcome.State);
            Assert.True(outcome.ClosedByServer);
            ServerFrame close = Assert.Single(outcome.Sent);
            Assert.Equal(Opcode.Close, close.Opcode);
            Assert.Equal(code, close.CloseCode);
        }

        [Theory]
        [InlineData(5, 6, false)]
        [InlineData(5, 6, true)]
        [InlineData(125, 6, false)]
        [InlineData(125, 6, true)]
        [InlineData(126, 8, false)]
        [InlineData(126, 8, true)]
        [InlineData(65535, 8, false)]
        [InlineData(65535, 8, true)]
        [InlineData(65536, 14, false)]
        [InlineData(65536, 14, true)]
        [InlineData(70000, 14, false)]
        [InlineData(70000, 14, true)]
        public void MaskedTextArrivesInEveryLengthForm(int length, int expectedHeaderBytes, bool byteAtATime)
        {
            string text = new string('k', length);
            byte[] frame = ClientFrame.Text(text);
            Assert.Equal(expectedHeaderBytes + length, frame.Length);

            Outcome outcome = Feed(frame, byteAtATime);

            Assert.Equal(new[] { text }, outcome.Texts);
            Assert.Equal(ConnState.Open, outcome.State);
            Assert.Empty(outcome.Sent);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultiByteTextSurvivesUnmasking(bool byteAtATime)
        {
            Outcome outcome = Feed(ClientFrame.Text("Mün ↑ \U0001F680"), byteAtATime);

            Assert.Equal(new[] { "Mün ↑ \U0001F680" }, outcome.Texts);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AMessageOfExactlyTheLimitIsAccepted(bool byteAtATime)
        {
            Outcome outcome = Feed(ClientFrame.Build(Opcode.Text, Enumerable.Repeat((byte)'a', MaxMessage).ToArray()), byteAtATime);

            Assert.Equal(MaxMessage, Assert.Single(outcome.Texts).Length);
            Assert.Equal(ConnState.Open, outcome.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PingIsAnsweredWithItsPayload(bool byteAtATime)
        {
            byte[] payload = { 1, 2, 3, 250 };

            Outcome outcome = Feed(ClientFrame.Build(Opcode.Ping, payload), byteAtATime);

            ServerFrame pong = Assert.Single(outcome.Sent);
            Assert.Equal(Opcode.Pong, pong.Opcode);
            Assert.Equal(payload, pong.Payload);
            Assert.Equal(ConnState.Open, outcome.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnsolicitedPongIsIgnored(bool byteAtATime)
        {
            Outcome outcome = Feed(Join(ClientFrame.Build(Opcode.Pong, new byte[] { 9 }), ClientFrame.Text("after")), byteAtATime);

            Assert.Equal(new[] { "after" }, outcome.Texts);
            Assert.Empty(outcome.Sent);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClientCloseIsEchoedWithItsCode(bool byteAtATime)
        {
            Outcome outcome = Feed(ClientFrame.Close(1001), byteAtATime);

            Assert.Equal(ConnState.Closed, outcome.State);
            Assert.Equal(1001, outcome.CloseCode);
            Assert.False(outcome.ClosedByServer);
            ServerFrame close = Assert.Single(outcome.Sent);
            Assert.Equal(Opcode.Close, close.Opcode);
            Assert.Equal(1001, close.CloseCode);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClientCloseWithoutACodeIsEchoedEmpty(bool byteAtATime)
        {
            Outcome outcome = Feed(ClientFrame.Build(Opcode.Close, Array.Empty<byte>()), byteAtATime);

            Assert.Equal(CloseCodes.NoStatus, outcome.CloseCode);
            Assert.False(outcome.ClosedByServer);
            Assert.Empty(Assert.Single(outcome.Sent).Payload);
        }

        public static IEnumerable<object[]> MalformedFrames()
        {
            var cases = new (string Name, byte[] Bytes)[]
            {
                ("RSV1 set", ClientFrame.Build(Opcode.Text, new byte[] { 1 }, rsv: 4)),
                ("RSV3 set", ClientFrame.Build(Opcode.Text, new byte[] { 1 }, rsv: 1)),
                ("unmasked", ClientFrame.Build(Opcode.Text, new byte[] { (byte)'x' }, masked: false)),
                ("64-bit length with the top bit", new byte[] { 0x81, 0xFF, 0x80, 0, 0, 0, 0, 0, 0, 0 }),
                ("ping over 125 bytes", ClientFrame.Header(Opcode.Ping, 126)),
                ("ping without FIN", ClientFrame.Header(Opcode.Ping, 0, fin: false)),
                ("close without FIN", ClientFrame.Header(Opcode.Close, 0, fin: false)),
                ("unknown opcode 3", ClientFrame.Header((Opcode)3, 0)),
                ("unknown opcode 11", ClientFrame.Header((Opcode)11, 0)),
                ("continuation with no message open", ClientFrame.Build(Opcode.Continuation, new byte[] { 1 })),
                ("16-bit form for a short length", ClientFrame.Header(Opcode.Text, 5, form: LengthForm.Force16)),
                ("64-bit form for a short length", ClientFrame.Header(Opcode.Text, 300, form: LengthForm.Force64)),
                ("close with a one-byte payload", ClientFrame.Build(Opcode.Close, new byte[] { 3 })),
                ("close with the reserved code 1005", ClientFrame.Close(1005)),
                ("close with code 0", ClientFrame.Close(0)),
                ("text inside a fragmented message", Join(ClientFrame.Text("a", fin: false), ClientFrame.Text("b"))),
            };
            foreach ((string name, byte[] bytes) in cases)
            {
                yield return new object[] { name, bytes, false };
                yield return new object[] { name, bytes, true };
            }
        }

        [Theory]
        [MemberData(nameof(MalformedFrames))]
        public void MalformedFrameClosesWith1002(string name, byte[] bytes, bool byteAtATime)
        {
            Assert.NotNull(name);
            AssertServerClosed(Feed(bytes, byteAtATime), 1002);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OversizeIsRefusedFromTheHeaderAlone(bool byteAtATime)
        {
            byte[] lengthOnly = ClientFrame.Header(Opcode.Text, 2 * MaxMessage, masked: false);
            Assert.Equal(10, lengthOnly.Length);

            AssertServerClosed(Feed(lengthOnly, byteAtATime), 1009);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OneByteOverTheLimitIsRefused(bool byteAtATime)
        {
            AssertServerClosed(Feed(ClientFrame.Header(Opcode.Text, MaxMessage + 1), byteAtATime), 1009);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FragmentsAreReassembledAroundAPing(bool byteAtATime)
        {
            byte[] input = Join(
                ClientFrame.Text("hel", fin: false),
                ClientFrame.Build(Opcode.Ping, new byte[] { 7 }),
                ClientFrame.Build(Opcode.Continuation, Encoding.UTF8.GetBytes("l"), fin: false),
                ClientFrame.Build(Opcode.Continuation, Array.Empty<byte>(), fin: false),
                ClientFrame.Build(Opcode.Continuation, Encoding.UTF8.GetBytes("o")),
                ClientFrame.Text("next"));

            Outcome outcome = Feed(input, byteAtATime);

            Assert.Equal(new[] { "hello", "next" }, outcome.Texts);
            Assert.Equal(new byte[] { 7 }, Assert.Single(outcome.Sent).Payload);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ACharacterSplitAcrossFragmentsIsOneCharacter(bool byteAtATime)
        {
            byte[] rocket = Encoding.UTF8.GetBytes("\U0001F680");
            byte[] input = Join(
                ClientFrame.Build(Opcode.Text, rocket.Take(2).ToArray(), fin: false),
                ClientFrame.Build(Opcode.Continuation, rocket.Skip(2).ToArray()));

            Assert.Equal(new[] { "\U0001F680" }, Feed(input, byteAtATime).Texts);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheLimitHoldsAcrossFragments(bool byteAtATime)
        {
            byte[] input = Join(
                ClientFrame.Build(Opcode.Text, new byte[600 * 1024], fin: false),
                ClientFrame.Header(Opcode.Continuation, 600 * 1024));

            AssertServerClosed(Feed(input, byteAtATime), 1009);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidUtf8ClosesWith1007(bool byteAtATime)
        {
            AssertServerClosed(Feed(ClientFrame.Build(Opcode.Text, new byte[] { (byte)'a', 0xC3, 0x28 }), byteAtATime), 1007);
            AssertServerClosed(Feed(ClientFrame.Build(Opcode.Text, new byte[] { 0xED, 0xA0, 0x80 }), byteAtATime), 1007);
            AssertServerClosed(Feed(ClientFrame.Build(Opcode.Text, new byte[] { 0xF0, 0x9F }), byteAtATime), 1007);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidUtf8InACloseReasonClosesWith1007(bool byteAtATime)
        {
            AssertServerClosed(Feed(ClientFrame.Build(Opcode.Close, new byte[] { 0x03, 0xE8, 0xFF }), byteAtATime), 1007);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BinaryClosesWith1003(bool byteAtATime)
        {
            AssertServerClosed(Feed(ClientFrame.Build(Opcode.Binary, new byte[] { 1, 2, 3 }), byteAtATime), 1003);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NothingIsReadAfterAViolation(bool byteAtATime)
        {
            byte[] input = Join(ClientFrame.Text("kept"), ClientFrame.Build(Opcode.Binary, new byte[] { 1 }), ClientFrame.Text("ignored"), ClientFrame.Build(Opcode.Ping, new byte[] { 1 }));

            Outcome outcome = Feed(input, byteAtATime);

            Assert.Equal(new[] { "kept" }, outcome.Texts);
            Assert.Equal(1003, Assert.Single(outcome.Sent).CloseCode);
        }

        [Fact]
        public void EveryProperPrefixOfAHeaderNeedsMore()
        {
            foreach (int length in new[] { 5, 300, 70000 })
            {
                byte[] header = ClientFrame.Header(Opcode.Text, length);
                for (int count = 0; count < header.Length; count++)
                {
                    Assert.Equal(DecodeStatus.NeedMore, Frame.TryDecodeHeader(header, 0, count, MaxMessage, out _));
                }
                Assert.Equal(DecodeStatus.Ok, Frame.TryDecodeHeader(header, 0, header.Length, MaxMessage, out FrameHeader decoded));
                Assert.Equal(length, decoded.PayloadLength);
                Assert.Equal(header.Length, decoded.HeaderLength);
                Assert.True(decoded.Masked);
            }
        }

        [Fact]
        public void HeaderIsReadAtAnOffset()
        {
            byte[] padded = Join(new byte[] { 0xFF, 0xFF, 0xFF }, ClientFrame.Header(Opcode.Ping, 4));

            Assert.Equal(DecodeStatus.Ok, Frame.TryDecodeHeader(padded, 3, padded.Length - 3, MaxMessage, out FrameHeader header));
            Assert.Equal(Opcode.Ping, header.Opcode);
            Assert.Equal(4, header.PayloadLength);
        }

        [Fact]
        public void UnmaskingInPiecesMatchesUnmaskingWhole()
        {
            byte[] whole = Enumerable.Range(0, 23).Select(i => (byte)(i * 11)).ToArray();
            byte[] pieces = (byte[])whole.Clone();

            Frame.Unmask(whole, 0, whole.Length, 0xA1B2C3D4, 0);
            Frame.Unmask(pieces, 0, 5, 0xA1B2C3D4, 0);
            Frame.Unmask(pieces, 5, 1, 0xA1B2C3D4, 5);
            Frame.Unmask(pieces, 6, 17, 0xA1B2C3D4, 6);

            Assert.Equal(whole, pieces);
            Assert.Equal((byte)(0 ^ 0xA1), whole[0]);
            Assert.Equal((byte)(11 ^ 0xB2), whole[1]);
        }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(125, 2)]
        [InlineData(126, 4)]
        [InlineData(65535, 4)]
        [InlineData(65536, 10)]
        public void EncodedTextUsesTheShortestLengthForm(int length, int expectedHeaderBytes)
        {
            string text = new string('z', length);

            byte[] encoded = Frame.EncodeText(text);

            Assert.Equal(expectedHeaderBytes + length, encoded.Length);
            ServerFrame frame = Assert.Single(ServerFrames.ReadAll(encoded));
            Assert.Equal(Opcode.Text, frame.Opcode);
            Assert.Equal(text, frame.Text);
        }

        [Fact]
        public void AcceptKeyMatchesTheRfcExample()
        {
            Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", Frame.AcceptKey("dGhlIHNhbXBsZSBub25jZQ=="));
        }
    }
}
