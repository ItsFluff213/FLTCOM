using System;
using System.Collections.Generic;

namespace FLTCOM.Server
{
    /// <summary>Closing: our close frame is queued and inbound bytes are discarded. Closed: it has been handed over in full.</summary>
    internal enum ConnState
    {
        Open,
        Closing,
        Closed,
    }

    internal enum ConnEventKind
    {
        None,
        Text,
    }

    internal readonly struct ConnEvent
    {
        public ConnEvent(string text)
        {
            Kind = ConnEventKind.Text;
            Text = text;
        }

        public ConnEventKind Kind { get; }
        public string Text { get; }
    }

    /// <summary>
    /// One client's WebSocket protocol with no socket: bytes in through Receive, bytes out through
    /// PeekOutbound and Advance. Only the server thread calls it.
    /// </summary>
    internal sealed class WebSocketConnection
    {
        private const int IdleMessageBufferBytes = 64 * 1024;
        private const int ControlBatchBytes = 64 * 1024;

        private enum ReadPhase
        {
            Header,
            Payload,
        }

        private readonly Limits _limits;
        private readonly Queue<string> _texts = new Queue<string>();

        private ReadPhase _phase = ReadPhase.Header;
        private readonly byte[] _head = new byte[Frame.MaxHeaderLength];
        private int _headCount;
        private FrameHeader _frame;
        private long _frameRead;
        private readonly byte[] _controlPayload = new byte[Frame.MaxControlPayload];
        private byte[] _message = Array.Empty<byte>();
        private int _messageCount;
        private bool _messageOpen;

        private readonly Queue<byte[]> _control = new Queue<byte[]>();
        private byte[]? _fleet;
        private byte[]? _inFlight;
        private int _inFlightOffset;

        public WebSocketConnection(Limits limits)
        {
            _limits = limits;
        }

        public ConnState State { get; private set; } = ConnState.Open;

        /// <summary>Valid once State has left Open. NoStatus when the client's close frame carried no code.</summary>
        public ushort CloseCode { get; private set; }

        /// <summary>Why the server closed, or null when the client started the close.</summary>
        public string? ServerCloseReason { get; private set; }

        public int QueuedControlBytes { get; private set; }

        public bool HasOutbound => _inFlight != null || _control.Count > 0 || _fleet != null;

        /// <summary>Never throws, whatever the bytes are. A violation queues a close frame and discards the rest.</summary>
        public void Receive(byte[] data, int offset, int count)
        {
            while (count > 0 && State == ConnState.Open)
            {
                if (_phase == ReadPhase.Header)
                {
                    _head[_headCount++] = data[offset++];
                    count--;
                    ReadHeader();
                    continue;
                }
                int take = (int)Math.Min(count, _frame.PayloadLength - _frameRead);
                byte[] target = _controlPayload;
                int targetOffset = (int)_frameRead;
                if (!_frame.IsControl)
                {
                    GrowMessage(_messageCount + take);
                    target = _message;
                    targetOffset = _messageCount;
                    _messageCount += take;
                }
                Buffer.BlockCopy(data, offset, target, targetOffset, take);
                Frame.Unmask(target, targetOffset, take, _frame.MaskKey, _frameRead);
                _frameRead += take;
                offset += take;
                count -= take;
                if (_frameRead == _frame.PayloadLength)
                {
                    EndFrame();
                }
            }
        }

        public ConnEvent TakeEvent() => _texts.Count > 0 ? new ConnEvent(_texts.Dequeue()) : default;

        /// <summary>Queues bytes every client may share. The array must never change afterwards.</summary>
        public void SendControl(byte[] encodedFrame)
        {
            if (State != ConnState.Open)
            {
                return;
            }
            Enqueue(encodedFrame);
        }

        /// <summary>Replaces a fleet frame that has not started to go out, so at most one waits.</summary>
        public void SendFleet(byte[] encodedFrame)
        {
            if (State != ConnState.Open)
            {
                return;
            }
            _fleet = encodedFrame;
        }

        public void Close(ushort code, string reason) => BeginClose(code, reason);

        public ArraySegment<byte> PeekOutbound()
        {
            if (_inFlight == null)
            {
                if (_control.Count > 0)
                {
                    _inFlight = TakeControlBatch();
                }
                else if (_fleet != null)
                {
                    _inFlight = _fleet;
                    _fleet = null;
                }
                else
                {
                    return default;
                }
            }
            return new ArraySegment<byte>(_inFlight, _inFlightOffset, _inFlight.Length - _inFlightOffset);
        }

        /// <summary>
        /// Joins queued control frames into one send of up to 64 KB. A client that sends thousands of tiny pings
        /// per read would otherwise cost one socket call per two-byte pong and starve every other client.
        /// </summary>
        private byte[] TakeControlBatch()
        {
            byte[] first = _control.Dequeue();
            QueuedControlBytes -= ControlCost(first);
            int length = first.Length;
            foreach (byte[] frame in _control)
            {
                if (length + frame.Length > ControlBatchBytes)
                {
                    break;
                }
                length += frame.Length;
            }
            if (length == first.Length)
            {
                return first;
            }
            var batch = new byte[length];
            Buffer.BlockCopy(first, 0, batch, 0, first.Length);
            for (int at = first.Length; at < length;)
            {
                byte[] frame = _control.Dequeue();
                QueuedControlBytes -= ControlCost(frame);
                Buffer.BlockCopy(frame, 0, batch, at, frame.Length);
                at += frame.Length;
            }
            return batch;
        }

        public void Advance(int sent)
        {
            if (_inFlight == null)
            {
                return;
            }
            _inFlightOffset += sent;
            if (_inFlightOffset < _inFlight.Length)
            {
                return;
            }
            _inFlight = null;
            _inFlightOffset = 0;
            if (State == ConnState.Closing && !HasOutbound)
            {
                State = ConnState.Closed;
            }
        }

        private void ReadHeader()
        {
            long room = _limits.MaxMessageBytes - _messageCount;
            switch (Frame.TryDecodeHeader(_head, 0, _headCount, room, out _frame))
            {
                case DecodeStatus.NeedMore:
                    return;
                case DecodeStatus.ProtocolError:
                    BeginClose(CloseCodes.ProtocolError, "malformed frame header");
                    return;
                case DecodeStatus.TooBig:
                    BeginClose(CloseCodes.MessageTooBig, "message over " + _limits.MaxMessageBytes + " bytes");
                    return;
            }
            _headCount = 0;
            if (!_frame.Masked)
            {
                BeginClose(CloseCodes.ProtocolError, "unmasked frame");
                return;
            }
            bool startsMessage = _frame.Opcode == Opcode.Text || _frame.Opcode == Opcode.Binary;
            if (startsMessage && _messageOpen)
            {
                BeginClose(CloseCodes.ProtocolError, "new message inside a fragmented one");
                return;
            }
            if (_frame.Opcode == Opcode.Continuation && !_messageOpen)
            {
                BeginClose(CloseCodes.ProtocolError, "continuation with no message open");
                return;
            }
            if (_frame.Opcode == Opcode.Binary)
            {
                BeginClose(CloseCodes.UnsupportedData, "binary message");
                return;
            }
            _frameRead = 0;
            if (_frame.PayloadLength == 0)
            {
                EndFrame();
            }
            else
            {
                _phase = ReadPhase.Payload;
            }
        }

        private void EndFrame()
        {
            _phase = ReadPhase.Header;
            switch (_frame.Opcode)
            {
                case Opcode.Ping:
                    Enqueue(Frame.EncodePong(_controlPayload, 0, (int)_frame.PayloadLength));
                    return;
                case Opcode.Pong:
                    return;
                case Opcode.Close:
                    EndCloseFrame((int)_frame.PayloadLength);
                    return;
            }
            _messageOpen = !_frame.Fin;
            if (_messageOpen)
            {
                return;
            }
            bool valid = Utf8.TryDecode(_message, 0, _messageCount, out string text);
            _messageCount = 0;
            if (_message.Length > IdleMessageBufferBytes)
            {
                _message = Array.Empty<byte>();
            }
            if (valid)
            {
                _texts.Enqueue(text);
            }
            else
            {
                BeginClose(CloseCodes.InvalidPayload, "text is not UTF-8");
            }
        }

        private void EndCloseFrame(int length)
        {
            if (length == 0)
            {
                BeginClose(CloseCodes.NoStatus, null);
                return;
            }
            ushort code = length >= 2 ? (ushort)((_controlPayload[0] << 8) | _controlPayload[1]) : (ushort)0;
            if (!MayAppearInCloseFrame(code))
            {
                BeginClose(CloseCodes.ProtocolError, "malformed close frame");
            }
            else if (!Utf8.TryDecode(_controlPayload, 2, length - 2, out _))
            {
                BeginClose(CloseCodes.InvalidPayload, "close reason is not UTF-8");
            }
            else
            {
                BeginClose(code, null);
            }
        }

        private static bool MayAppearInCloseFrame(ushort code) =>
            (code >= 1000 && code <= 1003) || (code >= 1007 && code <= 1011) || (code >= 3000 && code <= 4999);

        private void BeginClose(ushort code, string? serverReason)
        {
            if (State != ConnState.Open)
            {
                return;
            }
            CloseCode = code;
            ServerCloseReason = serverReason;
            _fleet = null;
            _message = Array.Empty<byte>();
            Enqueue(Frame.EncodeClose(code));
            State = ConnState.Closing;
        }

        /// <summary>A queued frame holds an array and a queue slot besides its bytes, so an empty pong is mostly overhead.</summary>
        private static int ControlCost(byte[] frame) => frame.Length + 64;

        private void Enqueue(byte[] bytes)
        {
            _control.Enqueue(bytes);
            QueuedControlBytes += ControlCost(bytes);
        }

        private void GrowMessage(int needed)
        {
            if (needed <= _message.Length)
            {
                return;
            }
            int size = Math.Max(_message.Length, 4096);
            while (size < needed)
            {
                size *= 2;
            }
            Array.Resize(ref _message, Math.Min(size, _limits.MaxMessageBytes));
        }
    }
}
