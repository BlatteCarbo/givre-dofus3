extern alias F06App;

using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Actual SocketGameClient.TryInject/Inject -> bounded _outbound channel ->
/// WriteQueuedEngineActionAsync tests and the production SocketProbe
/// SocketSessionOwnership.ForwardWritten callback. Only in-memory streams and
/// fake already-dialed identities; no network, login, client or server ACK.
/// </summary>
public sealed class SocketWriteAdmissionJourneyV9Tests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly App = typeof(BotHostService).Assembly;
    private static readonly Type Game = Required("SocketGameClient");
    private static readonly Type Params = Required("SocketGameParams");
    private static readonly Type Source = Required("SocketLinkProvenance");
    private static readonly Type Receipt = Required("SocketInjectionAdmissionReceipt");
    private static readonly Type Witness = Required("SocketTransportWriteObservation");
    private static readonly Type Registry = Required("SocketSessionOwnership");
    private static Type Required(string name) =>
        App.GetType("GivreClone.App.Services.Transport.Socket." + name, true)!;

    private static object Origin(uint pid, long epoch = 1, Guid? instance = null,
        Guid? connection = null) =>
        Activator.CreateInstance(Source,
            [instance ?? Guid.NewGuid(), epoch, connection ?? Guid.NewGuid(), pid])!;

    private static object Client(uint pid, object origin, CancellationTokenSource lifetime)
    {
        var game = Activator.CreateInstance(Game, [pid])!;
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", origin); // fixture models successful real dial
        Set(game, "_injectionReady", 1);
        Set(game, "_injectorPublished", 1);
        return game;
    }

    private static void Set(object game, string name, object? value) =>
        Game.GetField(name, Hidden)!.SetValue(game, value);

    private static bool Inject(object game, byte[] frame) =>
        (bool)Game.GetMethod("Inject")!.Invoke(game, [frame])!;

    private static (bool Accepted, object? Receipt) Admit(object game, byte[] frame)
    {
        object?[] args = [frame, null];
        bool accepted = (bool)Game.GetMethod("TryInject")!.Invoke(game, args)!;
        return (accepted, accepted ? args[1] : null);
    }

    private static long Number(object target, string name) =>
        (long)target.GetType().GetProperty(name)!.GetValue(target)!;

    private static bool Truth(object target, string name) =>
        (bool)target.GetType().GetProperty(name)!.GetValue(target)!;

    private static object Snapshot(object game) =>
        Game.GetMethod("ReadOutboundJourneyCounters")!.Invoke(game, null)!;

    private static object Dequeue(object game)
    {
        var channel = Game.GetField("_outbound", Hidden)!.GetValue(game)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] args = [null];
        Assert.True((bool)reader.GetType().GetMethod("TryRead")!.Invoke(reader, args)!);
        return Assert.IsAssignableFrom<object>(args[0]);
    }

    private static bool HasQueuedFrame(object game)
    {
        var channel = Game.GetField("_outbound", Hidden)!.GetValue(game)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] args = [null];
        return (bool)reader.GetType().GetMethod("TryRead")!.Invoke(reader, args)!;
    }

    private static Action<T> AdaptAction<T>(Action<object> callback) =>
        item => callback(item!);

    private static Func<T, bool> AdaptPredicate<T>(Func<object, bool> callback) =>
        item => callback(item!);

    private static Delegate WrittenHandler(Action<object> callback) =>
        (Delegate)typeof(SocketWriteAdmissionJourneyV9Tests)
            .GetMethod(nameof(AdaptAction), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Witness).Invoke(null, [callback])!;

    private static Delegate SourcePredicate(Func<object, bool> callback) =>
        (Delegate)typeof(SocketWriteAdmissionJourneyV9Tests)
            .GetMethod(nameof(AdaptPredicate), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Source).Invoke(null, [callback])!;

    private static object Config(Delegate? writtenHandler = null)
    {
        var config = Activator.CreateInstance(Params, nonPublic: true)!;
        if (writtenHandler is not null)
            Params.GetProperty("OutboundWritten")!.SetValue(config, writtenHandler);
        return config;
    }

    private static Task<bool> Drain(object game, Stream stream, object outgoing,
        object config, CancellationToken token) =>
        (Task<bool>)Game.GetMethod("WriteQueuedEngineActionAsync", Hidden)!
            .Invoke(game, [stream, outgoing, config, token])!;

    private static object RegistryInstance() =>
        Activator.CreateInstance(Registry, nonPublic: true)!;

    private static object Claim(object registry, uint pid, Action revoke,
        Func<object, bool> validate, Action<uint> unregister) =>
        Registry.GetMethod("Claim", Hidden)!.Invoke(
            registry, [pid, revoke, SourcePredicate(validate), unregister])!;

    private static void Forward(object registry, object lease, object witness,
        Delegate? diagnostic, Delegate? subscribers) =>
        Registry.GetMethod("ForwardWritten", Hidden)!
            .Invoke(registry, [lease, witness, diagnostic, subscribers]);

    private static bool Release(object registry, object lease, Action<uint> unregister) =>
        (bool)Registry.GetMethod("Release", Hidden)!
            .Invoke(registry, [lease, unregister])!;

    private static bool Register(object registry, object lease, Action register) =>
        (bool)Registry.GetMethod("TryRegister", Hidden)!
            .Invoke(registry, [lease, register])!;

    private sealed class BlockedFlushStream : MemoryStream
    {
        internal readonly TaskCompletionSource<bool> Entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Continue =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Continue.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FailedAfterWriteStream : MemoryStream
    {
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("flush failed after local WriteAsync"));
    }

    [Fact]
    public async Task Real_admission_receipt_matches_postflush_evidence_but_neither_is_server_ack()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91001);
        var game = Client(91001, source, lifetime);
        var writes = new List<object>();
        var config = Config(WrittenHandler(writes.Add));
        byte[] caller = [2, 8, 4];
        var (ok, receipt) = Admit(game, caller);
        Assert.True(ok);
        Assert.NotNull(receipt);
        Assert.True(Truth(receipt!, "Queued"));
        Assert.False(Truth(receipt!, "WriteAttempted"));
        Assert.False(Truth(receipt!, "LocalWriteCompleted"));
        Assert.False(Truth(receipt!, "ServerAcknowledged"));
        Assert.Equal(1, Number(receipt!, "AdmissionOrdinal"));
        Assert.Equal(source, Receipt.GetProperty("Source")!.GetValue(receipt));
        caller[2] = 99; // neither queue nor postflush witness borrows caller buffer

        var before = Snapshot(game);
        Assert.Equal(1, Number(before, "Queued"));
        Assert.Equal(0, Number(before, "WriteAttempted"));
        Assert.Equal(0, Number(before, "PostFlushWritten"));
        Assert.Empty(writes);

        using var wire = new MemoryStream();
        Assert.True(await Drain(game, wire, Dequeue(game), config, lifetime.Token));
        var completed = Assert.Single(writes);
        Assert.Equal(new byte[] { 2, 8, 4 }, wire.ToArray());
        Assert.Equal(source, Witness.GetProperty("Source")!.GetValue(completed));
        Assert.Equal(1, Number(completed, "AdmissionOrdinal"));
        Assert.Equal(1, Number(completed, "AttemptOrdinal"));
        Assert.Equal(1, Number(completed, "Ordinal"));
        Assert.True(Truth(completed, "LocalWriteCompleted"));
        Assert.False(Truth(completed, "ServerAcknowledged"));
        var copied = (byte[])Witness.GetMethod("CopyWireFrame")!.Invoke(completed, null)!;
        copied[1] = 0xFF;
        Assert.Equal(new byte[] { 2, 8, 4 },
            (byte[])Witness.GetMethod("CopyWireFrame")!.Invoke(completed, null)!);

        var after = Snapshot(game);
        Assert.Equal(1, Number(after, "Queued"));
        Assert.Equal(1, Number(after, "WriteAttempted"));
        Assert.Equal(1, Number(after, "PostFlushWritten"));
        Assert.Equal(0, Number(after, "AmbiguousWriteFailure"));
    }

    [Fact]
    public void Rejected_try_inject_returns_default_receipt_that_cannot_claim_admission()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91011);
        var game = Client(91011, source, lifetime);
        object?[] args = [new byte[] { 1 }, null]; // invalid short wire frame
        Assert.False((bool)Game.GetMethod("TryInject")!.Invoke(game, args)!);
        Assert.NotNull(args[1]);
        Assert.False(Truth(args[1]!, "Queued"));
        Assert.False(Truth(args[1]!, "WriteAttempted"));
        Assert.False(Truth(args[1]!, "LocalWriteCompleted"));
        Assert.False(Truth(args[1]!, "ServerAcknowledged"));
        Assert.Equal(0, Number(Snapshot(game), "Queued"));
    }

    [Fact]
    public async Task Paused_real_flush_shows_attempt_without_written_then_follows_with_postflush()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91002);
        var game = Client(91002, source, lifetime);
        var observed = new List<object>();
        var config = Config(WrittenHandler(observed.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        using var paused = new BlockedFlushStream();
        var running = Drain(game, paused, Dequeue(game), config, lifetime.Token);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var inFlight = Snapshot(game);
        Assert.Equal(1, Number(inFlight, "Queued"));
        Assert.Equal(1, Number(inFlight, "WriteAttempted"));
        Assert.Equal(0, Number(inFlight, "PostFlushWritten"));
        Assert.Empty(observed);

        paused.Continue.TrySetResult(true);
        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Number(Snapshot(game), "PostFlushWritten"));
        Assert.Equal(1, Number(Assert.Single(observed), "Ordinal"));
    }

    [Fact]
    public async Task Partial_write_followed_by_failed_flush_is_ambiguous_and_never_becomes_written()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91003);
        var game = Client(91003, source, lifetime);
        var witnessed = new List<object>();
        Assert.True(Inject(game, [2, 8, 3]));
        using var stream = new FailedAfterWriteStream();
        await Assert.ThrowsAsync<IOException>(() =>
            Drain(game, stream, Dequeue(game), Config(WrittenHandler(witnessed.Add)),
                lifetime.Token));
        Assert.Equal(new byte[] { 2, 8, 3 }, stream.ToArray());
        Assert.Empty(witnessed);
        Assert.False(HasQueuedFrame(game)); // no automatic replay after ambiguity
        var metrics = Snapshot(game);
        Assert.Equal(1, Number(metrics, "Queued"));
        Assert.Equal(1, Number(metrics, "WriteAttempted"));
        Assert.Equal(0, Number(metrics, "PostFlushWritten"));
        Assert.Equal(1, Number(metrics, "AmbiguousWriteFailure"));
    }

    [Fact]
    public async Task Eof_or_stop_before_drain_is_discarded_without_attempt_or_false_written_claim()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91004);
        var game = Client(91004, source, lifetime);
        Assert.True(Inject(game, [2, 8, 4]));
        var old = Dequeue(game);
        lifetime.Cancel(); // RunAsync EOF/Stop revokes its session token
        using var stream = new MemoryStream();
        Assert.False(await Drain(game, stream, old, Config(), lifetime.Token));
        Assert.Empty(stream.ToArray());
        Assert.False(Inject(game, [2, 8, 7]));
        var metrics = Snapshot(game);
        Assert.Equal(1, Number(metrics, "Queued"));
        Assert.Equal(0, Number(metrics, "WriteAttempted"));
        Assert.Equal(0, Number(metrics, "PostFlushWritten"));
        Assert.Equal(1, Number(metrics, "DiscardedBeforeWrite"));
    }

    [Fact]
    public async Task Old_link_queued_entry_cannot_write_to_new_run_despite_identical_pid()
    {
        using var lifetime = new CancellationTokenSource();
        var old = Origin(91005, 41);
        var next = Origin(91005, 42);
        var game = Client(91005, old, lifetime);
        var first = Admit(game, [2, 8, 1]);
        Assert.True(first.Accepted);
        var retained = Dequeue(game);
        Set(game, "_currentSource", next); // fixture simulates PID reuse/next connection
        using var stream = new MemoryStream();
        var writes = new List<object>();
        var config = Config(WrittenHandler(writes.Add));
        Assert.False(await Drain(game, stream, retained, config, lifetime.Token));
        Assert.Empty(stream.ToArray());
        Assert.Empty(writes);
        var second = Admit(game, [2, 8, 2]);
        Assert.True(second.Accepted);
        Assert.Equal(2, Number(second.Receipt!, "AdmissionOrdinal"));
        Assert.True(await Drain(game, stream, Dequeue(game), config, lifetime.Token));
        var evidence = Assert.Single(writes);
        Assert.Equal(next, Witness.GetProperty("Source")!.GetValue(evidence));
        Assert.Equal(2, Number(evidence, "AdmissionOrdinal"));
        Assert.Equal(1, Number(evidence, "AttemptOrdinal"));
        Assert.Equal(1, Number(evidence, "Ordinal"));
        Assert.Equal(1, Number(Snapshot(game), "DiscardedBeforeWrite"));
    }

    [Fact]
    public void Stop_reentrant_during_review_classifier_blocks_final_queue_admission()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91006);
        var game = Client(91006, source, lifetime);
        var reviewEnum = Game.GetNestedType("ReviewCommand", BindingFlags.NonPublic)!;
        var request = Expression.Parameter(typeof(byte[]), "wire");
        var revoke = Expression.Call(Expression.Constant(game),
            Game.GetMethod("RevokeForReplacement")!);
        var returnMove = Expression.Constant(Enum.Parse(reviewEnum, "Move"), reviewEnum);
        var classifier = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(byte[]), reviewEnum),
            Expression.Block(revoke, returnMove), request).Compile();
        Set(game, "_reviewClassifier", classifier);
        Set(game, "_reviewDiagnosticMode", 1);
        var admitted = Admit(game, [2, 8, 6]);
        Assert.False(admitted.Accepted);
        Assert.Null(admitted.Receipt);
        Assert.False(HasQueuedFrame(game));
        Assert.Equal(0, Number(Snapshot(game), "Queued"));
        Assert.False(Inject(game, [2, 8, 7]));
    }

    [Fact]
    public void Probe_retires_old_lease_before_unregister_callback_can_reenter_old_injector()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 91010;
        var source = Origin(pid, 101);
        var successorSource = Origin(pid, 102);
        var game = Client(pid, source, lifetime);
        var registry = RegistryInstance();
        int reentered = 0;
        Action<uint> unregister = _ =>
        {
            ++reentered;
            // Claim calls unregister while the old connection's Stop callback
            // has not yet run. The exact old lease must already be inactive.
            Assert.False(Inject(game, [2, 8, 1]));
        };
        var first = Claim(registry, pid,
            () => Game.GetMethod("RevokeForReplacement")!.Invoke(game, null),
            origin => (bool)Game.GetMethod("IsCurrentSocketLink")!
                .Invoke(game, [origin])!, unregister);
        var ownerType = first.GetType();
        Game.GetMethod("BindSessionOwnership")!.Invoke(game,
            [new Func<bool>(() => (bool)ownerType.GetProperty("IsActive",
                Hidden)!.GetValue(first)!)]);
        Assert.True(Register(registry, first, () => { }));
        Assert.True(Inject(game, [2, 8, 1]));
        var next = Claim(registry, pid, () => { },
            origin => Equals(origin, successorSource), unregister);
        Assert.Equal(1, reentered);
        Assert.False(Inject(game, [2, 8, 2]));
        Assert.True(Release(registry, next, unregister));
        Assert.False(Release(registry, first, unregister));
        Assert.Equal(1, Number(Snapshot(game), "Queued"));
    }

    [Fact]
    public async Task Reentrant_written_callback_can_enqueue_next_action_without_reordering_or_deadlock()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91007);
        var game = Client(91007, source, lifetime);
        var events = new List<long>();
        var config = Config(WrittenHandler(witness =>
        {
            events.Add(Number(witness, "AdmissionOrdinal"));
            if (events.Count == 1) Assert.True(Inject(game, [2, 8, 2]));
        }));
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new MemoryStream();
        Assert.True(await Drain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.True(await Drain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(new long[] { 1, 2 }, events);
        Assert.Equal(new byte[] { 2, 8, 1, 2, 8, 2 }, stream.ToArray());
        Assert.Equal(2, Number(Snapshot(game), "PostFlushWritten"));
    }

    [Fact]
    public async Task Probe_real_dispatch_rechecks_lease_after_reentrant_replacement()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 91008;
        var oldSource = Origin(pid, 71);
        var nextSource = Origin(pid, 72);
        var game = Client(pid, oldSource, lifetime);
        var registry = RegistryInstance();
        Action<uint> unregister = _ => { };
        var oldLease = Claim(registry, pid,
            () => Game.GetMethod("RevokeForReplacement")!.Invoke(game, null),
            origin => (bool)Game.GetMethod("IsCurrentSocketLink")!
                .Invoke(game, [origin])!, unregister);
        object? nextLease = null;
        int diagnostic = 0, later = 0;
        var diagnosticHandler = WrittenHandler(_ =>
        {
            Interlocked.Increment(ref diagnostic);
            nextLease = Claim(registry, pid, () => { },
                origin => Equals(origin, nextSource), unregister);
        });
        var additional = WrittenHandler(_ => Interlocked.Increment(ref later));
        var config = Config(WrittenHandler(witness =>
            Forward(registry, oldLease, witness, diagnosticHandler, additional)));
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new MemoryStream();
        Assert.True(await Drain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(1, diagnostic);
        Assert.Equal(0, later); // replacement revoked the first lease
        Assert.NotNull(nextLease);
        Assert.True(Release(registry, nextLease!, unregister));
        Assert.False(Release(registry, oldLease, unregister));
        Assert.False(Inject(game, [2, 8, 2]));
    }

    [Fact]
    public void Probe_rejects_v8_legacy_write_record_without_matching_queue_and_attempt_chain()
    {
        using var lifetime = new CancellationTokenSource();
        const uint pid = 91012;
        var source = Origin(pid, 202);
        var game = Client(pid, source, lifetime);
        var registry = RegistryInstance();
        Action<uint> unregister = _ => { };
        var lease = Claim(registry, pid, () => { },
            candidate => Equals(candidate, source), unregister);
        var oldCtor = Witness.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == 4);
        var legacy = oldCtor.Invoke([source, 1L, new byte[] { 2, 8, 1 }, 100L]);
        int forwarded = 0;
        Forward(registry, lease, legacy,
            WrittenHandler(_ => Interlocked.Increment(ref forwarded)), null);
        Assert.Equal(0, forwarded);
        Assert.True(Release(registry, lease, unregister));
    }

    [Fact]
    public async Task Queue_overflow_does_not_consume_admission_number_or_mint_write_evidence()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(91009);
        var game = Client(91009, source, lifetime);
        for (int i = 1; i <= 256; i++)
        {
            var admitted = Admit(game, [2, 8, 1]);
            Assert.True(admitted.Accepted);
            Assert.Equal(i, Number(admitted.Receipt!, "AdmissionOrdinal"));
        }
        Assert.False(Admit(game, [2, 8, 2]).Accepted);
        Assert.Equal(256, Number(Snapshot(game), "Queued"));
        Assert.Equal(0, Number(Snapshot(game), "PostFlushWritten"));
        using var stream = new MemoryStream();
        var written = new List<object>();
        Assert.True(await Drain(game, stream, Dequeue(game),
            Config(WrittenHandler(written.Add)), lifetime.Token));
        var next = Admit(game, [2, 8, 3]);
        Assert.True(next.Accepted);
        Assert.Equal(257, Number(next.Receipt!, "AdmissionOrdinal"));
        Assert.Equal(1, Number(Assert.Single(written), "Ordinal"));
    }
}
