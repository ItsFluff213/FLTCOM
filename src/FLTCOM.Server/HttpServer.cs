using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Server
{
    /// <summary>
    /// Serves the display and its WebSocket on 127.0.0.1. One background thread owns every socket.
    /// No member blocks the caller except Dispose, which waits up to one second for that thread.
    /// </summary>
    public sealed class HttpServer : IDisposable
    {
        private const long Never = long.MaxValue;
        private const int AcceptsPerTurn = 16;
        private const int ListenBacklog = 64;
        private static readonly byte[] WakeByte = { 0 };

        private abstract class Command
        {
            public sealed class Announce : Command
            {
                public Announce(ServerHello hello) => Hello = hello;
                public ServerHello Hello { get; }
            }

            public sealed class Send : Command
            {
                public Send(ServerMessage message, bool retain)
                {
                    Message = message;
                    Retain = retain;
                }

                public ServerMessage Message { get; }

                public bool Retain { get; }
            }

            public sealed class Stop : Command
            {
                public Stop(ByeReason reason) => Reason = reason;
                public ByeReason Reason { get; }
            }

            public sealed class Layout : Command
            {
                public Layout(CraftLayout value) => Value = value;
                public CraftLayout Value { get; }
            }

            public sealed class ForgetLayout : Command
            {
                public ForgetLayout(uint vesselId) => VesselId = vesselId;
                public uint VesselId { get; }
            }
        }

        private readonly Limits _limits = Limits.Default;
        private readonly Action<string> _log;
        private readonly Socket _listener;
        private readonly Socket _wake;
        private readonly EndPoint _wakeEndPoint;
        private readonly StaticSite _site;
        private readonly Thread _thread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly ConcurrentQueue<Command> _mail = new ConcurrentQueue<Command>();
        private Fleet? _latest;
        private int _stopRequested;

        private readonly List<Peer> _peers = new List<Peer>();
        private readonly byte[] _readBuffer = new byte[64 * 1024];
        private byte[] _helloFrame;
        private readonly Dictionary<uint, byte[]> _layoutFrames = new Dictionary<uint, byte[]>();
        private readonly Dictionary<Type, byte[]> _retainedFrames = new Dictionary<Type, byte[]>();
        private int _clientsSeen;
        private long _stopDeadline = Never;

        private HttpServer(ServerOptions options, StaticSite site, Socket listener, Socket wake)
        {
            _log = options.Log;
            _site = site;
            _listener = listener;
            _wake = wake;
            _wakeEndPoint = wake.LocalEndPoint;
            _helloFrame = Encode(options.Hello);
            Port = ((IPEndPoint)listener.LocalEndPoint).Port;
            Inbox = new Inbox();
            _thread = new Thread(Run, 1 << 20) { Name = "FLTCOM server", IsBackground = true };
        }

        /// <summary>The bound port. It differs from the requested one only when that was 0.</summary>
        public int Port { get; }

        public Inbox Inbox { get; }

        /// <summary>
        /// Reads the web root into memory, binds 127.0.0.1 and starts the server thread. A missing web root serves
        /// nothing; one that cannot be read, or holds more than 64 MB, throws the file system's exception.
        /// </summary>
        /// <exception cref="PortBusyException">Another process holds the port.</exception>
        public static HttpServer Start(ServerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            StaticSite site = StaticSite.Load(options.WebRoot, Limits.Default.MaxSiteBytes);
            Socket listener = Listen(options.Port);
            Socket? wake = null;
            try
            {
                wake = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                wake.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                var server = new HttpServer(options, site, listener, wake);
                server._thread.Start();
                return server;
            }
            catch
            {
                wake?.Close();
                listener.Close();
                throw;
            }
        }

        /// <summary>Replaces the snapshot every client should receive next. One the server has not picked up yet is dropped.</summary>
        public void Publish(Fleet fleet)
        {
            if (fleet == null)
            {
                throw new ArgumentNullException(nameof(fleet));
            }
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref _latest, fleet);
            Wake();
        }

        /// <summary>Replaces the hello sent to new clients and sends it to the ones already connected.</summary>
        public void Announce(ServerHello hello)
        {
            if (hello == null)
            {
                throw new ArgumentNullException(nameof(hello));
            }
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }
            _mail.Enqueue(new Command.Announce(hello));
            Wake();
        }

        /// <summary>Serves art made at runtime, such as a body texture or a craft model, at a path under /art/. Safe from any thread.</summary>
        public void PublishArt(string path, string contentType, byte[] body) => _site.PutArt(path, contentType, body);

        public bool HasArt(string path) => _site.HasArt(path);

        /// <summary>Sends a vessel's layout to every client now and to each client that connects later, until it is replaced or forgotten.</summary>
        public void PublishLayout(CraftLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }
            _mail.Enqueue(new Command.Layout(layout));
            Wake();
        }

        /// <summary>Stops resending a vessel's layout to clients that connect later.</summary>
        public void ForgetLayout(uint vesselId)
        {
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }
            _mail.Enqueue(new Command.ForgetLayout(vesselId));
            Wake();
        }

        /// <summary>Sends a message to every client now. A retained message also goes to each client that connects later, until a retained message of the same type replaces it.</summary>
        public void Send(ServerMessage message, bool retain)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }
            _mail.Enqueue(new Command.Send(message, retain));
            Wake();
        }

        /// <summary>
        /// Sends bye and close 1001 to every client, gives them half a second to take it, then closes everything.
        /// Returns at once. Later calls, and Publish or Announce after it, do nothing.
        /// </summary>
        public void Stop(ByeReason reason)
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            {
                return;
            }
            _mail.Enqueue(new Command.Stop(reason));
            Wake();
        }

        public void Dispose()
        {
            Stop(ByeReason.Shutdown);
            if (Thread.CurrentThread != _thread)
            {
                _thread.Join(1000);
            }
        }

        private static Socket Listen(int port)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            try
            {
                listener.ExclusiveAddressUse = true;
            }
            catch (SocketException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
            try
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
                listener.Listen(ListenBacklog);
                return listener;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse || e.SocketErrorCode == SocketError.AccessDenied)
            {
                // Windows reports AccessDenied for a port another process holds exclusively or has excluded.
                listener.Close();
                throw new PortBusyException(port);
            }
            catch
            {
                listener.Close();
                throw;
            }
        }

        private void Wake()
        {
            try
            {
                _wake.SendTo(WakeByte, _wakeEndPoint);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static byte[] Encode(ServerMessage message) => Frame.EncodeText(ProtocolJson.Write(message));

        private bool Stopping => _stopDeadline != Never;

        private void Run()
        {
            try
            {
                Log("listening on 127.0.0.1:" + Text(Port));
                var read = new List<Socket>();
                var write = new List<Socket>();
                while (!Stopping || (_peers.Count > 0 && _clock.ElapsedMilliseconds < _stopDeadline))
                {
                    Turn(read, write);
                }
                Log("stopped");
            }
            catch (Exception e)
            {
                Log("server thread failed, server stopped: " + e.GetType().Name + ": " + Printable(e.Message));
            }
            finally
            {
                Volatile.Write(ref _stopRequested, 1);
                foreach (Peer peer in _peers)
                {
                    peer.Socket.Close();
                }
                _listener.Close();
                _wake.Close();
            }
        }

        private void Turn(List<Socket> read, List<Socket> write)
        {
            read.Clear();
            write.Clear();
            read.Add(_wake);
            if (!Stopping)
            {
                read.Add(_listener);
            }
            foreach (Peer peer in _peers)
            {
                if (peer.Phase != PeerPhase.FlushingResponse)
                {
                    read.Add(peer.Socket);
                }
                if (peer.HasOutbound)
                {
                    write.Add(peer.Socket);
                }
            }
            int waitMs = Stopping ? 20 : 250;
            Socket.Select(read, write.Count > 0 ? write : null, null, waitMs * 1000);

            long now = _clock.ElapsedMilliseconds;
            if (read.Contains(_wake))
            {
                DrainWake();
            }
            while (_mail.TryDequeue(out Command? command))
            {
                Execute(command, now);
            }
            Fleet? fleet = Interlocked.Exchange(ref _latest, null);
            if (fleet != null && !Stopping)
            {
                Broadcast(fleet);
            }
            if (!Stopping && read.Contains(_listener))
            {
                AcceptPending(now);
            }
            foreach (Peer peer in _peers)
            {
                if (peer.Phase == PeerPhase.Done)
                {
                    continue;
                }
                try
                {
                    Step(peer, read.Contains(peer.Socket), now);
                }
                catch (Exception e)
                {
                    // A bug in handling one peer must cost that peer only, never the thread or the game.
                    Drop(peer, "internal error " + e.GetType().Name);
                }
            }
            _peers.RemoveAll(peer => peer.Phase == PeerPhase.Done);
        }

        private void DrainWake()
        {
            // Any local process can send here; the cap keeps a flood from starving the clients.
            for (int i = 0; i < 64; i++)
            {
                _wake.Receive(_readBuffer, 0, _readBuffer.Length, SocketFlags.None, out SocketError error);
                if (error != SocketError.Success)
                {
                    return;
                }
            }
        }

        private void Execute(Command command, long now)
        {
            switch (command)
            {
                case Command.Announce announce:
                    _helloFrame = Encode(announce.Hello);
                    foreach (Peer peer in _peers)
                    {
                        peer.WebSocket?.SendControl(_helloFrame);
                    }
                    break;
                case Command.Send send:
                    byte[] sent = Encode(send.Message);
                    if (send.Retain)
                    {
                        _retainedFrames[send.Message.GetType()] = sent;
                    }
                    foreach (Peer peer in _peers)
                    {
                        peer.WebSocket?.SendControl(sent);
                    }
                    break;
                case Command.Stop stop:
                    BeginStop(stop.Reason, now);
                    break;
                case Command.Layout layout:
                    byte[] frame = Encode(layout.Value);
                    _layoutFrames[layout.Value.VesselId] = frame;
                    foreach (Peer peer in _peers)
                    {
                        peer.WebSocket?.SendControl(frame);
                    }
                    break;
                case Command.ForgetLayout forget:
                    _layoutFrames.Remove(forget.VesselId);
                    break;
                default:
                    throw new InvalidOperationException("unknown command " + command.GetType().Name);
            }
        }

        private void BeginStop(ByeReason reason, long now)
        {
            _stopDeadline = now + _limits.StopFlushMs;
            _listener.Close();
            byte[] bye = Encode(new Bye(reason));
            foreach (Peer peer in _peers)
            {
                if (peer.Phase == PeerPhase.ReadingRequest)
                {
                    Remove(peer);
                }
                else if (peer.Phase == PeerPhase.WebSocket)
                {
                    peer.WebSocket!.SendControl(bye);
                    peer.WebSocket.Close(CloseCodes.GoingAway, "server stopping");
                    EnterClosing(peer, now);
                }
            }
        }

        private void Broadcast(Fleet fleet)
        {
            byte[] frame = Encode(fleet);
            foreach (Peer peer in _peers)
            {
                peer.WebSocket?.SendFleet(frame);
            }
        }

        private void AcceptPending(long now)
        {
            for (int i = 0; i < AcceptsPerTurn && _listener.Poll(0, SelectMode.SelectRead); i++)
            {
                Socket socket;
                try
                {
                    socket = _listener.Accept();
                    socket.Blocking = false;
                    socket.NoDelay = true;
                }
                catch (SocketException)
                {
                    continue;
                }
                if (_peers.Count >= _limits.MaxPeers)
                {
                    // Not tracked, so a connection flood cannot grow the peer list: one try at a reply, then gone.
                    Log("request refused 503 too many connections");
                    byte[] refusal = HttpResponse.Refusal(503);
                    socket.Send(refusal, 0, refusal.Length, SocketFlags.None, out _);
                    socket.Close();
                    continue;
                }
                _peers.Add(new Peer(socket, _limits.MaxRequestBytes, now + _limits.RequestTimeoutMs));
            }
        }

        private void Step(Peer peer, bool readable, long now)
        {
            if (readable)
            {
                Read(peer, now);
            }
            if (peer.Phase != PeerPhase.Done)
            {
                Flush(peer, now);
            }
            if (peer.Phase != PeerPhase.Done && now >= peer.Deadline)
            {
                Lose(peer, "no write progress for " + Text(_limits.WriteStallMs) + " ms");
            }
        }

        private void Read(Peer peer, long now)
        {
            bool readingRequest = peer.Phase == PeerPhase.ReadingRequest;
            byte[] buffer = readingRequest ? peer.Request : _readBuffer;
            int offset = readingRequest ? peer.RequestCount : 0;
            int received = peer.Socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None, out SocketError error);
            if (error == SocketError.WouldBlock)
            {
                return;
            }
            if (error != SocketError.Success || received == 0)
            {
                Lose(peer, error == SocketError.Success ? "connection lost" : "socket error " + error);
                return;
            }
            if (readingRequest)
            {
                peer.RequestCount += received;
                ReadRequest(peer, now);
            }
            else if (peer.Phase == PeerPhase.WebSocket)
            {
                ReadFrames(peer, _readBuffer, 0, received, now);
            }
        }

        private void ReadRequest(Peer peer, long now)
        {
            switch (HttpRequest.TryParse(peer.Request, peer.RequestCount, _limits, out HttpRequest? request, out int headLength))
            {
                case ParseStatus.NeedMore:
                    return;
                case ParseStatus.TooLarge:
                    Refuse(peer, 400, "request head over " + Text(_limits.MaxRequestBytes) + " bytes", now);
                    return;
                case ParseStatus.Malformed:
                    Refuse(peer, 400, "malformed request", now);
                    return;
            }
            bool hasRoomForClient = _peers.FindAll(other => other.WebSocket != null).Count < _limits.MaxClients;
            switch (Router.Decide(request!, Port, _site, hasRoomForClient))
            {
                case Route.Upgrade upgrade:
                    byte[] early = new byte[peer.RequestCount - headLength];
                    Buffer.BlockCopy(peer.Request, headLength, early, 0, early.Length);
                    var connection = new WebSocketConnection(_limits);
                    connection.SendControl(HttpResponse.SwitchingProtocols(upgrade.AcceptKey));
                    connection.SendControl(_helloFrame);
                    foreach (byte[] layoutFrame in _layoutFrames.Values)
                    {
                        connection.SendControl(layoutFrame);
                    }
                    foreach (byte[] retained in _retainedFrames.Values)
                    {
                        connection.SendControl(retained);
                    }
                    peer.BecomeWebSocket(connection, ++_clientsSeen, new InboxShare(_limits.InboxBudgetPerClient));
                    Log("client " + Text(peer.ClientNumber) + " connected");
                    ReadFrames(peer, early, 0, early.Length, now);
                    break;
                case Route.File file:
                    peer.Respond(now + _limits.WriteStallMs, file.HeadOnly ? new[] { file.Value.Head } : new[] { file.Value.Head, file.Value.Body });
                    break;
                case Route.Refuse refuse:
                    Refuse(peer, refuse.Status, refuse.Reason, now);
                    break;
                default:
                    throw new InvalidOperationException("unknown route");
            }
        }

        private void Refuse(Peer peer, int status, string reason, long now)
        {
            Log("request refused " + Text(status) + " " + reason);
            peer.Respond(now + _limits.WriteStallMs, HttpResponse.Refusal(status));
        }

        private void ReadFrames(Peer peer, byte[] buffer, int offset, int count, long now)
        {
            WebSocketConnection connection = peer.WebSocket!;
            connection.Receive(buffer, offset, count);
            for (ConnEvent e = connection.TakeEvent(); e.Kind == ConnEventKind.Text && connection.ServerCloseReason == null; e = connection.TakeEvent())
            {
                HandleText(peer, e.Text);
            }
            if (connection.State != ConnState.Open)
            {
                EnterClosing(peer, now);
            }
            else if (connection.QueuedControlBytes > _limits.MaxQueuedControlBytes)
            {
                Drop(peer, "not reading its replies");
            }
        }

        private void HandleText(Peer peer, string text)
        {
            WebSocketConnection connection = peer.WebSocket!;
            ClientMessage message;
            try
            {
                message = ProtocolJson.ReadClientMessage(text);
            }
            catch (ProtocolVersionException)
            {
                connection.SendControl(Encode(new Bye(ByeReason.ProtocolMismatch)));
                connection.Close(CloseCodes.Normal, "protocol version mismatch");
                return;
            }
            catch (ProtocolException)
            {
                connection.Close(CloseCodes.PolicyViolation, "not a valid client message");
                return;
            }
            bool forTheGame = message.Match(
                hello => true,
                ping =>
                {
                    connection.SendControl(Encode(new Pong(ping.Nonce)));
                    return false;
                },
                brief => true,
                approve => true,
                scrub => true,
                resume => true,
                answer => true,
                groupEdit => true);
            long cost = _limits.InboxCostPerMessage + 2L * text.Length;
            if (forTheGame && !Inbox.TryPost(message, peer.InboxShare!, cost))
            {
                connection.Close(CloseCodes.PolicyViolation, "its inbox share is full");
            }
        }

        private void EnterClosing(Peer peer, long now)
        {
            WebSocketConnection connection = peer.WebSocket!;
            string line = "client " + Text(peer.ClientNumber) + " closed " + Text(connection.CloseCode);
            Log(connection.ServerCloseReason == null ? line : line + " by server (" + connection.ServerCloseReason + ")");
            peer.Phase = PeerPhase.WebSocketClosing;
            peer.Deadline = now + _limits.ClosingMs;
        }

        private void Flush(Peer peer, long now)
        {
            bool progressed = false;
            while (peer.HasOutbound)
            {
                ArraySegment<byte> next = peer.PeekOutbound();
                int sent = peer.Socket.Send(next.Array, next.Offset, next.Count, SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock)
                {
                    break;
                }
                if (error != SocketError.Success)
                {
                    Lose(peer, "socket error " + error);
                    return;
                }
                if (sent == 0)
                {
                    break;
                }
                peer.Advance(sent);
                progressed = true;
            }
            switch (peer.Phase)
            {
                case PeerPhase.WebSocket:
                    if (!peer.HasOutbound)
                    {
                        peer.Deadline = Never;
                    }
                    else if (progressed || peer.Deadline == Never)
                    {
                        peer.Deadline = now + _limits.WriteStallMs;
                    }
                    break;
                case PeerPhase.FlushingResponse:
                case PeerPhase.WebSocketClosing:
                    if (!peer.HasOutbound)
                    {
                        Linger(peer, now);
                    }
                    break;
            }
        }

        /// <summary>
        /// Waits for the client to end its side. Closing at once could reset the connection and discard the last
        /// bytes we sent while the client still has data on the way. An HTTP response is not half-closed first:
        /// on Windows loopback, a FIN right behind a large body sometimes resets it before the client has read
        /// it all, and the client closes by itself once it has Content-Length bytes.
        /// </summary>
        private void Linger(Peer peer, long now)
        {
            try
            {
                if (peer.Phase == PeerPhase.WebSocketClosing)
                {
                    peer.Socket.Shutdown(SocketShutdown.Send);
                }
            }
            catch (SocketException)
            {
                Remove(peer);
                return;
            }
            peer.Phase = PeerPhase.Lingering;
            peer.Deadline = now + _limits.ClosingMs;
        }

        private void Lose(Peer peer, string reason)
        {
            if (peer.Phase == PeerPhase.WebSocket)
            {
                Drop(peer, reason);
            }
            else
            {
                Remove(peer);
            }
        }

        private void Drop(Peer peer, string reason)
        {
            if (peer.WebSocket != null)
            {
                Log("client " + Text(peer.ClientNumber) + " dropped (" + reason + ")");
            }
            Remove(peer);
        }

        private static void Remove(Peer peer)
        {
            peer.Socket.Close();
            peer.Phase = PeerPhase.Done;
        }

        private void Log(string line)
        {
            try
            {
                _log(line);
            }
            catch (Exception)
            {
                // The caller's logger runs on the server thread; if it throws there is nowhere left to report it.
            }
        }

        private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        internal static string Printable(string text)
        {
            var printable = new StringBuilder(Math.Min(text.Length, 80));
            for (int i = 0; i < text.Length && i < 80; i++)
            {
                printable.Append(text[i] >= ' ' && text[i] <= '~' ? text[i] : '?');
            }
            return printable.ToString();
        }
    }
}
