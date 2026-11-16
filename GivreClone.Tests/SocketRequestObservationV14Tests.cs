extern alias F06App;

using System.Globalization;
using System.IO;
using System.Reflection;
using CaptureLink = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using HostService = F06App::GivreClone.App.Services.BotHostService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Real SocketGameClient queue/drain and framed-observer entrypoints, the
/// SocketProbe ownership and exact-link boundary, and the V14 ledger's
/// uncorrelated read diagnostic. In-memory only; no network, login, gameplay
/// engine, fabricated UID or ACK.
/// </summary>
public sealed class SocketRequestObservationV14Tests
{
    private static readonly Assembly App = typeof(HostService).Assembly;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type Socket(string name) => App.GetType(
        "GivreClone.App.Services.Transport.Socket." + name, true)!;

    private static readonly Type Game = Socket("SocketGameClient");
    private static readonly Type Source = Socket("SocketLinkProvenance");
    private static readonly Type Frame = Socket("SocketTransportFrameObservation");
    private static readonly Type Written = Socket("SocketTransportWriteObservation");
    private static readonly Type Parameters = Socket("SocketGameParams");
    private static readonly Type Registry = Socket("SocketSessionOwnership");
    private static readonly Type Cycle = Socket("SocketSourceCycle");
    private static readonly Type Ledger = App.GetType(
        "GivreClone.App.Services.SocketSourceAuthorityLedger", true)!;

    private static object Origin(uint pid = 96014, long run = 1,
        Guid? client = null, Guid? connection = null) =>
        Activator.CreateInstance(Source,
            [client ?? Guid.NewGuid(), run, connection ?? Guid.NewGuid(), pid])!;

    private static T Property<T>(object subject, string name) =>
        (T)subject.GetType().GetProperty(name)!.GetValue(subject)!;

    private static object Client(uint pid, object source, CancellationTokenSource session)
    {
        var game = Activator.CreateInstance(Game, [pid])!;
        Game.GetField("_currentSource", Hidden)!.SetValue(game, source);
        Game.GetField("_sessionCancellation", Hidden)!.SetValue(game, session);
        Game.GetField("_injectionReady", Hidden)!.SetValue(game, 1);
        Game.GetField("_injectorPublished", Hidden)!.SetValue(game, 1);
        return game;
    }

    private static object NewLedger() => Activator.CreateInstance(Ledger,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, [true], CultureInfo.InvariantCulture)!;

    private static CaptureLink Link(object source)
    {
        var run = Property<long>(source, "RunEpoch");
        return new CaptureLink(
            Property<Guid>(source, "ClientInstance"),
            Property<uint>(source, "SyntheticPid"),
            run, run, Property<Guid>(source, "ConnectionToken"));
    }

    private static bool ObserveRead(object ledger, CaptureLink link, long sequence,
        bool outbound = false, int length = 3) =>
        (bool)Ledger.GetMethod("ObserveFramedChunk")!
            .Invoke(ledger, [link, sequence, outbound, length])!;

    private static bool ObserveWrite(object ledger, CaptureLink link, long ordinal,
        int length = 3) => (bool)Ledger.GetMethod("ObserveEngineWrite")!
            .Invoke(ledger, [link, ordinal, length])!;

    private static object? Inspect(object ledger, CaptureLink link) =>
        Ledger.GetMethod("InspectRequestObservation")!.Invoke(ledger, [link]);

    private static Action<T> ActionAdapter<T>(Action<object> callback) =>
        item => callback(item!);
    private static Func<T, bool> PredicateAdapter<T>(Func<object, bool> callback) =>
        item => callback(item!);

    private static Delegate Handler(Type itemType, Action<object> action) =>
        (Delegate)typeof(SocketRequestObservationV14Tests)
            .GetMethod(nameof(ActionAdapter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(itemType).Invoke(null, [action])!;

    private static Delegate Validator(Func<object, bool> validate) =>
        (Delegate)typeof(SocketRequestObservationV14Tests)
            .GetMethod(nameof(PredicateAdapter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(Source).Invoke(null, [validate])!;

    private static object Params(Delegate? onWritten = null, Delegate? onFramed = null)
    {
        var p = Activator.CreateInstance(Parameters, true)!;
        if (onWritten is not null)
            Parameters.GetProperty("OutboundWritten")!.SetValue(p, onWritten);
        if (onFramed is not null)
            Parameters.GetProperty("FeedObserved")!.SetValue(p, onFramed);
        return p;
    }

    private static object Dequeue(object client)
    {
        var queue = Game.GetField("_outbound", Hidden)!.GetValue(client)!;
        var reader = queue.GetType().GetProperty("Reader")!.GetValue(queue)!;
        object?[] args = [null];
        Assert.True((bool)reader.GetType().GetMethod("TryRead")!
            .Invoke(reader, args)!);
        return Assert.IsAssignableFrom<object>(args[0]);
    }

    private static Task<bool> Drain(object game, Stream wire, object outbound,
        object parameters, CancellationToken ct) =>
        (Task<bool>)Game.GetMethod("WriteQueuedEngineActionAsync", Hidden)!
            .Invoke(game, [wire, outbound, parameters, ct])!;

    private static void Publish(object game, object parameters, bool outbound,
        byte[] framed) => Game.GetMethod("PublishObserved", Hidden)!
            .Invoke(game, [parameters, outbound, framed]);

    private static object NewRegistry() => Activator.CreateInstance(Registry, true)!;

    private static object Claim(object registry, uint pid, Action revoke,
        Func<object, bool> validate, Action<uint> unregister) =>
        Registry.GetMethod("Claim", Hidden)!
            .Invoke(registry, [pid, revoke, Validator(validate), unregister])!;

    private static bool Register(object registry, object lease, Action action) =>
        (bool)Registry.GetMethod("TryRegister", Hidden)!
            .Invoke(registry, [lease, action])!;

    private static bool IsOwner(object registry, object lease) =>
        (bool)Registry.GetMethod("IsCurrent", Hidden)!
            .Invoke(registry, [lease])!;

    private static bool Release(object registry, object lease, Action<uint> unregister) =>
        (bool)Registry.GetMethod("Release", Hidden)!
            .Invoke(registry, [lease, unregister])!;

    [Fact]
    public void Framed_observation_keeps_original_snapshot_between_mutating_subscribers()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin();
        var game = Client(96014, source, lifetime);
        var observed = new List<byte[]>();
        var first = Handler(Frame, frame =>
        {
            var mutableCopy = Property<byte[]>(frame, "WireFrame");
            mutableCopy[1] = 0xFF;
            observed.Add(mutableCopy);
        });
        var second = Handler(Frame, frame =>
            observed.Add(Property<byte[]>(frame, "WireFrame")));
        byte[] caller = [3, 8, 1, 2];
        Publish(game, Params(onFramed: Delegate.Combine(first, second)), false, caller);
        caller[1] = 0xEE;
        Assert.Equal(2, observed.Count);
        Assert.Equal(0xFF, observed[0][1]);
        Assert.Equal(new byte[] { 3, 8, 1, 2 }, observed[1]);
    }

    [Fact]
    public void Frame_constructor_owns_bytes_and_copy_on_read_preserves_v9_byte_array_API()
    {
        var source = Origin();
        byte[] original = [2, 8, 1];
        var frame = Activator.CreateInstance(Frame, [1L, source, false, original])!;
        original[1] = 0xFF;
        var external = Property<byte[]>(frame, "WireFrame");
        external[2] = 0xEE;
        Assert.Equal(new byte[] { 2, 8, 1 },
            Property<byte[]>(frame, "WireFrame"));
        Assert.Equal(3, Property<int>(frame, "WireLength"));
        Assert.False(Property<bool>(frame, "ClientToServer"));
        object?[] parts = [null, null, null, null];
        Frame.GetMethod("Deconstruct")!.Invoke(frame, parts);
        Assert.Equal(1L, parts[0]);
        Assert.Equal(source, parts[1]);
        Assert.False((bool)parts[2]!);
        var deconstructed = (byte[])parts[3]!;
        deconstructed[0] = 0xFF;
        Assert.Equal(new byte[] { 2, 8, 1 },
            Property<byte[]>(frame, "WireFrame"));
    }

    [Fact]
    public void Stop_reentrant_in_first_receive_subscriber_discards_later_subscribers()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin();
        var game = Client(96014, source, lifetime);
        int first = 0, later = 0;
        var onFrame = Delegate.Combine(
            Handler(Frame, _ =>
            {
                first++;
                Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            }),
            Handler(Frame, _ => later++));
        var config = Params(onFramed: onFrame);
        Publish(game, config, false, [2, 8, 1]);
        Publish(game, config, false, [2, 8, 2]);
        Assert.Equal(1, first);
        Assert.Equal(0, later);
    }

    [Fact]
    public void Socket_source_cycle_retires_exact_link_once_and_denies_late_frames_after_eof()
    {
        var source = Origin();
        var later = Origin(96014, 2);
        var cycle = Activator.CreateInstance(Cycle, true)!;
        Assert.True((bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [source])!);
        int calls = 0;
        var captured = new List<object>();
        var revoke = Handler(Source, value => { calls++; captured.Add(value); });
        Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle, [revoke]);
        Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle, [revoke]);
        Assert.Equal(1, calls);
        Assert.Equal(source, Assert.Single(captured));
        Assert.False((bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [source])!);
        Assert.False((bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [later])!);
        object?[] outArgs = [null];
        Assert.False((bool)Cycle.GetMethod("TryGet", Hidden)!.Invoke(cycle, outArgs)!);
    }

    [Fact]
    public void Exact_revoke_failure_can_retry_same_source_without_reopening_late_ingress()
    {
        var source = Origin();
        var cycle = Activator.CreateInstance(Cycle, true)!;
        Assert.True((bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [source])!);
        int attempts = 0;
        var revoke = Handler(Source, observed =>
        {
            Assert.Equal(source, observed);
            if (++attempts == 1) throw new IOException("temporary callback failure");
        });
        var failure = Assert.Throws<TargetInvocationException>(() =>
            Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle, [revoke]));
        Assert.IsType<IOException>(failure.InnerException);
        Assert.False((bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [source])!);
        Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle, [revoke]);
        Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle, [revoke]);
        Assert.Equal(2, attempts); // failed once, exact retry, no third callback
    }

    [Fact]
    public void Reentrant_registration_cannot_mark_old_same_pid_lease_registered_after_replacement()
    {
        var registry = NewRegistry();
        const uint pid = 96101;
        int oldRevocations = 0;
        var initial = Claim(registry, pid, () => oldRevocations++, _ => true, _ => { });
        object? successor = null;
        var registered = Register(registry, initial, () =>
            successor = Claim(registry, pid, () => { }, _ => true, _ => { }));
        Assert.False(registered);
        Assert.NotNull(successor);
        Assert.Equal(1, oldRevocations);
        Assert.False(IsOwner(registry, initial));
        Assert.True(IsOwner(registry, successor!));
        Assert.True(Register(registry, successor!, () => { }));
        Assert.False(Release(registry, initial, _ => { }));
        Assert.True(Release(registry, successor!, _ => { }));
    }

    [Fact]
    public async Task Actual_socket_inject_then_postflush_then_read_is_temporal_not_individual_ack()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 96102;
        var source = Origin(pid, run: 51);
        var link = Link(source);
        var ledger = NewLedger();
        var game = Client(pid, source, lifetime);
        var written = new List<object>();
        var inbound = new List<object>();
        var config = Params(
            onWritten: Handler(Written, item =>
            {
                written.Add(item);
                Assert.True(ObserveWrite(ledger, link, Property<long>(item, "Ordinal"),
                    Property<int>(item, "WireLength")));
            }),
            onFramed: Handler(Frame, item =>
            {
                inbound.Add(item);
                Assert.True(ObserveRead(ledger, link,
                    Property<long>(item, "Sequence"),
                    Property<bool>(item, "ClientToServer"),
                    Property<int>(item, "WireLength")));
            }));
        Assert.True((bool)Game.GetMethod("Inject")!
            .Invoke(game, [new byte[] { 2, 8, 1 }])!);
        Assert.Null(Inspect(ledger, link));
        using var wire = new MemoryStream();
        Assert.True(await Drain(game, wire, Dequeue(game), config, lifetime.Token));
        Assert.Single(written);
        Assert.Empty(inbound);
        // The read is an isolated in-memory framed observation, NOT a
        // correlated server response. It uses the production observer path.
        Publish(game, config, false, [2, 8, 9]);
        Assert.Single(inbound);
        var diagnostic = Inspect(ledger, link)!;
        Assert.Equal(1, Property<long>(diagnostic, "LastPostFlushOrdinal"));
        Assert.Equal(1, Property<long>(diagnostic, "ServerFramesWhileWriteObserved"));
        Assert.Equal(1, Property<long>(diagnostic, "LastServerFrameSequence"));
        Assert.False(Property<bool>(diagnostic, "IndividualResponseIdentified"));
        Assert.False(Property<bool>(diagnostic, "ServerAcknowledged"));
        Assert.False(Property<bool>(diagnostic, "CanAuthorizeGameplay"));
        Assert.Contains("identifiant individuel",
            Property<string>(diagnostic, "BlockReason"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ledger_receive_before_local_write_and_outgoing_protocol_frames_never_become_reply()
    {
        var source = Origin(96103);
        var link = Link(source);
        var ledger = NewLedger();
        Assert.True(ObserveRead(ledger, link, 1));
        Assert.True(ObserveRead(ledger, link, 2, outbound: true));
        Assert.True(ObserveWrite(ledger, link, 1));
        var state = Inspect(ledger, link)!;
        Assert.Equal(0, Property<long>(state, "ServerFramesWhileWriteObserved"));
        Assert.Equal(1, Property<long>(state, "LastPostFlushOrdinal"));
        Assert.False(Property<bool>(state, "ServerAcknowledged"));
        Assert.True(ObserveRead(ledger, link, 3, outbound: true));
        Assert.Equal(0, Property<long>(Inspect(ledger, link)!, "ServerFramesWhileWriteObserved"));
        Assert.True(ObserveRead(ledger, link, 4));
        Assert.Equal(1, Property<long>(Inspect(ledger, link)!, "ServerFramesWhileWriteObserved"));
    }

    [Fact]
    public void Ledger_after_reconnect_rejects_old_exact_link_diagnostic_and_old_eof()
    {
        const uint pid = 96104;
        var first = Link(Origin(pid, 1));
        var replacement = Link(Origin(pid, 2));
        var ledger = NewLedger();
        Assert.True(ObserveWrite(ledger, first, 1));
        Assert.True(ObserveRead(ledger, first, 1));
        Assert.True(ObserveWrite(ledger, replacement, 1));
        Assert.Null(Inspect(ledger, first));
        Assert.NotNull(Inspect(ledger, replacement));
        Assert.False((bool)Ledger.GetMethod("TryRevokeExact")!
            .Invoke(ledger, [first])!);
        Assert.NotNull(Inspect(ledger, replacement));
        Assert.True(ObserveRead(ledger, replacement, 1));
        Assert.Equal(1, Property<long>(Inspect(ledger, replacement)!,
            "ServerFramesWhileWriteObserved"));
    }

    [Fact]
    public async Task Stop_before_queued_drain_cannot_create_written_or_receive_correlation()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 96105;
        var source = Origin(pid);
        var ledger = NewLedger();
        var game = Client(pid, source, lifetime);
        Assert.True((bool)Game.GetMethod("Inject")!
            .Invoke(game, [new byte[] { 2, 8, 1 }])!);
        var queued = Dequeue(game);
        Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        using var wire = new MemoryStream();
        Assert.False(await Drain(game, wire, queued,
            Params(onWritten: Handler(Written, item =>
                ObserveWrite(ledger, Link(source),
                    Property<long>(item, "Ordinal")))), lifetime.Token));
        Assert.Empty(wire.ToArray());
        Assert.Null(Inspect(ledger, Link(source)));
        var attempted = Game.GetMethod("ReadOutboundJourneyCounters")!
            .Invoke(game, null)!;
        Assert.Equal(0, Property<long>(attempted, "WriteAttempted"));
        Assert.Equal(0, Property<long>(attempted, "PostFlushWritten"));
    }

    [Fact]
    public void Bounded_frame_observation_reentrant_publish_preserves_source_scoped_fifo()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin();
        var game = Client(96014, source, lifetime);
        var delivered = new List<long>();
        object? config = null;
        config = Params(onFramed: Handler(Frame, item =>
        {
            var sequence = Property<long>(item, "Sequence");
            delivered.Add(sequence);
            if (sequence == 1) Publish(game, config!, false, [2, 8, 2]);
        }));
        Publish(game, config, false, [2, 8, 1]);
        Assert.Equal(new long[] { 1, 2 }, delivered);
        Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        Publish(game, config, false, [2, 8, 3]);
        Assert.Equal(new long[] { 1, 2 }, delivered);
    }
}
