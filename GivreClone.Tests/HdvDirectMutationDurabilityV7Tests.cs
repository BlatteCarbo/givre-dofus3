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

namespace GivreClone.Tests;

/// <summary>
/// This is a local fsynced anti-replay intent, never a fabricated DOFUS ACK
/// or an additional tax reservation. No game processes or network are used.
/// </summary>
public sealed class HdvDirectMutationDurabilityV7Tests
{
    private const string Original = "server:292:character:99100";
    private const string OtherCharacter = "server:292:character:99200";
    private const string OtherServer = "server:353:character:99100";

    private static string PathForTest()
        => Path.Combine(Path.GetTempPath(), "givre-hdv-v7-" +
            Guid.NewGuid().ToString("N"), "direct.journal");

    [Fact]
    public void Pre_send_intent_blocks_same_and_different_orders_after_process_restart()
    {
        string path = PathForTest();
        var first = new Journal(path);
        Assert.Equal(Decision.Allowed, first.TryBegin(Original, 811,
            "reprice:100:10:4500", out var lease));
        Assert.NotEqual(Guid.Empty, lease.RunId);
        // Simulated crash: the kernel-held in-flight lock disappears, while
        // the durable Started and anchor remain on disk.
        first.Dispose();
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Original));
        Assert.Equal(Decision.NeedsReview,
            restarted.TryBegin(Original, 812, "reprice:101:10:4700", out _));
        Assert.Equal(Decision.NeedsReview,
            restarted.TryBegin(Original, 812, "reprice:100:10:4500", out _));
        Assert.False(restarted.TryFinish(lease, true)); // no fabricated receipt
        Assert.True(restarted.TryReview(Original, "reprice:"));
        Assert.False(restarted.NeedsReview(Original));
        Assert.Equal(Decision.NeedsReview,
            restarted.TryBegin(Original, 812, "reprice:100:10:4500", out _));
        Assert.Equal(Decision.Allowed,
            restarted.TryBegin(Original, 812, "reprice:102:10:4800", out _));
    }

    [Fact]
    public void Exact_confirmed_outcome_releases_profile_but_not_same_order_key()
    {
        string path = PathForTest();
        using (var first = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, first.TryBegin(Original, 7,
                "sell:81:10:5000:100", out var lease));
            Assert.True(first.TryFinish(lease, true));
            Assert.False(first.NeedsReview(Original));
            Assert.False(first.TryFinish(lease, true));
        }
        using var restarted = new Journal(path);
        Assert.False(restarted.NeedsReview(Original));
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Original, 7,
            "sell:81:10:5000:100", out _));
        Assert.Equal(Decision.Allowed, restarted.TryBegin(Original, 7,
            "sell:82:10:5000:100", out var next));
        Assert.True(restarted.TryFinish(next, false));
        Assert.True(restarted.NeedsReview(Original));
    }

    [Fact]
    public void Uncertain_sell_requires_specific_operator_review_not_reprice_review()
    {
        string path = PathForTest();
        using var journal = new Journal(path);
        Assert.Equal(Decision.Allowed,
            journal.TryBegin(Original, 44, "sell:77:1:300:7", out var lease));
        Assert.False(journal.TryReview(Original, "sell:")); // active sender
        Assert.True(journal.TryFinish(lease, false));
        Assert.True(journal.NeedsReview(Original));
        Assert.False(journal.TryReview(Original, "reprice:"));
        Assert.True(journal.TryReview(Original, "sell:"));
        Assert.False(journal.TryReview(Original, "sell:"));
        Assert.False(journal.NeedsReview(Original));
    }

    [Fact]
    public void Foreign_process_cannot_review_in_flight_order_even_with_file_access()
    {
        string path = PathForTest();
        using var active = new Journal(path);
        using var foreign = new Journal(path);
        Assert.Equal(Decision.Allowed, active.TryBegin(Original, 7,
            "reprice:10:1:500", out var lease));
        Assert.False(foreign.TryReview(Original, "reprice:"));
        Assert.NotEqual(Decision.Allowed, foreign.TryBegin(Original, 8,
            "reprice:11:1:600", out _));
        Assert.True(active.TryFinish(lease, false));
        Assert.True(foreign.TryReview(Original, "reprice:"));
    }

    [Fact]
    public void Same_character_other_pid_is_blocked_but_other_server_and_character_are_independent()
    {
        using var journal = new Journal(PathForTest());
        Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 811,
            "sell:30:10:1500:10", out var lease));
        Assert.NotEqual(Decision.Allowed, journal.TryBegin(Original, 812,
            "reprice:31:10:3000", out _));
        Assert.Equal(Decision.Allowed, journal.TryBegin(OtherCharacter, 811,
            "reprice:31:10:3000", out _));
        Assert.Equal(Decision.Allowed, journal.TryBegin(OtherServer, 813,
            "reprice:31:10:3000", out _));
        Assert.True(journal.TryFinish(lease, false));
    }

    [Fact]
    public void Corrupt_record_and_anchor_are_preserved_and_refuse_replay_and_review()
    {
        string path = PathForTest();
        using (var journal = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 7,
                "reprice:10:1:500", out var lease));
            Assert.True(journal.TryFinish(lease, false));
        }
        File.AppendAllText(path, "CORRUPTION");
        byte[] bytes = File.ReadAllBytes(path);
        using var reopened = new Journal(path);
        Assert.True(reopened.NeedsReview(Original));
        Assert.Equal(Decision.Unavailable, reopened.TryBegin(Original, 7,
            "reprice:11:1:550", out _));
        Assert.False(reopened.TryReview(Original, "reprice:"));
        Assert.Equal(bytes, File.ReadAllBytes(path)); // never truncate bad bytes
    }

    [Fact]
    public void Missing_anchor_with_existing_journal_fails_closed_without_overwriting_bytes()
    {
        string path = PathForTest();
        using (var journal = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 7,
                "reprice:100:1:500", out var lease));
            Assert.True(journal.TryFinish(lease, true));
        }
        byte[] intact = File.ReadAllBytes(path);
        File.Delete(path + ".anchor"); // only test-owned scratch file
        using var restarted = new Journal(path);
        Assert.True(restarted.NeedsReview(Original));
        Assert.Equal(Decision.Unavailable, restarted.TryBegin(Original, 7,
            "reprice:101:1:600", out _));
        Assert.Equal(intact, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Concurrent_process_like_journals_admit_at_most_one_same_profile()
    {
        string path = PathForTest();
        var results = new ConcurrentBag<Decision>();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            using var journal = new Journal(path);
            var decision = journal.TryBegin(Original, (uint)(100 + i),
                "sell:" + (100 + i) + ":1:500:1", out _);
            results.Add(decision);
        })));
        Assert.Equal(1, results.Count(x => x == Decision.Allowed));
        using var reopened = new Journal(path);
        Assert.True(reopened.NeedsReview(Original));
    }

    [Fact]
    public void Real_launch_entry_reports_durable_quarantine_after_forget_and_pid_reuse()
    {
        string path = PathForTest();
        using (var initial = new Journal(path))
        {
            Assert.Equal(Decision.Allowed, initial.TryBegin(Original, 811,
                "reprice:199:10:4500", out _));
        }
        var launch = new Launch();
        typeof(Launch).GetField("_hdvDirectMutations",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(launch, new Journal(path));
        SeedIdentity(launch, 811, 292, 99100);
        Assert.True(launch.HdvRepriceReviewPending(811));
        // V8 reports each pending mutation in its OWN classic UI section.
        // A reprice is still globally blocked for further mutations, but it
        // must not masquerade as an uncertain SELL in the operator panel.
        Assert.Null(launch.HdvSellLastDisposition(811));
        Assert.True(launch.HdvRepriceReviewPending(811));
        typeof(Launch).GetMethod("ForgetHdv",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(launch, [811u]);
        SeedIdentity(launch, 811, 292, 99200);
        Assert.False(launch.HdvRepriceReviewPending(811));
        SeedIdentity(launch, 812, 292, 99100);
        Assert.True(launch.HdvRepriceReviewPending(812));
        Assert.True(launch.HdvAcknowledgeRepriceReview(812));
        Assert.False(launch.HdvRepriceReviewPending(812));
        Assert.False(launch.HdvAcknowledgeRepriceReview(811));
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
