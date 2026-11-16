using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Combat;

/// <summary>Garde de rythme Turbo. Les suspensions serveur de 5 min (06-07/11/2026) ont toutes frappé à la fin d'un long
/// combat Turbo : 4/4 combats de 30+ tours (32 à 58 tours en 20-48 s) bannis à la fin du combat, aucun combat non banni
/// au-delà de 25 tours ; session de Shadowbot coupée 50 ms après la fin du 3e combat Turbo en 50 s (34 tours).
/// Pur : aucune horloge ici, l'appelant fournit les ticks. Au-delà des plafonds, le reste tourne à allure humaine
/// (délais de la vitesse Fast + pause avant FightTurnFinishRequest).</summary>
public static class TurboPacing
{
    /// <summary>Fenêtre glissante (ms) du plafond « tours par 10 minutes », tous combats confondus.</summary>
    public const long WindowMs = 10 * 60 * 1000;

    /// <summary>Fenêtre glissante (ms) du plafond « tours par minute », tous combats confondus (Shadowbot 07/11 : coupé 50 ms après
    /// la fin de combat avec 34 puis 31 tours en 60 s ; aucun combat non coupé au-dessus de 25 tours / 60 s sur 1032).</summary>
    public const long MinuteWindowMs = 60 * 1000;

    /// <summary>true = ce tour doit tourner à allure humaine. <paramref name="turnInFight"/> = n° de MON tour dans ce combat
    /// (1 = premier) ; <paramref name="turnsInWindow"/> = mes tours sur les 10 dernières minutes et <paramref name="turnsInLastMinute"/>
    /// = sur les 60 dernières secondes, ce tour COMPRIS. Un plafond &lt;= 0 est désactivé. Tour == plafond reste Turbo,
    /// plafond + 1 passe à l'allure humaine ; les plafonds de fenêtre se relâchent seuls quand la fenêtre redescend.</summary>
    public static bool UseHumanPace(int turnInFight, int turnsInWindow, int turnsInLastMinute,
        int maxTurnsPerFight, int maxTurnsPer10Min, int maxTurnsPerMinute)
        => maxTurnsPerFight > 0 && turnInFight > maxTurnsPerFight
        || maxTurnsPer10Min > 0 && turnsInWindow > maxTurnsPer10Min
        || maxTurnsPerMinute > 0 && turnsInLastMinute > maxTurnsPerMinute;

    /// <summary>Nombre de tours de <paramref name="doneTicks"/> (ordre chronologique) terminés il y a moins de 60 s. Ne modifie pas la file.</summary>
    public static int CountLastMinute(Queue<long> doneTicks, long nowTick)
    {
        int n = 0;
        foreach (long t in doneTicks) if (nowTick - t < MinuteWindowMs) n++;
        return n;
    }

    /// <summary>Retire de <paramref name="doneTicks"/> (ordre chronologique) les tours terminés il y a plus de 10 min et
    /// renvoie le nombre restant.</summary>
    public static int PruneAndCount(Queue<long> doneTicks, long nowTick)
    {
        while (doneTicks.Count > 0 && nowTick - doneTicks.Peek() >= WindowMs) doneTicks.Dequeue();
        return doneTicks.Count;
    }

    /// <summary>Délai (ms) avant un lancer à allure humaine : mêmes bornes que CombatSpeed.Fast.</summary>
    public static int CastDelayMs(Random random) => random.Next(450, 900);

    /// <summary>Pause (ms) avant FightTurnFinishRequest à allure humaine : 1,5 à 3 s.</summary>
    public static int EndTurnDelayMs(Random random) => random.Next(1500, 3001);
}
