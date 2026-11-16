extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Evidence = F06App::GivreClone.App.Services.HdvOperatorEvidenceV12;
using Journal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decision = F06App::GivreClone.App.Services.HdvDirectMutationDecision;
using Phase = F06App::GivreClone.App.Services.HdvDirectMutationPhase;
using ReadState = F06App::GivreClone.App.Services.HdvDirectMutationReadState;

namespace GivreClone.Tests;

/// <summary>
/// Exercises the actual LaunchService V11/V12 inspection/review entrypoints
/// and the durable admission journal without a game, socket, ACK or tax ledger.
/// An inspection is an operator decision aid, never server receipt evidence.
/// </summary>
public sealed class HdvEvidenceAdmissionV13Tests
{
    private const string Original = "server:292:character:99100";
    private const string Other = "server:292:character:99200";

    private static string NewPath() => Path.Combine(Path.GetTempPath(),
        "givre-hdv-v13-" + Guid.NewGuid().ToString("N"), "direct.journal");

    private static (Launch Service, Decoder Decoder) SeedService(
        string path, uint pid = 811, long character = 99100)
    {
        var service = new Launch();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_hdvDirectMutations", flags)!
            .SetValue(service, new Journal(path));
        var server = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", flags)!.GetValue(service)!;
        var actor = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", flags)!.GetValue(service)!;
        server[pid] = (292, "fixture");
        actor[pid] = character;
        // Mint only a LOCAL decoder epoch. No packet / transport / server
        // action is fabricated by an empty decoder feed.
        var decoder = new Decoder(null!);
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        decoder.Feed(pid, false, Array.Empty<byte>());
        Assert.True(service.CurrentLocalProductionDecoderEpoch(pid) > 0);
        return (service, decoder);
    }

    private static (Journal Journal, string Order, Guid Run) PendingBuy(
        string path, uint pid = 811, long epoch = 71)
    {
        var journal = new Journal(path);
        string order = Journal.BuyOrder(551, 1934, 10, 1200, epoch);
        Assert.Equal(Decision.Allowed, journal.TryBegin(Original, pid, order, out var lease));
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.True(journal.TryFinish(lease, confirmed: false));
        return (journal, order, lease.RunId);
    }

    [Fact]
    public void Actual_V12_inspection_then_operator_review_is_one_shot_and_never_acks_purchase()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        string order = Assert.Single(sender.ReadSnapshot(Original).Rows).Order;
        Guid run = Assert.Single(sender.ReadSnapshot(Original).Rows).LocalRunId;
        var (service, _) = SeedService(path);
        Evidence? inspected = service.HdvInspectExactOperatorEvidenceV12(811, Original, order, run);
        Assert.NotNull(inspected);
        Assert.True(inspected!.MayAskHumanToReview);
        Assert.False(inspected.AttributedServerAck);
        Assert.True(inspected.InspectedAtTick > 0);
        Assert.NotEqual(Guid.Empty, inspected.InspectionHost);
        Assert.Contains("Aucune réponse serveur attribuable", inspected.Observations);
        Assert.True(service.HdvReviewFromExactEvidenceV12(inspected));
        Assert.False(service.HdvReviewFromExactEvidenceV12(inspected)); // double click
        Assert.Equal(Phase.Reviewed, Assert.Single(sender.ReadSnapshot(Original).Rows).Phase);
        Assert.False(sender.NeedsReview(Original));
        Assert.Equal(Decision.NeedsReview, sender.TryBegin(Original, 812, order, out _));
    }

    [Fact]
    public void Actual_review_refuses_stale_future_or_unissued_inspection_without_touching_journal()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, _) = SeedService(path);
        Evidence evidence = Assert.IsType<Evidence>(
            service.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        byte[] original = File.ReadAllBytes(path);
        byte[] anchor = File.ReadAllBytes(path + ".anchor");

        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { InspectedAtTick = Environment.TickCount64 - 60_000 }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { InspectedAtTick = Environment.TickCount64 + 60_000 }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { InspectedAtTick = 0 }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { InspectedAtTick = Environment.TickCount64 })); // forged refresh
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { InspectionHost = Guid.Empty }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { AttributedServerAck = true }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { RunId = Guid.NewGuid() }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { Profile = Other }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { Order = Journal.BuyOrder(552, 1934, 10, 1200, 71) }));
        Assert.False(service.HdvReviewFromExactEvidenceV12(
            evidence with { LinkedSource = new Link(Guid.NewGuid(), 811, 2, 1) }));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(anchor, File.ReadAllBytes(path + ".anchor"));
        Assert.True(sender.NeedsReview(Original, "buy:"));
    }

    [Fact]
    public void Second_inspection_invalidates_the_first_even_when_journal_and_source_are_identical()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, _) = SeedService(path);
        Evidence first = Assert.IsType<Evidence>(service.HdvInspectExactOperatorEvidenceV12(
            811, Original, row.Order, row.LocalRunId));
        Evidence second = Assert.IsType<Evidence>(service.HdvInspectExactOperatorEvidenceV12(
            811, Original, row.Order, row.LocalRunId));
        Assert.NotSame(first, second);
        Assert.False(service.HdvReviewFromExactEvidenceV12(first));
        Assert.True(sender.NeedsReview(Original));
        Assert.True(service.HdvReviewFromExactEvidenceV12(second));
    }

    [Fact]
    public void Reconnected_decoder_and_reused_pid_with_same_profile_cannot_reuse_old_selection()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, decoder) = SeedService(path);
        Evidence before = Assert.IsType<Evidence>(
            service.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        decoder.Forget(811);
        Assert.False(service.HdvReviewFromExactEvidenceV12(before));
        decoder.Feed(811, false, Array.Empty<byte>());
        Assert.NotEqual(before.SourceEpoch, service.CurrentLocalProductionDecoderEpoch(811));
        Assert.False(service.HdvReviewFromExactEvidenceV12(before));
        Assert.True(sender.NeedsReview(Original));

        Evidence after = Assert.IsType<Evidence>(
            service.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        Assert.False(Launch.IsSameExactHumanReviewEvidenceV12(before, after));
        Assert.True(service.HdvReviewFromExactEvidenceV12(after));
    }

    [Fact]
    public void Another_LaunchService_with_identical_pid_character_and_local_epoch_cannot_replay_token()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (first, _) = SeedService(path);
        Evidence oldToken = Assert.IsType<Evidence>(
            first.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        var (restarted, _) = SeedService(path); // independently minted host, same PID and epoch
        Assert.Equal(oldToken.SourceEpoch, restarted.CurrentLocalProductionDecoderEpoch(811));
        Assert.False(restarted.HdvReviewFromExactEvidenceV12(oldToken));
        Assert.True(sender.NeedsReview(Original));
        Evidence newlyInspected = Assert.IsType<Evidence>(
            restarted.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        Assert.NotEqual(oldToken.InspectionHost, newlyInspected.InspectionHost);
        Assert.True(restarted.HdvReviewFromExactEvidenceV12(newlyInspected));
        Assert.False(restarted.HdvReviewFromExactEvidenceV12(newlyInspected));
    }

    [Fact]
    public void Exact_local_source_replacement_denies_stale_inspection_even_if_decoder_epoch_did_not_change()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, _) = SeedService(path);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object ledger = typeof(Launch).GetField("_localMitmSourceAuthority", flags)!
            .GetValue(service)!;
        var observe = ledger.GetType().GetMethod("ObserveFramedChunk")!;
        var oldLink = new Link(Guid.NewGuid(), 811, 1, 1);
        var newLink = new Link(Guid.NewGuid(), 811, 2, 1);
        Assert.True((bool)observe.Invoke(ledger, [oldLink, 1L, false, 32])!);
        Evidence old = Assert.IsType<Evidence>(service.HdvInspectExactOperatorEvidenceV12(
            811, Original, row.Order, row.LocalRunId));
        long originalEpoch = service.CurrentLocalProductionDecoderEpoch(811);
        Assert.True(old.LinkedSource.HasValue);
        Assert.Equal(oldLink, old.LinkedSource.Value);

        // Model the producer-reported source replacement in the actual local
        // authority ledger. Neither observation is an HDV receipt.
        Assert.True((bool)observe.Invoke(ledger, [newLink, 1L, false, 32])!);
        Assert.Equal(originalEpoch, service.CurrentLocalProductionDecoderEpoch(811));
        Assert.False(service.HdvReviewFromExactEvidenceV12(old));
        Assert.True(sender.NeedsReview(Original));
        Evidence fresh = Assert.IsType<Evidence>(service.HdvInspectExactOperatorEvidenceV12(
            811, Original, row.Order, row.LocalRunId));
        Assert.True(fresh.LinkedSource.HasValue);
        Assert.Equal(newLink, fresh.LinkedSource.Value);
        Assert.True(service.HdvReviewFromExactEvidenceV12(fresh));
    }

    [Fact]
    public void Legacy_V11_review_entrypoint_uses_the_same_fresh_service_inspection()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, decoder) = SeedService(path);
        decoder.Forget(811); // no source means legacy may NOT clear an order
        Assert.False(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, row.LocalRunId));
        Assert.True(sender.NeedsReview(Original));
        decoder.Feed(811, false, Array.Empty<byte>());
        Assert.False(service.HdvReviewExactOperatorOrder(811, Other,
            row.Order, row.LocalRunId));
        Assert.False(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, Guid.NewGuid()));
        Assert.False(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, row.LocalRunId)); // no prior explicit inspection
        Assert.NotNull(service.HdvInspectExactOperatorEvidenceV12(811, Original,
            row.Order, row.LocalRunId));
        Assert.True(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, row.LocalRunId));
        Assert.False(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, row.LocalRunId));
        Assert.Equal(Phase.Reviewed, Assert.Single(sender.ReadSnapshot(Original).Rows).Phase);
    }

    [Fact]
    public void Deleted_journal_and_anchor_with_previous_sender_witness_do_not_bootstrap_or_replay()
    {
        string path = NewPath();
        using (var sender = PendingBuy(path).Journal)
            Assert.True(sender.NeedsReview(Original));
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!,
            Path.GetFileName(path) + ".active-*"));
        File.Delete(path);
        File.Delete(path + ".anchor");
        using var after = new Journal(path);
        Assert.Equal(ReadState.StorageUnavailable, after.ReadSnapshot(Original).State);
        Assert.True(after.NeedsReview(Original));
        Assert.True(after.NeedsReview(Other)); // shared journal, not just old profile
        Assert.Equal(Decision.Unavailable, after.TryBegin(Other, 812,
            Journal.BuyOrder(777, 1934, 1, 150, 71), out _));
        Assert.False(File.Exists(path)); // no automatic reconstruction of lost evidence
    }

    [Fact]
    public void Premature_manual_review_of_a_virgin_journal_does_not_poison_its_first_real_intent()
    {
        string path = NewPath();
        string order = Journal.BuyOrder(711, 1934, 1, 100, 71);
        using var journal = new Journal(path);
        Assert.False(journal.TryReview(Original, "buy:"));
        Assert.False(journal.TryReviewExact(Original, order, Guid.NewGuid()));
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)!));
        Assert.Equal(Decision.Allowed, journal.TryBegin(Original, 811,
            order, out var lease));
        Assert.True(journal.TryMarkAttempt(lease));
        Assert.True(journal.TryFinish(lease, false));
        Assert.True(journal.NeedsReview(Original, "buy:"));
    }

    [Fact]
    public async Task Concurrent_exact_operator_double_click_commits_one_review_only()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, _) = SeedService(path);
        Evidence selection = Assert.IsType<Evidence>(
            service.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        bool[] outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            Task.Run(() => service.HdvReviewFromExactEvidenceV12(selection))));
        Assert.Single(outcomes.Where(success => success));
        Assert.Equal(Phase.Reviewed, Assert.Single(sender.ReadSnapshot(Original).Rows).Phase);
    }

    [Fact]
    public void Corrupt_anchor_denies_real_service_inspection_and_cannot_release_tax_or_mark_ACK()
    {
        string path = NewPath();
        using var sender = PendingBuy(path).Journal;
        var row = Assert.Single(sender.ReadSnapshot(Original).Rows);
        var (service, _) = SeedService(path);
        Evidence selection = Assert.IsType<Evidence>(
            service.HdvInspectExactOperatorEvidenceV12(811, Original,
                row.Order, row.LocalRunId));
        File.AppendAllText(path + ".anchor", "tampered\n");
        byte[] corruptAnchor = File.ReadAllBytes(path + ".anchor");
        Assert.Null(service.HdvInspectExactOperatorEvidenceV12(811, Original,
            row.Order, row.LocalRunId));
        Assert.False(service.HdvReviewFromExactEvidenceV12(selection));
        Assert.False(service.HdvReviewExactOperatorOrder(811, Original,
            row.Order, row.LocalRunId));
        Assert.Equal(ReadState.StorageUnavailable, sender.ReadSnapshot(Original).State);
        Assert.Equal(corruptAnchor, File.ReadAllBytes(path + ".anchor"));
    }

    [Fact]
    public void Nonfinite_overflow_negative_or_zero_local_prices_fail_closed_before_journal_intent()
    {
        string path = NewPath();
        using var journal = new Journal(path);
        string[] invalid =
        [
            "sell:11:1:NaN:2", "sell:11:1:Infinity:2",
            "sell:11:1:9223372036854775808:2", "sell:11:1:0:2",
            "sell:11:1:-1:2", "sell:11:2:300:1",
            "reprice:11:10:NaN", "reprice:11:10:9223372036854775808",
            "reprice:11:10:0", "reprice:11:10:-5",
            "buy:11:1934:10:NaN:stock-unknown:epoch-71",
            "buy:11:1934:10:9223372036854775808:stock-unknown:epoch-71",
            "remove:11:1934:10:-2:stock-10:epoch-71"
        ];
        foreach (var order in invalid)
            Assert.Equal(Decision.Unavailable, journal.TryBegin(Original, 811, order, out _));
        Assert.False(File.Exists(path));
    }
}
