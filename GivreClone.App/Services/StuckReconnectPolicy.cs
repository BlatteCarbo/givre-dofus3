using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

public enum StuckReconnectDecision { Allow, Disabled, InFight, ModeratorContact, Overloaded, RateLimited }

/// <summary>Reconnexion de secours d'un bot bloqué : jamais en combat, jamais après un message de modérateur récent, jamais pour un perso
/// surchargé (pods pleins : une reconnexion ne le débloquerait pas), et au plus <see cref="MaxPerHour"/> fois par heure et par compte.</summary>
public static class StuckReconnectPolicy
{
    public const int MaxPerHour = 3;
    public static readonly TimeSpan ModeratorBan = TimeSpan.FromHours(2);
    public const int OverloadedPodsPercent = 97;

    public static StuckReconnectDecision Decide(bool enabled, bool inFight, TimeSpan? sinceModeratorContact, int podsPercent,
        IEnumerable<long> previousAttemptsAtMs, long nowMs)
    {
        if (!enabled) return StuckReconnectDecision.Disabled;
        if (inFight) return StuckReconnectDecision.InFight;
        if (sinceModeratorContact is { } s && s < ModeratorBan) return StuckReconnectDecision.ModeratorContact;
        if (podsPercent >= OverloadedPodsPercent) return StuckReconnectDecision.Overloaded;
        if (previousAttemptsAtMs.Count(t => nowMs - t is >= 0 and < 3_600_000) >= MaxPerHour) return StuckReconnectDecision.RateLimited;
        return StuckReconnectDecision.Allow;
    }
}
