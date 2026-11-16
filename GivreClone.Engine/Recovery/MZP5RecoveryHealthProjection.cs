namespace GivreClone.Engine.Recovery;

public enum MZP5RecoveryHealthState
{
    Unavailable,
    NoActiveGeneration,
    AwaitingIndependentResponse,
    RequiresExplicitReconciliation,
    LocallyQuiescent
}

/// <summary>
/// A non-sensitive, read-only operator projection from REAL journal records.
/// It contains no operation IDs, account names, protocol frames or UIDs.
/// A terminal journal record is not proof of present network connectivity.
/// </summary>
public sealed record MZP5RecoveryHealthSnapshot(
    MZP5RecoveryHealthState State,
    long Revision,
    int Total,
    int PendingFirstDispatch,
    int AwaitingExternalResponse,
    int RequiresReview,
    int JournalAcknowledged,
    int Reconciled,
    int Cancelled,
    bool HasActiveJournalGeneration)
{
    public bool AutoReplayAuthorized => false;
    public bool IsNetworkConnected => false;
    public bool ServerAckIndependentlyCertified => false;
    public bool IsH24Certified => false;
    public bool NeedsOperatorReview => State is MZP5RecoveryHealthState.Unavailable or
        MZP5RecoveryHealthState.RequiresExplicitReconciliation;

    public static MZP5RecoveryHealthSnapshot Unavailable =>
        new(MZP5RecoveryHealthState.Unavailable, 0, 0, 0, 0, 0, 0, 0, 0, false);
}

/// <summary>
/// A bounded journal-read projection. Never activates a session, opens a
/// dispatch window, reconciles a transaction or sends a request. An open
/// dispatch window from a previous generation is classified as REVIEW rather
/// than permitted re-dispatch. The double-revision read prevents publishing an
/// internally torn snapshot during concurrent journal mutations.
/// </summary>
public static class MZP5RecoveryHealthProjection
{
    public static MZP5RecoveryHealthSnapshot Read(MZP0RecoveryJournal? journal)
    {
        if (journal is null) return MZP5RecoveryHealthSnapshot.Unavailable;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var revision = journal.Revision;
                var records = journal.Records;
                var generation = journal.ActiveGeneration;
                if (revision != journal.Revision) continue;

                var pending = 0;
                var awaiting = 0;
                var review = 0;
                var acknowledged = 0;
                var reconciled = 0;
                var cancelled = 0;

                foreach (var record in records)
                {
                    bool belongsToCurrent = generation is { } current &&
                        record.Generation == current.Number &&
                        record.GenerationNonce == current.Nonce;
                    switch (record.State)
                    {
                        case MZP0DeliveryState.Prepared when belongsToCurrent:
                            pending++; break;
                        case MZP0DeliveryState.DispatchWindowOpened when belongsToCurrent:
                            awaiting++; break;
                        case MZP0DeliveryState.Prepared:
                        case MZP0DeliveryState.DispatchWindowOpened:
                        case MZP0DeliveryState.Uncertain:
                            review++; break;
                        case MZP0DeliveryState.Acknowledged:
                            acknowledged++; break;
                        case MZP0DeliveryState.ReconciledApplied:
                        case MZP0DeliveryState.ReconciledNotApplied:
                            reconciled++; break;
                        case MZP0DeliveryState.CancelledBeforeDispatch:
                            cancelled++; break;
                        default:
                            return MZP5RecoveryHealthSnapshot.Unavailable;
                    }
                }

                // A second read validates that the classification did not race
                // another transaction. The report is entirely categorical.
                if (revision != journal.Revision || generation != journal.ActiveGeneration)
                    continue;
                var state = review > 0 ? MZP5RecoveryHealthState.RequiresExplicitReconciliation :
                    awaiting > 0 ? MZP5RecoveryHealthState.AwaitingIndependentResponse :
                    generation is null ? MZP5RecoveryHealthState.NoActiveGeneration :
                    MZP5RecoveryHealthState.LocallyQuiescent;
                return new(state, revision, records.Count, pending, awaiting,
                    review, acknowledged, reconciled, cancelled, generation is not null);
            }
        }
        catch
        {
            // Journal may be disposed, damaged or concurrently replaced. None of
            // these conditions may result in a green operational indication.
        }
        return MZP5RecoveryHealthSnapshot.Unavailable;
    }
}
