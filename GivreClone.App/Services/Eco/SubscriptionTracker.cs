using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public enum SubscriptionLevel { Unknown, Ok, Soon, Critical, Expired }

/// <summary>Fin d'abonnement Ankama vue pour un compte (dernière valeur reçue du serveur) et les seuils d'alerte déjà envoyés pour CETTE date.</summary>
public sealed class SubEntry
{
    public string Login { get; set; } = "";
    public string Character { get; set; } = "";
    public DateTime EndUtc { get; set; }
    public DateTime SeenUtc { get; set; }
    public bool Manual { get; set; }   // date saisie à la main (le serveur ne l'envoie pas toujours) ; une valeur reçue du jeu la remplace
    public long SentForEndTicks { get; set; }
    public List<int> SentThresholds { get; set; } = new();
}

public sealed class SubscriptionBook
{
    public List<SubEntry> Entries { get; set; } = new();
}

/// <summary>Suivi de l'abonnement (comme le « sub-tracker » de STAKK) : fin d'abonnement par compte, jours restants, niveau d'alerte, et alertes
/// Discord à 7 / 3 / 1 jour(s) puis à l'expiration — une seule fois par seuil et par date de fin (un réabonnement repart de zéro). Pure.</summary>
public static class SubscriptionTracker
{
    public static readonly int[] Thresholds = { 7, 3, 1, 0 };

    public static double DaysLeft(DateTime endUtc, DateTime nowUtc) => (endUtc - nowUtc).TotalDays;

    public static SubscriptionLevel Level(DateTime? endUtc, DateTime nowUtc)
    {
        if (endUtc is not { } e || e.Year < 2000) return SubscriptionLevel.Unknown;
        double d = DaysLeft(e, nowUtc);
        return d <= 0 ? SubscriptionLevel.Expired : d <= 3 ? SubscriptionLevel.Critical : d <= 7 ? SubscriptionLevel.Soon : SubscriptionLevel.Ok;
    }

    public static string Describe(DateTime? endUtc, DateTime nowUtc)
    {
        if (endUtc is not { } e || e.Year < 2000) return "inconnu";
        double d = DaysLeft(e, nowUtc);
        if (d <= 0) return "expiré";
        if (d < 1) return $"{Math.Max(1, (int)Math.Ceiling(d * 24))} h";
        return $"{(int)Math.Floor(d)} j {(int)((d - Math.Floor(d)) * 24)} h";
    }

    /// <summary>Met à jour l'entrée avec la fin d'abonnement observée ; une date de fin différente efface les seuils déjà envoyés.</summary>
    public static SubEntry Observe(SubscriptionBook book, string login, string character, DateTime endUtc, DateTime nowUtc)
    {
        var e = book.Entries.FirstOrDefault(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase));
        if (e is null) book.Entries.Add(e = new SubEntry { Login = login });
        if (!string.IsNullOrWhiteSpace(character)) e.Character = character;
        // Tolérance d'une minute : le serveur renvoie la même date avec des arrondis différents.
        if (Math.Abs((e.EndUtc - endUtc).TotalMinutes) > 1) { e.SentThresholds.Clear(); e.SentForEndTicks = endUtc.Ticks; }
        e.EndUtc = endUtc; e.SeenUtc = nowUtc; e.Manual = false;
        return e;
    }

    /// <summary>Fin d'abonnement saisie à la main pour un compte (créé si absent). Une date différente efface les alertes déjà envoyées.</summary>
    public static SubEntry SetManual(SubscriptionBook book, string login, string character, DateTime endUtc, DateTime nowUtc)
    {
        var e = Observe(book, login, character, endUtc, nowUtc);
        e.Manual = true;
        return e;
    }

    /// <summary>Le seuil (7, 3, 1 ou 0 jour) à signaler maintenant, ou null. Seul le seuil le plus proche est retenu ; les plus larges sont marqués envoyés.</summary>
    public static int? AlertDue(SubEntry e, DateTime nowUtc)
    {
        if (e.EndUtc.Year < 2000) return null;
        double d = DaysLeft(e.EndUtc, nowUtc);
        var crossed = Thresholds.Where(t => d <= t).ToList();
        if (crossed.Count == 0) return null;
        int tightest = crossed.Min();
        if (e.SentThresholds.Contains(tightest)) return null;
        foreach (var t in crossed) if (!e.SentThresholds.Contains(t)) e.SentThresholds.Add(t);
        return tightest;
    }

    public static string AlertText(string who, int threshold, DateTime endUtc, DateTime nowUtc)
        => threshold == 0 ? $"⛔ [{who}] abonnement expiré."
         : $"⏰ [{who}] abonnement : {Describe(endUtc, nowUtc)} restant(s) (fin le {endUtc.ToLocalTime():dd/MM/yyyy HH:mm}).";
}
