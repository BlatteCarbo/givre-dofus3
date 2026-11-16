extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Disposition = F06App::GivreClone.App.Services.HdvSellDisposition;

namespace GivreClone.Tests;

/// <summary>
/// V9 integration contract at the shared actual-dispatch journal used by
/// manual desktop, Bulk, Lua and production LaunchService entrypoints.
/// No test creates a game client, transport, fictional server ACK or tax entry.
/// </summary>
public sealed class HdvCrossEntryTransactionJourneyV9Tests
{
    private const string Character = "server:292:character:99100";
    private const string OtherCharacter = "server:292:character:99200";
    private const string OtherServer = "server:353:character:99100";

    private static string Scratch() => Path.Combine(Path.GetTempPath(),
        "givre-hdv-v9-" + Guid.NewGuid().ToString("N"), "direct.journal");

    [Fact]
    public void Four_mutating_entry_kinds_share_fsynced_intent_attempt_and_result()
    {
        string path = Scratch();
        string[] orders =
        [
            "sell:71:10:1500:20",
            "reprice:82:10:1499",
            Journal.BuyOrder(93, 2416, 10, 1000, 51),
            Journal.RemoveOrder(104, 2416, 10, 1500, 51)
        ];
        using var journal = new Journal(path);
        foreach (string order in orders)
        {
            Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 100, order, out var lease));
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".anchor"));
            using (var observer = new Journal(path))
            {
                Assert.True(observer.NeedsReview(Character));
                Assert.NotEqual(Decision.Allowed, observer.TryBegin(Character, 101,
                    "sell:222:1:300:1", out _));
            }
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.False(journal.TryMarkAttempt(lease)); // one send admission, not two
            Assert.True(journal.TryFinish(lease, true)); // fixture-supplied exact proof
            Assert.False(journal.TryMarkAttempt(lease));
            Assert.False(journal.TryFinish(lease, true)); // no late duplicate completion
            Assert.False(journal.NeedsReview(Character));
            Assert.Equal(Decision.NeedsReview, journal.TryBegin(Character, 101, order, out _));
        }
        using var restarted = new Journal(path);
        Assert.False(restarted.NeedsReview(Character));
        foreach (var order in orders)
            Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Character, 101, order, out _));
    }

    [Fact]
    public void Malformed_sell_and_reprice_stock_quantity_price_or_uid_fail_before_disk_write()
    {
        string path = Scratch();
        using var journal = new Journal(path);
        string[] malformed =
        [
            "sell:", "sell:0:1:200:1", "sell:10:0:200:1",
            "sell:10:10:0:10", "sell:10:10:200:9", "sell:10:1:200:-1",
            "sell:010:1:200:1", "sell:10:1:0200:1", "sell:10:1:200:1:extra",
            "reprice:", "reprice:0:1:200", "reprice:10:0:200",
            "reprice:10:1:0", "reprice:010:1:200", "reprice:10:1:200:1"
        ];
        foreach (var order in malformed)
            Assert.Equal(Decision.Unavailable, journal.TryBegin(Character, 100, order, out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Started_without_attempt_after_crash_is_not_mistaken_for_no_send()
    {
        string path = Scratch();
        var beforeCrash = new Journal(path);
        string buy = Journal.BuyOrder(23, 2416, 1, 500, 51);
        Assert.Equal(Decision.Allowed, beforeCrash.TryBegin(Character, 800, buy, out var lease));
        beforeCrash.Dispose(); // releases only the kernel lock, never the started intent
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Character, "buy:"));
        Assert.False(restarted.TryMarkAttempt(lease));
        Assert.False(restarted.TryFinish(lease, true));
        Assert.NotEqual(Decision.Allowed, restarted.TryBegin(Character, 801,
            Journal.BuyOrder(24, 2416, 1, 500, 52), out _));
        Assert.False(restarted.TryReview(Character, "remove:"));
        Assert.True(restarted.TryReview(Character, "buy:"));
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Character, 801, buy, out _));
        Assert.Equal(Decision.Allowed, restarted.TryBegin(Character, 801,
            Journal.BuyOrder(24, 2416, 1, 500, 52), out _));
    }

    [Fact]
    public void Attempted_without_result_survives_crash_and_pid_reuse()
    {
        string path = Scratch();
        var sender = new Journal(path);
        string remove = Journal.RemoveOrder(44, 2416, 10, 2700, 60);
        Assert.Equal(Decision.Allowed, sender.TryBegin(Character, 800, remove, out var lease));
        Assert.True(sender.TryMarkAttempt(lease));
        sender.Dispose();
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Character, "remove:"));
        Assert.False(restarted.NeedsReview(OtherCharacter));
        Assert.False(restarted.NeedsReview(OtherServer));
        Assert.False(restarted.TryFinish(lease, true)); // old PID / run never a receipt
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Character, 800,
            Journal.RemoveOrder(45, 2416, 10, 2700, 61), out _));
        Assert.True(restarted.TryReview(Character, "remove:"));
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Character, 800, remove, out _));
        Assert.Equal(Decision.Allowed, restarted.TryBegin(Character, 800,
            Journal.RemoveOrder(45, 2416, 10, 2700, 61), out var next));
        Assert.True(restarted.TryMarkAttempt(next));
        Assert.True(restarted.TryFinish(next, false));
        Assert.True(restarted.NeedsReview(Character, "remove:"));
    }

    [Fact]
    public async Task Concurrent_bulk_lua_desktop_and_production_orders_have_one_profile_owner()
    {
        string path = Scratch();
        string[] orders =
        [
            "sell:81:1:100:1",
            "reprice:82:1:99",
            Journal.BuyOrder(83, 2416, 1, 100, 71),
            Journal.RemoveOrder(84, 2416, 1, 100, 71)
        ];
        var decisions = new ConcurrentBag<Decision>();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
        {
            using var contender = new Journal(path);
            Decision result = contender.TryBegin(Character, (uint)(100 + i),
                orders[i % orders.Length], out var lease);
            decisions.Add(result);
            // An admitted transaction is deliberately abandoned here, as on
            // process death; other entrypoints cannot auto-clear its intent.
        })));
        Assert.Equal(1, decisions.Count(x => x == Decision.Allowed));
        using var journal = new Journal(path);
        Assert.True(journal.NeedsReview(Character));
        Assert.Equal(Decision.Allowed, journal.TryBegin(OtherCharacter, 300,
            orders[0], out var another));
        Assert.True(journal.TryMarkAttempt(another));
        Assert.True(journal.TryFinish(another, true));
        Assert.Equal(Decision.Allowed, journal.TryBegin(OtherServer, 400,
            orders[0], out var otherServer));
        Assert.True(journal.TryMarkAttempt(otherServer));
        Assert.True(journal.TryFinish(otherServer, true));
    }

    [Fact]
    public void Corrupt_attempt_or_anchor_fails_closed_without_repairing_bytes()
    {
        string path = Scratch();
        using (var original = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, original.TryBegin(Character, 9,
                "sell:101:10:3000:10", out var lease));
            Assert.True(original.TryMarkAttempt(lease));
        }
        File.AppendAllText(path, "garbled-attempt");
        byte[] journalBytes = File.ReadAllBytes(path);
        byte[] anchorBytes = File.ReadAllBytes(path + ".anchor");
        using (var damaged = new Journal(path))
        {
            Assert.True(damaged.NeedsReview(Character, "remove:")); // corruption overrides type filter
            Assert.Equal(Decision.Unavailable, damaged.TryBegin(Character, 10,
                Journal.RemoveOrder(55, 2416, 10, 3000, 80), out _));
            Assert.False(damaged.TryReview(Character, "sell:"));
        }
        Assert.Equal(journalBytes, File.ReadAllBytes(path));
        Assert.Equal(anchorBytes, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Distinct_stock_epoch_and_UID_do_not_override_unreviewed_or_replayed_orders()
    {
        using var journal = new Journal(Scratch());
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 10,
            "sell:201:10:500:20", out var first));
        Assert.True(journal.TryMarkAttempt(first));
        Assert.True(journal.TryFinish(first, true));
        Assert.Equal(Decision.NeedsReview,
            journal.TryBegin(Character, 10, "sell:201:10:500:20", out _));
        // A confirmed partial sale can create a new, independently observed
        // stock count, but cannot replay the previous stock snapshot.
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 10,
            "sell:201:10:500:10", out var second));
        Assert.True(journal.TryMarkAttempt(second));
        Assert.True(journal.TryFinish(second, true));
        string oldBuy = Journal.BuyOrder(301, 2416, 1, 300, 10);
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 10, oldBuy, out var buy));
        Assert.True(journal.TryMarkAttempt(buy));
        Assert.True(journal.TryFinish(buy, false));
        Assert.Equal(Decision.NeedsReview, journal.TryBegin(Character, 10,
            Journal.BuyOrder(301, 2416, 1, 300, 11), out _));
        Assert.True(journal.TryReview(Character, "buy:"));
        Assert.Equal(Decision.NeedsReview, journal.TryBegin(Character, 10, oldBuy, out _));
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 10,
            Journal.BuyOrder(301, 2416, 1, 300, 11), out _));
    }

    [Fact]
    public async Task Real_launch_entrypoints_refuse_unverified_source_before_creating_intents()
    {
        string path = Scratch();
        var launch = new Launch();
        typeof(Launch).GetField("_hdvDirectMutations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(launch, new Journal(path));
        const uint pid = 909;
        SeedIdentity(launch, pid, 292, 99100);
        launch.Hdv.SetOffer(pid, 2416, 777, [300, 3000]);

        Assert.False(await launch.HdvOpenBuyerAsync(pid, 191105026));
        Assert.False(await launch.HdvOpenSellerAsync(pid, 191105026));
        Assert.Empty(await launch.HdvGetBuyerLotPricesAsync(pid, 2416));
        Assert.False(await launch.HdvBuyAsync(pid, 777, 1, 300));
        Assert.False(await launch.HdvBuyByGidAsync(pid, 2416, 1, 300));
        Assert.False(await launch.HdvSellAsync(pid, 71, 1, 300));
        Assert.False(await launch.HdvSellByGidAsync(pid, 2416, 1, 300));
        Assert.False(await launch.HdvRepriceAsync(pid, 81, 1, 300));
        Assert.False(await launch.HdvRepriceByGidAsync(pid, 2416, 1, 300));
        Assert.False(await launch.HdvRemoveAsync(pid, 81, 1));
        Assert.False(File.Exists(path));
        Assert.Null(typeof(Launch).GetMethod("ClearSettledHdvBuy",
            BindingFlags.Instance | BindingFlags.NonPublic)); // no timer/fiscal auto-clear hook
    }

    [Fact]
    public void Late_seller_review_from_old_character_cannot_appear_or_be_cleared_on_reused_pid()
    {
        string path = Scratch();
        var launch = new Launch();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!
            .SetValue(launch, new Journal(path));
        const uint pid = 909;
        SeedIdentity(launch, pid, 292, 99100);
        var pending = (ConcurrentDictionary<uint, (int Uid, int Quantity, long Price)>)
            typeof(Launch).GetField("_hdvSellNeedsReview", flags)!.GetValue(launch)!;
        var owner = (ConcurrentDictionary<uint, string>)
            typeof(Launch).GetField("_hdvSellUncertainProfile", flags)!.GetValue(launch)!;
        pending[pid] = (111, 1, 300);
        owner[pid] = Character;
        Assert.Equal(Disposition.NeedsReview, launch.HdvSellLastDisposition(pid));

        SeedIdentity(launch, pid, 292, 99200); // PID reused before old late timeout is cleared
        Assert.Null(launch.HdvSellLastDisposition(pid));
        Assert.False(launch.HdvSellManuallyReconciled(pid));
        Assert.True(pending.ContainsKey(pid)); // old claim was not lost or reassigned

        SeedIdentity(launch, pid, 292, 99100);
        Assert.Equal(Disposition.NeedsReview, launch.HdvSellLastDisposition(pid));
        Assert.True(pending.ContainsKey(pid));
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
