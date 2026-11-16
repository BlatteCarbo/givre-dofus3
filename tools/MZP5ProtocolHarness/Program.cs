using System.Security.Cryptography;
using System.Text;
using GivreClone.Protocol;

internal static class Program
{
    // ONLY synthetically chosen slots, not real Dofus 3.7 wire tags.
    private static readonly byte[] Matching = Encoding.UTF8.GetBytes(
        """{"type.ankama.com/a1":"MessageType.SampleRequest","type.ankama.com/e2":"MessageType.SampleEvent","type.ankama.com/r3":"MessageType.SampleResponse"}""");
    private static readonly byte[] Fields = Encoding.UTF8.GetBytes(
        """{"GameMessage":{"request":7,"event":5,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2,"uid":6},"SampleRequest":{"count":1},"SampleEvent":{"name":1},"SampleResponse":{"code":1}}""");
    private static readonly MZP4CatalogReference Reference = new(
        "3.7.10.10", Sha(Matching), Sha(Fields), "isolated-synthetic-fixture");
    private static readonly MZP5SourceScope Scope = new(
        "test-observer", "channel-1", 91, 1, Reference.ClientVersion,
        Reference.MatchingSha256, Reference.FieldsSha256);
    private static int _passed;
    private static int _failed;

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Assert(bool ok, string label)
    {
        if (ok) { _passed++; Console.WriteLine("PASS " + label); }
        else { _failed++; Console.Error.WriteLine("FAIL " + label); }
    }
    private static MZP4ProtocolAdapter Catalogue(byte[]? match = null, byte[]? fields = null)
    {
        byte[] m = match ?? Matching, f = fields ?? Fields;
        var opened = MZP4ProtocolAdapter.Open(m, f,
            new MZP4CatalogReference("3.7.10.10", Sha(m), Sha(f), "synthetic-fixture"));
        return opened.Adapter ?? throw new InvalidOperationException(opened.Reason);
    }
    private static MZP5InboundReader Reader(MZP4ProtocolAdapter? catalogue = null,
        MZP5SourceScope? scope = null, MZP3Limits? limits = null)
    {
        var result = MZP5InboundReader.Open(catalogue ?? Catalogue(),
            scope ?? Scope, limits);
        return result.Reader ?? throw new InvalidOperationException(result.Reason);
    }
    private static MZP5InboundObservation Observation(
        long sequence, MZP5Direction direction, MZP5SourceScope? scope = null)
        => new(scope ?? Scope, direction, sequence);

    private static byte[] WirePayload()
        => new MZP3MessageBuilder().AddVarint(1, 42).AddVarint(1, 43)
            .AddFixed64(99, 0xABCD1234UL).AddBytes(101, new byte[] { 0, 0xFF })
            .Build().Encode();

    private static byte[] Envelope(MZP5Branch kind, string code, byte[] payload,
        long? uid = null, bool extraUnknowns = false)
    {
        var any = new MZP3MessageBuilder()
            .AddUtf8(1, MZP3EnvelopeCodec.TypeUrlPrefix + code)
            .AddBytes(2, payload);
        if (extraUnknowns) any.AddFixed32(123, 0xCAFEBABE);
        var b = new MZP3MessageBuilder();
        int content = kind == MZP5Branch.Event ? 4 : 2;
        int uidTag = kind == MZP5Branch.Response ? 6 : 3;
        b.AddBytes(content, any.Build().Encode());
        if (uid.HasValue) b.AddSignedInt64(uidTag, uid.Value);
        if (extraUnknowns) b.AddVarint(127, 1);
        var root = new MZP3MessageBuilder();
        int tag = kind switch
        {
            MZP5Branch.Request => 7,
            MZP5Branch.Event => 5,
            _ => 9
        };
        root.AddBytes(tag, b.Build().Encode());
        if (extraUnknowns) root.AddBytes(128, new byte[] { 0xFF, 0x00, 0xF0 });
        return root.Build().Encode();
    }
    private static void CheckRejection(MZP5InboundReader reader,
        MZP5InboundObservation observation, byte[] frame,
        MZP5InboundStatus expected, string label)
    {
        var r = reader.ReadUnframed(observation, frame);
        Assert(r.Status == expected && r.Packet is null &&
            !r.Parsed && !r.IsAuthenticated && !r.ServerAckCertified, label);
    }
    private static void InvalidOnFresh(byte[] frame, MZP5InboundStatus expected,
        string label)
    {
        var reader = Reader();
        CheckRejection(reader, Observation(1, MZP5Direction.ServerToClient),
            frame, expected, label);
        Assert(reader.Snapshot().ServerDirectionQuarantined, label + " quarantines");
    }

    private static void Main()
    {
        var cat = Catalogue();
        var sameFamilyScope = Scope with { DeclaredClientVersion = "3.7.11.11" };
        var acrossPatch = MZP5InboundReader.Open(cat, sameFamilyScope);
        Assert(acrossPatch.IsOfflineCandidate && acrossPatch.Reader is not null &&
            !acrossPatch.IsRuntimeCertified,
            "MZP5 admits 3.7.11.11 scope with unchanged 3.7.10.10 SHA-pinned catalogue");
        var foreignFamily = MZP5InboundReader.Open(cat,
            Scope with { DeclaredClientVersion = "3.8.0.0" });
        Assert(!foreignFamily.IsOfflineCandidate,
            "MZP5 rejects foreign family even when hashes match");
        var opened = MZP5InboundReader.Open(cat, Scope);
        Assert(opened.IsOfflineCandidate && opened.Reader is not null &&
            opened.Reason == "OFFLINE_SHAPE_ONLY" && !opened.IsRuntimeCertified,
            "pin + explicit inbound tags open offline reader");
        var r = opened.Reader!;

        byte[] req = cat.BuildClientRequest(
            cat.ForMessage("SampleRequest").Int64("count", 5), -1);
        byte[] evt = Envelope(MZP5Branch.Event, "e2", WirePayload(), extraUnknowns: true);
        byte[] response = Envelope(MZP5Branch.Response, "r3", WirePayload(), uid: -1);
        var a = r.ReadUnframed(Observation(1, MZP5Direction.ClientToServer), req);
        Assert(a.Parsed && a.Packet is
            { Branch: MZP5Branch.Request, ClaimedDirection: MZP5Direction.ClientToServer,
                ClaimedSequence: 1, ClearName: "SampleRequest", Uid: -1 },
            "real MZP4 builder -> MZP5 Request");
        var b = r.ReadUnframed(Observation(1, MZP5Direction.ServerToClient), evt);
        Assert(b.Parsed && b.Packet is
            { Branch: MZP5Branch.Event, ClearName: "SampleEvent", Uid: null },
            "independent server direction Event shape");
        Assert(b.Packet is not null &&
            b.Packet.Root.Encode().AsSpan().SequenceEqual(evt) &&
            b.Packet.Root.Fields[^1].Number == 128 &&
            b.Packet.BranchFields.Fields[^1].Number == 127 &&
            b.Packet.AnyFields.Fields[^1].Number == 123 &&
            b.Packet.Payload.Fields[0].Number == 1 &&
            b.Packet.Payload.Fields[1].Number == 1 &&
            b.Packet.Payload.Fields[2].Number == 99 &&
            b.Packet.Payload.Encode().AsSpan().SequenceEqual(WirePayload()),
            "unknown root/branch/Any/payload fields lossless; repetitions in original order");
        var c = r.ReadUnframed(Observation(2, MZP5Direction.ServerToClient), response);
        Assert(c.Parsed && c.Packet is
            { Branch: MZP5Branch.Response, ClearName: "SampleResponse", Uid: -1 },
            "Response UID sign-extension but no ACK");
        Assert(c.Packet is not null && !c.Packet.ServerAckCertified &&
            !c.Packet.IsAuthenticated && !c.Packet.ReplayAuthorized,
            "Response never becomes certified ACK");
        var d = r.ReadUnframed(Observation(2, MZP5Direction.ClientToServer), req);
        Assert(d.Parsed, "independent client sequence advances to 2");
        var unknown = r.ReadUnframed(Observation(3, MZP5Direction.ServerToClient),
            Envelope(MZP5Branch.Event, "new_alias", WirePayload()));
        Assert(unknown.Parsed && unknown.Packet is
            { Code: "new_alias", ClearName: null, KnownAlias: false },
            "unknown alias is structurally available without invented clear name");
        var snapshot = r.Snapshot();
        Assert(snapshot.ClientPacketsAccepted == 2 && snapshot.ServerPacketsAccepted == 3 &&
            snapshot.ObservationsRejected == 0 &&
            !snapshot.ClientDirectionQuarantined && !snapshot.ServerDirectionQuarantined &&
            !snapshot.IsAuthenticated && !snapshot.ServerAckCertified,
            "redacted counters, no secrets or attestations");

        var rejectMeta = Reader();
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { SourceLabel = "forged" }), evt,
            MZP5InboundStatus.SourceMismatch, "scope source mismatch rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { StreamKey = "other-stream" }), evt,
            MZP5InboundStatus.SourceMismatch, "stream key mismatch rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { ConnectionId = 92 }), evt,
            MZP5InboundStatus.SourceMismatch, "connection mismatch rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { Generation = 2 }), evt,
            MZP5InboundStatus.SourceMismatch, "generation mismatch rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { MatchingSha256 = new string('0', 64) }), evt,
            MZP5InboundStatus.SourceMismatch, "mapping digest drift rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient,
                Scope with { DeclaredClientVersion = "3.6.12.16" }), evt,
            MZP5InboundStatus.VersionMismatch, "observation old version rejected");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.Unknown), evt,
            MZP5InboundStatus.InvalidDirection, "unknown direction rejected");
        CheckRejection(rejectMeta, Observation(1, (MZP5Direction)77), evt,
            MZP5InboundStatus.InvalidDirection, "invalid enum direction rejected");
        Assert(!rejectMeta.Snapshot().ServerDirectionQuarantined,
            "bad metadata never poisons separately selected scope");
        Assert(rejectMeta.ReadUnframed(Observation(1, MZP5Direction.ServerToClient), evt).Parsed,
            "valid observer can recover after source mismatch without reset");
        CheckRejection(rejectMeta, Observation(1, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.SequenceReplayOrReorder, "replay sequence rejected");
        CheckRejection(rejectMeta, Observation(2, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.StreamQuarantined, "replay permanently quarantines direction");
        Assert(rejectMeta.ReadUnframed(
            Observation(1, MZP5Direction.ClientToServer), req).Parsed,
            "quarantine server does not poison client direction");

        var gaps = Reader();
        CheckRejection(gaps, Observation(2, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.SequenceGap, "missing first observation refused");
        CheckRejection(gaps, Observation(1, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.StreamQuarantined, "no implicit gap repair");
        var reverse = Reader();
        Assert(reverse.ReadUnframed(Observation(1, MZP5Direction.ServerToClient), evt).Parsed,
            "first sequence admitted");
        CheckRejection(reverse, Observation(0, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.SequenceReplayOrReorder, "zero sequence invalidates stream");
        var negative = Reader();
        CheckRejection(negative, Observation(-1, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.SequenceReplayOrReorder, "negative sequence invalidates stream");
        var overflow = Reader();
        CheckRejection(overflow, Observation(long.MaxValue, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.SequenceGap, "sequence jump and potential overflow rejected");
        var parallel = Reader();
        var parallelResults = Task.WhenAll(
            Task.Run(() => parallel.ReadUnframed(
                Observation(1, MZP5Direction.ServerToClient), evt)),
            Task.Run(() => parallel.ReadUnframed(
                Observation(1, MZP5Direction.ServerToClient), evt))).GetAwaiter().GetResult();
        Assert(parallelResults.Count(x => x.Parsed) == 1 &&
            parallelResults.Any(x => x.Status == MZP5InboundStatus.SequenceReplayOrReorder) &&
            parallel.Snapshot() is
                { ServerPacketsAccepted: 1, ServerDirectionQuarantined: true },
            "concurrent duplicate delivery publishes at most once");
        var reset = Reader(scope: Scope with { Generation = 2 });
        Assert(reset.ReadUnframed(Observation(1, MZP5Direction.ServerToClient,
            Scope with { Generation = 2 }), evt).Parsed,
            "new explicit generation + new reader allows fresh sequence");

        InvalidOnFresh(new byte[] { 0x00 }, MZP5InboundStatus.MalformedWire,
            "wire field zero rejected");
        InvalidOnFresh(new byte[] { 0x0B }, MZP5InboundStatus.MalformedWire,
            "wire group rejected");
        InvalidOnFresh(new byte[] { 0x2A, 0x03, 0x08 }, MZP5InboundStatus.MalformedWire,
            "truncated length-delimited envelope rejected");
        InvalidOnFresh(new MZP3MessageBuilder().AddVarint(999, 9).Build().Encode(),
            MZP5InboundStatus.AmbiguousEnvelope, "missing branch rejected");
        InvalidOnFresh(new MZP3MessageBuilder().AddVarint(5, 4).Build().Encode(),
            MZP5InboundStatus.AmbiguousEnvelope, "branch wrong wire type rejected");
        InvalidOnFresh(req, MZP5InboundStatus.DirectionConflict,
            "client request claimed from server rejected");
        CheckRejection(Reader(), Observation(1, MZP5Direction.ClientToServer), evt,
            MZP5InboundStatus.DirectionConflict,
            "server event claimed from client rejected");
        var duplicated = new MZP3MessageBuilder().AddBytes(5, new byte[] { 0x20, 0x01 })
            .AddBytes(9, new byte[] { 0x10, 0x02 }).Build().Encode();
        InvalidOnFresh(duplicated, MZP5InboundStatus.AmbiguousEnvelope,
            "two oneof branches cannot be co-present");
        InvalidOnFresh(new MZP3MessageBuilder().AddBytes(5,
            new MZP3MessageBuilder().AddVarint(100, 1).Build().Encode()).Build().Encode(),
            MZP5InboundStatus.AmbiguousEnvelope, "branch with missing Any refused");

        byte[] any = new MZP3MessageBuilder()
            .AddUtf8(1, "type.ankama.com/e2").AddBytes(2, WirePayload()).Build().Encode();
        static byte[] ServerEventWithBranch(byte[] branch) =>
            new MZP3MessageBuilder().AddBytes(5, branch).Build().Encode();
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, any).AddBytes(4, any).Build().Encode()),
            MZP5InboundStatus.AmbiguousEnvelope, "duplicate Event.content rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddVarint(4, 1).Build().Encode()),
            MZP5InboundStatus.AmbiguousEnvelope, "Event.content type mismatch rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, new MZP3MessageBuilder()
                    .AddUtf8(1, "type.ankama.com/e2")
                    .AddUtf8(1, "type.ankama.com/e2").Build().Encode())
                .Build().Encode()),
            MZP5InboundStatus.AmbiguousEnvelope, "duplicate Any.type_url rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, new MZP3MessageBuilder()
                    .AddUtf8(1, "type.ankama.com/e2")
                    .AddBytes(2, WirePayload()).AddBytes(2, WirePayload())
                    .Build().Encode()).Build().Encode()),
            MZP5InboundStatus.AmbiguousEnvelope, "duplicate Any.value rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, new MZP3MessageBuilder()
                    .AddUtf8(1, "bad.example/e2").Build().Encode()).Build().Encode()),
            MZP5InboundStatus.AmbiguousEnvelope, "wrong Any URL prefix rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, new MZP3MessageBuilder()
                    .AddUtf8(1, "type.ankama.com/a/b").Build().Encode()).Build().Encode()),
            MZP5InboundStatus.UnknownEnvelopeCodeSyntax, "invalid alias code syntax rejected");
        InvalidOnFresh(ServerEventWithBranch(new MZP3MessageBuilder()
                .AddBytes(4, new MZP3MessageBuilder()
                    .AddUtf8(1, "type.ankama.com/e2")
                    .AddBytes(2, new byte[] { 0x08, 0x80 }).Build().Encode())
                .Build().Encode()),
            MZP5InboundStatus.MalformedWire, "truncated payload varint rejected");
        InvalidOnFresh(new MZP3MessageBuilder().AddBytes(9,
                new MZP3MessageBuilder().AddBytes(2, any)
                    .AddSignedInt64(6, 1).AddSignedInt64(6, 2)
                    .Build().Encode()).Build().Encode(),
            MZP5InboundStatus.AmbiguousEnvelope, "duplicate Response UID rejected");
        InvalidOnFresh(new MZP3MessageBuilder().AddBytes(9,
                new MZP3MessageBuilder().AddBytes(2, any).AddBytes(6, new byte[1])
                    .Build().Encode()).Build().Encode(),
            MZP5InboundStatus.AmbiguousEnvelope, "Response UID wrong wire rejected");

        var absentValue = ServerEventWithBranch(new MZP3MessageBuilder()
            .AddBytes(4, new MZP3MessageBuilder()
                .AddUtf8(1, "type.ankama.com/e2").Build().Encode()).Build().Encode());
        Assert(Reader().ReadUnframed(Observation(1, MZP5Direction.ServerToClient),
            absentValue) is { Parsed: true, Packet.Payload.Fields.Count: 0 },
            "proto3 Any.value omission parses empty payload");
        var malformed = Reader();
        var wireError = malformed.ReadUnframed(
            Observation(1, MZP5Direction.ServerToClient), new byte[] { 0x00 });
        Assert(wireError.Status == MZP5InboundStatus.MalformedWire &&
            wireError.WireError == MZP3WireError.InvalidFieldNumber &&
            malformed.Snapshot().ServerDirectionQuarantined &&
            malformed.Snapshot().ServerPacketsAccepted == 0,
            "exact parser failure classification and no partial publication");
        CheckRejection(malformed, Observation(2, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.StreamQuarantined, "malformed bytes require new scope");
        Assert(malformed.ReadUnframed(Observation(1, MZP5Direction.ClientToServer),
            req).Parsed, "malformed server cannot poison client");

        var limited = Reader(limits: new MZP3Limits(10, 10, 10, 3));
        CheckRejection(limited, Observation(1, MZP5Direction.ServerToClient), evt,
            MZP5InboundStatus.MalformedWire, "wire budget caps actual input");
        Assert(!MZP5InboundReader.Open(cat, Scope, new MZP3Limits(10, 10, 10, 2))
            .IsOfflineCandidate, "depth below three rejected before parsing");
        Assert(!MZP5InboundReader.Open(cat, Scope, default(MZP3Limits))
            .IsOfflineCandidate, "zeroed limits rejected");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { DeclaredClientVersion = "3.6.12.16" }).IsOfflineCandidate,
            "stale client-version scope rejected during factory admission");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { FieldsSha256 = new string('0', 64) }).IsOfflineCandidate,
            "pin mismatch prevents reader creation");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { SourceLabel = "bad source" }).IsOfflineCandidate,
            "unsafe producer label rejected");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { StreamKey = "" }).IsOfflineCandidate,
            "empty stream key rejected");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { ConnectionId = 0 }).IsOfflineCandidate,
            "unknown connection rejected");
        Assert(!MZP5InboundReader.Open(cat, Scope with
            { Generation = 0 }).IsOfflineCandidate,
            "unknown session generation rejected");
        Assert(!MZP5InboundReader.Open(null, Scope).IsOfflineCandidate,
            "missing catalogue rejected");
        Assert(!MZP5InboundReader.Open(cat, null).IsOfflineCandidate,
            "missing source scope rejected");

        byte[] missingEvent = Encoding.UTF8.GetBytes(
            """{"GameMessage":{"request":7,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2,"uid":6}}""");
        Assert(!MZP5InboundReader.Open(Catalogue(fields: missingEvent), Scope with
                { FieldsSha256 = Sha(missingEvent) }).IsOfflineCandidate,
            "inbound schema must contain Event branch");
        byte[] duplicateEnvelopeTags = Encoding.UTF8.GetBytes(
            """{"GameMessage":{"request":7,"event":7,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2,"uid":6}}""");
        // Upstream MZP0 admission already refuses ambiguous tags.
        var ambiguity = MZP4ProtocolAdapter.Open(Matching, duplicateEnvelopeTags,
            new MZP4CatalogReference("3.7.10.10", Sha(Matching),
                Sha(duplicateEnvelopeTags), "synthetic-fixture"));
        Assert(!ambiguity.IsOfflineCandidate,
            "MZP0 rejects duplicate GameMessage branch tags, not self-certified");
        byte[] responseMissingUid = Encoding.UTF8.GetBytes(
            """{"GameMessage":{"request":7,"event":5,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2}}""");
        Assert(!MZP5InboundReader.Open(Catalogue(fields: responseMissingUid),
                Scope with { FieldsSha256 = Sha(responseMissingUid) }).IsOfflineCandidate,
            "response UID tag must exist before opening reader");
        byte[] invalidLabel = new byte[] { 0x00 };
        for (int i = 0; i < 50; i++)
        {
            var bad = Reader().ReadUnframed(
                Observation(1, MZP5Direction.ServerToClient), invalidLabel);
            Assert(bad.Status == MZP5InboundStatus.MalformedWire &&
                bad.Packet is null && !bad.IsAuthenticated,
                "malformed fixture no partial result " + (i + 1));
        }

        Console.WriteLine($"MZP5 PROTOCOL: {_passed}/{_passed + _failed} PASS");
        if (_failed > 0) Environment.ExitCode = 1;
    }
}
