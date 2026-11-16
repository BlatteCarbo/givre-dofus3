using System.Security.Cryptography;
using System.Text;
using GivreClone.Protocol;

internal static class Program
{
    private static int _passes, _fails;
    static void Check(bool ok, string label)
    {
        if (ok) { _passes++; Console.WriteLine("PASS " + label); }
        else { _fails++; Console.Error.WriteLine("FAIL " + label); }
    }
    static void Expect(MZP7TypedResult result, MZP7TypedStatus status, string label)
        => Check(result.Status == status && result.Message is null &&
                 !result.Accepted && !result.ClientAuthenticated &&
                 !result.ServerAckCertified, label);

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // These tags are synthetic, not a Dofus 3.7 manifest. The API does not
    // invent any protobuf field kinds from numeric mappings.
    static readonly byte[] Matching = Encoding.UTF8.GetBytes(
        """{"type.ankama.com/q1":"MessageType.SampleRequest","type.ankama.com/e1":"MessageType.SampleEvent"}""");
    static readonly byte[] Fields = Encoding.UTF8.GetBytes(
        """{"GameMessage":{"request":7,"event":5,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2,"uid":6},"SampleRequest":{"name":1,"level":2,"active":3,"slots":4,"blob":5,"child":6,"fixed32":7,"fixed64":8,"ident":9,"unsigned":10,"large":11},"SampleEvent":{"child":1},"Child":{"value":1,"id":2}}""");
    static readonly MZP4CatalogReference Reference = new(
        "3.7.10.10", Sha(Matching), Sha(Fields), "test-only-bounded-manifest");
    static readonly MZP7MessageDeclaration Child = new("Child",
        new MZP7FieldDeclaration[]
        {
            new("value", MZP7ValueKind.Utf8String, Required: true),
            new("id", MZP7ValueKind.Int64)
        });
    static readonly MZP7MessageDeclaration Request = new("SampleRequest",
        new MZP7FieldDeclaration[]
        {
            new("name", MZP7ValueKind.Utf8String),
            new("level", MZP7ValueKind.Int32),
            new("active", MZP7ValueKind.Boolean),
            new("slots", MZP7ValueKind.Int32, Repeated: true, PackedAllowed: true),
            new("blob", MZP7ValueKind.Bytes),
            new("child", MZP7ValueKind.EmbeddedMessage, EmbeddedTypeName: "Child"),
            new("fixed32", MZP7ValueKind.Fixed32),
            new("fixed64", MZP7ValueKind.Fixed64),
            new("ident", MZP7ValueKind.Int64),
            new("unsigned", MZP7ValueKind.UInt32),
            new("large", MZP7ValueKind.UInt64)
        });
    static readonly MZP7MessageDeclaration Event = new("SampleEvent",
        new MZP7FieldDeclaration[]
        {
            new("child", MZP7ValueKind.EmbeddedMessage, Repeated: true,
                EmbeddedTypeName: "Child")
        });
    static readonly IReadOnlyList<MZP7MessageDeclaration> Manifest =
        new[] { Child, Request, Event };

    static MZP4ProtocolAdapter MakeAdapter(byte[]? matching = null, byte[]? fields = null)
    {
        var m = matching ?? Matching;
        var f = fields ?? Fields;
        var review = MZP4ProtocolAdapter.Open(m, f,
            new MZP4CatalogReference("3.7.10.10", Sha(m), Sha(f), "fixture-only"));
        return review.Adapter ?? throw new Exception(review.Reason);
    }

    static MZP7TypeSource MakeSource(
        IReadOnlyList<MZP7MessageDeclaration>? manifest = null,
        MZP4ProtocolAdapter? catalogue = null)
    {
        var r = catalogue?.Reference ?? Reference;
        return new("3.7.10.10", r.MatchingSha256, r.FieldsSha256,
            MZP7TypedCodec.ComputeManifestSha256(manifest ?? Manifest),
            "explicit-test-type-manifest");
    }
    static MZP7TypedCodec MakeCodec(
        IReadOnlyList<MZP7MessageDeclaration>? decl = null,
        MZP4ProtocolAdapter? catalogue = null, MZP3Limits? limits = null)
    {
        var adapter = catalogue ?? MakeAdapter();
        var definitions = decl ?? Manifest;
        var result = MZP7TypedCodec.Open(adapter, MakeSource(definitions, adapter),
            definitions, limits);
        return result.Codec ?? throw new Exception(result.Reason);
    }

    static byte[] BuildPayload()
    {
        byte[] child = new MZP3MessageBuilder()
            .AddUtf8(1, "Élève").AddSignedInt64(2, -77)
            .AddBytes(999, new byte[] { 0, 255, 22 }).Build().Encode();
        var packed = MZP3RawProtobuf.EncodeVarint(111)
            .Concat(MZP3RawProtobuf.EncodeVarint(559)).ToArray();
        return new MZP3MessageBuilder()
            .AddUtf8(1, "Crâ").AddSignedInt64(2, 42)
            .AddVarint(3, 1).AddBytes(4, packed).AddSignedInt64(4, 17)
            .AddBytes(5, new byte[] { 0, 254, 255 })
            .AddBytes(6, child).AddFixed32(7, 0x12345678)
            .AddFixed64(8, 0x1122334455667788UL)
            .AddSignedInt64(9, -9).AddVarint(10, uint.MaxValue)
            .AddVarint(11, ulong.MaxValue)
            .AddBytes(800, new byte[] { 1, 2 }).Build().Encode();
    }

    static byte[] One(string fieldType, ulong value = 1, byte[]? bytes = null)
    {
        int tag = fieldType switch
        {
            "level" => 2, "active" => 3, "slots" => 4, "blob" => 5,
            "child" => 6, "ident" => 9, "unsigned" => 10, "large" => 11,
            "name" => 1, _ => throw new ArgumentException()
        };
        var builder = new MZP3MessageBuilder();
        if (bytes is not null) builder.AddBytes(tag, bytes);
        else builder.AddVarint(tag, value);
        return builder.Build().Encode();
    }

    static void Main()
    {
        var adapter = MakeAdapter();
        var source = MakeSource();
        var acrossPatch = MZP7TypedCodec.Open(adapter,
            source with { ClientVersion = "3.7.11.11" }, Manifest);
        Check(acrossPatch.IsOfflineCandidate && acrossPatch.Codec is not null &&
              !acrossPatch.TypesIndependentlyAuthenticated,
            "MZP7 types accept 3.7.11.11 with unchanged pinned 3.7.10.10 catalogue");
        Check(!MZP7TypedCodec.Open(adapter,
            source with { ClientVersion = "3.8.0.0" }, Manifest).IsOfflineCandidate,
            "MZP7 refuses different protocol family");
        var opened = MZP7TypedCodec.Open(adapter, source, Manifest);
        Check(opened.IsOfflineCandidate && opened.Codec is not null &&
              !opened.TypesIndependentlyAuthenticated && !opened.MayInject,
              "MZP7 typed candidate opens with matching MZP4 pin + explicit schema");
        var codec = opened.Codec!;
        var body = BuildPayload();
        var decoded = codec.ReadUnframedPayload("SampleRequest", body);
        Check(decoded.Accepted && decoded.Message is not null &&
              !decoded.ClientAuthenticated && !decoded.ServerAckCertified,
              "MZP3 wire parser + MZP7 strong type metadata, no identity/ACK");
        var model = decoded.Message!;
        Check(model.Single("name") is { Kind: MZP7ValueKind.Utf8String, Text: "Crâ" } &&
              model.Single("level")?.Signed == 42 &&
              model.Single("active")?.Boolean == true,
              "UTF8 accents, strict int32 and boolean");
        Check(model.Values("slots").Select(v => v.Signed).SequenceEqual(
              new long?[] { 111, 559, 17 }), "mixed packed/unpacked repeated int32");
        Check(model.Single("ident")?.Signed == -9 &&
              model.Single("unsigned")?.Unsigned == uint.MaxValue &&
              model.Single("large")?.Unsigned == ulong.MaxValue,
              "signed 64-bit and exact unsigned range");
        Check(model.Single("fixed32")?.Unsigned == 0x12345678 &&
              model.Single("fixed64")?.Unsigned == 0x1122334455667788UL,
              "little-endian fixed-width fields");
        var child = model.Single("child")?.Nested;
        Check(child?.Single("value")?.Text == "Élève" &&
              child.Single("id")?.Signed == -77 && child.UnknownFieldCount == 1,
              "nested schema, negative int64 and unknown child field");
        Check(model.UnknownFieldCount == 1 &&
              model.EncodeLossless().AsSpan().SequenceEqual(body) &&
              child!.EncodeLossless().AsSpan().SequenceEqual(
                  new MZP3MessageBuilder().AddUtf8(1, "Élève")
                      .AddSignedInt64(2, -77)
                      .AddBytes(999, new byte[] { 0, 255, 22 }).Build().Encode()),
              "original payload unknown fields losslessly preserved");
        var parsedWire = MZP3RawProtobuf.TryParse(body);
        Check(parsedWire.Ok && codec.ReadWire("SampleRequest", parsedWire.Message).Accepted,
            "direct MZP3WireMessage conversion shares strict typed decoder");
        Check(model.Single("absent") is null &&
              model.Values("absent").Count == 0,
            "optional/unknown declared field is not invented");
        var blob = model.Single("blob")!.CopyBytes()!;
        blob[0] = 99;
        Check(model.Single("blob")!.CopyBytes()![0] == 0,
            "byte payload cannot mutate typed model through returned buffer");

        var inboundScope = new MZP5SourceScope("unit-source", "stream-one", 12, 1,
            Reference.ClientVersion, Reference.MatchingSha256, Reference.FieldsSha256);
        var reader = MZP5InboundReader.Open(adapter, inboundScope).Reader!;
        byte[] envelope = adapter.BuildClientRequest(
            adapter.ForMessage("SampleRequest").Bytes("blob", new byte[] { 2, 3 }), -1);
        var inbound = reader.ReadUnframed(
            new MZP5InboundObservation(inboundScope, MZP5Direction.ClientToServer, 1),
            envelope);
        Check(inbound.Parsed, "real MZP4 producer + MZP5 inbound parser");
        var asMZP5 = codec.FromMZP5(inbound.Packet);
        Check(asMZP5.Accepted && asMZP5.Message?.Single("blob")!.CopyBytes()![0] == 2,
              "MZP5 -> typed MZP7 correctly converts neutral packet");
        var mzp4 = adapter.TryReadClientRequest(envelope, out var req, out _);
        Check(mzp4 && codec.FromMZP4(req).Accepted,
              "MZP4 decoded request -> typed MZP7");
        Expect(codec.FromMZP4(req! with { Code = "wrong" }),
            MZP7TypedStatus.AliasMismatch, "forged MZP4 alias rejected");
        Expect(codec.FromMZP5(inbound.Packet! with { Code = "wrong" }),
            MZP7TypedStatus.AliasMismatch, "forged MZP5 alias rejected");
        Expect(codec.FromMZP5(inbound.Packet! with { ClearName = null }),
            MZP7TypedStatus.MissingOrUnrecognizedMessage,
            "unknown MZP5 type refused");

        Expect(codec.ReadUnframedPayload("SampleRequest", One("active", 2)),
            MZP7TypedStatus.InvalidScalar, "bool other than zero/one rejected");
        Expect(codec.ReadUnframedPayload("SampleRequest", One("unsigned", uint.MaxValue + 1UL)),
            MZP7TypedStatus.InvalidScalar, "uint32 overflow rejected");
        Expect(codec.ReadUnframedPayload("SampleRequest", One("level", unchecked((ulong)(long)int.MaxValue + 1))),
            MZP7TypedStatus.InvalidScalar, "int32 positive overflow rejected");
        Expect(codec.ReadUnframedPayload("SampleRequest", One("level", 0xFFFFFFFFUL)),
            MZP7TypedStatus.InvalidScalar,
            "negative int32 without 64-bit sign-extension refused");
        Check(codec.ReadUnframedPayload("SampleRequest",
            One("level", unchecked((ulong)(long)-10))).Message?.Single("level")?.Signed == -10,
            "negative int32 with 64-bit sign-extension supported");
        Expect(codec.ReadUnframedPayload("SampleRequest", One("name", bytes: new byte[] { 0xC3, 0x28 })),
            MZP7TypedStatus.InvalidUtf8, "invalid UTF8 refused, not binary reinterpreted");
        Expect(codec.ReadUnframedPayload("SampleRequest", One("name", 42)),
            MZP7TypedStatus.WireKindMismatch, "field declared string but encoded varint refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("active", bytes: new byte[] { 1 })),
            MZP7TypedStatus.WireKindMismatch, "unpacked bool declared, length wire refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("slots", bytes: new byte[] { 0x80 })),
            MZP7TypedStatus.PackedVarintMalformed, "unterminated packed varint refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("slots", bytes: new byte[] { 0x80, 0x00 })),
            MZP7TypedStatus.PackedVarintMalformed, "noncanonical packed varint refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("slots", bytes: Enumerable.Repeat((byte)0x80, 11).ToArray())),
            MZP7TypedStatus.PackedVarintMalformed,
            "packed varint overflow refused without partial output");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("slots", bytes: new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })),
            MZP7TypedStatus.InvalidScalar,
            "packed int32 negative truncated to 5-byte 2^32-1 refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("child", bytes: new byte[] { 0x00 })),
            MZP7TypedStatus.NestedMessageMalformed, "invalid nested field refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            One("child", bytes: new MZP3MessageBuilder().AddSignedInt64(2, 3)
                .Build().Encode())),
            MZP7TypedStatus.RequiredFieldMissing,
            "nested required field missing refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            new MZP3MessageBuilder().AddVarint(3, 1).AddVarint(3, 0).Build().Encode()),
            MZP7TypedStatus.DuplicateSingular, "duplicate singular boolean refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            new MZP3MessageBuilder().AddBytes(1, new byte[1])
                .AddUtf8(1, "abc").Build().Encode()),
            MZP7TypedStatus.DuplicateSingular,
            "duplicate singular Len field refused");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            new MZP3MessageBuilder().AddVarint(7, 3).Build().Encode()),
            MZP7TypedStatus.WireKindMismatch,
            "fixed32 declared field cannot be read as varint");
        Expect(codec.ReadUnframedPayload("SampleRequest",
            new MZP3MessageBuilder().AddFixed32(8, 4).Build().Encode()),
            MZP7TypedStatus.WireKindMismatch,
            "fixed64 declared field cannot be read as fixed32");
        Expect(codec.ReadUnframedPayload("SampleRequest", new byte[] { 0x00 }),
            MZP7TypedStatus.InvalidWireMessage, "malformed wire fails before typed read");
        Expect(codec.ReadUnframedPayload("NoSuchType", body),
            MZP7TypedStatus.MissingOrUnrecognizedMessage,
            "schema not declared -> cannot guess");
        Expect(codec.ReadUnframedPayload(null, body),
            MZP7TypedStatus.MissingOrUnrecognizedMessage,
            "null message name fails closed");
        Expect(codec.ReadWire(null, parsedWire.Message),
            MZP7TypedStatus.MissingOrUnrecognizedMessage,
            "null wire schema name fails closed");
        Expect(codec.ReadWire("SampleRequest", null),
            MZP7TypedStatus.MissingOrUnrecognizedMessage,
            "null wire message fails closed");

        var emptyChild = new MZP3MessageBuilder().AddBytes(1,
            new MZP3MessageBuilder().AddUtf8(1, "one").Build().Encode())
            .AddBytes(1, new MZP3MessageBuilder()
                .AddUtf8(1, "two").Build().Encode()).Build().Encode();
        var evtMessage = codec.ReadUnframedPayload("SampleEvent", emptyChild);
        Check(evtMessage.Accepted && evtMessage.Message!.Values("child").Count == 2 &&
              evtMessage.Message.Values("child")[1].Nested?.Single("value")?.Text == "two",
              "repeated nested messages preserve order and explicit typed values");

        var reordered = new[] { Event, Request, Child };
        Check(MZP7TypedCodec.ComputeManifestSha256(reordered) == ShaHex(Manifest),
            "type manifest deterministic across declaration ordering");
        MZP7TypedOpenResult Open(IReadOnlyList<MZP7MessageDeclaration> defs,
            MZP7TypeSource? s = null, MZP3Limits? limits = null) =>
            MZP7TypedCodec.Open(adapter, s ?? MakeSource(defs), defs, limits);
        Check(!Open(Manifest, source with { ClientVersion = "3.6.12.16" }).IsOfflineCandidate,
            "client drift refuses typed schema");
        Check(!Open(Manifest, source with { MatchingSha256 = new string('0', 64) }).IsOfflineCandidate,
            "mapping hash drift refuses typed schema");
        Check(!Open(Manifest, source with { FieldsSha256 = new string('0', 64) }).IsOfflineCandidate,
            "fields hash drift refuses typed schema");
        Check(!Open(Manifest, source with { ManifestSha256 = new string('0', 64) }).IsOfflineCandidate,
            "type manifest SHA drift refused");
        Check(!Open(Manifest, source with { SourceLabel = "" }).IsOfflineCandidate,
            "missing type provenance label rejected");
        Check(!MZP7TypedCodec.Open(null, source, Manifest).IsOfflineCandidate,
            "null adapter cannot produce typed codec");
        Check(!MZP7TypedCodec.Open(adapter, source, null).IsOfflineCandidate,
            "null manifest cannot produce typed codec");
        Check(!Open(Manifest, limits: default(MZP3Limits)).IsOfflineCandidate,
            "invalid zeroed limits refused");
        Check(!Open(new[] { Request, Request }).IsOfflineCandidate,
            "duplicate message declarations refused");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Concat(
            new[] { new MZP7FieldDeclaration("level", MZP7ValueKind.Int64) }).ToArray() }, Event })
            .IsOfflineCandidate, "duplicate field name refused");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Concat(
            new[] { new MZP7FieldDeclaration("missing_tag", MZP7ValueKind.Bytes) }).ToArray() }, Event })
            .IsOfflineCandidate, "field absent in real MZP4 catalogue refused");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Concat(
            new[] { new MZP7FieldDeclaration("nonsense", (MZP7ValueKind)999) }).ToArray() }, Event })
            .IsOfflineCandidate, "unknown kind enum refused");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Select(f =>
            f.Name == "child" ? f with { EmbeddedTypeName = "Ghost" } : f).ToArray() }, Event })
            .IsOfflineCandidate, "nested schema must be declared");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Select(f =>
            f.Name == "level" ? f with { PackedAllowed = true } : f).ToArray() }, Event })
            .IsOfflineCandidate, "packed singular rejected in manifest");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Select(f =>
            f.Name == "blob" ? f with { PackedAllowed = true, Repeated = true } : f).ToArray() }, Event })
            .IsOfflineCandidate, "packed bytes rejected in manifest");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Select(f =>
            f.Name == "slots" ? f with { Required = true } : f).ToArray() }, Event })
            .IsOfflineCandidate, "required repeated rejected in manifest");
        Check(!Open(new[] { Child, Request with { Fields = Request.Fields.Select(f =>
            f.Name == "level" ? f with { Kind = MZP7ValueKind.EmbeddedMessage } : f).ToArray() }, Event })
            .IsOfflineCandidate, "nested type must have a message target");

        var constrained = MakeCodec(limits: new MZP3Limits(64, 32, 32, 3));
        Expect(constrained.ReadUnframedPayload("SampleRequest", body),
            MZP7TypedStatus.BudgetExceeded, "message budget bounds third-party wire");
        var longPacked = new MZP3MessageBuilder().AddBytes(4,
            Enumerable.Repeat((byte)1, 5000).ToArray()).Build().Encode(
            new MZP3Limits(131072, 65536, 8192, 12));
        Expect(codec.ReadUnframedPayload("SampleRequest", longPacked),
            MZP7TypedStatus.BudgetExceeded,
            "packed repeated values capped, no unbounded allocations");

        Console.WriteLine($"MZP7 CORE: {_passes}/{_passes + _fails} PASS");
        if (_fails > 0) Environment.ExitCode = 1;
    }
    static string ShaHex(IReadOnlyList<MZP7MessageDeclaration> declarations) =>
        MZP7TypedCodec.ComputeManifestSha256(declarations);
}
