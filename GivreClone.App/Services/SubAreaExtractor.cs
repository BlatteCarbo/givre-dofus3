using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET.Extra;

namespace GivreClone.App.Services;

/// <summary>
/// Extraction RUNTIME de la table subAreaId -> nom de zone LOCALISÉ, depuis le client Dofus 3 installé, régénérable à
/// chaque patch. Deux sources croisées :
///   1) bundle Unity Content/Data/*subareas*dataroot* (classe SubArea : champs id + nameId, ~562 sous-zones) ;
///   2) i18n Content/I18n/&lt;lang&gt;.bin (binaire Ankama : [byte len + tag][uint count][count*(int nameId, uint offset)]
///      puis à l'offset une chaîne longueur-7bit + UTF-8) — MÊME format que <see cref="GameNameResolver"/>.
/// Produit la table panel { subAreaId : "nom" } (fr) et une variante multi-langue { subAreaId : {fr,en,es} } pour les
/// labels de zone au survol de la carte du monde. Best-effort : toute absence -> pas de fichier (le panel s'en passe).
/// </summary>
public static class SubAreaExtractor
{
    private static readonly string[] Langs = { "fr", "en", "es" };

    /// <summary>subAreaId -> (langue -> nom). Vide si client/bundle/i18n absents.</summary>
    public static IReadOnlyDictionary<int, Dictionary<string, string>> TryExtract()
    {
        var result = new Dictionary<int, Dictionary<string, string>>();
        var content = DofusPaths.ContentDir();
        if (content is null) return result;

        var dataDir = Path.Combine(content, "Data");
        var bundle = Directory.Exists(dataDir)
            ? Directory.EnumerateFiles(dataDir, "*subareas*dataroot*.asset.bundle").FirstOrDefault()
            : null;
        if (bundle is null) return result;

        Dictionary<int, int> subToName;
        try { subToName = ReadIdNameId(bundle); } catch { return result; }
        if (subToName.Count == 0) return result;

        // i18n par langue (chargé une fois).
        var i18n = new Dictionary<string, (byte[] bin, Dictionary<int, long> idx)>();
        var i18nDir = Path.Combine(content, "I18n");
        foreach (var l in Langs)
        {
            var p = Path.Combine(i18nDir, l + ".bin");
            if (!File.Exists(p)) continue;
            try { i18n[l] = LoadI18n(p); } catch { /* langue illisible -> ignorée */ }
        }
        if (i18n.Count == 0) return result;

        foreach (var (sub, nameId) in subToName)
        {
            var names = new Dictionary<string, string>();
            foreach (var l in Langs)
                if (i18n.TryGetValue(l, out var t) && t.idx.TryGetValue(nameId, out var off)
                    && ReadString(t.bin, (int)off) is { Length: > 0 } s)
                    names[l] = s;
            if (names.Count > 0) result[sub] = names;
        }
        return result;
    }

    /// <summary>Écrit les tables panel dans <paramref name="dir"/> : subarea-names.json { "&lt;id&gt;": "nom fr" } et
    /// subarea-names-i18n.json { "&lt;id&gt;": {"fr":…,"en":…,"es":…} } (minifiées). Best-effort.</summary>
    public static void WritePanelTables(string dir, IReadOnlyDictionary<int, Dictionary<string, string>> data)
    {
        // Flat fr (format demandé par le dev web : subAreaId -> nom).
        var flat = new StringBuilder(data.Count * 24 + 2); flat.Append('{'); bool f1 = true;
        foreach (var kv in data.OrderBy(k => k.Key))
        {
            if (!kv.Value.TryGetValue("fr", out var fr)) continue;
            if (!f1) flat.Append(','); f1 = false;
            flat.Append('"').Append(kv.Key).Append("\":").Append(JsonStr(fr));
        }
        flat.Append('}');
        File.WriteAllText(Path.Combine(dir, "subarea-names.json"), flat.ToString(), new UTF8Encoding(false));

        // Multi-langue (panel localisé).
        var multi = new StringBuilder(data.Count * 48 + 2); multi.Append('{'); bool f2 = true;
        foreach (var kv in data.OrderBy(k => k.Key))
        {
            if (!f2) multi.Append(','); f2 = false;
            multi.Append('"').Append(kv.Key).Append("\":{"); bool f3 = true;
            foreach (var l in Langs)
                if (kv.Value.TryGetValue(l, out var v)) { if (!f3) multi.Append(','); f3 = false; multi.Append('"').Append(l).Append("\":").Append(JsonStr(v)); }
            multi.Append('}');
        }
        multi.Append('}');
        File.WriteAllText(Path.Combine(dir, "subarea-names-i18n.json"), multi.ToString(), new UTF8Encoding(false));
    }

    // --- lecture bundle : id -> nameId (dataroot = 1 MonoBehaviour, refs SerializeReference) ---
    private static Dictionary<int, int> ReadIdNameId(string bundle)
    {
        var map = new Dictionary<int, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            foreach (var r in reg.references)
                try { map[r.data["id"].AsInt] = r.data["nameId"].AsInt; } catch { }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // --- i18n Ankama : mêmes primitives que GameNameResolver ---
    private static (byte[] bin, Dictionary<int, long> idx) LoadI18n(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int pos = 0;
        int len = bytes[pos++];
        pos += len;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        if (count == 0 || count > 5_000_000) return (bytes, new Dictionary<int, long>());
        var idx = new Dictionary<int, long>((int)count);
        for (uint i = 0; i < count; i++)
        {
            int nameId = BitConverter.ToInt32(bytes, pos); pos += 4;
            uint off = BitConverter.ToUInt32(bytes, pos); pos += 4;
            idx[nameId] = off;
        }
        return (bytes, idx);
    }

    private static string? ReadString(byte[] b, int pos)
    {
        if (pos < 0 || pos >= b.Length) return null;
        int n = 0, shift = 0;
        while (true)
        {
            if (pos >= b.Length) return null;
            byte x = b[pos++];
            n |= (x & 0x7F) << shift;
            if ((x & 0x80) == 0) break;
            shift += 7;
            if (shift > 35) return null;
        }
        if (n < 0 || pos + n > b.Length) return null;
        return Encoding.UTF8.GetString(b, pos, n);
    }

    private static string JsonStr(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
