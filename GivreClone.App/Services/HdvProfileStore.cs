using System;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Config HDV PAR PERSONNAGE, persistée dans %APPDATA%/GivreClone/hdv.json. Même patron que <see cref="BotSettingsStore"/>
/// /<see cref="CombatProfileStore"/> : un objet par nom de perso, Get() crée à la volée, clé de repli "?".
/// </summary>
public static class HdvProfileStore
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "hdv.json");
    private static HdvProfilesFile _data = new();
    // Chargement raté ET fichier non renommable : les Save vont dans hdv.json.recovered (original illisible intact).
    private static bool _writeAside;

    /// <summary>Charge hdv.json. Illisible -> mis de côté (.bad-…) et démarrage à vide, jamais écrasé en silence
    /// (<see cref="AtomicJson.LoadStore{T}"/>).</summary>
    public static void Load()
    {
        var r = AtomicJson.LoadStore(FilePath,
            json => (HdvProfilesFile?)JsonSerializer.Deserialize(json, typeof(HdvProfilesFile), AppJsonContext.Default),
            () => new HdvProfilesFile(), Logger.Warn);
        _data = r.Data;
        _writeAside = r.WriteAside;
    }

    /// <summary>Config HDV du personnage `character` (créée+mémorisée si inconnue). Clé de repli "?".</summary>
    public static HdvProfile Get(string? character)
    {
        string key = string.IsNullOrWhiteSpace(character) ? "?" : character!;
        if (!_data.Profiles.TryGetValue(key, out var p)) { p = new HdvProfile(); _data.Profiles[key] = p; }
        return p;
    }

    /// <summary>Isolation des règles HDV par serveur ET nom ; copie les anciens réglages nom-seul une seule fois
    /// si le profil scoped est nouveau, sans jamais partager une instance mutable entre serveurs.</summary>
    public static HdvProfile GetScoped(string? character, int serverId)
    {
        var key = HdvProfileKey.For(character, serverId);
        if (serverId <= 0 || key == "?") return Get(character);
        if (_data.Profiles.TryGetValue(key, out var existing)) return existing;
        var seeded = _data.Profiles.TryGetValue(character!, out var legacy) ? legacy.Clone() : new HdvProfile();
        _data.Profiles[key] = seeded;
        Save();
        return seeded;
    }

    public static void Save()
    {
        try
        {
            AtomicJson.Write(AtomicJson.StorePath(FilePath, _writeAside),
                             JsonSerializer.Serialize(_data, typeof(HdvProfilesFile), AppJsonContext.Default), keepBackup: true);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Rattache la config éditée sous la clé de repli "?" (avant résolution du nom) au vrai nom, une fois connu.</summary>
    public static void MigratePlaceholder(string? toName)
    {
        if (string.IsNullOrWhiteSpace(toName) || toName == "?") return;
        if (_data.Profiles.TryGetValue("?", out var tmp) && !_data.Profiles.ContainsKey(toName!))
        {
            _data.Profiles[toName!] = tmp;
            _data.Profiles.Remove("?");
            Save();
        }
    }
}
