using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace GivreClone.App.Services;

/// <summary>Abonnement du compte tel que le serveur de CONNEXION l'annonce dans IdentificationSuccess (champ <c>subscription_end_date</c>, chaîne de date, et droits <c>UnlimitedAccess</c> / <c>InfiniteSubscription</c>).
/// Même source que GivreActive. Sert quand le paquet de jeu <c>AccountInformationUpdateEvent</c> n'est pas (encore) reçu.</summary>
public static class AccountSubscriptionInfo
{
    public sealed record Info(DateTime? EndUtc, bool Unlimited, bool Infinite);

    private static readonly ConcurrentDictionary<uint, Info> ByPid = new();

    /// <summary>Date de fin → UTC. Chaîne vide ou illisible → null (jamais inventée).</summary>
    public static DateTime? ParseEnd(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }

    public static void Set(uint pid, string? rawEnd, bool unlimited, bool infinite)
    {
        if (pid == 0) return;
        ByPid[pid] = new Info(ParseEnd(rawEnd), unlimited, infinite);
    }

    public static Info? Get(uint pid) => ByPid.TryGetValue(pid, out var i) ? i : null;

    /// <summary>Nouvelle tentative d'identification : aucune ancienne réponse ne fait foi.</summary>
    public static void Clear(uint pid) => ByPid.TryRemove(pid, out _);

    /// <summary>Fin d'abonnement à afficher : la date annoncée ; abonnement illimité / infini → 31/12/2999 ; pas d'abonnement (date vide ou passée) → 1970 (« non abonné »). null = inconnu.</summary>
    public static DateTime? EffectiveEnd(Info? i)
    {
        if (i is null) return null;
        if (i.Unlimited || i.Infinite) return new DateTime(2999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        return i.EndUtc ?? DateTime.UnixEpoch;
    }
}
