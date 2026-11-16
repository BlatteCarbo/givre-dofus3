using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Réglages PAR PERSONNAGE (déplacement + messagerie), persistés dans %APPDATA%/GivreClone/bot-settings.json. Même patron
/// que <see cref="CombatProfileStore"/>/<see cref="PlanningStore"/> : un objet par nom de perso, Get() crée à la volée.
/// </summary>
public static class BotSettingsStore
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "bot-settings.json");
    private static BotSettingsFile _data = new();
    // Chargement raté ET fichier non renommable : les Save vont dans bot-settings.json.recovered (original illisible intact).
    private static bool _writeAside;
    // Ne pas abandonner le nettoyage .bak si l'écriture disque échoue après protection en mémoire.
    private static bool _pendingPlaintextMigration;

    /// <summary>Charge bot-settings.json. Illisible -> mis de côté (.bad-…) et démarrage à vide, jamais écrasé en silence
    /// (<see cref="AtomicJson.LoadStore{T}"/>).</summary>
    public static void Load()
    {
        var r = AtomicJson.LoadStore(FilePath,
            json => (BotSettingsFile?)JsonSerializer.Deserialize(json, typeof(BotSettingsFile), AppJsonContext.Default),
            () => new BotSettingsFile(), Logger.Warn);
        _data = r.Data;
        _writeAside = r.WriteAside;
        // La première lecture d'un ancien store migre immédiatement les URL en DPAPI.
        // Ne laisse surtout pas l'ancien JSON en clair dans la rotation .bak.
        _pendingPlaintextMigration = _data.Settings.Values.Any(s => s.NeedsWebhookMigration);
        if (_pendingPlaintextMigration) Save();
        else TryProtectLegacyBackup(FilePath + ".bak");
    }

    /// <summary>Réglages du personnage `character` (crée+enregistre en mémoire un objet vide si inconnu). Clé de repli "?".</summary>
    public static BotSettings Get(string? character)
    {
        string key = string.IsNullOrWhiteSpace(character) ? "?" : character!;
        if (!_data.Settings.TryGetValue(key, out var s)) { s = new BotSettings(); _data.Settings[key] = s; }
        return s;
    }

    public static void Save()
    {
        try
        {
            bool migrating = _pendingPlaintextMigration || _data.Settings.Values.Any(s => s.NeedsWebhookMigration);
            _pendingPlaintextMigration = migrating;
            foreach (var settings in _data.Settings.Values) settings.ProtectWebhookForSave();
            string json = JsonSerializer.Serialize(_data, typeof(BotSettingsFile), AppJsonContext.Default);
            string path = AtomicJson.StorePath(FilePath, _writeAside);
            AtomicJson.Write(path, json, keepBackup: !migrating);
            // Préserve les réglages historiques du backup ; réécrit uniquement leur webhook
            // en DPAPI. Rejoué aussi quand le fichier principal était déjà migré.
            if (!TryProtectLegacyBackup(path + ".bak"))
                throw new IOException("Nettoyage du backup Discord non confirmé.");
            _pendingPlaintextMigration = false;
        }
        catch (Exception ex) { Logger.Warn($"Réglages Discord : sauvegarde impossible ({ex.GetType().Name}); aucune URL écrite en clair."); }
    }

    private static bool TryProtectLegacyBackup(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            var backup = (BotSettingsFile?)JsonSerializer.Deserialize(File.ReadAllText(path),
                typeof(BotSettingsFile), AppJsonContext.Default);
            if (backup?.Settings is null || !backup.Settings.Values.Any(s => s.NeedsWebhookMigration)) return true;
            foreach (var settings in backup.Settings.Values) settings.ProtectWebhookForSave();
            AtomicJson.Write(path, JsonSerializer.Serialize(backup, typeof(BotSettingsFile), AppJsonContext.Default));
            return true;
        }
        catch (Exception ex)
        {
            // Aucun chemin, blob, token ou contenu de fichier dans le journal.
            Logger.Warn($"Réglages Discord : protection du backup impossible ({ex.GetType().Name}).");
            return false;
        }
    }

    /// <summary>Rattache les réglages édités sous la clé de repli "?" (avant résolution du nom) au vrai nom, une fois connu.</summary>
    public static void MigratePlaceholder(string? toName)
    {
        if (string.IsNullOrWhiteSpace(toName) || toName == "?") return;
        if (_data.Settings.TryGetValue("?", out var tmp) && !_data.Settings.ContainsKey(toName!))
        {
            _data.Settings[toName!] = tmp;
            _data.Settings.Remove("?");
            Save();
        }
    }
}
