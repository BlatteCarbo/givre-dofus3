using System.Reflection;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>In-memory lifecycle races. The proxy is never started or connected.</summary>
public sealed class BehaviorSessionLifecycleSafetyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HeldBehavior(string name) : IBehavior
    {
        public string Name => name;
        public TaskCompletionSource<bool> Entered { get; } = Signal();
        public TaskCompletionSource<bool> Cancelled { get; } = Signal();
        public TaskCompletionSource<bool> Release { get; } = Signal();

        public async Task RunAsync(IBotContext _, CancellationToken ct)
        {
            using var registration = ct.Register(() => Cancelled.TrySetResult(true));
            Entered.TrySetResult(true);
            // Deliberately ignore cancellation to model non-cancellable I/O.
            await Release.Task.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Replacement_waits_for_non_cooperative_predecessor()
    {
        var host = new BehaviorHost(null!);
        var first = new HeldBehavior("first");
        var second = new HeldBehavior("second");
        host.Start(first);
        await first.Entered.Task.WaitAsync(Deadline);

        host.Start(second);
        await first.Cancelled.Task.WaitAsync(Deadline);
        Assert.False(second.Entered.Task.IsCompleted);
        Assert.Same(second, host.Current);

        first.Release.TrySetResult(true);
        await second.Entered.Task.WaitAsync(Deadline);
        var stopped = host.StopAsync();
        await second.Cancelled.Task.WaitAsync(Deadline);
        Assert.False(stopped.IsCompleted); // waiting for the real end, not just Cancel()
        second.Release.TrySetResult(true);
        await stopped.WaitAsync(Deadline);
        Assert.False(host.IsRunning);
        Assert.Null(host.Current);
    }

    [Fact]
    public async Task Stop_skips_queued_replacements_and_never_starts_the_cancelled_behavior()
    {
        var host = new BehaviorHost(null!);
        var first = new HeldBehavior("first");
        var skipped = new HeldBehavior("skipped");
        host.Start(first);
        await first.Entered.Task.WaitAsync(Deadline);
        host.Start(skipped);
        await first.Cancelled.Task.WaitAsync(Deadline);
        var stop = host.StopAsync();
        Assert.False(stop.IsCompleted);
        first.Release.TrySetResult(true);
        await stop.WaitAsync(Deadline);
        Assert.False(skipped.Entered.Task.IsCompleted);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public async Task Failure_is_observed_and_next_start_still_works()
    {
        var host = new BehaviorHost(null!);
        host.Start(new FailingBehavior());
        await host.Completion.WaitAsync(Deadline);
        Assert.IsType<InvalidOperationException>(host.LastError);
        var next = new HeldBehavior("after-fault");
        host.Start(next);
        await next.Entered.Task.WaitAsync(Deadline);
        next.Release.TrySetResult(true);
        await host.StopAsync().WaitAsync(Deadline);
        Assert.Null(host.LastError);
    }

    private sealed class FailingBehavior : IBehavior
    {
        public string Name => "failure";
        public Task RunAsync(IBotContext _, CancellationToken ct) =>
            Task.FromException(new InvalidOperationException("test-failure"));
    }

    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromStrings(
        "message GameMessage { int64 uid = 1; }",
        "{\"type.ankama.com/sample\":\"MessageType.GameMessage\"}",
        null, "{\"GameMessage\":{\"uid\":1}}");

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => true;
    }

    private static SessionBinder NewBinder() => new(id =>
        new BotSession(id, Catalog(), new SilentSink(), new NoPacer()));

    private static CapturedMessage Frame(uint pid, int id) =>
        new(1, Direction.ServerToClient, id, pid, "type.ankama.com/sample",
            Array.Empty<ProtoField>(), Array.Empty<byte>());

    // An existing public event's delegate is raised in-memory. Unlike
    // TcpProxyServer.Start, this opens no socket and touches no client.
    private static Action<CapturedMessage> CaptureHandler(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    [Fact]
    public void New_connection_for_same_pid_retires_old_session_and_rejects_late_frames()
    {
        var binder = NewBinder();
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var feed = CaptureHandler(proxy);
        feed(Frame(202u, 10));
        var original = Assert.IsType<BotSession>(binder.FindByProcess(202));
        Assert.Equal(10, original.ConnectionId);

        feed(Frame(202u, 11));
        var current = Assert.IsType<BotSession>(binder.FindByProcess(202));
        Assert.Equal(11, current.ConnectionId);
        Assert.NotSame(original, current);
        Assert.Null(binder.Find(10));
        Assert.Single(binder.Sessions);

        feed(Frame(202u, 10)); // stale callback for old connection
        Assert.Same(current, binder.FindByProcess(202));
        Assert.Single(binder.Sessions);
        Assert.True(binder.Forget(11));
        Assert.False(binder.Forget(11));
        Assert.Null(binder.FindByProcess(202));
        feed(Frame(202u, 11)); // explicitly closed connection stays tombstoned
        Assert.Empty(binder.Sessions);
    }

    [Fact]
    public void Detach_invalidates_pid_lease_and_stale_event_after_reattach()
    {
        var binder = NewBinder();
        var proxy1 = new TcpProxyServer(15000);
        binder.Attach(proxy1);
        var stale = CaptureHandler(proxy1);
        stale(Frame(303u, 20));
        Assert.NotNull(binder.FindByProcess(303));

        binder.Detach(proxy1);
        Assert.Null(binder.FindByProcess(303));
        Assert.Null(binder.Find(20));
        Assert.Single(binder.Sessions); // existing diagnostic contract

        var proxy2 = new TcpProxyServer(15001);
        binder.Attach(proxy2);
        stale(Frame(303u, 21)); // callback retained from the detached attachment
        Assert.Empty(binder.Sessions);
        CaptureHandler(proxy2)(Frame(303u, 22));
        Assert.Equal(22, binder.FindByProcess(303)?.ConnectionId);
        binder.Detach(proxy2);
    }

    [Fact]
    public void Existing_connection_cannot_change_its_pid()
    {
        var binder = NewBinder();
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var feed = CaptureHandler(proxy);
        feed(Frame(404u, 40));
        feed(Frame(405u, 40));
        Assert.Equal(40, binder.FindByProcess(404)?.ConnectionId);
        Assert.Null(binder.FindByProcess(405));
        Assert.Single(binder.Sessions);
        binder.Detach(proxy);
    }

    [Fact]
    public void Failed_replacement_factory_keeps_the_original_pid_owner()
    {
        var binder = new SessionBinder((CapturedMessage capture) =>
            capture.ConnectionId == 42
                ? throw new InvalidOperationException("factory refused")
                : new BotSession(capture.ConnectionId, Catalog(), new SilentSink(), new NoPacer()));
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var feed = CaptureHandler(proxy);
        feed(Frame(440u, 41));
        var original = Assert.IsType<BotSession>(binder.FindByProcess(440));
        Assert.Throws<InvalidOperationException>(() => feed(Frame(440u, 42)));
        Assert.Same(original, binder.FindByProcess(440));
        Assert.Single(binder.Sessions);
        binder.Detach(proxy);
    }

    [Fact]
    public async Task Replaced_session_stuck_in_opened_callback_cannot_dispatch_after_replacement()
    {
        var binder = NewBinder();
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var feed = CaptureHandler(proxy);
        var entered = Signal();
        var release = Signal();
        binder.SessionOpened += session =>
        {
            if (session.ConnectionId != 50) return;
            entered.TrySetResult(true);
            release.Task.GetAwaiter().GetResult();
        };

        var oldCapture = Task.Run(() => feed(Frame(505u, 50)));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            feed(Frame(505u, 51));
        }
        finally { release.TrySetResult(true); }

        await oldCapture.WaitAsync(Deadline);
        Assert.Equal(51, binder.FindByProcess(505)?.ConnectionId);
        Assert.Null(binder.Find(50));
        Assert.Single(binder.Sessions);
        binder.Detach(proxy);
    }

    [Fact]
    public void Forget_during_opened_callback_never_dispatches_the_captured_packet()
    {
        var binder = NewBinder();
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var forwarded = 0;
        binder.SessionOpened += session =>
        {
            session.Bus.Subscribe(_ => forwarded++);
            Assert.True(binder.Forget(session.ConnectionId));
        };

        CaptureHandler(proxy)(Frame(606u, 60));
        Assert.Empty(binder.Sessions);
        Assert.Null(binder.FindByProcess(606));
        Assert.Equal(0, forwarded);
        // A late queued packet cannot resurrect the explicitly closed ID.
        CaptureHandler(proxy)(Frame(606u, 60));
        Assert.Empty(binder.Sessions);
        binder.Detach(proxy);
    }

    [Fact]
    public void Detach_during_opened_callback_never_dispatches_the_captured_packet()
    {
        var binder = NewBinder();
        var proxy = new TcpProxyServer(15000);
        binder.Attach(proxy);
        var forwarded = 0;
        binder.SessionOpened += session =>
        {
            session.Bus.Subscribe(_ => forwarded++);
            binder.Detach(proxy);
        };

        CaptureHandler(proxy)(Frame(707u, 70));
        Assert.Single(binder.Sessions); // historical diagnostics only
        Assert.Null(binder.Find(70));
        Assert.Null(binder.FindByProcess(707));
        Assert.Empty(binder.AuthenticatedSessions);
        Assert.Equal(0, forwarded);
    }
}
