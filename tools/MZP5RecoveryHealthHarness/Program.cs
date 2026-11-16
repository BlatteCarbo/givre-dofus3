using GivreClone.Engine.Recovery;

int checks = 0;
void Check(bool value, string why)
{
    if (!value) throw new InvalidOperationException(why);
    checks++;
}

var folder = Path.Combine(Path.GetTempPath(), "MZP5RecoveryHealth-" + Guid.NewGuid().ToString("N"));
var path = Path.Combine(folder, "journal.json");
try
{
    Check(MZP5RecoveryHealthProjection.Read(null).State == MZP5RecoveryHealthState.Unavailable,
        "A missing journal must not show connected/healthy");
    Guid operation;
    using (var journal = new MZP0RecoveryJournal(path))
    {
        var initial = MZP5RecoveryHealthProjection.Read(journal);
        Check(initial.State == MZP5RecoveryHealthState.NoActiveGeneration && initial.Total == 0,
            "Empty journal should have no session");
        var generation = journal.Activate(new MZP0SessionIdentity(
            70001, 638900000000000000, 412, "3.7.10.10"));
        operation = Guid.NewGuid();
        journal.BeginIntent(generation, operation, 1, MZP0OperationCategory.Economy,
            MZP0DeliverySemantics.NonIdempotent);
        var prepared = MZP5RecoveryHealthProjection.Read(journal);
        Check(prepared.PendingFirstDispatch == 1 && prepared.AwaitingExternalResponse == 0 &&
              !prepared.AutoReplayAuthorized && !prepared.IsNetworkConnected,
            "Prepared metadata must never auto-send");
        journal.OpenDispatchWindow(generation, operation);
        var opened = MZP5RecoveryHealthProjection.Read(journal);
        Check(opened.State == MZP5RecoveryHealthState.AwaitingIndependentResponse &&
              opened.AwaitingExternalResponse == 1 && !opened.ServerAckIndependentlyCertified,
            "Window opening must not claim a response");
        journal.MarkUncertain(generation, operation);
        var uncertain = MZP5RecoveryHealthProjection.Read(journal);
        Check(uncertain.RequiresReview == 1 && uncertain.NeedsOperatorReview &&
              !uncertain.AutoReplayAuthorized && !uncertain.IsH24Certified,
            "Uncertain action may not restart or become acknowledged");
    }

    // A new process opening the real journal cannot silently reuse the
    // previous generation or resend a still ambiguous non-idempotent operation.
    using (var reopened = new MZP0RecoveryJournal(path))
    {
        var prior = MZP5RecoveryHealthProjection.Read(reopened);
        Check(prior.State == MZP5RecoveryHealthState.RequiresExplicitReconciliation &&
              !prior.HasActiveJournalGeneration && prior.RequiresReview == 1,
            "Reopened journal should preserve unresolved action without active session");
        var fresh = reopened.Activate(new MZP0SessionIdentity(
            70002, 638900000000000001, 413, "3.7.10.10"));
        Check(!reopened.TryReconcile(fresh, operation,
            new MZP0ReconciliationEvidence(1, Guid.NewGuid(), 1,
                MZP0ReconciliationConclusion.Applied, AuthorityVerified: false)),
            "False external authority must not reconcile");
        Check(MZP5RecoveryHealthProjection.Read(reopened).RequiresReview == 1,
            "False reconciliation cleared warning");
        var priorRecord = reopened.Records.Single(r => r.OperationId == operation);
        Check(reopened.TryReconcile(fresh, operation, new MZP0ReconciliationEvidence(
            priorRecord.Generation, priorRecord.GenerationNonce, priorRecord.CorrelationUid,
            MZP0ReconciliationConclusion.DefinitelyNotApplied, AuthorityVerified: true)),
            "Explicit upstream evidence was refused by the real journal fixture");
        var resolved = MZP5RecoveryHealthProjection.Read(reopened);
        Check(resolved.State == MZP5RecoveryHealthState.LocallyQuiescent &&
              resolved.Reconciled == 1 && resolved.RequiresReview == 0 &&
              !resolved.ServerAckIndependentlyCertified,
            "Resolved journal falsely asserted a live ACK");
    }

    using (var journal = new MZP0RecoveryJournal(path))
    {
        // Reopening after reconciliation shows a locally durable result, but
        // there is still no active session or authenticated game state.
        var durable = MZP5RecoveryHealthProjection.Read(journal);
        Check(durable.State == MZP5RecoveryHealthState.NoActiveGeneration &&
              durable.Reconciled == 1 && !durable.IsNetworkConnected,
            "Durable history was confused with a connection");
        journal.Dispose();
        Check(MZP5RecoveryHealthProjection.Read(journal).State == MZP5RecoveryHealthState.Unavailable,
            "Disposed journal status should fail closed");
    }
}
finally
{
    var full = Path.GetFullPath(folder);
    var temp = Path.GetFullPath(Path.GetTempPath());
    if (!full.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
           StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(full).StartsWith("MZP5RecoveryHealth-", StringComparison.Ordinal))
        throw new InvalidOperationException("Unsafe cleanup location");
    if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
}
Console.WriteLine($"MZP5 health projection: PASS ({checks} real journal checks). No session created or action replayed.");
