extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Bag = F06App::GivreClone.App.Services.InventoryStore;
using Listings = F06App::GivreClone.App.Services.BidHouseStore;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Outcome = F06App::GivreClone.App.Services.HdvProductionListingOutcomeV14;
using TxState = F06App::GivreClone.App.Services.HdvDirectTransactionStateV15;
using TaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;

namespace GivreClone.Tests;

/// <summary>
/// Real LaunchService capture/assessment and fsynced direct journal. The
/// server-local listing witness is modeled without manufacturing a TCP ACK.
/// The independent manual/Bulk task deliberately has a DIFFERENT execution
/// context from the production call that issued the capture.
/// </summary>
public sealed class HdvCrossEntryAuthorityV16Tests
{
    private const uint Pid = 811;
    private const string Profile = "server:292:character:99100";
    private const long Gid = 1934;
    private const int BagUid = 713;
    private const int ListingUid = 911;

    private static (Launch Service, Journal Journal, Decoder Decoder) Setup()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "givre-hdv-v16-" + Guid.NewGuid().ToString("N"), "direct.journal");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var service = new Launch();
        var journal = new Journal(path);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!.SetValue(service, journal);
        var servers = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(service)!;
        var actors = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
        var seller = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(service)!;
        var any = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvAnyOpen", flags)!.GetValue(service)!;
        servers[Pid] = (292, "fixture");
        actors[Pid] = 99100;
        seller[Pid] = true;
        any[Pid] = true;
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        service.Inventory.Seed(Pid, [new Bag.Item(BagUid, Gid, 20)]);
        return (service, journal, decoder);
    }

    private static void AddLocalListing(Launch service)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var links = (ConcurrentDictionary<(uint Pid, int BagUid), int>)
            typeof(Launch).GetField("_hdvSellLinks", flags)!.GetValue(service)!;
        links[(Pid, BagUid)] = ListingUid;
        service.Hdv.UpsertMine(Pid, new Listings.Listing(ListingUid, Gid, 10, 1200));
    }

    [Fact]
    public async Task Original_production_flow_is_bound_to_its_exact_fsynced_run_across_await()
    {
        var (launch, journal, _) = Setup();
        using (journal)
        {
            var capture = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            var before = launch.HdvInspectProductionCrossEntryV16(capture);
            Assert.True(before.CaptureStillIssued);
            Assert.False(before.ExactCallRunMatches);
            Assert.Equal(Guid.Empty, before.CapturedCallRun);
            Assert.Equal(TxState.Unseen, before.Transaction!.Transaction.State);

            await Task.Yield(); // the original ListAsync continues in its logical async flow
            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Profile, Pid, capture.ExactOrder, out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            var live = launch.HdvInspectProductionCrossEntryV16(capture);
            Assert.True(live.ExactCallRunMatches);
            Assert.Equal(lease.RunId, live.CapturedCallRun);
            Assert.Equal(TxState.Attempted, live.Transaction!.Transaction.State);
            Assert.False(live.MaySendOrReplay);
            Assert.False(live.MayReleaseTax);
            Assert.False(live.IsServerAck);
            Assert.False(live.MayAutoReview);

            Assert.True(journal.TryFinish(lease, confirmed: true));
            AddLocalListing(launch);
            var accepted = launch.HdvAssessProductionListingV14(capture, true);
            Assert.Equal(Outcome.LocallyCorrelatedListing, accepted.Outcome);
            Assert.Equal(lease.RunId, accepted.ExactJournalOrder!.LocalRunId);
            Assert.True(accepted.HasLocalSaleCorrelation);
            Assert.False(accepted.AttributedServerAck);
            Assert.False(accepted.MayReplayOrReleaseTax);
            Assert.Equal(Guid.Empty, journal.ReadProductionCallRunV16(
                capture.LocalCaptureId, Pid, Profile, capture.ExactOrder));
            Assert.False(launch.HdvInspectProductionCrossEntryV16(capture).CaptureStillIssued);
            Assert.False(launch.HdvAssessProductionListingV14(capture, true).HasLocalSaleCorrelation);
        }
    }

    [Fact]
    public async Task Independent_manual_or_bulk_sale_with_identical_uid_lot_price_cannot_be_claimed_by_production()
    {
        var (launch, journal, _) = Setup();
        using (journal)
        {
            var capture = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));

            // This is a genuinely independent UI/Bulk entry, not a child task
            // of the guided production call that owns the async origin token.
            Task<Guid> otherEntry;
            using (ExecutionContext.SuppressFlow())
            {
                otherEntry = Task.Run(() =>
                {
                    Assert.Equal(Decision.Allowed,
                        journal.TryBegin(Profile, Pid, capture.ExactOrder, out var lease));
                    Assert.True(journal.TryMarkAttempt(lease));
                    Assert.True(journal.TryFinish(lease, confirmed: true));
                    return lease.RunId;
                });
            }
            Guid manualRun = await otherEntry;
            AddLocalListing(launch);
            var readOnly = launch.HdvInspectProductionCrossEntryV16(capture);
            Assert.Equal(manualRun, readOnly.Transaction!.Transaction.Row!.LocalRunId);
            Assert.False(readOnly.ExactCallRunMatches);
            Assert.Equal(Guid.Empty, readOnly.CapturedCallRun);
            Assert.False(readOnly.MayReleaseTax);

            // Even a true return flag plus exact local bag->listing UID witness
            // cannot turn that OTHER sender into a production confirmation.
            var result = launch.HdvAssessProductionListingV14(capture, true);
            Assert.Equal(Outcome.CrossEntryAttemptNotAttributed, result.Outcome);
            Assert.Equal(manualRun, result.ExactJournalOrder!.LocalRunId);
            Assert.False(result.HasLocalSaleCorrelation);
            Assert.False(result.AttributedServerAck);
            Assert.Equal(Decision.NeedsReview,
                journal.TryBegin(Profile, Pid, capture.ExactOrder, out _));
        }
    }

    [Fact]
    public void Double_click_replacement_and_pid_reuse_cannot_reissue_captured_local_authority()
    {
        var (launch, journal, decoder) = Setup();
        using (journal)
        {
            var first = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            var second = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.False(launch.HdvInspectProductionCrossEntryV16(first).CaptureStillIssued);
            Assert.True(launch.HdvInspectProductionCrossEntryV16(second).CaptureStillIssued);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(first, true).Outcome);

            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Profile, Pid, second.ExactOrder, out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, confirmed: false));
            Assert.Equal(lease.RunId,
                launch.HdvInspectProductionCrossEntryV16(second).CapturedCallRun);
            decoder.Forget(Pid);
            decoder.Feed(Pid, false, Array.Empty<byte>());
            Assert.Equal(Outcome.SourceChanged,
                launch.HdvAssessProductionListingV14(second, true).Outcome);
            Assert.True(journal.NeedsReview(Profile));
            Assert.Equal(Decision.NeedsReview,
                journal.TryBegin(Profile, Pid, second.ExactOrder, out _));
            Assert.True(TaxBudget.CanReserve(0, long.MaxValue, long.MaxValue, long.MaxValue));
        }
    }

    [Fact]
    public void Changing_the_actual_bag_uid_or_stock_revokes_the_previous_production_selection()
    {
        var (launch, journal, _) = Setup();
        using (journal)
        {
            var old = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal("sell:713:10:1200:20", old.ExactOrder);
            launch.Inventory.Upsert(Pid, new Bag.Item(BagUid, Gid, 21));
            var stockChanged = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal("sell:713:10:1200:21", stockChanged.ExactOrder);
            Assert.False(launch.HdvInspectProductionCrossEntryV16(old).CaptureStillIssued);
            Assert.False(launch.HdvAssessProductionListingV14(old, true).HasLocalSaleCorrelation);

            launch.Inventory.Remove(Pid, BagUid);
            launch.Inventory.Upsert(Pid, new Bag.Item(BagUid + 1, Gid, 21));
            var uidChanged = Assert.IsType<F06App::GivreClone.App.Services.HdvProductionListingCaptureV14>(
                launch.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal("sell:714:10:1200:21", uidChanged.ExactOrder);
            Assert.False(launch.HdvInspectProductionCrossEntryV16(stockChanged).CaptureStillIssued);
            Assert.False(launch.HdvAssessProductionListingV14(stockChanged, true).HasLocalSaleCorrelation);
            Assert.Equal(TxState.Unseen,
                launch.HdvInspectProductionCrossEntryV16(uidChanged).Transaction!.Transaction.State);
        }
    }
}
