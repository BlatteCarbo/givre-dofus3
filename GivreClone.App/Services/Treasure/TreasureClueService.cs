using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Treasure;

/// <summary>Cherche les indices de chasse au trésor sur l'API publique DofusDB (seuls la position et la direction partent sur le réseau) et garde chaque réponse
/// dans un cache disque : une position déjà consultée marche ensuite hors ligne.</summary>
public static class TreasureClueService
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.dofusdb.fr"), Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, IReadOnlyList<TreasureClue>> Memory = new();
    private static readonly object FileGate = new();
    private static string CacheFile => Path.Combine(MZP19LocalClone.DataRoot, "treasure", "clues-v2.json");

    static TreasureClueService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("GivreWork/1.0");

    private static string Key(int x, int y, int d) => $"{x},{y},{d}";

    /// <summary>Indices alignés depuis (x, y) dans la direction donnée (0 droite, 2 bas, 4 gauche, 6 haut), jusqu'à 10 cartes. Cache d'abord, réseau ensuite ;
    /// réseau en panne + position jamais vue = exception.</summary>
    public static async Task<IReadOnlyList<TreasureClue>> LoadAsync(int x, int y, int direction, CancellationToken ct)
    {
        if (!TreasureClues.IsValid(x, y, direction)) throw new ArgumentException("Coordonnées ou direction invalides.");
        string key = Key(x, y, direction);
        if (Memory.TryGetValue(key, out var hit)) return hit;
        if (ReadFile().TryGetValue(key, out var cached)) { Memory[key] = cached; return cached; }

        var all = new List<TreasureClue>();
        int skip = 0, total = int.MaxValue;
        while (skip < total && skip <= TreasureClues.MaxTotal)
        {
            using var resp = await Http.GetAsync(TreasureClues.RequestPath(x, y, direction, skip), ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { total = 0; break; }   // aucune carte d'indice dans cette direction
            resp.EnsureSuccessStatusCode();
            var page = TreasureClues.ParsePage(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), x, y, direction, skip);
            total = page.Total;
            all.AddRange(page.Clues);
            if (page.Count == 0) break;
            skip += page.Count;
        }
        IReadOnlyList<TreasureClue> result = all;
        Memory[key] = result;
        WriteFile(key, result);
        return result;
    }

    // ---- cache disque : { "x,y,d": [ {i,x,y,d,n}, ... ] } ----
    private sealed record Row(int I, int X, int Y, int D, string N);

    private static Dictionary<string, IReadOnlyList<TreasureClue>> ReadFile()
    {
        var res = new Dictionary<string, IReadOnlyList<TreasureClue>>();
        try
        {
            lock (FileGate)
            {
                if (!File.Exists(CacheFile)) return res;
                using var doc = JsonDocument.Parse(File.ReadAllText(CacheFile));
                foreach (var e in doc.RootElement.EnumerateObject())
                    res[e.Name] = e.Value.EnumerateArray().Select(r => new TreasureClue(r.GetProperty("i").GetInt32(), r.GetProperty("x").GetInt32(),
                        r.GetProperty("y").GetInt32(), r.GetProperty("d").GetInt32(), r.GetProperty("n").GetString() ?? "")).ToList();
            }
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { res.Clear(); }
        return res;
    }

    private static void WriteFile(string key, IReadOnlyList<TreasureClue> clues)
    {
        try
        {
            lock (FileGate)
            {
                var all = ReadFile();
                all[key] = clues;
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    foreach (var (k, list) in all)
                    {
                        w.WriteStartArray(k);
                        foreach (var c in list)
                        {
                            w.WriteStartObject(); w.WriteNumber("i", c.Id); w.WriteNumber("x", c.X); w.WriteNumber("y", c.Y); w.WriteNumber("d", c.Distance); w.WriteString("n", c.Name); w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                }
                File.WriteAllBytes(CacheFile, ms.ToArray());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* cache indisponible : on continue sans */ }
    }
}
