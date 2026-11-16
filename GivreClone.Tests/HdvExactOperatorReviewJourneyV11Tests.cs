extern alias F06App;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

public sealed class HdvExactOperatorReviewJourneyV11Tests
{
    private const string Owner = "server:292:character:9710";
    private const string Other = "server:353:character:9710";

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "givre-v11-hdv-exact-" +
            Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "direct.journal");
        public Journal Open() => new(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Actual_inflight_sell_cannot_be_manually_reviewed_even_with_correct_run()
    {
        using var folder = new Fixture();
        using var sender = folder.Open();
        using var reviewer = folder.Open();
        string key = "sell:710:10:1000:50";
        Assert.Equal(Decision.Allowed, sender.TryBegin(Owner, 91, key, out var lease));
        Assert.True(sender.TryMarkAttempt(lease));
        Assert.False(reviewer.TryReviewExact(Owner, key, lease.RunId));
        Assert.True(sender.TryFinish(lease, confirmed: false));
        Assert.Equal(Phase.Uncertain, Assert.Single(reviewer.ReadSnapshot(Owner).Rows).Phase);
    }

    [Fact]
    public void Crash_recovery_must_match_exact_profile_order_run_and_current_session()
    {
        using var folder = new Fixture();
        var beforeCrash = folder.Open();
        string key = Journal.BuyOrder(700, 1934, 10, 1200, 17);
        Assert.Equal(Decision.Allowed, beforeCrash.TryBegin(Owner, 91, key, out var lease));
        Assert.True(beforeCrash.TryMarkAttempt(lease));
        beforeCrash.Dispose(); // no real socket/game, simulated kernel lease release
        using var restarted = folder.Open();
        bool current = true;
        Assert.False(restarted.TryReviewExact(Other, key, lease.RunId));
        Assert.False(restarted.TryReviewExact(Owner,
            Journal.BuyOrder(701, 1934, 10, 1200, 17), lease.RunId));
        Assert.False(restarted.TryReviewExact(Owner, key, Guid.NewGuid()));
        current = false;
        Assert.False(restarted.TryReviewExact(Owner, key, lease.RunId, () => current));
        Assert.Equal(Phase.Attempted, Assert.Single(restarted.ReadSnapshot(Owner).Rows).Phase);
        current = true;
        Assert.True(restarted.TryReviewExact(Owner, key, lease.RunId, () => current));
        var state = restarted.ReadSnapshot(Owner);
        Assert.Equal(ReadState.Ready, state.State);
        Assert.Equal(Phase.Reviewed, Assert.Single(state.Rows).Phase);
        Assert.False(state.HasPending);
        Assert.False(restarted.TryReviewExact(Owner, key, lease.RunId));
        Assert.Equal(Decision.NeedsReview, restarted.TryBegin(Owner, 91, key, out _));
    }

    [Fact]
    public void Identity_reentrancy_inside_exact_review_preserves_pending_bytes()
    {
        using var folder = new Fixture();
        var creator = folder.Open();
        string key = Journal.RemoveOrder(719, 1934, 10, 2000, 15);
        Assert.Equal(Decision.Allowed, creator.TryBegin(Owner, 91, key, out var lease));
        Assert.True(creator.TryFinish(lease, confirmed: false));
        creator.Dispose();
        using var reviewer = folder.Open();
        string original = File.ReadAllText(folder.Path);
        Assert.False(reviewer.TryReviewExact(Owner, key, lease.RunId,
            () => { /* simulate owner replacement during confirmation */ return false; }));
        Assert.Equal(original, File.ReadAllText(folder.Path));
        Assert.True(reviewer.ReadSnapshot(Owner).HasPending);
    }

    [Fact]
    public void Corrupt_anchor_never_promotes_operator_review_or_replays_uid()
    {
        using var folder = new Fixture();
        var original = folder.Open();
        string key = "sell:710:10:3000:50";
        Assert.Equal(Decision.Allowed, original.TryBegin(Owner, 91, key, out var lease));
        Assert.True(original.TryFinish(lease, confirmed: false));
        original.Dispose();
        File.AppendAllText(folder.Path, "garbled\n");
        using var reviewer = folder.Open();
        Assert.Equal(ReadState.StorageUnavailable, reviewer.ReadSnapshot(Owner).State);
        Assert.False(reviewer.TryReviewExact(Owner, key, lease.RunId));
        Assert.Equal(Decision.Unavailable,
            reviewer.TryBegin(Owner, 91, "sell:711:10:3000:50", out _));
    }

    [Fact]
    public async Task Concurrent_operator_confirmations_can_record_only_one_review_transition()
    {
        using var folder = new Fixture();
        var sender = folder.Open();
        string key = "reprice:710:10:1000";
        Assert.Equal(Decision.Allowed, sender.TryBegin(Owner, 91, key, out var lease));
        Assert.True(sender.TryMarkAttempt(lease));
        sender.Dispose();
        int[] results = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            {
                using var journal = folder.Open();
                return journal.TryReviewExact(Owner, key, lease.RunId) ? 1 : 0;
            })));
        Assert.Equal(1, results.Sum());
        using var verifier = folder.Open();
        Assert.Equal(Phase.Reviewed, Assert.Single(verifier.ReadSnapshot(Owner).Rows).Phase);
        Assert.False(verifier.NeedsReview(Owner));
        Assert.Equal(Decision.NeedsReview,
            verifier.TryBegin(Owner, 91, key, out _)); // no replay
    }

    [Fact]
    public void Actual_hdvpane_and_service_expose_selected_run_review_without_fabricated_ack()
    {
        Assert.NotNull(typeof(Launch).GetMethod("HdvReviewExactOperatorOrder",
            new[] { typeof(uint), typeof(string), typeof(string), typeof(Guid) }));
        Assert.NotNull(typeof(HdvVm).GetProperty("SelectedHdvJournalOperation"));
        Assert.NotNull(typeof(HdvVm).GetProperty("ReconcileSelectedDurableOrderCommand"));
        string root = Root();
        var view = File.ReadAllText(System.IO.Path.Combine(root,
            "GivreClone.App", "Views", "BotsView.axaml"));
        var vm = File.ReadAllText(System.IO.Path.Combine(root,
            "GivreClone.App", "ViewModels", "HdvViewModel.JournalV9.cs"));
        Assert.Contains("{Binding Hdv.SelectedHdvJournalOperation", view);
        Assert.Contains("{Binding Hdv.ReconcileSelectedDurableOrderCommand}", view);
        Assert.Contains("_launch.HdvReviewFromExactEvidenceV12(inspected)", vm);
        Assert.Contains("InspectSelectedHdvOrderEvidence", vm);
        Assert.Contains("selected.LocalRunId", vm);
        Assert.DoesNotContain("TryReview(profile,", vm);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(dir.FullName, "GivreClone.App")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("GivreClone workspace absent");
    }
}
