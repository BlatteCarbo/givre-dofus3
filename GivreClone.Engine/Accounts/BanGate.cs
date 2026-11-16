using System.Globalization;

namespace GivreClone.Engine.Accounts;

/// <summary>Règle « compte banni » : un bannissement constaté bloque les lancements automatiques tant que sa date de fin
/// (si le serveur en a donné une lisible) n'est pas passée. Sans date lisible, il bloque jusqu'au déblocage manuel.</summary>
public static class BanGate
{
    public static bool IsBanned(Account a) => a.BannedAtUtc is not null;

    /// <summary>Fin du bannissement en UTC, null si absente ou illisible.</summary>
    public static DateTime? EndUtc(Account a)
        => a.BanEnd is { Length: > 0 } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d
         : a.BanEnd is { Length: > 0 } ms && long.TryParse(ms, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && epoch > 0
             ? (epoch > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch)).UtcDateTime
             : null;

    public static bool BlocksAutoLaunch(Account a, DateTime nowUtc)
        => IsBanned(a) && (EndUtc(a) is not { } end || end > nowUtc);

    public static void Record(Account a, DateTime nowUtc, string? banEnd) { a.BannedAtUtc = nowUtc; a.BanEnd = banEnd ?? ""; }

    public static void Clear(Account a) { a.BannedAtUtc = null; a.BanEnd = ""; }

    /// <summary>Lève le marqueur d'un bannissement dont la date de fin lisible est passée (true si levé). Sans date lisible
    /// le marqueur reste : seul un déblocage manuel le lève.</summary>
    public static bool ClearIfExpired(Account a, DateTime nowUtc)
    {
        if (!IsBanned(a) || EndUtc(a) is not { } end || end > nowUtc) return false;
        Clear(a);
        return true;
    }

    /// <summary>Décision à la fin de l'attente d'une suspension temporaire. false = ne pas relancer : la reprise AUTOMATIQUE
    /// (échéance atteinte) est annulée si « relance auto » a été coupée pendant l'attente ; un réveil MANUEL
    /// (« Reconnecter ») est une demande explicite et part toujours. true = relancer ; le marqueur n'est levé que si la date de
    /// fin est réellement passée : un réveil avant l'échéance garde le bannissement actif (un nouveau refus redonnera sa date).</summary>
    public static bool CompleteResumeWait(Account a, DateTime nowUtc, bool manualWake, bool autoRelaunchEnabled)
    {
        if (!manualWake && !autoRelaunchEnabled) return false;
        ClearIfExpired(a, nowUtc);
        return true;
    }

    /// <summary>Marge après la fin annoncée : l'heure du serveur et la nôtre peuvent différer de quelques secondes.</summary>
    public static readonly TimeSpan ResumeMargin = TimeSpan.FromSeconds(45);
    /// <summary>Au-delà, ce n'est plus une suspension temporaire : on garde le blocage jusqu'au déblocage manuel.</summary>
    public static readonly TimeSpan MaxAutoResumeWait = TimeSpan.FromHours(24);

    /// <summary>Attente avant la reconnexion automatique d'un compte suspendu temporairement, ou null s'il faut s'arrêter
    /// (pas de date de fin lisible, ou suspension trop longue pour être temporaire). Une seule tentative est faite après
    /// l'échéance : si le serveur refuse encore, ce nouveau refus donne la nouvelle date de fin.</summary>
    public static TimeSpan? AutoResumeDelay(Account a, DateTime nowUtc)
    {
        if (!IsBanned(a) || EndUtc(a) is not { } end) return null;
        var wait = end - nowUtc + ResumeMargin;
        if (wait > MaxAutoResumeWait) return null;
        return wait < ResumeMargin ? ResumeMargin : wait;
    }
}
