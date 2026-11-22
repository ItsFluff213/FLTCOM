using System;
using System.Net.Sockets;

namespace FLTCOM.Server
{
    internal enum PeerPhase
    {
        ReadingRequest,
        FlushingResponse,
        WebSocket,
        WebSocketClosing,
        Lingering,
        Done,
    }

    /// <summary>One accepted socket: a request being read, then a response being flushed or a WebSocket.</summary>
    internal sealed class Peer
    {
        private byte[][] _response = Array.Empty<byte[]>();
        private int _responsePart;
        private int _responseOffset;

        public Peer(Socket socket, int maxRequestBytes, long deadline)
        {
            Socket = socket;
            Request = new byte[maxRequestBytes];
            Deadline = deadline;
        }

        public Socket Socket { get; }
        public PeerPhase Phase { get; set; } = PeerPhase.ReadingRequest;
        public long Deadline { get; set; }
        public byte[] Request { get; private set; }
        public int RequestCount { get; set; }
        public WebSocketConnection? WebSocket { get; private set; }
        public int ClientNumber { get; private set; }
        public InboxShare? InboxShare { get; private set; }

        public bool HasOutbound => WebSocket != null ? WebSocket.HasOutbound : _responsePart < _response.Length;

        public void Respond(long deadline, params byte[][] parts)
        {
            Request = Array.Empty<byte>();
            _response = parts;
            Phase = PeerPhase.FlushingResponse;
            Deadline = deadline;
        }

        public void BecomeWebSocket(WebSocketConnection connection, int clientNumber, InboxShare inboxShare)
        {
            InboxShare = inboxShare;
            Request = Array.Empty<byte>();
            WebSocket = connection;
            ClientNumber = clientNumber;
            Phase = PeerPhase.WebSocket;
            Deadline = long.MaxValue;
        }

        public ArraySegment<byte> PeekOutbound()
        {
            if (WebSocket != null)
            {
                return WebSocket.PeekOutbound();
            }
            byte[] part = _response[_responsePart];
            return new ArraySegment<byte>(part, _responseOffset, part.Length - _responseOffset);
        }

        public void Advance(int sent)
        {
            if (WebSocket != null)
            {
                WebSocket.Advance(sent);
                return;
            }
            _responseOffset += sent;
            while (_responsePart < _response.Length && _responseOffset == _response[_responsePart].Length)
            {
                _responsePart++;
                _responseOffset = 0;
            }
        }
    }
}
