using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Une ligne de caractéristique à briser. <see cref="UnitWeight"/> = poids par point (EffectData.effectPowerRate, cf. <see cref="FmStat"/>) ;
/// <see cref="RuneValuePerWeight"/> = kamas bruts d'un point de poids en rune (null = prix de rune inconnu : jamais remplacé par 0).</summary>
public sealed record BrisageStatLine(string StatKey, int Value, double UnitWeight, decimal? RuneValuePerWeight)
{
    /// <summary>Ligne branchée sur <see cref="RuneCatalog"/> : la quantité de la rune de base vient du catalogue (Ba = Base points), donc
    /// valeur par poids = prix(Ba) / (Base × poids par point). Famille inconnue, poids nul ou prix absent => valeur inconnue (null).</summary>
    public static BrisageStatLine FromRune(int effectId, int value, double unitWeight, decimal? baseRunePrice)
    {
        var family = RuneCatalog.Of(effectId);
        decimal? perWeight = family is not null && baseRunePrice is > 0 && unitWeight > 0
            ? baseRunePrice.Value / (RuneCatalog.Amount(family, "Ba") * (decimal)unitWeight)
            : null;
        return new BrisageStatLine(family?.Label ?? "effet " + effectId, value, unitWeight, perWeight);
    }
}

public enum BrisageChoice { Undetermined, NoFocus, Focus }

public enum BrisageClass { Unknown, Anchor, Balanced, Speculative }

/// <summary>Poids total obtenu en visant la ligne <see cref="StatKey"/> et sa valeur nette en kamas (null si le prix de cette rune est inconnu).</summary>
public sealed record BrisageFocusCandidate(string StatKey, double TotalWeight, decimal? NetKama);

/// <summary>Résultat consultatif. <see cref="KnownComplete"/> est faux dès qu'une ligne pesant quelque chose n'a pas de prix de rune :
/// aucun choix n'est alors affirmé (<see cref="BrisageChoice.Undetermined"/>) et les valeurs dépendantes restent null.</summary>
public sealed record BrisageFocusEvaluation(int Level, double CoeffPercent, double NoFocusWeight, decimal? NoFocusNetKama,
    IReadOnlyList<BrisageFocusCandidate> Candidates, BrisageChoice Choice, string? FocusStatKey, decimal? BestNetKama, decimal? NetProfit,
    double? BreakEvenCoeffPercent, BrisageClass Classification, bool KnownComplete, IReadOnlyList<string> MissingPriceStats);

/// <summary>Textes d'affichage (français) d'une évaluation. <see cref="Complete"/> est faux si un prix de rune manque : le verdict le dit au lieu d'inventer un choix.</summary>
public sealed record BrisageFocusSummary(string Verdict, string BreakEven, string ClassLabel, string Profit, bool Complete);

/// <summary>Brisage : Focus vs sans Focus, valeur attendue en kamas. Pure : aucun accès HDV ni jeu, que des prix déjà observés.
/// Modèle de la recherche 02 (Y_i = L/200 × valeur × poids × coeff/100 ; Focus k : Y_k + η × Σ_{j≠k} Y_j, η = 0,50) : approximation
/// communautaire, pas la formule serveur ; le coefficient réel est caché et bouge à chaque brisage. Valeur de revente nette = brut × (1 − taxe).</summary>
public static class BrisageFocusAdvisor
{
    public const double DefaultFocusRetention = 0.50;
    public const decimal DefaultTaxRate = 0.02m;
    public const double AnchorMaxPercent = 60;
    public const double BalancedMaxPercent = 150;

    /// <summary>Lignes d'un objet à partir de ses effets observés (id d'effet, jet) et des poids du jeu (EffectData.effectPowerRate) ; le prix de la rune de base
    /// (« Rune Fo ») vient de `baseRunePrice` (null = inconnu). Jets nuls ou négatifs, effets sans poids ou doublons sont ignorés ; un effet pondéré sans
    /// rune du <see cref="RuneCatalog"/> garde un prix inconnu.</summary>
    public static IReadOnlyList<BrisageStatLine> BuildLines(IEnumerable<(int EffectId, int Value)> effects, IReadOnlyDictionary<int, decimal> ratePerPoint,
        Func<string, decimal?> baseRunePrice)
    {
        var res = new List<BrisageStatLine>();
        var seen = new HashSet<int>();
        foreach (var (id, value) in effects)
        {
            if (value <= 0 || !ratePerPoint.TryGetValue(id, out var rate) || rate <= 0 || !seen.Add(id)) continue;
            var family = RuneCatalog.Of(id);
            res.Add(BrisageStatLine.FromRune(id, value, (double)rate, family is null ? null : baseRunePrice(RuneCatalog.ItemName(family, "Ba"))));
        }
        return res;
    }

    public static BrisageClass Classify(double breakEvenCoeffPercent)
        => breakEvenCoeffPercent <= AnchorMaxPercent ? BrisageClass.Anchor
         : breakEvenCoeffPercent <= BalancedMaxPercent ? BrisageClass.Balanced : BrisageClass.Speculative;

    /// <summary>Verdict lisible. Sans coût saisi (<paramref name="costKnown"/> faux), pas de seuil ni de classe : le choix Focus reste affiché, il ne dépend pas du coût.</summary>
    public static BrisageFocusSummary Summarize(BrisageFocusEvaluation e, bool costKnown)
    {
        string verdict = !e.KnownComplete ? "Prix rune manquant : " + string.Join(", ", e.MissingPriceStats)
            : e.Candidates.Count == 0 ? "Rien à briser (aucune ligne pondérée)"
            : e.Choice == BrisageChoice.Focus ? "Focus recommandé : " + e.FocusStatKey
            : "Sans focus";
        bool be = costKnown && e.BreakEvenCoeffPercent is not null;
        string breakEven = be ? $"Seuil C_be {e.BreakEvenCoeffPercent:0} %" : costKnown ? "Seuil C_be : —" : "Seuil C_be : saisis un coût";
        string label = !be ? "—" : e.Classification switch
        {
            BrisageClass.Anchor => "Ancre", BrisageClass.Balanced => "Équilibré", BrisageClass.Speculative => "Spéculatif", _ => "—",
        };
        string profit = costKnown && e.NetProfit is { } p ? "Gain net estimé " + (p >= 0 ? "+" : "") + p.ToString("N0") + " k" : "";
        return new BrisageFocusSummary(verdict, breakEven, label, profit, e.KnownComplete);
    }

    public static BrisageFocusEvaluation Evaluate(int itemLevel, IReadOnlyList<BrisageStatLine> lines, decimal craftCost, double coeffPercent = 100,
        double focusRetention = DefaultFocusRetention, decimal taxRate = DefaultTaxRate)
    {
        if (itemLevel is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(itemLevel), "1..200");
        if (coeffPercent < 0) throw new ArgumentOutOfRangeException(nameof(coeffPercent));
        if (focusRetention is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(focusRetention));
        if (taxRate is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(taxRate));
        if (craftCost < 0) throw new ArgumentOutOfRangeException(nameof(craftCost));

        // Tout est calculé à 100 % : la valeur est linéaire en coeff, donc le choix Focus / sans Focus ne dépend pas du coefficient.
        double scale = coeffPercent / 100.0;
        var weighted = lines.Select(l => (Line: l, Y: l.Value > 0 && l.UnitWeight > 0 ? itemLevel / 200.0 * l.Value * l.UnitWeight : 0.0))
            .Where(x => x.Y > 0).ToArray();
        double total = weighted.Sum(x => x.Y);
        decimal net = 1m - taxRate;

        var missing = weighted.Where(x => x.Line.RuneValuePerWeight is not > 0).Select(x => x.Line.StatKey).ToArray();
        decimal? noFocus100 = missing.Length == 0 ? weighted.Sum(x => (decimal)x.Y * x.Line.RuneValuePerWeight!.Value) * net : null;

        var cands100 = weighted.Select(k =>
        {
            double w = k.Y + focusRetention * (total - k.Y);
            decimal? v = k.Line.RuneValuePerWeight is > 0 ? (decimal)w * k.Line.RuneValuePerWeight.Value * net : null;
            return (Key: k.Line.StatKey, W: w, V: v);
        }).ToArray();

        bool complete = missing.Length == 0;
        var choice = BrisageChoice.Undetermined;
        string? focusKey = null;
        decimal? best100 = null;
        if (complete)
        {
            if (weighted.Length == 0) { choice = BrisageChoice.NoFocus; best100 = 0m; }
            else
            {
                var top = cands100.OrderByDescending(c => c.V!.Value).First();
                if (top.V!.Value > noFocus100!.Value) { choice = BrisageChoice.Focus; focusKey = top.Key; best100 = top.V; }
                else { choice = BrisageChoice.NoFocus; best100 = noFocus100; }
            }
        }

        decimal s = (decimal)scale;
        double? be = best100 is > 0 ? (double)(craftCost / best100.Value) * 100.0 : null;
        return new BrisageFocusEvaluation(itemLevel, coeffPercent, total * scale, noFocus100 * s,
            cands100.Select(c => new BrisageFocusCandidate(c.Key, c.W * scale, c.V * s)).ToArray(),
            choice, focusKey, best100 * s, best100 * s - craftCost, be, be is { } b ? Classify(b) : BrisageClass.Unknown, complete, missing);
    }
}
