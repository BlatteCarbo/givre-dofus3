// Isolated harness-only replacement for absent GivreCore assembly. The real Engine,
// ActionSender, protobuf envelope builder and journal sources are compiled verbatim.
// This simple wire encoder/decoder is NOT a production codec or protocol attestation.
using System.Text;

namespace GivreCore.Mitm
{
    public enum Direction { ClientToServer, ServerToClient }
    public sealed partial class TcpProxyServer
    {
        public bool Replay(int connectionId, Direction direction, byte[] bytes)
            => throw new InvalidOperationException("Live proxy forbidden in offline harness.");
    }
}

namespace GivreCore.Protobuf
{
    public enum WireType { Varint = 0, Len = 2 }

    public sealed class ProtoField
    {
        public int Field { get; set; }
        public WireType Wire { get; set; }
        public ulong Varint { get; set; }
        public string? String { get; set; }
        public byte[]? Bytes { get; set; }
        public List<ProtoField>? Message { get; set; }
    }

    public static class RawProtobuf
    {
        public static byte[] Encode(IEnumerable<ProtoField> fields)
        {
            using var ms = new MemoryStream();
            foreach (var f in fields)
            {
                WriteVarint(ms, ((ulong)f.Field << 3) | (ulong)f.Wire);
                if (f.Wire == WireType.Varint)
                {
                    WriteVarint(ms, f.Varint);
                    continue;
                }
                byte[] bytes = f.Message is not null ? Encode(f.Message)
                    : f.String is not null ? Encoding.UTF8.GetBytes(f.String)
                    : f.Bytes ?? Array.Empty<byte>();
                WriteVarint(ms, (ulong)bytes.Length);
                ms.Write(bytes);
            }
            return ms.ToArray();
        }

        private static void WriteVarint(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }

        public static List<ProtoField> Parse(byte[] bytes) => Parse(bytes.AsSpan(), 0);

        private static List<ProtoField> Parse(ReadOnlySpan<byte> data, int depth)
        {
            if (depth > 20) throw new FormatException("Protobuf nesting too deep.");
            var fields = new List<ProtoField>();
            int position = 0;
            while (position < data.Length)
            {
                ulong key = ReadVarint(data, ref position);
                var wire = (WireType)(key & 7);
                if (key >> 3 is 0 or > 536870911 || wire is not (WireType.Varint or WireType.Len))
                    throw new FormatException("Invalid protobuf field.");
                var field = new ProtoField { Field = checked((int)(key >> 3)), Wire = wire };
                if (wire == WireType.Varint)
                    field.Varint = ReadVarint(data, ref position);
                else
                {
                    ulong length = ReadVarint(data, ref position);
                    if (length > (ulong)(data.Length - position)) throw new FormatException("Invalid length.");
                    field.Bytes = data.Slice(position, (int)length).ToArray();
                    position += (int)length;
                    try { field.Message = Parse(field.Bytes, depth + 1); }
                    catch (FormatException) { /* leaf is bytes rather than a nested message */ }
                }
                fields.Add(field);
            }
            return fields;
        }

        private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
        {
            ulong value = 0;
            for (int i = 0; i < 10; i++)
            {
                if (position >= data.Length) throw new FormatException("Truncated varint.");
                byte b = data[position++];
                if (i == 9 && b > 1) throw new FormatException("Oversized varint.");
                value |= (ulong)(b & 0x7f) << (i * 7);
                if ((b & 0x80) == 0) return value;
            }
            throw new FormatException("Unterminated varint.");
        }
    }
}
