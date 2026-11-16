using System;
using System.Globalization;
using System.Threading;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>
    /// Actual V11 UI→profile→durable journal boundary. The operator explicitly
    /// chose one exact run/order seen on screen. A still-live sender, stale
    /// selection, changed character/PID or corrupt anchor never becomes a
    /// generic clearance. Does not debit/refund fiscal tax reservations.
    /// </summary>
    public bool HdvReviewExactOperatorOrder(
        uint pid, string? selectedProfile, string? exactOrder, Guid selectedRun)
    {
        // Keep the V11 signature but never perform an inspection AND a review
        // in a single programmatic call: that would bypass the operator's
        // explicit inspect→confirm workflow. A separately issued, unchanged
        // V12/V13 token is mandatory before this legacy entry can clear it.
        if (!_hdvOperatorIssuedEvidence.TryGetValue(pid, out var issued) ||
            issued.Profile != selectedProfile || issued.Order != exactOrder ||
            issued.RunId != selectedRun) return false;
        return HdvReviewFromExactEvidenceV12(issued);
    }

    /// <summary>
    /// V12 adds a source/connection-specific callback that is checked under
    /// the SAME fsynced journal lock, not merely before the modal opens.
    /// Both the V11 public entrypoint and the V12 classic UI now require a
    /// fresh exact inspection before reaching this durable write boundary.
    /// </summary>
    private bool HdvReviewExactOperatorOrderCore(uint pid, string? selectedProfile,
        string? exactOrder, Guid selectedRun, Func<bool> isCurrentEvidence)
    {
        if (pid == 0 || string.IsNullOrWhiteSpace(selectedProfile) ||
            string.IsNullOrWhiteSpace(exactOrder) || selectedRun == Guid.Empty ||
            _hdvDirectMutations is null) return false;
        string actual = HdvCurrentMutationProfile(pid);
        if (!string.Equals(actual, selectedProfile, StringComparison.Ordinal))
            return false;
        var before = HdvInspectProductionNextStepV15(
            pid, actual, exactOrder, selectedRun).Transaction;
        if (before.State is not
            (HdvDirectTransactionStateV15.Started or
             HdvDirectTransactionStateV15.Attempted or
             HdvDirectTransactionStateV15.Uncertain) ||
            before.Row is not { NeedsReview: true, LocalRunId: var run } ||
            run != selectedRun)
            return false;
        bool Current()
        {
            try
            {
                return HdvCurrentMutationProfile(pid) == actual &&
                    isCurrentEvidence();
            }
            catch (Exception) { return false; }
        }
        if (!Current() ||
            !_hdvDirectMutations.TryReviewExact(actual, exactOrder, selectedRun, Current))
            return false;
        // Once the exact review was fsynced, its durable result remains true
        // even if the PID owner changes immediately afterward. A stale PID
        // must never clear the new owner's volatile review state.
        if (HdvCurrentMutationProfile(pid) != actual)
        {
            TransportLog.Line($"[hdv] pid {pid}: review {selectedRun:N} durably recorded before PID identity changed; no volatile latch cleared.");
            return true;
        }
        if (exactOrder.StartsWith("buy:", StringComparison.Ordinal) &&
            _hdvBuyUncertainProfile.TryGetValue(pid, out var b) && b == actual)
        {
            _hdvUncertainBuy.TryRemove(pid, out _);
            _hdvBuyUncertainProfile.TryRemove(pid, out _);
        }
        else if (exactOrder.StartsWith("sell:", StringComparison.Ordinal))
        {
            // All manual/Bulk/Lua/production sells share this PID semaphore.
            // A newer send can start immediately after the journal's review
            // fsync; only clear the old in-memory latch while holding the
            // SAME seller gate, and only if its UID/qty/price still belongs
            // to the exact reviewed key and no newer profile order is pending.
            var sellerGate = _hdvSellLocks.GetOrAdd(pid,
                static _ => new SemaphoreSlim(1, 1));
            if (sellerGate.Wait(0))
                try
                {
                    string[] fields = exactOrder.Split(':');
                    bool exactCached = fields.Length == 5 &&
                        int.TryParse(fields[4], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int stock) &&
                        _hdvSellNeedsReview.TryGetValue(pid, out var cached) &&
                        HdvTaxOrderKeys.Sell(cached.Uid, cached.Quantity,
                            cached.Price, stock) == exactOrder;
                    var post = _hdvDirectMutations.ReadTransactionV15(
                        actual, exactOrder, selectedRun);
                    if (exactCached &&
                        HdvCurrentMutationProfile(pid) == actual &&
                        _hdvSellUncertainProfile.TryGetValue(pid, out var seller) &&
                        seller == actual && !post.HasOtherPendingOrder &&
                        post.State == HdvDirectTransactionStateV15.Reviewed)
                    {
                        _hdvSellNeedsReview.TryRemove(pid, out _);
                        _hdvSellUncertainProfile.TryRemove(pid, out _);
                        _hdvSellError.TryRemove(pid, out _);
                    }
                }
                finally { sellerGate.Release(); }
        }
        else if (exactOrder.StartsWith("remove:", StringComparison.Ordinal) &&
            _hdvRemoveUncertainProfile.TryGetValue(pid, out var r) && r == actual)
        {
            _hdvRemoveNeedsReview.TryRemove(pid, out _);
            _hdvRemoveUncertainProfile.TryRemove(pid, out _);
            _hdvRemoveLastDisposition.TryRemove(pid, out _);
        }
        else if (exactOrder.StartsWith("reprice:", StringComparison.Ordinal))
        {
            // The profile-scoped latch has the original identity, so never
            // remove the stale PID cache for a different owner.
            int server = ServerIdOf(pid);
            long character = CharacterId(pid);
            if (server > 0 && character > 0 &&
                HdvDirectMutationJournal.Profile(server, character) == actual)
            {
                _hdvRepriceNeedsReview.TryRemove(pid, out _);
                _hdvRepriceProfileNeedsReview.TryRemove((server, character), out _);
            }
        }
        TransportLog.Line($"[hdv] pid {pid} : ordre {exactOrder.Split(':')[0]} " +
            $"run-local {selectedRun:N} rapproché UNIQUEMENT sur déclaration opérateur. " +
            "Aucune attribution réseau ni taxe libérée.");
        HdvUpdated?.Invoke(pid);
        return true;
    }
}
