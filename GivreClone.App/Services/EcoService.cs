using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services;

/// <summary>Accès partagé de l'atelier éco (interface) et du MCP : prix connus (HDV observé + prix manuels), noms, recettes et conseil « fabriquer ou acheter ».
/// Lecture seule : ne demande rien à l'HDV, n'envoie rien au jeu.</summary>
public static class EcoService
{
    private static readonly object Gate = new();
    private static ManualPriceBook? _book;
    private static string PricesPath => Path.Combine(MZP19LocalClone.DataRoot, "eco", "prices.json");

    /// <summary>Prix saisis à la main (par serveur), chargés une fois.</summary>
    public static ManualPriceBook Book { get { lock (Gate) return _book ??= EcoJson.Load<ManualPriceBook>(PricesPath); } }

    public static void SavePrices()
    {
        lock (Gate)
        {
            if (_book is null) return;
            EcoJson.Save(PricesPath, _book);
        }
    }

    /// <summary>Miniature d'un objet tirée du bundle d'icônes du client installé (null tant que le bundle n'est pas lu ou si l'icône est introuvable).</summary>
    public static Avalonia.Media.Imaging.Bitmap? IconOf(long gid)
    {
        if (!Stores.InteractiveIcons.IsLoaded) return null;
        int icon = Stores.Launch.GameData?.ItemIconIdOf(gid) ?? 0;
        return icon > 0 ? Stores.InteractiveIcons.GetByIcon(icon) : null;
    }

    public static string NameOf(long gid) => Stores.Launch.GameData?.ItemName(gid) is { Length: > 0 } n ? n : Stores.Launch.ItemDisplayName(gid);

    /// <summary>Prix d'un objet : le plus récent entre l'HDV observé du bot `pid` (lot le moins cher, sinon moyenne) et le prix saisi à la main.</summary>
    public static PriceQuote? PriceFor(uint pid, long gid)
    {
        var l = Stores.Launch;
        PriceQuote? market = null;
        if (pid > 0)
        {
            long unit = l.Hdv.MarketFor(pid, gid, 0);
            if (unit > 0) { long age = l.Hdv.PriceAgeMs(pid, gid); market = new(unit, age == long.MaxValue ? null : age, "marché"); }
            else
            {
                long avg = l.Hdv.AverageFor(pid, gid);
                if (avg <= 0 && gid > 0 && gid <= int.MaxValue) avg = l.AveragePrice(pid, (int)gid);
                if (avg > 0) market = new(avg, null, "moyenne");
            }
        }
        var manual = Book.Get(pid > 0 ? l.ServerIdOf(pid) : 0, gid);
        if (manual is null) return market;
        if (market is null) return manual;
        return manual.AgeMs <= (market.AgeMs ?? long.MaxValue) ? manual : market;
    }

    private static readonly Dictionary<int, IReadOnlyList<XpRecipe>> JobRecipeCache = new();

    /// <summary>Recettes d'un métier vues par le planificateur d'XP (cache par métier ; niveaux de recette jusqu'à 200).</summary>
    public static IReadOnlyList<XpRecipe> JobRecipes(int jobId)
    {
        lock (JobRecipeCache)
        {
            if (JobRecipeCache.TryGetValue(jobId, out var hit)) return hit;
            if (Stores.Launch.GameData is not { } db) return Array.Empty<XpRecipe>();
            var list = new List<XpRecipe>();
            foreach (var r in db.RecipesOfJob(jobId, 200))
                list.Add(new XpRecipe(r.ResultId, r.Name, r.Level, db.CraftXpRatio(r.ResultId), r.Ingredients.Select(i => (i.Gid, i.Name, i.Quantity)).ToArray()));
            return JobRecipeCache[jobId] = list;
        }
    }

    /// <summary>Plan pour monter un métier de <paramref name="fromLevel"/> à <paramref name="toLevel"/> (hors ligne ; prix = HDV observé ou saisi à la main).</summary>
    public static XpPlan? JobPlan(uint pid, int jobId, int fromLevel, int toLevel, bool cheapest, int coefficientPercent = 100)
    {
        if (fromLevel is < 1 or > 199 || toLevel <= fromLevel || toLevel > 200) return null;
        var recipes = JobRecipes(jobId).Where(r => r.Level <= toLevel).ToList();
        return recipes.Count == 0 ? null : JobXp.Plan(recipes, g => PriceFor(pid, g)?.Unit, JobXp.Experience(fromLevel), toLevel, cheapest, Math.Clamp(coefficientPercent, 1, 600));
    }

    /// <summary>Position du bot pour classer les cartes de ressources : coordonnées et monde de sa carte actuelle ; null hors ligne ou carte inconnue.</summary>
    public static (int X, int Y, int? World)? OriginOf(uint pid)
    {
        var launch = Stores.Launch;
        if (pid == 0 || launch.CurrentMap(pid) is not long map) return null;
        return launch.ResourceOriginOf(map);
    }

    public static decimal SaleTaxPercent(uint pid) => pid > 0 ? (decimal)Math.Clamp(Stores.Launch.HdvTaxPercent(pid), 0f, 50f) : 0;

    /// <summary>Conseil « fabriquer ou acheter » pour `quantity` exemplaires de `gid`. Null si les données du jeu (dofus.sqlite) sont introuvables.</summary>
    public static CraftAdvice? Advice(uint pid, long gid, long quantity, IReadOnlyDictionary<long, CraftForce>? force = null)
    {
        if (Stores.Launch.GameData is not { } db) return null;
        var cache = new Dictionary<long, CraftScanRecipe?>();
        CraftScanRecipe? Recipe(long g)
        {
            if (cache.TryGetValue(g, out var c)) return c;
            RecipeInfo? ri = null;
            try { ri = db.Recipe(g); } catch { /* recette illisible : objet non fabricable */ }
            return cache[g] = ri is null ? null : new CraftScanRecipe(ri.ResultId, ri.Name, ri.Level, ri.JobId, ri.Ingredients.Select(i => new CraftScanIngredient(i.Gid, i.Quantity)).ToArray());
        }
        return CraftAdvisor.Analyze(gid, Math.Clamp(quantity, 1, 100_000), Recipe, g => PriceFor(pid, g), NameOf, force, SaleTaxPercent(pid));
    }
}
