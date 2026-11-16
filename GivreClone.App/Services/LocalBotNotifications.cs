using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Supervisor;

namespace GivreClone.App.Services;

public sealed record LocalBotNotice(string AccountId, LocalBotEvent Event, string Title, string Body);
public enum LocalToastStatus { Available, Submitted, Unsupported, Failed, TimedOut, Suppressed, Busy }
public sealed record LocalToastResult(LocalToastStatus Status, int? PlatformSetting = null);
public interface ILocalToastDelivery { Task<LocalToastResult> SendAsync(LocalBotNotice notice, CancellationToken cancellationToken); }

public sealed class LocalBotNotifications
{
    private readonly Func<string, LocalNotificationPreference> _preferences;
    private readonly Action<LocalBotNotice> _publish;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, BotSnapshot> _previous = new();
    private readonly Dictionary<(string, LocalBotEvent), DateTimeOffset> _sent = new();
    private readonly Dictionary<string, long> _gains = new();
    private readonly HashSet<string> _full = new();

    public LocalBotNotifications(Func<string, LocalNotificationPreference> preferences, Action<LocalBotNotice> publish,
        Func<DateTimeOffset>? now = null) => (_preferences, _publish, _now) = (preferences, publish, now ?? (() => DateTimeOffset.UtcNow));

    public void Observe(string accountId, BotSnapshot current)
    {
        lock (_gate)
        {
            _previous.TryGetValue(accountId, out var previous);
            if (previous is not null && current.Status == BotStatus.Disconnected && previous.Status != current.Status)
                Emit(accountId, LocalBotEvent.Disconnected);
            if (previous is not null && previous.ScriptRunning && !current.ScriptRunning)
                Emit(accountId, LocalBotEvent.ScriptEnded);
            if (previous is not null && previous.Pid == current.Pid && previous.Name == current.Name)
            {
                if (current.Kamas >= 0 && previous.Kamas >= 0 && current.InGame && previous.InGame)
                {
                    var preference = _preferences(accountId);
                    if (!preference.Enabled || (preference.Events & LocalBotEvent.KamasGained) == 0)
                        _gains.Remove(accountId);
                    else if (current.Kamas > previous.Kamas)
                    {
                        var delta = current.Kamas - previous.Kamas;
                        var old = _gains.GetValueOrDefault(accountId);
                        _gains[accountId] = delta > long.MaxValue - old ? long.MaxValue : old + delta;
                        if (_gains[accountId] >= _preferences(accountId).KamasThreshold && Emit(accountId, LocalBotEvent.KamasGained))
                            _gains[accountId] = 0;
                    }
                }
                else _gains.Remove(accountId);
            }
            else { _gains.Remove(accountId); _full.Remove(accountId); }
            _previous[accountId] = current;
        }
    }

    public void Pods(string accountId, int current, int maximum)
    {
        lock (_gate)
        {
            if (maximum <= 0 || current < 0) return;
            if (current >= maximum) { if (_full.Add(accountId)) Emit(accountId, LocalBotEvent.PodsFull); }
            else _full.Remove(accountId);
        }
    }

    public void Raise(string accountId, LocalBotEvent kind) { lock (_gate) Emit(accountId, kind); }

    public void Retain(IReadOnlySet<string> accounts)
    {
        lock (_gate)
        {
            foreach (var id in new List<string>(_previous.Keys))
                if (!accounts.Contains(id)) { _previous.Remove(id); _gains.Remove(id); _full.Remove(id); }
            foreach (var key in new List<(string, LocalBotEvent)>(_sent.Keys)) if (!accounts.Contains(key.Item1)) _sent.Remove(key);
        }
    }

    private bool Emit(string id, LocalBotEvent kind)
    {
        var preference = _preferences(id);
        if (!preference.Enabled || (preference.Events & kind) == 0) return false;
        var key = (id, kind);
        var now = _now();
        if (_sent.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(preference.CooldownSeconds)) return false;
        string title = kind switch
        {
            LocalBotEvent.PodsFull => "Pods pleins", LocalBotEvent.Death => "Personnage mort",
            LocalBotEvent.Disconnected => "Déconnexion", LocalBotEvent.ScriptEnded => "Script arrêté ou terminé",
            LocalBotEvent.FightWon => "Combat gagné", LocalBotEvent.FightLost => "Combat perdu",
            LocalBotEvent.KamasGained => "Kamas gagnés", _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        _sent[key] = now;
        // Desktop text deliberately excludes login, character name, script path and transport errors.
        _publish(new(id, kind, title, "Un événement est disponible dans GivreClone."));
        return true;
    }
}
