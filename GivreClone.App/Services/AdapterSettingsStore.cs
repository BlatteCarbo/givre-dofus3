using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Reglages persistes par interface reseau : port fixe optionnel. "Fixe" -&gt; la meme URL a chaque activation
/// (utile pour coller le proxy ailleurs) ; sinon port automatique attribue par l'OS. Cle = Id (GUID) de l'interface.
/// </summary>
public static class AdapterSettingsStore
{
    public sealed class Entry { public bool FixedPort { get; set; } public int Port { get; set; } }

    private static readonly string PathFile =
        Path.Combine(MZP19LocalClone.DataRoot, "adapters.json");

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Map = Load();

    public static Entry Get(string id)
    {
        lock (Gate) return Map.TryGetValue(id, out var e) ? new Entry { FixedPort = e.FixedPort, Port = e.Port } : new Entry();
    }

    public static void Set(string id, bool fixedPort, int port)
    {
        lock (Gate) { Map[id] = new Entry { FixedPort = fixedPort, Port = port }; Save(); }
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(PathFile))
                return JsonSerializer.Deserialize(File.ReadAllText(PathFile), AppJsonContext.Default.AdapterMap) ?? new();
        }
        catch { /* store corrompu -> vide */ }
        return new();
    }

    private static void Save()
    {
        var dir = Path.GetDirectoryName(PathFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = PathFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Map, AppJsonContext.Default.AdapterMap));
        File.Move(tmp, PathFile, overwrite: true);
    }
}
