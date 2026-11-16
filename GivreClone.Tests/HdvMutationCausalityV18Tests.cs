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
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Bag = F06App::GivreClone.App.Services.InventoryStore;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;
using Capture = F06App::GivreClone.App.Services.HdvProductionListingCaptureV14;
using Outcome = F06App::GivreClone.App.Services.HdvProductionListingOutcomeV14;
using TaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;

namespace GivreClone.Tests;

/// <summary>
/// The real LaunchService bag/decoder/source capture and fsynced direct gate.
/// A fixture creates only local observations, never a correlated TCP receipt
/// or an authorization to clear an unrelated fiscal reservation.
/// </summary>
public sealed class HdvMutationCausalityV18Tests
{
    private const uint Pid = 811;
    private const string Profile = "server:292:character:99100";
    private const string Order = "sell:713:10:1200:20";
    private const long Gid = 1934;
    private const int Uid = 713;

    private static (Launch Service, Journal Journal, Decoder Decoder, string Path) Setup()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "givre-hdv-v18-" + Guid.NewGuid().ToString("N"), "direct.journal");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var service = new Launch();
        var journal = new Journal(path);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!.SetValue(service, journal);
        var servers = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(service)!;
        var actors = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
        var sellers = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(service)!;
        var any = (ConcurrentDictionary<uint, bool>)
            typeof(Launch).GetField("_hdvAnyOpen", flags)!.GetValue(service)!;
        servers[Pid] = (292, "fixture");
        actors[Pid] = 99100;
        sellers[Pid] = true;
        any[Pid] = true;
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        service.Inventory.Seed(Pid, [new Bag.Item(Uid, Gid, 20)]);
        object source = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
            .GetValue(service)!;
        var observe = source.GetType().GetMethod("ObserveFramedChunk")!;
        Assert.True((bool)observe.Invoke(source,
            [new Link(Guid.NewGuid(), Pid, 1, 1, Guid.NewGuid()), 1L, false, 32])!);
        return (service, journal, decoder, path);
    }

    private static void ChangeSource(Launch service)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object source = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
            .GetValue(service)!;
        var observe = source.GetType().GetMethod("ObserveFramedChunk")!;
        Assert.True((bool)observe.Invoke(source,
            [new Link(Guid.NewGuid(), Pid, 2, 2, Guid.NewGuid()), 1L, false, 32])!);
    }

    [Theory]
    [InlineData("stock")]
    [InlineData("newUid")]
    [InlineData("competingStack")]
    [InlineData("source")]
    [InlineData("decoder")]
    [InlineData("sellerClosed")]
    [InlineData("character")]
    public void Changed_real_production_preflight_denies_before_creating_a_durable_sender_witness(
        string mutation)
    {
        var (service, journal, decoder, path) = Setup();
        using (journal)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            switch (mutation)
            {
                case "stock":
                    service.Inventory.Upsert(Pid, new Bag.Item(Uid, Gid, 19));
                    break;
                case "newUid":
                    service.Inventory.Remove(Pid, Uid);
                    service.Inventory.Upsert(Pid, new Bag.Item(Uid + 1, Gid, 20));
                    break;
                case "competingStack":
                    service.Inventory.Upsert(Pid, new Bag.Item(Uid + 1, Gid, 20));
                    break;
                case "source":
                    ChangeSource(service);
                    break;
                case "decoder":
                    decoder.Forget(Pid);
                    decoder.Feed(Pid, false, Array.Empty<byte>());
                    break;
                case "sellerClosed":
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var sellers = (ConcurrentDictionary<uint, bool>)
                        typeof(Launch).GetField("_hdvOpen", flags)!.GetValue(service)!;
                    sellers[Pid] = false;
                    break;
                }
                case "character":
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var actors = (ConcurrentDictionary<uint, long>)
                        typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
                    actors[Pid] = 99200;
                    break;
                }
            }
            Assert.Equal(Decision.Unavailable,
                journal.TryBegin(Profile, Pid, capture.ExactOrder, out var lease));
            Assert.Equal(Guid.Empty, lease.RunId);
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".active-*"));
            var original = journal.ReadProductionOriginalAttemptV17(
                Profile, capture.ExactOrder, expectedCapture: capture.LocalCaptureId);
            Assert.Equal(ReadState.Ready, original.State);
            Assert.Null(original.Row);
            Assert.False(original.HasDurableAttempt);
            Assert.False(original.MaySendOrReplay);
            Assert.False(original.MayReleaseTax);
            Assert.Equal(Guid.Empty, journal.ReadProductionCallRunV16(
                capture.LocalCaptureId, capture.Pid, capture.Profile, capture.ExactOrder));
        }
    }

    [Fact]
    public void A_failed_callback_does_not_poison_the_virgin_journal_or_block_later_valid_intent()
    {
        var (service, journal, _, path) = Setup();
        using (journal)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            // The original host may throw while checking a stale provider.
            // The journal must fail closed without a synthetic STARTED phase.
            Assert.True(journal.ObserveProductionCallV17(capture.LocalCaptureId,
                Pid, Profile, Order, Gid, Uid, 10, 1200, 20,
                capture.SourceEpoch, capture.LinkedSource,
                () => throw new InvalidOperationException("source revoked")));
            Assert.Equal(Decision.Unavailable,
                journal.TryBegin(Profile, Pid, Order, out _));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".active-*"));

            // The original, unchanged inventory/session is still valid. A
            // fresh capture can start exactly one fsynced intent afterward.
            Capture second = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Profile, Pid, second.ExactOrder, out var lease));
            var original = journal.ReadProductionOriginalAttemptV17(
                Profile, second.ExactOrder, lease.RunId, second.LocalCaptureId);
            Assert.NotNull(original.ProductionOrigin);
            Assert.Equal(Gid, original.ProductionOrigin!.Gid);
            Assert.Equal(Phase.Started, original.Row!.Phase);
            Assert.False(original.HasDurableAttempt);
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, confirmed: false));
            Assert.True(journal.NeedsReview(Profile));
            Assert.False(original.MayReleaseTax);
            Assert.Equal(Decision.NeedsReview,
                journal.TryBegin(Profile, Pid, Order, out _));
        }
    }

    [Fact]
    public async Task Independent_manual_bulk_entry_with_identical_order_cannot_claim_original_production_capture()
    {
        var (service, journal, _, path) = Setup();
        using (journal)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Task<Guid> independent;
            using (ExecutionContext.SuppressFlow())
            {
                independent = Task.Run(() =>
                {
                    Assert.Equal(Decision.Allowed,
                        journal.TryBegin(Profile, Pid, Order, out var lease));
                    Assert.True(journal.TryMarkAttempt(lease));
                    Assert.True(journal.TryFinish(lease, confirmed: true));
                    return lease.RunId;
                });
            }
            Guid manualRun = await independent;
            using var restarted = new Journal(path);
            var durable = restarted.ReadProductionOriginalAttemptV17(
                Profile, Order, manualRun);
            Assert.Equal(Phase.Confirmed, durable.Row!.Phase);
            Assert.Null(durable.ProductionOrigin);
            Assert.True(durable.HasDurableAttempt);
            var inspection = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, manualRun);
            Assert.False(inspection.ExactRunAndSourceStillCurrent);
            Assert.False(inspection.IsIndividuallyAttributedServerReceipt);
            Assert.False(inspection.MayReleaseTax);
            Assert.False(inspection.MayAutoReconcile);
            Assert.Equal(Outcome.CrossEntryAttemptNotAttributed,
                service.HdvAssessProductionListingV14(capture, true).Outcome);
            Assert.Equal(Decision.NeedsReview,
                journal.TryBegin(Profile, Pid, Order, out _));
        }
    }

    [Fact]
    public void Source_and_fiscal_math_remain_read_only_without_synthetic_receipts_or_tax_clearance()
    {
        var (service, journal, _, _) = Setup();
        using (journal)
        {
            Capture capture = Assert.IsType<Capture>(
                service.HdvCaptureProductionListingV14(Pid, Gid, 10, 1200));
            Assert.Equal(Decision.Allowed,
                journal.TryBegin(Profile, Pid, Order, out var lease));
            Assert.True(journal.TryMarkAttempt(lease));
            Assert.True(journal.TryFinish(lease, confirmed: false));
            var guide = service.HdvInspectProductionNextManualReviewV17(
                Pid, Profile, Order, lease.RunId, capture.LocalCaptureId);
            Assert.Equal(Phase.Uncertain, guide.Original.Row!.Phase);
            Assert.True(guide.Original.HasDurableAttempt);
            Assert.True(guide.ExactRunAndSourceStillCurrent);
            Assert.False(guide.IsIndividuallyAttributedServerReceipt);
            Assert.False(guide.MayReleaseTax);
            Assert.False(guide.MaySendOrReplay);
            Assert.False(guide.MayAutoReconcile);
            Assert.Null(TaxBudget.EstimateUpperBound(long.MaxValue, float.NaN));
            Assert.Null(TaxBudget.EstimateUpperBound(long.MaxValue, float.PositiveInfinity));
            Assert.Null(TaxBudget.EstimateUpperBound(long.MaxValue, 101f));
            Assert.True(TaxBudget.CanReserve(0, long.MaxValue,
                long.MaxValue, long.MaxValue));
            Assert.False(TaxBudget.CanReserve(long.MaxValue,
                long.MaxValue, long.MaxValue, long.MaxValue));
        }
    }
}
