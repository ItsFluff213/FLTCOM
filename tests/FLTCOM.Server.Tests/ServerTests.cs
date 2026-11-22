using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Server.Tests
{
    /// <summary>A WebSocket client over a blocking socket. Every read gives up after five seconds.</summary>
    internal sealed class TestClient : IDisposable
    {
        private readonly TcpClient _tcp = new TcpClient();
        private readonly NetworkStream _stream;
        private readonly int _port;
        private byte[] _pending = Array.Empty<byte>();

        public TestClient(int port)
        {
            _port = port;
            _tcp.ReceiveTimeout = 5000;
            _tcp.NoDelay = true;
            _tcp.Connect("127.0.0.1", port);
            _stream = _tcp.GetStream();
        }

        public static TestClient Open(int port)
        {
            var client = new TestClient(port);
            Assert.Equal("HTTP/1.1 101 Switching Protocols", client.Upgrade("http://127.0.0.1:" + port));
            return client;
        }

        /// <summary>Returns the status line of the server's answer.</summary>
        public string Upgrade(string? origin, byte[]? sentInTheSameWrite = null)
        {
            string request = "GET /ws HTTP/1.1\r\nHost: 127.0.0.1:" + _port
                + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
                + (origin == null ? string.Empty : "Origin: " + origin + "\r\n") + "\r\n";
            Send(Encoding.ASCII.GetBytes(request).Concat(sentInTheSameWrite ?? Array.Empty<byte>()).ToArray());
            int end;
            while ((end = Encoding.Latin1.GetString(_pending).IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                Assert.True(ReadMore(), "the connection ended before the response head");
            }
            string head = Encoding.Latin1.GetString(_pending, 0, end);
            _pending = _pending.AsSpan(end + 4).ToArray();
            return head.Substring(0, head.IndexOf('\r'));
        }

        public void Send(byte[] bytes) => _stream.Write(bytes);

        public void Send(ClientMessage message) => Send(ClientFrame.Text(ProtocolJson.Write(message)));

        public ServerFrame ReadFrame()
        {
            while (true)
            {
                ServerFrame? frame = ServerFrames.TryRead(_pending, _pending.Length, out int consumed);
                if (frame != null)
                {
                    _pending = _pending.AsSpan(consumed).ToArray();
                    return frame;
                }
                Assert.True(ReadMore(), "the connection ended in the middle of a frame");
            }
        }

        public ServerMessage ReadMessage()
        {
            ServerFrame frame = ReadFrame();
            Assert.Equal(Opcode.Text, frame.Opcode);
            return ProtocolJson.ReadServerMessage(frame.Text);
        }

        public T Read<T>()
            where T : ServerMessage => Assert.IsType<T>(ReadMessage());

        public void AssertServerEndsTheConnection()
        {
            Assert.Empty(_pending);
            Assert.False(ReadMore(), "the server sent more bytes instead of ending the connection");
        }

        public void Dispose() => _tcp.Dispose();

        private bool ReadMore()
        {
            var buffer = new byte[64 * 1024];
            int read = _stream.Read(buffer, 0, buffer.Length);
            _pending = _pending.Concat(buffer.Take(read)).ToArray();
            return read > 0;
        }
    }

    public sealed class ServerTests : IDisposable
    {
        private readonly string _web = Path.Combine(Path.GetTempPath(), "fltcom-server-" + Guid.NewGuid().ToString("N"));
        private readonly ConcurrentQueue<string> _log = new ConcurrentQueue<string>();
        private readonly HttpServer _server;

        public ServerTests()
        {
            Directory.CreateDirectory(_web);
            File.WriteAllText(Path.Combine(_web, "index.html"), "<p>index</p>");
            _server = HttpServer.Start(new ServerOptions(0, _web, new ServerHello("test server", "save one", 1), _log.Enqueue));
        }

        public void Dispose()
        {
            _server.Dispose();
            Directory.Delete(_web, recursive: true);
        }

        private static Fleet FleetNumber(uint seq) => new Fleet(seq, 100.5, 1, null, Array.Empty<Body>(), Array.Empty<Vessel>(), null);

        private void WaitForLog(string line)
        {
            var waited = Stopwatch.StartNew();
            while (!_log.Contains(line))
            {
                Assert.True(waited.ElapsedMilliseconds < 5000, "no log line \"" + line + "\" in:\n" + string.Join("\n", _log));
                Thread.Sleep(5);
            }
        }

        private string Request(string requestLine, params string[] headers) =>
            StaticFileTests.RawRequest(_server.Port, requestLine + "\r\n" + string.Concat(headers.Select(header => header + "\r\n")) + "\r\n");

        private string Host => "Host: 127.0.0.1:" + _server.Port;

        [Fact]
        public void ANewClientGetsHelloThenOnlyFleetsPublishedAfterItConnected()
        {
            _server.Publish(FleetNumber(1));

            using TestClient client = TestClient.Open(_server.Port);
            ServerHello hello = client.Read<ServerHello>();
            WaitForLog("client 1 connected");
            _server.Publish(FleetNumber(2));

            Assert.Equal("test server", hello.Server);
            Assert.Equal("save one", hello.SaveName);
            Assert.Equal(2u, client.Read<Fleet>().Seq);
        }

        private static CraftLayout LayoutOf(uint vesselId, uint revision) => new CraftLayout(vesselId, revision, Array.Empty<CraftPart>(), null);

        [Fact]
        public void LayoutsReachConnectedClientsAndTheLatestOfEachIsResentAfterHello()
        {
            using TestClient early = TestClient.Open(_server.Port);
            early.Read<ServerHello>();
            WaitForLog("client 1 connected");

            _server.PublishLayout(LayoutOf(7, 1));
            _server.PublishLayout(LayoutOf(7, 2));
            _server.PublishLayout(LayoutOf(8, 1));
            _server.ForgetLayout(8);
            _server.PublishLayout(LayoutOf(9, 1));

            Assert.Equal(new[] { (7u, 1u), (7u, 2u), (8u, 1u), (9u, 1u) }, Enumerable.Range(0, 4).Select(_ => early.Read<CraftLayout>()).Select(l => (l.VesselId, l.Revision)));

            using TestClient late = TestClient.Open(_server.Port);
            late.Read<ServerHello>();
            var resent = new[] { late.Read<CraftLayout>(), late.Read<CraftLayout>() }.Select(l => (l.VesselId, l.Revision)).OrderBy(p => p.VesselId);
            Assert.Equal(new[] { (7u, 2u), (9u, 1u) }, resent);
        }

        [Fact]
        public void EveryClientGetsEachFleet()
        {
            TestClient[] clients = Enumerable.Range(0, 5).Select(_ => TestClient.Open(_server.Port)).ToArray();
            foreach (TestClient client in clients)
            {
                client.Read<ServerHello>();
            }
            WaitForLog("client 5 connected");

            _server.Publish(FleetNumber(7));

            foreach (TestClient client in clients)
            {
                Assert.Equal(7u, client.Read<Fleet>().Seq);
                client.Dispose();
            }
        }

        [Fact]
        public void PingIsAnsweredWithPongCarryingTheNonce()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            client.Send(new Ping(4242));

            Assert.Equal(4242u, client.Read<Pong>().Nonce);
            Assert.False(_server.Inbox.TryTake(out _));
        }

        [Fact]
        public void AClientHelloLandsInTheInbox()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Send(new ClientHello("a test tab"));
            client.Send(new Ping(1));
            client.Read<ServerHello>();
            client.Read<Pong>();

            Assert.True(_server.Inbox.TryTake(out ClientMessage message));
            Assert.Equal("a test tab", Assert.IsType<ClientHello>(message).Client);
            Assert.False(_server.Inbox.TryTake(out _));
        }

        [Fact]
        public void AClientThatFloodsTheInboxIsClosedAndAnotherStillGetsIn()
        {
            using TestClient flooder = TestClient.Open(_server.Port);
            using TestClient other = TestClient.Open(_server.Port);
            flooder.Read<ServerHello>();
            other.Read<ServerHello>();
            for (int i = 0; i < 10; i++)
            {
                flooder.Send(new ClientHello(new string('x', 60000)));
            }

            ServerFrame close = flooder.ReadFrame();
            Assert.Equal(Opcode.Close, close.Opcode);
            Assert.Equal(1008, close.CloseCode);
            WaitForLog("client 1 closed 1008 by server (its inbox share is full)");
            other.Send(new ClientHello("a test tab"));
            other.Send(new Ping(7));
            Assert.Equal(7u, other.Read<Pong>().Nonce);
            int landed = 0;
            while (_server.Inbox.TryTake(out ClientMessage message))
            {
                landed += ((ClientHello)message).Client == "a test tab" ? 1 : 0;
            }
            Assert.Equal(1, landed);
        }

        [Fact]
        public void NothingBehindAViolationInTheSameWriteReachesTheInbox()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();
            client.Send(ClientFrame.Text("not json").Concat(ClientFrame.Text(ProtocolJson.Write(new ClientHello("after close")))).ToArray());

            Assert.Equal(1008, client.ReadFrame().CloseCode);
            WaitForLog("client 1 closed 1008 by server (not a valid client message)");
            Assert.False(_server.Inbox.TryTake(out _));
        }

        [Fact]
        public void FramesSentRightBehindTheHandshakeAreRead()
        {
            using var client = new TestClient(_server.Port);
            byte[] ping = ClientFrame.Text(ProtocolJson.Write(new Ping(9)));

            Assert.Equal("HTTP/1.1 101 Switching Protocols", client.Upgrade("http://localhost:" + _server.Port, sentInTheSameWrite: ping));

            client.Read<ServerHello>();
            Assert.Equal(9u, client.Read<Pong>().Nonce);
        }

        [Fact]
        public void StopSendsByeThenClose1001ThenEndsTheConnection()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            _server.Stop(ByeReason.MultiplayerSession);

            Assert.Equal(ByeReason.MultiplayerSession, client.Read<Bye>().Reason);
            ServerFrame close = client.ReadFrame();
            Assert.Equal(Opcode.Close, close.Opcode);
            Assert.Equal(1001, close.CloseCode);
            client.AssertServerEndsTheConnection();
            WaitForLog("client 1 closed 1001 by server (server stopping)");
            WaitForLog("stopped");
        }

        [Fact]
        public void AfterStopThePortIsFreeAndFurtherCallsDoNothing()
        {
            _server.Stop(ByeReason.Shutdown);
            WaitForLog("stopped");

            _server.Publish(FleetNumber(1));
            _server.Announce(new ServerHello("late", null, 1));
            _server.Stop(ByeReason.Shutdown);
            _server.Dispose();

            using HttpServer again = HttpServer.Start(new ServerOptions(_server.Port, _web, new ServerHello("second", null, 1), _ => { }));
            Assert.Equal(_server.Port, again.Port);
        }

        [Fact]
        public void ASecondServerOnTheSamePortIsToldThePortIsBusy()
        {
            var options = new ServerOptions(_server.Port, _web, new ServerHello("second", null, 1), _ => { });

            PortBusyException busy = Assert.Throws<PortBusyException>(() => HttpServer.Start(options));

            Assert.Equal(_server.Port, busy.Port);
            Assert.Equal("port " + _server.Port + " busy", busy.Message);
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();
        }

        [Fact]
        public void ATwoMegabyteFrameIsClosedWith1009WhileAnotherClientKeepsReceiving()
        {
            using TestClient greedy = TestClient.Open(_server.Port);
            using TestClient other = TestClient.Open(_server.Port);
            greedy.Read<ServerHello>();
            other.Read<ServerHello>();

            greedy.Send(ClientFrame.Header(Opcode.Text, 2 * 1024 * 1024));
            greedy.Send(new byte[100_000]);

            ServerFrame close = greedy.ReadFrame();
            Assert.Equal(Opcode.Close, close.Opcode);
            Assert.Equal(1009, close.CloseCode);
            greedy.AssertServerEndsTheConnection();
            WaitForLog("client 1 closed 1009 by server (message over 1048576 bytes)");
            _server.Publish(FleetNumber(3));
            Assert.Equal(3u, other.Read<Fleet>().Seq);
            other.Send(new Ping(5));
            Assert.Equal(5u, other.Read<Pong>().Nonce);
        }

        [Fact]
        public void AClientThatClosesIsEchoedAndLoggedAsAClose()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            client.Send(ClientFrame.Close(1000));

            Assert.Equal(1000, client.ReadFrame().CloseCode);
            client.AssertServerEndsTheConnection();
            WaitForLog("client 1 closed 1000");
        }

        [Fact]
        public void AClientThatVanishesIsLoggedAsDropped()
        {
            TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            client.Dispose();

            WaitForLog("client 1 dropped (connection lost)");
        }

        [Fact]
        public void AnotherProtocolVersionGetsByeThenClose1000()
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            client.Send(ClientFrame.Text("{\"protocolVersion\":999,\"type\":\"hello\",\"client\":\"from the future\"}"));

            Assert.Equal(ByeReason.ProtocolMismatch, client.Read<Bye>().Reason);
            Assert.Equal(1000, client.ReadFrame().CloseCode);
            WaitForLog("client 1 closed 1000 by server (protocol version mismatch)");
            Assert.False(_server.Inbox.TryTake(out _));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"launch\"}")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"ping\",\"nonce\":-1}")]
        public void AMessageOutsideTheProtocolIsClosedWith1008(string text)
        {
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();

            client.Send(ClientFrame.Text(text));

            Assert.Equal(1008, client.ReadFrame().CloseCode);
            WaitForLog("client 1 closed 1008 by server (not a valid client message)");
        }

        [Fact]
        public void AnnounceReachesOpenClientsAndLaterOnes()
        {
            using TestClient early = TestClient.Open(_server.Port);
            Assert.Equal("save one", early.Read<ServerHello>().SaveName);
            WaitForLog("client 1 connected");

            _server.Announce(new ServerHello("test server", "save two", 1));

            Assert.Equal("save two", early.Read<ServerHello>().SaveName);
            using TestClient late = TestClient.Open(_server.Port);
            Assert.Equal("save two", late.Read<ServerHello>().SaveName);
        }

        [Fact]
        public void PagesAreServedAndTheConnectionEnds()
        {
            string get = Request("GET / HTTP/1.1", Host);
            string head = Request("HEAD /index.html HTTP/1.1", Host);

            Assert.StartsWith("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 12\r\n", get);
            Assert.EndsWith("\r\n\r\n<p>index</p>", get);
            Assert.Equal(get.Substring(0, get.Length - "<p>index</p>".Length), head);
        }

        [Fact]
        public void RefusalsCarryTheirStatusAndAreLogged()
        {
            Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", Request("GET / HTTP/1.1", "Host: evil.example"));
            Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", Request("GET /nothing.html HTTP/1.1", Host));
            Assert.StartsWith("HTTP/1.1 405 Method Not Allowed\r\n", Request("POST / HTTP/1.1", Host));
            Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", Request("\u0001\u0002 binary garbage", Host));
            Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", Request("GET /" + new string('a', 9000) + " HTTP/1.1", Host));

            WaitForLog("request refused 403 host not allowed");
            WaitForLog("request refused 404 no file /nothing.html");
            WaitForLog("request refused 405 method not allowed");
            WaitForLog("request refused 400 malformed request");
            WaitForLog("request refused 400 request head over 8192 bytes");
        }

        [Theory]
        [InlineData("http://evil.example")]
        [InlineData(null)]
        public void ASocketFromAnotherOriginOrNoneIsRefusedWith403(string? origin)
        {
            using var client = new TestClient(_server.Port);

            Assert.Equal("HTTP/1.1 403 Forbidden", client.Upgrade(origin));
            client.AssertServerEndsTheConnection();
        }

        [Fact]
        public void TheSeventeenthClientIsRefusedAndTheOthersStay()
        {
            TestClient[] clients = Enumerable.Range(0, 16).Select(_ => TestClient.Open(_server.Port)).ToArray();
            using var extra = new TestClient(_server.Port);

            Assert.Equal("HTTP/1.1 503 Service Unavailable", extra.Upgrade("http://127.0.0.1:" + _server.Port));

            _server.Publish(FleetNumber(1));
            foreach (TestClient client in clients)
            {
                client.Read<ServerHello>();
                Assert.Equal(1u, client.Read<Fleet>().Seq);
                client.Dispose();
            }
        }

        [Fact]
        public void AFleetOfSeveralHundredKilobytesArrivesWhole()
        {
            var bodies = Enumerable.Range(0, 3000)
                .Select(i => new Body("Body " + i, null, 600000, 3.5316e12, null, 70000, 21549.425, 0, 6768, "#4a7a9c", null))
                .ToArray();
            using TestClient client = TestClient.Open(_server.Port);
            client.Read<ServerHello>();
            WaitForLog("client 1 connected");

            _server.Publish(new Fleet(1, 0, 1, null, bodies, Array.Empty<Vessel>(), null));

            Assert.Equal(3000, client.Read<Fleet>().Bodies.Count);
        }

        [Fact]
        public void AThrowingLoggerDoesNotStopTheServer()
        {
            using HttpServer noisy = HttpServer.Start(new ServerOptions(0, _web, new ServerHello("noisy", null, 1), _ => throw new InvalidOperationException("logger broke")));

            using TestClient client = TestClient.Open(noisy.Port);

            Assert.Equal("noisy", client.Read<ServerHello>().Server);
        }
    }
}
