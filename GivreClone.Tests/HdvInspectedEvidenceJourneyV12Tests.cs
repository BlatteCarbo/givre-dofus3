extern alias F06App;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Evidence = F06App::GivreClone.App.Services.HdvOperatorEvidenceV12;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using Vm = F06App::GivreClone.App.ViewModels.HdvViewModel;

namespace GivreClone.Tests;

public sealed class HdvInspectedEvidenceJourneyV12Tests
{
    private const string Owner = "server:292:character:9710";

    private static Evidence Token(Guid run, string order,
        long epoch = 71, uint pid = 812, string profile = Owner,
        string observed = "observations locales exactes") =>
        new(pid, profile, order, run, epoch, Phase.Uncertain, observed, true);

    [Fact]
    public void Real_ui_review_policy_denies_replaced_source_run_uid_and_listing_snapshot()
    {
        var run = Guid.NewGuid();
        string order = Journal.BuyOrder(531, 1934, 10, 1200, 71);
        var initial = Token(run, order);
        Assert.True(Launch.IsSameExactHumanReviewEvidenceV12(initial, initial));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial, null));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { SourceEpoch = 72 })); // reconnect
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { Pid = 813 })); // PID changed
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { Profile = "server:292:character:9711" }));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { RunId = Guid.NewGuid() }));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { Order = Journal.BuyOrder(532, 1934, 10, 1200, 71) }));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { Observations = "l'annonce a changé" }));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { Phase = Phase.Reviewed }));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(initial,
            initial with { AttributedServerAck = true }));
    }

    [Fact]
    public void The_real_ledger_review_stays_pending_when_captured_epoch_is_revoked()
    {
        string path = Path.Combine(Path.GetTempPath(), "givre-v12-evidence-" +
            Guid.NewGuid().ToString("N"), "hdv.journal");
        string order = Journal.RemoveOrder(551, 1934, 10, 2200, 71);
        try
        {
            var creator = new Journal(path);
            Assert.Equal(Decision.Allowed, creator.TryBegin(
                Owner, 812, order, out var lease));
            Assert.True(creator.TryMarkAttempt(lease));
            Assert.True(creator.TryFinish(lease, confirmed: false));
            creator.Dispose();
            using var reviewer = new Journal(path);
            long currentEpoch = 71;
            long capturedEpoch = 71;
            // A late callback from old server/character/connection invalidates
            // the request before exact durable fsync; no prefix clear.
            currentEpoch = 72;
            Assert.False(reviewer.TryReviewExact(Owner, order, lease.RunId,
                () => currentEpoch == capturedEpoch));
            Assert.True(reviewer.ReadSnapshot(Owner).HasPending);
            currentEpoch = 71;
            Assert.True(reviewer.TryReviewExact(Owner, order, lease.RunId,
                () => currentEpoch == capturedEpoch));
            Assert.Equal(Phase.Reviewed,
                Assert.Single(reviewer.ReadSnapshot(Owner).Rows).Phase);
            Assert.Equal(Decision.NeedsReview,
                reviewer.TryBegin(Owner, 813, order, out _)); // no replay
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void V12_inspection_is_a_real_service_and_classic_HDV_command_before_review()
    {
        Assert.NotNull(typeof(Launch).GetMethod("HdvInspectExactOperatorEvidenceV12"));
        Assert.NotNull(typeof(Launch).GetMethod("HdvReviewFromExactEvidenceV12"));
        Assert.NotNull(typeof(Vm).GetProperty("InspectSelectedHdvOrderEvidenceCommand"));
        Assert.NotNull(typeof(Vm).GetProperty("HdvSelectedEvidenceStatus"));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "GivreClone.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string root = Path.Combine(dir!.FullName, "GivreClone.App");
        string vm = File.ReadAllText(Path.Combine(root, "ViewModels",
            "HdvViewModel.JournalV9.cs"));
        string service = File.ReadAllText(Path.Combine(root, "Services",
            "LaunchService.Hdv.OperatorEvidenceV12.cs"));
        string view = File.ReadAllText(Path.Combine(root, "Views", "BotsView.axaml"));
        Assert.Contains("_launch.HdvInspectExactOperatorEvidenceV12(ticket.Pid,", vm);
        Assert.Contains("_launch.HdvReviewFromExactEvidenceV12(inspected)", vm);
        Assert.Contains("IsSameExactHumanReviewEvidenceV12(evidence, live)", service);
        Assert.Contains("InspectSelectedHdvOrderEvidenceCommand", view);
        Assert.Contains("HdvSelectedEvidenceStatus", view);
        Assert.DoesNotContain("TryReview(profile,", vm);
    }
}
