using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// État persistant clé/valeur pour les scripts Lua : modules <c>memory:</c> (PAR PERSONNAGE) et <c>sharedMemory:</c>
/// (GLOBAL, partagé entre tous les persos — coordination chef↔mules). Fichiers JSON dans %APPDATA%/GivreClone/memory :
/// un par personnage (&lt;nom&gt;.json) + un global (_shared.json). Valeurs = string en V1 (les scripts font tonumber()
/// au besoin). IO sérialisées par un verrou global (le store partagé est un vrai point chaud
/// multi-écrivains). Écriture atomique (<see cref="AtomicJson"/>) + source-gen JSON (AOT-safe). Best-effort : aucune
/// exception remontée au script (fichier corrompu = vide). ⚠️ Un perso nommé "_shared" collisionnerait avec le fichier
/// global — impossible en pratique (les noms Dofus ne commencent pas par « _ »).
/// </summary>
public static class LuaMemoryStore
{
    private static readonly string Dir =
        Path.Combine(MZP19LocalClone.DataRoot, "memory");
    private const string SharedKey = "_shared";
    private static readonly object _ioLock = new();
    // fileKey -> contenu chargé (lazy). fileKey = nom de perso assaini, ou "_shared".
    private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    private static string PathFor(string fileKey)
    {
        var safe = string.Concat(fileKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Dir, safe + ".json");
    }

    private static string Norm(string? fileKey) => string.IsNullOrWhiteSpace(fileKey) ? "?" : fileKey!;

    // Charge (une fois) le fichier de `fileKey` en cache. Appelé SOUS _ioLock.
    private static Dictionary<string, string> LoadLocked(string fileKey)
    {
        if (_cache.TryGetValue(fileKey, out var cached)) return cached;
        var d = new Dictionary<string, string>();
        try
        {
            var path = PathFor(fileKey);
            if (File.Exists(path))
                d = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.StringMap) ?? new();
        }
        catch { d = new(); }   // store corrompu -> vide
        _cache[fileKey] = d;
        return d;
    }

    private static void SaveLocked(string fileKey, Dictionary<string, string> d)
    {
        try { AtomicJson.Write(PathFor(fileKey), JsonSerializer.Serialize(d, AppJsonContext.Default.StringMap)); }
        catch { /* best-effort */ }
    }

    public static string? Get(string? charName, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_ioLock) return LoadLocked(Norm(charName)).TryGetValue(key, out var v) ? v : null;
    }

    public static void Set(string? charName, string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        var fk = Norm(charName);
        lock (_ioLock) { var d = LoadLocked(fk); d[key] = value ?? ""; SaveLocked(fk, d); }
    }

    public static bool Delete(string? charName, string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var fk = Norm(charName);
        lock (_ioLock) { var d = LoadLocked(fk); if (!d.Remove(key)) return false; SaveLocked(fk, d); return true; }
    }

    public static IReadOnlyList<KeyValuePair<string, string>> All(string? charName)
    {
        lock (_ioLock) return LoadLocked(Norm(charName)).ToList();
    }

    // --- Global partagé (sharedMemory:) ---
    public static string? SharedGet(string key) => Get(SharedKey, key);
    public static void SharedSet(string key, string value) => Set(SharedKey, key, value);
    public static bool SharedDelete(string key) => Delete(SharedKey, key);
    public static IReadOnlyList<KeyValuePair<string, string>> SharedAll() => All(SharedKey);
}
