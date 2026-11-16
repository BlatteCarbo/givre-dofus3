extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using TxState = F06App::GivreClone.App.Services.HdvDirectTransactionStateV15;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using NextStep = F06App::GivreClone.App.Services.HdvProductionNextStepV15;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Inventory = F06App::GivreClone.App.Services.InventoryStore;
using Listings = F06App::GivreClone.App.Services.BidHouseStore;
using Capture = F06App::GivreClone.App.Services.HdvProductionListingCaptureV14;
using SaleOutcome = F06App::GivreClone.App.Services.HdvProductionListingOutcomeV14;
using ProductionPort = F06App::GivreClone.App.ViewModels.ItemCraftProductionLaunch;
using TaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;

namespace GivreClone.Tests;

/// <summary>
/// V15 exercises the actual durable journal and LaunchService consultation,
/// V11/V12 human review and the existing production adapter. No server ACK,
/// tax receipt or transport is mocked or inferred from a local inventory.
/// </summary>
public sealed class HdvProductionNextStepV15Tests
{
    private const uint Pid = 811;
    private const string Profile = "server:292:character:99100";
    private const string AnotherProfile = "server:292:character:99200";
    private const long Gid = 1934;
    private const int BagUid = 713;
    private const int ListingUid = 911;
    private const string Sale = "sell:713:10:1200:20";
    private const string AnotherSale = "sell:714:10:1200:20";

    private static string NewPath() => Path.Combine(Path.GetTempPath(),
        "givre-hdv-v15-" + Guid.NewGuid().ToString("N"), "direct.journal");

    private static (Launch Service, Journal Journal, Decoder Decoder) Service(string path)
    {
        // V13 deliberately rejects an absent journal *directory*, which is
        // different from an existing pristine root with no written orders.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var launch = new Launch();
        var journal = new Journal(path);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!.SetValue(launch, journal);
        var server = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(launch)!;
        var actor = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(launch)!;
        var seller = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(launch)!;
        var any = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvAnyOpen", flags)!.GetValue(launch)!;
        server[Pid] = (292, "fixture");
        actor[Pid] = 99100;
        seller[Pid] = true;
        any[Pid] = true;
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(launch, decoder);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        launch.Inventory.Seed(Pid, [new Inventory.Item(BagUid, Gid, 20)]);
        Assert.True(launch.CurrentLocalProductionDecoderEpoch(Pid) > 0);
        return (launch, journal, decoder);
    }

    private static F06App::GivreClone.App.Services.HdvDirectMutationLease Start(
        Journal journal, string order = Sale, string profile = Profile, uint pid = Pid)
    {
        Assert.Equal(Decision.Allowed, journal.TryBegin(profile, pid, order, out var lease));
        return lease;
    }

    private static void LocalListingWitness(Launch launch)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var map = (ConcurrentDictionary<(uint Pid, int BagUid), int>)
            typeof(Launch).GetField("_hdvSellLinks", flags)!.GetValue(launch)!;
        map[(Pid, BagUid)] = ListingUid;
        launch.Hdv.UpsertMine(Pid, new Listings.Listing(ListingUid, Gid, 10, 1200));
    }

    [Fact]
    public void Exact_intent_attempt_uncertain_and_review_have_distinct_transactional_next_steps()
    {
        string path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var journal = new Journal(path);
        var unseen = journal.ReadTransactionV15(Profile, Sale);
        Assert.Equal(TxState.Unseen, unseen.State);
        Assert.False(unseen.MayAuthorizeSend);
        Assert.False(unseen.MayReplay);
        Assert.False(unseen.IsServerAck);
        Assert.False(unseen.MayReleaseTax);

        var lease = Start(journal);
        var started = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.Started, started.State);
        Assert.True(started.RequiresOperatorInspection);
        Assert.False(started.HasDurableAttempt);
        Assert.Equal(Pid, started.Row!.OriginalPid);
        Assert.Equal(lease.RunId, started.Row.LocalRunId);
        Assert.Equal(TxState.RunMismatch,
            journal.ReadTransactionV15(Profile, Sale, Guid.NewGuid()).State);
        Assert.Null(journal.ReadTransactionV15(Profile, Sale, Guid.NewGuid()).Row);

        Assert.True(journal.TryMarkAttempt(lease));
        var attempted = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.Attempted, attempted.State);
        Assert.True(attempted.HasDurableAttempt);
        Assert.True(journal.TryFinish(lease, false));
        var uncertain = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.Uncertain, uncertain.State);
        Assert.True(uncertain.RequiresOperatorInspection);
        Assert.False(uncertain.HasLocallyConfirmedAttempt);
        Assert.False(journal.TryMarkAttempt(lease)); // no second send admission

        Assert.True(journal.TryReviewExact(Profile, Sale, lease.RunId));
        var reviewed = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.Reviewed, reviewed.State);
        Assert.False(reviewed.HasLocallyConfirmedAttempt);
        Assert.False(journal.TryReviewExact(Profile, Sale, lease.RunId));
        Assert.Equal(Decision.NeedsReview, journal.TryBegin(Profile, Pid, Sale, out _));
    }

    [Fact]
    public void Read_of_another_exact_order_sees_same_profile_pending_in_one_checksum_snapshot()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        var lease = Start(journal);
        var another = journal.ReadTransactionV15(Profile, AnotherSale);
        Assert.Equal(TxState.OtherOrderPending, another.State);
        Assert.True(another.HasOtherPendingOrder);
        Assert.False(another.OriginalOrderRecorded);
        Assert.False(another.MayAuthorizeSend);
        Assert.False(another.MayReleaseTax);
        Assert.NotEqual(Decision.Allowed,
            journal.TryBegin(Profile, Pid, AnotherSale, out _));
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.True(journal.TryFinish(lease, false));
        Assert.Equal(TxState.OtherOrderPending,
            journal.ReadTransactionV15(Profile, AnotherSale).State);
        Assert.Equal(TxState.Unseen,
            journal.ReadTransactionV15(AnotherProfile, AnotherSale).State);
    }

    [Fact]
    public void Legacy_direct_confirmed_without_real_attempt_cannot_be_a_production_verdict()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        var lease = Start(journal);
        Assert.True(journal.TryFinish(lease, true)); // old V7 transition only
        var status = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.LegacyConfirmedWithoutAttempt, status.State);
        Assert.Equal(Phase.Confirmed, status.Row!.Phase);
        Assert.False(status.HasDurableAttempt);
        Assert.False(status.HasLocallyConfirmedAttempt);
        Assert.False(status.IsServerAck);
    }

    [Fact]
    public void Previously_active_sender_and_truncated_empty_journal_are_not_a_fresh_start()
    {
        string path = NewPath();
        var journal = new Journal(path);
        var lease = Start(journal);
        Assert.True(journal.TryMarkAttempt(lease));
        journal.Dispose(); // kernel lock released, historical .active witness retained
        File.WriteAllText(path, "GIVRE-HDV-DIRECT-MUTATION-v1\n");
        File.WriteAllText(path + ".anchor", "GIVRE-HDV-DIRECT-ANCHOR-v1|0|GENESIS\n");
        byte[] bytes = File.ReadAllBytes(path);
        byte[] anchor = File.ReadAllBytes(path + ".anchor");
        using var restarted = new Journal(path);
        Assert.Equal(TxState.StorageUnavailable,
            restarted.ReadTransactionV15(Profile, Sale).State);
        Assert.True(restarted.NeedsReview(Profile));
        Assert.Equal(Decision.Unavailable,
            restarted.TryBegin(Profile, Pid, AnotherSale, out _));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(anchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Corrupt_anchor_fails_closed_for_consultation_and_does_not_fix_any_bytes()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        var lease = Start(journal);
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.True(journal.TryFinish(lease, false));
        File.AppendAllText(path + ".anchor", "tamper");
        byte[] bytes = File.ReadAllBytes(path);
        byte[] anchor = File.ReadAllBytes(path + ".anchor");
        var view = journal.ReadTransactionV15(Profile, Sale, lease.RunId);
        Assert.Equal(TxState.StorageUnavailable, view.State);
        Assert.False(view.RequiresOperatorInspection); // unavailable, not cleared
        Assert.False(journal.TryReviewExact(Profile, Sale, lease.RunId));
        Assert.False(view.MayAuthorizeSend);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(anchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Actual_service_returns_read_only_next_step_for_exact_run_and_never_grants_a_send()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            var before = launch.HdvInspectProductionNextStepV15(Pid, Profile, Sale);
            Assert.Equal(NextStep.NoRecordedLocalIntent, before.SuggestedReadOnlyNextStep);
            Assert.False(before.CanSendAnotherOrder);
            Assert.False(before.CanReleaseTax);
            Assert.False(before.HasServerAck);
            Assert.False(before.CanAutoReconcile);
            var lease = Start(journal);
            Assert.True(journal.TryMarkAttempt(lease));
            var attempted = launch.HdvInspectProductionNextStepV15(
                Pid, Profile, Sale, lease.RunId);
            Assert.Equal(TxState.Attempted, attempted.Transaction.State);
            Assert.Equal(NextStep.AwaitOrManuallyInspectExactRun,
                attempted.SuggestedReadOnlyNextStep);
            Assert.True(journal.TryFinish(lease, false));
            var uncertain = launch.HdvInspectProductionNextStepV15(
                Pid, Profile, Sale, lease.RunId);
            Assert.Equal(TxState.Uncertain, uncertain.Transaction.State);
            Assert.Equal(NextStep.ReviewRequiredForUncertainOutcome,
                uncertain.SuggestedReadOnlyNextStep);
            Assert.Equal(TxState.RunMismatch, launch.HdvInspectProductionNextStepV15(
                Pid, Profile, Sale, Guid.NewGuid()).Transaction.State);
            Assert.Equal(TxState.IdentityInvalid, launch.HdvInspectProductionNextStepV15(
                Pid, AnotherProfile, Sale).Transaction.State);
        }
    }

    [Fact]
    public void Human_V12_inspection_then_exact_review_never_issues_ACK_or_tax_receipt()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            var lease = Start(journal);
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, false));
            var preview = launch.HdvInspectExactOperatorEvidenceV12(
                Pid, Profile, Sale, lease.RunId);
            Assert.NotNull(preview);
            Assert.True(preview!.MayAskHumanToReview);
            Assert.False(preview.AttributedServerAck);
            Assert.False(launch.HdvReviewExactOperatorOrder(Pid, Profile,
                Sale, Guid.NewGuid()));
            Assert.True(launch.HdvReviewFromExactEvidenceV12(preview));
            Assert.False(launch.HdvReviewFromExactEvidenceV12(preview)); // double click
            var post = launch.HdvInspectProductionNextStepV15(
                Pid, Profile, Sale, lease.RunId);
            Assert.Equal(NextStep.AlreadyReviewedNotConfirmed, post.SuggestedReadOnlyNextStep);
            Assert.False(post.HasServerAck);
            Assert.False(post.CanReleaseTax);
            Assert.Equal(Phase.Reviewed, post.Transaction.Row!.Phase);
        }
    }

    [Fact]
    public void Original_return_false_then_late_matching_listing_does_not_advance_next_step()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            Capture ticket = Assert.IsType<Capture>(launch.HdvCaptureProductionListingV14(
                Pid, Gid, 10, 1200));
            Assert.Equal(SaleOutcome.NoNewOrder,
                launch.HdvAssessProductionListingV14(ticket, false).Outcome);
            var lease = Start(journal);
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, true)); // late local fixture phase
            LocalListingWitness(launch);
            Assert.False(launch.HdvAssessProductionListingV14(ticket, true).HasLocalSaleCorrelation);
            var next = launch.HdvInspectProductionNextStepV15(Pid, Profile, Sale);
            Assert.Equal(NextStep.LocalBusinessResultOnly, next.SuggestedReadOnlyNextStep);
            Assert.False(next.CanSendAnotherOrder);
            Assert.False(next.HasServerAck);
            Assert.False(next.CanReleaseTax);
        }
    }

    [Fact]
    public void Guided_real_production_port_with_invalid_price_stops_before_mutation_and_receipt()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            var observations = new ConcurrentBag<F06App::GivreClone.App.Services.HdvProductionListingAssessmentV14>();
            var port = new ProductionPort(launch, Pid,
                observations.Add, requireGuidedListingEvidence: true);
            // Zero is rejected at capture, so guided ListAsync does not even
            // enter the original Lua→HdvSellAsync sender or the fiscal broker.
            Assert.False(port.ListAsync(Gid, 10, 0, CancellationToken.None)
                .GetAwaiter().GetResult());
            Assert.Empty(observations);
            Assert.False(File.Exists(path));
            Assert.Equal(TxState.Unseen,
                journal.ReadTransactionV15(Profile, Sale).State);
        }
    }

    [Fact]
    public async Task Concurrent_read_only_consultations_never_complete_or_review_live_attempt()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            var lease = Start(journal);
            Assert.True(journal.TryMarkAttempt(lease));
            var checks = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                Task.Run(() => launch.HdvInspectProductionNextStepV15(
                    Pid, Profile, Sale, lease.RunId))));
            Assert.All(checks, check =>
            {
                Assert.Equal(TxState.Attempted, check.Transaction.State);
                Assert.False(check.CanSendAnotherOrder);
                Assert.False(check.HasServerAck);
                Assert.False(check.CanReleaseTax);
            });
            Assert.True(journal.NeedsReview(Profile));
            Assert.Equal(Phase.Attempted,
                journal.ReadTransactionV15(Profile, Sale).Row!.Phase);
            Assert.False(journal.TryReviewExact(Profile, Sale, lease.RunId)); // live sender lock
            Assert.True(journal.TryFinish(lease, false));
        }
    }

    [Fact]
    public void Historical_zero_tax_spend_cap_is_unlimited_but_does_not_grant_order_or_receipt_authority()
    {
        // Tax logic belongs to its unchanged, separately durable ledger.
        // A zero monetary cap must never become an artificial prohibition or
        // bypass the distinct direct-order identity/replay journal.
        Assert.True(TaxBudget.CanReserve(0, long.MaxValue,
            long.MaxValue, long.MaxValue));
        Assert.False(TaxBudget.CanReserve(-1, 0, 0, 1));
        string path = NewPath();
        // A virgin but inspectable root is distinct from a missing/unavailable
        // journal directory. Keep the strict V15 storage guard unchanged.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var journal = new Journal(path);
        Assert.Equal(TxState.Unseen, journal.ReadTransactionV15(Profile, Sale).State);
        var admitted = Start(journal);
        Assert.True(journal.TryMarkAttempt(admitted));
        Assert.True(journal.TryFinish(admitted, false));
        var view = journal.ReadTransactionV15(Profile, Sale, admitted.RunId);
        Assert.Equal(TxState.Uncertain, view.State);
        Assert.False(view.MayReleaseTax);
        Assert.False(view.MayAuthorizeSend);
        Assert.False(view.IsServerAck);
        Assert.Equal(Decision.NeedsReview,
            journal.TryBegin(Profile, Pid, Sale, out _));
    }

    [Fact]
    public void Missing_journal_parent_is_not_misreported_as_an_empty_authoritative_history()
    {
        string path = NewPath(); // Unique root intentionally NOT created.
        using var journal = new Journal(path);
        var observation = journal.ReadTransactionV15(Profile, Sale);
        Assert.Equal(TxState.StorageUnavailable, observation.State);
        Assert.False(observation.MayAuthorizeSend);
        Assert.False(observation.MayReleaseTax);
        Assert.False(observation.MayReplay);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Existing_manual_Lua_and_production_sale_entries_reject_bad_price_without_a_local_send()
    {
        string path = NewPath();
        var (launch, journal, _) = Service(path);
        using (journal)
        {
            // Manual and Bulk converge on HdvSellAsync; the Lua/prod GID
            // entry converges via HdvScriptSellByGidAsync. These calls are
            // intentionally rejected at their REAL service preflight, with
            // no fake transport, result packet or fee receipt in the test.
            Assert.False(await launch.HdvSellAsync(Pid, BagUid, 10, 0));
            Assert.False(await launch.HdvSellByGidAsync(Pid, Gid, 10, 0));
            Assert.False(await launch.HdvScriptSellByGidAsync(Pid, Gid, 10, 0));
            Assert.False(File.Exists(path));
            var consultation = launch.HdvInspectProductionNextStepV15(Pid, Profile, Sale);
            Assert.Equal(TxState.Unseen, consultation.Transaction.State);
            Assert.False(consultation.CanSendAnotherOrder);
        }
    }
}
