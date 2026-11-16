using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.DofusDbPartner;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Services;

/// <summary>
/// Explicit, consultative-only adapter: DofusDB source records are never
/// promoted to game-version-verified recipes or executable bot instructions.
/// No networking or game client activity takes place here.
/// </summary>
internal static class DofusDbPartnerUiBridge
{
    private static readonly object Gate = new();
    private static Task? _loading;
    private static DofusDbPartnerCatalog? _catalog;
    private static string _status = "Catalogue DofusDB non chargé";

    public static string Status { get { lock (Gate) return _status; } }

    public static DofusDbPartnerCatalog? Catalog
    {
        get { lock (Gate) return _catalog; }
    }

    public static Task StartLoadAsync()
    {
        lock (Gate)
        {
            if (_loading is not null) return _loading;
            if (!string.Equals(Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_OPERATOR_REVIEW"),
                    "1", StringComparison.Ordinal))
            {
                _status = "Accès DofusDB non activé pour cette session."
                    + " Configure GIVRE_DOFUSDB_OPERATOR_REVIEW=1 pour une revue locale.";
                return Task.CompletedTask;
            }
            string? root = DofusDbReviewDiscovery.FindDefaultCache();
            if (root is null)
            {
                _status = "Cache DofusDB introuvable (GIVRE_DOFUSDB_REVIEW_DIR).";
                return Task.CompletedTask;
            }
            _status = "Vérification des pages et empreintes DofusDB…";
            _loading = Task.Run(() =>
            {
                DofusDbPartnerLoadResult result;
                try
                {
                    result = DofusDbPartnerCatalogReader.LoadDirectory(root,
                        new DofusDbPartnerAdmission(
                            OperatorApprovedRuntimeUse: true,
                            OperatorApprovedAiMcpUse: false,
                            OperatorEvidenceId: Environment.GetEnvironmentVariable(
                                "GIVRE_DOFUSDB_OPERATOR_EVIDENCE_ID") ?? "partner-user-confirmed-20261020"));
                }
                catch (Exception)
                {
                    lock (Gate) _status = "Catalogue DofusDB indisponible : import rejeté.";
                    return;
                }
                lock (Gate)
                {
                    _catalog = result.Success ? result.Catalog : null;
                    _status = _catalog is { } cat
                        ? $"DofusDB : {cat.ItemCount:N0} objets, {cat.SetCount:N0} panoplies, " +
                          $"{cat.RecipeCount:N0} recettes · SHA local contrôlé · version du jeu INCONNUE"
                        : "DofusDB refusé : " + result.Reason;
                }
            });
            return _loading;
        }
    }

    public static IReadOnlyList<ItemCraftCatalogEntry> SearchCatalog(string? query, string? mode,
        IReadOnlyList<ItemCraftTarget>? selected = null)
    {
        var catalog = Catalog;
        if (catalog is null) return Array.Empty<ItemCraftCatalogEntry>();
        var phrase = (query ?? "").Trim();
        if (phrase.Length > 90) return Array.Empty<ItemCraftCatalogEntry>();
        string provenance = $"DofusDB · capture locale SHA {catalog.Provenance.ReviewSummarySha256[..12]}… " +
                            "· version du jeu NON vérifiée · consultation uniquement";
        var existing = selected ?? Array.Empty<ItemCraftTarget>();
        var retainedSets = existing.Select(x => x.GroupName)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sets = catalog.SearchSets(mode == ItemCraftPlannerViewModel.SetMode ? phrase : "", limit: 40)
            .Where(s => mode == ItemCraftPlannerViewModel.SetMode || retainedSets.Contains(s.NameFr ?? ""))
            .Where(s => s.MemberItemGids.Count > 0 &&
                        s.MemberItemGids.All(id => catalog.GetItem(id) is not null))
            .Take(25).ToList();
        foreach (var group in retainedSets)
        {
            var match = catalog.SearchSets(group, 10).FirstOrDefault(x =>
                string.Equals(x.NameFr, group, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match.MemberItemGids.Count > 0 &&
                match.MemberItemGids.All(id => catalog.GetItem(id) is not null) &&
                sets.All(x => x.Id != match.Id) && sets.Count < 25)
                sets.Add(match);
        }
        var selectedIds = existing.Select(x => x.ItemId)
            .Where(x => long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToHashSet();
        var mandatoryIds = sets.SelectMany(x => x.MemberItemGids).Concat(selectedIds).Distinct().Take(220).ToArray();
        var items = mandatoryIds.Select(catalog.GetItem).Where(x => x is not null).Cast<DofusDbPartnerItem>().ToList();
        if (mode != ItemCraftPlannerViewModel.SetMode)
        {
            foreach (var item in catalog.SearchItems(phrase, limit: 100))
                if (items.All(x => x.Id != item.Id)) items.Add(item);
        }
        return items.Take(250).Select(x => new ItemCraftCatalogEntry(
                x.Id.ToString(CultureInfo.InvariantCulture), x.NameFr ?? x.NameEn ?? $"Objet {x.Id}",
                "Objet", provenance, IsVerified: true))
            .Concat(sets.Select(s => new ItemCraftCatalogEntry(
                s.Id.ToString(CultureInfo.InvariantCulture), s.NameFr ?? s.NameEn ?? $"Panoplie {s.Id}",
                "Panoplie", provenance, IsVerified: true,
                MemberItemIds: s.MemberItemGids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray())))
            .Take(300).ToArray();
    }

    public static ItemCraftPlanSnapshot? BuildReviewPlan(ItemCraftPlannerRequest request)
    {
        var catalog = Catalog;
        if (catalog is null || request.Targets.Count is 0 or > 60) return null;
        var graph = new List<ItemCraftPlanNode>();
        var seen = new HashSet<long>();
        const int maxNodes = 240;

        void Expand(long gid, int depth, string rootKey)
        {
            if (depth >= 5 || graph.Count >= maxNodes || !seen.Add(gid)) return;
            var recipe = catalog.GetRecipesForItem(gid).FirstOrDefault();
            if (recipe is null) return;
            foreach (var ing in recipe.Ingredients.Take(32))
            {
                if (graph.Count >= maxNodes) break;
                var item = catalog.GetItem(ing.ItemGid);
                string name = item?.NameFr ?? item?.NameEn ?? $"Objet {ing.ItemGid}";
                graph.Add(new ItemCraftPlanNode(
                    $"{rootKey}:{recipe.SourceRecipeId}:{ing.ItemGid}:{graph.Count}", name,
                    depth + 1, "Ingrédient / craft", ing.Quantity is > 0
                        ? $"{ing.Quantity.Value:N0} / fabrication" : "Inconnue",
                    "Non vérifié", "Total inconnu",
                    "Rendement final non documenté : ce besoin est PAR fabrication, pas un total à acheter.",
                    $"DofusDB recette {recipe.SourceRecipeId} · version UNKNOWN"));
                Expand(ing.ItemGid, depth + 1, rootKey);
            }
        }

        foreach (var target in request.Targets)
        {
            if (graph.Count >= maxNodes) break;
            if (!long.TryParse(target.ItemId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long gid) || gid <= 0 ||
                catalog.GetItem(gid) is not { } item || target.Quantity is < 1 or > 100_000)
                return null;
            seen.Clear();
            graph.Add(new ItemCraftPlanNode(
                $"dofusdb:target:{gid}:{graph.Count}", item.NameFr ?? item.NameEn ?? $"Objet {gid}",
                0, "Cible", $"× {target.Quantity:N0}", "Non vérifié", "Total inconnu",
                "Objectif déclaré par l'utilisateur. Décomposition informative uniquement.",
                "DofusDB capture locale · jeu/version non certifiés"));
            Expand(gid, 0, "item:" + gid);
        }

        return new ItemCraftPlanSnapshot(
            "DofusDB · aperçu des recettes et ressources",
            "Arbre consultatif : les quantités d'ingrédients concernent une fabrication, pas le nombre total nécessaire.",
            $"DofusDB / revue SHA {catalog.Provenance.ReviewSummarySha256[..12]}… / version UNKNOWN",
            IsVerified: false, IsFictional: false,
            CostLabel: "Prix HDV non observés", DurationLabel: "Inconnue",
            ItemsLabel: $"{request.Targets.Count} cible(s)",
            Nodes: graph,
            Warnings: "Aucune conversion automatique en action du bot : rendements, XP, version du jeu, " +
                      "stocks de comptes et prix HDV non certifiés. Les ingrédients sont PAR fabrication.",
            CoverageLabel: "REVUE D'APRÈS SOURCE, NON EXÉCUTABLE", IsComplete: false);
    }
}
