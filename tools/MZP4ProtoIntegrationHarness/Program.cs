using System.Security.Cryptography;
using System.Text;
using GivreClone.Protocol;

internal static class Program
{
    private static readonly byte[] Matching = Encoding.UTF8.GetBytes(
        "{\"type.ankama.com/a1\":\"MessageType.MapMovementRequest\"," +
        "\"type.ankama.com/b2\":\"MessageType.ChatChannelMessageRequest\"}");
    private static readonly byte[] Fields = Encoding.UTF8.GetBytes(
        "{\"GameMessage\":{\"request\":7},\"Request\":{\"content\":4,\"uid\":6}," +
        "\"MapMovementRequest\":{\"cell_id\":2,\"active\":3,\"label\":5," +
        "\"blob\":8,\"path\":9,\"counter\":10}," +
        "\"ChatChannelMessageRequest\":{\"content\":1}}");

    private static readonly MZP4CatalogReference Pin = new(
        "3.7.10.10", Sha(Matching), Sha(Fields), "independent-test-fixture");
    private static int _cases;
    private static int _failed;

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void Check(bool condition, string name)
    {
        _cases++;
        if (!condition) { _failed++; Console.Error.WriteLine($"FAIL {name}"); }
        else Console.WriteLine($"PASS {name}");
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
    }

    private static MZP4ProtocolAdapter Open() =>
        MZP4ProtocolAdapter.Open(Matching, Fields, Pin).Adapter
        ?? throw new InvalidOperationException("Fixture catalogue invalid.");

    private static void Main()
    {
        var result = MZP4ProtocolAdapter.Open(Matching, Fields, Pin);
        var newer = MZP4ProtocolAdapter.Open(Matching, Fields,
            Pin with { ClientVersion = "3.7.11.11" });
        Check(newer.IsOfflineCandidate && newer.Adapter is not null &&
              !newer.IsRuntimeCertified,
            "3.7.11.11 accepts same SHA-pinned structural catalogue as 3.7.10.10");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields,
            Pin with { ClientVersion = "3.8.0.0" }).IsOfflineCandidate,
            "different protocol family denied");
        Check(result.IsOfflineCandidate && result.Adapter is not null &&
              result.Reason == "OFFLINE_CANDIDATE_ONLY", "MZP0 pins -> MZP4 candidate");
        var adapter = Open();
        Check(!adapter.CanSendToGame && !adapter.IsRuntimeCertified &&
            !adapter.CanCertifyServerAck && !result.IsRuntimeCertified,
            "no runtime/network/ACK authority");
        Check(adapter.CodeForName("MapMovementRequest") == "a1" &&
            adapter.NameForCode("b2") == "ChatChannelMessageRequest" &&
            adapter.FieldNumber("MapMovementRequest", "path") == 9 &&
            adapter.FieldNumber("MapMovementRequest", "unknown") is null,
            "real catalogue name/alias/field lookup");

        var builder = adapter.ForMessage("MapMovementRequest")
            .Int64("cell_id", -1).Bool("active", true).Utf8("label", "étape")
            .Bytes("blob", new byte[] { 0, 0xFF, 0x02 })
            .RepeatedInt64("path", new long[] { 1, 2, -3 })
            .Fixed32("counter", 0x12345678);
        var unknown = MZP3RawProtobuf.Parse(new byte[] { 0xE8, 0x06, 0x07 });
        builder.PreserveUnknown(unknown.Fields[0]);
        byte[] payload = builder.Build().Encode();
        byte[] envelope = adapter.BuildClientRequest(builder, uid: -1);
        Check(adapter.TryReadClientRequest(envelope, out var decoded, out var why) &&
            decoded is not null && decoded.ClearName == "MapMovementRequest" &&
            decoded.Code == "a1" && decoded.Uid == -1 &&
            decoded.Payload.Encode().AsSpan().SequenceEqual(payload) &&
            why == "OFFLINE_STRUCTURE_ONLY", "named payload wire + envelope roundtrip");
        Check(decoded is not null && decoded.Payload.Fields.Count == 9 &&
            decoded.Payload.Fields[^1].Number == 109 &&
            decoded.Payload.Fields[2].TryGetStrictUtf8(out var accented) &&
            accented == "étape", "unknown preserved, repeated order and strict UTF8");
        Check(decoded is not null && !decoded.IsRuntimeAuthenticated &&
            !decoded.ServerAckCertified, "structural extraction never attests a server");
        byte[] prefixed = MZP3FrameCodec.Prefix(envelope);
        Check(adapter.TryReadOneNeutralFrame(prefixed, out var framed, out _) &&
            framed?.Uid == -1, "neutral exact varint framing");
        Check(!adapter.TryReadOneNeutralFrame(prefixed.Concat(new byte[] { 0 }).ToArray(),
            out _, out var extra) && extra == "FRAME_TrailingBytes", "reject extra frame/trailing byte");
        Check(!adapter.TryReadOneNeutralFrame(prefixed[..^1], out _, out var truncated) &&
            truncated == "FRAME_TruncatedValue", "reject truncated frame");
        Check(!adapter.TryReadOneNeutralFrame(new byte[] { 0x80, 0x00 },
            out _, out var noncanonical) && noncanonical == "FRAME_NonCanonicalVarint",
            "reject noncanonical frame prefix");

        var empty = adapter.ForMessage("ChatChannelMessageRequest");
        byte[] zeroUid = adapter.BuildClientRequest(empty, 0);
        Check(adapter.TryReadClientRequest(zeroUid, out var uid0, out _) && uid0?.Uid == 0 &&
            uid0.Payload.Fields.Count == 0, "zero uid omitted per proto3");
        var slots = new MZP3RequestSlots(7, 4, 6);  // synthetic fixture only
        byte[] unknownCode = MZP3EnvelopeCodec.BuildClientRequest(slots, "zz", payload, 42);
        Check(!adapter.TryReadClientRequest(unknownCode, out _, out var badCode) &&
            badCode == "CODE_NOT_IN_PINNED_CATALOGUE", "reject code absent in pinned map");
        byte[] malformedPayload = MZP3EnvelopeCodec.BuildClientRequest(slots, "a1", new byte[] { 0x08, 0x80 }, 1);
        Check(!adapter.TryReadClientRequest(malformedPayload, out _, out var invalidPayload) &&
            invalidPayload.StartsWith("PAYLOAD_", StringComparison.Ordinal),
            "reject malformed protobuf payload");
        Check(!adapter.TryReadClientRequest(new byte[] { 0x08, 0x02 }, out _, out _),
            "reject malformed envelope");
        Check(!adapter.TryReadClientRequest(envelope, out _, out _,
            new MZP3Limits(10, 5, 20, 12)), "bounded parse enforces size limits");

        Throws<InvalidOperationException>(() => adapter.ForMessage("MissingRequest"),
            "reject uncatalogued message");
        Throws<InvalidOperationException>(() => adapter.ForMessage("MapMovementRequest")
            .Int64("missing_field", 1), "reject missing named field");
        var other = Open();
        Throws<InvalidOperationException>(() => other.BuildClientRequest(builder, -1),
            "prevent cross-catalogue payload confusion");

        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin with
            { ClientVersion = "3.6.12.16" }).IsOfflineCandidate,
            "reject old client version");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin with
            { MatchingSha256 = new string('0', 64) }).IsOfflineCandidate,
            "reject unpinned matching");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin with
            { FieldsSha256 = "" }).IsOfflineCandidate,
            "reject missing SHA reference");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin with
            { SourceLabel = "" }).IsOfflineCandidate,
            "reject missing provenance label");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, null).IsOfflineCandidate,
            "reject null provenance reference");
        var tampered = (byte[])Fields.Clone();
        tampered[3] ^= 1;
        Check(!MZP4ProtocolAdapter.Open(Matching, tampered, Pin).IsOfflineCandidate,
            "reject data mutation after external pin");

        byte[] missingUid = Encoding.UTF8.GetBytes(
            "{\"GameMessage\":{\"request\":7},\"Request\":{\"content\":4}}");
        Check(!MZP4ProtocolAdapter.Open(Matching, missingUid,
            Pin with { FieldsSha256 = Sha(missingUid) }).IsOfflineCandidate,
            "reject missing Request.uid despite valid own SHA");
        byte[] ambiguous = Encoding.UTF8.GetBytes(
            "{\"GameMessage\":{\"request\":7},\"Request\":{\"content\":4,\"uid\":4}}");
        Check(!MZP4ProtocolAdapter.Open(Matching, ambiguous,
            Pin with { FieldsSha256 = Sha(ambiguous) }).IsOfflineCandidate,
            "reject colliding Request tags");
        byte[] aliased = Encoding.UTF8.GetBytes(
            "{\"type.ankama.com/a1\":\"MessageType.MapMovementRequest\"," +
            "\"type.ankama.com/a2\":\"MessageType.MapMovementRequest\"}");
        Check(!MZP4ProtocolAdapter.Open(aliased, Fields,
            Pin with { MatchingSha256 = Sha(aliased) }).IsOfflineCandidate,
            "reject ambiguous clear-name alias");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin,
            new[] { new MZP0RequiredField("MapMovementRequest", "unknown") }).IsOfflineCandidate,
            "reject explicit missing dependent field");
        Check(!MZP4ProtocolAdapter.Open(Matching, Fields, Pin,
            new[] { new MZP0RequiredField("GameMessage", "request") }).IsOfflineCandidate,
            "reject duplicate required slot manifest");
        Check(!MZP4ProtocolAdapter.Open(new byte[1048577], Fields, Pin).IsOfflineCandidate,
            "reject oversized catalogue upfront");

        Console.WriteLine($"MZP4 RESULT: {_cases - _failed}/{_cases} passed");
        if (_failed != 0) Environment.ExitCode = 1;
    }
}
