namespace GivreClone.Engine.Recovery;

// Reconstructed local planning contract, not recovered native implementation.
public enum SmithRuneTier { Basic, Pa, Ra }
public sealed record SmithTarget(int EffectId, int Current, int Target, int NaturalMaximum,
    int PaFrom, int RaFrom);
public sealed record SmithRune(int ItemId, int EffectId, SmithRuneTier Tier, int Quantity, long UnitPrice, int BagQuantity = 0, int BankQuantity = 0)
{
    public string StockText => BankQuantity > 0
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} (sac {1}, banque {2})", Quantity, BagQuantity, BankQuantity)
        : Quantity.ToString(System.Globalization.CultureInfo.CurrentCulture);
}
public sealed record SmithMagicLimits(int MaxRunes, int MaxRunesPerLine, long MaxKamas,
    bool AllowSmallerRune);
public sealed record SmithMagicDecision(bool Ready, string Reason, SmithRune? Rune = null);

/// <summary>Deterministic next-rune suggestion from caller-supplied observations.
/// Does not predict fuse outcomes, infer rune weights, buy runes or send game messages.</summary>
public static class SmithMagicPlanner
{
    public static SmithMagicDecision Next(SmithTarget line, IEnumerable<SmithRune> runes,
        SmithMagicLimits limits, int usedTotal, int usedOnLine, long spent)
    {
        if (line.EffectId <= 0 || line.Target < 0 || line.NaturalMaximum < 0 ||
            line.PaFrom < 0 || line.RaFrom < line.PaFrom ||
            limits.MaxRunes <= 0 || limits.MaxRunesPerLine <= 0 || limits.MaxKamas < 0 ||
            usedTotal < 0 || usedOnLine < 0 || spent < 0)
            return new(false, "Configuration invalide.");
        if (line.Current >= line.Target) return new(false, "Objectif atteint.");
        if (usedTotal >= limits.MaxRunes || usedOnLine >= limits.MaxRunesPerLine)
            return new(false, "Limite de runes atteinte.");
        var desired = line.Current >= line.RaFrom ? SmithRuneTier.Ra :
            line.Current >= line.PaFrom ? SmithRuneTier.Pa : SmithRuneTier.Basic;
        var candidates = runes.Where(r => r.EffectId == line.EffectId && r.Quantity > 0 &&
            r.ItemId > 0 && r.UnitPrice >= 0 &&
            Enum.IsDefined(r.Tier) &&
            (r.Tier == desired || (limits.AllowSmallerRune && (int)r.Tier < (int)desired)))
            .OrderByDescending(r => r.Tier).ThenBy(r => r.UnitPrice).ThenBy(r => r.ItemId).ToArray();
        if (candidates.Length == 0) return new(false, "Runes manquantes.");
        // Subtraction avoids overflow for very large externally supplied prices.
        var rune = candidates.FirstOrDefault(r => spent <= limits.MaxKamas &&
            r.UnitPrice <= limits.MaxKamas - spent);
        return rune is null ? new(false, "Budget de runes atteint.") :
            new(true, "Suggestion locale ; aucune rune appliquée.", rune);
    }
}
