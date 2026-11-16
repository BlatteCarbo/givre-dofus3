using System.Text;
using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>
/// Table STATIQUE map_id -> coordonnees monde (posX, posY, worldMap). En Dofus 3 le PAQUET ne
/// porte PAS les coordonnees (MapComplementaryInformationEvent ne donne que map_id ; la variante WithCoords existe mais
/// worldX/worldY n'y sont jamais serialises) : la position se RESOUT via cette table, extraite de l'export d2o
/// "MapPositions" du jeu (chez un bot tiers: GameData.GetDataObject(MapPositions, mapId).Field("posX"/"posY"/"worldMap")).
///
/// Le <b>worldMap</b> (calque : 1 = Monde des Douze principal, Incarnam/instances = autre valeur, -1 = donjon) est
/// INDISPENSABLE pour DÉSAMBIGUÏSER une coordonnée présente sur PLUSIEURS calques (ex : "2,-1" existe à Incarnam ET à
/// Astrub) : sans lui, une résolution coord->carte pouvait renvoyer la carte d'un AUTRE monde -> chemin impossible.
/// Cache regenerable : data/map_positions.json = { "&lt;mapId&gt;": [posX, posY, worldMap] } (rétro-compat : 2 valeurs =
/// worldMap inconnu). Absence de table/carte = degradation propre (Get -> null, on affiche juste le map_id).
/// </summary>
public sealed class MapPositions
{
    /// <summary>worldMap inconnu (cache legacy à 2 valeurs) : ne matche JAMAIS un vrai worldMap lors de la désambiguïsation.</summary>
    public const int UnknownWorldMap = int.MinValue;

    private readonly IReadOnlyDictionary<long, (int X, int Y, int WorldMap)> _byMap;

    public int Count => _byMap.Count;

    private MapPositions(IReadOnlyDictionary<long, (int X, int Y, int WorldMap)> byMap) => _byMap = byMap;

    public static MapPositions Load(string jsonPath)
    {
        var map = new Dictionary<long, (int X, int Y, int WorldMap)>();
        if (File.Exists(jsonPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!long.TryParse(prop.Name, out var id)) continue;
                var arr = prop.Value;
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2) continue;
                if (!arr[0].TryGetInt32(out var x) || !arr[1].TryGetInt32(out var y)) continue;
                int w = arr.GetArrayLength() >= 3 && arr[2].TryGetInt32(out var wm) ? wm : UnknownWorldMap;   // legacy 2-val -> inconnu
                map[id] = (x, y, w);
            }
        }
        return new MapPositions(map);
    }

    /// <summary>Construit la table depuis un dictionnaire deja extrait (ex : lecture runtime du bundle client).</summary>
    public static MapPositions FromDictionary(IReadOnlyDictionary<long, (int X, int Y, int WorldMap)> byMap) => new(byMap);

    /// <summary>Coordonnees monde (x, y) d'une carte, ou null si inconnue (table a regenerer / carte sans position).</summary>
    public (int X, int Y)? Get(long mapId) => _byMap.TryGetValue(mapId, out var p) ? (p.X, p.Y) : null;

    /// <summary>Calque (worldMap) d'une carte, ou null si inconnu (carte absente / cache legacy sans worldMap).</summary>
    public int? WorldMapOf(long mapId)
        => _byMap.TryGetValue(mapId, out var p) && p.WorldMap != UnknownWorldMap ? p.WorldMap : (int?)null;

    /// <summary>Toutes les cartes situées à la coordonnée (x,y). Plusieurs possibles (extérieur + intérieurs, ou MÊME coord
    /// sur des calques différents : Incarnam vs Astrub) : l'appelant désambiguïse (ex : préférer le worldMap courant). Vide si aucune.</summary>
    public IReadOnlyList<long> MapsAt(int x, int y)
    {
        var list = new List<long>();
        foreach (var kv in _byMap) if (kv.Value.X == x && kv.Value.Y == y) list.Add(kv.Key);
        return list;
    }

    /// <summary>Coordonnées distinctes des cartes d'un calque (mini-carte du monde de l'éditeur de trajet). Parcours complet : à
    /// mettre en cache côté appelant.</summary>
    public HashSet<(int X, int Y)> CoordsOf(int worldMap)
    {
        var set = new HashSet<(int X, int Y)>();
        foreach (var kv in _byMap) if (kv.Value.WorldMap == worldMap) set.Add((kv.Value.X, kv.Value.Y));
        return set;
    }

    /// <summary>Ecrit la table au format cache compact { "&lt;id&gt;": [x, y, worldMap] } (regenere apres extraction runtime).</summary>
    public static void SaveCache(string path, IReadOnlyDictionary<long, (int X, int Y, int WorldMap)> map)
    {
        var sb = new StringBuilder(map.Count * 24 + 2);
        sb.Append('{');
        bool first = true;
        foreach (var kv in map)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(kv.Key).Append("\":[").Append(kv.Value.X).Append(',').Append(kv.Value.Y).Append(',').Append(kv.Value.WorldMap).Append(']');
            first = false;
        }
        sb.Append('}');
        File.WriteAllText(path, sb.ToString());
    }
}
