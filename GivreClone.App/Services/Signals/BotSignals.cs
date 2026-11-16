using System.Collections.Generic;

namespace GivreClone.App.Services.Signals;

/// <summary>État minimal d'un personnage en jeu, tel que la fiche du bot l'affiche déjà.</summary>
public readonly record struct BotVitals(int Life, int MaxLife, long PodsCur, long PodsMax, bool InFight);

/// <summary>Détecte l'APPARITION d'un signal (pour notifier une seule fois) ; un signal disparu peut ensuite se redéclencher.</summary>
public sealed class SignalEdges
{
    private readonly HashSet<string> _on = new();

    public List<string> Rising(IEnumerable<string> current)
    {
        var now = new HashSet<string>(current);
        var rising = new List<string>();
        foreach (var k in now) if (!_on.Contains(k)) rising.Add(k);
        _on.Clear(); _on.UnionWith(now);
        return rising;
    }
}

/// <summary>Points d'attention dérivés de l'état observé (aucune commande de jeu).</summary>
public static class BotSignals
{
    public const int PodsFullPercent = 95;
    public const int LowLifePercent = 25;

    public static IEnumerable<(string Category, string Detail)> For(BotVitals v)
    {
        if (v.PodsMax > 0 && v.PodsCur * 100 >= v.PodsMax * PodsFullPercent)
            yield return ("Inventaire", $"Pods presque pleins ({v.PodsCur} / {v.PodsMax})");
        // En combat, les PV bas sont normaux et changent à chaque coup : on ne signale que hors combat.
        if (!v.InFight && v.MaxLife > 0 && v.Life * 100 < v.MaxLife * LowLifePercent)
            yield return ("Santé", $"PV bas hors combat ({v.Life} / {v.MaxLife})");
    }
}
