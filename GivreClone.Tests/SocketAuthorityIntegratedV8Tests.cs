extern alias F06App;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Xunit.Abstractions;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

/// <summary>
/// Exercises the real LaunchService Socket observation and post-flush write
/// ingress. No network socket, game client or server receipt is synthesized.
/// These tests never claim that a local TCP write is a DOFUS cast ACK.
/// </summary>
[Collection("Transport capture diagnostic catalog")]
public sealed class SocketAuthorityIntegratedV8Tests
{
    private readonly ITestOutputHelper _output;
    public SocketAuthorityIntegratedV8Tests(ITestOutputHelper output) => _output = output;

    private static readonly Assembly App = typeof(Launch).Assembly;
    private static readonly Type SourceType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
    private static readonly Type FrameType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportFrameObservation", true)!;
    private static readonly Type WriteType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportWriteObservation", true)!;
    private static readonly Type LedgerType = App.GetType(
        "GivreClone.App.Services.SocketSourceAuthorityLedger", true)!;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static Launch InertService()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        typeof(Launch).GetField("_decoder", Hidden)!.SetValue(service, new Decoder(Catalog));
        typeof(Launch).GetField("_decoderInit", Hidden)!.SetValue(service, true);
        typeof(Launch).GetField("_socketSourceAuthority", Hidden)!
            .SetValue(service, Activator.CreateInstance(LedgerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [true], CultureInfo.InvariantCulture)!);
        return service;
    }

    private static object Source(uint pid, long epoch, Guid instance, Guid connection)
        => Activator.CreateInstance(SourceType, [instance, epoch, connection, pid])!;
    private static object Frame(object source, long sequence, bool outgoing, byte[] wire)
        => Activator.CreateInstance(FrameType, [sequence, source, outgoing, wire])!;
    private static object Written(object source, long ordinal, byte[] wire)
        => Activator.CreateInstance(WriteType, Hidden, null,
            [source, ordinal, wire, Environment.TickCount64], CultureInfo.InvariantCulture)!;

    private static Func<T, bool> Adapt<T>(Func<object, bool> predicate) => value => predicate(value!);
    private static Delegate Validator(Func<object, bool> predicate)
        => (Delegate)typeof(SocketAuthorityIntegratedV8Tests)
            .GetMethod(nameof(Adapt), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(SourceType).Invoke(null, [predicate])!;
    private static bool Feed(Launch service, object obs, Func<object, bool> current)
        => (bool)typeof(Launch).GetMethod("FeedSocketObservedFrame", Hidden)!
            .Invoke(service, [obs, Validator(current)])!;
    private static bool Write(Launch service, object obs, Func<object, bool> current)
        => (bool)typeof(Launch).GetMethod("FeedSocketWrittenObservation", Hidden)!
            .Invoke(service, [obs, Validator(current)])!;
    private static void Revoke(Launch service, object source)
        => typeof(Launch).GetMethod("RevokeSocketObservedLink", Hidden)!
            .Invoke(service, [source]);

    private static byte[] FramedGameMessage()
    {
        string code = Catalog.CodeForName("FightStartingEvent")!;
        byte[] body = EnvelopeBuilder.BuildServerEvent(code,
            [new ProtoField
            {
                Field = Catalog.FieldNumber("FightStartingEvent", "fight_id") ?? 8101,
                Wire = WireType.Varint, Varint = 19,
            }]);
        uint len = (uint)body.Length;
        var prefix = new System.Collections.Generic.List<byte>();
        do
        {
            byte next = (byte)(len & 0x7f);
            len >>= 7;
            prefix.Add(len == 0 ? next : (byte)(next | 0x80));
        } while (len > 0);
        return prefix.Concat(body).ToArray();
    }

    [Fact]
    public void Real_launch_reports_local_post_flush_write_and_rx_as_distinct_non_ack_facts()
    {
        var service = InertService();
        const uint pid = 70301;
        object source = Source(pid, 91, Guid.NewGuid(), Guid.NewGuid());
        byte[] wire = FramedGameMessage();

        Assert.True(Feed(service, Frame(source, 1, false, wire),
            candidate => Equals(source, candidate)));
        var before = service.SocketSourceAuthorityOf(pid);
        Assert.True(before.HasValue);
        Assert.Equal(1, before.Value.ServerFramesObserved);
        Assert.Equal(0, before.Value.EngineWritesCompleted);
        Assert.False(before.Value.ServerAckAttributed);

        object written = Written(source, 1, wire);
        Assert.True(Write(service, written, candidate => Equals(source, candidate)));
        var after = service.SocketSourceAuthorityOf(pid)!.Value;
        Assert.Equal(1, after.ServerFramesObserved);
        Assert.Equal(1, after.EngineWritesCompleted);
        Assert.Equal(wire.Length, after.EngineWriteBytes);
        Assert.True(after.OutgoingEngineWritesAreLocalOnly);
        Assert.False(after.GameplayAuthorizationAvailable);
        Assert.Contains("ACK", after.BlockReason, StringComparison.Ordinal);
        Assert.False(Write(service, written, candidate => Equals(source, candidate)));
        Assert.Equal(1, service.SocketSourceAuthorityOf(pid)!.Value.EngineWritesCompleted);
    }

    [Fact]
    public void Write_claim_does_not_promote_legacy_PacketDecoded_or_cast_authority()
    {
        var service = InertService();
        const uint pid = 70302;
        var source = Source(pid, 4, Guid.NewGuid(), Guid.NewGuid());
        int legacy = 0;
        ((Decoder)typeof(Launch).GetField("_decoder", Hidden)!.GetValue(service)!)
            .Packet += _ => legacy++;

        Assert.True(Write(service, Written(source, 1, FramedGameMessage()),
            candidate => Equals(source, candidate)));
        Assert.Equal(0, legacy);
        var state = service.SocketSourceAuthorityOf(pid)!.Value;
        Assert.Equal(0, state.ServerFramesObserved);
        Assert.Equal(1, state.EngineWritesCompleted);
        Assert.False(state.ServerAckAttributed);
    }

    [Fact]
    public void Stale_connection_pid_reuse_and_reentrant_revocation_cannot_lend_authority()
    {
        var service = InertService();
        const uint pid = 70303;
        object old = Source(pid, 5, Guid.NewGuid(), Guid.NewGuid());
        object next = Source(pid, 6, Guid.NewGuid(), Guid.NewGuid());
        byte[] frame = FramedGameMessage();
        Assert.True(Write(service, Written(old, 1, frame), c => Equals(c, old)));
        Revoke(service, old);
        Assert.Null(service.SocketSourceAuthorityOf(pid));
        Assert.False(Write(service, Written(old, 2, frame), _ => false));
        Assert.True(Feed(service, Frame(next, 1, false, frame), c => Equals(c, next)));
        Assert.True(Write(service, Written(next, 1, frame), c => Equals(c, next)));
        Revoke(service, old); // late EOF from predecessor
        var state = service.SocketSourceAuthorityOf(pid)!.Value;
        Assert.Equal(6, state.RunEpoch);
        Assert.Equal(1, state.ServerFramesObserved);
        Assert.Equal(1, state.EngineWritesCompleted);

        int checks = 0;
        Assert.False(Write(service, Written(next, 2, frame),
            c => Equals(c, next) && ++checks < 2)); // source flips during callback
        Assert.Null(service.SocketSourceAuthorityOf(pid));
    }

    [Fact]
    public void Missing_source_invalid_sequence_or_missing_post_flush_receipt_is_not_evidence()
    {
        var service = InertService();
        object source = Source(70304, 1, Guid.NewGuid(), Guid.NewGuid());
        var frame = FramedGameMessage();
        Assert.False(Write(service, Written(source, 0, frame), _ => true));
        Assert.False(Write(service, Written(source, 1, frame), _ => false));
        Assert.Null(service.SocketSourceAuthorityOf(70304));
        Assert.False(Feed(service, Frame(source, 0, false, frame), _ => true));
        Assert.False(Feed(service, Frame(source, 1, false, frame), _ => false));
        Assert.Null(service.SocketSourceAuthorityOf(70304));
    }

    [Fact]
    public void Local_projection_cost_is_measured_on_actual_launch_ingress_without_latency_claims()
    {
        var service = InertService();
        object source = Source(70305, 13, Guid.NewGuid(), Guid.NewGuid());
        byte[] wire = FramedGameMessage();
        var current = new Func<object, bool>(candidate => Equals(source, candidate));
        for (int i = 1; i <= 16; i++)
            Assert.True(Write(service, Written(source, i, wire), current));

        const int n = 256;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 17; i < 17 + n; i++)
            Assert.True(Write(service, Written(source, i, wire), current));
        watch.Stop();
        long allocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _output.WriteLine($"V8 LaunchService post-flush diagnostic: {n} synthetic " +
            $"write observations, {watch.Elapsed.TotalMilliseconds:F3} ms, " +
            $"{allocations} B allocated on caller thread; excludes TCP latency, " +
            "real DOFUS performance and server responses.");
        var status = service.SocketSourceAuthorityOf(70305)!.Value;
        Assert.Equal(16 + n, status.EngineWritesCompleted);
        Assert.False(status.ServerAckAttributed);
    }
}
