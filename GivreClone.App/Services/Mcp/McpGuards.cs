using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Mcp;

/// <summary>Garde-fous de l'automatisation (MCP + superviseurs) : comptes que l'utilisateur interdit de connecter ou de piloter.</summary>
public static class McpGuards
{
    /// <summary>Comptes protégés par défaut (partie locale du login, avant « @ »). Modifiables via AppConfig.McpBlockedLogins.</summary>
    public static readonly string[] DefaultBlockedLogins = { "player.one85", "player.one", "player.three" };

    /// <summary>Vrai si <paramref name="login"/> (avec ou sans « @domaine ») figure dans la liste. Comparaison insensible à la casse.</summary>
    public static bool IsBlocked(string? login, IEnumerable<string>? blocked)
    {
        if (string.IsNullOrWhiteSpace(login) || blocked is null) return false;
        static string Local(string s) { s = s.Trim(); int at = s.IndexOf('@'); return (at >= 0 ? s[..at] : s).ToLowerInvariant(); }
        string who = Local(login);
        return blocked.Any(b => !string.IsNullOrWhiteSpace(b) && Local(b) == who);
    }
}
