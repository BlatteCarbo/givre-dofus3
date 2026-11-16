using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Eco;

/// <summary>Alerte de prix : prévenir quand un objet passe sous (Below) ou au-dessus d'un prix cible, d'après l'HDV déjà observé par un bot.</summary>
public sealed class PriceAlert
{
    public long Gid { get; set; }
    public string Name { get; set; } = "";
    public bool Below { get; set; } = true;
    public decimal Target { get; set; }
    public DateTime? LastAlertUtc { get; set; }
    public decimal LastAlertPrice { get; set; }
}

public sealed class PriceAlertBook
{
    public List<PriceAlert> Alerts { get; set; } = new();
}

/// <summary>Règles d'alerte de prix. Pure : le prix et l'heure sont fournis. Anti-spam : une nouvelle alerte seulement après 6 h, ou si le prix s'est encore
/// éloigné de 10 % dans le sens de l'alerte depuis la dernière.</summary>
public static class PriceAlerts
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(6);
    public const long MaxObservationAgeMs = 3 * 3_600_000L;

    public static bool IsHit(PriceAlert a, decimal price) => price > 0 && (a.Below ? price <= a.Target : price >= a.Target);

    public static bool ShouldNotify(PriceAlert a, decimal price, DateTime nowUtc)
    {
        if (!IsHit(a, price)) return false;
        if (a.LastAlertUtc is not { } last) return true;
        if (nowUtc - last >= Cooldown) return true;
        return a.Below ? price <= a.LastAlertPrice * 0.9m : price >= a.LastAlertPrice * 1.1m;
    }

    public static void MarkNotified(PriceAlert a, decimal price, DateTime nowUtc) { a.LastAlertUtc = nowUtc; a.LastAlertPrice = price; }

    public static string Text(string who, PriceAlert a, decimal price)
        => $"💱 [{who}] {a.Name} : {price:N0} kamas ({(a.Below ? "sous" : "au-dessus de")} ta cible {a.Target:N0}).";

    /// <summary>Un prix est utilisable pour alerter s'il a été observé sur l'HDV récemment (un prix saisi à la main ou ancien ne déclenche rien).</summary>
    public static bool IsFreshObservation(string source, long? ageMs) => source is "marché" && ageMs is { } a && a <= MaxObservationAgeMs;
}
