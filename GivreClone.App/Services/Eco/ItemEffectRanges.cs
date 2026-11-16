using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Eco;

/// <summary>Jets THÉORIQUES d'un gabarit d'objet (effet -&gt; [min, max] des dés), c'est-à-dire le maximum NATUREL d'une ligne : la
/// forgemagie en a besoin pour savoir ce qu'est un jet parfait et ce qu'est un over, alors que l'objet du sac ne porte que son jet
/// courant. La base locale du client garde les effets possibles sous forme de références (« rid ») qu'elle ne résout pas ; ils sont
/// donc lus sur l'API publique DofusDB (seul le gid part sur le réseau) puis gardés dans un cache disque : un gabarit déjà vu
/// fonctionne ensuite hors ligne.</summary>
public static class ItemEffectRanges
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.dofusdb.fr"), Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<long, IReadOnlyDictionary<int, (int From, int To)>> Memory = new();
    private static readonly object FileGate = new();

    static ItemEffectRanges() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("GivreWork/1.0");

    private static string CacheFile => Path.Combine(MZP19LocalClone.DataRoot, "forge", "item-effects.json");

    /// <summary>Jets du gabarit déjà connus (mémoire ou cache disque), ou null : aucune requête réseau.</summary>
    public static IReadOnlyDictionary<int, (int From, int To)>? TryGet(long gid)
    {
        if (gid <= 0) return null;
        if (Memory.TryGetValue(gid, out var hit)) return hit;
        if (ReadFile().TryGetValue(gid, out var cached)) { Memory[gid] = cached; return cached; }
        return null;
    }

    /// <summary>Jets du gabarit : cache d'abord, DofusDB ensuite. Null si le gabarit est inconnu ou le réseau indisponible.</summary>
    public static async Task<IReadOnlyDictionary<int, (int From, int To)>?> LoadAsync(long gid, CancellationToken ct = default)
    {
        if (TryGet(gid) is { } known) return known;
        try
        {
            using var resp = await Http.GetAsync($"/items/{gid}?lang=fr", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var parsed = Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (parsed is null) return null;
            Memory[gid] = parsed;
            WriteFile(gid, parsed);
            return parsed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>« effects » de DofusDB : un effet par ligne, from = dé minimum, to = dé maximum (maximum naturel). to == 0 (valeur fixe) -&gt; from.</summary>
    public static IReadOnlyDictionary<int, (int From, int To)>? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("effects", out var effects) || effects.ValueKind != JsonValueKind.Array) return null;
        var map = new Dictionary<int, (int, int)>();
        foreach (var e in effects.EnumerateArray())
        {
            if (!e.TryGetProperty("effectId", out var idv) || !idv.TryGetInt32(out int id)) continue;
            int from = e.TryGetProperty("from", out var f) && f.TryGetInt32(out int fi) ? fi : 0;
            int to = e.TryGetProperty("to", out var t) && t.TryGetInt32(out int ti) ? ti : 0;
            map[id] = (from, Math.Max(from, to));
        }
        return map.Count > 0 ? map : null;
    }

    private static Dictionary<long, IReadOnlyDictionary<int, (int From, int To)>> ReadFile()
    {
        var res = new Dictionary<long, IReadOnlyDictionary<int, (int From, int To)>>();
        try
        {
            lock (FileGate)
            {
                if (!File.Exists(CacheFile)) return res;
                using var doc = JsonDocument.Parse(File.ReadAllText(CacheFile));
                foreach (var item in doc.RootElement.EnumerateObject())
                {
                    if (!long.TryParse(item.Name, out long gid)) continue;
                    var map = new Dictionary<int, (int, int)>();
                    foreach (var fx in item.Value.EnumerateObject())
                        if (int.TryParse(fx.Name, out int id) && fx.Value.ValueKind == JsonValueKind.Array && fx.Value.GetArrayLength() == 2)
                            map[id] = (fx.Value[0].GetInt32(), fx.Value[1].GetInt32());
                    if (map.Count > 0) res[gid] = map;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException) { res.Clear(); }
        return res;
    }

    private static void WriteFile(long gid, IReadOnlyDictionary<int, (int From, int To)> ranges)
    {
        try
        {
            lock (FileGate)
            {
                var all = ReadFile();
                all[gid] = ranges;
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    foreach (var (id, map) in all.OrderBy(x => x.Key))
                    {
                        w.WriteStartObject(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        foreach (var (effect, range) in map.OrderBy(x => x.Key))
                        {
                            w.WriteStartArray(effect.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            w.WriteNumberValue(range.From); w.WriteNumberValue(range.To);
                            w.WriteEndArray();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                File.WriteAllBytes(CacheFile, ms.ToArray());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* cache indisponible : on continue sans */ }
    }
}
