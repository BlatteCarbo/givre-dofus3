extern alias F06App;

using System.Globalization;
using System.IO;
using System.Reflection;
using CaptureLink = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using HostService = F06App::GivreClone.App.Services.BotHostService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Genuine SocketGameClient queue/write entry, SocketProbe lease and source
/// cycle, and SocketSourceAuthorityLedger accounting. Only in-memory streams;
/// no game, network, live capture, or fabricated server acknowledgement.
/// </summary>
public sealed class SocketSourceCycleV13Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly App = typeof(HostService).Assembly;
    private static Type SocketType(string name) => App.GetType(
        "GivreClone.App.Services.Transport.Socket." + name, true)!;
    private static readonly Type Game = SocketType("SocketGameClient");
    private static readonly Type Parameters = SocketType("SocketGameParams");
    private static readonly Type Source = SocketType("SocketLinkProvenance");
    private static readonly Type Written = SocketType("SocketTransportWriteObservation");
    private static readonly Type Registry = SocketType("SocketSessionOwnership");
    private static readonly Type Cycle = SocketType("SocketSourceCycle");
    private static readonly Type Ledger = App.GetType(
        "GivreClone.App.Services.SocketSourceAuthorityLedger", true)!;

    private static object SourceOf(uint pid = 95013, long epoch = 1,
        Guid? instance = null, Guid? token = null) => Activator.CreateInstance(Source,
            [instance ?? Guid.NewGuid(), epoch, token ?? Guid.NewGuid(), pid])!;

    private static object NewGame(uint pid, object source, CancellationTokenSource token)
    {
        var game = Activator.CreateInstance(Game, [pid])!;
        Set(game, "_sessionCancellation", token);
        Set(game, "_currentSource", source); // simulates completed local dial
        Set(game, "_injectionReady", 1);
        Set(game, "_injectorPublished", 1);
        return game;
    }

    private static void BindGameToLease(object game, object lease)
    {
        var active = lease.GetType().GetProperty("IsActive", Hidden)!;
        Game.GetMethod("BindSessionOwnership")!.Invoke(game,
            [new Func<bool>(() => (bool)active.GetValue(lease)!)]);
    }

    private static void Set(object game, string field, object? value) =>
        Game.GetField(field, Hidden)!.SetValue(game, value);

    private static bool Inject(object game, byte[] frame) =>
        (bool)Game.GetMethod("Inject")!.Invoke(game, [frame])!;

    private static object NextQueued(object game)
    {
        var channel = Game.GetField("_outbound", Hidden)!.GetValue(game)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] args = [null];
        Assert.True((bool)reader.GetType().GetMethod("TryRead")!.Invoke(reader, args)!);
        return Assert.IsAssignableFrom<object>(args[0]);
    }

    private static Action<T> ActionAdapter<T>(Action<object> callback) =>
        item => callback(item!);

    private static Func<T, bool> PredicateAdapter<T>(Func<object, bool> callback) =>
        item => callback(item!);

    private static Delegate Observer(Action<object> callback) =>
        (Delegate)typeof(SocketSourceCycleV13Tests)
            .GetMethod(nameof(ActionAdapter), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Written).Invoke(null, [callback])!;

    private static Delegate SourceAction(Action<object> callback) =>
        (Delegate)typeof(SocketSourceCycleV13Tests)
            .GetMethod(nameof(ActionAdapter), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Source).Invoke(null, [callback])!;

    private static Delegate Predicate(Func<object, bool> callback) =>
        (Delegate)typeof(SocketSourceCycleV13Tests)
            .GetMethod(nameof(PredicateAdapter), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Source).Invoke(null, [callback])!;

    private static object Config(Delegate? outbound = null)
    {
        var p = Activator.CreateInstance(Parameters, true)!;
        if (outbound is not null)
            Parameters.GetProperty("OutboundWritten")!.SetValue(p, outbound);
        return p;
    }

    private static Task<bool> Drain(object game, Stream stream, object queued,
        object config, CancellationToken ct) =>
        (Task<bool>)Game.GetMethod("WriteQueuedEngineActionAsync", Hidden)!
            .Invoke(game, [stream, queued, config, ct])!;

    private static long LongProperty(object value, string property) =>
        (long)value.GetType().GetProperty(property)!.GetValue(value)!;

    private static bool BoolProperty(object value, string property) =>
        (bool)value.GetType().GetProperty(property)!.GetValue(value)!;

    private static object NewRegistry() => Activator.CreateInstance(Registry, true)!;

    private static object Claim(object registry, uint pid, Action revoke,
        Func<object, bool> validate, Action<uint> unregister) =>
        Registry.GetMethod("Claim", Hidden)!.Invoke(registry,
            [pid, revoke, Predicate(validate), unregister])!;

    private static bool IsOwner(object registry, object lease) =>
        (bool)Registry.GetMethod("IsCurrent", Hidden)!.Invoke(registry, [lease])!;

    private static bool Register(object registry, object lease, Action register) =>
        (bool)Registry.GetMethod("TryRegister", Hidden)!
            .Invoke(registry, [lease, register])!;

    private static bool Release(object registry, object lease, Action<uint> unregister) =>
        (bool)Registry.GetMethod("Release", Hidden)!
            .Invoke(registry, [lease, unregister])!;

    private static void Forward(object registry, object lease, object evidence,
        Delegate diagnostic, Delegate further) =>
        Registry.GetMethod("ForwardWritten", Hidden)!
            .Invoke(registry, [lease, evidence, diagnostic, further]);

    private static object NewCycle() => Activator.CreateInstance(Cycle, true)!;

    private static bool Note(object cycle, object source) =>
        (bool)Cycle.GetMethod("Note", Hidden)!.Invoke(cycle, [source])!;

    private static object? CycleSource(object cycle)
    {
        object?[] args = [null];
        bool known = (bool)Cycle.GetMethod("TryGet", Hidden)!.Invoke(cycle, args)!;
        return known ? args[0] : null;
    }

    private static object NewLedger() => Activator.CreateInstance(Ledger,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, [true], CultureInfo.InvariantCulture)!;

    private static bool EngineWritten(object ledger, CaptureLink link, long ordinal) =>
        (bool)Ledger.GetMethod("ObserveEngineWrite")!
            .Invoke(ledger, [link, ordinal, 3])!;

    private static object? LinkOf(object ledger, uint pid) =>
        Ledger.GetMethod("CurrentLink")!.Invoke(ledger, [pid]);

    private sealed class FailingFlush : MemoryStream
    {
        public override Task FlushAsync(CancellationToken ct) =>
            Task.FromException(new IOException("after WriteAsync"));
    }

    [Fact]
    public void Probe_Claim_reentrant_unregister_cannot_overwrite_newer_owner()
    {
        const uint pid = 95101;
        var registry = NewRegistry();
        object? nested = null;
        bool nestedRegisterWasDenied = false;
        int revokeOld = 0;
        Action<uint> unregister = _ =>
        {
            nested = Claim(registry, pid, () => { }, _ => true, _ => { });
            nestedRegisterWasDenied = !Register(registry, nested, () => { });
        };
        var first = Claim(registry, pid, () => revokeOld++, _ => true, unregister);
        Assert.True(Register(registry, first, () => { }));

        var replaced = Claim(registry, pid, () => { }, _ => true, unregister);
        Assert.NotNull(nested);
        Assert.True(nestedRegisterWasDenied);
        Assert.False(IsOwner(registry, first));
        Assert.False(IsOwner(registry, replaced));
        Assert.True(IsOwner(registry, nested!));
        Assert.True(Register(registry, nested!, () => { }));
        Assert.Equal(1, revokeOld);
        Assert.False(Release(registry, replaced, _ => { }));
        Assert.True(Release(registry, nested!, _ => { }));
    }

    [Fact]
    public void Probe_Release_reentrant_unregister_cannot_remove_replacement_owner()
    {
        const uint pid = 95102;
        var registry = NewRegistry();
        object? nested = null;
        Action<uint> unregister = _ =>
        {
            nested = Claim(registry, pid, () => { }, _ => true, _ => { });
            Assert.False(Register(registry, nested, () => { }));
        };
        var first = Claim(registry, pid, () => { }, _ => true, unregister);
        Assert.True(Register(registry, first, () => { }));
        Assert.True(Release(registry, first, unregister));
        Assert.NotNull(nested);
        Assert.False(IsOwner(registry, first));
        Assert.True(IsOwner(registry, nested!));
        Assert.True(Register(registry, nested!, () => { }));
        Assert.False(Release(registry, first, _ => { }));
        Assert.True(Release(registry, nested!, _ => { }));
    }

    [Fact]
    public void Probe_unregistration_failure_still_revokes_old_socket_and_denies_successor()
    {
        const uint pid = 95110;
        var registry = NewRegistry();
        int oldRevoked = 0;
        Action<uint> failedUnregister = _ => throw new IOException("simulated unregister failure");
        var first = Claim(registry, pid, () => oldRevoked++, _ => true,
            failedUnregister);
        Assert.True(Register(registry, first, () => { }));
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Claim(registry, pid, () => { }, _ => true, failedUnregister));
        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal(1, oldRevoked);
        Assert.False(IsOwner(registry, first));
        // Failure cannot reauthorize an old or half-published successor.
        Assert.False(Register(registry, first, () => { }));
    }

    [Fact]
    public void Probe_source_cycle_retires_exact_write_only_link_and_rejects_pid_alias()
    {
        const uint pid = 95103;
        var origin = SourceOf(pid, 11);
        var impostor = SourceOf(pid, 12);
        var cycle = NewCycle();
        Assert.Null(CycleSource(cycle));
        Assert.True(Note(cycle, origin)); // may be postflush-only
        Assert.True(Note(cycle, origin));
        Assert.False(Note(cycle, impostor)); // same pid is not source identity
        Assert.Equal(origin, CycleSource(cycle));
        var revoked = new List<object>();
        Cycle.GetMethod("RevokeExact", Hidden)!.Invoke(cycle,
            [SourceAction(revoked.Add)]);
        Assert.Equal(origin, Assert.Single(revoked));
    }

    [Fact]
    public void Ledger_rejects_written_ordinal_gap_without_poisoning_current_link()
    {
        const uint pid = 95104;
        var ledger = NewLedger();
        var source = new CaptureLink(Guid.NewGuid(), pid, 30, 30, Guid.NewGuid());
        var newer = new CaptureLink(Guid.NewGuid(), pid, 31, 31, Guid.NewGuid());
        Assert.False(EngineWritten(ledger, source, 2)); // no initial 1
        Assert.Null(LinkOf(ledger, pid));
        Assert.True(EngineWritten(ledger, source, 1));
        Assert.False(EngineWritten(ledger, source, 3)); // missing 2
        Assert.Equal(source, LinkOf(ledger, pid));
        Assert.True(EngineWritten(ledger, source, 2));
        Assert.False(EngineWritten(ledger, newer, 9)); // cannot replace by gap
        Assert.Equal(source, LinkOf(ledger, pid));
        Assert.True(EngineWritten(ledger, newer, 1));
        Assert.Equal(newer, LinkOf(ledger, pid));
        Assert.False((bool)Ledger.GetMethod("TryRevokeExact")!
            .Invoke(ledger, [source])!);
        Assert.Equal(newer, LinkOf(ledger, pid));
        var snapshot = Ledger.GetMethod("Read")!.Invoke(ledger, [pid])!;
        Assert.False(BoolProperty(snapshot, "ServerAckAttributed"));
        Assert.False(BoolProperty(snapshot, "GameplayAuthorizationAvailable"));
    }

    [Fact]
    public async Task Game_direct_admission_and_real_postflush_keep_source_and_distinct_ordinals()
    {
        using var lifetime = new CancellationTokenSource();
        var source = SourceOf(95105);
        var game = NewGame(95105, source, lifetime);
        var received = new List<object>();
        object?[] admission = [new byte[] { 2, 8, 1 }, null];
        Assert.True((bool)Game.GetMethod("TryInject")!.Invoke(game, admission)!);
        var receipt = admission[1]!;
        Assert.True(BoolProperty(receipt, "Queued"));
        Assert.False(BoolProperty(receipt, "WriteAttempted"));
        Assert.False(BoolProperty(receipt, "ServerAcknowledged"));
        var before = Game.GetMethod("ReadOutboundJourneyCounters")!.Invoke(game, null)!;
        Assert.Equal(1, LongProperty(before, "Queued"));
        Assert.Equal(0, LongProperty(before, "WriteAttempted"));
        Assert.Equal(0, LongProperty(before, "PostFlushWritten"));
        using var stream = new MemoryStream();
        Assert.True(await Drain(game, stream, NextQueued(game),
            Config(Observer(received.Add)), lifetime.Token));
        var witness = Assert.Single(received);
        Assert.Equal(source, Written.GetProperty("Source")!.GetValue(witness));
        Assert.Equal(LongProperty(receipt, "AdmissionOrdinal"),
            LongProperty(witness, "AdmissionOrdinal"));
        Assert.Equal(1, LongProperty(witness, "AttemptOrdinal"));
        Assert.Equal(1, LongProperty(witness, "Ordinal"));
        Assert.False(BoolProperty(witness, "ServerAcknowledged"));
        Assert.Equal(new byte[] { 2, 8, 1 }, stream.ToArray());
    }

    [Fact]
    public async Task Game_failed_flush_preserves_attempt_but_never_fabricates_written_or_ack()
    {
        using var lifetime = new CancellationTokenSource();
        var source = SourceOf(95106);
        var game = NewGame(95106, source, lifetime);
        var observed = new List<object>();
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new FailingFlush();
        await Assert.ThrowsAsync<IOException>(() =>
            Drain(game, stream, NextQueued(game),
                Config(Observer(observed.Add)), lifetime.Token));
        Assert.Empty(observed);
        var counters = Game.GetMethod("ReadOutboundJourneyCounters")!.Invoke(game, null)!;
        Assert.Equal(1, LongProperty(counters, "Queued"));
        Assert.Equal(1, LongProperty(counters, "WriteAttempted"));
        Assert.Equal(0, LongProperty(counters, "PostFlushWritten"));
        Assert.Equal(1, LongProperty(counters, "AmbiguousWriteFailure"));
    }

    [Fact]
    public async Task Game_stop_before_drain_disallows_reused_pid_and_stale_local_write()
    {
        using var lifetime = new CancellationTokenSource();
        var source = SourceOf(95107);
        var game = NewGame(95107, source, lifetime);
        Assert.True(Inject(game, [2, 8, 1]));
        var pending = NextQueued(game);
        Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        using var stream = new MemoryStream();
        Assert.False(await Drain(game, stream, pending, Config(), lifetime.Token));
        Assert.Empty(stream.ToArray());
        Assert.False(Inject(game, [2, 8, 2]));
        Assert.False((bool)Game.GetMethod("IsCurrentSocketLink")!
            .Invoke(game, [source])!);
    }

    [Fact]
    public void Game_already_retired_lease_refuses_admission_before_any_network_start()
    {
        using var lifetime = new CancellationTokenSource();
        var source = SourceOf(95112);
        var game = NewGame(95112, source, lifetime);
        Game.GetMethod("BindSessionOwnership")!.Invoke(game,
            [new Func<bool>(() => false)]);
        Assert.False(Inject(game, [2, 8, 1]));
        Assert.False((bool)Game.GetMethod("IsCurrentSocketLink")!
            .Invoke(game, [source])!);
        var counters = Game.GetMethod("ReadOutboundJourneyCounters")!.Invoke(game, null)!;
        Assert.Equal(0, LongProperty(counters, "Queued"));
        Assert.Equal(0, LongProperty(counters, "WriteAttempted"));
        Assert.Equal(0, LongProperty(counters, "PostFlushWritten"));
    }

    [Fact]
    public async Task Game_reentrant_stop_in_first_write_observer_cannot_notify_second()
    {
        using var lifetime = new CancellationTokenSource();
        var source = SourceOf(95108);
        var game = NewGame(95108, source, lifetime);
        int first = 0, second = 0;
        var callbacks = Delegate.Combine(
            Observer(_ =>
            {
                first++;
                Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            }),
            Observer(_ => second++));
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new MemoryStream();
        Assert.True(await Drain(game, stream, NextQueued(game),
            Config(callbacks), lifetime.Token));
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.False(Inject(game, [2, 8, 2]));
    }

    [Fact]
    public async Task Probe_real_written_consumer_revoked_in_diagnostic_cannot_notify_next_observer()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 95111;
        var source = SourceOf(pid);
        var game = NewGame(pid, source, lifetime);
        var registry = NewRegistry();
        var lease = Claim(registry, pid,
            () => Game.GetMethod("RevokeForReplacement")!.Invoke(game, null),
            value => Equals(value, source), _ => { });
        BindGameToLease(game, lease);
        int diagnostic = 0, additional = 0;
        var first = Observer(_ =>
        {
            diagnostic++;
            Assert.True(Release(registry, lease, _ => { }));
        });
        var second = Observer(_ => additional++);
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new MemoryStream();
        Assert.True(await Drain(game, stream, NextQueued(game),
            Config(Observer(write => Forward(registry, lease, write,
                first, second))), lifetime.Token));
        Assert.Equal(1, diagnostic);
        Assert.Equal(0, additional);
        Assert.False(Inject(game, [2, 8, 2]));
    }

    [Fact]
    public void Probe_source_validation_does_not_accept_only_matching_synthetic_pid()
    {
        var registry = NewRegistry();
        const uint pid = 95109;
        var firstSource = SourceOf(pid);
        var otherSource = SourceOf(pid);
        var lease = Claim(registry, pid, () => { },
            value => Equals(value, firstSource), _ => { });
        Assert.True(IsOwner(registry, lease));
        Assert.True((bool)Registry.GetMethod("IsCurrentSocketLink", Hidden)!
            .Invoke(registry, [firstSource])!);
        Assert.False((bool)Registry.GetMethod("IsCurrentSocketLink", Hidden)!
            .Invoke(registry, [otherSource])!);
        Assert.True(Release(registry, lease, _ => { }));
    }
}
