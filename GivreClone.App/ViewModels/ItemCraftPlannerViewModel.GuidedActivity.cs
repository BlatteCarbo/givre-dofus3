using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;

namespace GivreClone.App.ViewModels;

/// <summary>Operator-facing projection of real V9/V12 prerequisites, not a second executor.</summary>
public sealed record GuidedSupplyStageRow(
    string Stage, long Gid, long Missing, string Decision, string State);

public sealed record GuidedSupplyRoutePreparation(uint Pid,
    GuidedActivityProductionGoal Goal, string RequestHash,
    long GatherItemGid, long Required = 0,
    string PlanHash = "", string SourceKey = "", string SessionKey = "",
    long InventoryRevision = 0);

/// <summary>Explicit navigation to existing HDV review; never a sell request.</summary>
public sealed record GuidedSupplyHdvInspection(uint Pid,
    GuidedActivityProductionGoal Goal);

/// <summary>Exact durable InspectBuy instruction for staging a normal HDV buy row.
/// Consultation only: no budget grant, game send, stock receipt or ACK.</summary>
public sealed record GuidedSupplyHdvBuyStage(uint Pid,
    GuidedActivityProductionGoal Goal, GuidedSupplyExactChoiceV16 Choice);

/// <summary>Navigation to existing manual bank/pods UI, no withdrawal send.</summary>
public sealed record GuidedSupplyBankInspection(uint Pid,
    GuidedActivityProductionGoal Goal,
    GuidedSupplyExactChoiceV16? Choice = null);

/// <summary>Read-only handoff to the existing Lua recovery inspection panel.</summary>
public sealed record GuidedSupplyRecoveryInspection(uint Pid,
    GuidedActivityProductionGoal Goal, long IngredientGid = 0,
    long Required = 0);

public sealed partial class ItemCraftPlannerViewModel
{
    private readonly GuidedProductionWorkflowLedger _guidedWorkflow =
        new(GuidedProductionWorkflowLedger.DefaultRoot());
    private Guid _activeGuidedLocalRun;
    private GuidedProductionStockPreflight? _lastGuidedSupplyStock;
    private string? _lastGuidedSupplyFingerprint;
    private GuidedSupplyPlanV13? _lastGuidedSupplyPlan;
    private bool _lastGuidedSupplyCheckpointPresent;
    private int _guidedV16RunnerHandoffInFlight;

    public event Action<GuidedSupplyRoutePreparation>? GuidedSupplyRoutePreparationRequested;
    public event Action<GuidedSupplyHdvInspection>? GuidedSupplyHdvInspectionRequested;
    public event Action<GuidedSupplyHdvBuyStage>? GuidedSupplyHdvBuyStageRequested;
    public event Action<GuidedSupplyBankInspection>? GuidedSupplyBankInspectionRequested;
    public event Action<GuidedSupplyRecoveryInspection>? GuidedSupplyRecoveryInspectionRequested;
    public ObservableCollection<GuidedSupplyStageRow> GuidedSupplyStages { get; } = new();
    public ObservableCollection<GuidedSupplyExactChoiceV16> GuidedV16ExactChoices { get; } = new();
    [ObservableProperty] public partial GuidedSupplyExactChoiceV16? SelectedGuidedV16ExactChoice { get; set; }
    [ObservableProperty] public partial string GuidedSupplyV16ChoiceStatus { get; private set; } =
        "V16 · Inspecter les choix exacts GID/quantité du plan V13 ; un choix durable ne constitue pas une admission de jeu.";
    [ObservableProperty] public partial string GuidedSupplyActionStatus { get; private set; } =
        "Approvisionnement : inspecter les ressources avant de préparer un trajet one-shot.";
    [ObservableProperty] public partial string GuidedSupplyListingEvidenceStatus { get; private set; } =
        "Listing HDV : aucune corrélation locale inspectée. Une vente manuelle n'est pas un ACK.";
    [ObservableProperty] public partial string GuidedSupplyContinuationStatus { get; private set; } =
        "V15 · Inspecter la suite après trajet, récolte ou intervention manuelle ; aucun effet local n'est un ACK.";
    [ObservableProperty] public partial bool GuidedSupplyV16RunnerOptIn { get; set; }
    [ObservableProperty] public partial string GuidedSupplyV16RunnerStatus { get; private set; } =
        "V16 · Le runner V9 doit être explicitement autorisé pour cette seule action ; ni le plan ni le stock ne valent ACK.";
    [ObservableProperty] public partial string GuidedSupplyV16RecoveryStatus { get; private set; } =
        "V16 · Inspection après activité disponible ; le marqueur Pending reste durable jusqu'à preuve indépendante.";
    [ObservableProperty] public partial string GuidedSupplyGatherItemGidText { get; set; } = "";

    public ObservableCollection<GuidedProductionIngredientObservation> GuidedObservedIngredients { get; } = new();
    [ObservableProperty] public partial string GuidedWorkflowStatus { get; private set; } =
        "Workflow : importer un objectif, vérifier le stock et la reprise, puis lancer explicitement.";
    [ObservableProperty] public partial string GuidedStockStatus { get; private set; } =
        "Stock : observation non demandée, aucune récolte ou fabrication certifiée.";

    /// <summary>
    /// Active operator-guided source. The normal manual atelier remains
    /// available after ClearGuidedActivity, but a source-bound goal is never
    /// silently migrated to a new character or a different product.
    /// </summary>
    public GuidedActivityProductionGoal? GuidedActivityGoal { get; private set; }

    /// <summary>
    /// The selected target may be edited after import. Real bank/HDV/craft
    /// action admission must continuously match the immutable persisted
    /// objective, rather than silently using a different GID or quantity.
    /// </summary>
    public bool GuidedActivityTargetStillMatches(GuidedActivityProductionGoal goal) =>
        goal is { Valid: true } && Equals(GuidedActivityGoal, goal) &&
        Targets.Count == 1 &&
        string.Equals(Targets[0].ItemId,
            goal.ResultGid.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal) &&
        Targets[0].Quantity == goal.Quantity &&
        !DemoMode;

    /// <summary>
    /// Actual production-action port asks this BEFORE and AFTER each await.
    /// Only the V9 journal's exact active LocalRunId can keep an in-progress
    /// V12 workflow alive. A local UI token never becomes a server receipt.
    /// </summary>
    public bool GuidedWorkflowMayBind(GuidedActivityProductionGoal goal)
    {
        if (!GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string profile, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId) return false;
        var flow = _guidedWorkflow.Read(server, character);
        if (flow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(goal))
            return false;
        if (_activeGuidedLocalRun == Guid.Empty)
            return flow.Phase == GuidedProductionWorkflowPhase.Ready;
        return GuidedWorkflowMayContinue(goal);
    }

    /// <summary>
    /// Mutable actions are not admitted by Ready alone. Their *actual* V9
    /// durable Started lease must be bound to the same V12 workflow and run.
    /// Unlike read-only preflight, an external caller holding a factory-created
    /// adapter cannot bypass durable admission by invoking Buy/Craft directly.
    /// </summary>
    public bool GuidedWorkflowMayContinue(GuidedActivityProductionGoal goal)
    {
        if (_activeGuidedLocalRun == Guid.Empty ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string profile, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
            return false;
        var flow = _guidedWorkflow.Read(server, character);
        var attempt = _productionAttempts.ReadStatus(profile);
        return flow.GoalHash == GuidedProductionWorkflowLedger.Fingerprint(goal) &&
            flow.Phase == GuidedProductionWorkflowPhase.InProgress &&
            flow.LocalRunId == _activeGuidedLocalRun &&
            attempt.Phase == ItemCraftAttemptPhase.Started &&
            attempt.LocalRunId == flow.LocalRunId &&
            attempt.RequestHash == flow.RequestHash;
    }

    /// <summary>
    /// The real VM command uses the same recipe/budget/stock observation and
    /// the actual ItemCraftProductionRun.Preflight as the Lancer command.
    /// No gameplay request is sent; preflight merely makes the explicit
    /// durable workflow Ready/Blocked for the selected owner.
    /// </summary>
    [RelayCommand]
    private void InspectGuidedStockPreflight()
    {
        GuidedObservedIngredients.Clear();
        GuidedSupplyStages.Clear();
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        _lastGuidedSupplyStock = null;
        _lastGuidedSupplyFingerprint = null;
        _lastGuidedSupplyPlan = null;
        _lastGuidedSupplyCheckpointPresent = false;
        if (GuidedActivityGoal is not { } goal ||
            !GuidedActivityTargetStillMatches(goal))
        {
            GuidedWorkflowStatus = "BLOQUÉ · objectif absent ou cible modifiée.";
            return;
        }
        if (ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string profile,
                out _, out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
        {
            GuidedWorkflowStatus = "BLOQUÉ · personnage ou serveur modifié.";
            return;
        }
        var activity = new ActivityRecoveryDurableStore(
            ActivityRecoveryDurableStore.DefaultRoot())
            .ReadStatus(new ActivityRecoveryScope(server, character));
        if (activity.State != ActivityRecoveryDurableState.Clear)
        {
            GuidedWorkflowStatus = "ATTENTE OBSERVATION · activité " + activity.State +
                " ; aucun nouvel ordre ni tentative de production.";
            GuidedSupplyActionStatus =
                "REPRISE DURABLE · " + activity.Detail +
                " ; contrôler le segment depuis le panneau Lua. " +
                "Aucun bouton ne réarme ni ne nettoie ce journal.";
            GuidedStockStatus =
                "Stock non ré-admis tant que ActivityRecovery = " + activity.State +
                ". Inventaire local et changement de cellule ne constituent pas un reçu.";
            GuidedSupplyStages.Add(new("REPRISE", goal.ResultGid, goal.Quantity,
                "Attente de rapprochement du segment " +
                (activity.Segment?.Kind.ToString() ?? "inconnu") +
                " ; relire ensuite stock/recette/source à la main",
                "QUARANTAINE " + activity.State));
            return;
        }
        var oldAttempt = _productionAttempts.ReadStatus(profile);
        if (oldAttempt.Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
        {
            GuidedWorkflowStatus =
                "INCERTAIN · tentative V9 durable non résolue ; pas de deuxième production.";
            GuidedSupplyActionStatus =
                "Atelier/HDV : inspecter les reçus exacts dans les journaux, " +
                "sans nouvelle fabrication, retrait, achat ni listing.";
            GuidedSupplyStages.Add(new("ATELIER", goal.ResultGid, goal.Quantity,
                "Tentative V9 " + oldAttempt.Phase +
                " : inspection des reçus attribuables requise",
                "BLOQUÉ SANS REPLAY"));
            return;
        }
        if (!BeginProduction(out var request, out var actions, previewOnly: true)) return;
        if (!GuidedActivityTargetStillMatches(goal)) return;
        var bounded = new ItemCraftSessionBoundActions(actions,
            () => ProductionCharacter?.Invoke() == pid &&
                  Stores.Launch.ServerIdOf(pid) == server &&
                  Stores.Launch.CharacterId(pid) == character &&
                  GuidedActivityTargetStillMatches(goal) &&
                  new ActivityRecoveryDurableStore(
                      ActivityRecoveryDurableStore.DefaultRoot())
                      .ReadStatus(new ActivityRecoveryScope(server, character)).State ==
                      ActivityRecoveryDurableState.Clear);
        GuidedProductionStockPreflight stock;
        try { stock = GuidedProductionStockPlanner.Evaluate(request, bounded); }
        catch (Exception ex)
        {
            GuidedWorkflowStatus = "BLOQUÉ · lecture de stock interrompue (" +
                ex.GetType().Name + ") ; aucune action envoyée.";
            return;
        }
        string? productionIssue;
        try { productionIssue = stock.Valid ? _production.Preflight(request, bounded) : stock.Reason; }
        catch (Exception e) { productionIssue = "préflight invalide (" + e.GetType().Name + ")"; }
        bool ownerCurrent = !bounded.IsRevoked &&
            ProductionCharacter?.Invoke() == pid &&
            Stores.Launch.ServerIdOf(pid) == server &&
            Stores.Launch.CharacterId(pid) == character &&
            GuidedActivityTargetStillMatches(goal) &&
            new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character)).State ==
                    ActivityRecoveryDurableState.Clear &&
            _productionAttempts.ReadStatus(profile).Phase is not
                (ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain);
        long currentMap = Stores.Launch.CurrentMap(pid) ?? 0;
        var world = Stores.World.Get(pid);
        bool podsObserved = currentMap > 0 && world.SelfReadyFor(currentMap) &&
            world.CellsReadyFor(currentMap) && world.MapGeneration > 0 &&
            world.PodsAuthoritative && world.Pods.Max > 0;
        int pods = world.PodsPercent;
        bool podsAtLimit = podsObserved && pods >= goal.MaxPods;
        GuidedSupplyPlanV13? plan = null;
        if (ownerCurrent && actions is GuidedProductionReadOnlyActions preview)
        {
            try
            {
                plan = GuidedSupplyPlannerV13.PrepareAndCheckpoint(goal, request,
                    preview, activity, oldAttempt, _guidedWorkflow,
                    explicitPreviewCheckpoint: true);
            }
            catch (Exception ex)
            {
                GuidedWorkflowStatus = "BLOQUÉ · approvisionnement V13 impossible (" +
                    ex.GetType().Name + ") ; aucun ordre autorisé.";
                return;
            }
        }
        bool existingCheckpoint = plan is { EvidenceValid: true } &&
            GuidedSupplyPlannerV13.MatchesExistingReadOnlyCheckpoint(plan,
                _guidedWorkflow.Read(server, character));
        bool checkpointPresent = plan?.CheckpointWritten == true || existingCheckpoint;
        // A V12-only local quantity projection may be displayed, but MUST NOT
        // unlock production without a V13 source+revision checkpoint.
        foreach (var row in plan is { EvidenceValid: true }
            ? plan.Rows.Select(r => new GuidedProductionIngredientObservation(
                r.Gid, r.Required, r.BagObserved, r.ChestObserved,
                Math.Max(0, r.Required - r.BagObserved),
                r.BuyCandidate + r.GatherDeficit, r.BuyAllowed))
            : stock.Ingredients)
            GuidedObservedIngredients.Add(row);
        bool ready = ownerCurrent && stock.Valid && !stock.NeedsGather &&
            !podsAtLimit &&
            checkpointPresent && plan is { MayOfferExistingRunner: true } &&
            productionIssue is null;
        string detail = !ownerCurrent ? "session perdue pendant la pré-vérification" :
            productionIssue is not null ? productionIssue :
            podsAtLimit ? "pods au seuil : dépôt non attesté, reprise manuelle nécessaire" :
            !checkpointPresent ?
                "checkpoint V13 refusé : " + (plan?.Reason ?? "source de stock non observée") :
            stock.NeedsGather ? "ressources manquantes : itinéraire/récolte manuelle sous contrôle Recovery" :
            "préflight local admis ; runner V9 contrôle à nouveau toutes les actions";
        string hash = ItemCraftProductionAttemptJournal.RequestFingerprint(request);
        if (!checkpointPresent ||
            !_guidedWorkflow.TryNotePreflight(goal, hash, ready, detail))
        {
            GuidedWorkflowStatus =
                "BLOQUÉ · étape durable in-progress/incertaine ou inaccessible ; aucune réémission.";
            return;
        }
        _lastGuidedSupplyStock = stock;
        _lastGuidedSupplyFingerprint = hash;
        _lastGuidedSupplyPlan = plan;
        _lastGuidedSupplyCheckpointPresent = checkpointPresent;
        var gatherRows = plan.Rows.Where(r => r.GatherDeficit > 0).ToArray();
        if (gatherRows.Length == 1)
            GuidedSupplyGatherItemGidText = gatherRows[0].Gid.ToString(CultureInfo.InvariantCulture);
        else if (gatherRows.Length == 0)
            GuidedSupplyGatherItemGidText = "";
        GuidedSupplyStages.Add(new("PODS", 0, 0,
            podsObserved ? $"{pods} % observés / limite {goal.MaxPods} %" :
                "charge non attestée sur une carte courante",
            podsAtLimit ? "BLOQUÉ : banque/pods sous contrôle manuel" :
                podsObserved ? "Observation locale" : "ATTENTE OBSERVATION"));
        foreach (var row in plan.Rows)
        {
            if (row.TakeFromBank > 0)
                GuidedSupplyStages.Add(new("COFFRE", row.Gid,
                    row.TakeFromBank,
                    "Retrait seulement dans ItemCraftProductionRun V9 avec opt-in et contrôles de stock",
                    request.BankAllowed ? "ATTEND RUN V9" : "COFFRE INTERDIT"));
            if (row.BuyCandidate > 0)
                GuidedSupplyStages.Add(new("ACHAT", row.Gid, row.BuyCandidate,
                    $"Budget {request.MaxKamas} kamas ; confirmation/UID via le runner et HDV journalisé",
                    "ATTEND RUN V9"));
            if (row.GatherDeficit > 0)
                GuidedSupplyStages.Add(new("RÉCOLTE", row.Gid, row.GatherDeficit,
                    goal.Gather ? "Préparer un trajet one-shot, observer ensuite le stock" :
                        "Récolte non autorisée par l'objectif",
                    goal.Gather ? "NAVIGATION À PRÉPARER" : "BLOQUÉ"));
        }
        GuidedSupplyStages.Add(new("ATELIER", request.Recipe.ResultGid,
            request.Quantity, "Vrai runner V9, reçu exact GID/quantité et consommation vérifiée",
            ready ? "PRÊT POUR LANCER" : "ATTENTE DES PRÉREQUIS"));
        if (request.SaleFloor is long floor)
            GuidedSupplyStages.Add(new("LISTING", request.Recipe.ResultGid,
                request.Quantity, $"Prix plancher explicite {floor} ; journal HDV, pas de replay",
                "APRÈS REÇUS CRAFT EXACTS"));
        GuidedSupplyActionStatus = stock.NeedsGather
            ? goal.Gather
                ? "Récolte requise : préparer la route one-shot depuis l'atelier, confirmer puis lancer explicitement depuis Lua."
                : "Récolte requise mais non autorisée par l'objectif. Aucun trajet automatique."
            : ready ? "Stock pré-vérifié : Lancer garde le journal V9 et toutes les preuves exactes."
                : "Approvisionnement suspendu : inspecter le motif et ne pas réémettre.";
        GuidedStockStatus =
            $"Ingrédients vérifiés localement : {stock.Ingredients.Count}, " +
            $"manquants après coffre autorisé : {stock.MissingCount}. " +
            $"Coffre : {(stock.NeedsChest ? "à confirmer au runner" : "sans retrait prévu")}, " +
            $"achat : {(stock.NeedsBuyer ? "selon opt-in" : "non requis")}, " +
            $"récolte : {(stock.NeedsGather ? "requise, pas de preuve encore" : "non requise")}. " +
            "Aucun de ces comptes n'est un reçu serveur.";
        GuidedWorkflowStatus = (ready ? "PRÊT" : "BLOQUÉ") +
            " · " + detail + ". Journal V12 mis à jour sans émission réseau.";
    }

    /// <summary>
    /// Real V13 atelier→Lua entry: re-runs V12 source/stock preflight and
    /// requests the SAME strict Lua route command as the existing Bots panel.
    /// Never dispatches a movement, harvest or purchase by itself.
    /// </summary>
    [RelayCommand]
    private void PrepareGuidedSupplyRoute()
    {
        if (IsProductionRunning || GuidedActivityGoal is not { } goal || !goal.Gather)
        {
            GuidedSupplyActionStatus =
                "BLOQUÉ : objectif récolte explicite absent ou production déjà en cours.";
            return;
        }
        if (!_lastGuidedSupplyCheckpointPresent ||
            _lastGuidedSupplyPlan is not { GatherNeeded: true } plan ||
            _lastGuidedSupplyFingerprint is not { } hash ||
            !long.TryParse(GuidedSupplyGatherItemGidText, NumberStyles.None,
                CultureInfo.InvariantCulture, out long gatherGid) ||
            gatherGid is <= 0 or > int.MaxValue ||
            !plan.Rows.Any(r => r.Gid == gatherGid && r.GatherDeficit > 0 && !r.BuyAllowed) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !GuidedActivityTargetStillMatches(goal) ||
            !TryResolveProductionIdentity(pid, out _, out _, out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
        {
            GuidedSupplyActionStatus =
                "BLOQUÉ : déficit récoltable ou source non revérifiés. Aucun itinéraire préparé.";
            return;
        }
        var deficit = plan.Rows.Single(r => r.Gid == gatherGid);
        var ticket = new GuidedSupplyRoutePreparation(pid, goal, hash, gatherGid,
            deficit.Required, plan.PlanHash, plan.SourceKey,
            plan.SessionKey, plan.ObservationRevision);
        if (!GuidedSupplyMayPrepareRoute(ticket))
        {
            GuidedSupplyActionStatus =
                "BLOQUÉ : reprise incertaine, but ou recette changé ; aucune émission ni réarmement.";
            return;
        }
        GuidedSupplyActionStatus =
            "Ressource GID " + gatherGid +
            " : préparation d'une récolte filtrée one-shot ; confirmation manuelle exigée.";
        GuidedSupplyRoutePreparationRequested?.Invoke(ticket);
    }

    /// <summary>
    /// Recheck the exact staged deficit and live owner before the Lua editor
    /// confirmation. The source run/actor and the input GID must still match;
    /// never reselect a different resource by its map-interactive type ID.
    /// </summary>
    public bool GuidedSupplyMayPrepareRoute(GuidedSupplyRoutePreparation request)
    {
        if (IsProductionRunning || !request.Goal.Gather ||
            !GuidedActivityTargetStillMatches(request.Goal) ||
            ProductionCharacter?.Invoke() != request.Pid ||
            request.GatherItemGid is <= 0 or > int.MaxValue ||
            request.Required <= 0 || request.InventoryRevision <= 0) return false;
        var workflow = _guidedWorkflow.Read(request.Goal.ServerId,
            request.Goal.CharacterId);
        if (workflow.Phase != GuidedProductionWorkflowPhase.Blocked ||
            workflow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(request.Goal) ||
            workflow.RequestHash != request.RequestHash ||
            workflow.SupplyPlanHash.Length == 0 ||
            !TryInspectCurrentSupplyPlan(request.Goal, workflow, out var current) ||
            current is not { EvidenceValid: true, GatherNeeded: true } ||
            !current.Rows.Any(r => r.Gid == request.GatherItemGid &&
                r.Required == request.Required &&
                r.GatherDeficit > 0 && !r.BuyAllowed) ||
            current.PlanHash != request.PlanHash ||
            current.SourceKey != request.SourceKey ||
            current.SessionKey != request.SessionKey ||
            current.ObservationRevision != request.InventoryRevision ||
            current.PlanHash != workflow.SupplyPlanHash ||
            current.SourceKey != workflow.SupplySourceKey ||
            current.SessionKey != workflow.SupplySessionKey ||
            current.ObservationRevision != workflow.SupplyObservationRevision)
            return false;
        return true;
    }

    /// <summary>Pure V13 replay-safe read, never writes or re-arms a checkpoint.</summary>
    private bool TryInspectCurrentSupplyPlan(GuidedActivityProductionGoal goal,
        GuidedProductionWorkflowSnapshot persisted, out GuidedSupplyPlanV13? observed)
    {
        observed = null;
        try
        {
            if (ProductionCharacter?.Invoke() is not uint pid ||
                !TryResolveProductionIdentity(pid, out string profile, out _,
                    out int server, out long character) ||
                server != goal.ServerId || character != goal.CharacterId ||
                !BeginProduction(out var request, out var actions, previewOnly: true) ||
                actions is not GuidedProductionReadOnlyActions readonlyActions ||
                !GuidedActivityTargetStillMatches(goal)) return false;
            var activity = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character));
            observed = GuidedSupplyPlannerV13.Evaluate(goal, request,
                readonlyActions, activity, _productionAttempts.ReadStatus(profile), persisted);
            return observed is { EvidenceValid: true } &&
                !readonlyActions.IsRevoked && GuidedActivityTargetStillMatches(goal) &&
                ProductionCharacter?.Invoke() == pid;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Called by the actual Lancer entry before V9 Started is fsynced. A new
    /// inventory revision, reconnection or map source means a new explicit
    /// preflight is required; it cannot authorize an old checkpoint.
    /// </summary>
    private bool GuidedSupplyCheckpointStillCurrent(GuidedActivityProductionGoal goal,
        ItemCraftProductionRequest request)
    {
        var persisted = _guidedWorkflow.Read(goal.ServerId, goal.CharacterId);
        if (persisted.Phase != GuidedProductionWorkflowPhase.Ready ||
            persisted.SupplyPlanHash.Length == 0 ||
            !TryInspectCurrentSupplyPlan(goal, persisted, out var observed) ||
            observed is not { MayOfferExistingRunner: true } ||
            observed.RequestHash != ItemCraftProductionAttemptJournal.RequestFingerprint(request) ||
            observed.PlanHash != persisted.SupplyPlanHash ||
            observed.SourceKey != persisted.SupplySourceKey ||
            observed.SessionKey != persisted.SupplySessionKey ||
            observed.ObservationRevision != persisted.SupplyObservationRevision)
            return false;
        return true;
    }

    /// <summary>
    /// Re-read the REAL durable goal, V9 attempt, recovery marker and V13
    /// source-bound preview. The pending-activity branch NEVER calls a stock
    /// or recipe preview; a post-gather local inventory delta cannot clear it.
    /// </summary>
    private GuidedSupplyContinuationV15 ReadGuidedSupplyContinuation()
    {
        static GuidedSupplyContinuationV15 Deny(string reason) =>
            new(GuidedSupplyNextStepV15.Blocked, reason, 0, 0, "", "", 0, false);
        if (IsProductionRunning || GuidedActivityGoal is not { } goal ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string profile, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
            return Deny("objectif, session ou personnage non attribuable ; aucun envoi");
        try
        {
            var saved = new GuidedActivityProductionStore(
                GuidedActivityProductionStore.DefaultRoot()).Read(server, character);
            if (saved.State != GuidedActivityProductionState.Ready ||
                !Equals(saved.Goal, goal))
                return Deny("objectif durable absent/corrompu ou remplacé ; réimporter sans effacer le journal");
            var recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character));
            var workflow = _guidedWorkflow.Read(server, character);
            var attempt = _productionAttempts.ReadStatus(profile);
            GuidedSupplyPlanV13? observed = null;
            if (recovery.State == ActivityRecoveryDurableState.Clear &&
                attempt.Phase is not (ItemCraftAttemptPhase.Started or
                    ItemCraftAttemptPhase.Uncertain) &&
                workflow.Phase is GuidedProductionWorkflowPhase.Ready or
                    GuidedProductionWorkflowPhase.Blocked)
            {
                if (TryInspectCurrentSupplyPlan(goal, workflow, out var evaluated))
                    observed = evaluated;
            }
            if (ProductionCharacter?.Invoke() != pid ||
                Stores.Launch.ServerIdOf(pid) != server ||
                Stores.Launch.CharacterId(pid) != character ||
                !GuidedActivityTargetStillMatches(goal))
                return Deny("source remplacée pendant les lectures ; aucun nouvel ordre");
            // Re-read the immutable durable roots to detect a racing Stop,
            // write-ahead marker or workflow change during our own inspection.
            var recoveryAfter = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character));
            var workflowAfter = _guidedWorkflow.Read(server, character);
            var attemptAfter = _productionAttempts.ReadStatus(profile);
            if (recovery != recoveryAfter || workflow != workflowAfter ||
                attempt != attemptAfter)
                return Deny("journaux changés pendant la consultation ; réinspecter, jamais rejouer");
            return GuidedSupplyContinuationPlannerV15.Inspect(goal, workflow,
                recovery, attempt, observed, sameObservedOwner: true);
        }
        catch (Exception ex)
        {
            return Deny("lecture des preuves interrompue (" + ex.GetType().Name +
                ") ; aucune nouvelle action admise");
        }
    }

    private GuidedSupplyContinuationV15 DisplayGuidedSupplyContinuation()
    {
        var step = ReadGuidedSupplyContinuation();
        GuidedSupplyContinuationStatus = "V15 · " + step.NextStep + " · " +
            step.Reason + ". Aucun ACK serveur attribué par cette lecture.";
        for (int i = GuidedSupplyStages.Count - 1; i >= 0; i--)
            if (GuidedSupplyStages[i].Stage == "SUITE V15")
                GuidedSupplyStages.RemoveAt(i);
        GuidedSupplyStages.Add(new("SUITE V15", step.Gid, step.Quantity,
            step.Reason, step.NextStep.ToString().ToUpperInvariant()));
        return step;
    }

    [RelayCommand]
    private void InspectGuidedSupplyContinuation() =>
        _ = DisplayGuidedSupplyContinuation();

    /// <summary>
    /// Operator-controlled return from the existing bank, Lua or HDV panel.
    /// The actual V9 preflight and V13 checkpoint are re-read before exposing
    /// the exact next candidates; this never resumes an earlier send, accepts
    /// an inventory delta as ACK or consumes a one-shot instruction.
    /// </summary>
    [RelayCommand]
    private void ReinspectGuidedSupplyAfterExternalAction()
    {
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        if (IsProductionRunning || GuidedActivityGoal is not { } goal ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !GuidedActivityTargetStillMatches(goal))
        {
            GuidedSupplyContinuationStatus =
                "V18 · Réinspection refusée : production active ou objectif changé. Aucun envoi.";
            return;
        }
        var sourceBefore = Stores.Launch.ReadGuidedSupplySource(pid);
        if (sourceBefore is null ||
            Stores.Launch.ServerIdOf(pid) != goal.ServerId ||
            Stores.Launch.CharacterId(pid) != goal.CharacterId)
        {
            GuidedSupplyContinuationStatus =
                "V18 · Source liée au personnage absente : inspection des journaux " +
                "uniquement, aucune instruction réutilisée.";
            return;
        }

        var before = ReadGuidedSupplyContinuation();
        if (before.NextStep is GuidedSupplyNextStepV15.InspectActivityRecovery or
            GuidedSupplyNextStepV15.InspectProductionAttempt)
        {
            // The durable marker is not cleared by inspection, bag change or
            // a human review. Direct the operator to the existing journal UI.
            GuidedSupplyContinuationStatus =
                "V18 · Reprise en quarantaine : " + before.Reason +
                ". Utiliser « Préparer étape suivante » pour inspecter le journal, " +
                "sans nouveau préflight ni tentative.";
            return;
        }
        if (before.NextStep == GuidedSupplyNextStepV15.InspectCompletedHdvListing)
        {
            // A completed V9 run must never be sent back into stock preflight.
            // Its only next step is the original listing/journal inspection.
            InspectGuidedHdvListing();
            GuidedSupplyContinuationStatus =
                "V18 · Run V9 rapproché : inspection du listing d'origine demandée, " +
                "sans nouveau préflight, annonce ou taxe libérée.";
            return;
        }
        if (before.NextStep == GuidedSupplyNextStepV15.Blocked)
        {
            GuidedSupplyContinuationStatus =
                "V18 · Réinspection suspendue : " + before.Reason +
                ". Examiner l'identité et les journaux avant toute action.";
            return;
        }

        InspectGuidedStockPreflight(); // actual existing stock/recipe/V9/V13 port
        if (ProductionCharacter?.Invoke() != pid ||
            !GuidedActivityTargetStillMatches(goal) ||
            Stores.Launch.ReadGuidedSupplySource(pid) != sourceBefore ||
            !_lastGuidedSupplyCheckpointPresent)
        {
            GuidedSupplyContinuationStatus =
                "V18 · Nouveau checkpoint indisponible ou source remplacée. " +
                "Aucun choix ou ancien ordre réarmé ; inspecter le motif du stock.";
            return;
        }
        InspectGuidedV16ExactChoices(); // durable current revision, no selection persisted
        if (ProductionCharacter?.Invoke() != pid ||
            !GuidedActivityTargetStillMatches(goal) ||
            Stores.Launch.ReadGuidedSupplySource(pid) != sourceBefore)
        {
            GuidedV16ExactChoices.Clear();
            SelectedGuidedV16ExactChoice = null;
            GuidedSupplyContinuationStatus =
                "V18 · Source modifiée après lecture : candidats retirés, aucun envoi.";
            return;
        }
        var next = DisplayGuidedSupplyContinuation();
        if (ProductionCharacter?.Invoke() != pid ||
            !GuidedActivityTargetStillMatches(goal) ||
            Stores.Launch.ReadGuidedSupplySource(pid) != sourceBefore)
        {
            GuidedV16ExactChoices.Clear();
            SelectedGuidedV16ExactChoice = null;
            GuidedSupplyContinuationStatus =
                "V18 · Source révoquée pendant le bilan final : " +
                "aucun candidat conservé ni envoi admis.";
            return;
        }
        GuidedSupplyContinuationStatus =
            "V18 · Réinspection opérateur GID/stock terminée : " + next.NextStep +
            " · " + GuidedV16ExactChoices.Count + " choix exact(s) proposés, " +
            "sans les valider. " + next.Reason;
    }

    /// <summary>
    /// Reuses the real V13 preview port and the existing four durable roots.
    /// No second planner, inventory seed, synthetic journal or gameplay sender.
    /// </summary>
    private bool TryReadGuidedV16Resume(
        out GuidedSupplyResumeV16? resume,
        out ItemCraftProductionRequest? request,
        out GuidedProductionReadOnlyActions? previewActions,
        out string profile)
    {
        resume = null;
        request = null;
        previewActions = null;
        profile = "";
        if (IsProductionRunning || GuidedActivityGoal is not { } goal ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out profile, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
            return false;
        // Even an inspection must not initialize a new gameplay adapter if a
        // possible prior harvest escaped. The operator can inspect its marker
        // through the separate Lua command without modifying this workflow.
        var recovery = new ActivityRecoveryDurableStore(
            ActivityRecoveryDurableStore.DefaultRoot());
        if (recovery.ReadStatus(new ActivityRecoveryScope(server, character)).State !=
            ActivityRecoveryDurableState.Clear)
            return false;
        if (!BeginProduction(out var currentRequest, out var port, previewOnly: true) ||
            port is not GuidedProductionReadOnlyActions observed)
            return false;
        try
        {
            var store = new GuidedActivityProductionStore(
                GuidedActivityProductionStore.DefaultRoot());
            var inspected = GuidedSupplyResumePlannerV16.InspectDurable(
                goal, currentRequest, observed,
                store,
                _guidedWorkflow, recovery, _productionAttempts, profile);
            // V17 reuses the SAME V12/V13 journal. After a non-emitting bank
            // or HDV inspection, expose remaining precise choices rather
            // than freezing the operator at the first durable consultation.
            if (inspected is { Step: GuidedSupplyResumeStepV16.SelectionRecorded,
                    RecordedChoice: { } earlier } &&
                GuidedProductionWorkflowLedger.IsConsultation(earlier.Kind))
            {
                inspected = GuidedSupplyResumePlannerV16.InspectNextAdmissionV17(
                    goal, currentRequest, observed, store, _guidedWorkflow,
                    recovery, _productionAttempts, profile);
            }
            if (ProductionCharacter?.Invoke() != pid ||
                Stores.Launch.ServerIdOf(pid) != server ||
                Stores.Launch.CharacterId(pid) != character ||
                !GuidedActivityTargetStillMatches(goal) ||
                observed.IsRevoked)
                return false;
            resume = inspected;
            request = currentRequest;
            previewActions = observed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [RelayCommand]
    private void InspectGuidedV16ExactChoices()
    {
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        if (TryReadGuidedV16Resume(out var resume, out _, out _, out _) &&
            resume is not null)
        {
            foreach (var candidate in resume.Candidates)
                GuidedV16ExactChoices.Add(candidate);
            SelectedGuidedV16ExactChoice = GuidedV16ExactChoices.FirstOrDefault();
            GuidedSupplyV16ChoiceStatus =
                "V16 · " + resume.Step + " · " + resume.Reason +
                " · révision " + resume.ObservationRevision +
                (resume.RecordedChoice is { } recorded
                    ? " · choix durable : " + recorded.Kind + " / GID " +
                      recorded.Gid + " ×" + recorded.Quantity
                    : " · " + resume.Candidates.Count + " instruction(s) GID sélectionnable(s)") +
                ". Aucun ACK, replay ni droit d'envoi n'est créé.";
        }
        else
        {
            GuidedSupplyV16ChoiceStatus =
                "V16 · Objectif/source/reprise non admissibles à une sélection : " +
                "inspecter les journaux, puis vérifier le stock. Aucun nouvel ordre.";
        }
    }

    /// <summary>
    /// Operator chooses ONE source/revision-bound instruction. The service
    /// atomically persists the choice in the EXISTING workflow checksum ledger
    /// and re-inspects before confirming. Only existing panel commands may
    /// subsequently prepare an action; no dispatch occurs in this method.
    /// </summary>
    [RelayCommand]
    private void ChooseGuidedV16ExactStep()
    {
        var selected = SelectedGuidedV16ExactChoice;
        if (selected is not { Valid: true } ||
            GuidedActivityGoal is not { } goal ||
            !TryReadGuidedV16Resume(out var before, out var request,
                out var port, out string profile) ||
            before is null || request is null || port is null ||
            !before.Candidates.Contains(selected) ||
            !(before.RecordedChoice is { } earlier &&
              GuidedProductionWorkflowLedger.IsConsultation(earlier.Kind)
                ? GuidedSupplyResumePlannerV16.TryChooseNextOnceV17(
                    goal, request, port,
                    new GuidedActivityProductionStore(
                        GuidedActivityProductionStore.DefaultRoot()),
                    _guidedWorkflow,
                    new ActivityRecoveryDurableStore(
                        ActivityRecoveryDurableStore.DefaultRoot()),
                    _productionAttempts, profile, selected)
                : GuidedSupplyResumePlannerV16.TryChooseOnce(
                    goal, request, port,
                    new GuidedActivityProductionStore(
                        GuidedActivityProductionStore.DefaultRoot()),
                    _guidedWorkflow,
                    new ActivityRecoveryDurableStore(
                        ActivityRecoveryDurableStore.DefaultRoot()),
                    _productionAttempts, profile, selected)))
        {
            GuidedSupplyV16ChoiceStatus =
                "V16 · Choix exact refusé : GID, plan, source ou checkpoint remplacé, " +
                "ou choix déjà consommé. Réinspecter les preuves, aucun replay.";
            return;
        }
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        GuidedSupplyV16ChoiceStatus =
            "V16 · Choix unique fsync : " + selected.Kind +
            " · GID " + selected.Gid + " ×" + selected.Quantity +
            " · révision " + selected.ObservationRevision +
            ". La preuve d'intention ne constitue pas un reçu serveur.";
        if (selected.Kind == GuidedSupplyResumeStepV16.PrepareExactGatherRoute)
        {
            GuidedSupplyGatherItemGidText =
                selected.Gid.ToString(CultureInfo.InvariantCulture);
            PrepareGuidedSupplyRoute(); // same strict Lua modal and final guards
        }
        else if (selected.Kind == GuidedSupplyResumeStepV16.InspectBank)
            InspectGuidedBankSupply(); // existing UI, no implicit withdrawal
        else if (selected.Kind == GuidedSupplyResumeStepV16.InspectBuy)
            StageGuidedV17ExactHdvBuy(selected); // real buy-list handoff, no implicit buy
        else if (selected.Kind == GuidedSupplyResumeStepV16.UseExistingV9Runner)
            GuidedSupplyV16RunnerStatus =
                "V16 · Choix V9 conservé dans le journal. Pour une émission, " +
                "cocher l'opt-in ponctuel et utiliser « Exécuter étape prête via V9 ».";
    }

    /// <summary>
    /// After a UI restart, the choice survives in the SAME V12 journal.
    /// Re-inspection requires the original source/session/stock revision and
    /// the exact candidate, never just a remembered GID. A prior send marker
    /// keeps this branch blocked until genuine independent reconciliation.
    /// </summary>
    [RelayCommand]
    private void ResumeGuidedV16RecordedChoice()
    {
        if (!TryReadGuidedV16Resume(out var status, out _, out _, out _) ||
            status is not { RecordedChoice: { Valid: true } recorded } ||
            !string.Equals(recorded.PlanHash, status.PlanHash,
                StringComparison.Ordinal) ||
            !string.Equals(recorded.SourceKey, status.SourceKey,
                StringComparison.Ordinal) ||
            !string.Equals(recorded.SessionKey, status.SessionKey,
                StringComparison.Ordinal) ||
            recorded.ObservationRevision != status.ObservationRevision)
        {
            GuidedSupplyV16ChoiceStatus =
                "V16 · Choix de session/révision absent ou périmé : " +
                "inspection et nouveau checkpoint nécessaires, aucun replay.";
            return;
        }
        GuidedSupplyV16ChoiceStatus =
            "V16 · Reprise consultative du choix durable " + recorded.Kind +
            " GID " + recorded.Gid + " ×" + recorded.Quantity +
            " ; chaque commande réelle garde son propre préflight.";
        switch (recorded.Kind)
        {
            case GuidedSupplyResumeStepV16.PrepareExactGatherRoute:
                // Preflight only restores the current VM cache; ledger's
                // one-shot choice is retained when the revision is identical.
                if (!_lastGuidedSupplyCheckpointPresent ||
                    _lastGuidedSupplyPlan?.PlanHash != recorded.PlanHash)
                    InspectGuidedStockPreflight();
                if (_lastGuidedSupplyCheckpointPresent &&
                    _lastGuidedSupplyPlan?.PlanHash == recorded.PlanHash)
                {
                    GuidedSupplyGatherItemGidText =
                        recorded.Gid.ToString(CultureInfo.InvariantCulture);
                    PrepareGuidedSupplyRoute();
                }
                else
                    GuidedSupplyV16ChoiceStatus =
                        "V16 · Préflight source changé pendant la reprise ; " +
                        "aucun brouillon Lua généré.";
                break;
            case GuidedSupplyResumeStepV16.InspectBank:
                InspectGuidedBankSupply();
                break;
            case GuidedSupplyResumeStepV16.InspectBuy:
                StageGuidedV17ExactHdvBuy(recorded);
                break;
            case GuidedSupplyResumeStepV16.UseExistingV9Runner:
                GuidedSupplyV16RunnerStatus =
                    "V16 · Choix V9 relu sous la même révision. " +
                    "Opt-in à renouveler pour la commande d'exécution V9.";
                break;
        }
    }

    /// <summary>
    /// Explicit operator follow-up through EXISTING commands, not a second
    /// production executor. A fresh read precedes every handoff; underlying
    /// Lua/MainWindow/V9 entry points independently revalidate once again.
    /// </summary>
    [RelayCommand]
    private void PrepareGuidedSupplyNextStep()
    {
        var step = DisplayGuidedSupplyContinuation();
        switch (step.NextStep)
        {
            case GuidedSupplyNextStepV15.RefreshObservedStock:
                InspectGuidedStockPreflight();
                GuidedSupplyContinuationStatus =
                    "V15 · Préflight V13 demandé ; vérifier son résultat puis réinspecter la suite, sans valider un reçu local.";
                break;
            case GuidedSupplyNextStepV15.InspectActivityRecovery:
                if (GuidedActivityGoal is { } pendingGoal &&
                    ProductionCharacter?.Invoke() is uint recoveryPid &&
                    GuidedActivityTargetStillMatches(pendingGoal))
                {
                    long selectedGid = 0, required = 0;
                    if (long.TryParse(GuidedSupplyGatherItemGidText,
                            NumberStyles.None, CultureInfo.InvariantCulture, out long itemGid) &&
                        _lastGuidedSupplyPlan?.Rows.FirstOrDefault(r =>
                            r.Gid == itemGid && r.GatherDeficit > 0) is { } row)
                    {
                        selectedGid = row.Gid;
                        required = row.Required;
                    }
                    else
                    {
                        // Restart may have discarded the VM cache, while the
                        // operator's one-shot GID instruction remains anchored
                        // in the V12 journal. Use it ONLY as an inspection
                        // subject, NEVER as evidence of harvest or admission.
                        var durable = _guidedWorkflow.Read(
                            pendingGoal.ServerId, pendingGoal.CharacterId);
                        if (durable.GoalHash ==
                                GuidedProductionWorkflowLedger.Fingerprint(pendingGoal) &&
                            durable.SupplyChoiceV16 is
                                { Kind: GuidedSupplyResumeStepV16.PrepareExactGatherRoute,
                                  Valid: true } recorded &&
                            recorded.PlanHash == durable.SupplyPlanHash &&
                            recorded.SourceKey == durable.SupplySourceKey &&
                            recorded.SessionKey == durable.SupplySessionKey &&
                            recorded.ObservationRevision ==
                                durable.SupplyObservationRevision)
                        {
                            selectedGid = recorded.Gid;
                            // A persisted deficit is only the operator's
                            // requested amount; without the old bag snapshot,
                            // it cannot become a confirmed total or receipt.
                            required = recorded.Quantity;
                        }
                    }
                    GuidedSupplyRecoveryInspectionRequested?.Invoke(
                        new(recoveryPid, pendingGoal, selectedGid, required));
                }
                break;
            case GuidedSupplyNextStepV15.InspectProductionAttempt:
                RefreshProductionRecoveryCommand.Execute(null);
                InspectGuidedWorkflow();
                GuidedSupplyContinuationStatus =
                    "V15 · Journaux V9/V12 consultés dans l'atelier. " +
                    "Tentative incertaine : vérifier les reçus exacts avant toute reprise.";
                break;
            case GuidedSupplyNextStepV15.PrepareExactGatherRoute:
                if (!long.TryParse(GuidedSupplyGatherItemGidText, NumberStyles.None,
                        CultureInfo.InvariantCulture, out long selected) ||
                    selected != step.Gid)
                {
                    GuidedSupplyContinuationStatus =
                        "V15 · GID choisi différent du seul déficit attesté : " +
                        step.Gid + ". Sélection explicite requise ; aucun trajet armé.";
                    return;
                }
                PrepareGuidedSupplyRoute();
                break;
            case GuidedSupplyNextStepV15.InspectBankAndPods:
                InspectGuidedBankSupply();
                break;
            case GuidedSupplyNextStepV15.InspectCompletedHdvListing:
                InspectGuidedHdvListing();
                break;
            case GuidedSupplyNextStepV15.UseExistingProductionRunner:
                GuidedSupplyContinuationStatus =
                    "V15 · Bouton Lancer existant disponible sur demande distincte. " +
                    "Le runner V9 contrôle à nouveau le lease, les achats, le craft, le listing et les reçus.";
                break;
            case GuidedSupplyNextStepV15.ChooseExactGatherGid:
                if (long.TryParse(GuidedSupplyGatherItemGidText,
                        NumberStyles.None, CultureInfo.InvariantCulture, out long chosen) &&
                    GuidedActivityGoal is { } selectionGoal &&
                    _guidedWorkflow.Read(selectionGoal.ServerId, selectionGoal.CharacterId)
                        is { Phase: GuidedProductionWorkflowPhase.Blocked } blocked &&
                    TryInspectCurrentSupplyPlan(selectionGoal, blocked, out var selectedPlan) &&
                    selectedPlan is { EvidenceValid: true, GatherNeeded: true } &&
                    selectedPlan.Rows.Any(row => row.Gid == chosen &&
                        row.GatherDeficit > 0 && !row.BuyAllowed))
                {
                    // This is still the existing strict route's explicit modal,
                    // not an automatic selection of the first deficient item.
                    PrepareGuidedSupplyRoute();
                }
                else
                    GuidedSupplyContinuationStatus =
                        "V16 · Plusieurs GID manquants : choisir explicitement un " +
                        "GID récoltable du plan V13 ; aucun trajet armé sans preuve.";
                break;
            default:
                GuidedSupplyContinuationStatus =
                    "V15 · " + step.Reason + " ; inspection uniquement. " +
                    "Aucun replay, aucun effacement de journal.";
                break;
        }
    }

    /// <summary>Late Lua inspection callbacks may only update the same actor/goal UI.</summary>
    public void ObserveGuidedSupplyV16Recovery(uint pid,
        GuidedActivityProductionGoal goal, string detail)
    {
        if (ProductionCharacter?.Invoke() != pid ||
            !GuidedActivityTargetStillMatches(goal)) return;
        GuidedSupplyV16RecoveryStatus = detail;
    }

    /// <summary>
    /// Actual V16 next-step handoff: the same RunProduction() and V9 journal
    /// used by the classic Lancer button. The explicit checkbox is a ONE-shot
    /// operator opt-in, consumed before any await, never a receipt or bypass.
    /// Bank and buyer actions are performed only by the existing V9 runner
    /// after its own preflight, fsynced Started lease and per-send guard.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExecuteGuidedSupplyV16Runner()
    {
        if (System.Threading.Interlocked.CompareExchange(
                ref _guidedV16RunnerHandoffInFlight, 1, 0) != 0)
        {
            GuidedSupplyV16RunnerStatus =
                "V16 · Double-clic refusé : passage au runner V9 déjà en cours.";
            return;
        }
        try
        {
            // Consume the opt-in at entry. An error, cancellation or crash
            // cannot silently keep it valid for a second operation.
            bool optedIn = GuidedSupplyV16RunnerOptIn;
            GuidedSupplyV16RunnerOptIn = false;
            if (!optedIn || IsProductionRunning)
            {
                GuidedSupplyV16RunnerStatus =
                    "V16 · Opération refusée : opt-in ponctuel absent ou production déjà active.";
                return;
            }
            var stage = ReadGuidedSupplyContinuation();
            if (stage.NextStep is not (GuidedSupplyNextStepV15.UseExistingProductionRunner or
                    GuidedSupplyNextStepV15.InspectBankAndPods) ||
                GuidedActivityGoal is not { } goal ||
                !GuidedActivityTargetStillMatches(goal) ||
                ProductionCharacter?.Invoke() is not uint pid ||
                !TryResolveProductionIdentity(pid, out string profile, out _,
                    out int server, out long character) ||
                server != goal.ServerId || character != goal.CharacterId ||
                !CanRunProduction())
            {
                GuidedSupplyV16RunnerStatus =
                    "V16 · Suite non admise : vérifier ressources, objectif et quarantaine. Aucun nouvel ordre.";
                return;
            }
            var workflow = _guidedWorkflow.Read(server, character);
            // Bank-panel inspection is not runner admission by itself.
            // A bank-assisted V9 run is eligible ONLY if the SAME persistent
            // checkpoint was independently Ready and really source-bound.
            if (workflow.Phase != GuidedProductionWorkflowPhase.Ready ||
                !TryInspectCurrentSupplyPlan(goal, workflow, out var exactPlan) ||
                exactPlan is not { MayOfferExistingRunner: true } ||
                !GuidedSupplyPlannerV13.MatchesExistingReadOnlyCheckpoint(
                    exactPlan, workflow) ||
                exactPlan.SourceKey != stage.SourceKey ||
                exactPlan.SessionKey != stage.SessionKey ||
                exactPlan.ObservationRevision != stage.InventoryRevision ||
                (stage.NextStep == GuidedSupplyNextStepV15.InspectBankAndPods &&
                 (!exactPlan.BankOptIn ||
                  !exactPlan.Rows.Any(r => r.TakeFromBank > 0))) ||
                !_lastGuidedSupplyCheckpointPresent ||
                _lastGuidedSupplyPlan?.PlanHash != exactPlan.PlanHash ||
                _lastGuidedSupplyFingerprint != workflow.RequestHash ||
                _productionAttempts.ReadStatus(profile).Phase is
                    ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
            {
                GuidedSupplyV16RunnerStatus =
                    "V16 · Le checkpoint Ready/budget/source/banque n'est plus identique. " +
                    "Relire le stock et renouveler l'opt-in ; aucune tentative émise.";
                return;
            }
            GuidedSupplyV16RunnerStatus =
                "V16 · Passage explicite au runner V9 pour GID " + goal.ResultGid +
                " ×" + goal.Quantity + " ; retrait/achat/craft/listing soumis " +
                "aux contrôles de source et reçus de leurs propres frontières.";
            // RunProduction re-reads the entire checkpoint and current decoder
            // generation immediately before fsync and EVERY real mutation is
            // guarded by the existing session-bound V9 action port.
            await RunProduction();
            RefreshProductionRecovery();
            // Re-read read-only, NEVER auto-clear a pending V9/Activity marker.
            var after = ReadGuidedSupplyContinuation();
            GuidedSupplyV16RunnerStatus =
                "V16 · Runner V9 revenu ; " + after.NextStep + " · " +
                after.Reason + ". Vérifier les reçus avant tout nouveau cycle.";
        }
        catch (Exception ex)
        {
            GuidedSupplyV16RunnerStatus =
                "V16 · Handoff interrompu (" + ex.GetType().Name +
                ") ; relire les journaux durables, aucun replay.";
        }
        finally
        {
            System.Threading.Interlocked.Exchange(
                ref _guidedV16RunnerHandoffInFlight, 0);
        }
    }

    [RelayCommand]
    private void InspectGuidedWorkflow()
    {
        if (GuidedActivityGoal is not { } goal)
        {
            GuidedWorkflowStatus = "Aucun objectif guidé actif dans l'atelier.";
            return;
        }
        var activity = new ActivityRecoveryDurableStore(
            ActivityRecoveryDurableStore.DefaultRoot())
            .ReadStatus(new ActivityRecoveryScope(goal.ServerId, goal.CharacterId));
        ItemCraftAttemptSnapshot attempt = new(ItemCraftAttemptPhase.Uncertain,
            "profil de production non observé");
        if (ProductionCharacter?.Invoke() is uint pid &&
            TryResolveProductionIdentity(pid, out string profile, out _,
                out int server, out long character) &&
            server == goal.ServerId && character == goal.CharacterId)
            attempt = _productionAttempts.ReadStatus(profile);
        var flow = _guidedWorkflow.Inspect(goal, activity, attempt);
        GuidedWorkflowStatus = flow.Phase.ToString().ToUpperInvariant() + " · " +
            flow.Reason + ". Run local : " +
            (flow.LocalRunId == Guid.Empty ? "aucun" : flow.LocalRunId.ToString("N")) +
            ". Aucun ACK inféré.";
    }

    /// <summary>
    /// Send the operator to the EXISTING HDV panel and exact mutation journal
    /// for human inspection after craft/listing, including ambiguous outcomes.
    /// This never repeats a listing or certifies a sale from a user decision.
    /// </summary>
    /// <summary>
    /// UI projection of the exact one-shot V14 listing assessment emitted
    /// AFTER the real V9 ListAsync. It does not promote local correlation to
    /// TCP ACK, nor grant another sell or tax release.
    /// </summary>
    private long _guidedV17ListingReadSequence;

    public void ObserveProductionListingAssessment(uint pid,
        HdvProductionListingAssessmentV14 assessment)
    {
        var capturedGoal = GuidedActivityGoal;
        long generation = System.Threading.Volatile.Read(ref _productionUiRevision);
        long readSequence = System.Threading.Interlocked.Increment(
            ref _guidedV17ListingReadSequence);
        void Apply()
        {
            if (capturedGoal is null || !Equals(GuidedActivityGoal, capturedGoal) ||
                ProductionCharacter?.Invoke() != pid ||
                readSequence != System.Threading.Volatile.Read(
                    ref _guidedV17ListingReadSequence) ||
                generation != System.Threading.Volatile.Read(ref _productionUiRevision))
                return;
            GuidedSupplyListingEvidenceStatus =
                (assessment.HasLocalSaleCorrelation
                    ? "LISTING RAPPROCHÉ LOCALEMENT"
                    : "LISTING EN ATTENTE DE PREUVES") +
                " · " + assessment.Outcome +
                (assessment.ListingUid is int uid ? " · UID " + uid : "") +
                " · " + assessment.Reason +
                ". Aucune attribution TCP/ACK ni libération fiscale.";
            for (int i = GuidedSupplyStages.Count - 1; i >= 0; i--)
                if (GuidedSupplyStages[i].Stage == "HDV V14")
                    GuidedSupplyStages.RemoveAt(i);
            GuidedSupplyStages.Add(new("HDV V14", capturedGoal.ResultGid, 0,
                "Contrôle exact du journal SELL/UID après le vrai retour ListAsync",
                assessment.HasLocalSaleCorrelation
                    ? "CORRÉLATION LOCALE" : "REVUE REQUISE"));

            // V17: The original V9 production adapter is the only listing
            // sender. Follow its returned EXACT SELL order/run into the
            // source-bound read-only V17 journal inspector on a worker thread;
            // never reconstruct an order from goal GID, listing UID or price.
            if (assessment.ExactJournalOrder is not { } exact ||
                exact.LocalRunId == Guid.Empty)
            {
                GuidedSupplyListingEvidenceStatus +=
                    " · V17 : ordre/run d'origine indisponible ; inspection historique " +
                    "requise, aucune mutation admise.";
                return;
            }
            var expectedGoal = capturedGoal;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                HdvProductionManualReviewInspectionV17? inspection = null;
                try
                {
                    string profile = HdvDirectMutationJournal.Profile(
                        expectedGoal.ServerId, expectedGoal.CharacterId);
                    if (profile.Length != 0)
                        inspection = Stores.Launch.HdvInspectProductionNextManualReviewV17(
                            pid, profile, exact.Order, exact.LocalRunId);
                }
                catch (Exception)
                {
                    // IO/identity failure is a denial, never an inferred
                    // absence of the original attempt or server success.
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (readSequence != System.Threading.Volatile.Read(
                            ref _guidedV17ListingReadSequence) ||
                        generation != System.Threading.Volatile.Read(
                            ref _productionUiRevision) ||
                        ProductionCharacter?.Invoke() != pid ||
                        !Equals(GuidedActivityGoal, expectedGoal) ||
                        Stores.Launch.ServerIdOf(pid) != expectedGoal.ServerId ||
                        Stores.Launch.CharacterId(pid) != expectedGoal.CharacterId)
                        return;
                    GuidedSupplyListingEvidenceStatus += inspection is null
                        ? " · V17 : journal SELL d'origine inaccessible, " +
                          "aucun nouveau listing/ACK/taxe admis."
                        : " · V17 revue de l'ordre original : " +
                          inspection.NextManualStep +
                          " · run/source actuels " +
                          (inspection.ExactRunAndSourceStillCurrent
                              ? "concordants localement" : "non concordants") +
                          ". Aucun ACK serveur, replay ni libération fiscale.";
                });
            });
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Apply();
        else Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>Re-read the actual V9/V12/V13/V16 stores before offering a
    /// selected buy deficit to the existing HDV buyer. Safe for delayed UI
    /// callbacks: no retained preview object can certify the current source.</summary>
    public bool IsCurrentGuidedV17BuyStage(uint pid,
        GuidedActivityProductionGoal goal, GuidedSupplyExactChoiceV16 choice)
    {
        if (choice is not { Valid: true, Kind: GuidedSupplyResumeStepV16.InspectBuy } ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() != pid ||
            !TryReadGuidedV16Resume(out var state, out var request,
                out var previewActions, out string profile) ||
            state is not { RecordedChoice: { } recorded } ||
            request is null || previewActions is null ||
            recorded != choice ||
            state.PlanHash != choice.PlanHash ||
            state.SourceKey != choice.SourceKey ||
            state.SessionKey != choice.SessionKey ||
            state.ObservationRevision != choice.ObservationRevision)
            return false;
        // Quantity is the already-net deficit (Required - bag - bank), NOT
        // the recipe's gross required count; do not subtract stock twice.
        if (!state.Rows.Any(r => r.Gid == choice.Gid && r.BuyAllowed &&
                r.BuyCandidate == choice.Quantity)) return false;

        // V18: a persisted UI choice cannot outlive a changed pods snapshot or
        // a late V9/ActivityRecovery marker. Recheck the exact V11/V12/V13/V9
        // roots and source twice BEFORE even staging an HDV row; this verdict
        // is read-only and never grants an HDV mutation/send lease.
        var evidence = GuidedSupplyResumePlannerV16.RevalidateRecordedNextStepV18(
            goal, request, previewActions,
            new GuidedActivityProductionStore(
                GuidedActivityProductionStore.DefaultRoot()),
            _guidedWorkflow,
            new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()),
            _productionAttempts, profile, choice);
        return evidence.MayPrepareConsultativeHandoff &&
            evidence.NextStep == GuidedSupplyResumeStepV16.InspectBuy &&
            evidence.RecordedChoice == choice &&
            evidence.ObservationRevision == choice.ObservationRevision &&
            ProductionCharacter?.Invoke() == pid &&
            GuidedActivityTargetStillMatches(goal) && !previewActions.IsRevoked;
    }

    /// <summary>Bank is always an inspection, never a transfer. A persisted
    /// InspectBank choice must still match the exact V18 source and pods when
    /// the UI consumes it; a direct manual bank navigation is a separate path.</summary>
    public bool IsCurrentGuidedV18BankInspection(uint pid,
        GuidedActivityProductionGoal goal, GuidedSupplyExactChoiceV16 choice)
    {
        if (choice is not { Valid: true, Kind: GuidedSupplyResumeStepV16.InspectBank } ||
            ProductionCharacter?.Invoke() != pid ||
            !GuidedActivityTargetStillMatches(goal) ||
            !TryReadGuidedV16Resume(out var state, out var request,
                out var actions, out string profile) ||
            state is null || request is null || actions is null ||
            state.RecordedChoice != choice)
            return false;
        var evidence = GuidedSupplyResumePlannerV16.RevalidateRecordedNextStepV18(
            goal, request, actions,
            new GuidedActivityProductionStore(
                GuidedActivityProductionStore.DefaultRoot()),
            _guidedWorkflow,
            new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()),
            _productionAttempts, profile, choice);
        return evidence.MayPrepareConsultativeHandoff &&
            evidence.NextStep == GuidedSupplyResumeStepV16.InspectBank &&
            evidence.RecordedChoice == choice &&
            ProductionCharacter?.Invoke() == pid &&
            GuidedActivityTargetStillMatches(goal) && !actions.IsRevoked;
    }

    private void StageGuidedV17ExactHdvBuy(GuidedSupplyExactChoiceV16 choice)
    {
        if (GuidedActivityGoal is not { } goal ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !IsCurrentGuidedV17BuyStage(pid, goal, choice))
        {
            GuidedSupplyV16ChoiceStatus =
                "V17 · Achat exact refusé : choix durable GID/déficit/source " +
                "révoqué ou tentative incertaine ; consulter les journaux.";
            return;
        }
        GuidedSupplyV16ChoiceStatus =
            "V17 · Handoff HDV consultatif : déficit GID " + choice.Gid +
            " ×" + choice.Quantity + " (stock et coffre déjà déduits). " +
            "Aucun achat automatique ; budget et lots restent à vérifier.";
        GuidedSupplyHdvBuyStageRequested?.Invoke(new(pid, goal, choice));
    }

    [RelayCommand]
    private void InspectGuidedHdvListing()
    {
        if (GuidedActivityGoal is not { } goal ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out _, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
        {
            GuidedSupplyActionStatus =
                "HDV : inspection refusée, objectif ou personnage source absent.";
            return;
        }
        var flow = _guidedWorkflow.Read(server, character);
        if (flow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(goal) ||
            flow.Phase == GuidedProductionWorkflowPhase.Absent)
        {
            GuidedSupplyActionStatus =
                "HDV : objectif ou checkpoint non attribuable, vérifier la reprise durable.";
            return;
        }
        GuidedSupplyActionStatus =
            "HDV consultatif · workflow " + flow.Phase + " ; " +
            "inspecter l'UID, le prix, les taxes et le journal durable. " +
            "Aucune nouvelle annonce ni quittance inventée.";
        GuidedSupplyHdvInspectionRequested?.Invoke(new(pid, goal));
    }

    /// <summary>
    /// Explicit operator path from the same supply objective to the EXISTING
    /// bank/pods panel. It makes no bank request, does not bypass V9 or clear
    /// ActivityRecovery. The user must return and refresh the observed stock
    /// revision before the V9 runner can be admitted to withdraw or craft.
    /// </summary>
    [RelayCommand]
    private void InspectGuidedBankSupply()
    {
        if (IsProductionRunning || GuidedActivityGoal is not { } goal ||
            !GuidedActivityTargetStillMatches(goal) ||
            ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out _, out _,
                out int server, out long character) ||
            server != goal.ServerId || character != goal.CharacterId)
        {
            GuidedSupplyActionStatus =
                "Banque : objectif source non confirmé ou atelier en cours.";
            return;
        }
        ActivityRecoveryDurableStatus activity;
        try
        {
            activity = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character));
        }
        catch (Exception)
        {
            GuidedSupplyActionStatus =
                "Banque : journal de reprise inaccessible ; aucune nouvelle opération admise.";
            return;
        }
        if (activity.State != ActivityRecoveryDurableState.Clear)
        {
            GuidedSupplyActionStatus =
                "Banque BLOQUÉE : activité " + activity.State +
                ", rapprochement du segment requis avant tout nouvel ordre.";
            return;
        }
        var bankChoice = _guidedWorkflow.Read(server, character).SupplyChoiceV16;
        if (bankChoice is { Kind: GuidedSupplyResumeStepV16.InspectBank } &&
            !IsCurrentGuidedV18BankInspection(pid, goal, bankChoice))
        {
            GuidedSupplyActionStatus =
                "V18 · Banque : choix GID/coffre durable ou témoin pods périmé. " +
                "Réinspecter le stock et sélectionner la nouvelle quantité ; aucun transfert.";
            return;
        }
        GuidedSupplyActionStatus =
            "Banque/pods : ouverture du panneau existant pour consultation ou " +
            "intervention manuelle opt-in. Revenir puis vérifier la NOUVELLE " +
            "révision du stock ; aucun transfert n'est envoyé par ce bouton.";
        GuidedSupplyBankInspectionRequested?.Invoke(new(pid, goal,
            bankChoice is { Kind: GuidedSupplyResumeStepV16.InspectBank }
                ? bankChoice : null));
    }

    /// <summary>RunProduction invokes AFTER the existing V9 Started fsync.</summary>
    private bool AdmitGuidedProductionActualRun(
        GuidedActivityProductionGoal goal,
        string fingerprint, ItemCraftAttemptLease actualLease)
    {
        if (!GuidedActivityTargetStillMatches(goal) ||
            !_guidedWorkflow.TryBegin(goal, fingerprint, actualLease.RunId))
            return false;
        _activeGuidedLocalRun = actualLease.RunId;
        return true;
    }

    /// <summary>Only V9's exact run/receipt completion can reconcile V12.</summary>
    private void FinishGuidedProductionActualRun(GuidedActivityProductionGoal goal,
        string profile)
    {
        var outcome = _productionAttempts.ReadStatus(profile);
        bool closed = _guidedWorkflow.TryCompleteFromActualProductionJournal(goal, outcome);
        _activeGuidedLocalRun = Guid.Empty;
        if (!closed)
            GuidedWorkflowStatus =
                "INCERTAIN · journal V12 non rapproché au run V9 exact ; ne pas réémettre.";
        else
            GuidedWorkflowStatus = outcome.Phase == ItemCraftAttemptPhase.Completed
                ? "RAPPROCHÉ · reçus V9 exacts associés au même run local ; aucun ACK transport supposé."
                : "INCERTAIN · production V9 incomplète ou issue ambiguë ; reprise interdite.";
    }

    [ObservableProperty] public partial string GuidedActivityStatus { get; private set; } =
        "Aucun objectif activité→atelier importé.";

    /// <summary>
    /// Reuses the ACTUAL verified catalog, AddSelection command, planner
    /// and production runner. It never bypasses recipe checks, dispatches
    /// network traffic or fabricates missing ingredient counts.
    /// </summary>
    public bool TryImportGuidedActivity(GuidedActivityProductionGoal goal)
    {
        uint? selected = ProductionCharacter?.Invoke();
        if (selected is not uint pid || pid == 0)
        {
            GuidedActivityStatus = "REFUS : aucun personnage source sélectionné.";
            return false;
        }
        return TryImportGuidedActivity(goal,
            Stores.Launch.ServerIdOf(pid), Stores.Launch.CharacterId(pid));
    }

    /// <summary>
    /// Same actual VM catalog/selection/plan entry, with explicit observed
    /// owner arguments for offline entry tests. The LIVE adapter supplies the
    /// current LaunchService source. Even a forged caller here cannot bypass
    /// the real MainWindow bank/craft/HDV admission checks.
    /// </summary>
    public bool TryImportGuidedActivity(GuidedActivityProductionGoal goal,
        int observedServer, long observedCharacter)
    {
        if (goal is not { Valid: true } || IsProductionRunning || DemoMode)
        {
            GuidedActivityStatus =
                "REFUS : objectif invalide, atelier en cours ou démonstration active.";
            return false;
        }
        if (observedServer != goal.ServerId ||
            observedCharacter != goal.CharacterId)
        {
            GuidedActivityStatus =
                "REFUS : le personnage sélectionné ne correspond pas à l'objectif durable.";
            return false;
        }
        if (CatalogProvider is null)
        {
            GuidedActivityStatus =
                "BLOQUÉ : aucun catalogue réel raccordé à l'atelier.";
            return false;
        }
        // The search is local/provider-backed and has no gameplay side effect.
        // A previously selected target remains unchanged if the exact GID is
        // not returned by the verified source.
        Mode = SingleMode;
        SearchText = goal.ResultGid.ToString(CultureInfo.InvariantCulture);
        try { LoadCatalog(CatalogProvider() ?? Array.Empty<ItemCraftCatalogEntry>()); }
        catch (Exception ex)
        {
            GuidedActivityStatus = "BLOQUÉ : catalogue indisponible (" +
                ex.GetType().Name + ").";
            return false;
        }
        var exact = Catalog.FirstOrDefault(e =>
            e.Category == "Objet" && IsTrusted(e) &&
            e.Id == SearchText);
        if (exact is null)
        {
            GuidedActivityStatus =
                "BLOQUÉ : GID absent de la source locale vérifiée. " +
                "La cible actuelle de l'atelier est préservée.";
            return false;
        }
        SelectedCatalogEntry = exact;
        QuantityText = goal.Quantity.ToString(CultureInfo.InvariantCulture);
        AddSelectionCommand.Execute(null);
        if (Targets.Count != 1 ||
            !string.Equals(Targets[0].ItemId, exact.Id, StringComparison.Ordinal) ||
            Targets[0].Quantity != goal.Quantity ||
            !UpdateTargetEvidenceStatus())
        {
            GuidedActivityStatus =
                "BLOQUÉ : échec de la vraie sélection de cible ; rien n'est envoyé.";
            return false;
        }
        // This is a LIVE VM plan from the same catalog/provider, not a
        // production receipt. Budget, bank opt-in and price floor remain
        // explicit user settings in the actual existing panel.
        GuidedActivityGoal = goal;
        _activeGuidedLocalRun = Guid.Empty;
        _lastGuidedSupplyStock = null;
        _lastGuidedSupplyFingerprint = null;
        _lastGuidedSupplyPlan = null;
        _lastGuidedSupplyCheckpointPresent = false;
        GuidedSupplyStages.Clear();
        GuidedSupplyGatherItemGidText = "";
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        GuidedSupplyV16ChoiceStatus =
            "V16 · Objectif importé : inspecter le plan V13 avant tout choix GID durable.";
        GuidedSupplyV16RunnerOptIn = false;
        GuidedSupplyV16RecoveryStatus =
            "V16 · Nouvel objectif importé : état Lua à inspecter après une activité.";
        GuidedSupplyV16RunnerStatus =
            "V16 · Nouvel objectif : vérifier la recette, le stock, la source et le journal avant tout runner.";
        GuidedSupplyContinuationStatus =
            "V15 · Objectif importé, inspecter l'état durable et le stock avant toute suite.";
        RefreshPlanCommand.Execute(null);
        InspectGuidedWorkflow();
        GuidedActivityStatus =
            $"PRÊT POUR PRÉVÉRIFICATION · GID {goal.ResultGid} ×{goal.Quantity} " +
            $"(serveur {goal.ServerId}, personnage {goal.CharacterId}). " +
            "Les stocks/ingrédients et le budget seront revérifiés au lancement ; " +
            "aucune action de jeu n'a été envoyée.";
        return true;
    }

    [RelayCommand]
    private void ClearGuidedActivity()
    {
        if (IsProductionRunning)
        {
            GuidedActivityStatus =
                "Impossible de détacher l'objectif pendant une production active.";
            return;
        }
        GuidedActivityGoal = null;
        _activeGuidedLocalRun = Guid.Empty;
        _lastGuidedSupplyStock = null;
        _lastGuidedSupplyFingerprint = null;
        _lastGuidedSupplyPlan = null;
        _lastGuidedSupplyCheckpointPresent = false;
        GuidedSupplyStages.Clear();
        GuidedSupplyGatherItemGidText = "";
        GuidedV16ExactChoices.Clear();
        SelectedGuidedV16ExactChoice = null;
        GuidedSupplyV16ChoiceStatus =
            "V16 · Aucun choix conservé dans l'interface ; le journal durable reste intact.";
        GuidedSupplyV16RunnerOptIn = false;
        GuidedSupplyV16RecoveryStatus =
            "V16 · Liaison détachée ; état durable Lua non effacé.";
        GuidedSupplyV16RunnerStatus =
            "V16 · Objectif détaché, aucun opt-in conservé ; journaux durables inchangés.";
        GuidedSupplyContinuationStatus =
            "V15 · Liaison UI détachée, quarantaines et preuves durables conservées.";
        GuidedActivityStatus =
            "Liaison supprimée. L'atelier manuel reste disponible ; " +
            "les journaux durables d'activité et de production sont inchangés.";
    }
}
