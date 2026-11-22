using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FLTCOM.Server.Tests
{
    internal enum LengthForm
    {
        Shortest,
        Force16,
        Force64,
    }

    internal sealed record ServerFrame(Opcode Opcode, byte[] Payload)
    {
        public const int CloseCodeAbsent = -1;

        public string Text => Encoding.UTF8.GetString(Payload);

        public int CloseCode => Payload.Length >= 2 ? (Payload[0] << 8) | Payload[1] : CloseCodeAbsent;
    }

    /// <summary>Builds the frames a client would send, including ones no honest client sends.</summary>
    internal static class ClientFrame
    {
        private static readonly byte[] Mask = { 0x37, 0xFA, 0x21, 0x3D };

        public static byte[] Text(string text, bool fin = true) => Build(Opcode.Text, Encoding.UTF8.GetBytes(text), fin);

        public static byte[] Close(ushort code) => Build(Opcode.Close, new[] { (byte)(code >> 8), (byte)code });

        public static byte[] Build(Opcode opcode, byte[] payload, bool fin = true, bool masked = true, int rsv = 0, LengthForm form = LengthForm.Shortest)
        {
            var bytes = new MemoryStream();
            bytes.Write(Header(opcode, payload.LongLength, fin, masked, rsv, form));
            for (int i = 0; i < payload.Length; i++)
            {
                bytes.WriteByte(masked ? (byte)(payload[i] ^ Mask[i & 3]) : payload[i]);
            }
            return bytes.ToArray();
        }

        public static byte[] Header(Opcode opcode, long declaredLength, bool fin = true, bool masked = true, int rsv = 0, LengthForm form = LengthForm.Shortest)
        {
            var bytes = new MemoryStream();
            bytes.WriteByte((byte)((fin ? 0x80 : 0) | (rsv << 4) | (byte)opcode));
            int maskBit = masked ? 0x80 : 0;
            if (form == LengthForm.Shortest && declaredLength < 126)
            {
                bytes.WriteByte((byte)(maskBit | (int)declaredLength));
            }
            else if (form == LengthForm.Force16 || (form == LengthForm.Shortest && declaredLength <= ushort.MaxValue))
            {
                bytes.WriteByte((byte)(maskBit | 126));
                bytes.WriteByte((byte)(declaredLength >> 8));
                bytes.WriteByte((byte)declaredLength);
            }
            else
            {
                bytes.WriteByte((byte)(maskBit | 127));
                for (int shift = 56; shift >= 0; shift -= 8)
                {
                    bytes.WriteByte((byte)(declaredLength >> shift));
                }
            }
            if (masked)
            {
                bytes.Write(Mask);
            }
            return bytes.ToArray();
        }
    }

    internal static class ServerFrames
    {
        /// <summary>Reads one unmasked frame from the start of bytes, or returns null when it is incomplete.</summary>
        public static ServerFrame? TryRead(byte[] bytes, int count, out int consumed)
        {
            consumed = 0;
            if (Frame.TryDecodeHeader(bytes, 0, count, long.MaxValue, out FrameHeader header) != DecodeStatus.Ok
                || count < header.HeaderLength + header.PayloadLength)
            {
                return null;
            }
            if (header.Masked || !header.Fin)
            {
                throw new InvalidDataException("the server sent a masked or fragmented frame");
            }
            consumed = header.HeaderLength + (int)header.PayloadLength;
            return new ServerFrame(header.Opcode, bytes.AsSpan(header.HeaderLength, (int)header.PayloadLength).ToArray());
        }

        public static List<ServerFrame> ReadAll(byte[] bytes)
        {
            var frames = new List<ServerFrame>();
            int offset = 0;
            while (offset < bytes.Length)
            {
                byte[] rest = bytes.AsSpan(offset).ToArray();
                frames.Add(TryRead(rest, rest.Length, out int consumed) ?? throw new InvalidDataException("trailing partial frame"));
                offset += consumed;
            }
            return frames;
        }
    }
}
