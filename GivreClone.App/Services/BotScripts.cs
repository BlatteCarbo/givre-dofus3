using System.Collections.Concurrent;

namespace GivreClone.App.Services;

/// <summary>État du script Lua PAR BOT (pid) — pont entre BotsViewModel (qui possède les hôtes Lua) et la télémétrie
/// serveur (construite par AccountsViewModel). Alimente les champs `script` / `script_running` du canal « bots ».
/// Pas d'entrée = aucun script jamais lancé sur ce pid (script_running envoyé null = inconnu).</summary>
public static class BotScripts
{
    private static readonly ConcurrentDictionary<int, (string? Name, bool Running)> _state = new();

    /// <summary>Nom + état d'exécution (au lancement d'un script nommé).</summary>
    public static void Set(int pid, string? name, bool running) => _state[pid] = (name, running);

    /// <summary>Change juste l'état d'exécution (garde le nom) — au démarrage/arrêt/fin naturelle.</summary>
    public static void SetRunning(int pid, bool running) =>
        _state.AddOrUpdate(pid, (null, running), (_, cur) => (cur.Name, running));

    public static void Forget(int pid) => _state.TryRemove(pid, out _);

    /// <summary>État actuel, ou null si aucun script n'a jamais été lancé sur ce pid (télémétrie script_running = null).</summary>
    public static (string? Name, bool Running)? Get(int pid) => _state.TryGetValue(pid, out var v) ? v : null;
}
