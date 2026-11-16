using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Choix d'un personnage dans la liste du compte : nom exact (insensible à la casse) ou « * » = le plus haut niveau. Pur.</summary>
public static class CharacterPick
{
    public static long? Pick(IReadOnlyList<(long Id, string? Name, int Level)> list, string who)
    {
        if (list.Count == 0) return null;
        if (who == "*") return list.OrderByDescending(c => c.Level).First().Id;
        foreach (var c in list) if (string.Equals(c.Name, who, StringComparison.OrdinalIgnoreCase)) return c.Id;
        return null;
    }
}
