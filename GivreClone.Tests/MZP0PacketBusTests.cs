using GivreClone.Engine;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using System.Runtime.CompilerServices;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Deterministic offline regressions for PacketBus completion and resource races.</summary>
public sealed class MZP0PacketBusTests
{
    private static BotMessage Msg(string code = "match") =>
        new(1, Direction.ServerToClient, 101, code, code, Array.Empty<ProtoField>(), Array.Empty<byte>());

    [Fact]
    public async Task Publish_commits_waiter_before_slow_subscriber_even_if_timer_fires()
    {
        var bus = new PacketBus();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var sub = bus.Subscribe(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(4));
        });
        Task<BotMessage>? wait = null;
        // Arm the deadline on the publisher's own thread. With Task.Run the
        // thread pool could queue Publish until after the 120 ms deadline,
        // making this test assert a timeout-before-publish instead of its
        // intended publish-before-subscriber ordering.
        var published = Task.Factory.StartNew(() =>
        {
            wait = bus.WaitForAsync(m => m.Code == "match", TimeSpan.FromMilliseconds(120));
            bus.Publish(Msg());
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            // Give the timer enough time to run while subscribers still block the publisher.
            await Task.Delay(250);
            Assert.NotNull(wait);
            Assert.True(wait.IsCompletedSuccessfully, "Publish must commit TCS before invoking subscribers.");
            Assert.Equal("match", (await wait).Code);
        }
        finally
        {
            release.Set();
            await published.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Publish_commits_waiter_before_slow_subscriber_even_if_cancel_runs()
    {
        var bus = new PacketBus();
        using var cts = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var sub = bus.Subscribe(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(4));
        });
        var wait = bus.WaitForAsync(_ => true, Timeout.InfiniteTimeSpan, cts.Token);
        var published = Task.Run(() => bus.Publish(Msg()));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            cts.Cancel();
            Assert.True(wait.IsCompletedSuccessfully, "Cancellation cannot supersede an already published match.");
            Assert.Equal("match", (await wait).Code);
        }
        finally
        {
            release.Set();
            await published.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Already_cancelled_token_never_enqueues_waiter_and_does_not_change_subscriptions()
    {
        var bus = new PacketBus();
        int calls = 0;
        using var sub = bus.Subscribe(_ => Interlocked.Increment(ref calls));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        for (int n = 0; n < 64; n++)
        {
            var task = bus.WaitForAsync(_ => true, Timeout.InfiniteTimeSpan, cts.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
        bus.Publish(Msg());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Timeout_first_cannot_be_reversed_by_a_later_matching_publish_or_cancel()
    {
        var bus = new PacketBus();
        using var cts = new CancellationTokenSource();
        var pending = bus.WaitForAsync(_ => true, TimeSpan.FromMilliseconds(30), cts.Token);
        await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        cts.Cancel();
        bus.Publish(Msg());
        Assert.True(pending.IsFaulted);
        Assert.IsType<TimeoutException>(pending.Exception!.InnerException);
    }

    [Fact]
    public async Task Cancellation_first_cannot_be_reversed_by_later_publish_or_timeout()
    {
        var bus = new PacketBus();
        using var cts = new CancellationTokenSource();
        var pending = bus.WaitForAsync(_ => true, TimeSpan.FromMilliseconds(100), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        await Task.Delay(140);
        bus.Publish(Msg());
        Assert.True(pending.IsCanceled);
    }

    [Fact]
    public async Task Subscriber_exceptions_are_isolated_and_snapshot_fanout_remains_stable()
    {
        var bus = new PacketBus();
        var received = new List<string>();
        using var throwing = bus.Subscribe(_ => throw new InvalidOperationException("intentional test error"));
        using var one = bus.Subscribe(m => received.Add("one:" + m.Code));
        using var two = bus.Subscribe(m => received.Add("two:" + m.Code));
        var awaited = bus.WaitForAsync(m => m.Code == "match", TimeSpan.FromSeconds(2));
        bus.Publish(Msg());
        Assert.Equal("match", (await awaited).Code);
        Assert.Equal(new[] { "one:match", "two:match" }, received);

        one.Dispose();
        bus.Publish(Msg("other"));
        Assert.Equal(new[] { "one:match", "two:match", "two:other" }, received);
    }

    [Fact]
    public async Task Throwing_predicate_does_not_prevent_another_waiter_or_future_match()
    {
        var bus = new PacketBus();
        var first = bus.WaitForAsync(m => m.Code == "match", TimeSpan.FromSeconds(2));
        var second = bus.WaitForAsync(m =>
        {
            if (m.Code == "bad") throw new InvalidOperationException("bad parser");
            return m.Code == "match";
        }, TimeSpan.FromSeconds(2));
        bus.Publish(Msg("bad"));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        bus.Publish(Msg());
        Assert.Equal("match", (await first).Code);
        Assert.Equal("match", (await second).Code);
    }

    [Fact]
    public async Task Subscriber_can_register_a_waiter_and_publish_reentrantly()
    {
        var bus = new PacketBus();
        Task<BotMessage>? nested = null;
        using var sub = bus.Subscribe(m =>
        {
            if (m.Code != "outer") return;
            nested = bus.WaitForAsync(x => x.Code == "inner", TimeSpan.FromSeconds(2));
            bus.Publish(Msg("inner"));
        });
        var outer = bus.WaitForAsync(m => m.Code == "outer", TimeSpan.FromSeconds(2));
        bus.Publish(Msg("outer"));
        Assert.Equal("outer", (await outer).Code);
        Assert.NotNull(nested);
        Assert.Equal("inner", (await nested!).Code);
    }

    [Fact]
    public async Task Predicate_cancelling_its_own_waiter_reentrantly_does_not_remove_other_waiters()
    {
        var bus = new PacketBus();
        using var cts = new CancellationTokenSource();
        // Reverse iteration visits the cancelling predicate first.
        var ordinary = bus.WaitForAsync(_ => true, TimeSpan.FromSeconds(2));
        var cancelsSelf = bus.WaitForAsync(_ =>
        {
            cts.Cancel();
            return true;
        }, TimeSpan.FromSeconds(2), cts.Token);

        bus.Publish(Msg());
        Assert.Equal("match", (await ordinary).Code);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelsSelf);
    }

    [Fact]
    public async Task Cancellation_registration_and_timer_initialization_race_do_not_hang_or_leak_waiters()
    {
        var bus = new PacketBus();
        // Cancellation races registration creation (including synchronous cancellation).
        for (int i = 0; i < 160; i++)
        {
            using var cts = new CancellationTokenSource();
            // Un thread dédié fait courir l'annulation contre l'enregistrement même si
            // les autres tests occupent tous les workers du pool.
            var cancel = Task.Factory.StartNew(cts.Cancel, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Sous charge xUnit parallèle le timer 30 ms peut gagner avant que Task.Run(cts.Cancel)
            // ne soit planifié : cela testerait le timeout, pas la course registration/cancellation.
            var wait = bus.WaitForAsync(m => m.Code == "unused", TimeSpan.FromSeconds(3), cts.Token);
            await cancel.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        // Every previous waiter was removed: a new Publish does not hang on stale predicates.
        var fresh = bus.WaitForAsync(_ => true, TimeSpan.FromSeconds(2));
        bus.Publish(Msg());
        Assert.True(fresh.IsCompletedSuccessfully);
        Assert.Equal("match", (await fresh).Code);
    }

    [Fact]
    public async Task Competing_publish_cancel_and_timeout_settle_once_under_load()
    {
        var bus = new PacketBus();
        for (int i = 0; i < 150; i++)
        {
            using var cts = new CancellationTokenSource();
            int seq = i;
            using var start = new Barrier(3);
            // Race the operations, not their admission to a potentially saturated pool.
            var send = Task.Factory.StartNew(() =>
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Publish worker rendezvous timed out");
                bus.Publish(Msg(seq.ToString()));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var cancel = Task.Factory.StartNew(() =>
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Cancellation worker rendezvous timed out");
                cts.Cancel();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var wait = bus.WaitForAsync(m => m.Code == seq.ToString(), TimeSpan.FromMilliseconds(20), cts.Token);
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(3)), "Workers did not reach the race barrier");
            await Task.WhenAll(send, cancel).WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                Assert.Equal(seq.ToString(), (await wait.WaitAsync(TimeSpan.FromSeconds(3))).Code);
            }
            catch (OperationCanceledException)
            {
                Assert.True(wait.IsCanceled);
            }
            catch (TimeoutException)
            {
                Assert.True(wait.IsFaulted);
                Assert.IsType<TimeoutException>(wait.Exception!.InnerException);
            }
        }
    }

    [Fact]
    public async Task Invalid_timeout_is_rejected_without_registering_anything()
    {
        var bus = new PacketBus();
        Assert.Throws<ArgumentNullException>(() => { _ = bus.WaitForAsync(null!, TimeSpan.FromSeconds(1)); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = bus.WaitForAsync(_ => true, TimeSpan.FromMilliseconds(-2)); });
        var zero = bus.WaitForAsync(_ => true, TimeSpan.Zero);
        await Assert.ThrowsAsync<TimeoutException>(() => zero);
        var fresh = bus.WaitForAsync(_ => true, TimeSpan.FromSeconds(2));
        bus.Publish(Msg());
        Assert.Equal("match", (await fresh).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_waiters_release_registration_and_timer_while_cts_is_alive(bool timedOut)
    {
        var bus = new PacketBus();
        using var longLivedCts = new CancellationTokenSource();
        var (predicateMarker, pending) = RegisterWithCapturedPredicate(bus, longLivedCts.Token,
            timedOut ? TimeSpan.FromMilliseconds(20) : TimeSpan.FromMinutes(5));

        if (timedOut)
            await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        else
        {
            bus.Publish(Msg());
            Assert.Equal("match", (await pending).Code);
        }

        // With an orphaned CancellationTokenRegistration, the live CTS would keep
        // the Waiter's predicate and marker reachable for its entire lifetime.
        for (int i = 0; i < 6 && predicateMarker.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(predicateMarker.IsAlive);
        GC.KeepAlive(longLivedCts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference PredicateMarker, Task<BotMessage> Pending) RegisterWithCapturedPredicate(
        PacketBus bus, CancellationToken token, TimeSpan timeout)
    {
        var marker = new object();
        var pending = bus.WaitForAsync(m =>
        {
            GC.KeepAlive(marker);
            return m.Code == "match";
        }, timeout, token);
        return (new WeakReference(marker), pending);
    }
}
