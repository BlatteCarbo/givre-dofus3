namespace GivreClone.Protocol;

/// <summary>PV courants et plafond érodé : ne jamais confondre perte et dégâts permanents.</summary>
public static class FightLifePolicy
{
    public static FighterStats ApplyGain(FighterStats stats, int amount)
        => stats with { Life = (int)System.Math.Clamp((long)stats.Life + System.Math.Max(0, amount), 0L, System.Math.Max(0, stats.MaxLife)) };

    public static FighterStats ApplyLoss(FighterStats stats, int loss, int permanentDamage, int shieldLoss = 0)
    {
        int max = System.Math.Max(0, stats.MaxLife - System.Math.Max(0, permanentDamage));
        int current = System.Math.Clamp(stats.Life - System.Math.Max(0, loss), 0, max);
        var characteristics = stats.Characteristics;
        if (characteristics.TryGetValue(FightStatsReader.IdShield, out int shield))
            characteristics = characteristics.SetItem(FightStatsReader.IdShield,
                System.Math.Max(0, shield - System.Math.Max(0, shieldLoss)));
        return stats with { Life = current, MaxLife = max, Characteristics = characteristics };
    }
}
