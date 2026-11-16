using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GivreClone.App.Services.ProfessionPlanner;

/// <summary>
/// A complete, caller-attested material snapshot for one character/account scope.
    /// MaxUnits on every source is the REMAINING capacity observed at this revision,
/// not an amount inferred from a previous proposal.
/// A new revision must come from a fresh observation, not from a planned action.
/// </summary>
public sealed record ProfessionRecoveryObservation(
    string SubjectKey,
    long Revision,
    string CatalogVersion,
    bool InventoryComplete,
    bool SourceCapsComplete,
    IReadOnlyList<ProfessionInventoryStack> Inventory,
    IReadOnlyList<MaterialSource> Sources);

public enum ProfessionRecoveryMode { Training, Item }
public enum ProfessionRecoveryIssue
{
    None,
    InvalidObservation,
    StaleObservation,
    SubjectChanged,
    CatalogChanged,
    RequestChanged,
    PreviousProposalChanged,
    PlanningFailed
}

/// <summary>
/// Reconciliation cursor for a preview. Hashes detect accidental stale/mutated data,
/// not authentication or a game-server receipt. Keep the current cursor with its plan.
/// </summary>
public sealed record ProfessionRecoveryCheckpoint(
    string SubjectKey,
    long Revision,
    string CatalogVersion,
    ProfessionRecoveryMode Mode,
    long ItemGid,
    long ItemQuantity,
    string RequestFingerprint,
    string ObservationFingerprint,
    string ProposalFingerprint);

public sealed record ProfessionRecoveryResult(
    ProfessionRecoveryIssue Issue,
    ProfessionPlan? Plan,
    ProfessionRecoveryCheckpoint? Checkpoint,
    bool PreviousProposalInvalidated,
    string Detail)
{
    public bool Accepted => Issue == ProfessionRecoveryIssue.None;
}

/// <summary>
/// Pure reconciliation for the existing ProfessionPlannerEngine workflow.
/// It never carries a proposed UseInventory/Gather/Buy/Craft step forward as
/// evidence of execution. Every accepted revision recomputes the entire DAG
/// from the observed inventory and remaining source capacity.
/// </summary>
public static class ProfessionRecoveryWorkflow
{
    public static ProfessionRecoveryResult BeginItem(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation,
        long itemGid, long quantity)
    {
        if (itemGid <= 0 || quantity <= 0 || quantity > 1_000_000_000L)
            return Refuse(ProfessionRecoveryIssue.InvalidObservation, false,
                "Demande d'objet invalide ou hors limite.");
        return Create(request, observation, ProfessionRecoveryMode.Item, itemGid, quantity, false);
    }

    public static ProfessionRecoveryResult BeginTraining(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation) =>
        Create(request, observation, ProfessionRecoveryMode.Training, 0, 0, false);

    public static ProfessionRecoveryResult Resume(
        ProfessionPlanRequest request,
        ProfessionRecoveryCheckpoint checkpoint,
        ProfessionPlan previousProposal,
        ProfessionRecoveryObservation previousObservation,
        ProfessionRecoveryObservation observation)
    {
        // Any failed reconciliation invalidates the old preview. It cannot be
        // offered as current if the next inventory/capacity observation is bad.
        if (request is null || checkpoint is null || previousProposal is null ||
            previousObservation is null)
            return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                "Reprise impossible : requête, curseur, plan ou ancienne observation manquant.");
        if (!IsFingerprint(checkpoint.RequestFingerprint) ||
            !IsFingerprint(checkpoint.ObservationFingerprint) ||
            !IsFingerprint(checkpoint.ProposalFingerprint) ||
            checkpoint.Revision <= 0 ||
            !Enum.IsDefined(checkpoint.Mode) ||
            (checkpoint.Mode == ProfessionRecoveryMode.Item &&
             (checkpoint.ItemGid <= 0 || checkpoint.ItemQuantity is <= 0 or > 1_000_000_000L)) ||
            (checkpoint.Mode == ProfessionRecoveryMode.Training &&
             (checkpoint.ItemGid != 0 || checkpoint.ItemQuantity != 0)))
            return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                "Curseur de reprise malformé.");

        if (!SafeRequestShape(request) || !SafePlanShape(previousProposal) ||
            !ValidObservation(request, previousObservation))
            return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                "Données originales incomplètes ou illisibles.");
        try
        {
            if (!string.Equals(checkpoint.RequestFingerprint, FingerprintRequest(request), StringComparison.Ordinal))
                return Refuse(ProfessionRecoveryIssue.RequestChanged, true,
                    "Objectif, recettes, niveaux ou choix modifiés : créer un nouveau plan initial.");
            if (!string.Equals(checkpoint.ObservationFingerprint,
                    FingerprintObservation(previousObservation), StringComparison.Ordinal) ||
                checkpoint.Revision != previousObservation.Revision ||
                !string.Equals(checkpoint.SubjectKey, previousObservation.SubjectKey, StringComparison.Ordinal) ||
                !string.Equals(checkpoint.CatalogVersion, previousObservation.CatalogVersion, StringComparison.Ordinal))
                return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                    "Ancienne observation modifiée : reprise refusée.");
            if (!string.Equals(checkpoint.ProposalFingerprint, FingerprintPlan(previousProposal), StringComparison.Ordinal))
                return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                    "Ancienne proposition modifiée : reprise refusée.");

            // Rebuild the previous preview to bind the cursor's target/mode/quantity
            // to the original observation as well. A changed target must never be
            // accepted merely because it reuses a valid old proposal fingerprint.
            var originalRequest = request with
            {
                Inventory = previousObservation.Inventory.ToArray(),
                Sources = previousObservation.Sources.Where(s => s.MaxUnits > 0).ToArray()
            };
            var replayed = checkpoint.Mode == ProfessionRecoveryMode.Item
                ? ProfessionPlannerEngine.PlanItem(originalRequest,
                    checkpoint.ItemGid, checkpoint.ItemQuantity)
                : ProfessionPlannerEngine.Plan(originalRequest);
            if (!string.Equals(checkpoint.ProposalFingerprint, FingerprintPlan(replayed), StringComparison.Ordinal))
                return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                    "Cible, quantité ou ancienne observation incompatibles avec le plan initial.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException
            or JsonException or NotSupportedException)
        {
            return Refuse(ProfessionRecoveryIssue.PreviousProposalChanged, true,
                "Impossible de vérifier l'ancienne proposition.");
        }

        if (observation is null || string.IsNullOrWhiteSpace(observation.SubjectKey))
            return Refuse(ProfessionRecoveryIssue.InvalidObservation, true,
                "Observation de reprise absente ou incomplète.");
        if (!string.Equals(checkpoint.SubjectKey, observation.SubjectKey, StringComparison.Ordinal))
            return Refuse(ProfessionRecoveryIssue.SubjectChanged, true,
                "Le périmètre du personnage/compte a changé.");
        if (!string.Equals(checkpoint.CatalogVersion, observation.CatalogVersion, StringComparison.Ordinal))
            return Refuse(ProfessionRecoveryIssue.CatalogChanged, true,
                "Version de catalogue changée : reconstruire une base de plan.");
        if (observation.Revision <= checkpoint.Revision)
            return Refuse(ProfessionRecoveryIssue.StaleObservation, true,
                "Observation ancienne ou rejouée : aucune capacité ni aucun stock réutilisé.");

        return Create(request, observation, checkpoint.Mode, checkpoint.ItemGid,
            checkpoint.ItemQuantity, true);
    }

    private static ProfessionRecoveryResult Create(
        ProfessionPlanRequest request,
        ProfessionRecoveryObservation observation,
        ProfessionRecoveryMode mode,
        long itemGid,
        long quantity,
        bool invalidateOld)
    {
        if (!ValidObservation(request, observation))
            return Refuse(ProfessionRecoveryIssue.InvalidObservation, invalidateOld,
                "Observation non attestée, partielle ou aux capacités/provenances ambiguës.");
        try
        {
            // Snapshot arrays isolate the planning run from caller collection mutation.
            var observedRequest = request with
            {
                Inventory = observation.Inventory.ToArray(),
                // Exhausted sources are valid observations, but the base engine
                // cannot accept MaxUnits=0: remove them from planning candidates.
                Sources = observation.Sources.Where(s => s.MaxUnits > 0).ToArray()
            };
            var plan = mode == ProfessionRecoveryMode.Item
                ? ProfessionPlannerEngine.PlanItem(observedRequest, itemGid, quantity)
                : ProfessionPlannerEngine.Plan(observedRequest);
            var checkpoint = new ProfessionRecoveryCheckpoint(
                observation.SubjectKey, observation.Revision, observation.CatalogVersion,
                mode, itemGid, quantity, FingerprintRequest(request),
                FingerprintObservation(observation), FingerprintPlan(plan));
            return new(ProfessionRecoveryIssue.None, plan, checkpoint, invalidateOld,
                plan.IsComplete
                    ? "Nouveau plan consultatif calculé depuis l'observation complète ; aucune action attestée."
                    : "Nouveau plan incomplet ; les étapes non résolues nécessitent des observations ou choix supplémentaires.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException
            or JsonException or NotSupportedException)
        {
            return Refuse(ProfessionRecoveryIssue.PlanningFailed, invalidateOld,
                "Recalcul refusé : données incohérentes, quantité hors borne ou débordement.");
        }
    }

    private static bool ValidObservation(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation)
    {
        if (!SafeRequestShape(request) || observation is null ||
            !observation.InventoryComplete || !observation.SourceCapsComplete ||
            observation.Revision <= 0 ||
            string.IsNullOrWhiteSpace(observation.SubjectKey) || observation.SubjectKey.Length > 128 ||
            string.IsNullOrWhiteSpace(observation.CatalogVersion) || observation.CatalogVersion.Length > 128 ||
            observation.Inventory is null || observation.Sources is null ||
            observation.Inventory.Count > 10000 || observation.Sources.Count > 10000)
            return false;

        // Complete source-cap observations must be finite and independently
        // versioned. Zero is an observed exhausted source; null means unknown.
        if (observation.Sources.Any(s => s is null || !s.Verified ||
            s.ItemGid <= 0 || s.Method == AcquisitionMethod.Craft ||
            !Enum.IsDefined(s.Method) || s.SecondsPerUnit is < 0 || s.KamasPerUnit is < 0 ||
            s.MaxUnits is null or < 0 or > 1_000_000_000L ||
            string.IsNullOrWhiteSpace(s.SourceVersion) || s.SourceVersion.Length > 128 ||
            !string.Equals(s.SourceVersion, observation.CatalogVersion, StringComparison.Ordinal)))
            return false;
        if (request.Recipes.Any(r => r is null || (r.Verified &&
            !string.Equals(r.SourceVersion, observation.CatalogVersion, StringComparison.Ordinal))) ||
            request.XpTable is { Verified: true } xp &&
            !string.Equals(xp.Version, observation.CatalogVersion, StringComparison.Ordinal))
            return false;

        // No two rows may imply independent quotas for the same source identity.
        if (observation.Sources.GroupBy(s => (s.ItemGid, s.Method, s.SourceVersion))
            .Any(g => g.Count() > 1))
            return false;
        if (observation.Inventory.Any(s => s is null || s.ItemGid <= 0 ||
            s.Quantity is < 0 or > 1_000_000_000L ||
            string.IsNullOrWhiteSpace(s.StackId) || s.StackId.Length > 128 ||
            string.IsNullOrWhiteSpace(s.Container) || s.Container.Length > 128) ||
            observation.Inventory.GroupBy(s => s.StackId, StringComparer.Ordinal)
                .Any(g => g.Count() > 1))
            return false;
        return true;
    }

    private static bool SafeRequestShape(ProfessionPlanRequest request) =>
        request is not null && request.Goal is not null &&
        request.Goal.JobName?.Length is not > 256 &&
        request.Recipes is not null && request.Inventory is not null &&
        request.Sources is not null && request.Recipes.Count <= 2000 &&
        request.Inventory.Count <= 10000 && request.Sources.Count <= 10000 &&
        request.XpTable is not { Thresholds: null } &&
        request.XpTable?.Version?.Length is not > 128 &&
        request.XpTable?.Thresholds.Count is not > 201 &&
        request.Recipes.All(r => r is not null && r.Ingredients is not null &&
            r.Ingredients.Count <= 64 && r.Ingredients.All(i => i is not null) &&
            r.SourceVersion is not null && r.SourceVersion.Length <= 128);

    private static bool SafePlanShape(ProfessionPlan plan) =>
        plan is not null && plan.Steps is not null &&
        plan.Steps.Count <= 2048 && plan.UsedStock is not null &&
        plan.RemainingStock is not null && plan.Warnings is not null &&
        plan.Steps.All(s => s is not null && s.Dependencies is not null &&
            s.Alternatives is not null && s.Alternatives.All(a => a is not null));

    private static ProfessionRecoveryResult Refuse(
        ProfessionRecoveryIssue issue, bool invalidateOld, string detail) =>
        new(issue, null, null, invalidateOld, detail);

    private static bool IsFingerprint(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string FingerprintRequest(ProfessionPlanRequest r) => Hash(new
    {
        r.Goal,
        r.TrainingRecipeId,
        r.RequestedCrafts,
        Recipes = r.Recipes.OrderBy(x => x.RecipeId).Select(x => new
        {
            x.RecipeId, x.ResultGid, x.ResultQty, x.RequiredLevel, x.JobId,
            x.XpPerCraft, x.XpVerified, x.SourceVersion, x.Verified,
            x.SecondsPerCraft, x.KamasPerCraft,
            Ingredients = x.Ingredients.OrderBy(i => i.ItemGid).ThenBy(i => i.Quantity).ToArray()
        }).ToArray(),
        XpVersion = r.XpTable?.Version,
        XpVerified = r.XpTable?.Verified,
        XpThresholds = r.XpTable?.Thresholds.OrderBy(t => t.Level).ToArray(),
        Preferences = r.Preferences ?? new ProfessionPreferences(),
        RecipeChoices = r.RecipeChoices?.OrderBy(x => x.Key).ToArray(),
        MethodChoices = r.MethodChoices?.OrderBy(x => x.Key).ToArray(),
        KnownJobLevels = r.KnownJobLevels?.OrderBy(x => x.Key).ToArray()
    });

    private static string FingerprintObservation(ProfessionRecoveryObservation o) => Hash(new
    {
        o.SubjectKey, o.Revision, o.CatalogVersion, o.InventoryComplete, o.SourceCapsComplete,
        Inventory = o.Inventory.OrderBy(s => s.StackId, StringComparer.Ordinal).ToArray(),
        Sources = o.Sources.OrderBy(s => s.ItemGid).ThenBy(s => s.Method)
            .ThenBy(s => s.SourceVersion, StringComparer.Ordinal).ToArray()
    });

    private static string FingerprintPlan(ProfessionPlan p) => Hash(new
    {
        p.Goal, p.Progress,
        Steps = p.Steps.Select(s => new
        {
            s.Id, s.Kind, s.ItemGid, s.Quantity, s.RecipeId, s.Detail,
            s.Dependencies, s.Verified, s.Kamas, s.Seconds, s.Alternatives
        }).ToArray(),
        UsedStock = p.UsedStock.OrderBy(x => x.Key).ToArray(),
        RemainingStock = p.RemainingStock.OrderBy(x => x.Key).ToArray(),
        p.Warnings, p.KnownKamasSubtotal, p.KnownSecondsSubtotal,
        p.FullKamasEstimate, p.FullSecondsEstimate,
        p.IsComplete, p.Executed, p.Acknowledged
    });

    private static string Hash<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
}
