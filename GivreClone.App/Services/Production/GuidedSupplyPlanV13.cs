using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Host-observed provenance for a single inventory read, NOT an ACK. The source
/// current map/generation MAY differ from the imported activity's original
/// route source after legitimate travel. SessionToken must be a host-owned
/// link/decoder generation, never a PID or user label.
/// Pods values are optional: an absent value means NOT OBSERVED.
/// </summary>
public sealed record GuidedSupplySourceObservation(
    int ServerId, long CharacterId, long SourceMap, long SourceMapGeneration,
    string SessionToken, long InventoryRevision, int? UsedPods, int? CapacityPods)
{
    public bool Valid => ServerId > 0 && CharacterId > 0 &&
        SourceMap > 0 && SourceMapGeneration > 0 &&
        SessionToken is { Length: > 0 and <= 256 } &&
        InventoryRevision > 0 &&
        (UsedPods is null && CapacityPods is null ||
         UsedPods is >= 0 && CapacityPods is > 0 &&
         UsedPods <= CapacityPods);
}

public enum GuidedSupplyPlanPhaseV13
{
    Blocked,
    AwaitingObservation,
    ReadyForExistingRunner
}

/// <summary>Read-only proposed quantities, never a dispatched order.</summary>
public sealed record GuidedSupplyRowV13(
    long Gid, int PerCraft, long Required, int BagObserved, int ChestObserved,
    long TakeFromBank, long BuyCandidate, long GatherDeficit,
    bool BuyAllowed);

public sealed record GuidedSupplyPlanV13(
    GuidedSupplyPlanPhaseV13 Phase, string Reason,
    string GoalHash, string RequestHash, string SourceKey, string SessionKey,
    long ObservationRevision, string PlanHash,
    IReadOnlyList<GuidedSupplyRowV13> Rows,
    bool BankOptIn, bool BuyerNeeded, bool GatherNeeded,
    bool GatherAdmissionPending, bool PodsObserved,
    bool EvidenceValid, bool CheckpointWritten = false)
{
    // V18 transient pods witness from the same read-only host source as
    // inventory. Not a receipt; not a second durability format. These values
    // permit a precise check immediately before writing a V13 checkpoint.
    public int? ObservedUsedPodsV18 { get; init; }
    public int? ObservedCapacityPodsV18 { get; init; }

    // This means that the existing ItemCraftProductionRun MAY be offered to
    // the operator, not that a bank/buy/craft send is authorized.
    public bool MayOfferExistingRunner =>
        EvidenceValid && Phase == GuidedSupplyPlanPhaseV13.ReadyForExistingRunner;
}

/// <summary>
/// A supply-plan projection ON TOP of GuidedProductionWorkflowLedger (V12),
/// ItemCraftProductionRun and ActivityRecovery. It never builds a second
/// gameplay engine. No observed quantity alone can establish server success.
/// </summary>
public static class GuidedSupplyPlannerV13
{
    public static GuidedSupplyPlanV13 Evaluate(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        ActivityRecoveryDurableStatus activity,
        ItemCraftAttemptSnapshot actualAttempt,
        GuidedProductionWorkflowSnapshot workflow,
        ActivityRecoveryDurableAdmission? gatherAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(actualAttempt);
        ArgumentNullException.ThrowIfNull(workflow);

        string goalHash = goal.Valid ? GuidedProductionWorkflowLedger.Fingerprint(goal) : "";
        string requestHash;
        try { requestHash = ItemCraftProductionAttemptJournal.RequestFingerprint(request); }
        catch (Exception) { return Denied("requête ou recette non lisible", goalHash); }

        if (!goal.Valid || request.Recipe is not { ResultGid: > 0, ResultPerCraft: > 0,
                Inputs: { Count: > 0 and <= 128 } } recipe ||
            recipe.ResultGid != goal.ResultGid || request.Quantity != goal.Quantity ||
            request.Quantity <= 0 || request.MaxKamas < 0 ||
            recipe.KamasPerCraft is not >= 0 || recipe.KamasPerCraft > request.MaxKamas)
            return Denied("objectif GID/quantité/recette/budget incompatible", goalHash, requestHash);

        if (workflow.Phase is GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain or
                GuidedProductionWorkflowPhase.AwaitingObservation ||
            workflow.Phase is not (GuidedProductionWorkflowPhase.Absent or
                GuidedProductionWorkflowPhase.Reconciled) &&
            (workflow.GoalHash != goalHash || workflow.RequestHash != requestHash))
            return Denied("workflow existant incertain ou objectif/cache remplacé",
                goalHash, requestHash);
        if (actualAttempt.Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
            return Denied("tentative de production réelle non rapprochée",
                goalHash, requestHash);
        if (!actions.SupportsStockObservation ||
            !actions.TryReadSupplySource(out var source))
            return Denied("observation source/stock certifiée par l'hôte absente",
                goalHash, requestHash);
        if (source.ServerId != goal.ServerId || source.CharacterId != goal.CharacterId)
            return Denied("identité serveur/personnage différente",
                goalHash, requestHash);

        string sourceKey = SourceKey(source);
        string sessionKey = SessionKey(source);
        if (workflow.SupplyPlanHash.Length != 0 &&
            (workflow.SupplySessionKey != sessionKey ||
             workflow.SupplyObservationRevision > source.InventoryRevision ||
             workflow.SupplyObservationRevision == source.InventoryRevision &&
             workflow.SupplySourceKey != sourceKey))
            return Denied("session du cache changée ou révision/source antérieure",
                goalHash, requestHash, sourceKey, source.InventoryRevision);
        // Re-reading the exact already-staged revision is allowed for a VM
        // compare-against-PlanHash check. TryNoteSupplyPlan still refuses a
        // second WRITE at the same revision (durable anti-replay).
        bool podsKnown = source.UsedPods is int && source.CapacityPods is int;
        // MaxPods is the operator's upper stop threshold, not an inclusive
        // target. Match the existing atelier guard (pods >= MaxPods).
        if (podsKnown &&
            (long)source.UsedPods!.Value * 100 >=
                (long)source.CapacityPods!.Value * goal.MaxPods)
            return Denied("seuil pods observé atteint ou dépassé ; déposer avant production",
                goalHash, requestHash, sourceKey, source.InventoryRevision);

        if (activity.Scope.ServerId != goal.ServerId ||
            activity.Scope.CharacterId != goal.CharacterId)
            return Denied("identité ActivityRecovery incompatible", goalHash,
                requestHash, sourceKey, source.InventoryRevision);

        bool gatherPending = AdmissionMatches(goal, activity, gatherAdmission);
        if (activity.State != ActivityRecoveryDurableState.Clear &&
            !gatherPending)
            return Denied("ActivityRecovery non libérée : " + activity.State,
                goalHash, requestHash, sourceKey, source.InventoryRevision);

        var rows = new List<GuidedSupplyRowV13>();
        long totalWorkshopFee;
        try
        {
            long batches = checked(((long)request.Quantity +
                recipe.ResultPerCraft!.Value - 1) / recipe.ResultPerCraft.Value);
            totalWorkshopFee = checked(batches * recipe.KamasPerCraft!.Value);
            var merged = recipe.Inputs.GroupBy(x => x.Gid).OrderBy(x => x.Key);
            foreach (var group in merged)
            {
                if (group.Key <= 0 || group.Any(x => x.PerCraft <= 0) ||
                    !actions.TryReadBagCount(group.Key, out int bag) ||
                    bag < 0)
                    return Denied("ingrédient ou stock non observé", goalHash,
                        requestHash, sourceKey, source.InventoryRevision);
                // Equal-GID rows are combined exactly. Mixed BuyAllowed flags
                // are ambiguous; do not silently widen purchase permission.
                if (group.Select(x => x.BuyAllowed).Distinct().Count() != 1)
                    return Denied("autorisation d'achat ambiguë sur GID dupliqué",
                        goalHash, requestHash, sourceKey, source.InventoryRevision);
                long totalPerCraft = checked(group.Sum(x => (long)x.PerCraft));
                if (totalPerCraft > int.MaxValue) throw new OverflowException();
                int perCraft = (int)totalPerCraft;
                long required = checked(batches * perCraft);
                long missing = Math.Max(0L, required - bag);
                int chest = 0;
                if (request.BankAllowed && missing > 0 &&
                    !actions.TryReadChestCount(group.Key, out chest))
                    return Denied("coffre opt-in non observable", goalHash,
                        requestHash, sourceKey, source.InventoryRevision);
                long fromBank = Math.Min(missing, chest);
                long uncovered = missing - fromBank;
                bool canBuy = group.First().BuyAllowed;
                rows.Add(new(group.Key, perCraft, required, bag, chest,
                    fromBank, canBuy ? uncovered : 0, canBuy ? 0 : uncovered,
                    canBuy));
            }
        }
        catch (Exception ex) when (ex is OverflowException or
            ArgumentOutOfRangeException or InvalidOperationException or
            ObjectDisposedException or IOException or UnauthorizedAccessException)
        {
            return Denied("quantité ou source d'approvisionnement non vérifiable (" +
                    ex.GetType().Name + ")",
                goalHash, requestHash, sourceKey, source.InventoryRevision);
        }

        if (!actions.TryReadSupplySource(out var end) ||
            end != source || actions.IsRevoked)
            return Denied("cache/révision/source modifié pendant le plan",
                goalHash, requestHash, sourceKey, source.InventoryRevision);

        bool buyer = rows.Any(r => r.BuyCandidate > 0);
        bool gather = rows.Any(r => r.GatherDeficit > 0);
        GuidedSupplyPlanPhaseV13 phase;
        string reason;
        if (gatherPending)
        {
            phase = GuidedSupplyPlanPhaseV13.AwaitingObservation;
            reason = "tentative de récolte admise durablement mais non rapprochée ; jamais réémettre";
        }
        else if (gather)
        {
            phase = GuidedSupplyPlanPhaseV13.Blocked;
            reason = !goal.Gather ? "récolte non autorisée par l'objectif" :
                !podsKnown ? "pods non observés : récolte interdite" :
                "récolte nécessaire sans preuve d'admission ActivityRecovery ; demander l'action réelle distinctement";
        }
        else if (totalWorkshopFee > request.MaxKamas ||
                 buyer && totalWorkshopFee >= request.MaxKamas)
        {
            phase = GuidedSupplyPlanPhaseV13.Blocked;
            reason = "budget insuffisant pour le nombre de crafts et les achats candidats";
        }
        else
        {
            phase = GuidedSupplyPlanPhaseV13.ReadyForExistingRunner;
            reason = buyer
                ? "achat permis par ingrédient, prix non observé : seul le runner existant contrôle budget et reçus"
                : "approvisionnement sac/coffre observé ; seul le runner existant peut émettre";
        }
        string planHash = Fingerprint(goalHash, requestHash, sourceKey,
            sessionKey, source.InventoryRevision, phase, reason, rows,
            request.BankAllowed, buyer, gather, gatherPending, podsKnown);
        return new(phase, reason, goalHash, requestHash, sourceKey, sessionKey,
            source.InventoryRevision, planHash, rows.AsReadOnly(),
            request.BankAllowed, buyer, gather, gatherPending, podsKnown, true)
        {
            ObservedUsedPodsV18 = source.UsedPods,
            ObservedCapacityPodsV18 = source.CapacityPods
        };
    }

    /// <summary>
    /// Explicit operator opt-in persists only a V12 preview checkpoint, not a
    /// gameplay order. A stale revision or an unresolved run cannot be staged.
    /// </summary>
    public static GuidedSupplyPlanV13 PrepareAndCheckpoint(
        GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request,
        GuidedProductionReadOnlyActions actions,
        ActivityRecoveryDurableStatus activity,
        ItemCraftAttemptSnapshot actualAttempt,
        GuidedProductionWorkflowLedger ledger,
        bool explicitPreviewCheckpoint,
        ActivityRecoveryDurableAdmission? gatherAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var plan = Evaluate(goal, request, actions, activity,
            actualAttempt, ledger.Read(goal.ServerId, goal.CharacterId),
            gatherAdmission);
        if (!explicitPreviewCheckpoint || !plan.EvidenceValid) return plan;
        if (!actions.TryReadSupplySource(out var now) ||
            now.InventoryRevision != plan.ObservationRevision ||
            SourceKey(now) != plan.SourceKey ||
            SessionKey(now) != plan.SessionKey ||
            now.UsedPods != plan.ObservedUsedPodsV18 ||
            now.CapacityPods != plan.ObservedCapacityPodsV18 ||
            actions.IsRevoked)
            return plan with
            {
                Phase = GuidedSupplyPlanPhaseV13.Blocked,
                Reason = "checkpoint refusé : révision, identité ou état durable modifié",
                CheckpointWritten = false
            };
        // V14: an identical read-only reinspection after restart is not a
        // second checkpoint write, and must remain useful to the operator.
        // The V13 ledger still REFUSES any TryNoteSupplyPlan at equal revision.
        // Only the same checksum-verified plan/source/phase may be redisplayed;
        // Started/Uncertain/AwaitingObservation are never admitted here.
        if (MatchesExistingReadOnlyCheckpoint(plan,
                ledger.Read(goal.ServerId, goal.CharacterId)) &&
            MatchesExistingReadOnlyCheckpoint(plan,
                ledger.Read(goal.ServerId, goal.CharacterId)))
            return plan; // V13 CheckpointWritten remains FALSE: no new write.
        if (!ledger.TryNoteSupplyPlan(goal, plan))
            return plan with
            {
                Phase = GuidedSupplyPlanPhaseV13.Blocked,
                Reason = "checkpoint refusé : révision, identité ou état durable modifié",
                CheckpointWritten = false
            };
        // A marker may be fsynced while the hosted decoder/pods changes.
        // Keep that marker quarantined as a stale preview: reporting a usable
        // checkpoint here could enable a downstream operator handoff.
        if (!actions.TryReadSupplySource(out var afterWrite) ||
            afterWrite != now || actions.IsRevoked)
            return plan with
            {
                Phase = GuidedSupplyPlanPhaseV13.Blocked,
                Reason = "checkpoint écrit mais source/pods changés pendant l'IO ; réinspecter",
                CheckpointWritten = false
            };
        return plan with { CheckpointWritten = true };
    }

    /// <summary>
    /// A durable V13 preview may be RE-INSPECTED idempotently without a new
    /// journal write. This does not re-arm an attempt or grant any gameplay
    /// permission: production still checks the V9 Started lease and source.
    /// </summary>
    public static bool MatchesExistingReadOnlyCheckpoint(
        GuidedSupplyPlanV13 plan, GuidedProductionWorkflowSnapshot snapshot) =>
        plan is { EvidenceValid: true, PlanHash: { Length: 64 } } &&
        snapshot.Phase == (plan.MayOfferExistingRunner
            ? GuidedProductionWorkflowPhase.Ready
            : GuidedProductionWorkflowPhase.Blocked) &&
        snapshot.GoalHash == plan.GoalHash &&
        snapshot.RequestHash == plan.RequestHash &&
        snapshot.SupplyPlanHash == plan.PlanHash &&
        snapshot.SupplySourceKey == plan.SourceKey &&
        snapshot.SupplySessionKey == plan.SessionKey &&
        snapshot.SupplyObservationRevision == plan.ObservationRevision &&
        snapshot.SupplyPhase == plan.Phase &&
        // V18: inventory revision does not necessarily advance with a host
        // pods update. Never reuse a checksum-verified older checkpoint with
        // a different (or pre-V18 absent) pods witness.
        snapshot.SupplyUsedPodsV18 == plan.ObservedUsedPodsV18 &&
        snapshot.SupplyCapacityPodsV18 == plan.ObservedCapacityPodsV18;

    private static bool AdmissionMatches(GuidedActivityProductionGoal goal,
        ActivityRecoveryDurableStatus activity,
        ActivityRecoveryDurableAdmission? admission) =>
        goal.Gather && activity.State == ActivityRecoveryDurableState.Pending &&
        admission is { MayDispatch: true } approved &&
        approved.Status.Scope == activity.Scope &&
        approved.Status.State == activity.State &&
        approved.Status.MapId == goal.SourceMap &&
        approved.Status.MapGeneration == goal.SourceMapGeneration &&
        approved.Status.Segment is { Kind: ActivityRecoveryKind.Gather } &&
        Equals(approved.Status.Segment, activity.Segment) &&
        approved.Status.MapId == activity.MapId &&
        approved.Status.MapGeneration == activity.MapGeneration;

    internal static string SourceKey(GuidedSupplySourceObservation source) =>
        Hash(string.Join("|", source.ServerId, source.CharacterId,
            source.SourceMap, source.SourceMapGeneration, source.SessionToken));

    internal static string SessionKey(GuidedSupplySourceObservation source) =>
        Hash(string.Join("|", source.ServerId, source.CharacterId, source.SessionToken));

    internal static string Fingerprint(string goalHash, string requestHash,
        string sourceKey, string sessionKey, long revision,
        GuidedSupplyPlanPhaseV13 phase, string reason,
        IEnumerable<GuidedSupplyRowV13> rows,
        bool bankOptIn, bool buyer, bool gather,
        bool gatherPending, bool podsObserved)
    {
        var text = new StringBuilder(goalHash).Append('|').Append(requestHash)
            .Append('|').Append(sourceKey).Append('|').Append(sessionKey).Append('|')
            .Append(revision.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append((int)phase)
            .Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(reason)))
            .Append('|').Append(bankOptIn ? 1 : 0)
            .Append('|').Append(buyer ? 1 : 0)
            .Append('|').Append(gather ? 1 : 0)
            .Append('|').Append(gatherPending ? 1 : 0)
            .Append('|').Append(podsObserved ? 1 : 0);
        foreach (var r in rows)
            text.Append('|').Append(r.Gid).Append(':').Append(r.PerCraft)
                .Append(':').Append(r.Required).Append(':')
                .Append(r.BagObserved).Append(':').Append(r.ChestObserved)
                .Append(':').Append(r.TakeFromBank).Append(':')
                .Append(r.BuyCandidate).Append(':')
                .Append(r.GatherDeficit).Append(':')
                .Append(r.BuyAllowed ? 1 : 0);
        return Hash(text.ToString());
    }

    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static GuidedSupplyPlanV13 Denied(string reason, string goalHash = "",
        string requestHash = "", string sourceKey = "", long revision = 0) =>
        new(GuidedSupplyPlanPhaseV13.Blocked, reason, goalHash,
            requestHash, sourceKey, "", revision, "",
            Array.Empty<GuidedSupplyRowV13>(), false, false, false,
            false, false, false);
}
