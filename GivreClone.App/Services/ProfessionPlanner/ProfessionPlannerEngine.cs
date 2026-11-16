using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.ProfessionPlanner;

/// <summary>Tout identifiant, prix, XP et trajet doit venir d'une source fournie par l'intégrateur.
/// Les modèles et résultats sont consultatifs : aucun socket, script, banque ou ordre HDV.</summary>
public sealed record ProfessionGoal(int? JobId, string? JobName, int StartLevel, int TargetLevel,
    long? CurrentXp = null, bool CurrentXpVerified = false);
public sealed record XpThreshold(int Level, long CumulativeXp);
public sealed record XpTable(string Version, bool Verified, IReadOnlyList<XpThreshold> Thresholds);
public sealed record RecipeIngredient(long ItemGid, long Quantity);
public sealed record ProfessionRecipe(long RecipeId, long ResultGid, long ResultQty,
    IReadOnlyList<RecipeIngredient> Ingredients, int RequiredLevel, int? JobId = null,
    long? XpPerCraft = null, bool XpVerified = false, string SourceVersion = "",
    bool Verified = false, long? SecondsPerCraft = null, decimal? KamasPerCraft = null);
public sealed record ProfessionInventoryStack(string StackId, long ItemGid, long Quantity, string Container);

public enum AcquisitionMethod { Craft, Gather, Drop, Buy }
public sealed record MaterialSource(long ItemGid, AcquisitionMethod Method,
    long? SecondsPerUnit, decimal? KamasPerUnit, string SourceVersion, bool Verified,
    long? MaxUnits = null);
public sealed record ProfessionPreferences(decimal KamasWeight = 1m, decimal TimeWeight = 0m);
public sealed record ProfessionPlanRequest(ProfessionGoal Goal, long? TrainingRecipeId,
    long? RequestedCrafts, IReadOnlyList<ProfessionRecipe> Recipes,
    IReadOnlyList<ProfessionInventoryStack> Inventory, IReadOnlyList<MaterialSource> Sources,
    XpTable? XpTable = null, ProfessionPreferences? Preferences = null,
    IReadOnlyDictionary<long, long>? RecipeChoices = null,
    IReadOnlyDictionary<long, AcquisitionMethod>? MethodChoices = null,
    IReadOnlyDictionary<int, int>? KnownJobLevels = null);

public enum PlanStepKind { UseInventory, Craft, Gather, Drop, Buy, Unresolved }
public sealed record AcquisitionOption(AcquisitionMethod Method, long? RecipeId,
    string SourceVersion, bool Verified, decimal? KamasPerUnit, long? SecondsPerUnit);
public sealed record ProfessionPlanStep(int Id, PlanStepKind Kind, long ItemGid, long Quantity,
    long? RecipeId, string Detail, IReadOnlyList<int> Dependencies,
    bool Verified, decimal? Kamas, long? Seconds,
    IReadOnlyList<AcquisitionOption> Alternatives);
public sealed record ProfessionProgress(long? TargetXp, long? NeededXp, long? PlannedCrafts,
    long? PotentialXpEarned, long? PotentialFinalXp, bool TargetReachableFromKnownXp);
public sealed record ProfessionPlan(ProfessionGoal Goal, ProfessionProgress Progress,
    IReadOnlyList<ProfessionPlanStep> Steps, IReadOnlyDictionary<long, long> UsedStock,
    IReadOnlyDictionary<long, long> RemainingStock, IReadOnlyList<string> Warnings,
    decimal KnownKamasSubtotal, long KnownSecondsSubtotal,
    decimal? FullKamasEstimate, long? FullSecondsEstimate, bool IsComplete,
    bool Executed, bool Acknowledged);
public sealed record ProfessionTrainingCandidate(long RecipeId, long ResultGid,
    string SourceVersion, long? Crafts, decimal? Kamas, long? Seconds,
    bool Comparable, IReadOnlyList<string> Warnings);
public sealed record ProfessionTrainingRecommendation(
    IReadOnlyList<ProfessionTrainingCandidate> Candidates, long? SuggestedRecipeId, string Reason);

/// <summary>Estimateur borné et déterministe. La récursion est explicite, avec une réserve de stock
/// UNIQUE partagée entre toutes les branches de l'arbre d'ingrédients. Ne fait jamais d'action en jeu.</summary>
public static class ProfessionPlannerEngine
{
    private const int MaxNodes = 2048;
    private const int MaxDepth = 32;
    private const long MaxQuantity = 1_000_000_000L;

    /// <summary>Compare les recettes d'entraînement du métier à niveau accessible.
    /// Suggestion automatique SEULEMENT si toutes les options ont XP, coût complet et durée comparables ;
    /// sinon expose les candidats à l'UI, sans choisir un plan arbitraire.</summary>
    public static ProfessionTrainingRecommendation SuggestTrainingRecipes(ProfessionPlanRequest request)
    {
        Validate(request);
        var candidates = new List<ProfessionTrainingCandidate>();
        var eligible = request.Recipes
            .Where(r => request.Goal.JobId == r.JobId && r.RequiredLevel <= request.Goal.StartLevel)
            .OrderBy(r => r.RecipeId).ToArray();
        if (eligible.Length > 64)
            return new(candidates.AsReadOnly(), null,
                "Trop de recettes candidates (>64) : filtrer par niveau/type avant comparaison.");
        foreach (var recipe in eligible)
        {
            var proposal = Plan(request with { TrainingRecipeId = recipe.RecipeId, RequestedCrafts = null });
            bool comparable = proposal.IsComplete && proposal.Progress.TargetReachableFromKnownXp &&
                proposal.Progress.PlannedCrafts is not null &&
                proposal.FullKamasEstimate is not null && proposal.FullSecondsEstimate is not null;
            candidates.Add(new(recipe.RecipeId, recipe.ResultGid, recipe.SourceVersion,
                proposal.Progress.PlannedCrafts, proposal.FullKamasEstimate,
                proposal.FullSecondsEstimate, comparable, proposal.Warnings));
        }
        if (candidates.Count == 0)
            return new(candidates.AsReadOnly(), null, "Aucune recette de ce métier à ce niveau dans le catalogue fourni.");
        if (candidates.Any(c => !c.Comparable))
            return new(candidates.AsReadOnly(), null, "Options incomplètes ou non comparables : choisissez manuellement après vérification.");
        var prefs = request.Preferences ?? new ProfessionPreferences();
        var selected = candidates.OrderBy(c => checked(c.Kamas!.Value * prefs.KamasWeight +
                                                    c.Seconds!.Value * prefs.TimeWeight))
            .ThenBy(c => c.RecipeId).First();
        return new(candidates.AsReadOnly(), selected.RecipeId,
            "Comparaison strictement fondée sur coûts/temps/XP attestés et pondérations explicites ; aucune action exécutée.");
    }

    public static ProfessionPlan Plan(ProfessionPlanRequest request)
    {
        Validate(request);
        var ctx = new Context(request);
        var progress = ctx.CalculateProgress();
        long? count = request.RequestedCrafts ?? progress.PlannedCrafts;
        if (request.RequestedCrafts is not null && progress.PlannedCrafts is > 0 &&
            request.RequestedCrafts < progress.PlannedCrafts)
            ctx.Warn("Nombre de crafts demandé insuffisant pour l'XP ciblée selon les données vérifiées.");
        if (count is null)
            ctx.Warn("XP de départ, table XP versionnée ou gain XP/recette non vérifiés : nombre de crafts inconnu.");
        else if (request.TrainingRecipeId is not { } recipeId)
            ctx.Warn("Choisir une recette d'entraînement vérifiée pour calculer les composants.");
        else
            ctx.ExpandTraining(recipeId, count.Value);
        return ctx.Finish(progress with { PlannedCrafts = count,
            PotentialXpEarned = ctx.PotentialXp(count),
            PotentialFinalXp = ctx.PotentialFinalXp(count),
            TargetReachableFromKnownXp = ctx.CanReachTarget(count, progress) });
    }

    /// <summary>Calcul d'une demande manuelle, indépendamment des XP et niveaux. Pour une recette de formation,
    /// utiliser Plan avec TrainingRecipeId et RequestedCrafts. Le résultat ne constitue jamais un ACK.</summary>
    public static ProfessionPlan PlanItem(ProfessionPlanRequest request, long itemGid, long quantity)
    {
        Validate(request);
        if (itemGid <= 0 || quantity <= 0 || quantity > MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        var ctx = new Context(request);
        ctx.Expand(itemGid, quantity, new HashSet<long>(), 0);
        return ctx.Finish(new ProfessionProgress(null, null, null, null, null, false));
    }

    /// <summary>
    /// Entry points for a craft/gather workflow that can provide complete inventory
    /// and remaining source-capacity observations. Plain Plan/PlanItem are still
    /// available for one-off previews without reconciliation.
    /// </summary>
    public static ProfessionRecoveryResult BeginObservedItem(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation,
        long itemGid, long quantity) =>
        ProfessionRecoveryWorkflow.BeginItem(request, observation, itemGid, quantity);

    public static ProfessionRecoveryResult BeginObservedTraining(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation) =>
        ProfessionRecoveryWorkflow.BeginTraining(request, observation);

    public static ProfessionRecoveryResult ResumeObserved(
        ProfessionPlanRequest request,
        ProfessionRecoveryCheckpoint checkpoint,
        ProfessionPlan previousProposal,
        ProfessionRecoveryObservation previousObservation,
        ProfessionRecoveryObservation observation) =>
        ProfessionRecoveryWorkflow.Resume(request, checkpoint, previousProposal,
            previousObservation, observation);

    private static void Validate(ProfessionPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Goal);
        ArgumentNullException.ThrowIfNull(request.Recipes);
        ArgumentNullException.ThrowIfNull(request.Inventory);
        ArgumentNullException.ThrowIfNull(request.Sources);
        var g = request.Goal;
        if ((g.JobId is null || g.JobId <= 0) && string.IsNullOrWhiteSpace(g.JobName))
            throw new ArgumentException("Identité de métier requise.", nameof(request));
        if (g.StartLevel is < 1 or > 200 || g.TargetLevel is < 1 or > 200 ||
            g.TargetLevel < g.StartLevel || g.CurrentXp is < 0 ||
            request.RequestedCrafts is < 0 or > MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(request), "Niveaux, XP ou crafts invalides.");
        if (request.Recipes.Count > 2000 || request.Inventory.Count > 10000 || request.Sources.Count > 10000)
            throw new ArgumentException("Catalogue dépassant les bornes.", nameof(request));
        var weights = request.Preferences ?? new ProfessionPreferences();
        if (weights.KamasWeight < 0 || weights.TimeWeight < 0 ||
            weights.KamasWeight + weights.TimeWeight <= 0 || weights.KamasWeight > 1000000 ||
            weights.TimeWeight > 1000000) throw new ArgumentException("Pondérations invalides.", nameof(request));
        var stackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stack in request.Inventory)
            if (stack is null || stack.ItemGid <= 0 || stack.Quantity < 0 ||
                stack.Quantity > MaxQuantity || string.IsNullOrWhiteSpace(stack.StackId) ||
                !stackIds.Add(stack.StackId))
                throw new ArgumentException("Stack d'inventaire invalide ou dupliqué.", nameof(request));
        foreach (var recipe in request.Recipes)
        {
            if (recipe is null || recipe.RecipeId <= 0 || recipe.ResultGid <= 0 ||
                recipe.ResultQty is <= 0 or > MaxQuantity ||
                recipe.RequiredLevel is < 1 or > 200 || recipe.Ingredients is null ||
                recipe.Ingredients.Count > 64 || recipe.Ingredients.Any(i => i is null ||
                    i.ItemGid <= 0 || i.Quantity is <= 0 or > MaxQuantity) ||
                recipe.XpPerCraft is < 0 || recipe.SecondsPerCraft is < 0 ||
                recipe.KamasPerCraft is < 0)
                throw new ArgumentException("Recette invalide.", nameof(request));
        }
        if (request.Recipes.Select(x => x.RecipeId).Distinct().Count() != request.Recipes.Count)
            throw new ArgumentException("RecipeId dupliqué.", nameof(request));
        foreach (var source in request.Sources)
            if (source is null || source.ItemGid <= 0 ||
                source.Method == AcquisitionMethod.Craft ||
                !Enum.IsDefined(source.Method) || source.SecondsPerUnit is < 0 ||
                source.KamasPerUnit is < 0 || source.MaxUnits is <= 0 or > MaxQuantity)
                throw new ArgumentException("Source d'acquisition invalide.", nameof(request));
        // No source identifier exists beyond item/method/version. Two rows with the same
        // identity cannot safely be treated as two independent capacity allowances.
        if (request.Sources.GroupBy(s => (s.ItemGid, s.Method, s.SourceVersion))
            .Any(g => g.Count() > 1))
            throw new ArgumentException("Source d'acquisition dupliquée ou capacité ambiguë.", nameof(request));
        if (request.KnownJobLevels?.Any(e => e.Key <= 0 || e.Value is < 1 or > 200) == true)
            throw new ArgumentException("Niveau de métier secondaire invalide.", nameof(request));
        if (request.XpTable is { } xp &&
            (xp.Thresholds is null || xp.Thresholds.Count > 201 ||
             xp.Thresholds.Any(t => t is null || t.Level is < 1 or > 200 || t.CumulativeXp < 0) ||
             xp.Thresholds.Select(t => t.Level).Distinct().Count() != xp.Thresholds.Count))
            throw new ArgumentException("Table XP invalide.", nameof(request));
    }

    private sealed class Context
    {
        private readonly ProfessionPlanRequest _r;
        private readonly Dictionary<long, long> _remaining = new();
        private readonly Dictionary<long, long> _used = new();
        private readonly Dictionary<long, List<ProfessionRecipe>> _recipes;
        private readonly Dictionary<long, List<MaterialSource>> _sources;
        // The limit belongs to a source for the entire proposal, not to each ingredient
        // occurrence. These allocations are local to one planning run; no real purchases
        // or collection are inferred from them.
        private readonly Dictionary<MaterialSource, long> _sourceAllocations = new();
        private readonly List<ProfessionPlanStep> _steps = new();
        private readonly List<string> _warnings = new();
        private readonly ProfessionRecipe? _training;

        public Context(ProfessionPlanRequest r)
        {
            _r = r;
            foreach (var stack in r.Inventory)
            {
                _remaining.TryGetValue(stack.ItemGid, out long old);
                _remaining[stack.ItemGid] = checked(old + stack.Quantity);
            }
            _recipes = r.Recipes.GroupBy(x => x.ResultGid).ToDictionary(g => g.Key, g => g.OrderBy(x => x.RecipeId).ToList());
            _sources = r.Sources.GroupBy(x => x.ItemGid).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Method).ThenBy(x => x.SourceVersion, StringComparer.Ordinal).ToList());
            if (r.TrainingRecipeId is { } id) _training = r.Recipes.FirstOrDefault(x => x.RecipeId == id);
        }

        public void Warn(string message) { if (!_warnings.Contains(message, StringComparer.Ordinal)) _warnings.Add(message); }
        private static bool ProvenRecipe(ProfessionRecipe p) => p.Verified && !string.IsNullOrWhiteSpace(p.SourceVersion);
        private static bool ProvenSource(MaterialSource s) => s.Verified && !string.IsNullOrWhiteSpace(s.SourceVersion);

        public ProfessionProgress CalculateProgress()
        {
            var g = _r.Goal;
            long? target = null, needed = null, crafts = null;
            if (_r.XpTable is { Verified: true, Version.Length: > 0 } xp)
            {
                var table = xp.Thresholds.OrderBy(x => x.Level).ToArray();
                if (table.Select(t => t.CumulativeXp).SequenceEqual(table.Select(t => t.CumulativeXp).OrderBy(x => x)) &&
                    table.FirstOrDefault(t => t.Level == g.TargetLevel) is { } atTarget)
                {
                    target = atTarget.CumulativeXp;
                    if (g.CurrentXpVerified && g.CurrentXp is { } current &&
                        table.FirstOrDefault(t => t.Level == g.StartLevel) is { } atStart &&
                        current >= atStart.CumulativeXp && current <= atTarget.CumulativeXp)
                        needed = Math.Max(0, target.Value - current);
                    else Warn("XP courant/borne de niveau de départ absent ou incompatible.");
                }
                else Warn("Table d'XP non monotone ou seuil du niveau cible absent.");
            }
            else Warn("Table XP/version vérifiée absente : aucune estimation du 20→30.");
            if (needed is { } left && _training is { } recipe && ProvenRecipe(recipe) &&
                recipe.XpVerified && recipe.XpPerCraft is > 0 &&
                string.Equals(recipe.SourceVersion, _r.XpTable?.Version, StringComparison.Ordinal) &&
                _r.Goal.JobId is { } jobId && recipe.JobId == jobId &&
                recipe.RequiredLevel <= g.StartLevel)
                crafts = checked(left / recipe.XpPerCraft.Value + (left % recipe.XpPerCraft.Value == 0 ? 0 : 1));
            else if (needed is not null && _r.TrainingRecipeId is not null)
                Warn("Gain XP par craft, provenance, métier ou niveau requis non attestés.");
            return new(target, needed, crafts, null, null, false);
        }

        public long? PotentialXp(long? crafts) =>
            crafts is { } qty && _training is { XpVerified: true, XpPerCraft: > 0 } recipe &&
            ProvenRecipe(recipe) && string.Equals(recipe.SourceVersion, _r.XpTable?.Version, StringComparison.Ordinal) &&
            _r.Goal.JobId is { } jobId &&
            recipe.JobId == jobId && recipe.RequiredLevel <= _r.Goal.StartLevel
                ? checked(qty * recipe.XpPerCraft!.Value) : null;
        public long? PotentialFinalXp(long? crafts) =>
            _r.Goal.CurrentXpVerified && _r.Goal.CurrentXp is { } current &&
            PotentialXp(crafts) is { } gain ? checked(current + gain) : null;
        public bool CanReachTarget(long? count, ProfessionProgress progress) =>
            progress.TargetXp is { } target && PotentialFinalXp(count) is { } final && final >= target;

        public void ExpandTraining(long recipeId, long crafts)
        {
            if (_training is not { } r || r.RecipeId != recipeId)
            {
                Unknown(0, crafts, "Recette d'entraînement absente.", Array.Empty<AcquisitionOption>());
                return;
            }
            Craft(r, crafts, new HashSet<long>(), 0);
        }

        private int Add(PlanStepKind kind, long gid, long quantity, long? recipeId, string detail,
            IEnumerable<int>? deps, bool verified, decimal? kamas, long? seconds,
            IReadOnlyList<AcquisitionOption>? options = null)
        {
            if (_steps.Count >= MaxNodes) throw new ArgumentException("Graphe dépassant 2 048 étapes.");
            int id = _steps.Count + 1;
            _steps.Add(new(id, kind, gid, quantity, recipeId, detail, (deps ?? Array.Empty<int>()).ToArray(),
                verified, kamas, seconds, options ?? Array.Empty<AcquisitionOption>()));
            return id;
        }
        private int Unknown(long gid, long quantity, string reason, IReadOnlyList<AcquisitionOption> alternatives)
        {
            Warn(reason);
            return Add(PlanStepKind.Unresolved, gid, quantity, null, reason, null, false, null, null, alternatives);
        }

        public IReadOnlyList<int> Expand(long gid, long requested, HashSet<long> path, int depth)
        {
            if (requested == 0) return Array.Empty<int>();
            if (requested < 0 || requested > MaxQuantity) throw new ArgumentOutOfRangeException(nameof(requested));
            var ids = new List<int>();
            _remaining.TryGetValue(gid, out long available);
            long taken = Math.Min(available, requested);
            if (taken > 0)
            {
                _remaining[gid] = available - taken;
                _used.TryGetValue(gid, out long prior);
                _used[gid] = checked(prior + taken);
                ids.Add(Add(PlanStepKind.UseInventory, gid, taken, null, "Réservé dans le stock fourni.", null, true, 0, 0));
            }
            long need = requested - taken;
            if (need == 0) return ids;
            if (depth >= MaxDepth || path.Contains(gid))
            {
                ids.Add(Unknown(gid, need, "Cycle de recettes ou profondeur maximale : choix manuel requis.", Array.Empty<AcquisitionOption>()));
                return ids;
            }
            _recipes.TryGetValue(gid, out var recipes);
            _sources.TryGetValue(gid, out var sources);
            recipes ??= new();
            sources ??= new();
            var options = recipes.Select(r => new AcquisitionOption(AcquisitionMethod.Craft, r.RecipeId,
                    r.SourceVersion, ProvenRecipe(r), r.KamasPerCraft, r.SecondsPerCraft))
                .Concat(sources.Select(s => new AcquisitionOption(s.Method, null, s.SourceVersion,
                    ProvenSource(s), s.KamasPerUnit, s.SecondsPerUnit))).ToArray();
            var verifiedRecipes = recipes.Where(ProvenRecipe).ToList();
            var verifiedSources = sources.Where(ProvenSource).ToList();
            ProfessionRecipe? chosenRecipe = null;
            IReadOnlyList<MaterialSource>? candidateSources = null;
            if (_r.RecipeChoices?.TryGetValue(gid, out long forcedRecipe) == true)
                chosenRecipe = verifiedRecipes.FirstOrDefault(x => x.RecipeId == forcedRecipe);
            else if (_r.MethodChoices?.TryGetValue(gid, out var method) == true)
            {
                if (method == AcquisitionMethod.Craft && verifiedRecipes.Count == 1)
                    chosenRecipe = verifiedRecipes[0];
                else if (method != AcquisitionMethod.Craft)
                    candidateSources = verifiedSources.Where(s => s.Method == method).ToArray();
            }
            else if (verifiedRecipes.Count == 1 && sources.Count == 0 && recipes.Count == 1)
                chosenRecipe = verifiedRecipes[0];
            else if (recipes.Count == 0 && sources.Count == 1 && verifiedSources.Count == 1)
                candidateSources = verifiedSources;
            else if (recipes.Count == 0 && verifiedSources.Count == sources.Count)
                candidateSources = verifiedSources;

            if (chosenRecipe is not null)
            {
                long crafts = checked(need / chosenRecipe.ResultQty + (need % chosenRecipe.ResultQty == 0 ? 0 : 1));
                ids.Add(Craft(chosenRecipe, crafts, new HashSet<long>(path), depth + 1));
                // Résultat du craft indivisible : conserver son surplus pour un autre branchement.
                // Ne jamais créer un surplus si le craft est bloqué/cyclique.
                if (_steps[ids[^1] - 1] is { Kind: PlanStepKind.Craft, Verified: true })
                {
                    long surplus = checked(checked(crafts * chosenRecipe.ResultQty) - need);
                    _remaining.TryGetValue(gid, out long previous);
                    _remaining[gid] = checked(previous + surplus);
                }
            }
            else if (candidateSources is not null &&
                     TryAllocateSources(candidateSources, need, out var allocations))
            {
                foreach (var (source, quantity) in allocations)
                {
                    var kind = source.Method switch
                    {
                        AcquisitionMethod.Gather => PlanStepKind.Gather,
                        AcquisitionMethod.Drop => PlanStepKind.Drop,
                        _ => PlanStepKind.Buy
                    };
                    ids.Add(Add(kind, gid, quantity, null,
                        "OPTION CONSULTATIVE — " + source.SourceVersion, null, true,
                        source.KamasPerUnit is { } cost ? checked(cost * quantity) : null,
                        source.SecondsPerUnit is { } sec ? checked(sec * quantity) : null, options));
                    _sourceAllocations.TryGetValue(source, out long used);
                    _sourceAllocations[source] = checked(used + quantity);
                }
            }
            else ids.Add(Unknown(gid, need, options.Length == 0
                ? $"Aucune recette ou source prouvée pour GID {gid}."
                : $"Alternatives non comparables, capacité insuffisante ou choix non vérifié pour GID {gid}.", options));
            return ids;
        }

        private bool TryAllocateSources(IReadOnlyList<MaterialSource> sources, long required,
            out IReadOnlyList<(MaterialSource Source, long Quantity)> allocations)
        {
            allocations = Array.Empty<(MaterialSource, long)>();
            if (sources.Count == 0 || sources.Any(s => !ProvenSource(s))) return false;
            // A single explicitly selected source may have unknown price/time. Multiple
            // sources must be comparable before choosing a split or a winner.
            if (sources.Count > 1 && sources.Any(s => s.SecondsPerUnit is null ||
                                                     s.KamasPerUnit is null)) return false;
            var prefs = _r.Preferences ?? new ProfessionPreferences();
            // Ordered by evidenced unit cost/time and stable provenance. A scarce cheap
            // source can supply part of the need; the rest comes from other proven sources.
            var ordered = sources.Count == 1 ? sources : sources
                .OrderBy(s => checked(s.KamasPerUnit!.Value * prefs.KamasWeight +
                                      s.SecondsPerUnit!.Value * prefs.TimeWeight))
                .ThenBy(s => s.Method).ThenBy(s => s.SourceVersion, StringComparer.Ordinal).ToArray();
            long left = required;
            var pending = new List<(MaterialSource Source, long Quantity)>();
            foreach (var source in ordered)
            {
                _sourceAllocations.TryGetValue(source, out long alreadyAllocated);
                long available = source.MaxUnits is { } cap ? cap - alreadyAllocated : left;
                long take = Math.Min(left, Math.Max(0, available));
                if (take == 0) continue;
                pending.Add((source, take));
                left -= take;
                if (left == 0) break;
            }
            // Commit nothing unless all of this occurrence can be sourced. An unresolved
            // branch must not reserve an arbitrary fraction of the remaining capacity.
            if (left != 0) return false;
            allocations = pending;
            return true;
        }

        private int Craft(ProfessionRecipe recipe, long crafts, HashSet<long> path, int depth)
        {
            long produced = checked(crafts * recipe.ResultQty);
            if (produced > MaxQuantity) return Unknown(recipe.ResultGid, produced,
                "Quantité produite dépassant la limite du planificateur.", Array.Empty<AcquisitionOption>());
            if (!ProvenRecipe(recipe))
                return Unknown(recipe.ResultGid, produced, "Recette non attestée / version source absente.", Array.Empty<AcquisitionOption>());
            int level = recipe.JobId is { } job
                ? (_r.Goal.JobId == job ? _r.Goal.StartLevel :
                   _r.KnownJobLevels?.TryGetValue(job, out int other) == true ? other : 0)
                : 0;
            if (level < recipe.RequiredLevel)
                return Unknown(recipe.ResultGid, produced, "Niveau métier ou spécialité requis non acquis.", Array.Empty<AcquisitionOption>());
            if (depth >= MaxDepth || (!path.Add(recipe.ResultGid) && depth > 0))
                return Unknown(recipe.ResultGid, produced, "Cycle de recettes : confirmation humaine requise.", Array.Empty<AcquisitionOption>());
            var deps = new List<int>();
            foreach (var ingredient in recipe.Ingredients.OrderBy(i => i.ItemGid))
                deps.AddRange(Expand(ingredient.ItemGid, checked(ingredient.Quantity * crafts),
                    path, depth + 1));
            return Add(PlanStepKind.Craft, recipe.ResultGid, produced, recipe.RecipeId,
                $"Projet de {crafts} craft(s) × {recipe.ResultQty}; provenance {recipe.SourceVersion}.",
                deps, deps.All(id => _steps[id - 1].Verified),
                recipe.KamasPerCraft is { } money ? checked(money * crafts) : null,
                recipe.SecondsPerCraft is { } seconds ? checked(seconds * crafts) : null);
        }

        public ProfessionPlan Finish(ProfessionProgress progress)
        {
            decimal kamas = 0;
            long seconds = 0;
            bool costKnown = true, timeKnown = true;
            foreach (var step in _steps)
            {
                if (step.Kamas is { } k) kamas = checked(kamas + k);
                else costKnown = false;
                if (step.Seconds is { } t) seconds = checked(seconds + t);
                else timeKnown = false;
            }
            bool full = _steps.All(s => s.Verified && s.Kind != PlanStepKind.Unresolved);
            return new(_r.Goal, progress, _steps.AsReadOnly(),
                new SortedDictionary<long, long>(_used), new SortedDictionary<long, long>(_remaining),
                _warnings.AsReadOnly(), kamas, seconds,
                full && costKnown ? kamas : null, full && timeKnown ? seconds : null,
                full && _warnings.Count == 0, false, false);
        }
    }
}
