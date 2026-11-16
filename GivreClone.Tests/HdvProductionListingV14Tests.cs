extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using Outcome = F06App::GivreClone.App.Services.HdvProductionListingOutcomeV14;
using Capture = F06App::GivreClone.App.Services.HdvProductionListingCaptureV14;
using Bag = F06App::GivreClone.App.Services.InventoryStore;
using Hdv = F06App::GivreClone.App.Services.BidHouseStore;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Production = F06App::GivreClone.App.ViewModels.ItemCraftProductionLaunch;

namespace GivreClone.Tests;

/// <summary>
/// The real production port routes ListAsync through HdvScriptSellByGidAsync,
/// then through the SAME HdvSellAsync durable journal as UI/Lua/Bulk. These
/// tests exercise the new read-only service boundaries and actual fsynced
/// journal; controlled local store observations are NOT fake server ACKs.
/// No socket, game, automatic clearance or tax reservation is created here.
/// </summary>
public sealed class HdvProductionListingV14Tests
{
    private const string Owner = "server:292:character:99100";
    private const uint Pid = 811;
    private const long Gid = 1934;
    private const int BagUid = 713;
    private const int ListingUid = 911;
    private const int Lot = 10;
    private const long Price = 1200;

    private static string NewPath() => Path.Combine(Path.GetTempPath(),
        "givre-hdv-v14-" + Guid.NewGuid().ToString("N"), "direct.journal");

    private static (Launch Service, Journal Journal, Decoder Decoder) Setup(
        string path, int stock = 20)
    {
        // The service's read-only inspection deliberately treats a missing
        // parent root as unavailable (V13 fail-closed). This fixture models
        // an existing HDV data root without creating a journal or an intent.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var service = new Launch();
        var journal = new Journal(path);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!.SetValue(service, journal);
        var server = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(service)!;
        var character = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
        var seller = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(service)!;
        var any = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvAnyOpen", flags)!.GetValue(service)!;
        server[Pid] = (292, "fixture");
        character[Pid] = 99100;
        seller[Pid] = true;
        any[Pid] = true;
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        Assert.True(service.CurrentLocalProductionDecoderEpoch(Pid) > 0);
        service.Inventory.Seed(Pid, [new Bag.Item(BagUid, Gid, stock)]);
        return (service, journal, decoder);
    }

    private static Capture CaptureOne(Launch service)
        => Assert.IsType<Capture>(service.HdvCaptureProductionListingV14(
            Pid, Gid, Lot, Price));

    private static Guid PersistOrder(Journal journal, Capture ticket, bool confirmed,
        uint pid = Pid)
    {
        Assert.Equal(Decision.Allowed, journal.TryBegin(Owner, pid,
            ticket.ExactOrder, out var lease));
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.True(journal.TryFinish(lease, confirmed));
        return lease.RunId;
    }

    private static void SeedVisibleCorrelation(Launch launch, int listingUid = ListingUid,
        long gid = Gid, int qty = Lot, long price = Price)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var links = (ConcurrentDictionary<(uint Pid, int BagUid), int>)
            typeof(Launch).GetField("_hdvSellLinks", flags)!.GetValue(launch)!;
        links[(Pid, BagUid)] = listingUid;
        launch.Hdv.UpsertMine(Pid, new Hdv.Listing(listingUid, gid, qty, price));
    }

    [Fact]
    public void Real_journal_full_history_exact_lookup_is_not_limited_to_last_128_rows()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        string first = "sell:1000:1:150:1";
        Guid originalRun = Guid.Empty;
        for (int i = 0; i < 131; i++)
        {
            string order = $"sell:{1000 + i}:1:150:1";
            Assert.Equal(Decision.Allowed, journal.TryBegin(Owner, Pid,
                order, out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, true)); // fixture models local business proof
            if (i == 0) originalRun = lease.RunId;
        }
        Assert.DoesNotContain(journal.ReadSnapshot(Owner, 128).Rows, r => r.Order == first);
        var exact = journal.ReadExactV14(Owner, first, originalRun);
        Assert.Equal(ReadState.Ready, exact.State);
        Assert.Equal(Phase.Confirmed, exact.Row!.Phase);
        Assert.Equal(originalRun, exact.Row.LocalRunId);
        Assert.True(exact.HasDurableAttempt);
        Assert.True(exact.HasCorrelatedLocalResult);
        Assert.False(exact.IsServerAck);
        Assert.False(exact.CanAuthorizeReplay);
        Assert.Null(journal.ReadExactV14(Owner, first, Guid.NewGuid()).Row);
        Assert.Equal(Decision.NeedsReview, journal.TryBegin(Owner, Pid, first, out _));
    }

    [Fact]
    public void Actual_service_capture_fails_without_seller_or_with_ambiguous_stack()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, 0, Price));
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, 0));
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, 0, Lot, Price));
        launch.Inventory.Upsert(Pid, new Bag.Item(BagUid + 1, Gid, 15));
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, Price));
        launch.Inventory.Remove(Pid, BagUid + 1);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var seller = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(launch)!;
        seller[Pid] = false;
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, Price));
        seller[Pid] = true;
        Assert.Equal("sell:713:10:1200:20", CaptureOne(launch).ExactOrder);
        Assert.False(File.Exists(path)); // read-only capture does not create intent
        journal.Dispose();
    }

    [Fact]
    public void New_exact_journal_run_and_current_bag_to_listing_witness_correlate_locally_only()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            Guid run = PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            var result = launch.HdvAssessProductionListingV14(ticket,
                originalListAsyncReturnedTrue: true);
            Assert.Equal(Outcome.LocallyCorrelatedListing, result.Outcome);
            Assert.True(result.HasLocalSaleCorrelation);
            Assert.False(result.AttributedServerAck);
            Assert.False(result.MayReplayOrReleaseTax);
            Assert.Equal(run, result.ExactJournalOrder!.LocalRunId);
            Assert.Equal(Pid, result.ExactJournalOrder.OriginalPid);
            Assert.Equal(Phase.Confirmed, result.ExactJournalOrder.Phase);
            Assert.Equal(ListingUid, result.ListingUid);
            Assert.Equal(Outcome.Denied, launch.HdvAssessProductionListingV14(ticket, true).Outcome);
            Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, Price)); // exact key already recorded
            Assert.Equal(Decision.NeedsReview, journal.TryBegin(Owner, Pid,
                ticket.ExactOrder, out _));
        }
    }

    [Fact]
    public void Legacy_Started_to_Confirmed_without_an_Attempted_record_is_not_production_emission_proof()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            Assert.Equal(Decision.Allowed, journal.TryBegin(Owner, Pid,
                ticket.ExactOrder, out var lease));
            // Preserved for V7 journal compatibility, but V14 must not
            // promote this skipped emission boundary into production proof.
            Assert.True(journal.TryFinish(lease, true));
            SeedVisibleCorrelation(launch);
            var exact = journal.ReadExactV14(Owner, ticket.ExactOrder, lease.RunId);
            Assert.False(exact.HasDurableAttempt);
            Assert.False(exact.HasCorrelatedLocalResult);
            Assert.Equal(Outcome.UnverifiedAttempt,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
    }

    [Fact]
    public void False_original_production_return_never_becomes_success_from_a_manual_or_bulk_matching_order()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            var result = launch.HdvAssessProductionListingV14(ticket,
                originalListAsyncReturnedTrue: false);
            Assert.Equal(Outcome.OriginalCallNotConfirmed, result.Outcome);
            Assert.False(result.HasLocalSaleCorrelation);
            Assert.False(result.AttributedServerAck);
            Assert.Equal(Outcome.Denied, launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
    }

    [Theory]
    [InlineData(0, 1934, 10, 1200)]
    [InlineData(911, 1935, 10, 1200)]
    [InlineData(911, 1934, 1, 1200)]
    [InlineData(911, 1934, 10, 1201)]
    public void Missing_or_inexact_listing_uid_gid_quantity_price_never_certifies_local_sale(
        int uid, long gid, int qty, long price)
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true);
            if (uid != 0) SeedVisibleCorrelation(launch, uid, gid, qty, price);
            var result = launch.HdvAssessProductionListingV14(ticket, true);
            Assert.Equal(Outcome.ListingWitnessMissing, result.Outcome);
            Assert.False(result.AttributedServerAck);
            Assert.False(result.HasLocalSaleCorrelation);
        }
    }

    [Fact]
    public void Timeout_uncertain_order_is_never_reinterpreted_as_success_after_a_late_event()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            Guid run = PersistOrder(journal, ticket, confirmed: false);
            SeedVisibleCorrelation(launch);
            var result = launch.HdvAssessProductionListingV14(ticket, true);
            Assert.Equal(Outcome.PendingOrUncertain, result.Outcome);
            Assert.Equal(run, result.ExactJournalOrder!.LocalRunId);
            Assert.False(result.HasLocalSaleCorrelation);
            Assert.Equal(Outcome.Denied, launch.HdvAssessProductionListingV14(ticket, true).Outcome);
            Assert.True(journal.NeedsReview(Owner, "sell:"));
            Assert.True(journal.TryReviewExact(Owner, ticket.ExactOrder, run)); // human-only modeled
            Assert.Equal(Phase.Reviewed, journal.ReadExactV14(Owner,
                ticket.ExactOrder, run).Row!.Phase);
            Assert.Equal(Decision.NeedsReview, journal.TryBegin(Owner, Pid,
                ticket.ExactOrder, out _));
        }
    }

    [Fact]
    public void PID_reuse_or_decoder_reconnect_after_await_revokes_production_capture()
    {
        string path = NewPath();
        var (launch, journal, decoder) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            decoder.Forget(Pid);
            decoder.Feed(Pid, false, Array.Empty<byte>());
            Assert.NotEqual(ticket.SourceEpoch, launch.CurrentLocalProductionDecoderEpoch(Pid));
            Assert.Equal(Outcome.SourceChanged,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
    }

    [Fact]
    public void Same_PID_but_different_character_refuses_even_when_exact_order_and_listing_exist()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var character = (ConcurrentDictionary<uint, long>)
                typeof(Launch).GetField("_charId", flags)!.GetValue(launch)!;
            character[Pid] = 99200; // recycled PID, identical decoder epoch
            Assert.Equal(Outcome.SourceChanged,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
            Assert.Equal(Phase.Confirmed,
                journal.ReadExactV14(Owner, ticket.ExactOrder).Row!.Phase);
            Assert.False(launch.HdvAssessProductionListingV14(ticket, true).HasLocalSaleCorrelation);
        }
    }

    [Fact]
    public void Local_MITM_link_replaced_without_decoder_epoch_change_revokes_production_attribution()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object ledger = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
                .GetValue(launch)!;
            var observe = ledger.GetType().GetMethod("ObserveFramedChunk")!;
            var first = new Link(Guid.NewGuid(), Pid, 1, 1);
            var second = new Link(Guid.NewGuid(), Pid, 2, 1);
            Assert.True((bool)observe.Invoke(ledger, [first, 1L, false, 32])!);
            Capture ticket = CaptureOne(launch);
            Assert.True(ticket.LinkedSource.HasValue);
            Assert.Equal(first, ticket.LinkedSource.Value);
            PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            Assert.True((bool)observe.Invoke(ledger, [second, 1L, false, 32])!);
            Assert.Equal(ticket.SourceEpoch, launch.CurrentLocalProductionDecoderEpoch(Pid));
            Assert.Equal(Outcome.SourceChanged,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
    }

    [Fact]
    public void Forged_copy_or_expired_original_capture_cannot_claim_post_return_success()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true);
            SeedVisibleCorrelation(launch);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(ticket with
                {
                    CapturedAtTick = Environment.TickCount64
                }, true).Outcome); // record clone is not the issued object
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var issued = (ConcurrentDictionary<uint, Capture>)
                typeof(Launch).GetField("_hdvProductionListingCapturesV14", flags)!
                    .GetValue(launch)!;
            Capture expired = ticket with
            {
                CapturedAtTick = Environment.TickCount64 - 120_000
            };
            issued[Pid] = expired; // deterministic elapsed-time expiry fixture
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(expired, true).Outcome);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
    }

    [Fact]
    public async Task Late_async_journal_result_after_a_failed_return_does_not_reanimate_old_capture()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            // The awaited ListAsync returned false before the late transport
            // update; the assessment consumes the ticket immediately.
            Assert.Equal(Outcome.NoNewOrder,
                launch.HdvAssessProductionListingV14(ticket, false).Outcome);
            await Task.Run(() => PersistOrder(journal, ticket, confirmed: true));
            SeedVisibleCorrelation(launch);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
            Assert.False(launch.HdvAssessProductionListingV14(ticket, true).HasLocalSaleCorrelation);
        }
    }

    [Fact]
    public void Wrong_original_PID_and_corruption_fail_closed_before_listing_attribution()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture ticket = CaptureOne(launch);
            PersistOrder(journal, ticket, confirmed: true, pid: Pid + 1);
            SeedVisibleCorrelation(launch);
            Assert.Equal(Outcome.Denied,
                launch.HdvAssessProductionListingV14(ticket, true).Outcome);
        }
        // A separate workflow with a corrupted anchor cannot mint a new
        // capture, claim a missing order, or heal durable bytes.
        byte[] before = File.ReadAllBytes(path);
        File.AppendAllText(path + ".anchor", "corrupt\n");
        byte[] badAnchor = File.ReadAllBytes(path + ".anchor");
        Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, Price));
        using var observer = new Journal(path);
        Assert.Equal(ReadState.StorageUnavailable,
            observer.ReadExactV14(Owner, "sell:713:10:1200:20").State);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(badAnchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Second_observation_or_preexisting_order_blocks_reuse_without_sending_again()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            Capture first = CaptureOne(launch);
            Capture second = CaptureOne(launch);
            Assert.NotSame(first, second);
            Assert.Equal(Outcome.Denied, launch.HdvAssessProductionListingV14(first, true).Outcome);
            PersistOrder(journal, second, confirmed: true);
            SeedVisibleCorrelation(launch);
            Assert.True(launch.HdvAssessProductionListingV14(second, true).HasLocalSaleCorrelation);
            Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, Price));
        }
    }

    [Fact]
    public async Task Actual_ItemCraftProductionLaunch_ListAsync_enters_the_existing_script_path_without_RAW_send()
    {
        string path = NewPath();
        var (launch, journal, _) = Setup(path);
        using (journal)
        {
            var port = new Production(launch, Pid);
            // A zero-priced production listing is rejected by the existing
            // HdvScriptSellByGidAsync preflight before tax or wire mutation.
            Assert.False(await port.ListAsync(Gid, Lot, 0, CancellationToken.None));
            Assert.False(File.Exists(path));
            Assert.False(journal.NeedsReview(Owner));
            Assert.Null(launch.HdvCaptureProductionListingV14(Pid, Gid, Lot, 0));
        }
    }
}
