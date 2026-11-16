using System;

namespace GivreClone.App.Services.Combat;

/// <summary>Pause humaine entre deux combats. Les 3 suspensions temporaires de 5 min observées (04-05/11/2026) ont toutes
/// suivi 6 à 8 combats éclair enchaînés à 2-6 s d'intervalle, le serveur coupant la session juste après une fin de combat.
/// Pur : testable sans horloge réelle.</summary>
public static class FightPacing
{
    /// <summary>Durée d'attente restante (ms) avant d'engager le combat suivant, ou 0. <paramref name="lastFightEndTick"/> = 0 :
    /// aucun combat terminé dans ce run, on engage tout de suite. Bornes en secondes ; min &gt; max est corrigé, négatif = 0.</summary>
    public static int RemainingPauseMs(long lastFightEndTick, long nowTick, int minSeconds, int maxSeconds, Random random)
    {
        if (lastFightEndTick <= 0) return 0;
        int lo = Math.Max(0, Math.Min(minSeconds, maxSeconds)), hi = Math.Max(0, Math.Max(minSeconds, maxSeconds));
        if (hi == 0) return 0;
        long target = random.Next(lo * 1000, hi * 1000 + 1);
        long elapsed = nowTick - lastFightEndTick;
        if (elapsed < 0) elapsed = 0;
        return (int)Math.Max(0, target - elapsed);
    }
}
