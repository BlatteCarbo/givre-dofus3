extern alias F06App;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;
using OrderKey = F06App::GivreClone.App.Services.HdvTaxOrderKeys;

namespace GivreClone.Tests;

/// <summary>
/// Real fsync journal→operator history reconstruction. This read-only desktop
/// path cannot review, resend, settle tax or synthesize a server ACK.
/// </summary>
public sealed class HdvDurableOperatorJourneyV10Tests
{
    private readonly ITestOutputHelper _output;
    public HdvDurableOperatorJourneyV10Tests(ITestOutputHelper output) => _output = output;
    private const string Owner = "server:292:character:99111";
    private const string Other = "server:353:character:99111";

    private sealed class Fixture : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "givre-v10-hdv-operator-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_dir, "hdv", "direct.journal");
        public Journal Open() => new(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Real_send_lease_is_visible_as_intent_attempt_and_correlated_business_result()
    {
        using var folder = new Fixture();
        using var journal = folder.Open();
        string key = OrderKey.Sell(710, 10, 12400, 50);
        Assert.Equal(Decision.Allowed,
            journal.TryBegin(Owner, 91, key, out var lease));
        var intent = Assert.Single(journal.ReadSnapshot(Owner).Rows);
        Assert.Equal(Phase.Started, intent.Phase);
        Assert.True(intent.NeedsReview);
        Assert.Equal("sell", intent.Kind);
        Assert.Equal(91u, intent.OriginalPid);
        Assert.Equal(lease.RunId, intent.LocalRunId);
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.Equal(Phase.Attempted, Assert.Single(journal.ReadSnapshot(Owner).Rows).Phase);
        Assert.True(journal.TryFinish(lease, confirmed: true));
        var final = journal.ReadSnapshot(Owner);
        Assert.Equal(ReadState.Ready, final.State);
        Assert.False(final.HasPending);
        Assert.Equal(Phase.Confirmed, Assert.Single(final.Rows).Phase);
        Assert.Empty(journal.ReadSnapshot(Other).Rows);
        Assert.Equal(Decision.NeedsReview,
            journal.TryBegin(Owner, 91, key, out _)); // never replay same key
    }

    [Fact]
    public void Crash_after_intent_or_after_attempt_does_not_rearm_an_uncertain_buy()
    {
        using var folder = new Fixture();
        var first = folder.Open();
        string key = Journal.BuyOrder(815, 9101, 10, 2700, 19);
        Assert.Equal(Decision.Allowed,
            first.TryBegin(Owner, 91, key, out var lease));
        Assert.True(first.TryMarkAttempt(lease));
        first.Dispose(); // simulated process crash: kernel handles released
        using var recovered = folder.Open();
        var snapshot = recovered.ReadSnapshot(Owner);
        Assert.Equal(ReadState.Ready, snapshot.State);
        Assert.True(snapshot.HasPending);
        Assert.Equal(Phase.Attempted, Assert.Single(snapshot.Rows).Phase);
        Assert.Equal(Decision.NeedsReview,
            recovered.TryBegin(Owner, 9000,
                Journal.BuyOrder(816, 9101, 10, 2700, 20), out _));
        // This is an OPERATOR action, never a timer, wallet delta or ACK.
        Assert.False(recovered.TryReview(Other, "buy:"));
        Assert.True(recovered.TryReview(Owner, "buy:"));
        Assert.Equal(Phase.Reviewed, Assert.Single(recovered.ReadSnapshot(Owner).Rows).Phase);
    }

    [Fact]
    public void Corrupt_append_never_appears_as_clean_empty_operator_history()
    {
        using var folder = new Fixture();
        using (var journal = folder.Open())
        {
            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Owner, 91, OrderKey.Reprice(710, 10, 1000), out var lease));
            Assert.True(journal.TryFinish(lease, confirmed: false));
        }
        string bytes = File.ReadAllText(folder.Path);
        File.AppendAllText(folder.Path, "untrusted|tail\n");
        using var recovered = folder.Open();
        var snapshot = recovered.ReadSnapshot(Owner);
        Assert.Equal(ReadState.StorageUnavailable, snapshot.State);
        Assert.True(snapshot.HasPending);
        Assert.Empty(snapshot.Rows);
        Assert.Equal(Decision.Unavailable,
            recovered.TryBegin(Owner, 91, Journal.RemoveOrder(710, 9101, 10, 1000, 19), out _));
        Assert.Equal(bytes + "untrusted|tail\n", File.ReadAllText(folder.Path));
    }

    [Fact]
    public void Multiple_orders_are_shown_as_distinct_transactions_not_duplicate_phase_rows()
    {
        using var folder = new Fixture();
        using var journal = folder.Open();
        string[] keys =
        [
            OrderKey.Sell(710, 10, 1000, 50),
            OrderKey.Sell(711, 10, 2000, 40),
            OrderKey.Sell(712, 10, 3000, 30)
        ];
        foreach (string key in keys)
        {
            Assert.Equal(Decision.Allowed, journal.TryBegin(Owner, 91, key, out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, confirmed: true));
        }
        var all = journal.ReadSnapshot(Owner, maxRows: 3);
        Assert.Equal(keys.Reverse(), all.Rows.Select(x => x.Order));
        Assert.All(all.Rows, row => Assert.Equal(Phase.Confirmed, row.Phase));
        Assert.Single(journal.ReadSnapshot(Owner, maxRows: 1).Rows);
        Assert.Equal(ReadState.StorageUnavailable,
            journal.ReadSnapshot(Owner, maxRows: 129).State);
    }

    [Fact]
    public async Task Concurrent_reader_never_mutates_inflight_order_or_releases_active_lock()
    {
        using var folder = new Fixture();
        using var sender = folder.Open();
        using var other = folder.Open();
        string key = Journal.RemoveOrder(710, 9101, 10, 1000, 19);
        Assert.Equal(Decision.Allowed,
            sender.TryBegin(Owner, 91, key, out var lease));
        var observations = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                Task.Run(() => other.ReadSnapshot(Owner))));
        Assert.All(observations, state =>
        {
            Assert.Equal(ReadState.Ready, state.State);
            Assert.True(state.HasPending);
            Assert.Equal(Phase.Started, Assert.Single(state.Rows).Phase);
        });
        Assert.False(other.TryReview(Owner, "remove:"));
        Assert.True(sender.TryMarkAttempt(lease));
        Assert.True(sender.TryFinish(lease, confirmed: false));
        Assert.Equal(Phase.Uncertain, Assert.Single(other.ReadSnapshot(Owner).Rows).Phase);
    }

    [Fact]
    public void Missing_parent_is_unavailable_not_a_verified_empty_transaction_history()
    {
        using var folder = new Fixture();
        using var journal = folder.Open();
        var result = journal.ReadSnapshot(Owner);
        Assert.Equal(ReadState.StorageUnavailable, result.State);
        Assert.True(result.HasPending);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Actual_disk_history_read_cost_is_bounded_and_measured_offline()
    {
        using var folder = new Fixture();
        using var journal = folder.Open();
        for (int uid = 100; uid < 108; uid++)
        {
            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Owner, 91,
                    OrderKey.Sell(uid, 10, 1000 + uid, 50), out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, confirmed: true));
        }
        const int samples = 48;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            var snapshot = journal.ReadSnapshot(Owner);
            Assert.Equal(ReadState.Ready, snapshot.State);
            Assert.Equal(8, snapshot.Rows.Count);
            Assert.False(snapshot.HasPending);
        }
        elapsed.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _output.WriteLine($"V10 HDV real durable journal read: {samples} snapshots, " +
            $"{elapsed.Elapsed.TotalMilliseconds:F3} ms, " +
            $"{allocated} caller-thread bytes; excludes DOFUS/network latency.");
        Assert.InRange(allocated, 0, 100_000_000);
    }
}
