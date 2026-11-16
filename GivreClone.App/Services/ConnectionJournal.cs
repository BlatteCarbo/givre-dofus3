using System;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Journal de CONNEXION lisible, étape par étape, dans la console Lua du bot (lignes colorées) : proxy utilisé, adresse IP,
/// connexion Ankama, serveur de connexion, serveur de jeu, bascule sur le proxy de jeu, personnage, arrivée en jeu.
/// Le firehose technique (Console transport) reste inchangé ; chaque étape y est aussi recopiée.
/// Une étape peut être émise avant que le bot existe (authentification d'un lancement classique : pas encore de pid) : elle
/// est alors adressée au COMPTE et affichée dès que son bot apparaît.
/// </summary>
public static class ConnectionJournal
{
    /// <summary>(compte ou null, pid ou 0, message, niveau). Levé depuis n'importe quel thread.</summary>
    public static event Action<string?, uint, string, LuaLogLevel>? Logged;

    public static void ForAccount(string accountId, string message, LuaLogLevel level = LuaLogLevel.Info) => Raise(accountId, 0, message, level);

    public static void ForBot(uint pid, string message, LuaLogLevel level = LuaLogLevel.Info) => Raise(null, pid, message, level);

    private static void Raise(string? accountId, uint pid, string message, LuaLogLevel level)
    {
        TransportLog.Line(pid, "[connexion] " + message);
        try { Logged?.Invoke(accountId, pid, message, level); } catch { /* l'affichage ne casse jamais une connexion */ }
    }

    /// <summary>« « Rotatif » (SOCKS5 87.98.158.144:10025) » ; agent GivreRelay : son nom seulement (l'adresse est le pont local).</summary>
    public static string Describe(string? name, ProxySettings ps, bool relay) =>
        relay || string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(name) ? $"{ps.Kind} {ps.Host}:{ps.Port}" : $"« {name} »")
                                            : $"« {name} » ({ps.Kind} {ps.Host}:{ps.Port})";
}
