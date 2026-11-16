using System.Text;

namespace GivreClone.Protocol;

/// <summary>Generic length-delimited wire frame, NOT evidence of client framing.</summary>
public sealed record MZP3FrameResult(MZP3WireError Error, int BodyOffset, int BodyLength, int Consumed)
{
    public bool Complete => Error == MZP3WireError.None;
    public bool GameFramingCertified => false;
}

public static class MZP3FrameCodec
{
    public static MZP3FrameResult TryReadOne(ReadOnlySpan<byte> input,
        int maxFrameBytes = 131072, bool requireExact = false)
    {
        if (maxFrameBytes is < 1 or > 1048576)
            return new(MZP3WireError.InvalidLimits, 0, 0, 0);
        // A non-exact read may receive several frames in one buffer. Only the
        // declared first frame length is subject to the per-frame size limit.
        if (requireExact && input.Length > maxFrameBytes + 10)
            return new(MZP3WireError.TooLarge, 0, 0, 0);
        var p = 0;
        var error = MZP3RawProtobuf.ReadVarint(input, ref p, out var length);
        if (error != MZP3WireError.None) return new(error, 0, 0, 0);
        if (length > (ulong)maxFrameBytes) return new(MZP3WireError.TooLarge, 0, 0, 0);
        if (length > (ulong)(input.Length - p))
            return new(MZP3WireError.TruncatedValue, 0, 0, 0);
        int consumed = p + (int)length;
        if (requireExact && consumed != input.Length)
            return new(MZP3WireError.TrailingBytes, 0, 0, 0);
        return new(MZP3WireError.None, p, (int)length, consumed);
    }

    public static byte[] Prefix(ReadOnlySpan<byte> body, int maxFrameBytes = 131072)
    {
        if (maxFrameBytes is < 1 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
        if (body.Length > maxFrameBytes)
            throw new MZP3FormatException(MZP3WireError.TooLarge, 0);
        byte[] prefix = MZP3RawProtobuf.EncodeVarint((ulong)body.Length);
        byte[] result = new byte[prefix.Length + body.Length];
        prefix.CopyTo(result, 0);
        body.CopyTo(result.AsSpan(prefix.Length));
        return result;
    }
}

/// <summary>
/// These slots MUST be supplied by an independently verified schema catalogue.
/// No stale 3.6/3.7 Dofus field-number defaults are ever assumed here.
/// </summary>
public readonly record struct MZP3RequestSlots(
    int GameMessageRequest, int RequestContent, int RequestUid)
{
    public void Validate()
    {
        MZP3RawProtobuf.ValidateNumber(GameMessageRequest);
        MZP3RawProtobuf.ValidateNumber(RequestContent);
        MZP3RawProtobuf.ValidateNumber(RequestUid);
        if (RequestContent == RequestUid)
            throw new ArgumentException("Request content and uid cannot use the same field number.");
    }
}

public sealed record MZP3RequestPayload(string Code, long? Uid, byte[] Payload)
{
    // Parsing an envelope is NOT proof the request was accepted by a server.
    public bool ServerAckCertified => false;
}

/// <summary>
/// Source-based isolated Any/request codec matching EnvelopeBuilder's explicit
/// nested shape. Does not import GivreCore, touch transport, or send requests.
/// </summary>
public static class MZP3EnvelopeCodec
{
    public const string TypeUrlPrefix = "type.ankama.com/";

    public static byte[] BuildAny(string code, ReadOnlySpan<byte> payload, MZP3Limits? limits = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Contains('/') || code.Any(char.IsWhiteSpace))
            throw new ArgumentException("Expected a non-empty single-segment type code.", nameof(code));
        var builder = new MZP3MessageBuilder().AddUtf8(1, TypeUrlPrefix + code);
        if (payload.Length > 0) builder.AddBytes(2, payload);
        return builder.Build().Encode(limits);
    }

    /// <summary>Creates an UNFRAMED GameMessage; UID 0 follows proto3 omission.</summary>
    public static byte[] BuildClientRequest(MZP3RequestSlots slots, string code,
        ReadOnlySpan<byte> payload, long uid, MZP3Limits? limits = null)
    {
        slots.Validate();
        var budget = limits ?? MZP3Limits.Default;
        var request = new MZP3MessageBuilder().AddBytes(slots.RequestContent, BuildAny(code, payload, budget));
        if (uid != 0) request.AddSignedInt64(slots.RequestUid, uid);
        var requestBody = request.Build().Encode(budget);
        return new MZP3MessageBuilder()
            .AddBytes(slots.GameMessageRequest, requestBody).Build().Encode(budget);
    }

    /// <summary>
    /// Decode using explicit slots only. Unknown sibling fields survive in the
    /// raw message if caller retains it; this helper extracts values, not an ACK.
    /// </summary>
    public static bool TryReadClientRequest(ReadOnlySpan<byte> unframed,
        MZP3RequestSlots slots, out MZP3RequestPayload? extracted,
        MZP3Limits? limits = null)
    {
        extracted = null;
        try { slots.Validate(); }
        catch (ArgumentException) { return false; }
        var budget = limits ?? MZP3Limits.Default;
        var root = MZP3RawProtobuf.TryParse(unframed, budget);
        if (!root.Ok) return false;
        MZP3WireField? branch = null;
        foreach (var field in root.Message!.Fields)
        {
            if (field.Number != slots.GameMessageRequest) continue;
            if (branch is not null || field.WireType != MZP3WireType.LengthDelimited) return false;
            branch = field;
        }
        if (branch is null) return false;
        var req = branch.ParseEmbedded(budget);
        if (!req.Ok) return false;
        MZP3WireField? anyField = null;
        long? uid = null;
        foreach (var field in req.Message!.Fields)
        {
            if (field.Number == slots.RequestContent)
            {
                if (anyField is not null || field.WireType != MZP3WireType.LengthDelimited) return false;
                anyField = field;
            }
            if (field.Number == slots.RequestUid)
            {
                if (uid is not null || field.WireType != MZP3WireType.Varint) return false;
                uid = unchecked((long)field.UnsignedValue!.Value);
            }
        }
        if (anyField is null) return false;
        var any = anyField.ParseEmbedded(budget, 2);
        if (!any.Ok) return false;
        string? code = null;
        byte[]? payload = null;
        foreach (var field in any.Message!.Fields)
        {
            if (field.Number == 1)
            {
                if (code is not null || !field.TryGetStrictUtf8(out var url) ||
                    url is null || !url.StartsWith(TypeUrlPrefix, StringComparison.Ordinal) ||
                    url.Length == TypeUrlPrefix.Length) return false;
                code = url[TypeUrlPrefix.Length..];
                if (code.Contains('/') || code.Any(char.IsWhiteSpace)) return false;
            }
            if (field.Number == 2)
            {
                if (payload is not null || field.WireType != MZP3WireType.LengthDelimited) return false;
                payload = field.GetData();
            }
        }
        if (code is null) return false;
        extracted = new(code, uid, payload ?? Array.Empty<byte>());
        return true;
    }
}
