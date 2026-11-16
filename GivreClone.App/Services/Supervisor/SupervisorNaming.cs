using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Règle de nommage des superviseurs : « Superviseur », « Superviseur 2 »… sans doublon (casse ignorée, casse saisie conservée).</summary>
public static class SupervisorNaming
{
    public static string Unique(string? wanted, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(wanted) ? "Superviseur" : wanted.Trim();
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) if (!taken.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }
}
