using System.Reflection;
using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Synthetic, independently authorized test source. No transport, listening
/// port, DOFUS client, credentials or trusted production adapter is involved.
/// </summary>
public sealed class MZP1OrderedPublicationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const int Connection = 71;
    private const uint Process = 81001;
    private const string Version = "3.7.10.10";
    private const string Code = "type.ankama.com/sample";
    private static readonly MZP0SessionIdentity Identity =
        new((int)Process, 638960000000000000L, Connection, Version);

    private sealed class Origin : IMZP0AuthenticatedSessionSource
    {
        public bool Authorized { get; set; } = true;
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        {
            identity = Identity;
            return Authorized;
        }
    }

    private sealed class UntrustedResponses : IMZP0PositiveResponseVerifier
    {
        public bool IsVerifiedPositiveResponse(BotMessage _, MZP0SessionGeneration __, int ___)
            => false; // No frame can be mistaken for an authenticated ACK.
    }

    private sealed class NoNetworkSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => throw new InvalidOperationException("offline");
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "givre-mzp1-order-" + Guid.NewGuid().ToString("N"));
        private readonly MZP0RecoveryJournal _journal;
        public Origin Authority { get; } = new();
        public MZP1GuardedEngineWiring Wiring { get; }

        public Rig()
        {
            Directory.CreateDirectory(_directory);
            _journal = new MZP0RecoveryJournal(Path.Combine(_directory, "journal.json"));
            var catalog = ProtocolCatalog.LoadFromStrings(
                "message GameMessage { int64 uid = 1; }",
                "{\"type.ankama.com/sample\":\"MessageType.GameMessage\"}",
                null, "{\"GameMessage\":{\"uid\":1}}");
            Wiring = MZP1GuardedEngineWiringFactory.Create(Connection, Version, catalog,
                new NoNetworkSink(), new NoPacer(), _journal, Authority, new UntrustedResponses());
            Assert.Equal(MZP1Admission.Ready, Wiring.Admission);
        }

        public void Dispose()
        {
            Wiring.Revoke();
            _journal.Dispose();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }
    }

    private static CapturedMessage Frame(long seq, uint pid = Process, int connection = Connection)
        => new(seq, Direction.ServerToClient, connection, pid, Code,
            new ProtoField[] { new() { Field = 1, Wire = WireType.Len, String = "private" } },
            new byte[] { 0x08, 0x01 });

    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The intentionally blocked callback must not consume a ThreadPool
    // worker for up to Deadline while thousands of other xUnit cases run.
    // A dedicated test thread makes the scheduling interleaving deterministic
    // without relaxing any production FIFO/reentrance assertions.
    private static Task<MZP1CaptureResult> DispatchBlockedFirst(Func<MZP1CaptureResult> dispatch)
        => Task.Factory.StartNew(dispatch, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    [Fact]
    public async Task Concurrent_captures_publish_in_their_state_fold_order_not_callback_thread_order()
    {
        using var rig = new Rig();
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var seen = new List<long>();
        using var sub = rig.Wiring.Bus.Subscribe(msg =>
        {
            lock (seen) seen.Add(msg.Seq);
            if (msg.Seq == 1)
            {
                entered.TrySetResult(true);
                Assert.True(release.Wait(Deadline));
            }
        });
        var first = DispatchBlockedFirst(() => rig.Wiring.TryDispatchCaptured(Frame(1)));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            var second = await Task.Run(() => rig.Wiring.TryDispatchCaptured(Frame(2))).WaitAsync(Deadline);
            Assert.Equal(MZP1CaptureResult.Accepted, second);
            lock (seen) Assert.Equal(new long[] { 1 }, seen);
        }
        finally { release.Set(); }
        Assert.Equal(MZP1CaptureResult.Accepted, await first.WaitAsync(Deadline));
        lock (seen) Assert.Equal(new long[] { 1, 2 }, seen);
    }

    [Fact]
    public void Reentrant_subscriber_enqueues_followup_without_recursive_publication()
    {
        using var rig = new Rig();
        int depth = 0, maxDepth = 0;
        var observed = new List<long>();
        MZP1CaptureResult? reentered = null;
        using var sub = rig.Wiring.Bus.Subscribe(msg =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            if (msg.Seq == 1)
                reentered = rig.Wiring.TryDispatchCaptured(Frame(2));
            observed.Add(msg.Seq);
            depth--;
        });

        Assert.Equal(MZP1CaptureResult.Accepted, rig.Wiring.TryDispatchCaptured(Frame(1)));
        Assert.Equal(MZP1CaptureResult.Accepted, reentered);
        Assert.Equal(1, maxDepth);
        Assert.Equal(new long[] { 1, 2 }, observed);
    }

    [Fact]
    public async Task Revoke_during_callback_discards_waiting_packets_without_blocking_on_subscriber()
    {
        using var rig = new Rig();
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var seen = new List<long>();
        using var sub = rig.Wiring.Bus.Subscribe(msg =>
        {
            lock (seen) seen.Add(msg.Seq);
            if (msg.Seq == 1)
            {
                entered.TrySetResult(true);
                Assert.True(release.Wait(Deadline));
            }
        });
        var first = DispatchBlockedFirst(() => rig.Wiring.TryDispatchCaptured(Frame(1)));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.Equal(MZP1CaptureResult.Accepted, rig.Wiring.TryDispatchCaptured(Frame(2)));
            await Task.Run(rig.Wiring.Revoke).WaitAsync(Deadline);
            Assert.Equal(MZP1Admission.Revoked, rig.Wiring.Admission);
            Assert.Equal(MZP1CaptureResult.Blocked, rig.Wiring.TryDispatchCaptured(Frame(3)));
        }
        finally { release.Set(); }
        await first.WaitAsync(Deadline);
        lock (seen) Assert.Equal(new long[] { 1 }, seen);
    }

    [Fact]
    public async Task Bounded_queue_rejects_overflow_and_revokes_instead_of_losing_a_hidden_event()
    {
        using var rig = new Rig();
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var observed = new List<long>();
        using var sub = rig.Wiring.Bus.Subscribe(msg =>
        {
            lock (observed) observed.Add(msg.Seq);
            if (msg.Seq == 1)
            {
                entered.TrySetResult(true);
                Assert.True(release.Wait(Deadline));
            }
        });
        var first = DispatchBlockedFirst(() => rig.Wiring.TryDispatchCaptured(Frame(1)));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            for (int i = 2; i <= 1025; i++)
                Assert.Equal(MZP1CaptureResult.Accepted, rig.Wiring.TryDispatchCaptured(Frame(i)));
            Assert.Equal(MZP1CaptureResult.Blocked, rig.Wiring.TryDispatchCaptured(Frame(1026)));
            Assert.Equal(MZP1Admission.Revoked, rig.Wiring.Admission);
            var queue = typeof(MZP1GuardedEngineWiring).GetField("_pendingPublications",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Wiring)!;
            int remaining = (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
            Assert.Equal(0, remaining);
        }
        finally { release.Set(); }
        await first.WaitAsync(Deadline);
        lock (observed) Assert.Equal(new long[] { 1 }, observed);
    }

    [Fact]
    public void Accepted_notifications_stay_categorical_and_wrong_connection_cannot_enqueue()
    {
        using var rig = new Rig();
        var seen = new List<BotMessage>();
        using var sub = rig.Wiring.Bus.Subscribe(seen.Add);
        Assert.Equal(MZP1CaptureResult.MismatchedSession,
            rig.Wiring.TryDispatchCaptured(Frame(1, connection: Connection + 1)));
        Assert.Equal(MZP1CaptureResult.MismatchedSession,
            rig.Wiring.TryDispatchCaptured(Frame(2, pid: Process + 1)));
        Assert.Equal(MZP1CaptureResult.MissingProcessProof,
            rig.Wiring.TryDispatchCaptured(Frame(3, pid: 0)));
        Assert.Equal(MZP1CaptureResult.Accepted, rig.Wiring.TryDispatchCaptured(Frame(4)));
        var only = Assert.Single(seen);
        Assert.Equal(4, only.Seq);
        Assert.Empty(only.Payload);
        Assert.Empty(only.RawFrame);
    }
}
