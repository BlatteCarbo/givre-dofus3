namespace GivreClone.App.Services.Combat;

public static class SpellReach
{
    public static int Effective(int baseRange, int minRange, bool boostable, int rangeStat)
        => Math.Max(minRange, baseRange + (boostable ? rangeStat : 0));

    public static string Describe(int spellId, int baseRange, int minRange, bool boostable, int rangeStat)
    {
        int eff = Effective(baseRange, minRange, boostable, rangeStat);
        string mod = !boostable ? "non modifiable" : rangeStat == 0 ? "PO +0" : $"PO {(rangeStat > 0 ? "+" : "")}{rangeStat}";
        string floor = boostable && baseRange + rangeStat < minRange ? $", plancher {minRange}" : "";
        return $"{spellId}:{minRange}-{eff} (base {baseRange}, {mod}{floor})";
    }
}
