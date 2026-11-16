using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Message entre un bot et un superviseur : sujet + donnée (nombre, chaîne, booléen, liste, table ; recopiée, jamais de fonction).</summary>
public sealed record BusMessage(string Sender, BotSnapshot? SenderBot, string Subject, object? Data);

/// <summary>
/// Boîte aux lettres d'un script (portail) : 64 messages en attente au plus ; un message livré à personne (ni callback ni attente)
/// est GARDÉ — 32 au plus, les plus anciens perdus — pour un wait ultérieur. Thread-safe : le producteur est un autre thread que
/// le script propriétaire.
/// </summary>
public sealed class Mailbox
{
    public const int MaxPending = 64, MaxKept = 32;

    private readonly object _gate = new();
    private readonly Queue<BusMessage> _pending = new();
    private readonly List<BusMessage> _kept = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private int _dropped;

    /// <summary>Messages refusés faute de place depuis la dernière lecture de ce compteur.</summary>
    public int TakeDropped() => Interlocked.Exchange(ref _dropped, 0);
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public int KeptCount { get { lock (_gate) return _kept.Count; } }

    /// <summary>Dépose un message. False (message perdu) si 64 messages attendent déjà.</summary>
    public bool Post(BusMessage m)
    {
        lock (_gate)
        {
            if (_pending.Count >= MaxPending) { _dropped++; return false; }
            _pending.Enqueue(m);
        }
        _signal.Set();
        return true;
    }

    /// <summary>Vide les messages en attente vers <paramref name="onMessage"/> ; il rend true si un callback les a CONSOMMÉS.
    /// Ceux qu'il ne consomme pas sont gardés pour un wait. Un seul thread (le propriétaire) appelle cette méthode.</summary>
    public int Pump(Func<BusMessage, bool> onMessage, int budget = 64)
    {
        int n = 0;
        while (n < budget)
        {
            BusMessage? m;
            lock (_gate) m = _pending.Count > 0 ? _pending.Dequeue() : null;
            if (m is null) break;
            n++;
            if (!onMessage(m)) Keep(m);
        }
        lock (_gate) if (_pending.Count == 0) _signal.Reset();
        return n;
    }

    private void Keep(BusMessage m)
    {
        lock (_gate)
        {
            _kept.Add(m);
            while (_kept.Count > MaxKept) _kept.RemoveAt(0);
        }
    }

    /// <summary>Retire et rend le premier message gardé ou en attente de ce sujet (casse ignorée), sans attendre.</summary>
    public BusMessage? TryTake(string subject)
    {
        lock (_gate)
        {
            int i = _kept.FindIndex(k => Same(k.Subject, subject));
            if (i >= 0) { var m = _kept[i]; _kept.RemoveAt(i); return m; }
            var rest = _pending.ToList();
            int j = rest.FindIndex(p => Same(p.Subject, subject));
            if (j < 0) return null;
            var hit = rest[j]; rest.RemoveAt(j);
            _pending.Clear(); foreach (var r in rest) _pending.Enqueue(r);
            return hit;
        }
    }

    /// <summary>
    /// Attend le prochain message de ce sujet (déjà gardé ou à venir). Pendant l'attente, les AUTRES messages sont servis à
    /// <paramref name="onOther"/> (callbacks) puis gardés s'il ne les consomme pas. Null au délai ou à l'annulation.
    /// </summary>
    public BusMessage? Wait(string subject, TimeSpan timeout, CancellationToken ct, Func<BusMessage, bool>? onOther = null)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (TryTake(subject) is { } hit) return hit;
            Pump(m => !Same(m.Subject, subject) && (onOther?.Invoke(m) ?? false), budget: 16);
            if (TryTake(subject) is { } late) return late;
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return null;
            _signal.Wait((int)Math.Min(left.TotalMilliseconds, 50), ct);
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Acheminement des messages. Bot → superviseurs en cours (<c>supervisor:send</c>) ; superviseur → bot (<c>bots:send</c>,
/// <c>broadcast</c>), seulement vers un bot dont le script PRINCIPAL tourne. Le bus ne connaît ni l'UI ni Lua.
/// </summary>
public sealed class SupervisorBus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Mailbox> _supervisors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Mailbox> _bots = new();

    /// <summary>Le bot a-t-il un script principal en cours (et non une commande /cmd) ? Fourni par l'application.</summary>
    public Func<int, bool> BotHasMainScript { get; set; } = _ => true;

    public Mailbox RegisterSupervisor(string name)
    {
        lock (_gate) return _supervisors[name] = new Mailbox();
    }

    public void UnregisterSupervisor(string name) { lock (_gate) _supervisors.Remove(name); }
    public bool AnySupervisorRunning { get { lock (_gate) return _supervisors.Count > 0; } }
    public IReadOnlyList<string> RunningSupervisors { get { lock (_gate) return _supervisors.Keys.ToList(); } }

    public Mailbox BotBox(int pid)
    {
        lock (_gate) return _bots.TryGetValue(pid, out var b) ? b : _bots[pid] = new Mailbox();
    }

    public void ForgetBot(int pid) { lock (_gate) _bots.Remove(pid); }

    /// <summary>Bot → tous les superviseurs en cours. Rend le nombre de superviseurs atteints (0 sans superviseur).</summary>
    public int SendToSupervisors(BotSnapshot from, string subject, object? data)
    {
        List<Mailbox> boxes;
        lock (_gate) boxes = _supervisors.Values.ToList();
        int n = 0;
        foreach (var b in boxes) if (b.Post(new BusMessage(from.Name, from, subject, data))) n++;
        return n;
    }

    /// <summary>Superviseur → un bot. False si le bot n'a pas de script principal en cours ou si sa boîte est pleine.</summary>
    public bool SendToBot(int pid, string supervisor, string subject, object? data)
        => pid > 0 && BotHasMainScript(pid) && BotBox(pid).Post(new BusMessage(supervisor, null, subject, data));

    /// <summary>Superviseur → tous les bots dont un script tourne. Rend le nombre atteint.</summary>
    public int Broadcast(IEnumerable<int> pids, string supervisor, string subject, object? data)
        => pids.Count(pid => SendToBot(pid, supervisor, subject, data));
}
