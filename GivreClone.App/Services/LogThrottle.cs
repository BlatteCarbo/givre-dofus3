using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>Limite la fréquence d'une même ligne de journal : la 1re occurrence d'une clé passe toujours, les suivantes seulement une fois l'intervalle écoulé, avec le nombre de lignes masquées entre-temps.
/// Garde la variété (chaque type de message apparaît) en écrasant le flot des messages répétitifs (déplacements, pings…). Pur et thread-safe : l'heure est fournie.</summary>
public sealed class LogThrottle
{
    private readonly long _intervalMs;
    private readonly int _maxKeys;
    private readonly Dictionary<string, (long Last, int Skipped)> _state = new();
    private readonly object _gate = new();

    public LogThrottle(TimeSpan interval, int maxKeys = 1024) => (_intervalMs, _maxKeys) = ((long)interval.TotalMilliseconds, Math.Max(16, maxKeys));

    /// <summary>true = écrire la ligne ; <paramref name="skipped"/> = lignes de cette clé masquées depuis la dernière écrite.</summary>
    public bool ShouldLog(string key, long nowMs, out int skipped)
    {
        lock (_gate)
        {
            if (_state.TryGetValue(key, out var s))
            {
                if (nowMs - s.Last < _intervalMs) { _state[key] = (s.Last, s.Skipped + 1); skipped = 0; return false; }
                skipped = s.Skipped;
                _state[key] = (nowMs, 0);
                return true;
            }
            if (_state.Count >= _maxKeys) _state.Clear();   // garde-fou mémoire : une clé par type de message, jamais des milliers
            _state[key] = (nowMs, 0);
            skipped = 0;
            return true;
        }
    }

    /// <summary>Clé d'un message de trame (« [jeu] Nom (123o) », « LoginMessage/Identification 127o »…) : le nom, sans la taille ni le contenu variable.</summary>
    public static string KeyOf(string message)
    {
        int p = message.IndexOf(" (", StringComparison.Ordinal);
        string head = p > 0 ? message[..p] : message;
        int end = head.Length;
        while (end > 0 && (char.IsDigit(head[end - 1]) || head[end - 1] == 'o' && end > 1 && char.IsDigit(head[end - 2]) || head[end - 1] == ' ')) end--;
        return end > 0 ? head[..end] : head;
    }
}
