extern alias F06App;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;

namespace GivreClone.Tests;

/// <summary>
/// True Inject/queue/WriteQueuedEngineActionAsync entrypoint, isolated in
/// memory. A local write/flush is never treated as a server ACK or receipt.
/// </summary>
public sealed class SocketConcurrentDrainV18Tests
{
    private readonly ITestOutputHelper _output;
    public SocketConcurrentDrainV18Tests(ITestOutputHelper output) => _output = output;

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly App = typeof(BotHostService).Assembly;
    private static readonly Type Client = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketGameClient", true)!;
    private static readonly Type Source = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
    private static readonly Type Parameters = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketGameParams", true)!;

    private static object Create(out CancellationTokenSource lifetime)
    {
        const uint pid = 948231;
        lifetime = new CancellationTokenSource();
        var client = Activator.CreateInstance(Client, [pid])!;
        var provenance = Activator.CreateInstance(Source,
            [Guid.NewGuid(), 1L, Guid.NewGuid(), pid])!;
        Put(client, "_sessionCancellation", lifetime);
        Put(client, "_currentSource", provenance);
        Put(client, "_injectionReady", 1);
        Put(client, "_injectorPublished", 1);
        return client;
    }

    private static void Put(object obj, string key, object value) =>
        Client.GetField(key, Hidden)!.SetValue(obj, value);

    private static object Queue(object client, byte[] frame)
    {
        Assert.True((bool)Client.GetMethod("Inject")!.Invoke(client, [frame])!);
        var channel = Client.GetField("_outbound", Hidden)!.GetValue(client)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] args = [null];
        Assert.True((bool)reader.GetType().GetMethod("TryRead")!
            .Invoke(reader, args)!);
        return args[0]!;
    }

    private static Task<bool> DrainExact(object client, Stream stream, object queued,
        CancellationToken token) =>
        (Task<bool>)Client.GetMethod("WriteQueuedEngineActionAsync", Hidden)!
            .Invoke(client, [stream, queued,
                Activator.CreateInstance(Parameters, nonPublic: true)!, token])!;

    private sealed class PausingStream : MemoryStream
    {
        public readonly TaskCompletionSource<bool> Entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _flushCount;

        public override async Task FlushAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _flushCount) == 1)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }

    private sealed class NonCancellingFlushStream : MemoryStream
    {
        public readonly TaskCompletionSource<bool> Entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task FlushAsync(CancellationToken token)
        {
            Entered.TrySetResult(true);
            // Deliberately ignores cancellation, as a transport flush is
            // permitted to do. Revocation still must prevent a written lease.
            await Release.Task.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Parallel_drain_is_serialized_at_actual_write_and_first_flush()
    {
        var game = Create(out var lifetime);
        using (lifetime)
        using (var stream = new PausingStream())
        {
            var first = Queue(game, [2, 8, 1]);
            var second = Queue(game, [2, 8, 2]);
            var writeFirst = DrainExact(game, stream, first, lifetime.Token);
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var writeSecond = DrainExact(game, stream, second, lifetime.Token);
            Assert.False(writeSecond.IsCompleted); // gate held until first flush
            Assert.Equal(0L, (long)Client.GetField("_outboundWriteOrdinal", Hidden)!
                .GetValue(game)!);
            stream.Release.TrySetResult(true);
            Assert.True(await writeFirst.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await writeSecond.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(new byte[] { 2, 8, 1, 2, 8, 2 }, stream.ToArray());
            Assert.Equal(2L, (long)Client.GetField("_outboundWriteOrdinal", Hidden)!
                .GetValue(game)!);
        }
    }

    [Fact]
    public async Task Revocation_during_in_flight_flush_does_not_admit_second_frame()
    {
        var game = Create(out var lifetime);
        using (lifetime)
        using (var stream = new PausingStream())
        {
            var first = Queue(game, [2, 8, 1]);
            var second = Queue(game, [2, 8, 2]);
            var writeFirst = DrainExact(game, stream, first, lifetime.Token);
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var writeSecond = DrainExact(game, stream, second, lifetime.Token);
            Client.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            stream.Release.TrySetResult(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => { await writeFirst; });
            // A queued write may cancel while waiting for the write gate or
            // simply refuse the retired provenance; neither is a server ACK.
            try { Assert.False(await writeSecond.WaitAsync(TimeSpan.FromSeconds(10))); }
            catch (OperationCanceledException) { }
            Assert.Equal(new byte[] { 2, 8, 1 }, stream.ToArray());
            Assert.Equal(0L, (long)Client.GetField("_outboundWriteOrdinal", Hidden)!
                .GetValue(game)!);
        }
    }

    [Fact]
    public async Task Flush_completing_after_owner_revocation_is_ambiguous_not_postflush_success()
    {
        var game = Create(out var lifetime);
        using (lifetime)
        using (var stream = new NonCancellingFlushStream())
        {
            var queued = Queue(game, [2, 8, 1]);
            var write = DrainExact(game, stream, queued, lifetime.Token);
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Client.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            stream.Release.TrySetResult(true);

            Assert.False(await write.WaitAsync(TimeSpan.FromSeconds(10)));
            // These bytes MAY have escaped on the retired link. They are not
            // evidence of a valid current-source completed write or server ACK.
            Assert.Equal(new byte[] { 2, 8, 1 }, stream.ToArray());
            Assert.Equal(0L, (long)Client.GetField("_outboundWriteOrdinal", Hidden)!
                .GetValue(game)!);
            Assert.Equal(1L, (long)Client.GetField("_outboundAmbiguousWriteFailures", Hidden)!
                .GetValue(game)!);
        }
    }

    [Fact]
    public void Current_link_gate_records_local_latency_and_allocations_without_live_extrapolation()
    {
        var game = Create(out var lifetime);
        using (lifetime)
        {
            var source = Client.GetField("_currentSource", Hidden)!.GetValue(game)!;
            var gate = Client.GetMethod("IsCurrentSocketLink")!;
            Assert.True((bool)gate.Invoke(game, [source])!);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 256; i++)
                Assert.True((bool)gate.Invoke(game, [source])!);
            long elapsed = Stopwatch.GetTimestamp() - started;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _output.WriteLine($"V18 offline Socket current-source read: 256 checks; " +
                $"{elapsed} Stopwatch ticks; {bytes} caller-thread bytes; " +
                "reflection harness included; no transport, no game.");
            Client.GetMethod("RevokeForReplacement")!.Invoke(game, null);
            Assert.False((bool)gate.Invoke(game, [source])!);
        }
    }
}
