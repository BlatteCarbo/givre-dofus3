using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Supervisor;

/// <summary>
/// Orchestre les superviseurs en cours : un hôte Lua par superviseur, et un sondage de l'état des bots (appelé ~1 fois par seconde
/// par l'application) qui déduit les évènements (<see cref="BotEventDiff"/>) et les distribue à tous les superviseurs. Les évènements
/// qu'on ne peut pas déduire d'un instantané (crash, popup modérateur, création de personnage, connexion en attente) sont levés
/// explicitement par <see cref="Raise"/>. Aucune dépendance UI : testable avec un annuaire factice et des appels à <see cref="Tick"/>.
/// </summary>
public sealed class SupervisorRuntime : IDisposable
{
    private readonly SupervisorServices _svc;
    private readonly object _gate = new();
    private readonly Dictionary<string, SupervisorLuaHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BotSnapshot> _prev = new(StringComparer.OrdinalIgnoreCase);

    public SupervisorRuntime(SupervisorServices svc) => _svc = svc;

    /// <summary>Journal d'un superviseur : (nom, niveau, texte).</summary>
    public event Action<string, string, string>? HostLog;
    /// <summary>Un superviseur s'est arrêté : (nom, raison).</summary>
    public event Action<string, string>? HostEnded;

    public bool IsRunning(string name) { lock (_gate) return _hosts.TryGetValue(name, out var h) && h.Running; }
    public IReadOnlyList<string> RunningNames { get { lock (_gate) return _hosts.Where(h => h.Value.Running).Select(h => h.Key).ToList(); } }

    /// <summary>Lance (ou relance) le superviseur <paramref name="name"/>. Refusé sans le droit Multi. Rend false et la raison sinon.</summary>
    public (bool Ok, string Reason) Start(string name, string code)
    {
        if (!_svc.Perms.HasMulti) return (false, SupervisorLuaHost.NeedMulti);
        if (string.IsNullOrWhiteSpace(code)) return (false, "script vide");
        SupervisorLuaHost host;
        lock (_gate)
        {
            if (_hosts.TryGetValue(name, out var old))
            {
                if (old.Running) return (false, "déjà en cours");
                old.Dispose();
                _hosts.Remove(name);
            }
            host = new SupervisorLuaHost(name, _svc);
            host.Log += (level, text) => HostLog?.Invoke(name, level, text);
            host.Ended += reason => HostEnded?.Invoke(name, reason);
            _hosts[name] = host;
            _prev.Clear();   // nouvelle base : un superviseur qui démarre ne reçoit pas « connected » pour les bots déjà en jeu
        }
        host.Start(code);
        return (true, "");
    }

    public void Stop(string name, string reason = "user")
    {
        SupervisorLuaHost? h;
        lock (_gate) _hosts.TryGetValue(name, out h);
        h?.Stop(reason);
    }

    public void StopAll(string reason = "user") { foreach (var n in RunningNames) Stop(n, reason); }

    /// <summary>Sondage : compare l'instantané courant au précédent et distribue les évènements déduits aux superviseurs en cours.
    /// Un bot vu pour la première fois sert de base (aucun évènement).</summary>
    public int Tick()
    {
        List<SupervisorLuaHost> running;
        lock (_gate)
        {
            running = _hosts.Values.Where(h => h.Running).ToList();
            if (running.Count == 0) { _prev.Clear(); return 0; }
        }
        int n = 0;
        foreach (var cur in _svc.Bots.Snapshot())
        {
            BotSnapshot? prev;
            lock (_gate) { _prev.TryGetValue(cur.Account, out prev); _prev[cur.Account] = cur; }
            if (prev is null) continue;
            foreach (var ev in BotEventDiff.Diff(prev, cur))
            {
                foreach (var h in running) h.Raise(cur, ev);
                n++;
            }
        }
        return n;
    }

    /// <summary>Évènement explicite (crashed, moderator, characterCreated…) pour le bot désigné (alias, perso ou pid).</summary>
    public bool Raise(string botKey, BotEvent ev)
    {
        var bot = BotMatcher.Find(_svc.Bots.Snapshot(), botKey);
        if (bot is null) return false;
        List<SupervisorLuaHost> running;
        lock (_gate) running = _hosts.Values.Where(h => h.Running).ToList();
        foreach (var h in running) h.Raise(bot, ev);
        return running.Count > 0;
    }

    /// <summary>Évènement explicite pour un bot qui n'est pas (encore) dans l'annuaire, p. ex. loginWaiting (bot = libellé, pid 0).</summary>
    public void Raise(BotSnapshot bot, BotEvent ev)
    {
        List<SupervisorLuaHost> running;
        lock (_gate) running = _hosts.Values.Where(h => h.Running).ToList();
        foreach (var h in running) h.Raise(bot, ev);
    }

    public void Dispose()
    {
        List<SupervisorLuaHost> all;
        lock (_gate) { all = _hosts.Values.ToList(); _hosts.Clear(); }
        foreach (var h in all) h.Dispose();
    }
}
