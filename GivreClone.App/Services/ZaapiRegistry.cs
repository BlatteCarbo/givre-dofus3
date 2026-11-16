using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>Une entrée de Zaapi connu.</summary>
public sealed record ZaapiEntry(long MapId, int SubAreaId, string City, string? Name, DateTime LastSeenUtc);

/// <summary>Saut Zaapi retenu par le planificateur de trajet intra-ville.</summary>
public readonly record struct ZaapiHop(
    long SourceZaapiMap,
    long DestZaapiMap,
    int HopsToSource,
    int HopsFromDest,
    int TotalWalkHops,
    int SavedHops);

/// <summary>
/// Registre des Zaapis connus par ville et sous-zone.
/// Alimenté dynamiquement par :
///   1) les interactifs observés (type 106 ou skill 157) ;
///   2) les événements TeleportDestinationsEvent (type 1) ;
///   3) les transitions du world-graph portant le skill 157 ;
///   4) persistance locale (zaapi-registry.json).
/// </summary>
public sealed class ZaapiRegistry
{
    public const int DefaultCityWalkThreshold = 4;
    private const int MinKamasForZaapi = 20;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<long, ZaapiEntry> _byMap = new();
    private readonly ConcurrentDictionary<string, HashSet<long>> _byCity = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, HashSet<long>> _bySubArea = new();
    private readonly ConcurrentDictionary<int, string> _subAreaToCity = new();

    /// <summary>Nombre total de Zaapis enregistrés.</summary>
    public int Count => _byMap.Count;

    public bool IsKnownZaapi(long mapId) => mapId > 0 && _byMap.ContainsKey(mapId);

    public ZaapiEntry? GetZaapi(long mapId) => _byMap.TryGetValue(mapId, out var e) ? e : null;

    public IReadOnlyList<long> GetAllZaapiMaps() => _byMap.Keys.ToArray();

    public IReadOnlyList<long> GetZaapisForCity(string city)
    {
        string norm = NormalizeCity(city);
        if (norm.Length == 0) return Array.Empty<long>();
        lock (_gate)
        {
            return _byCity.TryGetValue(norm, out var set) ? set.ToArray() : Array.Empty<long>();
        }
    }

    public IReadOnlyList<long> GetZaapisForSubArea(int subAreaId)
    {
        if (subAreaId <= 0) return Array.Empty<long>();
        lock (_gate)
        {
            return _bySubArea.TryGetValue(subAreaId, out var set) ? set.ToArray() : Array.Empty<long>();
        }
    }

    public string? GetCityForMap(long mapId)
        => _byMap.TryGetValue(mapId, out var e) && !string.IsNullOrWhiteSpace(e.City) ? e.City : null;

    public string? GetCityForSubArea(int subAreaId)
        => subAreaId > 0 && _subAreaToCity.TryGetValue(subAreaId, out var c) ? c : null;

    /// <summary>
    /// Normalise le nom d'une ville (minuscules, sans accents, sans préfixes "cité de", etc.).
    /// </summary>
    public static string NormalizeCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return "";
        string s = city.Trim().ToLowerInvariant();

        // Supprime les accents
        string normalizedString = s.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);
        foreach (char c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }
        s = stringBuilder.ToString().Normalize(NormalizationForm.FormC);

        // Supprime les préfixes courants
        if (s.StartsWith("cite de ", StringComparison.Ordinal)) s = s["cite de ".Length..].Trim();
        else if (s.StartsWith("cite d'", StringComparison.Ordinal)) s = s["cite d'".Length..].Trim();
        else if (s.StartsWith("ville de ", StringComparison.Ordinal)) s = s["ville de ".Length..].Trim();
        else if (s.StartsWith("centre-ville de ", StringComparison.Ordinal)) s = s["centre-ville de ".Length..].Trim();
        else if (s.StartsWith("centre-ville d'", StringComparison.Ordinal)) s = s["centre-ville d'".Length..].Trim();

        // Regroupe les variantes connues
        if (s.Contains("bonta", StringComparison.Ordinal)) return "bonta";
        if (s.Contains("brakmar", StringComparison.Ordinal)) return "brakmar";
        if (s.Contains("astrub", StringComparison.Ordinal)) return "astrub";
        if (s.Contains("bourgade", StringComparison.Ordinal) || s.Contains("frigost", StringComparison.Ordinal)) return "bourgade";
        if (s.Contains("sufokia", StringComparison.Ordinal)) return "sufokia";

        return s;
    }

    /// <summary>
    /// Détermine si deux cartes sont situées dans la même ville.
    /// </summary>
    public bool AreInSameCity(long mapA, long mapB, Func<long, int>? subAreaLookup = null, Func<int, string?>? cityLookup = null)
    {
        if (mapA <= 0 || mapB <= 0) return false;
        if (mapA == mapB) return true;

        // 1. Villes associées aux cartes elles-mêmes
        string cityA = NormalizeCity(GetCityForMap(mapA));
        string cityB = NormalizeCity(GetCityForMap(mapB));
        if (cityA.Length > 0 && cityB.Length > 0 && cityA == cityB) return true;

        // 2. Sous-zones des deux cartes
        int subA = subAreaLookup?.Invoke(mapA) ?? (_byMap.TryGetValue(mapA, out var ea) ? ea.SubAreaId : 0);
        int subB = subAreaLookup?.Invoke(mapB) ?? (_byMap.TryGetValue(mapB, out var eb) ? eb.SubAreaId : 0);

        if (subA > 0 && subB > 0)
        {
            if (subA == subB)
            {
                // Même sous-zone : est-ce une sous-zone urbaine / zaapi ?
                if (cityA.Length > 0) return true;
                if (cityB.Length > 0) return true;
                string? cFromSub = GetCityForSubArea(subA);
                if (!string.IsNullOrEmpty(cFromSub)) return true;
                string? cFromLookup = cityLookup?.Invoke(subA);
                if (!string.IsNullOrEmpty(cFromLookup)) return true;
            }

            string subCityA = NormalizeCity(GetCityForSubArea(subA) ?? cityLookup?.Invoke(subA));
            string subCityB = NormalizeCity(GetCityForSubArea(subB) ?? cityLookup?.Invoke(subB));
            if (subCityA.Length > 0 && subCityB.Length > 0 && subCityA == subCityB) return true;

            // Comparaison croisée carte <-> sous-zone
            if (cityA.Length > 0 && subCityB.Length > 0 && cityA == subCityB) return true;
            if (cityB.Length > 0 && subCityA.Length > 0 && cityB == subCityA) return true;
        }

        return false;
    }

    /// <summary>
    /// Récupère l'ensemble des Zaapis d'une ville englobant mapA et/ou mapB.
    /// </summary>
    public IReadOnlyList<long> GetZaapisForCityOf(long mapA, long mapB, Func<long, int>? subAreaLookup = null, Func<int, string?>? cityLookup = null)
    {
        string cityA = NormalizeCity(GetCityForMap(mapA));
        string cityB = NormalizeCity(GetCityForMap(mapB));
        string city = cityA.Length > 0 ? cityA : cityB;

        if (city.Length == 0 && subAreaLookup is not null)
        {
            int saA = subAreaLookup(mapA);
            int saB = subAreaLookup(mapB);
            string subCityA = NormalizeCity(GetCityForSubArea(saA) ?? (saA > 0 ? cityLookup?.Invoke(saA) : null));
            string subCityB = NormalizeCity(GetCityForSubArea(saB) ?? (saB > 0 ? cityLookup?.Invoke(saB) : null));
            city = subCityA.Length > 0 ? subCityA : subCityB;
        }

        if (city.Length > 0)
        {
            var list = GetZaapisForCity(city);
            if (list.Count > 0) return list;
        }

        // Si la ville n'est pas résolue textuellement mais que les cartes partagent une sous-zone connue
        if (subAreaLookup is not null)
        {
            int sa = subAreaLookup(mapA);
            if (sa > 0)
            {
                var list = GetZaapisForSubArea(sa);
                if (list.Count > 0) return list;
            }
        }

        return GetAllZaapiMaps();
    }

    /// <summary>
    /// Enregistre un Zaapi connu.
    /// </summary>
    public bool Register(long mapId, int subAreaId = 0, string? city = null, string? name = null)
    {
        if (mapId <= 0) return false;
        string normCity = NormalizeCity(city);
        if (normCity.Length == 0 && subAreaId > 0 && _subAreaToCity.TryGetValue(subAreaId, out var knownCity))
            normCity = knownCity;

        var entry = new ZaapiEntry(mapId, subAreaId, normCity, name, DateTime.UtcNow);
        _byMap[mapId] = entry;

        lock (_gate)
        {
            if (normCity.Length > 0)
            {
                if (!_byCity.TryGetValue(normCity, out var set)) _byCity[normCity] = set = new HashSet<long>();
                set.Add(mapId);
                if (subAreaId > 0) _subAreaToCity[subAreaId] = normCity;
            }
            if (subAreaId > 0)
            {
                if (!_bySubArea.TryGetValue(subAreaId, out var subSet)) _bySubArea[subAreaId] = subSet = new HashSet<long>();
                subSet.Add(mapId);
            }
        }
        return true;
    }

    /// <summary>
    /// Enregistre un Zaapi à partir d'un interactif observé en jeu.
    /// </summary>
    public bool RegisterInteractive(long mapId, int subAreaId, int elementTypeId, IEnumerable<(int SkillId, bool Enabled)>? skills, string? city = null, string? name = null)
    {
        if (mapId <= 0) return false;
        bool usable = ZaapiTravel.IsUsable(elementTypeId, skills ?? Array.Empty<(int, bool)>());
        if (!usable && elementTypeId != ZaapiTravel.ElementTypeId && !(skills?.Any(s => s.SkillId == ZaapiTravel.SkillId) ?? false))
            return false;

        return Register(mapId, subAreaId, city, name);
    }

    /// <summary>
    /// Enregistre toutes les destinations d'un événement TeleportDestinationsEvent (type 1).
    /// </summary>
    public int RegisterTeleportDestinations(long sourceMapId, IReadOnlyList<TeleportReader.Destination> destinations, Func<int, string?>? cityOrAreaLookup = null)
    {
        if (destinations is null || destinations.Count == 0) return 0;

        // Détermine la ville commune pour tout ce réseau
        string? resolvedCity = null;
        if (sourceMapId > 0 && GetCityForMap(sourceMapId) is { Length: > 0 } c)
            resolvedCity = c;

        if (resolvedCity is null && cityOrAreaLookup is not null)
        {
            foreach (var d in destinations)
            {
                if (d.SubAreaId <= 0) continue;
                string? candidate = cityOrAreaLookup(d.SubAreaId);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    resolvedCity = NormalizeCity(candidate);
                    break;
                }
            }
        }

        // Repli : si la sous-zone d'une des destinations a une ville connue
        if (resolvedCity is null)
        {
            foreach (var d in destinations)
            {
                if (d.SubAreaId > 0 && _subAreaToCity.TryGetValue(d.SubAreaId, out var known))
                {
                    resolvedCity = known;
                    break;
                }
            }
        }

        int count = 0;
        if (sourceMapId > 0)
        {
            int srcSub = destinations.FirstOrDefault(d => d.MapId == sourceMapId).SubAreaId;
            if (Register(sourceMapId, srcSub, resolvedCity)) count++;
        }

        foreach (var dest in destinations)
        {
            if (dest.MapId <= 0) continue;
            if (Register(dest.MapId, dest.SubAreaId, resolvedCity)) count++;
        }

        return count;
    }

    /// <summary>
    /// Alimente le registre depuis le world-graph (cartes avec transition portant le skill 157).
    /// </summary>
    public int RegisterFromWorldGraph(WorldGraph? graph, Func<long, int>? subAreaLookup = null, Func<int, string?>? cityLookup = null)
    {
        if (graph is null) return 0;
        var zaapiMaps = graph.SourceMapsWithSkill(ZaapiTravel.SkillId);
        int added = 0;
        foreach (int mapId in zaapiMaps)
        {
            if (mapId <= 0) continue;
            int subArea = subAreaLookup?.Invoke(mapId) ?? 0;
            string? city = subArea > 0 ? cityLookup?.Invoke(subArea) : null;
            if (Register(mapId, subArea, city)) added++;
        }
        return added;
    }

    /// <summary>
    /// Calcule un saut Zaapi avantageux si origin et targetMap sont dans la même ville
    /// et que la marche directe dépasse le seuil choisi.
    /// </summary>
    public ZaapiHop? PlanZaapiHop(
        long originMap,
        long targetMap,
        int directWalkHops,
        int threshold = DefaultCityWalkThreshold,
        Func<int, int, int>? hopsFinder = null,
        Func<long, int>? subAreaLookup = null,
        Func<int, string?>? cityLookup = null,
        long kamas = -1)
    {
        if (originMap <= 0 || targetMap <= 0 || originMap == targetMap) return null;
        if (kamas >= 0 && kamas < MinKamasForZaapi) return null;

        bool unreachable = directWalkHops <= 0;
        if (!unreachable && directWalkHops < threshold) return null;

        if (!AreInSameCity(originMap, targetMap, subAreaLookup, cityLookup)) return null;

        var cityZaapis = GetZaapisForCityOf(originMap, targetMap, subAreaLookup, cityLookup);
        if (cityZaapis.Count == 0) return null;

        // 1. Zaapi source : le plus proche de originMap
        long sourceZaapi = 0;
        int hopsToSource = int.MaxValue;

        if (cityZaapis.Contains(originMap))
        {
            sourceZaapi = originMap;
            hopsToSource = 0;
        }
        else if (hopsFinder is not null)
        {
            foreach (long z in cityZaapis)
            {
                if (z <= 0 || z > int.MaxValue) continue;
                int h = hopsFinder((int)originMap, (int)z);
                if (h > 0 && h < hopsToSource)
                {
                    hopsToSource = h;
                    sourceZaapi = z;
                }
            }
        }

        if (sourceZaapi == 0 || hopsToSource == int.MaxValue) return null;

        // 2. Zaapi destination : celui duquel on rejoint targetMap avec le moins de marche
        long destZaapi = 0;
        int hopsFromDest = int.MaxValue;

        if (cityZaapis.Contains(targetMap))
        {
            destZaapi = targetMap;
            hopsFromDest = 0;
        }
        else if (hopsFinder is not null)
        {
            foreach (long z in cityZaapis)
            {
                if (z <= 0 || z > int.MaxValue) continue;
                int h = hopsFinder((int)z, (int)targetMap);
                if (h > 0 && h < hopsFromDest)
                {
                    hopsFromDest = h;
                    destZaapi = z;
                }
            }
        }

        if (destZaapi == 0 || hopsFromDest == int.MaxValue) return null;
        if (sourceZaapi == destZaapi) return null; // Le saut ne nous déplace pas

        int totalWalk = hopsToSource + hopsFromDest;
        if (!unreachable && totalWalk >= directWalkHops) return null;

        int savedHops = unreachable ? (directWalkHops <= 0 ? 99 : directWalkHops - totalWalk) : (directWalkHops - totalWalk);
        return new ZaapiHop(sourceZaapi, destZaapi, hopsToSource, hopsFromDest, totalWalk, savedHops);
    }

    /// <summary>
    /// Surcharge acceptant un pathFinder de transitions (MapTransition).
    /// </summary>
    public ZaapiHop? PlanZaapiHop(
        long originMap,
        long targetMap,
        int directWalkHops,
        int threshold,
        Func<int, int, IReadOnlyList<MapTransition>>? pathFinder,
        Func<long, int>? subAreaLookup = null,
        Func<int, string?>? cityLookup = null,
        long kamas = -1)
        => PlanZaapiHop(originMap, targetMap, directWalkHops, threshold,
            hopsFinder: pathFinder is null ? null : (from, to) =>
            {
                var p = pathFinder(from, to);
                return p is { Count: > 0 } ? p.Count : -1;
            },
            subAreaLookup, cityLookup, kamas);

    private sealed class Dto
    {
        public List<ZaapiEntryDto> Entries { get; set; } = new();
    }

    private sealed class ZaapiEntryDto
    {
        public long MapId { get; set; }
        public int SubAreaId { get; set; }
        public string City { get; set; } = "";
        public string? Name { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    public string ToJson()
    {
        var dto = new Dto();
        foreach (var e in _byMap.Values)
        {
            dto.Entries.Add(new ZaapiEntryDto
            {
                MapId = e.MapId,
                SubAreaId = e.SubAreaId,
                City = e.City,
                Name = e.Name,
                LastSeenUtc = e.LastSeenUtc
            });
        }
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    public static ZaapiRegistry FromJson(string? json)
    {
        var reg = new ZaapiRegistry();
        if (string.IsNullOrWhiteSpace(json)) return reg;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto?.Entries is not null)
            {
                foreach (var item in dto.Entries)
                    reg.Register(item.MapId, item.SubAreaId, item.City, item.Name);
            }
        }
        catch { }
        return reg;
    }

    public void SaveToFile(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
        }
        catch { }
    }

    public static ZaapiRegistry LoadFromFile(string path)
    {
        try
        {
            if (File.Exists(path)) return FromJson(File.ReadAllText(path));
        }
        catch { }
        return new ZaapiRegistry();
    }
}
