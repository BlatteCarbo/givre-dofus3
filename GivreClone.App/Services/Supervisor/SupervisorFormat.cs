using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Textes d'affichage du tableau « bots supervisés » (purs : testés sans interface). Libellés choisis ici, non observés.</summary>
public static class SupervisorFormat
{
    public static string StatusText(string status) => status switch
    {
        BotStatus.Offline => "Hors ligne",
        BotStatus.Connecting => "Connexion",
        BotStatus.InGame => "En jeu",
        BotStatus.Moving => "Déplacement",
        BotStatus.Gathering => "Récolte",
        BotStatus.Fight => "Combat",
        BotStatus.Dead => "Mort",
        BotStatus.Disconnected => "Déconnecté",
        _ => status,
    };

    /// <summary>Famille de couleur du statut : ok (en jeu et actif), warn (en cours de connexion ou déconnecté), bad (mort), off (hors ligne).</summary>
    public static string StatusTone(string status) => status switch
    {
        BotStatus.InGame or BotStatus.Moving or BotStatus.Gathering or BotStatus.Fight => "ok",
        BotStatus.Connecting or BotStatus.Disconnected => "warn",
        BotStatus.Dead => "bad",
        _ => "off",
    };

    public static string ModeText(string mode) => mode == "socket" ? "Socket" : "MITM";
    public static string ModeTip(string mode) => mode == "socket" ? "Mode Socket — cliquer pour passer en MITM" : "Mode MITM — cliquer pour passer en Socket";
    public static string PodsText(BotSnapshot b) => b.PodsMax > 0 ? $"{b.PodsPercent} %" : "—";
    public static string MapText(BotSnapshot b) => !string.IsNullOrEmpty(b.Coords) ? b.Coords : b.MapId > 0 ? b.MapId.ToString() : "—";
    public static string ScriptText(BotSnapshot b) => b.ScriptRunning ? (string.IsNullOrEmpty(b.Script) ? "en cours" : b.Script) : "—";

    /// <summary>« 5 bots · 3 en jeu · 1 déconnecté · 1 hors ligne » ; aucun bot → « Aucun bot ».</summary>
    public static string Overview(IReadOnlyCollection<BotSnapshot> bots)
    {
        if (bots.Count == 0) return "Aucun bot";
        var parts = new List<string> { bots.Count == 1 ? "1 bot" : $"{bots.Count} bots" };
        int inGame = bots.Count(b => b.InGame), disc = bots.Count(b => b.Status == BotStatus.Disconnected), off = bots.Count(b => b.Status == BotStatus.Offline);
        if (inGame > 0) parts.Add($"{inGame} en jeu");
        if (disc > 0) parts.Add(disc == 1 ? "1 déconnecté" : $"{disc} déconnectés");
        if (off > 0) parts.Add($"{off} hors ligne");
        return string.Join(" · ", parts);
    }

    /// <summary>Un bot supervisé qui demande de l'attention (déconnecté ou mort) : alimente le bandeau « problème ».</summary>
    public static string? Problem(IReadOnlyCollection<BotSnapshot> bots)
    {
        var bad = bots.Where(b => b.Status is BotStatus.Disconnected or BotStatus.Dead).ToList();
        return bad.Count == 0 ? null : string.Join(", ", bad.Take(3).Select(b => $"{b.Account} ({StatusText(b.Status).ToLowerInvariant()})")) + (bad.Count > 3 ? $" +{bad.Count - 3}" : "");
    }
}
