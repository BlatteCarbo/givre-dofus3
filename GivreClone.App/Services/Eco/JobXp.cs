using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Recette vue par le planificateur d'XP : niveau de l'objet fabriqué, ratio d'XP de l'objet (1 = 100 %) et ingrédients.</summary>
public sealed record XpRecipe(long Gid, string Name, int Level, double Ratio, IReadOnlyList<(long Gid, string Name, int Quantity)> Ingredients);

/// <summary>Un palier du plan : « fabrique N fois cette recette entre le niveau A et le niveau B ».</summary>
public sealed record XpStep(XpRecipe Recipe, int FromLevel, int ToLevel, long Crafts, long Xp, decimal? Cost);

public sealed record XpPlan(IReadOnlyList<XpStep> Steps, long TotalCrafts, long TotalXp, decimal? TotalCost, bool Reached, int FinalLevel,
                            IReadOnlyList<(long Gid, string Name, long Quantity, decimal? Cost)> Shopping);

/// <summary>XP des métiers : niveau ↔ expérience, gain d'un craft et simulation. Formules relevées dans dofus-miniatures (avec l'accord de son auteur) ; pur.
/// Niveau = ⌊(√(1 + 0,4·xp) + 1) / 2⌋ (1..200) ; xp(niveau) = 10·niveau·(niveau−1) ; gain = ⌊⌊20·niveauRecette·(épique ? 3 : 1) / (((niveau−niveauRecette)^1,1)/10 + 1) · ratio⌋ · coeff/100⌋,
/// nul si la recette est plus haute que le métier ou plus de 100 niveaux en dessous.</summary>
public static class JobXp
{
    public const int MaxLevel = 200, MaxCrafts = 99_999;

    public static int Level(long xp)
    {
        if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
        return Math.Clamp((int)Math.Floor((Math.Sqrt(1 + 0.4 * Math.Min(xp, 398_000)) + 1) / 2), 1, MaxLevel);
    }

    public static long Experience(int level)
    {
        if (level is < 1 or > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return 10L * level * (level - 1);
    }

    public static int CraftXp(XpRecipe r, int level, int coefficientPercent = 100, bool epic = false)
    {
        if (level is < 1 or > MaxLevel || r.Level < 1 || r.Level > level || level - r.Level > 100 || coefficientPercent is < 1 or > 600
            || double.IsNaN(r.Ratio) || r.Ratio < 0 || r.Ratio > 100) return 0;
        double baseXp = 20.0 * r.Level * (epic ? 3 : 1) / (Math.Pow(level - r.Level, 1.1) / 10 + 1);
        return (int)Math.Floor(Math.Floor(baseXp * r.Ratio) * (coefficientPercent / 100.0));
    }

    /// <summary>Plan pour passer de <paramref name="startXp"/> au niveau <paramref name="targetLevel"/>. À chaque niveau on choisit la recette la plus rentable :
    /// <c>cheapest</c> = coût par point d'XP le plus bas (recettes sans prix connu classées après), <c>fastest</c> = le plus d'XP par craft (peu importe le prix).
    /// Les crafts d'un même niveau sont regroupés ; les paliers consécutifs de même recette sont fusionnés.</summary>
    public static XpPlan Plan(IReadOnlyList<XpRecipe> recipes, Func<long, decimal?> unitPrice, long startXp, int targetLevel, bool cheapest = true,
                              int coefficientPercent = 100, bool epic = false)
    {
        long goal = Experience(targetLevel), xp = Math.Max(0, startXp), crafts = 0;
        decimal? Cost(XpRecipe r)
        {
            decimal sum = 0;
            foreach (var i in r.Ingredients) { if (unitPrice(i.Gid) is not { } p) return null; sum += p * i.Quantity; }
            return sum;
        }
        var costs = recipes.ToDictionary(r => r.Gid, Cost);
        var raw = new List<(XpRecipe R, int Level, long Crafts, long Xp, decimal? Cost, long XpAfter)>();
        while (xp < goal && crafts < MaxCrafts)
        {
            int level = Level(xp);
            (XpRecipe R, int Gain, decimal? Cost)? best = null;
            foreach (var r in recipes)
            {
                int gain = CraftXp(r, level, coefficientPercent, epic);
                if (gain <= 0) continue;
                var c = costs[r.Gid];
                bool better = best is null || (cheapest ? Rank(c, gain, best.Value.Cost, best.Value.Gain) : gain > best.Value.Gain || (gain == best.Value.Gain && c < best.Value.Cost));
                if (better) best = (r, gain, c);
            }
            if (best is not { } b) break;
            long boundary = Math.Min(goal, Experience(Math.Min(MaxLevel, level + 1)));
            long n = Math.Min((boundary - xp + b.Gain - 1) / b.Gain, MaxCrafts - crafts);
            if (n <= 0) break;
            xp += n * b.Gain; crafts += n;
            raw.Add((b.R, level, n, n * b.Gain, b.Cost is { } cc ? cc * n : null, xp));
        }
        var steps = new List<XpStep>();
        foreach (var s in raw)
        {
            if (steps.Count > 0 && steps[^1].Recipe.Gid == s.R.Gid)
            {
                var p = steps[^1];
                steps[^1] = p with { ToLevel = Level(s.XpAfter), Crafts = p.Crafts + s.Crafts, Xp = p.Xp + s.Xp, Cost = p.Cost is { } a && s.Cost is { } d ? a + d : null };
            }
            else steps.Add(new XpStep(s.R, s.Level, Level(s.XpAfter), s.Crafts, s.Xp, s.Cost));
        }
        decimal? total = steps.Count > 0 && steps.All(x => x.Cost is not null) ? steps.Sum(x => x.Cost!.Value) : null;
        var shopping = new Dictionary<long, (string Name, long Qty)>();
        foreach (var s in steps)
            foreach (var i in s.Recipe.Ingredients)
                shopping[i.Gid] = (i.Name, (shopping.TryGetValue(i.Gid, out var cur) ? cur.Qty : 0) + (long)i.Quantity * s.Crafts);
        return new XpPlan(steps, crafts, xp - Math.Max(0, startXp), total, xp >= goal, Level(xp),
            shopping.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Qty, unitPrice(kv.Key) is { } p ? p * kv.Value.Qty : (decimal?)null))
                    .OrderByDescending(x => x.Item4 ?? 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());

        // meilleur = coût par XP plus bas ; une recette sans prix passe après une recette avec prix, puis on retombe sur le gain
        static bool Rank(decimal? c, int gain, decimal? bestCost, int bestGain)
        {
            if (c is { } x && bestCost is { } y) return x / gain < y / bestGain;
            if (c is not null && bestCost is null) return true;
            if (c is null && bestCost is not null) return false;
            return gain > bestGain;
        }
    }
}
