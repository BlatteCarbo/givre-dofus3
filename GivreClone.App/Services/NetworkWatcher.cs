using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;

namespace GivreClone.App.Services;

/// <summary>
/// Surveillance centralisee des interfaces reseau : un seul minuteur partage (2,5 s). Detecte les
/// interfaces qui apparaissent ou disparaissent (ex : partage de connexion d'un telephone branche
/// apres le demarrage) et previent les onglets via l'evenement Changed, quel que soit l'onglet
/// affiche. Une nouvelle interface leve AdapterAppeared une seule fois.
/// </summary>
public static class NetworkWatcher
{
    private static readonly DispatcherTimer _timer;
    private static HashSet<string> _known = new();
    private static bool _primed;

    /// <summary>Derniere liste connue des interfaces actives.</summary>
    public static IReadOnlyList<NetAdapter> Current { get; private set; } = new List<NetAdapter>();

    /// <summary>Leve a chaque scan : les onglets s'y abonnent pour se resynchroniser.</summary>
    public static event Action? Changed;

    /// <summary>Leve quand une NOUVELLE interface apparait (apres le premier scan).</summary>
    public static event Action<NetAdapter>? AdapterAppeared;

    static NetworkWatcher()
    {
        Poll();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>Amorce la surveillance (declenche le constructeur statique). A appeler tot au demarrage.</summary>
    public static void Ensure() { }

    /// <summary>Scan immediat (bouton Rafraichir).</summary>
    public static void Refresh() => Poll();

    private static void Poll()
    {
        var live = NetworkInterfaces.List();
        Current = live;

        if (_primed)
        {
            foreach (var a in live)
                if (!_known.Contains(a.Id))
                    AdapterAppeared?.Invoke(a);
        }

        _known = live.Select(a => a.Id).ToHashSet();
        _primed = true;
        Changed?.Invoke();
    }
}
