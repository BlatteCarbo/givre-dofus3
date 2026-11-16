using System.Collections.Generic;

namespace GivreClone.App.Services.UiNavigation;

/// <summary>Historique borné des pages quittées (Alt+← / Retour). Logique pure, sans UI.</summary>
public sealed class NavigationHistory
{
    private readonly List<string> _keys = new();

    public NavigationHistory(int capacity = 20) => Capacity = capacity;

    public int Capacity { get; }
    public int Count => _keys.Count;
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>Mémorise la page quittée. Ignore les changements nuls et les doublons consécutifs ; jette la plus ancienne
    /// au-delà de la capacité.</summary>
    public void Record(string? fromKey, string? toKey)
    {
        if (string.IsNullOrEmpty(fromKey) || fromKey == toKey) return;
        if (_keys.Count > 0 && _keys[^1] == fromKey) return;
        _keys.Add(fromKey);
        if (_keys.Count > Capacity) _keys.RemoveAt(0);
    }

    /// <summary>Dépile la page précédente différente de <paramref name="currentKey"/>.</summary>
    public bool TryPop(string? currentKey, out string key)
    {
        while (_keys.Count > 0)
        {
            key = _keys[^1];
            _keys.RemoveAt(_keys.Count - 1);
            if (key != currentKey) return true;
        }
        key = "";
        return false;
    }
}
