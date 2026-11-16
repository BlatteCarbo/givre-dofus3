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
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;
using Outcome = F06App::GivreClone.App.Services.HdvProductionListingOutcomeV14;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Capture = F06App::GivreClone.App.Services.HdvProductionListingCaptureV14;
using Production = F06App::GivreClone.App.ViewModels.ItemCraftProductionLaunch;
using TaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;

namespace GivreClone.Tests;

/// <summary>
/// Genuine service capture, fsynced direct mutation journal and production
/// adapter entry. Fixture phases are LOCAL observations, never fake remote
/// transport ACKs or tax receipts. All effects are isolated temp files.
/// </summary>
public sealed class HdvCrossEntryJourneyV17Tests
{
    private const uint Pid = 811;
    private const string Profile = "server:292:character:99100";
    private const long Gid = 1934;
    private const int Uid = 713;
    private const int ListingUid = 911;
    private const string Order = "sell:713:10:1200:20";

    private static (Launch Service, Journal Ledger, Decoder Decoder, string Path) Setup()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "givre-hdv-v17-" + Guid.NewGuid().ToString("N"), "direct.journal");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var service = new Launch();
        var ledger = new Journal(path);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!.SetValue(service, ledger);
        var servers = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(service)!;
        var actors = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
        var sellers = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(service)!;
        var any = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvAnyOpen", flags)!.GetValue(service)!;
        servers[Pid] = (292, "local fixture");
        actors[Pid] = 99100;
        sellers[Pid] = true;
        any[Pid] = true;
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        // Genuine source-identity admission into the existing local relay
        // authority store; this does not forge any inbound server packet.
        object source = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
            .GetValue(service)!;
        var observe = source.GetType().GetMethod("ObserveFramedChunk")!;
        var linked = new Link(Guid.NewGuid(), Pid, 1, 1, Guid.NewGuid());
        Assert.True((bool)observe.Invoke(source, [linked, 1L, false, 32])!);
        service.Inventory.Seed(Pid, [new Bag.Item(Uid, Gid, 20)]);
        return (service, ledger, decoder, path);
    }

    private static void VisibleLocalListing(Launch service)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var links = (ConcurrentDictionary<(uint Pid, int BagUid), int>)
            typeof(Launch).GetField("_hdvSellLinks", flags)!.GetValue(service)!;
        links[(Pid, Uid)] = ListingUid;
        service.Hdv.UpsertMine(Pid, new Listings.Listing(ListingUid, Gid, 10, 1200));
    }

    [Fact]
    public async Task Original_production_started_attempt_and_locally_correlated_result_are_durable_by_run()
    {
        var (service, ledger, _, path) = Setup();
        using (ledger)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal(Order, capture.ExactOrder);
            await Task.Yield(); // same original async ListAsync logical flow
            Assert.Equal(Decision.Allowed,
                ledger.TryBegin(Profile, Pid, capture.ExactOrder, out var lease));
            var original = ledger.ReadProductionOriginalAttemptV17(
                Profile, Order, lease.RunId, capture.LocalCaptureId);
            Assert.Equal(ReadState.Ready, original.State);
            Assert.NotNull(original.ProductionOrigin);
            Assert.Equal(Gid, original.ProductionOrigin!.Gid);
            Assert.Equal(Uid, original.ProductionOrigin.BagUid);
            Assert.Equal(20, original.ProductionOrigin.ObservedStock);
            Assert.Equal(capture.SourceEpoch, original.ProductionOrigin.DecoderEpoch);
            Assert.Equal(capture.LinkedSource, original.ProductionOrigin.LinkedSource);
            Assert.Equal(Phase.Started, original.Row!.Phase);
            Assert.False(original.HasDurableAttempt);
            Assert.True(ledger.TryMarkAttempt(lease));
            Assert.True(ledger.TryFinish(lease, confirmed: true));
            VisibleLocalListing(service);
            var result = service.HdvAssessProductionListingV14(capture, true);
            Assert.Equal(Outcome.LocallyCorrelatedListing, result.Outcome);
            Assert.True(result.HasLocalSaleCorrelation);
            Assert.False(result.AttributedServerAck);
            Assert.False(result.MayReplayOrReleaseTax);
            var historic = new Journal(path);
            using (historic)
            {
                var persisted = historic.ReadProductionOriginalAttemptV17(
                    Profile, Order, lease.RunId, capture.LocalCaptureId);
                Assert.Equal(Phase.Confirmed, persisted.Row!.Phase);
                Assert.True(persisted.HasDurableAttempt);
                Assert.Equal(Gid, persisted.ProductionOrigin!.Gid);
                Assert.Equal(capture.LinkedSource, persisted.ProductionOrigin.LinkedSource);
                Assert.False(persisted.MayReleaseTax);
                Assert.False(persisted.IsServerAck);
                Assert.False(persisted.MaySendOrReplay);
            }
        }
    }

    [Fact]
    public void Crash_after_fsynced_attempt_retains_exact_production_identity_and_manual_review_only()
    {
        var (service, ledger, _, path) = Setup();
        Capture capture = Assert.IsType<Capture>(
            service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
        Assert.Equal(Decision.Allowed,
            ledger.TryBegin(Profile, Pid, Order, out var lease));
        Assert.True(ledger.TryMarkAttempt(lease));
        ledger.Dispose(); // simulates release of the OS lock after sender crash
        using var restarted = new Journal(path);
        var pending = restarted.ReadProductionOriginalAttemptV17(
            Profile, Order, lease.RunId, capture.LocalCaptureId);
        Assert.Equal(ReadState.Ready, pending.State);
        Assert.Equal(Phase.Attempted, pending.Row!.Phase);
        Assert.True(pending.HasDurableAttempt);
        Assert.True(pending.RequiresManualInspection);
        Assert.Equal(capture.LocalCaptureId, pending.ProductionOrigin!.CaptureId);
        Assert.Equal(Gid, pending.ProductionOrigin.Gid);
        Assert.False(pending.MaySendOrReplay);
        Assert.False(pending.MayAutoReconcile);
        Assert.False(pending.MayReleaseTax);
        Assert.True(restarted.NeedsReview(Profile));
        Assert.NotEqual(Decision.Allowed,
            restarted.TryBegin(Profile, Pid, "sell:714:10:1200:20", out _));
        var freshService = new Launch();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!
            .SetValue(freshService, restarted);
        var recovered = freshService.HdvInspectProductionNextManualReviewV17(
            0, Profile, Order, lease.RunId, capture.LocalCaptureId);
        Assert.Null(recovered.Current);
        Assert.Equal(Phase.Attempted, recovered.Original.Row!.Phase);
        Assert.Equal(Gid, recovered.Original.ProductionOrigin!.Gid);
        Assert.False(recovered.ExactRunAndSourceStillCurrent);
        Assert.False(recovered.MaySendOrReplay);
        Assert.False(recovered.MayReleaseTax);
        var guide = service.HdvInspectProductionNextManualReviewV17(
            Pid, Profile, Order, lease.RunId, capture.LocalCaptureId);
        // The old service's journal was disposed, so live advice must fail
        // closed; crash recovery must be performed from the fresh journal.
        Assert.False(guide.ExactRunAndSourceStillCurrent);
        Assert.Contains("Stop", guide.NextManualStep);
        Assert.True(restarted.TryReviewExact(Profile, Order, lease.RunId));
        Assert.Equal(Phase.Reviewed,
            restarted.ReadProductionOriginalAttemptV17(Profile, Order,
                lease.RunId, capture.LocalCaptureId).Row!.Phase);
        Assert.Equal(Decision.NeedsReview,
            restarted.TryBegin(Profile, Pid, Order, out _));
    }

    [Fact]
    public async Task Concurrent_ui_bulk_manual_exact_order_has_no_production_origin_or_positive_attribution()
    {
        var (service, ledger, _, _) = Setup();
        using (ledger)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Task<Guid> manual;
            using (ExecutionContext.SuppressFlow())
            {
                manual = Task.Run(() =>
                {
                    Assert.Equal(Decision.Allowed,
                        ledger.TryBegin(Profile, Pid, Order, out var lease));
                    Assert.True(ledger.TryMarkAttempt(lease));
                    Assert.True(ledger.TryFinish(lease, confirmed: true));
                    return lease.RunId;
                });
            }
            Guid manualRun = await manual;
            VisibleLocalListing(service);
            var durable = ledger.ReadProductionOriginalAttemptV17(Profile, Order,
                manualRun);
            Assert.Null(durable.ProductionOrigin);
            Assert.Equal(Phase.Confirmed, durable.Row!.Phase);
            Assert.False(durable.IsServerAck);
            var guide = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, manualRun);
            Assert.False(guide.ExactRunAndSourceStillCurrent);
            Assert.False(guide.MayReleaseTax);
            Assert.False(guide.IsIndividuallyAttributedServerReceipt);
            var result = service.HdvAssessProductionListingV14(capture, true);
            Assert.Equal(Outcome.CrossEntryAttemptNotAttributed, result.Outcome);
            Assert.False(result.HasLocalSaleCorrelation);
            Assert.False(result.MayReplayOrReleaseTax);
        }
    }

    [Fact]
    public void Exact_source_epoch_and_link_are_rechecked_before_read_only_manual_inspection()
    {
        var (service, ledger, decoder, _) = Setup();
        using (ledger)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal(Decision.Allowed,
                ledger.TryBegin(Profile, Pid, Order, out var lease));
            Assert.True(ledger.TryMarkAttempt(lease));
            Assert.True(ledger.TryFinish(lease, confirmed: false));
            var current = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, lease.RunId, capture.LocalCaptureId);
            Assert.True(current.ExactRunAndSourceStillCurrent);
            Assert.Contains("fresh V12", current.NextManualStep);
            decoder.Forget(Pid);
            decoder.Feed(Pid, false, Array.Empty<byte>());
            var reused = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, lease.RunId, capture.LocalCaptureId);
            Assert.False(reused.ExactRunAndSourceStillCurrent);
            Assert.Contains("current source is not individually matched",
                reused.NextManualStep);
            Assert.False(reused.MayAutoReconcile);
            Assert.False(reused.IsIndividuallyAttributedServerReceipt);
            Assert.Equal(Outcome.SourceChanged,
                service.HdvAssessProductionListingV14(capture, true).Outcome);
            Assert.True(ledger.NeedsReview(Profile));
        }
    }

    [Fact]
    public void Replaced_relay_link_with_same_decoder_epoch_revokes_original_attempt_source()
    {
        var (service, ledger, _, _) = Setup();
        using (ledger)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.NotNull(capture.LinkedSource);
            Assert.Equal(Decision.Allowed,
                ledger.TryBegin(Profile, Pid, Order, out var lease));
            Assert.True(ledger.TryMarkAttempt(lease));
            Assert.True(ledger.TryFinish(lease, confirmed: false));
            long epochBefore = service.CurrentLocalProductionDecoderEpoch(Pid);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object source = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
                .GetValue(service)!;
            var observe = source.GetType().GetMethod("ObserveFramedChunk")!;
            var next = new Link(Guid.NewGuid(), Pid, 2, 2, Guid.NewGuid());
            Assert.True((bool)observe.Invoke(source, [next, 1L, false, 32])!);
            Assert.Equal(epochBefore, service.CurrentLocalProductionDecoderEpoch(Pid));
            var inspection = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, lease.RunId, capture.LocalCaptureId);
            Assert.Equal(capture.LinkedSource,
                inspection.Original.ProductionOrigin!.LinkedSource);
            Assert.False(inspection.ExactRunAndSourceStillCurrent);
            Assert.False(inspection.MayReleaseTax);
            Assert.False(inspection.MayAutoReconcile);
            Assert.Equal(Outcome.SourceChanged,
                service.HdvAssessProductionListingV14(capture, true).Outcome);
            Assert.True(ledger.NeedsReview(Profile));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_v12_manual_review_clears_only_exact_seller_latch_under_shared_sender_gate(
        bool another_sender_owns_gate)
    {
        var (service, ledger, _, _) = Setup();
        using (ledger)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal(Decision.Allowed,
                ledger.TryBegin(Profile, Pid, Order, out var lease));
            Assert.True(ledger.TryMarkAttempt(lease));
            Assert.True(ledger.TryFinish(lease, confirmed: false));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var latches = (ConcurrentDictionary<uint, (int Uid, int Quantity, long Price)>)
                typeof(Launch).GetField("_hdvSellNeedsReview", flags)!.GetValue(service)!;
            var profiles = (ConcurrentDictionary<uint, string>)
                typeof(Launch).GetField("_hdvSellUncertainProfile", flags)!
                    .GetValue(service)!;
            var gates = (ConcurrentDictionary<uint, SemaphoreSlim>)
                typeof(Launch).GetField("_hdvSellLocks", flags)!.GetValue(service)!;
            latches[Pid] = (Uid, 10, 1200);
            profiles[Pid] = Profile;
            var ticket = service.HdvInspectExactOperatorEvidenceV12(
                Pid, Profile, Order, lease.RunId);
            Assert.NotNull(ticket);
            var gate = gates.GetOrAdd(Pid, static _ => new SemaphoreSlim(1, 1));
            if (another_sender_owns_gate) Assert.True(gate.Wait(0));
            try
            {
                Assert.True(service.HdvReviewFromExactEvidenceV12(ticket));
                Assert.False(service.HdvReviewFromExactEvidenceV12(ticket));
                Assert.Equal(Phase.Reviewed,
                    ledger.ReadProductionOriginalAttemptV17(Profile,
                        Order, lease.RunId, capture.LocalCaptureId).Row!.Phase);
                Assert.Equal(another_sender_owns_gate, latches.ContainsKey(Pid));
                Assert.Equal(another_sender_owns_gate, profiles.ContainsKey(Pid));
                Assert.Equal(Decision.NeedsReview,
                    ledger.TryBegin(Profile, Pid, Order, out _)); // exact replay still blocked
            }
            finally
            {
                if (another_sender_owns_gate) gate.Release();
            }
        }
    }

    [Fact]
    public void Tampered_production_origin_or_anchor_is_not_read_as_an_empty_or_replayable_ledger()
    {
        var (service, ledger, _, path) = Setup();
        Capture capture = Assert.IsType<Capture>(
            service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
        Assert.Equal(Decision.Allowed,
            ledger.TryBegin(Profile, Pid, Order, out var lease));
        ledger.Dispose();
        byte[] anchor = File.ReadAllBytes(path + ".anchor");
        string original = File.ReadAllText(path);
        Assert.Contains("production-v17:", original);
        File.WriteAllText(path, original.Replace("production-v17:", "production-v18:"));
        byte[] corrupted = File.ReadAllBytes(path);
        using var observer = new Journal(path);
        Assert.Equal(ReadState.StorageUnavailable,
            observer.ReadProductionOriginalAttemptV17(
                Profile, Order, lease.RunId, capture.LocalCaptureId).State);
        Assert.Equal(Decision.Unavailable,
            observer.TryBegin(Profile, Pid, "sell:714:10:1200:20", out _));
        Assert.False(observer.TryReviewExact(Profile, Order, lease.RunId));
        Assert.Equal(corrupted, File.ReadAllBytes(path));
        Assert.Equal(anchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public async Task Real_production_adapter_invalid_price_stops_before_any_send_or_tax_receipt()
    {
        var (service, ledger, _, path) = Setup();
        using (ledger)
        {
            var adapter = new Production(service, Pid, requireGuidedListingEvidence: true);
            Assert.False(await adapter.ListAsync(Gid, 10, 0, CancellationToken.None));
            Assert.False(File.Exists(path));
            Assert.Null(ledger.ReadProductionOriginalAttemptV17(Profile, Order).Row);
            Assert.True(TaxBudget.CanReserve(0, long.MaxValue,
                long.MaxValue, long.MaxValue)); // zero remains unlimited
        }
    }
}
