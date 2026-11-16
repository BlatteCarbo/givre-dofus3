extern alias F06App;

using Flow = F06App::GivreClone.App.Services.HdvTaxedMutationFlow;
using Ledger = F06App::GivreClone.App.Services.HdvTaxReservations;
using Phase = F06App::GivreClone.App.Services.HdvTaxOrderPhase;
using Decision = F06App::GivreClone.App.Services.HdvTaxOrderReservation;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// App mutation seam under the same flow called by manual, Bulk and watcher.
/// The sender is faked: no network, game, or unearned server ACK.
/// </summary>
public sealed class HdvTaxedMutationFlowTests
{
    private const string Profile = "server:292:Character";

    private sealed class Journal : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "givre-hdv-flow-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(_root, "taxes.journal");
        public Ledger Reopen() => new(PathName);
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Sale_attempt_is_durable_before_callback_and_confirmed_result_retains_fee()
    {
        using var journal = new Journal();
        int sends = 0;
        var result = await Flow.ExecuteAsync(journal.Reopen(), 13, Profile,
            "sell:73:10:500:100", 100, 0, 500, 5f, _ =>
            {
                sends++;
                Assert.Equal(Phase.Attempted,
                    journal.Reopen().ReadOrderPhase(Profile, "sell:73:10:500:100"));
                Assert.Equal(25, journal.Reopen().PendingDurable(Profile));
                return Task.FromResult(true); // A simulated uniquely correlated listing, NOT a tax receipt.
            });
        Assert.Equal(Decision.Reserved, result.Decision);
        Assert.True(result.ServerResultObserved);
        Assert.Equal(1, sends);
        Assert.Equal(Phase.Observed, journal.Reopen().ReadOrderPhase(Profile, "sell:73:10:500:100"));
        Assert.Equal(25, journal.Reopen().PendingDurable(Profile));
        Assert.Equal(Decision.AlreadyReserved,
            (await Flow.ExecuteAsync(journal.Reopen(), 25, Profile,
                "sell:73:10:500:100", 100, 0, 500, 5f, _ =>
                {
                    sends++;
                    return Task.FromResult(true);
                })).Decision);
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Reprice_without_proven_outcome_becomes_uncertain_and_blocks_new_order()
    {
        using var journal = new Journal();
        int sends = 0;
        var result = await Flow.ExecuteAsync(journal.Reopen(), 13, Profile,
            "reprice:9:1:1000", 100, 0, 1000, 5f, _ =>
            {
                sends++;
                return Task.FromResult(false); // could have emitted before a timeout
            });
        Assert.Equal(Decision.Reserved, result.Decision);
        Assert.False(result.ServerResultObserved);
        Assert.Equal(Phase.Uncertain, journal.Reopen().ReadOrderPhase(Profile, "reprice:9:1:1000"));
        Assert.Equal(50, journal.Reopen().PendingDurable(Profile));
        var denied = await Flow.ExecuteAsync(journal.Reopen(), 99, Profile,
            "reprice:10:1:900", 100, 0, 900, 5f, _ =>
            {
                sends++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.NeedsReconciliation, denied.Decision);
        Assert.Equal(1, sends);
        Assert.Equal(50, journal.Reopen().PendingDurable(Profile));
    }

    [Fact]
    public async Task Sender_exception_after_possible_emission_keeps_uncertain_no_retry()
    {
        using var journal = new Journal();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Flow.ExecuteAsync(
            journal.Reopen(), 1, Profile, "sell:2:1:400:3", 100, 0, 400, 5f,
            _ => throw new InvalidOperationException("ambiguous transport failure")));
        Assert.Equal(Phase.Uncertain, journal.Reopen().ReadOrderPhase(Profile, "sell:2:1:400:3"));
        Assert.Equal(20, journal.Reopen().PendingDurable(Profile));
        Assert.Equal(Decision.AlreadyReserved, (await Flow.ExecuteAsync(
            journal.Reopen(), 9, Profile, "sell:2:1:400:3", 100, 0, 400, 5f,
            _ => throw new Exception("must not invoke"))).Decision);
    }

    [Fact]
    public async Task Cancelled_after_durable_attempt_stays_uncertain_and_does_not_send()
    {
        using var journal = new Journal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool invoked = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Flow.ExecuteAsync(
            journal.Reopen(), 1, Profile, "sell:5:1:100:2", 100, 0, 100, 5f,
            _ => { invoked = true; return Task.FromResult(true); }, cts.Token));
        Assert.False(invoked);
        Assert.Equal(Phase.Uncertain, journal.Reopen().ReadOrderPhase(Profile, "sell:5:1:100:2"));
    }

    [Fact]
    public async Task Crash_at_reserved_or_attempted_phase_never_reemits_existing_order()
    {
        using var journal = new Journal();
        Assert.Equal(Decision.Reserved,
            journal.Reopen().TryReserveOrder(2, Profile, "sell:7:10:200:90", 100, 0, 10));
        Assert.Equal(Phase.Reserved, journal.Reopen().ReadOrderPhase(Profile, "sell:7:10:200:90"));
        Assert.Equal(Decision.AlreadyReserved, (await Flow.ExecuteAsync(
            journal.Reopen(), 3, Profile, "sell:7:10:200:90", 100, 0, 200, 5f,
            _ => throw new Exception("no dispatch"))).Decision);
        Assert.True(journal.Reopen().TryAdvanceOrder(2, Profile,
            "sell:7:10:200:90", Phase.Attempted));
        Assert.Equal(Phase.Attempted, journal.Reopen().ReadOrderPhase(Profile, "sell:7:10:200:90"));
        Assert.Equal(Decision.NeedsReconciliation, (await Flow.ExecuteAsync(
            journal.Reopen(), 99, Profile, "sell:8:10:200:90", 100, 0, 200, 5f,
            _ => throw new Exception("no dispatch"))).Decision);
        Assert.False(journal.Reopen().TryAdvanceOrder(99, Profile,
            "sell:7:10:200:90", Phase.Observed));
        Assert.Equal(10, journal.Reopen().PendingDurable(Profile));
    }

    [Fact]
    public async Task Independent_profiles_remain_isolated_after_unknown_result()
    {
        using var journal = new Journal();
        await Flow.ExecuteAsync(journal.Reopen(), 1, Profile, "sell:1", 50, 0, 100, 5f,
            _ => Task.FromResult(false));
        var other = await Flow.ExecuteAsync(journal.Reopen(), 2, "server:353:Character",
            "sell:1", 50, 0, 100, 5f, _ => Task.FromResult(true));
        Assert.Equal(Decision.Reserved, other.Decision);
        Assert.Equal(5, journal.Reopen().PendingDurable(Profile));
        Assert.Equal(5, journal.Reopen().PendingDurable("server:353:Character"));
    }

    [Theory]
    [InlineData(0, 0, 5f)]
    [InlineData(-1, 100, 5f)]
    [InlineData(100, -1, 5f)]
    [InlineData(100, 100, float.NaN)]
    [InlineData(100, 100, float.PositiveInfinity)]
    public async Task Invalid_price_rate_and_overflow_cannot_invoke_sender(
        long cap, long price, float percent)
    {
        using var journal = new Journal();
        bool invoked = false;
        var result = await Flow.ExecuteAsync(journal.Reopen(), 1, Profile,
            "sell:overflow", cap, 0, price, percent,
            _ => { invoked = true; return Task.FromResult(true); });
        Assert.Equal(Decision.Unavailable, result.Decision);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Extreme_price_hits_budget_without_truncation_or_dispatch()
    {
        using var journal = new Journal();
        bool invoked = false;
        var result = await Flow.ExecuteAsync(journal.Reopen(), 1, Profile,
            "sell:huge", 100, 0, long.MaxValue, 100f,
            _ => { invoked = true; return Task.FromResult(true); });
        Assert.Equal(Decision.CapExceeded, result.Decision);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Unlimited_tax_setting_records_intents_but_never_imposes_a_money_ceiling()
    {
        using var journal = new Journal();
        int sends = 0;
        var first = await Flow.ExecuteAsync(journal.Reopen(), 1, Profile,
            "sell:same", 0, 0, 100, 5f, _ =>
            { sends++; return Task.FromResult(true); });
        Assert.True(first.ServerResultObserved);
        Assert.Equal(Phase.Observed, journal.Reopen().ReadOrderPhase(Profile, "sell:same"));
        var replay = await Flow.ExecuteAsync(journal.Reopen(), 2, Profile,
            "sell:same", 0, 0, 100, 5f, _ =>
            { sends++; return Task.FromResult(true); });
        Assert.Equal(Decision.AlreadyReserved, replay.Decision);
        Assert.Equal(1, sends);

        // Monetary cap stays unlimited even if the durable fee summary
        // saturates; every distinct order still needs its own fsynced record.
        var huge = await Flow.ExecuteAsync(journal.Reopen(), 2, Profile,
            "sell:huge", 0, 0, long.MaxValue, 100f, _ =>
            { sends++; return Task.FromResult(true); });
        Assert.True(huge.ServerResultObserved);
        var afterSaturation = await Flow.ExecuteAsync(journal.Reopen(), 2, Profile,
            "sell:after-huge", 0, 0, 200, 5f, _ =>
            { sends++; return Task.FromResult(true); });
        Assert.True(afterSaturation.ServerResultObserved);
        Assert.Equal(3, sends);
        Assert.True(File.Exists(journal.PathName));
        Assert.Equal(long.MaxValue, journal.Reopen().PendingDurable(Profile));
    }

    [Fact]
    public async Task Unlimited_tax_crash_and_uncertain_sender_block_replay_and_other_orders()
    {
        using var journal = new Journal();
        int sends = 0;
        var pending = await Flow.ExecuteAsync(journal.Reopen(), 5, Profile,
            "sell:uncapped-uncertain", 0, 0, 100, 5f, _ =>
            { sends++; return Task.FromResult(false); });
        Assert.False(pending.ServerResultObserved);
        Assert.Equal(Phase.Uncertain,
            journal.Reopen().ReadOrderPhase(Profile, "sell:uncapped-uncertain"));
        var duplicate = await Flow.ExecuteAsync(journal.Reopen(), 6, Profile,
            "sell:uncapped-uncertain", 0, 0, 100, 5f, _ =>
            { sends++; return Task.FromResult(true); });
        var different = await Flow.ExecuteAsync(journal.Reopen(), 7, Profile,
            "sell:uncapped-new", 0, 0, 100, 5f, _ =>
            { sends++; return Task.FromResult(true); });
        Assert.Equal(Decision.AlreadyReserved, duplicate.Decision);
        Assert.Equal(Decision.NeedsReconciliation, different.Decision);
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Unlimited_tax_inflight_attempt_is_durable_barrier_for_other_instance()
    {
        using var journal = new Journal();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Flow.ExecuteAsync(journal.Reopen(), 5, Profile,
            "sell:uncapped-first", 0, 0, 100, 5f, _ => completion.Task);
        Assert.Equal(Phase.Attempted,
            journal.Reopen().ReadOrderPhase(Profile, "sell:uncapped-first"));
        int secondSends = 0;
        var second = await Flow.ExecuteAsync(journal.Reopen(), 6, Profile,
            "sell:uncapped-other", 0, 0, 100, 5f, _ =>
            { secondSends++; return Task.FromResult(true); });
        Assert.Equal(Decision.NeedsReconciliation, second.Decision);
        Assert.Equal(0, secondSends);
        completion.SetResult(true);
        Assert.True((await first).ServerResultObserved);
    }

    [Fact]
    public async Task Disabling_cap_does_not_unlock_old_uncertain_or_duplicate_persisted_orders()
    {
        using var journal = new Journal();
        int sends = 0;
        var uncertain = await Flow.ExecuteAsync(journal.Reopen(), 1, Profile,
            "sell:uncertain", 100, 0, 100, 5f, _ =>
            {
                sends++;
                return Task.FromResult(false);
            });
        Assert.False(uncertain.ServerResultObserved);
        var bypass = await Flow.ExecuteAsync(journal.Reopen(), 3, Profile,
            "sell:different", 0, 0, 100, 5f, _ =>
            {
                sends++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.NeedsReconciliation, bypass.Decision);
        Assert.Equal(1, sends);
        Assert.Equal(5, journal.Reopen().PendingDurable(Profile));

        using var secondJournal = new Journal();
        var confirmed = await Flow.ExecuteAsync(secondJournal.Reopen(), 1, Profile,
            "sell:previous", 100, 0, 100, 5f, _ => Task.FromResult(true));
        Assert.True(confirmed.ServerResultObserved);
        var same = await Flow.ExecuteAsync(secondJournal.Reopen(), 2, Profile,
            "sell:previous", 0, 0, 100, 5f, _ =>
            {
                sends++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.AlreadyReserved, same.Decision);
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Corrupted_journal_and_anchor_refuse_actual_mutation_without_mutating_bytes()
    {
        using var journal = new Journal();
        await Flow.ExecuteAsync(journal.Reopen(), 1, Profile, "sell:first", 100, 0, 100, 5f,
            _ => Task.FromResult(true));
        var bad = File.ReadAllText(journal.PathName) + "broken phase\n";
        File.WriteAllText(journal.PathName, bad);
        bool invoked = false;
        var result = await Flow.ExecuteAsync(journal.Reopen(), 1, Profile,
            "sell:second", 100, 0, 100, 5f, _ =>
            { invoked = true; return Task.FromResult(true); });
        Assert.Equal(Decision.Unavailable, result.Decision);
        Assert.False(invoked);
        Assert.Equal(bad, File.ReadAllText(journal.PathName));
    }

    [Fact]
    public async Task Two_instances_observe_attempt_as_barrier_even_before_sender_returns()
    {
        using var journal = new Journal();
        var inFlight = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Flow.Result> first = Flow.ExecuteAsync(journal.Reopen(), 10, Profile,
            "sell:one", 100, 0, 200, 5f, _ =>
            {
                inFlight.TrySetResult(true);
                return finish.Task;
            });
        await inFlight.Task;
        Assert.Equal(Phase.Attempted, journal.Reopen().ReadOrderPhase(Profile, "sell:one"));
        bool secondSent = false;
        var second = await Flow.ExecuteAsync(journal.Reopen(), 11, Profile,
            "sell:two", 100, 0, 200, 5f, _ =>
            { secondSent = true; return Task.FromResult(true); });
        Assert.Equal(Decision.NeedsReconciliation, second.Decision);
        Assert.False(secondSent);
        finish.TrySetResult(true);
        Assert.True((await first).ServerResultObserved);
    }
}
