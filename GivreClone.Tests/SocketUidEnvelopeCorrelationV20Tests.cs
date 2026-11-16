extern alias F06App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Snapshot = F06App::GivreClone.App.Services.SocketUidEnvelopeCorrelationV20Snapshot;

namespace GivreClone.Tests;

/// <summary>
/// Envelope-level P/N cases using real framed protobuf and the actual V20
/// ledger entrypoints. Test responses are SYNTHETIC; no game network or
/// session is used and these cases do not certify gameplay acknowledgements.
/// </summary>
public sealed class SocketUidEnvelopeCorrelationV20Tests
{
    private const uint Pid = 96120;
    private static readonly Type Ledger =
        typeof(Snapshot).Assembly.GetType(
            "GivreClone.App.Services.SocketUidEnvelopeCorrelationV20", true)!;

    // Pinned, explicit field slots used by the fixture only, not a claim of
    // current game-version provenance.
    private static readonly Func<string, string, int?> Slots = (message, field) =>
        (message, field) switch
        {
            ("GameMessage", "request") => 4,
            ("GameMessage", "event") => 3,
            ("GameMessage", "response") => 1,
            ("Request", "uid") => 1,
            ("Request", "content") => 2,
            ("Response", "uid") => 1,
            ("Response", "content") => 2,
            _ => null
        };

    private static object NewLedger() => Activator.CreateInstance(Ledger, true)!;

    private static Link Source(long run = 1, Guid? instance = null,
        Guid? token = null) => new(instance ?? Guid.NewGuid(), Pid, run, run,
            token ?? Guid.NewGuid());

    private static bool Written(object ledger, Link source, long ordinal, byte[] bytes)
        => (bool)Ledger.GetMethod("ObservePostFlush")!
            .Invoke(ledger, [source, ordinal, bytes, Slots])!;

    private static bool Receive(object ledger, Link source, long sequence, byte[] bytes)
        => (bool)Ledger.GetMethod("ObserveServerFrame")!
            .Invoke(ledger, [source, sequence, bytes, Slots])!;

    private static Snapshot? Read(object ledger, uint pid = Pid)
        => (Snapshot?)Ledger.GetMethod("Read")!.Invoke(ledger, [pid]);

    private static bool Revoke(object ledger, Link source)
        => (bool)Ledger.GetMethod("RevokeExact")!.Invoke(ledger, [source])!;

    private static byte[] Envelope(int branch, long? uid, bool content = true)
    {
        var fields = new List<ProtoField>();
        if (uid is { } value)
            fields.Add(new ProtoField
            {
                Field = 1, Wire = WireType.Varint,
                Varint = unchecked((ulong)value)
            });
        if (content)
            fields.Add(new ProtoField
            {
                Field = 2, Wire = WireType.Len,
                Message =
                [
                    new ProtoField { Field = 1, Wire = WireType.Len,
                        String = "type.ankama.com/test" },
                    new ProtoField { Field = 2, Wire = WireType.Len,
                        Message = [new ProtoField { Field = 1,
                            Wire = WireType.Varint, Varint = 7 }] }
                ]
            });
        byte[] raw = RawProtobuf.Encode(
        [
            new ProtoField { Field = branch, Wire = WireType.Len,
                Message = fields }
        ]);
        return RawProtobuf.EncodeVarint((ulong)raw.Length)
            .Concat(raw).ToArray();
    }

    private static byte[] Request(long? uid) => Envelope(4, uid);
    private static byte[] Response(long? uid) => Envelope(1, uid);
    private static byte[] Event(long? uid) => Envelope(3, uid);

    [Fact]
    public void Actual_launch_socket_observation_ports_match_only_current_source_uid_without_authorization()
    {
        // Real LaunchService.FeedSocketWrittenObservation and
        // FeedSocketObservedFrame entrypoints: the producer callbacks below
        // are explicitly SIMULATED, not a real TCP session or server ACK.
        var catalogue = ProtocolCatalog.LoadFromStrings(null,
            """{"type.ankama.com/test":"MessageType.TestMessage"}""",
            null,
            """{"GameMessage":{"request":4,"event":3,"response":1},"Request":{"uid":1,"content":2},"Response":{"uid":1,"content":2}}""");
        var launch = new Launch();
        typeof(Launch).GetField("_catalog",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(launch, catalogue);
        typeof(Launch).GetField("_decoder",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(launch, new Decoder(catalogue));

        var originType = typeof(Launch).Assembly.GetType(
            "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
        var source = Activator.CreateInstance(originType,
            Guid.NewGuid(), 1L, Guid.NewGuid(), Pid)!;
        var parameter = Expression.Parameter(originType, "source");
        var guard = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(originType, typeof(bool)),
            Expression.Equal(parameter, Expression.Constant(source, originType)),
            parameter).Compile();

        var writeType = typeof(Launch).Assembly.GetType(
            "GivreClone.App.Services.Transport.Socket.SocketTransportWriteObservation", true)!;
        var ctor = writeType.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(x => x.GetParameters().Length == 6);
        var write = ctor.Invoke([source, 1L, 1L, 1L, Request(117), Environment.TickCount64]);
        var writeMethod = typeof(Launch).GetMethod("FeedSocketWrittenObservation",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)writeMethod.Invoke(launch, [write, guard])!);
        Assert.Equal(0,
            Assert.IsType<Snapshot>(launch.SocketUidEnvelopeEvidenceOf(Pid))
                .MatchingResponseEnvelopes);

        var frameType = typeof(Launch).Assembly.GetType(
            "GivreClone.App.Services.Transport.Socket.SocketTransportFrameObservation", true)!;
        var observed = Activator.CreateInstance(frameType, 1L, source, false, Response(117))!;
        var frameMethod = typeof(Launch).GetMethod("FeedSocketObservedFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)frameMethod.Invoke(launch, [observed, guard])!);
        var matched = Assert.IsType<Snapshot>(launch.SocketUidEnvelopeEvidenceOf(Pid));
        Assert.Equal(117, matched.LastMatchedUid);
        Assert.Equal(1, matched.MatchingResponseEnvelopes);
        Assert.False(matched.CanAuthorizeGameplay);
        Assert.False(matched.GameplayOutcomeVerified);

        // Wrong source has no authority; old exact link retirement revokes
        // its diagnostic but creates no gameplay permission.
        var oldRevoke = typeof(Launch).GetMethod("RevokeSocketObservedLink",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        oldRevoke.Invoke(launch, [source]);
        Assert.Null(launch.SocketUidEnvelopeEvidenceOf(Pid));
        Assert.False((bool)frameMethod.Invoke(launch, [observed, guard])!);
    }

    [Fact]
    public void Matching_unique_positive_uid_observes_response_envelope_but_never_gameplay_ack()
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.True(Written(ledger, source, 1, Request(27)));
        Assert.True(Receive(ledger, source, 1, Response(27)));
        var result = Assert.IsType<Snapshot>(Read(ledger));
        Assert.Equal(source, result.Link);
        Assert.Equal(1, result.PostFlushRequestsWithUniqueUid);
        Assert.Equal(1, result.MatchingResponseEnvelopes);
        Assert.Equal(27, result.LastMatchedUid);
        Assert.True(result.PositiveUidResponseObserved);
        Assert.False(result.GameplayOutcomeVerified);
        Assert.False(result.CanAuthorizeGameplay);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Shared_default_or_omitted_uid_never_identifies_one_request(long defaultUid)
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.False(Written(ledger, source, 1, Request(defaultUid)));
        Assert.False(Receive(ledger, source, 1, Response(defaultUid)));
        var result = Assert.IsType<Snapshot>(Read(ledger));
        Assert.Equal(0, result.MatchingResponseEnvelopes);
        Assert.False(result.PositiveUidResponseObserved);
    }

    [Fact]
    public void Incoming_event_with_matching_uid_or_unrelated_response_cannot_complete_request()
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.True(Written(ledger, source, 1, Request(51)));
        Assert.False(Receive(ledger, source, 1, Event(51)));
        Assert.False(Receive(ledger, source, 2, Response(52)));
        Assert.True(Receive(ledger, source, 3, Response(51)));
        var result = Assert.IsType<Snapshot>(Read(ledger));
        Assert.Equal(1, result.UnmatchedResponseEnvelopes);
        Assert.Equal(1, result.MatchingResponseEnvelopes);
        Assert.False(result.CanAuthorizeGameplay);
    }

    [Fact]
    public void Duplicate_request_uid_revokes_its_pending_candidate_instead_of_correlating()
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.True(Written(ledger, source, 1, Request(73)));
        Assert.False(Written(ledger, source, 2, Request(73)));
        Assert.False(Receive(ledger, source, 1, Response(73)));
        var result = Assert.IsType<Snapshot>(Read(ledger));
        Assert.Equal(0, result.MatchingResponseEnvelopes);
        Assert.True(result.AmbiguousOrReusedUids > 0);
    }

    [Fact]
    public void Duplicate_server_response_and_replayed_sequence_are_not_second_ack()
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.True(Written(ledger, source, 1, Request(81)));
        Assert.True(Receive(ledger, source, 1, Response(81)));
        Assert.False(Receive(ledger, source, 1, Response(81)));
        Assert.False(Receive(ledger, source, 2, Response(81)));
        var result = Assert.IsType<Snapshot>(Read(ledger));
        Assert.Equal(1, result.MatchingResponseEnvelopes);
        Assert.Equal(0, result.LastMatchedUid);
        Assert.True(result.AmbiguousOrReusedUids > 0);
        Assert.False(result.PositiveUidResponseObserved);
        Assert.False(result.CanAuthorizeGameplay);
    }

    [Fact]
    public void Reconnect_discards_old_source_and_old_eof_cannot_revoke_new_uid()
    {
        var ledger = NewLedger();
        var instance = Guid.NewGuid();
        var first = Source(1, instance);
        var second = Source(2, instance);
        Assert.True(Written(ledger, first, 1, Request(101)));
        Assert.True(Written(ledger, second, 1, Request(102)));
        Assert.False(Receive(ledger, first, 1, Response(101)));
        Assert.False(Revoke(ledger, first));
        Assert.True(Receive(ledger, second, 1, Response(102)));
        Assert.Equal(second, Assert.IsType<Snapshot>(Read(ledger)).Link);
        Assert.True(Revoke(ledger, second));
        Assert.Null(Read(ledger));
        Assert.False(Receive(ledger, second, 2, Response(102)));
    }

    [Fact]
    public void Missing_postflush_and_wrong_source_have_no_response_authority()
    {
        var ledger = NewLedger();
        var source = Source();
        Assert.False(Receive(ledger, source, 1, Response(7)));
        Assert.True(Written(ledger, source, 1, Request(7)));
        Assert.False(Receive(ledger, Source(), 2, Response(7)));
        Assert.True(Receive(ledger, source, 2, Response(7)));
        Assert.False(Written(ledger, source, 1, Request(8)));
        Assert.False(Receive(ledger, source, 2, Response(7)));
    }

    [Fact]
    public void Partial_duplicate_and_ambiguous_wire_are_not_admitted()
    {
        var ledger = NewLedger();
        var source = Source();
        byte[] framed = Request(5);
        Assert.False(Written(ledger, source, 1, framed[..^2]));
        Assert.False(Written(ledger, source, 2,
            framed.Concat(framed).ToArray()));
        Assert.False(Written(ledger, source, 3, Envelope(4, 5, content: false)));
        Assert.False(Receive(ledger, source, 1, Response(5)));
        Assert.Equal(0, Assert.IsType<Snapshot>(Read(ledger)).MatchingResponseEnvelopes);
    }
}
