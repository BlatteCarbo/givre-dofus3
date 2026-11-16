using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Plannings PAR PERSONNAGE, persistes dans %APPDATA%/GivreClone/planning.json (meme patron que CombatProfileStore).
/// Get() cree un planning vide a la volee si absent. Save() a chaque edition.
/// </summary>
public static class PlanningStore
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "planning.json");
    private static PlanningFile _data = new();
    // Chargement raté ET fichier non renommable : les Save vont dans planning.json.recovered (original illisible intact).
    private static bool _writeAside;

    /// <summary>Charge planning.json. Illisible -> mis de côté (.bad-…) et démarrage à vide, jamais écrasé en silence
    /// (<see cref="AtomicJson.LoadStore{T}"/>).</summary>
    public static void Load()
    {
        var r = AtomicJson.LoadStore(FilePath,
            json => (PlanningFile?)JsonSerializer.Deserialize(json, typeof(PlanningFile), AppJsonContext.Default),
            () => new PlanningFile(), Logger.Warn);
        _data = r.Data;
        _writeAside = r.WriteAside;
    }

    /// <summary>Planning du personnage `character` (cree+enregistre en memoire un planning vide si inconnu). Cle de repli "?".</summary>
    public static CharPlanning Get(string? character)
    {
        string key = string.IsNullOrWhiteSpace(character) ? "?" : character!;
        if (!_data.Plannings.TryGetValue(key, out var p)) { p = new CharPlanning(); _data.Plannings[key] = p; }
        return p;
    }

    /// <summary>Planning EXISTANT du personnage, sans en créer (le scheduler de connexion parcourt des noms arbitraires).</summary>
    public static bool TryGet(string? character, out CharPlanning cp)
    {
        cp = null!;
        return !string.IsNullOrWhiteSpace(character) && _data.Plannings.TryGetValue(character!, out cp!);
    }

    /// <summary>Instantané (nom, planning) de tous les plannings nommés (hors clé de repli "?"). Copie : sûr à itérer.</summary>
    public static List<KeyValuePair<string, CharPlanning>> All()
    {
        var res = new List<KeyValuePair<string, CharPlanning>>(_data.Plannings.Count);
        foreach (var kv in _data.Plannings) if (kv.Key != "?") res.Add(kv);
        return res;
    }

    /// <summary>Si un planning a ete edite sous la cle de repli "?" (nom pas encore resolu) et que `toName` est vierge,
    /// on le RATTACHE au vrai nom des que celui-ci est connu (evite de perdre les plages saisies avant la resolution).</summary>
    public static void MigratePlaceholder(string? toName)
    {
        if (string.IsNullOrWhiteSpace(toName) || toName == "?") return;
        if (!_data.Plannings.TryGetValue("?", out var placeholder) || placeholder.Slots.Count == 0) return;
        if (_data.Plannings.TryGetValue(toName, out var target) && (target.Slots.Count > 0 || target.Enabled)) return;
        _data.Plannings[toName!] = placeholder;
        _data.Plannings.Remove("?");
        Save();
    }

    public static void Save()
    {
        try
        {
            AtomicJson.Write(AtomicJson.StorePath(FilePath, _writeAside),
                             JsonSerializer.Serialize(_data, typeof(PlanningFile), AppJsonContext.Default), keepBackup: true);
        }
        catch { /* best-effort */ }
    }
}
