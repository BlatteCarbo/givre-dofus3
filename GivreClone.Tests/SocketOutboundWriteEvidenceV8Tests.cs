extern alias F06App;

using System.IO;
using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// The real SocketGameClient.Inject -> bounded _outbound -> local drain write
/// path, driven only by isolated in-memory Stream instances. No mapping, game,
/// network connection, foreign process, or fabricated server ACK is involved.
/// </summary>
public sealed class SocketOutboundWriteEvidenceV8Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly App = typeof(BotHostService).Assembly;
    private static readonly Type Game = Required("SocketGameClient");
    private static readonly Type Parameters = Required("SocketGameParams");
    private static readonly Type Source = Required("SocketLinkProvenance");
    private static readonly Type Written = Required("SocketTransportWriteObservation");

    private static Type Required(string name) =>
        App.GetType("GivreClone.App.Services.Transport.Socket." + name,
            throwOnError: true)!;

    private static object Origin(uint pid, long epoch = 1, Guid? instance = null,
        Guid? connection = null) =>
        Activator.CreateInstance(Source,
            [instance ?? Guid.NewGuid(), epoch, connection ?? Guid.NewGuid(), pid])!;

    private static Action<T> Adapt<T>(Action<object> callback) => value => callback(value!);

    private static Delegate Observer(Action<object> callback) =>
        (Delegate)typeof(SocketOutboundWriteEvidenceV8Tests)
            .GetMethod(nameof(Adapt), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Written).Invoke(null, [callback])!;

    private static object GameClient(uint pid, object source, CancellationTokenSource lifetime)
    {
        var game = Activator.CreateInstance(Game, [pid])!;
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source); // fixture models completed DialAsync
        Set(game, "_injectionReady", 1);
        Set(game, "_injectorPublished", 1);
        return game;
    }

    private static object Config(Delegate? handlers = null)
    {
        var config = Activator.CreateInstance(Parameters, nonPublic: true)!;
        if (handlers is not null)
            Parameters.GetProperty("OutboundWritten")!.SetValue(config, handlers);
        return config;
    }

    private static void Set(object owner, string name, object? value) =>
        Game.GetField(name, Hidden)!.SetValue(owner, value);

    private static bool Inject(object game, byte[] frame) =>
        (bool)Game.GetMethod("Inject")!.Invoke(game, [frame])!;

    private static object Dequeue(object game)
    {
        var channel = Game.GetField("_outbound", Hidden)!.GetValue(game)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] args = [null];
        Assert.True((bool)reader.GetType().GetMethod("TryRead")!
            .Invoke(reader, args)!);
        return Assert.IsAssignableFrom<object>(args[0]);
    }

    private static Task<bool> ActualDrain(object game, Stream stream, object queued,
        object config, CancellationToken token) =>
        (Task<bool>)Game.GetMethod("WriteQueuedEngineActionAsync", Hidden)!
            .Invoke(game, [stream, queued, config, token])!;

    private static long Ordinal(object evidence) =>
        (long)Written.GetProperty("Ordinal")!.GetValue(evidence)!;

    private static object EvidenceSource(object evidence) =>
        Written.GetProperty("Source")!.GetValue(evidence)!;

    private static byte[] Bytes(object evidence) =>
        (byte[])Written.GetMethod("CopyWireFrame")!.Invoke(evidence, null)!;

    private static bool Flag(object evidence, string name) =>
        (bool)Written.GetProperty(name)!.GetValue(evidence)!;

    private sealed class FailingFlushStream : MemoryStream
    {
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("synthetic flush failure"));
    }

    private sealed class PausedFlushStream : MemoryStream
    {
        internal readonly TaskCompletionSource<bool> Entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Resume =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Inject_only_enqueues_and_post_flush_write_mints_immutable_source_bound_witness()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50001);
        var game = GameClient(50001, source, lifetime);
        var witnessed = new List<object>();
        var config = Config(Observer(witnessed.Add));
        byte[] original = [3, 0x08, 0x01, 0x02];

        Assert.True(Inject(game, original));
        original[2] = 0xEE; // the real Inject method owns its queued snapshot
        Assert.Empty(witnessed); // queue acceptance is NOT a write

        var queued = Dequeue(game);
        Assert.Empty(witnessed); // dequeue is NOT a write
        using var stream = new MemoryStream();
        Assert.True(await ActualDrain(game, stream, queued, config, lifetime.Token));
        Assert.Single(witnessed);
        Assert.Equal(new byte[] { 3, 0x08, 0x01, 0x02 }, stream.ToArray());
        Assert.Equal(source, EvidenceSource(witnessed[0]));
        Assert.Equal(1, Ordinal(witnessed[0]));
        Assert.True(Flag(witnessed[0], "LocalWriteCompleted"));
        Assert.False(Flag(witnessed[0], "ServerAcknowledged"));
        Assert.Equal(4, (int)Written.GetProperty("WireLength")!.GetValue(witnessed[0])!);

        var returned = Bytes(witnessed[0]);
        returned[1] = 0xFF;
        Assert.Equal(new byte[] { 3, 0x08, 0x01, 0x02 }, Bytes(witnessed[0]));
    }

    [Fact]
    public async Task Ordinals_increase_only_after_each_actual_flush_in_fifo_outbound_order()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50002);
        var game = GameClient(50002, source, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 0x08, 0x01]));
        Assert.True(Inject(game, [2, 0x08, 0x02]));
        Assert.Empty(received);
        using var stream = new MemoryStream();
        Assert.True(await ActualDrain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(1, Ordinal(Assert.Single(received)));
        Assert.True(await ActualDrain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(new long[] { 1, 2 }, received.Select(Ordinal));
        Assert.Equal(new byte[] { 2, 0x08, 0x01, 2, 0x08, 0x02 }, stream.ToArray());
        Assert.All(received, x => Assert.False(Flag(x, "ServerAcknowledged")));
    }

    [Fact]
    public async Task A_local_flush_failure_cannot_emit_written_evidence_or_advance_ordinal()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50003);
        var game = GameClient(50003, source, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        using var failed = new FailingFlushStream();
        await Assert.ThrowsAsync<IOException>(
            () => ActualDrain(game, failed, Dequeue(game), config, lifetime.Token));
        Assert.Empty(received); // WriteAsync alone does not constitute evidence
        Assert.Equal(0L, (long)Game.GetField("_outboundWriteOrdinal", Hidden)!.GetValue(game)!);
    }

    [Fact]
    public async Task Paused_flush_does_not_advance_write_ordinal_until_successful_completion()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50009);
        var game = GameClient(50009, source, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new PausedFlushStream();
        var drain = ActualDrain(game, stream, Dequeue(game), config, lifetime.Token);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(received);
        Assert.Equal(0L, (long)Game.GetField("_outboundWriteOrdinal", Hidden)!.GetValue(game)!);
        stream.Resume.TrySetResult(true);
        Assert.True(await drain.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Ordinal(Assert.Single(received)));
        Assert.Equal(new byte[] { 2, 8, 1 }, stream.ToArray());
        Assert.False(Flag(received[0], "ServerAcknowledged"));
    }

    [Fact]
    public async Task Stop_before_drain_refuses_a_queued_frame_without_writing_any_bytes()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50004);
        var game = GameClient(50004, source, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        var queued = Dequeue(game);
        Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        using var stream = new MemoryStream();
        Assert.False(await ActualDrain(game, stream, queued, config, lifetime.Token));
        Assert.Empty(received);
        Assert.Empty(stream.ToArray());
        Assert.False(Inject(game, [2, 8, 1]));
    }

    [Fact]
    public async Task Revoked_during_in_flight_flush_may_have_written_bytes_but_never_claims_success()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50005);
        var game = GameClient(50005, source, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        using var stream = new PausedFlushStream();
        var drain = ActualDrain(game, stream, Dequeue(game), config, lifetime.Token);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        stream.Resume.TrySetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await drain; });
        Assert.Empty(received);
        Assert.Equal(0L, (long)Game.GetField("_outboundWriteOrdinal", Hidden)!.GetValue(game)!);
        // These bytes may have been accepted by local Stream.WriteAsync. The
        // failure of FlushAsync prevents claiming a completed local write.
    }

    [Fact]
    public async Task Old_connection_or_pid_reuse_cannot_write_or_observe_a_successor_stream()
    {
        using var lifetime = new CancellationTokenSource();
        var old = Origin(50006, epoch: 11);
        var current = Origin(50006, epoch: 12);
        var game = GameClient(50006, old, lifetime);
        var received = new List<object>();
        var config = Config(Observer(received.Add));
        Assert.True(Inject(game, [2, 8, 1]));
        var queued = Dequeue(game);
        Set(game, "_currentSource", current); // new connection with reused PID
        using var stream = new MemoryStream();
        Assert.False(await ActualDrain(game, stream, queued, config, lifetime.Token));
        Assert.Empty(received);
        Assert.Empty(stream.ToArray());
        Assert.True(Inject(game, [2, 8, 2]));
        Assert.True(await ActualDrain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(current, EvidenceSource(Assert.Single(received)));
        Assert.Equal(new byte[] { 2, 8, 2 }, stream.ToArray());
    }

    [Fact]
    public async Task Stop_inside_first_written_subscriber_drops_later_subscribers_and_actions()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50007);
        var game = GameClient(50007, source, lifetime);
        int first = 0, later = 0;
        var callbacks = Delegate.Combine(
            Observer(_ =>
            {
                Interlocked.Increment(ref first);
                Game.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            }),
            Observer(_ => Interlocked.Increment(ref later)));
        var config = Config(callbacks);
        Assert.True(Inject(game, [2, 8, 1]));
        Assert.True(Inject(game, [2, 8, 2]));
        using var stream = new MemoryStream();
        Assert.True(await ActualDrain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(1, first);
        Assert.Equal(0, later);
        Assert.False(await ActualDrain(game, stream, Dequeue(game), config, lifetime.Token));
        Assert.Equal(1, first);
        Assert.False(Inject(game, [2, 8, 3]));
    }

    [Fact]
    public void Bounded_real_inject_queue_never_announces_queued_as_written_on_overflow()
    {
        using var lifetime = new CancellationTokenSource();
        var source = Origin(50008);
        var game = GameClient(50008, source, lifetime);
        for (int i = 0; i < 256; i++)
            Assert.True(Inject(game, [2, 8, 1]));
        Assert.False(Inject(game, [2, 8, 1])); // bounded channel is full
        Assert.Equal(0L, (long)Game.GetField("_outboundWriteOrdinal", Hidden)!.GetValue(game)!);
    }
}
