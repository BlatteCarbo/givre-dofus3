using System.Globalization;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Calculs d'affichage des lignes HDV (scan, achat, relist). Sans dépendance UI pour rester testable.
/// Les NOMS de membres des lignes viennent de l'original v1.3.1 (métadonnées de bindings lues dans un dump mémoire, MZP23) ;
/// les RÈGLES ci-dessous sont déduites de ces noms et du reste de l'HDV du clone : à confirmer en jeu, pas observées.
/// </summary>
public static class HdvRowLogic
{
    public const string None = "—";

    public static string Kamas(long v) => v.ToString("N0", CultureInfo.CurrentUICulture);
    public static string KamasOrNone(long v) => v > 0 ? Kamas(v) : None;
    public static string Lot(int qty) => "x" + qty;

    /// <summary>Gain signé « +1 200 » / « -300 » / « 0 ».</summary>
    public static string SignedKamas(long v) => (v > 0 ? "+" : "") + Kamas(v);

    /// <summary>Gain relatif au coût, une décimale : « +12,5 % ». Coût inconnu (≤ 0) → tiret.</summary>
    public static string GainPct(long cost, long value)
    {
        if (cost <= 0) return None;
        double pct = (value - cost) * 100.0 / cost;
        return (pct > 0 ? "+" : "") + pct.ToString("0.0", CultureInfo.CurrentUICulture) + " %";
    }

    /// <summary>« Niv. 120 » ; niveau inconnu (≤ 0) → vide.</summary>
    public static string Level(int level) => level > 0 ? "Niv. " + level.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>Situation d'une ligne d'achat par rapport au besoin : manque, exact, surplus (stock + achats vs besoin).</summary>
    public enum Coverage { Missing, Exact, Surplus }

    public static Coverage CoverageOf(int needed, int owned, int bought)
    {
        int have = owned + bought;
        return have < needed ? Coverage.Missing : have == needed ? Coverage.Exact : Coverage.Surplus;
    }

    /// <summary>Reste à acheter (jamais négatif).</summary>
    public static int ToBuy(int needed, int owned, int bought) => System.Math.Max(0, needed - owned - bought);

    /// <summary>Solde signé stock + achats − besoin.</summary>
    public static long Balance(int needed, int owned, int bought) => (long)owned + bought - needed;

    /// <summary>Un lot dépasse le prix maximum si un maximum est fixé (&gt; 0) et que le prix du lot le dépasse.</summary>
    public static bool OverMax(long lotPrice, long max) => max > 0 && lotPrice > max;
}
