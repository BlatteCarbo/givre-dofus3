using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>Type/source d'une notification -> icône + couleur côté UI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotifKind>))]
public enum NotifKind { Info, Success, Warning, Error, PmIn, PmOut }

/// <summary>Une notification persistée : titre + corps + horodatage + type + source (ex nom du perso) + lu/non-lu.</summary>
public sealed class NotifItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Ts { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public NotifKind Kind { get; set; } = NotifKind.Info;
    public string? Source { get; set; }
    public bool Read { get; set; }
}

public sealed class NotifFile { public List<NotifItem> Items { get; set; } = new(); }

/// <summary>Persistance de l'historique de notifications dans %APPDATA%/GivreClone/notifications.json. Plafonné (garde les
/// N plus récentes). Chargé au démarrage, réécrit à chaque changement (best-effort).</summary>
public static class NotificationStore
{
    private const int MaxItems = 200;
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "notifications.json");

    public static List<NotifItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // AUDIT M5 : notifications.json contient des MP de TIERS (PII) -> chiffré au repos par DPAPI (comme les comptes).
                // Repli lecture claire pour migrer les anciens fichiers non chiffrés.
                var raw = File.ReadAllText(FilePath);
                var json = GivreClone.Engine.Secrets.Dpapi.Unprotect(raw) ?? raw;
                var f = (NotifFile?)JsonSerializer.Deserialize(json, typeof(NotifFile), AppJsonContext.Default);
                if (f?.Items is { } items)
                {
                    if (items.Count > MaxItems) items.RemoveRange(0, items.Count - MaxItems);
                    return items;
                }
            }
        }
        catch { /* fichier corrompu -> historique vide */ }
        return new List<NotifItem>();
    }

    private static readonly object _pendingLock = new();
    private static List<NotifItem>? _pending;
    private static bool _writerRunning;

    /// <summary>Sauvegarde HORS du thread UI : la sérialisation + le chiffrement DPAPI + l'écriture atomique coûtent jusqu'à
    /// plusieurs secondes (gel de l'app à chaque toast de vente HDV). Un seul écrivain ; seul le DERNIER instantané en attente
    /// est écrit, jamais un ancien après un plus récent.</summary>
    public static void SaveInBackground(IReadOnlyList<NotifItem> items)
    {
        var snapshot = items.Select(Copy).ToList();
        lock (_pendingLock)
        {
            _pending = snapshot;
            if (_writerRunning) return;
            _writerRunning = true;
        }
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            while (true)
            {
                List<NotifItem>? next;
                lock (_pendingLock)
                {
                    next = _pending; _pending = null;
                    if (next is null) { _writerRunning = false; return; }
                }
                Save(next);
            }
        });
    }

    private static NotifItem Copy(NotifItem i) => new()
    {
        Id = i.Id, Ts = i.Ts, Title = i.Title, Body = i.Body, Kind = i.Kind, Source = i.Source, Read = i.Read,
    };

    public static void Save(IReadOnlyList<NotifItem> items)
    {
        try
        {
            var list = new List<NotifItem>(items);
            if (list.Count > MaxItems) list.RemoveRange(0, list.Count - MaxItems);
            var json = JsonSerializer.Serialize(new NotifFile { Items = list }, typeof(NotifFile), AppJsonContext.Default);
            // AUDIT M5 : chiffré DPAPI au repos (MP de tiers = PII). Repli clair si DPAPI indispo (non-Windows).
            var blob = GivreClone.Engine.Secrets.Dpapi.Protect(json);
            AtomicJson.Write(FilePath, string.IsNullOrEmpty(blob) ? json : blob);
        }
        catch { /* best-effort */ }
    }
}
