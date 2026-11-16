using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET.Extra;

namespace GivreClone.App.Services;

/// <summary>
/// Extraction RUNTIME de la table map_id -> (posX, posY, worldMap, subAreaId) DIRECTEMENT depuis le bundle Unity du
/// client Dofus 3 installe, sans fichier embarque et toujours a jour au patch. Le bundle porte son type tree
/// (self-describing) : AssetsTools.NET le deserialise nativement, sans Assembly-CSharp (build IL2CPP) ni global-metadata.
///
/// Source = %LOCALAPPDATA%/Ankama/Dofus-dofus3/.../Content/Data/*mapsinformation*dataroot*.asset.bundle
/// (classe MapInformationData, ns Core.DataCenter.Metadata.World : champs id (uint), posX/posY (SInt16),
/// worldMap (SInt8 ; 1 = Monde des Douze, -1 = instance/donjon), subAreaId (UInt16), nameId, tacticalModeTemplateId ;
/// ~15k cartes). On resout par GLOB (les noms de bundles derivent a chaque patch), jamais en dur, avec repli sur
/// Dofus-experimental. Toute erreur/absence -> null (l'appelant retombe alors sur le cache data/map_positions.json).
/// Lecture seule + FileShare.ReadWrite (le client peut tenir le fichier).
/// </summary>
public static class MapDataExtractor
{
    private const int MonoBehaviourTypeId = 114;

    /// <summary>Une carte : coordonnees monde + calque + sous-zone. worldMap 1 = Monde des Douze principal ;
    /// -1 = instance/donjon (pas de position monde) ; autres = Incarnam/Frigost/donjons.</summary>
    public readonly record struct MapInfo(int X, int Y, int WorldMap, int SubAreaId);

    /// <summary>id -> (posX, posY) lu du client, ou null si client/bundle absent ou illisible. (Compat : projette
    /// depuis <see cref="TryExtractFull"/> ; l'app n'utilise que x/y pour le matching des cartes.)</summary>
    public static IReadOnlyDictionary<long, (int X, int Y)>? TryExtract()
    {
        var full = TryExtractFull();
        if (full is null) return null;
        var proj = new Dictionary<long, (int X, int Y)>(full.Count);
        foreach (var kv in full) proj[kv.Key] = (kv.Value.X, kv.Value.Y);
        return proj;
    }

    /// <summary>id -> (posX, posY, worldMap, subAreaId) lu du client, ou null si client/bundle absent ou illisible.</summary>
    public static IReadOnlyDictionary<long, MapInfo>? TryExtractFull()
    {
        var bundle = FindBundle();
        if (bundle is null) return null;
        try { return ReadBundle(bundle); }
        catch { return null; }
    }

    private static string? FindBundle()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var dir = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Data");
            if (!Directory.Exists(dir)) continue;
            var hit = Directory.EnumerateFiles(dir, "*mapsinformation*dataroot*.asset.bundle").FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }

    private static Dictionary<long, MapInfo> ReadBundle(string path)
    {
        var result = new Dictionary<long, MapInfo>();
        var mgr = new AssetsManager();
        var bunInst = mgr.LoadBundleFile(path, true);                 // true = decompresse en memoire
        var afInst = mgr.LoadAssetsFileFromBundle(bunInst, 0, false);
        var af = afInst.file;

        foreach (var inf in af.AssetInfos)
        {
            if (inf.TypeId != MonoBehaviourTypeId) continue;          // DataRoot = MonoBehaviour
            try
            {
                var bf = mgr.GetBaseField(afInst, inf);
                // Les objets reels (MapInformationData) sont dans la registry SerializeReference.
                var reg = bf["references"].Value.AsManagedReferencesRegistry;
                foreach (var reference in reg.references)
                {
                    var data = reference.data;
                    try
                    {
                        long id = data["id"].AsLong;
                        int x = data["posX"].AsInt;
                        int y = data["posY"].AsInt;
                        int w = data["worldMap"].AsInt;              // SInt8 : -1..+40
                        int sub = data["subAreaId"].AsInt;           // UInt16
                        if (id != 0) result[id] = new MapInfo(x, y, w, sub);
                    }
                    catch { /* refObj d'un autre type (pas de id/posX/posY) -> ignore */ }
                }
            }
            catch { /* MonoBehaviour sans references -> ignore */ }
        }
        return result;
    }

    /// <summary>Ecrit la table PANEL au format attendu par le dashboard web : { "&lt;mapId&gt;": [posX, posY, worldMap] }
    /// minifie (regeneree a chaque patch, remplace la table figee du panel). <paramref name="includeSubArea"/>=true
    /// ajoute subAreaId en 4e valeur ([posX, posY, worldMap, subAreaId]) pour les labels de zone au survol.</summary>
    public static void WritePanelTable(string path, IReadOnlyDictionary<long, MapInfo> maps, bool includeSubArea = false)
    {
        var sb = new StringBuilder(maps.Count * 24 + 2);
        sb.Append('{');
        bool first = true;
        foreach (var kv in maps)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":[")
              .Append(kv.Value.X).Append(',').Append(kv.Value.Y).Append(',').Append(kv.Value.WorldMap);
            if (includeSubArea) sb.Append(',').Append(kv.Value.SubAreaId);
            sb.Append(']');
        }
        sb.Append('}');
        File.WriteAllText(path, sb.ToString());
    }
}
