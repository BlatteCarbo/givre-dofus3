using System;
using System.Linq;
using GivreClone.App.Services;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Read-only next step for the EXISTING V9 production runner and V12/V13
/// checkpoints. This is neither an action admission nor a reconciliation API.
/// An observation after a possible send cannot close its write-ahead marker.
/// </summary>
public enum GuidedSupplyNextStepV15
{
    Blocked,
    InspectActivityRecovery,
    InspectProductionAttempt,
    RefreshObservedStock,
    ChooseExactGatherGid,
    PrepareExactGatherRoute,
    InspectBankAndPods,
    UseExistingProductionRunner,
    InspectCompletedHdvListing
}

public sealed record GuidedSupplyContinuationV15(
    GuidedSupplyNextStepV15 NextStep, string Reason,
    long Gid, long Quantity, string SourceKey, string SessionKey,
    long InventoryRevision, bool RequiresExplicitOperatorAction)
{
    // A next-step suggestion is never a gameplay lease or proof of server ACK.
    public bool MayAuthorizeGameSend => false;
    public bool MayClearPendingRecovery => false;
    public bool HasIndividuallyAttributedServerAck => false;
}

public static class GuidedSupplyContinuationPlannerV15
{
    public static GuidedSupplyContinuationV15 Inspect(
        GuidedActivityProductionGoal goal,
        GuidedProductionWorkflowSnapshot workflow,
        ActivityRecoveryDurableStatus activity,
        ItemCraftAttemptSnapshot attempt,
        GuidedSupplyPlanV13? currentPlan,
        bool sameObservedOwner)
    {
        static GuidedSupplyContinuationV15 Result(
            GuidedSupplyNextStepV15 step, string reason, long gid = 0,
            long quantity = 0, GuidedSupplyPlanV13? plan = null) =>
            new(step, reason, gid, quantity, plan?.SourceKey ?? "",
                plan?.SessionKey ?? "", plan?.ObservationRevision ?? 0,
                step is not (GuidedSupplyNextStepV15.Blocked or
                    GuidedSupplyNextStepV15.InspectActivityRecovery or
                    GuidedSupplyNextStepV15.InspectProductionAttempt));

        if (goal is not { Valid: true } || workflow is null || activity is null ||
            !sameObservedOwner || activity.Scope.ServerId != goal.ServerId ||
            activity.Scope.CharacterId != goal.CharacterId)
            return Result(GuidedSupplyNextStepV15.Blocked,
                "identité/objectif/source non vérifiés ; aucun nouvel ordre");

        // An activity marker can represent an action that actually escaped.
        // Never inspect fresh inventory to turn this into a new admission.
        if (activity.State != ActivityRecoveryDurableState.Clear)
            return Result(GuidedSupplyNextStepV15.InspectActivityRecovery,
                "activité " + activity.State + " : " + activity.Detail +
                " ; inspecter le segment durable (carte " + activity.MapId +
                ", génération " + activity.MapGeneration +
                "). Ni déplacement, ni stock local, ni pods ne sont un ACK");

        if (workflow.Phase == GuidedProductionWorkflowPhase.Uncertain)
            return Result(GuidedSupplyNextStepV15.InspectProductionAttempt,
                "journal durable workflow illisible/incertain : inspection humaine requise, aucune réémission");

        if (workflow.Phase != GuidedProductionWorkflowPhase.Absent &&
            workflow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(goal))
            return Result(GuidedSupplyNextStepV15.Blocked,
                "objectif remplacé depuis le checkpoint durable");

        if (workflow.Phase is GuidedProductionWorkflowPhase.Uncertain or
            GuidedProductionWorkflowPhase.InProgress or
            GuidedProductionWorkflowPhase.AwaitingObservation ||
            attempt.Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
            return Result(GuidedSupplyNextStepV15.InspectProductionAttempt,
                "run V9 ou workflow " + workflow.Phase +
                " non rapproché ; inspecter reçus exacts, aucun replay");

        if (workflow.Phase == GuidedProductionWorkflowPhase.Reconciled)
        {
            if (attempt.Phase != ItemCraftAttemptPhase.Completed ||
                workflow.LocalRunId == Guid.Empty ||
                attempt.LocalRunId != workflow.LocalRunId ||
                attempt.RequestHash != workflow.RequestHash)
                return Result(GuidedSupplyNextStepV15.InspectProductionAttempt,
                    "workflow rapproché mais reçu V9 exact non attribuable");
            return Result(GuidedSupplyNextStepV15.InspectCompletedHdvListing,
                "run V9 exact clôturé ; inspecter le listing HDV sans nouvelle vente",
                goal.ResultGid, goal.Quantity);
        }

        if (workflow.Phase == GuidedProductionWorkflowPhase.Absent ||
            currentPlan is not { EvidenceValid: true } plan ||
            !GuidedSupplyPlannerV13.MatchesExistingReadOnlyCheckpoint(
                plan, workflow))
            return Result(GuidedSupplyNextStepV15.RefreshObservedStock,
                "checkpoint absent/périmé ; relire objectif, recette, source, pods et révision avant un nouveau préflight");

        if (plan.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(goal) ||
            plan.Rows is null || plan.Rows.Count == 0 ||
            plan.ObservationRevision <= 0)
            return Result(GuidedSupplyNextStepV15.Blocked,
                "plan GID/recette/révision incohérent");

        var gather = plan.Rows.Where(r => r.GatherDeficit > 0 &&
            !r.BuyAllowed).ToArray();
        if (gather.Length > 0)
        {
            if (!goal.Gather || !plan.PodsObserved)
                return Result(GuidedSupplyNextStepV15.Blocked,
                    "récolte requise mais opt-in ou pods attestés manquants");
            if (gather.Length != 1)
                return Result(GuidedSupplyNextStepV15.ChooseExactGatherGid,
                    "plusieurs GID manquants : sélectionner explicitement un GID, sans trajet implicite",
                    plan: plan);
            return Result(GuidedSupplyNextStepV15.PrepareExactGatherRoute,
                "GID " + gather[0].Gid +
                " : préparation du trajet Lua existant, confirmation one-shot obligatoire",
                gather[0].Gid, gather[0].GatherDeficit, plan);
        }

        if (plan.Rows.Any(r => r.TakeFromBank > 0) && plan.BankOptIn)
            return Result(GuidedSupplyNextStepV15.InspectBankAndPods,
                "coffre autorisé : inspecter les quantités puis réévaluer le stock ; retrait uniquement via V9",
                plan: plan);

        if (plan.MayOfferExistingRunner &&
            workflow.Phase == GuidedProductionWorkflowPhase.Ready)
            return Result(GuidedSupplyNextStepV15.UseExistingProductionRunner,
                "étape suivante : bouton Lancer existant, V9 revérifie achats, budget, atelier et reçus exacts",
                goal.ResultGid, goal.Quantity, plan);

        return Result(GuidedSupplyNextStepV15.RefreshObservedStock,
            "préflight V13 non prêt : " + plan.Reason +
            " ; aucun sender indépendant", plan: plan);
    }
}
