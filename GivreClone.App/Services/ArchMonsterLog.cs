using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;

namespace GivreClone.App.Services;

/// <summary>Une ligne du journal des archimonstres repérés : le monstre + le contexte (perso/serveur/carte/coord/heure).</summary>
public sealed class ArchSightingRow
{
    public string Name { get; init; } = "";
    public int Gid { get; init; }
    public string Character { get; init; } = "";
    public string Server { get; init; } = "";
    public long MapId { get; init; }
    public string Coords { get; init; } = "";        // "x, y" (monde) ou ""
    public int Cell { get; init; }
    public int BreedId { get; init; }
    public DateTime Time { get; init; }
    public string TimeText => Time.ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture);
    /// <summary>Ligne de contexte affichée sous le nom : "perso · serveur · x, y".</summary>
    public string Context =>
        string.Join("  ·  ",
            new[] { Character, Server is "" or "-" ? "" : Server, Coords })
        .Replace("  ·    ·  ", "  ·  ").Trim(' ', '·');
}

/// <summary>Journal GLOBAL (tous bots) des archimonstres repérés, alimenté par AccountsViewModel sur détection. Source du
/// panneau UI « Archimonstres récents ». Borné (les plus récents en tête). Thread-safe : Add marshale sur le thread UI.</summary>
public static class ArchMonsterLog
{
    private const int Max = 100;
    /// <summary>Le plus récent en tête. À binder tel quel dans un ItemsControl/ListBox.</summary>
    public static ObservableCollection<ArchSightingRow> Recent { get; } = new();

    /// <summary>Un archimonstre vient d'être journalisé (pour un rafraîchissement réactif éventuel : surbrillance carte…).</summary>
    public static event Action? Changed;

    public static void Add(ArchSightingRow row)
    {
        void Do()
        {
            Recent.Insert(0, row);
            while (Recent.Count > Max) Recent.RemoveAt(Recent.Count - 1);
            Changed?.Invoke();
        }
        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
