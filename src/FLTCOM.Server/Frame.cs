using System;
using System.Security.Cryptography;
using System.Text;

namespace FLTCOM.Server
{
    internal sealed class Limits
    {
        public static readonly Limits Default = new Limits();

        public int MaxMessageBytes { get; } = 1 << 20;
        public int MaxQueuedControlBytes { get; } = 1 << 20;
        public int MaxRequestBytes { get; } = 8 * 1024;
        public int MaxRequestHeaders { get; } = 64;
        public int MaxClients { get; } = 16;
        public int MaxPeers { get; } = 48;
        public long InboxBudgetPerClient { get; } = 512 * 1024;
        public int InboxCostPerMessage { get; } = 256;
        public long MaxSiteBytes { get; } = 64L << 20;
        public int RequestTimeoutMs { get; } = 5000;
        public int WriteStallMs { get; } = 10000;
        public int ClosingMs { get; } = 2000;
        public int StopFlushMs { get; } = 500;
    }

    internal enum Opcode : byte
    {
        Continuation = 0,
        Text = 1,
        Binary = 2,
        Close = 8,
        Ping = 9,
        Pong = 10,
    }

    internal static class CloseCodes
    {
        public const ushort Normal = 1000;
        public const ushort GoingAway = 1001;
        public const ushort ProtocolError = 1002;
        public const ushort UnsupportedData = 1003;
        /// <summary>Stands for a close frame with no code. Never sent on the wire.</summary>
        public const ushort NoStatus = 1005;
        public const ushort InvalidPayload = 1007;
        public const ushort PolicyViolation = 1008;
        public const ushort MessageTooBig = 1009;
    }

    internal enum DecodeStatus
    {
        NeedMore,
        Ok,
        ProtocolError,
        TooBig,
    }

    internal readonly struct FrameHeader
    {
        public FrameHeader(bool fin, Opcode opcode, bool masked, uint maskKey, long payloadLength, int headerLength)
        {
            Fin = fin;
            Opcode = opcode;
            Masked = masked;
            MaskKey = maskKey;
            PayloadLength = payloadLength;
            HeaderLength = headerLength;
        }

        public bool Fin { get; }
        public Opcode Opcode { get; }
        public bool Masked { get; }
        public uint MaskKey { get; }
        public long PayloadLength { get; }
        public int HeaderLength { get; }
        public bool IsControl => ((byte)Opcode & 0x08) != 0;
    }

    internal static class Frame
    {
        public const int MaxHeaderLength = 14;
        public const int MaxControlPayload = 125;

        private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>
        /// Reads a frame header from buffer[offset..offset+count). An error is reported as soon as the bytes that
        /// prove it are present, so an oversize frame is refused before its mask key or payload arrives.
        /// maxPayload applies to data frames; a control frame is limited to 125 bytes by the protocol.
        /// </summary>
        public static DecodeStatus TryDecodeHeader(byte[] buffer, int offset, int count, long maxPayload, out FrameHeader header)
        {
            header = default;
            if (count < 1)
            {
                return DecodeStatus.NeedMore;
            }
            byte first = buffer[offset];
            var opcode = (Opcode)(first & 0x0F);
            bool fin = (first & 0x80) != 0;
            bool control = (first & 0x08) != 0;
            bool reservedBits = (first & 0x70) != 0;
            if (reservedBits || !IsKnown(opcode) || (control && !fin))
            {
                return DecodeStatus.ProtocolError;
            }
            if (count < 2)
            {
                return DecodeStatus.NeedMore;
            }
            byte second = buffer[offset + 1];
            bool masked = (second & 0x80) != 0;
            int shortLength = second & 0x7F;
            if (control && shortLength > MaxControlPayload)
            {
                return DecodeStatus.ProtocolError;
            }
            int lengthBytes = shortLength == 126 ? 2 : shortLength == 127 ? 8 : 0;
            if (count < 2 + lengthBytes)
            {
                return DecodeStatus.NeedMore;
            }
            ulong length = (ulong)shortLength;
            if (lengthBytes > 0)
            {
                length = 0;
                for (int i = 0; i < lengthBytes; i++)
                {
                    length = (length << 8) | buffer[offset + 2 + i];
                }
                ulong smallestForThisForm = lengthBytes == 2 ? 126UL : 65536UL;
                if (length > long.MaxValue || length < smallestForThisForm)
                {
                    return DecodeStatus.ProtocolError;
                }
            }
            if (!control && length > (ulong)Math.Max(maxPayload, 0))
            {
                return DecodeStatus.TooBig;
            }
            int maskOffset = 2 + lengthBytes;
            int headerLength = maskOffset + (masked ? 4 : 0);
            if (count < headerLength)
            {
                return DecodeStatus.NeedMore;
            }
            uint maskKey = 0;
            if (masked)
            {
                int at = offset + maskOffset;
                maskKey = ((uint)buffer[at] << 24) | ((uint)buffer[at + 1] << 16) | ((uint)buffer[at + 2] << 8) | buffer[at + 3];
            }
            header = new FrameHeader(fin, opcode, masked, maskKey, (long)length, headerLength);
            return DecodeStatus.Ok;
        }

        /// <summary>streamOffset is how many payload bytes of this frame came before buffer[offset].</summary>
        public static void Unmask(byte[] buffer, int offset, int count, uint maskKey, long streamOffset)
        {
            int phase = (int)(streamOffset & 3);
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] ^= (byte)(maskKey >> (24 - 8 * ((phase + i) & 3)));
            }
        }

        public static byte[] EncodeText(string text)
        {
            byte[] frame = NewFrame(Opcode.Text, Encoding.UTF8.GetByteCount(text), out int payloadOffset);
            Encoding.UTF8.GetBytes(text, 0, text.Length, frame, payloadOffset);
            return frame;
        }

        public static byte[] EncodePong(byte[] payload, int offset, int count)
        {
            byte[] frame = NewFrame(Opcode.Pong, count, out int payloadOffset);
            Buffer.BlockCopy(payload, offset, frame, payloadOffset, count);
            return frame;
        }

        public static byte[] EncodeClose(ushort code)
        {
            if (code == CloseCodes.NoStatus)
            {
                return NewFrame(Opcode.Close, 0, out _);
            }
            byte[] frame = NewFrame(Opcode.Close, 2, out int payloadOffset);
            frame[payloadOffset] = (byte)(code >> 8);
            frame[payloadOffset + 1] = (byte)code;
            return frame;
        }

        public static string AcceptKey(string secWebSocketKey)
        {
#pragma warning disable CA5350 // RFC 6455 fixes SHA-1 for the handshake; it is not used for security here.
            using (var sha1 = SHA1.Create())
#pragma warning restore CA5350
            {
                return Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(secWebSocketKey + HandshakeGuid)));
            }
        }

        private static bool IsKnown(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.Continuation:
                case Opcode.Text:
                case Opcode.Binary:
                case Opcode.Close:
                case Opcode.Ping:
                case Opcode.Pong:
                    return true;
                default:
                    return false;
            }
        }

        private static byte[] NewFrame(Opcode opcode, int payloadLength, out int payloadOffset)
        {
            payloadOffset = payloadLength < 126 ? 2 : payloadLength <= ushort.MaxValue ? 4 : 10;
            var frame = new byte[payloadOffset + payloadLength];
            frame[0] = (byte)(0x80 | (byte)opcode);
            if (payloadOffset == 2)
            {
                frame[1] = (byte)payloadLength;
            }
            else if (payloadOffset == 4)
            {
                frame[1] = 126;
                frame[2] = (byte)(payloadLength >> 8);
                frame[3] = (byte)payloadLength;
            }
            else
            {
                frame[1] = 127;
                for (int i = 0; i < 4; i++)
                {
                    frame[6 + i] = (byte)(payloadLength >> (24 - 8 * i));
                }
            }
            return frame;
        }
    }

    internal static class Utf8
    {
        private static readonly UTF8Encoding Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public static bool TryDecode(byte[] bytes, int offset, int count, out string text)
        {
            try
            {
                text = Strict.GetString(bytes, offset, count);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = string.Empty;
                return false;
            }
        }
    }
}
