using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.PhoneAlerts;

public sealed record PhoneEvent(long Id, DateTime At, string Kind, string Who, string Text, bool Discord = true);

/// <summary>Centre des alertes « téléphone » : mort, pods pleins, déconnexion, défaite, bot bloqué, archimonstre, fin d'abonnement. Garde les 200 derniers événements (consultés par la page du téléphone en
/// long-poll) et prévient les abonnés (envoi Discord : le webhook du perso arrive en notification push sur le téléphone). Un même événement (type + bot) n'est pas répété avant 5 minutes
/// sauf si l'appelant donne une clé plus fine. Pur côté logique (l'heure est injectable), thread-safe.</summary>
public static class PhoneAlertHub
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
    private const int Keep = 200;

    private static readonly object Gate = new();
    private static readonly List<PhoneEvent> Events = new();
    private static readonly Dictionary<string, long> Last = new();
    private static long _nextId = 1;
    private static TaskCompletionSource _signal = NewSignal();

    /// <summary>Un événement vient d'être levé (thread appelant : ne rien faire de long).</summary>
    public static event Action<PhoneEvent>? Raised;

    /// <summary>Hors tests : horloge en ms.</summary>
    public static Func<long> NowMs { get; set; } = () => Environment.TickCount64;
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lève un événement. <paramref name="key"/> = clé d'anti-répétition (défaut : type + bot). Retourne l'événement, ou null s'il a été étouffé par le délai.</summary>
    public static PhoneEvent? Raise(string kind, string who, string text, string? key = null, TimeSpan? cooldown = null, bool discord = true)
    {
        PhoneEvent ev;
        TaskCompletionSource old;
        lock (Gate)
        {
            long now = NowMs();
            string k = key ?? kind + "|" + who;
            if (Last.TryGetValue(k, out var last) && now - last < (long)(cooldown ?? DefaultCooldown).TotalMilliseconds) return null;
            if (Last.Count > 2000) Last.Clear();
            Last[k] = now;
            ev = new PhoneEvent(_nextId++, UtcNow(), kind, who, text, discord);
            Events.Add(ev);
            if (Events.Count > Keep) Events.RemoveAt(0);
            old = _signal; _signal = NewSignal();
        }
        old.TrySetResult();
        try { Raised?.Invoke(ev); } catch (Exception e) when (e is not OutOfMemoryException) { /* un abonné défaillant ne bloque pas les autres */ }
        return ev;
    }

    /// <summary>Événements plus récents que <paramref name="afterId"/> (0 = les derniers), du plus ancien au plus récent.</summary>
    public static IReadOnlyList<PhoneEvent> Since(long afterId, int limit = 50)
    {
        lock (Gate) return Events.Where(e => e.Id > afterId).TakeLast(Math.Clamp(limit, 1, Keep)).ToList();
    }

    public static long LatestId { get { lock (Gate) return _nextId - 1; } }

    /// <summary>Attend (long-poll) qu'un événement plus récent que <paramref name="afterId"/> existe, au plus <paramref name="timeout"/>.</summary>
    public static async Task<IReadOnlyList<PhoneEvent>> WaitAsync(long afterId, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            Task wait;
            lock (Gate)
            {
                if (Events.Any(e => e.Id > afterId)) return Events.Where(e => e.Id > afterId).TakeLast(50).ToList();
                wait = _signal.Task;
            }
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return Array.Empty<PhoneEvent>();
            try { await Task.WhenAny(wait, Task.Delay(left, ct)).ConfigureAwait(false); }
            catch (OperationCanceledException) { return Array.Empty<PhoneEvent>(); }
            if (ct.IsCancellationRequested) return Array.Empty<PhoneEvent>();
        }
    }

    /// <summary>Remet à zéro (tests).</summary>
    public static void Reset()
    {
        lock (Gate) { Events.Clear(); Last.Clear(); _nextId = 1; _signal = NewSignal(); }
    }
}
