using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>Une entrée du journal HDV persistant : une VENTE (IsTax=false, Amount = kamas encaissés) ou une TAXE payée
/// (IsTax=true, Amount = taxe). Gid/Qty/Name permettent l'agrégation par objet dans le sous-onglet Statistiques.</summary>
public sealed class HdvStatEntry
{
    public long Ts { get; set; }         // ms epoch UTC
    public bool IsTax { get; set; }      // false = vente encaissée ; true = taxe payée
    public long Gid { get; set; }
    public int Qty { get; set; }
    public long Amount { get; set; }
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";   // pour une taxe : "vente" / "reprice"
    // Vente HORS LIGNE : clé de déduplication (la liste du jeu peut revenir à une connexion suivante). Absente sinon.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Key { get; set; }
}

/// <summary>
/// Journal HDV PERSISTANT par personnage : <c>%APPDATA%\GivreClone\hdv-stats\&lt;perso&gt;.jsonl</c>, une entrée JSON PAR LIGNE
/// (append O(1), robuste au crash — pas de réécriture du fichier à chaque vente). Enregistre les ventes abouties (event lqn)
/// et les taxes payées ; lu par le sous-onglet Statistiques HDV (agrégats sur période via <see cref="BotStatsStore.Window"/>).
/// Émondé à 365 jours au 1er accès de session. AOT-safe (JsonSerializerContext source-gen, comme <see cref="BotStatsStore"/>).
/// </summary>
public static partial class HdvStatsStore
{
    private const int RetentionDays = 365;
    private static readonly string Dir = Path.Combine(MZP19LocalClone.DataRoot, "hdv-stats");
    private static readonly object _io = new();
    private static readonly HashSet<string> _pruned = new(StringComparer.OrdinalIgnoreCase);

    [JsonSerializable(typeof(HdvStatEntry))]
    private sealed partial class HdvStatsJson : JsonSerializerContext { }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string FileFor(string charName)
    {
        var safe = new string(charName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "_";
        return Path.Combine(Dir, safe + ".jsonl");
    }

    /// <summary>Enregistre une VENTE aboutie (kamas encaissés).</summary>
    public static void RecordSale(string? charName, long gid, int qty, long amount, string name)
        => Append(charName, new HdvStatEntry { Ts = Now, IsTax = false, Gid = gid, Qty = qty, Amount = amount, Name = name });

    /// <summary>Enregistre une TAXE payée (mise en vente / reprice).</summary>
    public static void RecordTax(string? charName, long gid, int qty, long tax, string name, string action)
        => Append(charName, new HdvStatEntry { Ts = Now, IsTax = true, Gid = gid, Qty = qty, Amount = tax, Name = name, Action = action });

    /// <summary>Enregistre des ventes faites HORS LIGNE à leur date réelle (Ts = date de vente). Une vente dont la clé est déjà
    /// dans le journal (même liste renvoyée à une connexion suivante) n'est pas ré-enregistrée. Renvoie le nombre et le total (kamas) des ventes ajoutées.</summary>
    public static (int Count, long Total) RecordOfflineSales(string? charName, IReadOnlyList<HdvStatEntry> sales)
    {
        if (string.IsNullOrWhiteSpace(charName) || sales.Count == 0) return (0, 0);
        var known = new HashSet<string>(LoadAll(charName!).Select(e => e.Key).OfType<string>(), StringComparer.Ordinal);
        int added = 0; long total = 0;
        foreach (var e in sales)
        {
            if (e.Key is null || !known.Add(e.Key)) continue;
            Append(charName, e);
            added++; total += e.Amount;
        }
        return (added, total);
    }

    private static void Append(string? charName, HdvStatEntry e)
    {
        if (string.IsNullOrWhiteSpace(charName) || e.Amount <= 0) return;
        try
        {
            lock (_io)
            {
                Directory.CreateDirectory(Dir);
                string line = JsonSerializer.Serialize(e, typeof(HdvStatEntry), HdvStatsJson.Default);
                File.AppendAllText(FileFor(charName!), line + "\n");
            }
        }
        catch { /* best-effort : jamais de crash sur échec disque */ }
    }

    /// <summary>Toutes les entrées d'un perso dans [from, to[ (ms epoch). Émonde le fichier (365 j) au 1er accès de session.</summary>
    public static IReadOnlyList<HdvStatEntry> EntriesInWindow(string? charName, long from, long to)
    {
        if (string.IsNullOrWhiteSpace(charName)) return Array.Empty<HdvStatEntry>();
        var all = LoadAll(charName!);
        var list = new List<HdvStatEntry>();
        foreach (var e in all) if (e.Ts >= from && e.Ts < to) list.Add(e);
        return list;
    }

    private static List<HdvStatEntry> LoadAll(string charName)
    {
        var list = new List<HdvStatEntry>();
        try
        {
            lock (_io)
            {
                string path = FileFor(charName);
                if (!File.Exists(path)) return list;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { if (JsonSerializer.Deserialize(line, typeof(HdvStatEntry), HdvStatsJson.Default) is HdvStatEntry e) list.Add(e); } catch { }
                }
                PruneOnceLocked(charName, list, path);
            }
        }
        catch { }
        return list;
    }

    // Émonde une fois par session : réécrit le fichier sans les entrées > 365 j (garde le journal borné). Sous _io.
    private static void PruneOnceLocked(string charName, List<HdvStatEntry> loaded, string path)
    {
        if (!_pruned.Add(charName)) return;
        long cutoff = Now - RetentionDays * 86_400_000L;
        if (loaded.All(e => e.Ts >= cutoff)) return;
        var kept = loaded.Where(e => e.Ts >= cutoff).ToList();
        loaded.Clear(); loaded.AddRange(kept);
        try
        {
            var sb = new StringBuilder();
            foreach (var e in kept) sb.Append(JsonSerializer.Serialize(e, typeof(HdvStatEntry), HdvStatsJson.Default)).Append('\n');
            AtomicJson.Write(path, sb.ToString());
        }
        catch { }
    }
}
