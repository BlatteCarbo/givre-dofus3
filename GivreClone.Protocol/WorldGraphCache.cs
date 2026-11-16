using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>Format du cache disque du world-graph (JSON compact, sans réflexion : AOT-safe). Par carte, une liste de
/// transitions <c>[cellule, idEnvoyé, direction, type(, critère|null(, arrivée(, zoneDépart, zoneArrivée, élément, skill)))]</c> :
/// 5e élément = critère (chaîne) ou <c>null</c> ; 6e élément = carte d'ARRIVÉE (m_to) quand elle diffère de l'id envoyé
/// (ScrollAction), 0 sinon ; 7e-10e (v5) = zones de départ/arrivée, interactif et skill d'une porte, écrits seulement s'ils
/// diffèrent du cas général (zones 1, pas de porte). Clé <c>"_v":5</c> : sans elle (cache v4, fixtures de test), les zones
/// sont INCONNUES (0) -> planification au niveau carte. Lecture rétro-compatible.</summary>
public static class WorldGraphCache
{
    public const int Version = 5;

    public static string Serialize(IReadOnlyDictionary<int, IReadOnlyList<MapTransition>> byMap)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_v\":").Append(Version);
        bool first = false;
        foreach (var kv in byMap)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(kv.Key).Append("\":[");
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var t = kv.Value[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(t.CellId).Append(',').Append(t.TransitionMapId).Append(',')
                  .Append(t.Direction).Append(',').Append(t.Type);
                bool hasCrit = !string.IsNullOrEmpty(t.Criterion);
                bool hasArrival = t.ArrivalMapId != 0 && t.ArrivalMapId != t.TransitionMapId;
                bool hasExtra = t.FromZone != 1 || t.ArrivalZone != 1 || t.ElementId != 0 || t.SkillId != 0;
                if (hasCrit || hasArrival || hasExtra)
                {
                    if (hasCrit) sb.Append(",\"").Append(t.Criterion!.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    else sb.Append(",null");
                }
                if (hasArrival || hasExtra) sb.Append(',').Append(hasArrival ? t.ArrivalMapId : 0);
                if (hasExtra) sb.Append(',').Append(t.FromZone).Append(',').Append(t.ArrivalZone).Append(',').Append(t.ElementId).Append(',').Append(t.SkillId);
                sb.Append(']');
            }
            sb.Append(']');
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Relit un cache (formats anciens compris : sans 5e/6e élément). Exception si le JSON est invalide.</summary>
    public static Dictionary<int, IReadOnlyList<MapTransition>> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var byMap = new Dictionary<int, IReadOnlyList<MapTransition>>();
        // v5 : zones présentes (1 = cas général non écrit). Avant : inconnues (0) -> BFS au niveau carte.
        int defZone = doc.RootElement.TryGetProperty("_v", out var ver) && ver.ValueKind == JsonValueKind.Number && ver.GetInt32() >= 5 ? 1 : 0;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, out var mapId)) continue;
            var list = new List<MapTransition>();
            foreach (var a in prop.Value.EnumerateArray())
            {
                int n = a.GetArrayLength();
                if (n < 4) continue;
                string? crit = n >= 5 && a[4].ValueKind == JsonValueKind.String ? a[4].GetString() : null;
                int arrival = n >= 6 && a[5].ValueKind == JsonValueKind.Number ? a[5].GetInt32() : 0;
                int Opt(int k, int def) => n > k && a[k].ValueKind == JsonValueKind.Number ? a[k].GetInt32() : def;
                list.Add(new MapTransition(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(),
                                           string.IsNullOrEmpty(crit) ? null : crit, arrival,
                                           Opt(6, defZone), Opt(7, defZone), Opt(8, 0), Opt(9, 0)));
            }
            byMap[mapId] = list;
        }
        return byMap;
    }
}
