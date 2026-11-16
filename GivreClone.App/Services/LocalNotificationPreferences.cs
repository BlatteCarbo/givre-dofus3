using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

[Flags]
public enum LocalBotEvent { None = 0, PodsFull = 1, Death = 2, Disconnected = 4, ScriptEnded = 8, FightWon = 16, FightLost = 32, KamasGained = 64, All = 127 }

public sealed record LocalNotificationPreference(bool Enabled = false, LocalBotEvent Events = LocalBotEvent.All,
    long KamasThreshold = 1000, int CooldownSeconds = 30);

[JsonSerializable(typeof(Dictionary<string, LocalNotificationPreference>))]
internal partial class LocalNotificationJson : JsonSerializerContext { }

// Stores only opaque clone account IDs and notification preferences, never account credentials.
public sealed class LocalNotificationPreferences
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, LocalNotificationPreference> _accounts;

    public LocalNotificationPreferences(string path)
    {
        _path = path;
        // A corrupt file is reported to the caller and is never silently overwritten.
        _accounts = File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), LocalNotificationJson.Default.DictionaryStringLocalNotificationPreference)
                ?? throw new InvalidDataException("Invalid notification preferences.")
            : new(StringComparer.Ordinal);
        foreach (var entry in _accounts)
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null || entry.Value.KamasThreshold < 1 ||
                entry.Value.CooldownSeconds is < 1 or > 86400 || (entry.Value.Events & ~LocalBotEvent.All) != 0)
                throw new InvalidDataException("Invalid notification preference entry.");
    }

    public LocalNotificationPreference Get(string accountId)
    {
        lock (_gate) return _accounts.TryGetValue(accountId, out var value) ? value : new();
    }

    public void Set(string accountId, LocalNotificationPreference value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (value.KamasThreshold < 1 || value.CooldownSeconds is < 1 or > 86400 || (value.Events & ~LocalBotEvent.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        lock (_gate)
        {
            var updated = new Dictionary<string, LocalNotificationPreference>(_accounts, StringComparer.Ordinal) { [accountId] = value };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(updated, LocalNotificationJson.Default.DictionaryStringLocalNotificationPreference));
                File.Move(temporary, _path, true);
                _accounts = updated;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
