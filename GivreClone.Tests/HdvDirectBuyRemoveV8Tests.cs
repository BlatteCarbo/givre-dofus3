extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Disposition = F06App::GivreClone.App.Services.HdvSellDisposition;
using VM = F06App::GivreClone.App.ViewModels;

namespace GivreClone.Tests;

/// <summary>
/// Durable pre-send admission exercised without a client or transport. The
/// operator review only acknowledges uncertainty; it cannot manufacture an ACK.
/// </summary>
public sealed class HdvDirectBuyRemoveV8Tests
{
    private const string Original = "server:292:character:99100";
    private const string Other = "server:292:character:99200";
    private const string DifferentServer = "server:353:character:99100";

    private static string NewPath() => Path.Combine(Path.GetTempPath(),
        "givre-hdv-v8-" + Guid.NewGuid().ToString("N"), "direct.journal");

    [Fact]
    public void Buy_and_remove_orders_record_the_real_fields_and_decoder_lifetime()
    {
        string buy = Journal.BuyOrder(171, 2416, 10, 2345, 51);
        string remove = Journal.RemoveOrder(982, 2416, 10, 3500, 52);
        Assert.Equal("buy:171:2416:10:2345:stock-unknown:epoch-51", buy);
        Assert.Equal("remove:982:2416:10:3500:stock-10:epoch-52", remove);

        using var journal = new Journal(NewPath());
        Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 50, buy, out var lease));
        Assert.NotEqual(Guid.Empty, lease.RunId);
        Assert.True(journal.NeedsReview(Original, "buy:"));
        Assert.False(journal.NeedsReview(Original, "remove:"));
        Assert.True(journal.NeedsReview(Original));
        Assert.False(journal.TryReview(Original, "buy:")); // sender still alive
        // The live writer retains an OS handle: a competitor is either
        // semantically quarantined or temporarily unavailable while that
        // exclusive handle exists. The invariant is NO second admission.
        Assert.NotEqual(Decision.Allowed,
            journal.TryBegin(Original, 51, remove, out _));
        Assert.True(journal.TryFinish(lease, false));
        Assert.False(journal.TryReview(Original, "remove:"));
        Assert.True(journal.TryReview(Original, "buy:"));
        Assert.False(journal.NeedsReview(Original));
        Assert.Equal(Decision.NeedsReview, journal.TryBegin(Original, 51, buy, out _));
        Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 51, remove, out _));
    }

    [Fact]
    public void Missing_uid_gid_quantity_price_stock_or_epoch_is_rejected_before_any_journal_write()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        string[] malformed =
        [
            "buy:", "buy:0:2416:1:100:stock-unknown:epoch-5",
            "buy:1:0:1:100:stock-unknown:epoch-5",
            "buy:1:2416:2:100:stock-unknown:epoch-5",
            "buy:1:2416:1:0:stock-unknown:epoch-5",
            "buy:1:2416:1:100:stock-10:epoch-5",
            "buy:1:2416:1:100:stock-unknown:epoch-0",
            "remove:9:2416:10:100:stock-1:epoch-5",
            "remove:9:2416:10:100:stock-10:epoch-0"
        ];
        foreach (string order in malformed)
            Assert.Equal(Decision.Unavailable, journal.TryBegin(Original, 9, order, out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Crash_restart_and_late_confirmation_cannot_clear_buy_or_repeat_it()
    {
        string path = NewPath();
        string buy = Journal.BuyOrder(771, 2400, 1, 200, 71);
        var original = new Journal(path);
        Assert.Equal(Decision.Allowed, original.TryBegin(Original, 800, buy, out var lease));
        byte[] persisted = File.ReadAllBytes(path);
        Assert.NotEmpty(persisted);
        Assert.True(File.Exists(path + ".anchor"));
        original.Dispose(); // model process crash: OS releases the in-flight file lock

        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Original, "buy:"));
        Assert.Equal(Decision.NeedsReview,
            restarted.TryBegin(Original, 800, Journal.BuyOrder(772, 2400, 1, 201, 72), out _));
        Assert.False(restarted.TryFinish(lease, true)); // old process cannot commit late ACK
        Assert.False(restarted.TryReview(Original, "remove:"));
        Assert.True(restarted.TryReview(Original, "buy:")); // explicit operator only
        Assert.False(restarted.NeedsReview(Original));
        Assert.False(restarted.TryFinish(lease, true));
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Original, 801, buy, out _));
    }

    [Fact]
    public void Remove_crash_and_PID_reuse_require_original_character_review()
    {
        string path = NewPath();
        using (var previous = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, previous.TryBegin(Original, 800,
                Journal.RemoveOrder(100, 2400, 10, 3000, 42), out _));
        }
        var launch = NewLaunchWithJournal(path);
        SeedIdentity(launch, 800, 292, 99100);
        Assert.Equal(Disposition.NeedsReview, launch.HdvRemoveLastDisposition(800));
        Assert.Null(launch.HdvSellLastDisposition(800));
        Assert.False(launch.HdvRepriceReviewPending(800));
        Assert.False(launch.HdvAcknowledgeBuyReview(800));
        Assert.False(launch.HdvAcknowledgeRepriceReview(800));
        SeedIdentity(launch, 800, 292, 99200); // Windows reused the PID
        Assert.Null(launch.HdvRemoveLastDisposition(800));
        Assert.False(launch.HdvAcknowledgeRemoveReview(800));
        SeedIdentity(launch, 801, 292, 99100);
        Assert.Equal(Disposition.NeedsReview, launch.HdvRemoveLastDisposition(801));
        Assert.True(launch.HdvAcknowledgeRemoveReview(801));
        Assert.Null(launch.HdvRemoveLastDisposition(801));
        Assert.False(launch.HdvAcknowledgeRemoveReview(801));
    }

    [Fact]
    public void Buy_review_is_original_profile_scoped_and_never_acknowledges_another_kind()
    {
        string path = NewPath();
        using (var previous = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, previous.TryBegin(Original, 800,
                Journal.BuyOrder(101, 2400, 1, 500, 42), out var lease));
            Assert.True(previous.TryFinish(lease, false));
        }
        var launch = NewLaunchWithJournal(path);
        SeedIdentity(launch, 800, 292, 99100);
        Assert.True(launch.HdvBuyReviewPending(800));
        Assert.Null(launch.HdvSellLastDisposition(800));
        Assert.False(launch.HdvRepriceReviewPending(800));
        Assert.False(launch.HdvAcknowledgeRemoveReview(800));
        Assert.False(launch.HdvSellManuallyReconciled(800));
        SeedIdentity(launch, 800, 292, 99200);
        Assert.False(launch.HdvBuyReviewPending(800));
        Assert.False(launch.HdvAcknowledgeBuyReview(800));
        SeedIdentity(launch, 801, 353, 99100);
        Assert.False(launch.HdvBuyReviewPending(801));
        SeedIdentity(launch, 802, 292, 99100);
        Assert.True(launch.HdvBuyReviewPending(802));
        Assert.True(launch.HdvAcknowledgeBuyReview(802));
        Assert.False(launch.HdvBuyReviewPending(802));
        Assert.False(launch.HdvAcknowledgeBuyReview(802));
    }

    [Fact]
    public void Late_remove_timeout_after_source_swap_stays_attributed_to_old_character()
    {
        string path = NewPath();
        var launch = NewLaunchWithJournal(path);
        SeedIdentity(launch, 41, 292, 99100);
        typeof(Launch).GetMethod("FlagHdvRemoveReview",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(launch, [41u, Original, 882, 10]);
        Assert.Equal(Disposition.NeedsReview, launch.HdvRemoveLastDisposition(41));
        SeedIdentity(launch, 41, 292, 99200);
        Assert.Null(launch.HdvRemoveLastDisposition(41));
        Assert.False(launch.HdvAcknowledgeRemoveReview(41));
        SeedIdentity(launch, 42, 292, 99100);
        Assert.False(launch.HdvAcknowledgeRemoveReview(42)); // PID-local memory only
        SeedIdentity(launch, 41, 292, 99100);
        Assert.True(launch.HdvAcknowledgeRemoveReview(41));
        Assert.Null(launch.HdvRemoveLastDisposition(41));
    }

    [Fact]
    public async Task Concurrent_sell_reprice_buy_and_remove_have_one_shared_fsynced_sender()
    {
        string path = NewPath();
        var decisions = new ConcurrentBag<Decision>();
        string[] orders =
        [
            "sell:81:1:200:1", "reprice:33:1:150",
            Journal.BuyOrder(77, 2416, 10, 1200, 43),
            Journal.RemoveOrder(22, 2416, 10, 1200, 43)
        ];
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            using var instance = new Journal(path);
            decisions.Add(instance.TryBegin(Original, (uint)(1000 + i), orders[i % orders.Length], out _));
        })));
        Assert.Equal(1, decisions.Count(d => d == Decision.Allowed));
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Original));
        // A different server/character has a distinct quarantine namespace.
        Assert.Equal(Decision.Allowed, restarted.TryBegin(Other, 1002,
            Journal.BuyOrder(77, 2416, 10, 1200, 43), out var otherLease));
        Assert.True(restarted.TryFinish(otherLease, true));
        Assert.Equal(Decision.Allowed, restarted.TryBegin(DifferentServer, 1002,
            Journal.RemoveOrder(22, 2416, 10, 1200, 43), out _));
    }

    [Fact]
    public async Task Real_batch_runner_stops_on_second_distinct_offer_without_proof()
    {
        string path = NewPath();
        using var port = new JournalBackedBuyPort(path, true, false, true);
        // The fake port models three distinct offer UIDs. The second has an
        // unproven result, so HdvBuyRunner must not reach the third or count it.
        var result = await VM.HdvBuyRunner.RunAsync(port,
            new VM.BuyRequest(2416, 30, [new VM.LotQuote(10, 200, true, 300)]),
            2000, false, CancellationToken.None);
        Assert.Equal(2, port.Attempts);
        Assert.Equal(10, result.Bought);
        Assert.Equal(200, result.Spent);
        Assert.Equal(20, result.Uncovered);
        Assert.Equal("achat refusé", result.Stop);
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Original, "buy:"));
    }

    [Fact]
    public async Task UID_only_receipt_does_not_count_as_batch_success_without_price_proof()
    {
        string path = NewPath();
        using var port = new JournalBackedBuyPort(path, false, true);
        var result = await VM.HdvBuyRunner.RunAsync(port,
            new VM.BuyRequest(2416, 20, [new VM.LotQuote(10, 200, true, 300)]),
            1000, false, CancellationToken.None, cheaperUnitMarginPct: 5);
        Assert.Equal(1, port.Attempts);
        Assert.Equal(0, result.Bought);
        Assert.Equal(0, result.Spent);
        Assert.Equal(20, result.Uncovered);
        using var persisted = new Journal(path);
        Assert.True(persisted.NeedsReview(Original, "buy:"));
    }

    [Fact]
    public void Corrupt_journal_or_anchor_preserves_bytes_and_forbids_all_reviews_and_sends()
    {
        string path = NewPath();
        using (var first = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, first.TryBegin(Original, 3,
                Journal.RemoveOrder(66, 99, 1, 100, 3), out var lease));
            Assert.True(first.TryFinish(lease, false));
        }
        File.AppendAllText(path, "partial-garbage");
        byte[] damaged = File.ReadAllBytes(path);
        byte[] anchor = File.ReadAllBytes(path + ".anchor");
        using var damagedJournal = new Journal(path);
        Assert.True(damagedJournal.NeedsReview(Original, "buy:"));
        Assert.True(damagedJournal.NeedsReview(Original, "remove:"));
        Assert.Equal(Decision.Unavailable, damagedJournal.TryBegin(Original, 3,
            Journal.BuyOrder(33, 99, 1, 101, 3), out _));
        Assert.False(damagedJournal.TryReview(Original, "remove:"));
        Assert.Equal(damaged, File.ReadAllBytes(path));
        Assert.Equal(anchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Buy_UI_getter_rechecks_disk_and_fails_closed_after_an_external_corruption()
    {
        string path = NewPath();
        var launch = NewLaunchWithJournal(path);
        SeedIdentity(launch, 987, 292, 99100);
        // The initial no-journal state is readable and has no pending purchase.
        Assert.False(launch.HdvBuyReviewPending(987));
        using (var external = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, external.TryBegin(Original, 888,
                Journal.BuyOrder(77, 2416, 1, 500, 42), out var lease));
            Assert.True(external.TryFinish(lease, false));
        }
        // No cached false from the VM getter can authorize another buy.
        Assert.True(launch.HdvBuyReviewPending(987));
        Assert.True(launch.HdvAcknowledgeBuyReview(987));
        Assert.False(launch.HdvBuyReviewPending(987));

        byte[] intact = File.ReadAllBytes(path);
        File.AppendAllText(path, "\npartial-invalid-record");
        byte[] corrupt = File.ReadAllBytes(path);
        Assert.NotEqual(intact, corrupt);
        Assert.True(launch.HdvBuyReviewPending(987));
        Assert.False(launch.HdvAcknowledgeBuyReview(987));
        Assert.Equal(corrupt, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Direct_calls_without_an_authorized_live_transport_cannot_write_an_intent()
    {
        string path = NewPath();
        var launch = NewLaunchWithJournal(path);
        SeedIdentity(launch, 800, 292, 99100);
        // An observed offer alone cannot replace a live source/decoder lease.
        launch.Hdv.SetOffer(800, 2416, 77, [400, 4000]);
        Assert.False(await launch.HdvBuyAsync(800, 77, 1, 400));
        Assert.False(await launch.HdvRemoveAsync(800, 77, 1));
        Assert.False(File.Exists(path));
        Assert.False(launch.HdvAcknowledgeBuyReview(800));
        Assert.False(launch.HdvAcknowledgeRemoveReview(800));
    }

    private static Launch NewLaunchWithJournal(string path)
    {
        var launch = new Launch();
        typeof(Launch).GetField("_hdvDirectMutations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(launch, new Journal(path));
        return launch;
    }

    // Deterministic port exercises the existing runner and the real journal;
    // true means an exactly corroborated outcome in this fixture. UID-only
    // observations are deliberately represented as false, not invented ACKs.
    private sealed class JournalBackedBuyPort : VM.IHdvBuyPort, IDisposable
    {
        private readonly Journal _journal;
        private readonly Queue<bool> _proofs;
        public int Attempts { get; private set; }

        public JournalBackedBuyPort(string path, params bool[] proofs)
        {
            _journal = new Journal(path);
            _proofs = new Queue<bool>(proofs);
        }

        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
            => Task.FromResult(200L);

        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int uid = 100 + Attempts++;
            if (_journal.TryBegin(Original, 900,
                    Journal.BuyOrder(uid, gid, qty, 200, 50), out var lease) != Decision.Allowed)
                return Task.FromResult(false);
            bool proven = _proofs.Dequeue();
            return Task.FromResult(_journal.TryFinish(lease, proven) && proven);
        }

        public void Dispose() => _journal.Dispose();
    }

    private static void SeedIdentity(Launch launch, uint pid, int serverId, long characterId)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var server = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(launch)!;
        var actor = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(launch)!;
        server[pid] = (serverId, "fixture");
        actor[pid] = characterId;
    }
}
