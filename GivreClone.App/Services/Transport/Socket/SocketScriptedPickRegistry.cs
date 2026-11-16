using System;
using System.Collections.Concurrent;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>Serveur (nom ou numéro) imposé par un script / un outil pour la prochaine session de jeu d'un compte : remplace le sélecteur
/// serveur-personnage, consommé UNE fois.</summary>
public sealed record ScriptedPick(string Server, string? Character);

public static class SocketScriptedPickRegistry
{
    private static readonly ConcurrentDictionary<string, ScriptedPick> Pending = new(StringComparer.OrdinalIgnoreCase);
    public static void Set(string login, ScriptedPick pick) => Pending[login] = pick;
    public static bool Has(string login) => Pending.ContainsKey(login);
    public static ScriptedPick? Take(string login) => Pending.TryRemove(login, out var p) ? p : null;
    public static void Clear(string login) => Pending.TryRemove(login, out _);
}
