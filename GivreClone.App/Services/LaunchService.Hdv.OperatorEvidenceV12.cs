using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>
/// Immutable, read-only local evidence for exactly ONE selected fsynced order
/// and the CURRENT decoder source. It is not a network receipt or a clearance
/// of tax reservations. If the same PID reconnects, the preview expires.
/// </summary>
public sealed record HdvOperatorEvidenceV12(
    uint Pid, string Profile, string Order, Guid RunId, long SourceEpoch,
    HdvDirectMutationPhase Phase, string Observations,
    bool MayAskHumanToReview, bool AttributedServerAck = false,
    long InspectedAtTick = 0, Guid InspectionHost = default,
    CaptureLinkIdentity? LinkedSource = null);

/// <summary>
/// PRE-call observation only. The production GID belongs to this local
/// inventory snapshot, NOT to the original durable SELL order (which stores
/// bag UID, lot, price and observed stock, but no GID or production caller).
/// It is not a new mutation lease, ACK, tax credit or replay authority.
/// </summary>
public sealed record HdvProductionListingCaptureV14(
    uint Pid, string Profile, long Gid, int BagUid, int Quantity,
    long Price, int ObservedStock, string ExactOrder, long SourceEpoch,
    CaptureLinkIdentity? LinkedSource, long CapturedAtTick,
    Guid InspectionHost, Guid LocalCaptureId);

public enum HdvProductionListingOutcomeV14
{
    Denied, SourceChanged, JournalUnavailable, NoNewOrder,
    PendingOrUncertain, ManuallyReviewed, UnverifiedAttempt,
    OriginalCallNotConfirmed,
    ListingWitnessMissing, LocallyCorrelatedListing,
    CrossEntryAttemptNotAttributed
}

/// <summary>
/// Positives require BOTH the caller's result from its original awaited
/// HdvScriptSellByGidAsync call and the independent exact durable journal /
/// current listing witness. The service cannot authenticate an arbitrary
/// bool passed by a different caller. This is NOT a server-issued ACK.
/// </summary>
public sealed record HdvProductionListingAssessmentV14(
    HdvProductionListingOutcomeV14 Outcome,
    HdvDirectMutationHistoryRow? ExactJournalOrder,
    int? ListingUid, string Reason)
{
    public bool HasLocalSaleCorrelation =>
        Outcome == HdvProductionListingOutcomeV14.LocallyCorrelatedListing;
    public bool AttributedServerAck => false;
    public bool HasIndividuallyAttributableServerReceipt => false;
    public bool MayReplayOrReleaseTax => false;
}

/// <summary>
/// Advisory NEXT STEP for one exact HDV transaction. No value here can admit
/// a new send, turn human inspection into ACK, or release fiscal reservations.
/// </summary>
public enum HdvProductionNextStepV15
{
    StopForStorageOrIdentity, NoRecordedLocalIntent,
    OtherTransactionInFlight, AwaitOrManuallyInspectExactRun,
    ReviewRequiredForUncertainOutcome, AlreadyReviewedNotConfirmed,
    LocalBusinessResultOnly, LegacyAttemptNotProven
}

public sealed record HdvProductionTransactionInspectionV15(
    uint Pid, string Profile, string Order, long DecoderEpoch,
    CaptureLinkIdentity? LinkedSource,
    HdvDirectTransactionViewV15 Transaction,
    HdvProductionNextStepV15 SuggestedReadOnlyNextStep)
{
    public bool CanSendAnotherOrder => false;
    public bool HasServerAck => false;
    public bool CanReleaseTax => false;
    public bool CanAutoReconcile => false;
}

/// <summary>
/// V16 exact cross-entry consultation: the caller-bound run is only a volatile
/// local provenance observation. Manual/Bulk/Lua can commit the same SELL key;
/// this view separates their durable order from the captured ListAsync flow.
/// </summary>
public sealed record HdvProductionCrossEntryInspectionV16(
    HdvProductionTransactionInspectionV15? Transaction,
    Guid CapturedCallRun, bool CaptureStillIssued, bool ExactCallRunMatches,
    string Reason)
{
    public bool IsServerAck => false;
    public bool MaySendOrReplay => false;
    public bool MayReleaseTax => false;
    public bool MayAutoReview => false;
}

/// <summary>
/// Durable V17 local-origin inspection, including a crash-recovered original
/// GID/UID/stock and current source comparison when a PID is still online.
/// A historical origin does not prove a server response, safe replay or fees.
/// </summary>
public sealed record HdvProductionManualReviewInspectionV17(
    HdvProductionOriginalAttemptV17 Original,
    HdvProductionTransactionInspectionV15? Current,
    bool ExactRunAndSourceStillCurrent, string NextManualStep)
{
    public bool IsIndividuallyAttributedServerReceipt => false;
    public bool MayAutoReconcile => false;
    public bool MaySendOrReplay => false;
    public bool MayReleaseTax => false;
}

public sealed partial class LaunchService
{
    // A V12/V13 inspected selection is valid only briefly, and only for the
    // LaunchService instance that actually inspected it. Neither local tick
    // nor a linked TCP/relay source is a server transaction receipt.
    private const long HdvOperatorEvidenceMaxAgeMs = 45_000;
    private readonly Guid _hdvOperatorInspectionHost = Guid.NewGuid();
    // One current *positive* inspection per PID. This is NOT a cache of a
    // clean journal or a permission to ACK/release taxes. Every review still
    // rereads the disk and revalidates the current transport-local source.
    // Reference identity rejects modified/copied or reissued public records.
    private readonly ConcurrentDictionary<uint, HdvOperatorEvidenceV12>
        _hdvOperatorIssuedEvidence = new();
    // Separate from the HUMAN review ticket. Listing captures are read-only
    // and one-use: a late timeout callback cannot upgrade an old failed
    // production attempt when another operation reused its PID.
    private const long HdvProductionListingCaptureMaxAgeMs = 90_000;
    private readonly ConcurrentDictionary<uint, HdvProductionListingCaptureV14>
        _hdvProductionListingCapturesV14 = new();

    /// <summary>
    /// Current exact profile/order/run read from ONE verified journal snapshot,
    /// bracketed by source/identity checks on both sides of file IO.
    /// Appropriate for production next-step diagnosis AND human selection,
    /// never for automatic retry/admission; senders still call TryBegin.
    /// </summary>
    public HdvProductionTransactionInspectionV15 HdvInspectProductionNextStepV15(
        uint pid, string? selectedProfile, string? exactOrder,
        Guid originalRun = default)
    {
        string profile = selectedProfile ?? "", order = exactOrder ?? "";
        HdvDirectTransactionViewV15 Refused(HdvDirectTransactionStateV15 state,
            string reason) => new(profile, order, state, null, false, false, reason);
        HdvProductionTransactionInspectionV15 Wrap(HdvDirectTransactionViewV15 transaction,
            long epoch = 0, CaptureLinkIdentity? source = null)
        {
            HdvProductionNextStepV15 advice = transaction.State switch
            {
                HdvDirectTransactionStateV15.Unseen =>
                    HdvProductionNextStepV15.NoRecordedLocalIntent,
                HdvDirectTransactionStateV15.OtherOrderPending =>
                    HdvProductionNextStepV15.OtherTransactionInFlight,
                HdvDirectTransactionStateV15.Started or HdvDirectTransactionStateV15.Attempted =>
                    HdvProductionNextStepV15.AwaitOrManuallyInspectExactRun,
                HdvDirectTransactionStateV15.Uncertain =>
                    HdvProductionNextStepV15.ReviewRequiredForUncertainOutcome,
                HdvDirectTransactionStateV15.Reviewed =>
                    HdvProductionNextStepV15.AlreadyReviewedNotConfirmed,
                HdvDirectTransactionStateV15.LocallyConfirmedWithAttempt =>
                    HdvProductionNextStepV15.LocalBusinessResultOnly,
                HdvDirectTransactionStateV15.LegacyConfirmedWithoutAttempt =>
                    HdvProductionNextStepV15.LegacyAttemptNotProven,
                _ => HdvProductionNextStepV15.StopForStorageOrIdentity
            };
            // A confirmed old order does not make any newer pending order
            // safe: this advisory must not signal completion of the profile.
            if (transaction.HasOtherPendingOrder)
                advice = HdvProductionNextStepV15.OtherTransactionInFlight;
            return new(pid, profile, order, epoch, source, transaction, advice);
        }
        if (pid == 0 || IsLinkSuspended(pid) || _hdvDirectMutations is null)
            return Wrap(Refused(HdvDirectTransactionStateV15.IdentityInvalid,
                "no active PID/link or journal"));
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        long epochBefore = CurrentLocalProductionDecoderEpoch(pid);
        CaptureLinkIdentity? linkBefore = CurrentHdvOperatorLinkedSource(pid);
        if (server <= 0 || character <= 0 || epochBefore <= 0 ||
            HdvDirectMutationJournal.Profile(server, character) != profile)
            return Wrap(Refused(HdvDirectTransactionStateV15.IdentityInvalid,
                "selected profile differs from current character/server"));
        var transaction = _hdvDirectMutations.ReadTransactionV15(
            profile, order, originalRun);
        if (IsLinkSuspended(pid) || ServerIdOf(pid) != server ||
            CharacterId(pid) != character ||
            CurrentLocalProductionDecoderEpoch(pid) != epochBefore ||
            CurrentHdvOperatorLinkedSource(pid) != linkBefore)
            return Wrap(Refused(HdvDirectTransactionStateV15.SourceChanged,
                "PID/character/decoder/link changed during exact journal inspection"));
        return Wrap(transaction, epochBefore, linkBefore);
    }

    /// <summary>
    /// Inspect the original production capture and its exact journal run in
    /// one bounded, read-only service entry. The journal is checked under its
    /// disk lock; the source is revalidated by V15 around that disk read.
    /// Callers may display the next manual step, never authorize mutation.
    /// </summary>
    public HdvProductionCrossEntryInspectionV16 HdvInspectProductionCrossEntryV16(
        HdvProductionListingCaptureV14? capture)
    {
        if (capture is null || capture.Pid == 0 ||
            capture.InspectionHost != _hdvOperatorInspectionHost ||
            capture.LocalCaptureId == Guid.Empty)
            return new(null, Guid.Empty, false, false,
                "no host-issued production capture; nothing to attribute");
        long now = Environment.TickCount64;
        bool issued = capture.CapturedAtTick > 0 &&
            now >= capture.CapturedAtTick &&
            now - capture.CapturedAtTick <= HdvProductionListingCaptureMaxAgeMs &&
            _hdvProductionListingCapturesV14.TryGetValue(capture.Pid,
                out var current) && ReferenceEquals(current, capture) &&
            HdvSellerOpen(capture.Pid) && !IsLinkSuspended(capture.Pid) &&
            HdvDirectMutationJournal.Profile(ServerIdOf(capture.Pid),
                CharacterId(capture.Pid)) == capture.Profile &&
            CurrentLocalProductionDecoderEpoch(capture.Pid) == capture.SourceEpoch &&
            CurrentHdvOperatorLinkedSource(capture.Pid) == capture.LinkedSource;
        Guid localRun = issued && _hdvDirectMutations is not null
            ? _hdvDirectMutations.ReadProductionCallRunV16(
                capture.LocalCaptureId, capture.Pid, capture.Profile, capture.ExactOrder)
            : Guid.Empty;
        var transaction = HdvInspectProductionNextStepV15(
            capture.Pid, capture.Profile, capture.ExactOrder, localRun);
        var durable = localRun != Guid.Empty && _hdvDirectMutations is not null
            ? _hdvDirectMutations.ReadProductionOriginalAttemptV17(
                capture.Profile, capture.ExactOrder, localRun, capture.LocalCaptureId)
            : null;
        issued = issued && _hdvProductionListingCapturesV14.TryGetValue(capture.Pid,
            out var afterRead) && ReferenceEquals(afterRead, capture) &&
            HdvSellerOpen(capture.Pid) && !IsLinkSuspended(capture.Pid) &&
            HdvDirectMutationJournal.Profile(ServerIdOf(capture.Pid),
                CharacterId(capture.Pid)) == capture.Profile &&
            CurrentLocalProductionDecoderEpoch(capture.Pid) == capture.SourceEpoch &&
            CurrentHdvOperatorLinkedSource(capture.Pid) == capture.LinkedSource;
        if (!issued) localRun = Guid.Empty;
        bool matches = issued && localRun != Guid.Empty &&
            transaction.Transaction.Row is { LocalRunId: var run, OriginalPid: var originalPid } &&
            originalPid == capture.Pid &&
            run == localRun && !transaction.Transaction.HasOtherPendingOrder &&
            durable is { State: HdvDirectMutationReadState.Ready,
                ProductionOrigin: { } original } &&
            durable.Row?.LocalRunId == localRun &&
            original.Gid == capture.Gid && original.BagUid == capture.BagUid &&
            original.Quantity == capture.Quantity && original.Price == capture.Price &&
            original.ObservedStock == capture.ObservedStock &&
            original.DecoderEpoch == capture.SourceEpoch &&
            original.LinkedSource == capture.LinkedSource &&
            transaction.Transaction.State is not
                (HdvDirectTransactionStateV15.SourceChanged or
                 HdvDirectTransactionStateV15.StorageUnavailable or
                 HdvDirectTransactionStateV15.IdentityInvalid);
        return new(transaction, localRun, issued, matches,
            matches ? "original ListAsync call context reached the exact durable TryBegin; business result and server attribution still require separate verification" :
            "no exact original-call TryBegin provenance, or source/run changed; matching UID/GID/price is insufficient");
    }

    /// <summary>
    /// Can inspect crash-retained provenance even with no active PID/decoder.
    /// With a live PID, independently compares the exact owner/source and
    /// original run to a V15 read-only next step. All advice remains manual.
    /// </summary>
    public HdvProductionManualReviewInspectionV17 HdvInspectProductionNextManualReviewV17(
        uint pid, string? selectedProfile, string? exactOrder,
        Guid originalRun = default, Guid originalCapture = default)
    {
        var original = _hdvDirectMutations.ReadProductionOriginalAttemptV17(
            selectedProfile, exactOrder, originalRun, originalCapture);
        HdvProductionTransactionInspectionV15? current = pid != 0
            ? HdvInspectProductionNextStepV15(pid, selectedProfile, exactOrder, originalRun)
            : null;
        bool same = current is not null &&
            original.State == HdvDirectMutationReadState.Ready &&
            original.Row is { } row && row.LocalRunId != Guid.Empty &&
            row.LocalRunId == current.Transaction.Row?.LocalRunId &&
            row.OriginalPid == pid &&
            row.Phase == current.Transaction.Row?.Phase &&
            original.HasDurableAttempt == current.Transaction.HasDurableAttempt &&
            original.OtherOrderPending == current.Transaction.HasOtherPendingOrder &&
            original.ProductionOrigin is { } origin &&
            origin.DecoderEpoch == current.DecoderEpoch &&
            origin.LinkedSource is not null &&
            origin.LinkedSource == current.LinkedSource &&
            current.DecoderEpoch > 0 &&
            current.Transaction.State is not
                (HdvDirectTransactionStateV15.SourceChanged or
                 HdvDirectTransactionStateV15.StorageUnavailable or
                 HdvDirectTransactionStateV15.IdentityInvalid or
                 HdvDirectTransactionStateV15.RunMismatch) &&
            !current.Transaction.HasOtherPendingOrder &&
            // No transient decoder reclassification between the journal and
            // final comparison can turn a historical record into permission.
            !IsLinkSuspended(pid) &&
            HdvDirectMutationJournal.Profile(ServerIdOf(pid), CharacterId(pid)) ==
                selectedProfile &&
            CurrentLocalProductionDecoderEpoch(pid) == current.DecoderEpoch &&
            CurrentHdvOperatorLinkedSource(pid) == current.LinkedSource;
        string instruction = original.State != HdvDirectMutationReadState.Ready
            ? "Stop: original journal/identity unavailable; inspect storage before any action."
            : original.Row is null
                ? "No exact durable original sender; inspect historical records, do not infer a send."
                : original.ProductionOrigin is null
                    ? "This exact run lacks production origin; inspect the manual/Bulk/Lua sender before attributing production."
                : original.Row.NeedsReview
                    ? "Inspect this exact profile/order/run with a fresh V12 human evidence ticket; never replay automatically."
                    : original.Row.Phase == HdvDirectMutationPhase.Reviewed
                        ? "Human reviewed locally; server delivery and tax remain unproven."
                        : "Locally completed order; individually attributable server receipt and tax proof remain unproven.";
        if (original.OtherOrderPending)
            instruction = "Another exact order is pending for this profile; inspect that run before proceeding.";
        else if (original.Row is { NeedsReview: true } && !same &&
                 original.State == HdvDirectMutationReadState.Ready)
            instruction = "Original run remains pending; current source is not individually matched. Reinspect the exact historical order and live link before an explicit human review.";
        return new(original, current, same, instruction);
    }

    /// <summary>
    /// Capture one unambiguous, server-model-backed bag UID immediately BEFORE
    /// a trusted production adapter invokes ListAsync. If manual/Lua/Bulk has
    /// already used the same order key, or another run is pending, refuse.
    /// No transport send, reservation, review or taxable mutation happens here.
    /// </summary>
    public HdvProductionListingCaptureV14? HdvCaptureProductionListingV14(
        uint pid, long gid, int quantity, long price)
    {
        if (pid != 0 && _hdvProductionListingCapturesV14.TryRemove(pid, out var previous))
            _hdvDirectMutations?.ConsumeProductionCallRunV16(
                previous.LocalCaptureId, previous.Pid, previous.Profile, previous.ExactOrder);
        if (pid == 0 || gid <= 0 || quantity <= 0 || price <= 0 ||
            !HdvSellerOpen(pid) || IsLinkSuspended(pid) || _hdvDirectMutations is null)
            return null;
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        long epoch = CurrentLocalProductionDecoderEpoch(pid);
        string profile = HdvDirectMutationJournal.Profile(server, character);
        if (profile.Length == 0 || epoch <= 0) return null;
        CaptureLinkIdentity? source = CurrentHdvOperatorLinkedSource(pid);
        // The GID is witnessed ONLY from the bag inventory at this moment.
        // Multiple eligible UIDs are ambiguous: the Lua picker can select a
        // different stack after async dispatch, so do not guess its UID.
        var eligible = HdvMergedItems(pid).Where(x =>
            x.Item.Uid > 0 && x.Item.Gid == gid && x.Item.Position == 63 &&
            x.Item.Qty >= quantity && ItemCanTrade(pid, x.Item.Uid)).ToArray();
        if (eligible.Length != 1) return null;
        var bag = eligible[0].Item;
        if (HdvSellableQuantity(pid, bag.Uid) != bag.Qty) return null;
        string order = HdvTaxOrderKeys.Sell(bag.Uid, quantity, price, bag.Qty);
        // One durable/anchored read covers BOTH exact replay and the profile's
        // other outstanding order. This observation is NOT an admission lease;
        // the actual HdvSellAsync must still issue its own TryBegin at send.
        var before = _hdvDirectMutations.ReadTransactionV15(profile, order);
        if (before.State != HdvDirectTransactionStateV15.Unseen)
            return null;
        // Journal reads can block behind a concurrent UI/Bulk mutation. Pin
        // the same unique stack/stock AGAIN after IO: the selected UID and
        // baseline must not silently drift before the Lua sender starts.
        var refreshed = HdvMergedItems(pid).Where(x =>
            x.Item.Uid > 0 && x.Item.Gid == gid && x.Item.Position == 63 &&
            x.Item.Qty >= quantity && ItemCanTrade(pid, x.Item.Uid)).ToArray();
        if (refreshed.Length != 1 || refreshed[0].Item.Uid != bag.Uid ||
            refreshed[0].Item.Qty != bag.Qty ||
            HdvSellableQuantity(pid, bag.Uid) != bag.Qty)
            return null;
        if (!HdvSellerOpen(pid) || IsLinkSuspended(pid) ||
            CurrentLocalProductionDecoderEpoch(pid) != epoch ||
            ServerIdOf(pid) != server || CharacterId(pid) != character ||
            CurrentHdvOperatorLinkedSource(pid) != source) return null;
        long now = Environment.TickCount64;
        if (now <= 0) return null;
        var capture = new HdvProductionListingCaptureV14(
            pid, profile, gid, bag.Uid, quantity, price, bag.Qty,
            order, epoch, source, now, _hdvOperatorInspectionHost, Guid.NewGuid());
        // Only a bounded table of positive captures; eviction invalidates
        // observation rights, never the durable journal or a tax reservation.
        if (_hdvProductionListingCapturesV14.Count >= 1024 &&
            !_hdvProductionListingCapturesV14.ContainsKey(pid))
            _hdvProductionListingCapturesV14.Clear();
        _hdvProductionListingCapturesV14[pid] = capture;
        bool CurrentProductionPreSend()
        {
            if (!HdvSellerOpen(pid) || IsLinkSuspended(pid) ||
                HdvDirectMutationJournal.Profile(ServerIdOf(pid),
                    CharacterId(pid)) != capture.Profile ||
                CurrentLocalProductionDecoderEpoch(pid) != capture.SourceEpoch ||
                CurrentHdvOperatorLinkedSource(pid) != capture.LinkedSource)
                return false;
            var eligibleAtSend = HdvMergedItems(pid).Where(x =>
                x.Item.Uid > 0 && x.Item.Gid == capture.Gid &&
                x.Item.Position == 63 && x.Item.Qty >= capture.Quantity &&
                ItemCanTrade(pid, x.Item.Uid)).ToArray();
            return eligibleAtSend.Length == 1 &&
                eligibleAtSend[0].Item.Uid == capture.BagUid &&
                eligibleAtSend[0].Item.Qty == capture.ObservedStock &&
                HdvSellableQuantity(pid, capture.BagUid) ==
                    capture.ObservedStock;
        }
        if (!_hdvDirectMutations.ObserveProductionCallV17(
                capture.LocalCaptureId, capture.Pid, capture.Profile,
                capture.ExactOrder, capture.Gid, capture.BagUid,
                capture.Quantity, capture.Price, capture.ObservedStock,
                capture.SourceEpoch, capture.LinkedSource,
                CurrentProductionPreSend))
        {
            ((ICollection<KeyValuePair<uint, HdvProductionListingCaptureV14>>)
                _hdvProductionListingCapturesV14).Remove(new(pid, capture));
            return null;
        }
        return capture;
    }

    /// <summary>
    /// POST-return one-shot observation for PRIME to wire AFTER awaiting the
    /// original ItemCraftProductionLaunch.ListAsync task. A false/late/expired
    /// caller result cannot become a positive due to a later listing or a
    /// matching manual/Bulk order. The journal's business correlation never
    /// certifies a TCP ACK or the source of the production call by itself.
    /// </summary>
    public HdvProductionListingAssessmentV14 HdvAssessProductionListingV14(
        HdvProductionListingCaptureV14? capture,
        bool originalListAsyncReturnedTrue)
    {
        HdvProductionListingAssessmentV14 Denied(HdvProductionListingOutcomeV14 state,
            string reason, HdvDirectMutationHistoryRow? row = null)
            => new(state, row, null, reason);
        if (capture is null || capture.Pid == 0 ||
            capture.InspectionHost != _hdvOperatorInspectionHost ||
            capture.LocalCaptureId == Guid.Empty ||
            !_hdvProductionListingCapturesV14.TryGetValue(capture.Pid, out var current) ||
            !ReferenceEquals(capture, current))
            return Denied(HdvProductionListingOutcomeV14.Denied,
                "no original one-use production observation");
        // Consume even when the operation timed out or returned false. A
        // delayed true callback after a failed return must not resurrect it.
        bool consumed = ((ICollection<KeyValuePair<uint, HdvProductionListingCaptureV14>>)
            _hdvProductionListingCapturesV14).Remove(new(capture.Pid, capture));
        if (!consumed) return Denied(HdvProductionListingOutcomeV14.Denied,
            "production capture superseded by concurrent caller");
        // The original bool and a matching listing are insufficient to
        // distinguish a production sender from a concurrent manual/Bulk sale
        // of the same UID. Consume the original async call's TryBegin witness
        // BEFORE any early return; never promote a different run to success.
        Guid originallyAdmittedRun = _hdvDirectMutations?.ConsumeProductionCallRunV16(
            capture.LocalCaptureId, capture.Pid, capture.Profile, capture.ExactOrder) ?? Guid.Empty;
        long now = Environment.TickCount64;
        if (capture.CapturedAtTick <= 0 || now < capture.CapturedAtTick ||
            now - capture.CapturedAtTick > HdvProductionListingCaptureMaxAgeMs)
            return Denied(HdvProductionListingOutcomeV14.Denied,
                "production observation expired; no late confirmation");
        if (!HdvSellerOpen(capture.Pid) || IsLinkSuspended(capture.Pid) ||
            HdvDirectMutationJournal.Profile(ServerIdOf(capture.Pid),
                CharacterId(capture.Pid)) != capture.Profile ||
            CurrentLocalProductionDecoderEpoch(capture.Pid) != capture.SourceEpoch ||
            CurrentHdvOperatorLinkedSource(capture.Pid) != capture.LinkedSource)
            return Denied(HdvProductionListingOutcomeV14.SourceChanged,
                "PID/character/decoder/link changed after the original call");
        if (_hdvDirectMutations is null)
            return Denied(HdvProductionListingOutcomeV14.JournalUnavailable,
                "journal absent; never infer a listing from local inventory alone");
        var after = _hdvDirectMutations.ReadTransactionV15(capture.Profile, capture.ExactOrder);
        if (after.State is HdvDirectTransactionStateV15.StorageUnavailable or
            HdvDirectTransactionStateV15.IdentityInvalid)
            return Denied(HdvProductionListingOutcomeV14.JournalUnavailable,
                after.Reason);
        if (after.Row is not { } row)
            return Denied(HdvProductionListingOutcomeV14.NoNewOrder,
                "no new exact fsynced production-looking SELL order");
        if (row.OriginalPid != capture.Pid || row.LocalRunId == Guid.Empty)
            return Denied(HdvProductionListingOutcomeV14.Denied,
                "original PID or run does not match capture", row);
        if (after.HasOtherPendingOrder)
            return Denied(HdvProductionListingOutcomeV14.PendingOrUncertain,
                "another order on the same profile is pending; do not advance production", row);
        if (row.Phase is HdvDirectMutationPhase.Started or
            HdvDirectMutationPhase.Attempted or HdvDirectMutationPhase.Uncertain)
            return Denied(HdvProductionListingOutcomeV14.PendingOrUncertain,
                "request may have been sent; operator must inspect exact run", row);
        if (row.Phase == HdvDirectMutationPhase.Reviewed)
            return Denied(HdvProductionListingOutcomeV14.ManuallyReviewed,
                "human review is not a confirmed sale or ACK", row);
        if (row.Phase == HdvDirectMutationPhase.Confirmed &&
            !after.HasDurableAttempt)
            return Denied(HdvProductionListingOutcomeV14.UnverifiedAttempt,
                "legacy or incomplete SELL lacks its fsynced Attempted transition", row);
        if (row.Phase != HdvDirectMutationPhase.Confirmed ||
            !originalListAsyncReturnedTrue)
            return Denied(HdvProductionListingOutcomeV14.OriginalCallNotConfirmed,
                "matching local journal phase cannot replace original awaited call result", row);
        if (originallyAdmittedRun == Guid.Empty ||
            originallyAdmittedRun != row.LocalRunId)
            return Denied(HdvProductionListingOutcomeV14.CrossEntryAttemptNotAttributed,
                "matching SELL originated outside the captured production call; UID/listing and a local bool are not sender attribution", row);
        var durable = _hdvDirectMutations.ReadProductionOriginalAttemptV17(
            capture.Profile, capture.ExactOrder, originallyAdmittedRun,
            capture.LocalCaptureId);
        if (durable.State == HdvDirectMutationReadState.StorageUnavailable)
            return Denied(HdvProductionListingOutcomeV14.JournalUnavailable,
                "original production origin journal or anchor unavailable", row);
        if (durable.State != HdvDirectMutationReadState.Ready ||
            durable.Row is not { LocalRunId: var originalRun, OriginalPid: var originalPid } ||
            originalRun != originallyAdmittedRun || originalPid != capture.Pid ||
            durable.Row.Phase != HdvDirectMutationPhase.Confirmed ||
            !durable.HasDurableAttempt || durable.OtherOrderPending ||
            durable.ProductionOrigin is not { } origin ||
            origin.Gid != capture.Gid || origin.BagUid != capture.BagUid ||
            origin.Quantity != capture.Quantity || origin.Price != capture.Price ||
            origin.ObservedStock != capture.ObservedStock ||
            origin.DecoderEpoch != capture.SourceEpoch ||
            origin.LinkedSource != capture.LinkedSource)
            return Denied(HdvProductionListingOutcomeV14.CrossEntryAttemptNotAttributed,
                "durable original-call GID/UID/stock/price/source/run is absent or changed; no individual attribution", row);
        // The genuine HdvSellAsync correlated the bag debit + a new listing
        // before Confirmed; require its exact bag->exchange UID link and the
        // still-visible listing GID/quantity/price, never just an identical
        // GID or a similar sale by another UI entrypoint.
        if (!_hdvSellLinks.TryGetValue((capture.Pid, capture.BagUid), out int listingUid) ||
            listingUid <= 0 ||
            !Hdv.MyListings(capture.Pid).Any(x => x.Uid == listingUid &&
                x.Gid == capture.Gid && x.Qty == capture.Quantity &&
                x.Price == capture.Price))
            return Denied(HdvProductionListingOutcomeV14.ListingWitnessMissing,
                "no exact bag UID -> visible listing UID/GID/lot/price witness", row);
        if (!HdvSellerOpen(capture.Pid) || IsLinkSuspended(capture.Pid) ||
            HdvDirectMutationJournal.Profile(ServerIdOf(capture.Pid),
                CharacterId(capture.Pid)) != capture.Profile ||
            CurrentLocalProductionDecoderEpoch(capture.Pid) != capture.SourceEpoch ||
            CurrentHdvOperatorLinkedSource(capture.Pid) != capture.LinkedSource)
            return Denied(HdvProductionListingOutcomeV14.SourceChanged,
                "source changed during journal/listing lookup", row);
        return new(HdvProductionListingOutcomeV14.LocallyCorrelatedListing,
            row, listingUid,
            "exact locally correlated seller listing; not a server ACK, tax receipt or replay permission");
    }

    private CaptureLinkIdentity? CurrentHdvOperatorLinkedSource(uint pid)
        => IsSocketPid(pid)
            ? _socketSourceAuthority.CurrentLink(pid)
            : _localMitmSourceAuthority.CurrentLink(pid);

    private static bool IsFreshHdvOperatorEvidence(HdvOperatorEvidenceV12 evidence,
        long now)
        => evidence.InspectedAtTick > 0 && now >= evidence.InspectedAtTick &&
           now - evidence.InspectedAtTick <= HdvOperatorEvidenceMaxAgeMs;

    /// <summary>
    /// Strict immutable equality for an operator's read-only evidence token.
    /// A fresh inspected offer/listing snapshot is mandatory even if the
    /// profile and recycled PID still happen to match. Equality does not
    /// establish a received server ACK.
    /// </summary>
    public static bool IsSameExactHumanReviewEvidenceV12(
        HdvOperatorEvidenceV12? inspected, HdvOperatorEvidenceV12? current)
        => inspected is { MayAskHumanToReview: true, AttributedServerAck: false,
                SourceEpoch: > 0 } &&
           current is { MayAskHumanToReview: true, AttributedServerAck: false } &&
           inspected.Pid == current.Pid &&
           inspected.Profile == current.Profile &&
           inspected.Order == current.Order &&
           inspected.RunId != Guid.Empty &&
           inspected.RunId == current.RunId &&
           inspected.Phase == current.Phase &&
           inspected.SourceEpoch == current.SourceEpoch &&
           inspected.InspectionHost == current.InspectionHost &&
           inspected.LinkedSource == current.LinkedSource &&
           string.Equals(inspected.Observations, current.Observations,
               StringComparison.Ordinal);

    public HdvOperatorEvidenceV12? HdvInspectExactOperatorEvidenceV12(
        uint pid, string? selectedProfile, string? selectedOrder, Guid selectedRun)
        => HdvInspectExactOperatorEvidenceCoreV13(
            pid, selectedProfile, selectedOrder, selectedRun, issueForHumanReview: true);

    private HdvOperatorEvidenceV12? HdvInspectExactOperatorEvidenceCoreV13(
        uint pid, string? selectedProfile, string? selectedOrder, Guid selectedRun,
        bool issueForHumanReview)
    {
        if (issueForHumanReview && pid != 0)
            _hdvOperatorIssuedEvidence.TryRemove(pid, out _);
        if (pid == 0 || selectedRun == Guid.Empty ||
            string.IsNullOrWhiteSpace(selectedProfile) ||
            string.IsNullOrWhiteSpace(selectedOrder) || IsLinkSuspended(pid))
            return null;
        int server = ServerIdOf(pid);
        long actor = CharacterId(pid);
        long sourceEpoch = CurrentLocalProductionDecoderEpoch(pid);
        CaptureLinkIdentity? linkedSource = CurrentHdvOperatorLinkedSource(pid);
        string profile = HdvDirectMutationJournal.Profile(server, actor);
        if (sourceEpoch <= 0 ||
            !string.Equals(profile, selectedProfile, StringComparison.Ordinal))
            return null;
        // Exact run inspection is not limited to the newest 128 rows and is
        // pinned to the SAME hash-chain/anchor reconstruction as production.
        var transaction = HdvInspectProductionNextStepV15(
            pid, profile, selectedOrder, selectedRun).Transaction;
        if (transaction.State is not
            (HdvDirectTransactionStateV15.Started or
             HdvDirectTransactionStateV15.Attempted or
             HdvDirectTransactionStateV15.Uncertain) ||
            transaction.Row is not { NeedsReview: true } entry) return null;

        // The key is a LOCAL intent. For sell/reprice it contains no GID;
        // NEVER infer one from a matching quantity/price. Buyer and removal
        // keys carry a source-epoch and a GID, but their inventory/listing
        // deltas are still uncorrelated to an individual request.
        string observation = "Source locale epoch " +
            sourceEpoch.ToString(CultureInfo.InvariantCulture) +
            " ; journal " + entry.Phase +
            ". Aucune réponse serveur attribuable au run. " +
            (linkedSource is null
                ? "Lien transport précis non attesté : l'epoch du décodeur seul " +
                  "ne prouve pas une connexion TCP."
                : "Lien transport source-identifié observé, sans attribution d'ACK.");
        string[] fields = selectedOrder.Split(':');
        try
        {
            if (fields.Length == 7 &&
                (fields[0] == "buy" || fields[0] == "remove") &&
                long.TryParse(fields[2], NumberStyles.None,
                    CultureInfo.InvariantCulture, out long gid) && gid > 0 &&
                int.TryParse(fields[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int uid) && uid > 0)
            {
                int bag = Inventory.CountByGid(pid, gid);
                var listings = Hdv.MyListings(pid);
                bool exactListing = listings.Any(x => x.Uid == uid &&
                    x.Gid == gid && x.Qty.ToString(CultureInfo.InvariantCulture) == fields[3] &&
                    x.Price.ToString(CultureInfo.InvariantCulture) == fields[4]);
                observation += $" GID {gid} : sac modèle local {bag}, " +
                    $"annonce UID/quantité/prix exacte actuellement " +
                    (exactListing ? "présente" : "non visible") +
                    ". Changement de stock/annonce non attribuable.";
            }
            else if (fields.Length == 4 && fields[0] == "reprice" &&
                     int.TryParse(fields[1], NumberStyles.None,
                         CultureInfo.InvariantCulture, out int repriceUid) && repriceUid > 0)
            {
                var listings = Hdv.MyListings(pid);
                bool uidPresent = listings.Any(x => x.Uid == repriceUid);
                observation += " Annonce de repricing UID " + repriceUid +
                    (uidPresent ? " visible dans le modèle local" :
                        " absente du modèle local") +
                    " ; aucun GID prouvé par cette clé locale.";
            }
            else if (fields.Length == 5 && fields[0] == "sell")
            {
                observation += " Vente : la clé locale n'inclut pas le GID ; " +
                    "aucune attribution par stock/quantité seule.";
            }
            else return null; // unexpected/malformed durable order
        }
        catch (Exception) { return null; }
        if (ServerIdOf(pid) != server || CharacterId(pid) != actor ||
            CurrentLocalProductionDecoderEpoch(pid) != sourceEpoch ||
            CurrentHdvOperatorLinkedSource(pid) != linkedSource ||
            IsLinkSuspended(pid))
            return null;
        long inspectedAtTick = Environment.TickCount64;
        if (inspectedAtTick <= 0) return null; // no valid monotonic freshness witness
        var result = new HdvOperatorEvidenceV12(pid, profile, selectedOrder, selectedRun, sourceEpoch,
            entry.Phase, observation, true, false, inspectedAtTick,
            _hdvOperatorInspectionHost, linkedSource);
        if (issueForHumanReview)
        {
            // A long-lived desktop may observe many retired PIDs. Reset the
            // small, non-durable POSITIVE token table if full; this only
            // invalidates old inspection permissions (fail closed), never
            // clears a durable order or a tax reservation.
            if (_hdvOperatorIssuedEvidence.Count >= 1024 &&
                !_hdvOperatorIssuedEvidence.ContainsKey(pid))
                _hdvOperatorIssuedEvidence.Clear();
            _hdvOperatorIssuedEvidence[pid] = result;
        }
        return result;
    }

    /// <summary>
    /// Operator-only transition after inspecting a current transport-local
    /// snapshot. The source epoch is compared under the journal review lock;
    /// a stale socket/decoder session or changed order is denied. No ACK or
    /// fiscal credit is inferred even if an offer/stock difference is visible.
    /// </summary>
    public bool HdvReviewFromExactEvidenceV12(HdvOperatorEvidenceV12? evidence)
    {
        if (evidence is not { MayAskHumanToReview: true,
                AttributedServerAck: false } || evidence.SourceEpoch <= 0 ||
            evidence.InspectionHost != _hdvOperatorInspectionHost ||
            !_hdvOperatorIssuedEvidence.TryGetValue(evidence.Pid, out var issued) ||
            !ReferenceEquals(issued, evidence) ||
            !IsFreshHdvOperatorEvidence(evidence, Environment.TickCount64))
            return false;
        var live = HdvInspectExactOperatorEvidenceCoreV13(evidence.Pid,
            evidence.Profile, evidence.Order, evidence.RunId,
            issueForHumanReview: false);
        if (!IsSameExactHumanReviewEvidenceV12(evidence, live)) return false;
        bool reviewed = HdvReviewExactOperatorOrderCore(evidence.Pid, evidence.Profile,
            evidence.Order, evidence.RunId, () =>
                _hdvOperatorIssuedEvidence.TryGetValue(evidence.Pid, out var current) &&
                ReferenceEquals(current, evidence) &&
                evidence.InspectionHost == _hdvOperatorInspectionHost &&
                IsFreshHdvOperatorEvidence(evidence, Environment.TickCount64) &&
                !IsLinkSuspended(evidence.Pid) &&
                CurrentHdvOperatorLinkedSource(evidence.Pid) == evidence.LinkedSource &&
                CurrentLocalProductionDecoderEpoch(evidence.Pid) ==
                    evidence.SourceEpoch &&
                ServerIdOf(evidence.Pid) > 0 && CharacterId(evidence.Pid) > 0);
        if (reviewed) _hdvOperatorIssuedEvidence.TryRemove(evidence.Pid, out _);
        return reviewed;
    }
}
