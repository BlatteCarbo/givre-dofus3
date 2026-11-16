using System;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Profils de combat PAR PERSONNAGE, persistes dans %APPDATA%/GivreClone/combat.json. Un profil par nom de perso : deux
/// bots de classes differentes ont donc des rotations/sorts distincts. Get() cree un profil vide a la volee si absent.
/// </summary>
public static class CombatProfileStore
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "combat.json");
    private static CombatProfilesFile _data = new();
    // Chargement raté ET fichier non renommable : les Save vont dans combat.json.recovered (original illisible intact).
    private static bool _writeAside;

    /// <summary>Charge combat.json. Illisible -> mis de côté (.bad-…) et démarrage à vide, jamais écrasé en silence
    /// (<see cref="AtomicJson.LoadStore{T}"/>).</summary>
    public static void Load()
    {
        var r = AtomicJson.LoadStore(FilePath,
            json => (CombatProfilesFile?)JsonSerializer.Deserialize(json, typeof(CombatProfilesFile), AppJsonContext.Default),
            () => new CombatProfilesFile(), Logger.Warn);
        _data = r.Data;
        _writeAside = r.WriteAside;
        // Ancienne clé partagée « ? » (réglages saisis avant l'identification du perso, perdus à l'identification) : plus
        // jamais lue ni écrite (profil transitoire côté onglet Combat) -> retirée au prochain enregistrement.
        _data.Profiles.Remove("?");
    }

    /// <summary>Profil du personnage `character` (cree+enregistre en memoire un profil vide si inconnu). Cle de repli "?".</summary>
    public static CombatProfile Get(string? character)
    {
        string key = string.IsNullOrWhiteSpace(character) ? "?" : character!;
        if (!_data.Profiles.TryGetValue(key, out var p)) { p = new CombatProfile(); _data.Profiles[key] = p; }
        return p;
    }

    /// <summary>Profil ENREGISTRÉ d'un personnage, sans en créer un vide s'il n'existe pas (contrairement à <see cref="Get"/>).</summary>
    public static bool TryGet(string? character, out CombatProfile profile)
    {
        profile = null!;
        return !string.IsNullOrWhiteSpace(character) && _data.Profiles.TryGetValue(character!, out profile!);
    }

    public static void Save()
    {
        try
        {
            AtomicJson.Write(AtomicJson.StorePath(FilePath, _writeAside),
                             JsonSerializer.Serialize(_data, typeof(CombatProfilesFile), AppJsonContext.Default), keepBackup: true);
        }
        catch { /* best-effort */ }
    }
}
