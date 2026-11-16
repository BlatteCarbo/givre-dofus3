using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Un lot visible à l'HDV : prix unitaire et nombre d'unités du lot.</summary>
public readonly record struct SnipeLot(decimal UnitPrice, long Units);

/// <summary>Réglages des garde-fous du sniper. Valeurs par défaut prudentes.</summary>
public sealed class SniperGuardSettings
{
    public double MinDiscountPercent { get; set; } = 25;        // le lot visé doit être au moins X % sous la médiane des AUTRES lots
    public int MinOtherLots { get; set; } = 4;                   // il faut assez d'autres lots pour que la médiane veuille dire quelque chose
    public double MaxReferenceSpreadPercent { get; set; } = 40;  // médiane et moyenne pondérée des autres lots doivent s'accorder (sinon marché truqué ou chaotique)
    public long MaxTotalUnitsVisible { get; set; } = 20_000;     // au-delà : objet de masse (eau, blé…) dont le prix se manipule trop facilement
    public double MaxLotShareOfDepthPercent { get; set; } = 60;  // un seul lot ne doit pas faire l'essentiel de l'offre (mur de vendeur)
    public double TaxPercent { get; set; } = 2;                  // taxe de mise en vente
    public double MinResaleMarginPercent { get; set; } = 10;     // marge nette mini à la revente, taxe déduite
    public long MaxSpendPerPurchase { get; set; } = 2_000_000;   // plafond de kamas d'un achat
    public long MaxSpendPerItem { get; set; } = 10_000_000;      // plafond cumulé par objet (session)
    public int MaxPurchasesPerItemPerHour { get; set; } = 6;     // limite de cadence : pas de rafale sur un même objet
    public HashSet<long> BlockedGids { get; set; } = new();      // objets jamais sniper
}

public sealed record SniperVerdict(bool Allowed, IReadOnlyList<string> Reasons, decimal? ReferencePrice, decimal? ExpectedMarginPercent);

/// <summary>Garde-fous anti-manipulation du sniper HDV (pur). Un sniper naïf achète tout lot « sous le prix » : on peut le piéger en truquant le marché (acheter tout un objet de masse puis jouer sur le prix,
/// poser un lot très bas à côté d'un mur de vendeur, gonfler la moyenne…). Ici on exige un marché assez épais et cohérent, un rabais net par rapport aux AUTRES lots, une marge de revente réelle (taxe incluse)
/// et des plafonds de dépense et de cadence. Le moindre doute refuse l'achat.</summary>
public static class SniperGuard
{
    public static SniperVerdict Evaluate(long gid, SnipeLot candidate, IReadOnlyList<SnipeLot> allVisibleLots, long spentOnItem, int purchasesLastHour, SniperGuardSettings s)
    {
        var no = new List<string>();
        if (s.BlockedGids.Contains(gid)) no.Add("objet interdit au sniper");
        if (candidate.UnitPrice <= 0 || candidate.Units <= 0) no.Add("lot invalide");

        // « autres lots » = tout sauf CE lot (le lot visé ne doit pas servir à calculer sa propre référence)
        var others = allVisibleLots.Where(l => !(l.UnitPrice == candidate.UnitPrice && l.Units == candidate.Units)).ToList();
        long totalUnits = allVisibleLots.Sum(l => l.Units);
        decimal? reference = null; decimal? margin = null;

        if (others.Count < s.MinOtherLots) no.Add($"marché trop mince ({others.Count} autre(s) lot(s), {s.MinOtherLots} requis)");
        else
        {
            decimal median = Median(others.Select(l => l.UnitPrice).ToList());
            decimal weighted = others.Sum(l => l.UnitPrice * l.Units) / Math.Max(1, others.Sum(l => l.Units));
            reference = median;
            if (median > 0 && (double)(Math.Abs(weighted - median) / median) * 100 > s.MaxReferenceSpreadPercent) no.Add("prix de référence instable (médiane et moyenne pondérée très éloignées : marché truqué ou chaotique)");
            if (median > 0)
            {
                double discount = (double)((median - candidate.UnitPrice) / median) * 100;
                if (discount < s.MinDiscountPercent) no.Add($"rabais insuffisant ({discount:0}% < {s.MinDiscountPercent:0}% sous la médiane)");
                decimal resale = median * (1 - (decimal)s.TaxPercent / 100m);
                margin = candidate.UnitPrice > 0 ? (resale - candidate.UnitPrice) / candidate.UnitPrice * 100m : null;
                if (margin is { } m && (double)m < s.MinResaleMarginPercent) no.Add($"marge de revente trop faible ({m:0}% après taxe)");
            }
        }

        if (totalUnits > s.MaxTotalUnitsVisible) no.Add($"objet de masse ({totalUnits} unités en vente) : prix trop facile à manipuler");
        if (totalUnits > 0 && (double)candidate.Units / totalUnits * 100 > s.MaxLotShareOfDepthPercent && allVisibleLots.Count > 1) no.Add("ce lot fait l'essentiel de l'offre (mur de vendeur)");

        decimal cost = candidate.UnitPrice * candidate.Units;
        if (cost > s.MaxSpendPerPurchase) no.Add($"achat trop gros ({cost:0} k > plafond {s.MaxSpendPerPurchase})");
        if (spentOnItem + cost > s.MaxSpendPerItem) no.Add("plafond de dépense par objet atteint");
        if (purchasesLastHour >= s.MaxPurchasesPerItemPerHour) no.Add("trop d'achats sur cet objet cette heure");

        return new SniperVerdict(no.Count == 0, no, reference, margin);
    }

    internal static decimal Median(IReadOnlyList<decimal> v)
    {
        var s = v.OrderBy(x => x).ToList();
        if (s.Count == 0) return 0;
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
