using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Every selectable action is a *read-only operator instruction*. Selecting
/// the V9 runner here does not invoke it or reserve a production attempt.
/// </summary>
public enum GuidedSupplyResumeStepV16
{
    Blocked,
    InspectActivityRecovery,
    InspectProductionAttempt,
    RefreshObservedStock,
    InspectPods,
    StageFreshCheckpoint,
    InspectBank,
    InspectBuy,
    PrepareExactGatherRoute,
    UseExistingV9Runner,
    InspectCompletedHdvListing,
    SelectionRecorded
}

/// <summary>
/// Exact, immutable instruction bound to the checksum-verified V13 checkpoint.
/// A one-shot choice is not a receipt, a route admission, or a V9 mutation lease.
/// </summary>
public sealed record GuidedSupplyExactChoiceV16(
    GuidedSupplyResumeStepV16 Kind, long Gid, long Quantity,
    string PlanHash, string SourceKey, string SessionKey,
    long ObservationRevision)
{
    // V18 optional fields preserve the old V16 journal format. New operator
    // choices carry the exact pods witness from their host inventory preview;
    // pre-V18 choices without it remain readable but cannot pass V18 staging.
    public int? ObservedUsedPodsV18 { get; init; }
    public int? ObservedCapacityPodsV18 { get; init; }

    public bool Valid =>
        (Kind is GuidedSupplyResumeStepV16.InspectBank or
            GuidedSupplyResumeStepV16.InspectBuy or
            GuidedSupplyResumeStepV16.PrepareExactGatherRoute or
            GuidedSupplyResumeStepV16.UseExistingV9Runner) &&
        Gid > 0 && Quantity > 0 && ObservationRevision > 0 &&
        HexHash(PlanHash) && HexHash(SourceKey) && HexHash(SessionKey) &&
        (ObservedUsedPodsV18 is null && ObservedCapacityPodsV18 is null ||
         ObservedUsedPodsV18 is >= 0 &&
         ObservedCapacityPodsV18 is > 0 &&
         ObservedUsedPodsV18 <= ObservedCapacityPodsV18);

    public bool HasExactPodsEvidenceV18 =>
        ObservedUsedPodsV18 is >= 0 && ObservedCapacityPodsV18 is > 0 &&
        ObservedUsedPodsV18 < ObservedCapacityPodsV18;

    public bool MayAuthorizeGameSend => false;
    public bool HasIndividuallyAttributedServerAck => false;

    private static bool HexHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}

/// <summary>
/// Snapshot of a fresh host observation joined with actual durable goal,
/// activity marker, V9 attempt and V12/V13 workflow. Rows/candidates are
/// defensive immutable copies. An empty candidate list means no selection.
/// </summary>
public sealed record GuidedSupplyResumeV16(
    GuidedSupplyResumeStepV16 Step, string Reason,
    string GoalHash, string RequestHash, string PlanHash,
    string SourceKey, string SessionKey, long ObservationRevision,
    IReadOnlyList<GuidedSupplyRowV13> Rows,
    IReadOnlyList<GuidedSupplyExactChoiceV16> Candidates,
    GuidedSupplyExactChoiceV16? RecordedChoice,
    int? UsedPods, int? CapacityPods)
{
    public bool MayAuthorizeGameSend => false;
    public bool MayClearPendingRecovery => false;
    public bool HasIndividuallyAttributedServerAck => false;
    public bool HasSelectableChoice => Candidates.Count > 0;

    // A fresh host witness is retained *only* in the read-only result. All
    // V18 post-choice checks compare this entire record, including pods.
    public GuidedSupplySourceObservation? ObservedSourceV18 { get; init; }
}

/// <summary>
/// Exact current operator follow-up for an already persisted choice. This
/// verdict may prepare only a read-only UI inspection; it never conveys a
/// gameplay, HDV transaction, route, bank or V9 dispatch lease.
/// </summary>
public sealed record GuidedSupplyNextStepEvidenceV18(
    GuidedSupplyResumeStepV16 NextStep, string Reason,
    GuidedSupplyExactChoiceV16? RecordedChoice,
    bool MayPrepareConsultativeHandoff, long ObservationRevision)
{
    public bool MayAuthorizeGameSend => false;
    public bool HasIndividuallyAttributedServerAck => false;
    public bool MayClearPendingRecovery => false;
}

/// <summary>
/// Resume view for the existing V9 runner. No commands or remote pricing calls
/// are exposed. Every candidate is reconstructed from V13 on the SAME current
/// host source/revision, then compared to the durable checkpoint.
/// </summary>
public static class GuidedSupplyResumePlannerV16
{
    private static readonly IReadOnlyList<GuidedSupplyRowV13> NoRows =
        Array.AsReadOnly(Array.Empty<GuidedSupplyRowV13>());
    private static readonly IReadOnlyList<GuidedSupplyExactChoiceV16> NoChoices =
        Array.AsReadOnly(Array.Empty<GuidedSupplyExactChoiceV16>());

    private static GuidedSupplyResumeV16 Deny(GuidedSupplyResumeStepV16 step,
        string reason, string goalHash = "", string requestHash = "") =>
        new(step, reason, goalHash, requestHash, "", "", "", 0,
            NoRows, NoChoices, null, null, null);

    /// <summary>
    /// Read-only, safe to invoke after a crash/restart. Requires a previously
    /// explicit V13 PrepareAndCheckpoint before offering operator choices.
    /// The caller owns the host identity predicate in its read-only actions.
    /// </summary>
    public static GuidedSupplyResumeV16 InspectDurable(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile) =>
        InspectCore(goal, request, actions, goalStore, ledger, activityStore,
            attemptJournal, profile, continueAfterConsultation: false);

    /// <summary>
    /// V17 next-admission diagnostic. A completed bank/HDV *inspection* may be
    /// followed by another exact choice on this same V13 preview; the prior
    /// consultation is never offered again. Existing gather/V9 selections,
    /// pending activity or V9 mutations remain non-replayable.
    /// </summary>
    public static GuidedSupplyResumeV16 InspectNextAdmissionV17(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile) =>
        InspectCore(goal, request, actions, goalStore, ledger, activityStore,
            attemptJournal, profile, continueAfterConsultation: true);

    private static GuidedSupplyResumeV16 InspectCore(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile,
        bool continueAfterConsultation)
    {
        if (goal is not { Valid: true } || request is null || actions is null ||
            goalStore is null || ledger is null || activityStore is null ||
            attemptJournal is null || string.IsNullOrWhiteSpace(profile) ||
            !profile.StartsWith("server:" +
                goal.ServerId.ToString(CultureInfo.InvariantCulture) + ":",
                StringComparison.Ordinal))
            return Deny(GuidedSupplyResumeStepV16.Blocked,
                "objectif, profile ou ports durables absents");

        try
        {
            var saved = goalStore.Read(goal.ServerId, goal.CharacterId);
            if (saved.State != GuidedActivityProductionState.Ready ||
                !Equals(saved.Goal, goal))
                return Deny(GuidedSupplyResumeStepV16.Blocked,
                    "objectif durable absent, illisible ou remplacé");

            string goalHash = GuidedProductionWorkflowLedger.Fingerprint(goal);
            string requestHash = ItemCraftProductionAttemptJournal.RequestFingerprint(request);
            var scope = new ActivityRecoveryScope(goal.ServerId, goal.CharacterId);
            var activity = activityStore.ReadStatus(scope);
            var workflow = ledger.Read(goal.ServerId, goal.CharacterId);
            var attempt = attemptJournal.ReadStatus(profile);

            if (activity.Scope != scope || activity.State != ActivityRecoveryDurableState.Clear)
                return Deny(GuidedSupplyResumeStepV16.InspectActivityRecovery,
                    "ActivityRecovery " + activity.State +
                    " ; mouvement/récolte possiblement émis, reçu causal manquant",
                    goalHash, requestHash);

            if (workflow.Phase == GuidedProductionWorkflowPhase.Uncertain ||
                workflow.Phase is GuidedProductionWorkflowPhase.InProgress or
                    GuidedProductionWorkflowPhase.AwaitingObservation ||
                attempt.Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
                return Deny(GuidedSupplyResumeStepV16.InspectProductionAttempt,
                    "V9/V12 commencé, corrompu ou incertain : rapprocher le run exact",
                    goalHash, requestHash);

            if (workflow.Phase != GuidedProductionWorkflowPhase.Absent &&
                (workflow.GoalHash != goalHash || workflow.RequestHash != requestHash))
                return Deny(GuidedSupplyResumeStepV16.Blocked,
                    "objectif ou recette différents du checkpoint durable",
                    goalHash, requestHash);

            if (workflow.Phase == GuidedProductionWorkflowPhase.Reconciled)
            {
                if (workflow.LocalRunId == Guid.Empty ||
                    attempt.Phase != ItemCraftAttemptPhase.Completed ||
                    attempt.LocalRunId != workflow.LocalRunId ||
                    attempt.RequestHash != workflow.RequestHash)
                    return Deny(GuidedSupplyResumeStepV16.InspectProductionAttempt,
                        "reçu final V9 non attribuable au même run/recette",
                        goalHash, requestHash);
                return Deny(GuidedSupplyResumeStepV16.InspectCompletedHdvListing,
                    "run V9 exact clôturé ; consulter le listing existant",
                    goalHash, requestHash);
            }

            // A route or V9 operator handoff can have escaped this planner.
            // Even a newer inventory revision cannot clear that instruction.
            // V17 must diagnose its terminal state before offering a fresh
            // checkpoint; the actual ActivityRecovery/V9 journal above remains
            // the authority for any subsequent reconciliation.
            if (continueAfterConsultation &&
                workflow.SupplyChoiceV16 is
                    { Kind: GuidedSupplyResumeStepV16.PrepareExactGatherRoute or
                            GuidedSupplyResumeStepV16.UseExistingV9Runner } terminal)
                return new(GuidedSupplyResumeStepV16.SelectionRecorded,
                    "choix trajet/V9 durable : admission extérieure à vérifier ; " +
                    "révision de stock locale insuffisante pour rejouer",
                    goalHash, requestHash, workflow.SupplyPlanHash,
                    workflow.SupplySourceKey, workflow.SupplySessionKey,
                    workflow.SupplyObservationRevision, NoRows, NoChoices,
                    terminal, null, null);

            if (!actions.TryReadSupplySource(out var before) || actions.IsRevoked ||
                before.ServerId != goal.ServerId || before.CharacterId != goal.CharacterId)
                return Deny(GuidedSupplyResumeStepV16.RefreshObservedStock,
                    "identité et révision inventaire hôte non attestées",
                    goalHash, requestHash);

            var plan = GuidedSupplyPlannerV13.Evaluate(goal, request, actions,
                activity, attempt, workflow);

            // A later marker, V9 start, goal replacement or decoder generation
            // may race the local inventory read. Any such race revokes choices.
            if (!actions.TryReadSupplySource(out var after) || actions.IsRevoked ||
                before != after ||
                !Equals(saved, goalStore.Read(goal.ServerId, goal.CharacterId)) ||
                activity != activityStore.ReadStatus(scope) ||
                workflow != ledger.Read(goal.ServerId, goal.CharacterId) ||
                attempt != attemptJournal.ReadStatus(profile))
                return Deny(GuidedSupplyResumeStepV16.RefreshObservedStock,
                    "session/source/journal changés durant le préflight",
                    goalHash, requestHash);

            if (!plan.EvidenceValid)
                return Deny(plan.Reason.Contains("pods", StringComparison.OrdinalIgnoreCase)
                        ? GuidedSupplyResumeStepV16.InspectPods
                        : GuidedSupplyResumeStepV16.RefreshObservedStock,
                    "V13 refuse le stock : " + plan.Reason, goalHash, requestHash);

            if (!plan.PodsObserved || before.UsedPods is null || before.CapacityPods is null)
                return Deny(GuidedSupplyResumeStepV16.InspectPods,
                    "pods hôte non attestés ; maxPods non vérifiable",
                    goalHash, requestHash);

            if (!GuidedSupplyPlannerV13.MatchesExistingReadOnlyCheckpoint(plan, workflow))
                return Deny(GuidedSupplyResumeStepV16.StageFreshCheckpoint,
                    "nouvelle révision observée ; préflight V13 explicite requis avant le choix",
                    goalHash, requestHash);

            var rows = Array.AsReadOnly(plan.Rows.ToArray());
            if (rows.Count == 0 || rows.Count > 128 ||
                rows.Any(x => x.Gid <= 0 || x.Required <= 0 ||
                    x.BagObserved < 0 || x.ChestObserved < 0 ||
                    x.TakeFromBank < 0 || x.BuyCandidate < 0 ||
                    x.GatherDeficit < 0))
                return Deny(GuidedSupplyResumeStepV16.Blocked,
                    "quantités GID invalides", goalHash, requestHash);

            if (workflow.SupplyChoiceV16 is { } recorded &&
                (!continueAfterConsultation ||
                 !GuidedProductionWorkflowLedger.IsConsultation(recorded.Kind)))
                return new(GuidedSupplyResumeStepV16.SelectionRecorded,
                    "choix V16 durable : vérifier les preuves causales avant une nouvelle admission",
                    goalHash, requestHash, plan.PlanHash, plan.SourceKey,
                    plan.SessionKey, plan.ObservationRevision,
                    rows, NoChoices, recorded, before.UsedPods, before.CapacityPods)
                { ObservedSourceV18 = after };

            var available = new List<GuidedSupplyExactChoiceV16>();
            void Add(GuidedSupplyResumeStepV16 step, long gid, long qty)
            {
                if (qty <= 0) return;
                available.Add(new(step, gid, qty, plan.PlanHash, plan.SourceKey,
                    plan.SessionKey, plan.ObservationRevision)
                {
                    ObservedUsedPodsV18 = before.UsedPods,
                    ObservedCapacityPodsV18 = before.CapacityPods
                });
            }

            if (plan.BankOptIn)
                foreach (var row in rows.Where(x => x.TakeFromBank > 0))
                    Add(GuidedSupplyResumeStepV16.InspectBank, row.Gid, row.TakeFromBank);

            if (plan.GatherNeeded && goal.Gather)
                foreach (var row in rows.Where(x => x.GatherDeficit > 0 && !x.BuyAllowed))
                    Add(GuidedSupplyResumeStepV16.PrepareExactGatherRoute,
                        row.Gid, row.GatherDeficit);

            if (plan.MayOfferExistingRunner)
            {
                foreach (var row in rows.Where(x => x.BuyCandidate > 0 && x.BuyAllowed))
                    Add(GuidedSupplyResumeStepV16.InspectBuy, row.Gid, row.BuyCandidate);
                Add(GuidedSupplyResumeStepV16.UseExistingV9Runner, goal.ResultGid,
                    goal.Quantity);
            }

            if (available.Count == 0)
                return Deny(GuidedSupplyResumeStepV16.Blocked,
                    "aucun choix cohérent : " + plan.Reason, goalHash, requestHash);

            if (continueAfterConsultation && workflow.SupplyChoiceV16 is { } old &&
                GuidedProductionWorkflowLedger.IsConsultation(old.Kind))
            {
                var consulted = GuidedProductionWorkflowLedger.SplitConsultations(
                    workflow.SupplyConsultationsV17);
                // Older V16 files can have only SupplyChoiceV16; V17 treats
                // that previous inspection as already consumed.
                var priorToken = GuidedProductionWorkflowLedger.ConsultationToken(old);
                available.RemoveAll(x =>
                    GuidedProductionWorkflowLedger.IsConsultation(x.Kind) &&
                    (GuidedProductionWorkflowLedger.ConsultationToken(x) == priorToken ||
                     consulted.Contains(
                         GuidedProductionWorkflowLedger.ConsultationToken(x),
                         StringComparer.Ordinal)));
            }
            var choices = Array.AsReadOnly(available.ToArray());
            if (choices.Count == 0 && workflow.SupplyChoiceV16 is { } last)
                return new(GuidedSupplyResumeStepV16.SelectionRecorded,
                    "toutes les consultations exactes de ce checkpoint sont consommées",
                    goalHash, requestHash, plan.PlanHash, plan.SourceKey,
                    plan.SessionKey, plan.ObservationRevision, rows,
                    NoChoices, last, before.UsedPods, before.CapacityPods)
                { ObservedSourceV18 = after };
            return new(choices[0].Kind,
                workflow.SupplyChoiceV16 is null
                    ? "choisir une instruction GID/quantité exacte ; seul le runner V9 " +
                      "ou le trajet existant pourra ensuite demander une admission indépendante"
                    : "inspection locale précédente consignée ; prochain choix exact restant " +
                      "sur la même révision, sans reçu serveur implicite",
                goalHash, requestHash, plan.PlanHash, plan.SourceKey,
                plan.SessionKey, plan.ObservationRevision, rows, choices,
                workflow.SupplyChoiceV16, before.UsedPods, before.CapacityPods)
            { ObservedSourceV18 = after };
        }
        catch (Exception ex)
        {
            return Deny(GuidedSupplyResumeStepV16.Blocked,
                "preuve durable/source indisponible (" + ex.GetType().Name +
                ") ; aucun nouveau choix");
        }
    }

    /// <summary>
    /// Opt-in one-shot read-only choice. A fresh full durable inspection checks
    /// that the exact candidate still exists. The V12 cross-process journal
    /// serializes the winner; a loser must inspect again after restart.
    /// No gameplay mutation is performed here, including on success.
    /// </summary>
    public static bool TryChooseOnce(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile, GuidedSupplyExactChoiceV16 choice)
    {
        if (choice is not { Valid: true } || actions is null || ledger is null)
            return false;
        var preview = InspectDurable(goal, request, actions, goalStore, ledger,
            activityStore, attemptJournal, profile);
        if (!preview.HasSelectableChoice || !preview.Candidates.Contains(choice) ||
            !actions.TryReadSupplySource(out var now) || actions.IsRevoked ||
            now != preview.ObservedSourceV18)
            return false;
        if (!ledger.TryNoteExactSupplyChoice(goal, preview.RequestHash, choice))
            return false;
        // Race after the durable write cannot undo the one-shot choice. If it
        // happens, report false and leave the choice consumed (fail closed).
        var after = InspectDurable(goal, request, actions, goalStore, ledger,
            activityStore, attemptJournal, profile);
        return after.Step == GuidedSupplyResumeStepV16.SelectionRecorded &&
            after.RecordedChoice == choice &&
            after.ObservedSourceV18 == preview.ObservedSourceV18;
    }

    /// <summary>
    /// Record the next exact instruction after one or more *non-emitting*
    /// consultations. A durable cross-process compare-and-set rejects an
    /// already consulted GID, a route/V9 selection or a stale checkpoint.
    /// May return false after a successful journal write when source changed:
    /// the durable choice remains consumed and must only be inspected.
    /// </summary>
    public static bool TryChooseNextOnceV17(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile, GuidedSupplyExactChoiceV16 choice)
    {
        if (choice is not { Valid: true } || actions is null || ledger is null)
            return false;
        var preview = InspectNextAdmissionV17(goal, request, actions,
            goalStore, ledger, activityStore, attemptJournal, profile);
        if (!preview.HasSelectableChoice || !preview.Candidates.Contains(choice) ||
            !actions.TryReadSupplySource(out var now) || actions.IsRevoked ||
            now != preview.ObservedSourceV18)
            return false;

        // The external roots are read before AND after the fsynced choice.
        // The ledger serializes V17 consumers even across app restarts.
        var fresh = InspectNextAdmissionV17(goal, request, actions,
            goalStore, ledger, activityStore, attemptJournal, profile);
        if (!fresh.Candidates.Contains(choice) ||
            fresh.PlanHash != preview.PlanHash ||
            fresh.ObservationRevision != preview.ObservationRevision ||
            fresh.SourceKey != preview.SourceKey ||
            fresh.SessionKey != preview.SessionKey ||
            fresh.ObservedSourceV18 != preview.ObservedSourceV18 ||
            !ledger.TryNoteExactSupplyChoice(goal, preview.RequestHash, choice))
            return false;
        if (!actions.TryReadSupplySource(out var postWrite) ||
            actions.IsRevoked ||
            postWrite != preview.ObservedSourceV18)
            return false; // Selection remains consumed; never retry automatically.
        var after = InspectNextAdmissionV17(goal, request, actions,
            goalStore, ledger, activityStore, attemptJournal, profile);
        bool terminalChoice =
            after.Step == GuidedSupplyResumeStepV16.SelectionRecorded &&
            !GuidedProductionWorkflowLedger.IsConsultation(choice.Kind);
        if (terminalChoice &&
            (!actions.TryReadSupplySource(out var terminalSource) ||
             actions.IsRevoked ||
             terminalSource != preview.ObservedSourceV18))
            return false;
        return after.RecordedChoice == choice &&
            after.PlanHash == choice.PlanHash &&
            after.SourceKey == choice.SourceKey &&
            after.SessionKey == choice.SessionKey &&
            after.ObservationRevision == choice.ObservationRevision &&
            (after.ObservedSourceV18 == preview.ObservedSourceV18 ||
             terminalChoice);
    }

    /// <summary>
    /// V18: read-only confirmation immediately BEFORE an existing operator UI
    /// stages an HDV buy or inspects bank stock. This verifies the actual
    /// persistent V16/V17 choice against V11/V12/V13, ActivityRecovery, V9 and
    /// the entire current host witness (including pods), twice across I/O.
    /// It is a one-time diagnostic, not a lease: the caller must re-inspect
    /// immediately before any later handoff. Route/V9 choices always require
    /// their independent existing admission and are never certified here.
    /// </summary>
    public static GuidedSupplyNextStepEvidenceV18 RevalidateRecordedNextStepV18(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        GuidedActivityProductionStore goalStore,
        GuidedProductionWorkflowLedger ledger,
        ActivityRecoveryDurableStore activityStore,
        ItemCraftProductionAttemptJournal attemptJournal,
        string profile, GuidedSupplyExactChoiceV16 choice)
    {
        GuidedSupplyNextStepEvidenceV18 Denied(GuidedSupplyResumeStepV16 step,
            string reason) => new(step, reason, null, false, 0);

        if (choice is not { Valid: true } || actions is null ||
            !GuidedProductionWorkflowLedger.IsConsultation(choice.Kind))
            return Denied(GuidedSupplyResumeStepV16.SelectionRecorded,
                "choix trajet/V9 ou preuve absente : admission indépendante requise");

        // Old V16/V17 choices (persisted before exact pods binding) remain
        // valid journal history; they cannot be promoted into a staged V18
        // buy by inferring their previous pods from today's local UI.
        if (!choice.HasExactPodsEvidenceV18)
            return Denied(GuidedSupplyResumeStepV16.InspectPods,
                "choix ancien sans témoin pods exact ; nouveau checkpoint requis");

        var before = InspectNextAdmissionV17(goal, request, actions,
            goalStore, ledger, activityStore, attemptJournal, profile);
        if (before.RecordedChoice != choice ||
            before.PlanHash != choice.PlanHash ||
            before.SourceKey != choice.SourceKey ||
            before.SessionKey != choice.SessionKey ||
            before.ObservationRevision != choice.ObservationRevision ||
            before.ObservedSourceV18 is not { } source ||
            before.Step is GuidedSupplyResumeStepV16.Blocked or
                GuidedSupplyResumeStepV16.InspectActivityRecovery or
                GuidedSupplyResumeStepV16.InspectProductionAttempt or
                GuidedSupplyResumeStepV16.RefreshObservedStock or
                GuidedSupplyResumeStepV16.InspectPods or
                GuidedSupplyResumeStepV16.StageFreshCheckpoint)
            return Denied(before.Step,
                "choix durable absent ou revu par un autre opérateur : " +
                before.Reason);

        if (!SourceMatchesChoice(source, choice) ||
            !actions.TryReadSupplySource(out var observed) || actions.IsRevoked ||
            observed != source)
            return Denied(GuidedSupplyResumeStepV16.RefreshObservedStock,
                "source ou pods remplacés avant la consultation");

        // Quantity is an advisory shortage *after hypothetical authorized
        // bank stock*, not a verified withdrawal or an HDV transaction.
        bool exact = before.Rows.Any(row => row.Gid == choice.Gid &&
            (choice.Kind == GuidedSupplyResumeStepV16.InspectBank &&
             row.TakeFromBank == choice.Quantity &&
             row.TakeFromBank > 0 ||
             choice.Kind == GuidedSupplyResumeStepV16.InspectBuy &&
             row.BuyAllowed && row.BuyCandidate == choice.Quantity &&
             row.BuyCandidate > 0));
        if (!exact)
            return Denied(GuidedSupplyResumeStepV16.RefreshObservedStock,
                "GID ou déficit net ne correspondent plus au plan exact");

        // After all external journal and stock reads, recompute the whole
        // source-scoped result; a V9 Started, activity marker, new revision or
        // another exact choice must revoke the local staging decision.
        var again = InspectNextAdmissionV17(goal, request, actions,
            goalStore, ledger, activityStore, attemptJournal, profile);
        if (again.RecordedChoice != choice ||
            again.PlanHash != before.PlanHash ||
            again.ObservedSourceV18 != source ||
            !actions.TryReadSupplySource(out var after) || actions.IsRevoked ||
            after != source)
            return Denied(again.Step,
                "preuve modifiée pendant l'inspection ; renouveler la lecture");

        return new(choice.Kind,
            choice.Kind == GuidedSupplyResumeStepV16.InspectBuy
                ? "GID/quantité d'achat net après coffre hypothétique confirmés " +
                  "pour consultation HDV ; aucun prix ni reçu d'achat admis"
                : "GID/quantité du coffre observés pour consultation ; " +
                  "aucun retrait ni dépôt attesté",
            choice, true, choice.ObservationRevision);
    }

    private static bool SourceMatchesChoice(
        GuidedSupplySourceObservation source, GuidedSupplyExactChoiceV16 choice) =>
        source.Valid &&
        GuidedSupplyPlannerV13.SourceKey(source) == choice.SourceKey &&
        GuidedSupplyPlannerV13.SessionKey(source) == choice.SessionKey &&
        source.InventoryRevision == choice.ObservationRevision &&
        source.UsedPods == choice.ObservedUsedPodsV18 &&
        source.CapacityPods == choice.ObservedCapacityPodsV18;
}
