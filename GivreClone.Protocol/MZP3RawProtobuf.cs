using System.Buffers.Binary;
using System.Text;

namespace GivreClone.Protocol;

/// <summary>Neutral protobuf wire types; groups (3/4) are deliberately unsupported.</summary>
public enum MZP3WireType { Varint = 0, Fixed64 = 1, LengthDelimited = 2, Fixed32 = 5 }

public enum MZP3WireError
{
    None, TooLarge, TooManyFields, InvalidFieldNumber, InvalidWireType,
    TruncatedVarint, OverflowVarint, NonCanonicalVarint, TruncatedValue,
    FieldTooLarge, InvalidLimits, DepthExceeded, TrailingBytes
}

/// <summary>
/// Safety budgets for UNTRUSTED input. Do not interpret as Dofus framing proof.
/// MaxDepth only applies when explicitly parsing an embedded message; len fields
/// are opaque by default (protobuf bytes/string/submessages share wire type 2).
/// </summary>
public readonly record struct MZP3Limits(
    int MaxMessageBytes = 131072, int MaxFieldBytes = 65536,
    int MaxFields = 2048, int MaxDepth = 12)
{
    // Explicit positional construction is essential for value types: default(T)
    // and `new T()` may zero-initialize instead of invoking optional parameters.
    public static MZP3Limits Default => new(131072, 65536, 2048, 12);
    public bool IsValid => MaxMessageBytes is >= 1 and <= 1048576 &&
                           MaxFieldBytes is >= 1 and <= 1048576 &&
                           MaxFields is >= 1 and <= 16384 &&
                           MaxDepth is >= 1 and <= 64;
}

public sealed class MZP3FormatException(MZP3WireError error, int offset)
    : FormatException($"Protobuf wire {error} at byte offset {offset}.")
{
    public MZP3WireError Error { get; } = error;
    public int Offset { get; } = offset;
}

/// <summary>
/// Immutable wire field. RawEncoded contains EXACT original field bytes, including
/// unknown wire fields and repeated order; no speculative UTF-8 or message parse.
/// A decoded field is re-emitted byte-for-byte, even for non-canonical varint
/// encodings when AllowNonCanonicalValues was explicitly selected.
/// </summary>
public sealed class MZP3WireField
{
    private readonly byte[] _encoded;
    private readonly byte[] _body;

    internal MZP3WireField(int number, MZP3WireType wire, ulong? value,
        byte[] body, byte[] encoded)
    {
        Number = number;
        WireType = wire;
        UnsignedValue = value;
        _body = body;
        _encoded = encoded;
    }

    public int Number { get; }
    public MZP3WireType WireType { get; }
    public ulong? UnsignedValue { get; }
    public uint? Fixed32 => WireType == MZP3WireType.Fixed32 ? BinaryPrimitives.ReadUInt32LittleEndian(_body) : null;
    public ulong? Fixed64 => WireType == MZP3WireType.Fixed64 ? BinaryPrimitives.ReadUInt64LittleEndian(_body) : null;
    public int DataLength => _body.Length;
    public byte[] GetData() => (byte[])_body.Clone();
    public byte[] GetEncoded() => (byte[])_encoded.Clone();
    internal ReadOnlySpan<byte> EncodedSpan => _encoded;

    public bool TryGetStrictUtf8(out string? text)
    {
        text = null;
        if (WireType != MZP3WireType.LengthDelimited) return false;
        try
        {
            text = new UTF8Encoding(false, true).GetString(_body);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public MZP3ReadResult ParseEmbedded(MZP3Limits? limits = null, int nestingLevel = 1) =>
        WireType == MZP3WireType.LengthDelimited
            ? MZP3RawProtobuf.TryParse(_body, limits, nestingLevel)
            : new(MZP3WireError.InvalidWireType, null, 0);
}

public sealed class MZP3WireMessage
{
    private readonly MZP3WireField[] _fields;
    internal MZP3WireMessage(MZP3WireField[] fields) => _fields = fields;
    public IReadOnlyList<MZP3WireField> Fields => Array.AsReadOnly(_fields);

    /// <summary>Lossless decode/encode preserving unknown fields and exact wire order.</summary>
    public byte[] Encode(MZP3Limits? limits = null)
    {
        var budget = limits ?? MZP3Limits.Default;
        if (!budget.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        if (_fields.Length > budget.MaxFields)
            throw new MZP3FormatException(MZP3WireError.TooManyFields, 0);
        int total = 0;
        foreach (var field in _fields)
        {
            if (field.DataLength > budget.MaxFieldBytes)
                throw new MZP3FormatException(MZP3WireError.FieldTooLarge, total);
            if (field.EncodedSpan.Length > budget.MaxMessageBytes - total)
                throw new MZP3FormatException(MZP3WireError.TooLarge, total);
            total += field.EncodedSpan.Length;
        }
        var buffer = new byte[total];
        var offset = 0;
        foreach (var field in _fields)
        {
            field.EncodedSpan.CopyTo(buffer.AsSpan(offset));
            offset += field.EncodedSpan.Length;
        }
        return buffer;
    }
}

public sealed record MZP3ReadResult(MZP3WireError Error, MZP3WireMessage? Message, int ErrorOffset)
{
    public bool Ok => Error == MZP3WireError.None && Message is not null;
    public bool FramingAuthenticated => false;
    public bool SenderAuthenticated => false;
    public bool ServerAckCertified => false;
}

/// <summary>
/// Bounded neutral protobuf wire parser and encoder. Not a GivreCore shim,
/// not an identity mapping, and not a message-type or ACK verifier.
/// </summary>
public static class MZP3RawProtobuf
{
    public const int MaximumFieldNumber = 536870911;

    public static MZP3WireMessage Parse(ReadOnlySpan<byte> bytes, MZP3Limits? limits = null)
    {
        var r = TryParse(bytes, limits);
        return r.Message ?? throw new MZP3FormatException(r.Error, r.ErrorOffset);
    }

    public static MZP3ReadResult TryParse(ReadOnlySpan<byte> data,
        MZP3Limits? limits = null, int nestingLevel = 0,
        bool allowNonCanonicalValues = false)
    {
        var b = limits ?? MZP3Limits.Default;
        if (!b.IsValid) return new(MZP3WireError.InvalidLimits, null, 0);
        if (nestingLevel < 0 || nestingLevel > b.MaxDepth)
            return new(MZP3WireError.DepthExceeded, null, 0);
        if (data.Length > b.MaxMessageBytes)
            return new(MZP3WireError.TooLarge, null, 0);
        var fields = new List<MZP3WireField>(Math.Min(32, b.MaxFields));
        var p = 0;
        MZP3ReadResult Fail(MZP3WireError error, int offset) => new(error, null, offset);
        while (p < data.Length)
        {
            if (fields.Count == b.MaxFields) return Fail(MZP3WireError.TooManyFields, p);
            int begin = p;
            var err = ReadVarint(data, ref p, out ulong tag, allowNonCanonical: false);
            if (err != MZP3WireError.None) return Fail(err, begin);
            ulong fieldNumber = tag >> 3;
            if (fieldNumber is 0 or > MaximumFieldNumber or >= 19000 and <= 19999)
                return Fail(MZP3WireError.InvalidFieldNumber, begin);
            var wire = (MZP3WireType)(tag & 7);
            if (wire is not (MZP3WireType.Varint or MZP3WireType.Fixed64 or
                            MZP3WireType.LengthDelimited or MZP3WireType.Fixed32))
                return Fail(MZP3WireError.InvalidWireType, begin);
            int bodyStart;
            int bodyLength;
            ulong? scalar = null;
            switch (wire)
            {
                case MZP3WireType.Varint:
                    bodyStart = p;
                    err = ReadVarint(data, ref p, out var v, allowNonCanonicalValues);
                    if (err != MZP3WireError.None) return Fail(err, bodyStart);
                    scalar = v;
                    bodyLength = p - bodyStart;
                    break;
                case MZP3WireType.LengthDelimited:
                    var prefixStart = p;
                    err = ReadVarint(data, ref p, out var length, allowNonCanonical: false);
                    if (err != MZP3WireError.None) return Fail(err, prefixStart);
                    bodyStart = p;
                    if (length > (ulong)b.MaxFieldBytes) return Fail(MZP3WireError.FieldTooLarge, p);
                    if (length > (ulong)(data.Length - p)) return Fail(MZP3WireError.TruncatedValue, p);
                    bodyLength = (int)length;
                    p += bodyLength;
                    break;
                default:
                    bodyStart = p;
                    bodyLength = wire == MZP3WireType.Fixed64 ? 8 : 4;
                    if (bodyLength > b.MaxFieldBytes) return Fail(MZP3WireError.FieldTooLarge, p);
                    if (data.Length - p < bodyLength) return Fail(MZP3WireError.TruncatedValue, p);
                    p += bodyLength;
                    break;
            }
            if (bodyLength > b.MaxFieldBytes) return Fail(MZP3WireError.FieldTooLarge, bodyStart);
            fields.Add(new MZP3WireField((int)fieldNumber, wire, scalar,
                data.Slice(bodyStart, bodyLength).ToArray(), data.Slice(begin, p - begin).ToArray()));
        }
        return new(MZP3WireError.None, new MZP3WireMessage(fields.ToArray()), -1);
    }

    /// <summary>Strict unsigned 64-bit varint; optional loose mode for decoded VALUE bytes only.</summary>
    public static MZP3WireError ReadVarint(ReadOnlySpan<byte> bytes, ref int offset,
        out ulong value, bool allowNonCanonical = false)
    {
        value = 0;
        int p = offset;
        for (int i = 0; i < 10; i++)
        {
            if (p >= bytes.Length) return MZP3WireError.TruncatedVarint;
            byte current = bytes[p++];
            if (i == 9 && (current & 0xFE) != 0)
                return MZP3WireError.OverflowVarint;
            value |= (ulong)(current & 0x7F) << (7 * i);
            if ((current & 0x80) != 0) continue;
            if (!allowNonCanonical && i > 0 && current == 0)
            {
                value = 0;
                return MZP3WireError.NonCanonicalVarint;
            }
            offset = p;
            return MZP3WireError.None;
        }
        value = 0;
        return MZP3WireError.OverflowVarint;
    }

    public static byte[] EncodeVarint(ulong value)
    {
        Span<byte> bytes = stackalloc byte[10];
        var p = 0;
        while (value >= 0x80)
        {
            bytes[p++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }
        bytes[p++] = (byte)value;
        return bytes[..p].ToArray();
    }

    internal static void ValidateNumber(int number)
    {
        if (number is < 1 or > MaximumFieldNumber or >= 19000 and <= 19999)
            throw new ArgumentOutOfRangeException(nameof(number), "Invalid or reserved protobuf field number.");
    }
}

/// <summary>Field-preserving builder; never guesses schema or drops unknown data.</summary>
public sealed class MZP3MessageBuilder
{
    private readonly List<MZP3WireField> _fields = new();

    public MZP3MessageBuilder AddRaw(MZP3WireField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _fields.Add(field);
        return this;
    }

    public MZP3MessageBuilder AddVarint(int number, ulong value)
        => Add(number, MZP3WireType.Varint, MZP3RawProtobuf.EncodeVarint(value), value);

    public MZP3MessageBuilder AddSignedInt64(int number, long value)
        => AddVarint(number, unchecked((ulong)value));

    public MZP3MessageBuilder AddFixed32(int number, uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return Add(number, MZP3WireType.Fixed32, data);
    }

    public MZP3MessageBuilder AddFixed64(int number, ulong value)
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, value);
        return Add(number, MZP3WireType.Fixed64, data);
    }

    public MZP3MessageBuilder AddBytes(int number, ReadOnlySpan<byte> data)
        => Add(number, MZP3WireType.LengthDelimited, data.ToArray());

    public MZP3MessageBuilder AddUtf8(int number, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddBytes(number, new UTF8Encoding(false, true).GetBytes(value));
    }

    public MZP3MessageBuilder AddEmbedded(int number, MZP3WireMessage message, MZP3Limits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return AddBytes(number, message.Encode(limits));
    }

    public MZP3WireMessage Build() => new(_fields.ToArray());

    private MZP3MessageBuilder Add(int number, MZP3WireType wire, byte[] data, ulong? value = null)
    {
        MZP3RawProtobuf.ValidateNumber(number);
        using var stream = new MemoryStream();
        stream.Write(MZP3RawProtobuf.EncodeVarint(((ulong)number << 3) | (uint)wire));
        if (wire == MZP3WireType.LengthDelimited)
            stream.Write(MZP3RawProtobuf.EncodeVarint((ulong)data.Length));
        stream.Write(data);
        _fields.Add(new MZP3WireField(number, wire, value, (byte[])data.Clone(), stream.ToArray()));
        return this;
    }
}
