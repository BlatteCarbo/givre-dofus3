using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GivreClone.App.Services.Resources;

/// <summary>Une ressource de récolte : objet récolté, métier (1 = eau, 2 bûcheron, 24 mineur, 26 alchimiste, 28 paysan, 36 pêcheur), niveau de récolte, icône, nom français.</summary>
public sealed record ResourceInfo(int ItemId, int Job, int Level, int IconId, string Name);

/// <summary>Points de récolte d'une ressource sur une carte (nombre recensé ; une ressource déjà récoltée ou inaccessible compte quand même).</summary>
public sealed record ResourceNode(long MapId, int World, int X, int Y, int ItemId, int Count, bool Indoor);

public sealed record ResourceHit(ResourceNode Node, int Distance);

/// <summary>Où récolter quoi : les 86 ressources de récolte et leurs 7 773 emplacements (carte, coordonnées, quantité), tirés des relevés de l'API publique DofusDB
/// (<c>tools/gen_resource_map.py</c>, idée de dofus-miniatures reprise avec l'accord de son auteur). Données embarquées : tout marche hors ligne ; pur.</summary>
public sealed class ResourceMap
{
    public IReadOnlyList<ResourceInfo> Resources { get; }
    private readonly ILookup<int, ResourceNode> _byItem;
    public int NodeCount { get; }

    public ResourceMap(IReadOnlyList<ResourceInfo> resources, IReadOnlyList<ResourceNode> nodes)
    {
        Resources = resources;
        NodeCount = nodes.Count;
        _byItem = nodes.ToLookup(n => n.ItemId);
    }

    public static string JobName(int job) => job switch { 1 => "Base (eau)", 2 => "Bûcheron", 24 => "Mineur", 26 => "Alchimiste", 28 => "Paysan", 36 => "Pêcheur", _ => "Métier " + job };

    public static ResourceMap Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var res = root.GetProperty("resources").EnumerateArray()
            .Select(r => new ResourceInfo(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32(), r[4].GetString() ?? "")).ToList();
        var nodes = root.GetProperty("nodes").EnumerateArray()
            .Select(n => new ResourceNode(n[0].GetInt64(), n[1].GetInt32(), n[2].GetInt32(), n[3].GetInt32(), n[4].GetInt32(), n[5].GetInt32(), n[6].GetInt32() != 0)).ToList();
        return new ResourceMap(res, nodes);
    }

    private static readonly Lazy<ResourceMap> Embedded = new(() =>
    {
        try
        {
            using var s = typeof(ResourceMap).Assembly.GetManifestResourceStream("resource_map.json");
            if (s is null) return new ResourceMap(Array.Empty<ResourceInfo>(), Array.Empty<ResourceNode>());
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd());
        }
        catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return new ResourceMap(Array.Empty<ResourceInfo>(), Array.Empty<ResourceNode>());
        }
    });

    /// <summary>Jeu de données embarqué dans l'application (vide s'il est absent ou illisible).</summary>
    public static ResourceMap Default => Embedded.Value;

    private static string Key(string? s) => Treasure.TreasureClues.SearchKey(s);

    /// <summary>Ressources dont le nom contient le texte (sans accents ni casse), filtrées par métier si demandé ; triées métier puis niveau.</summary>
    public IReadOnlyList<ResourceInfo> Find(string? text, int? job = null)
    {
        string k = Key(text);
        return Resources.Where(r => (job is null || r.Job == job) && (k.Length == 0 || Key(r.Name).Contains(k, StringComparison.Ordinal)))
            .OrderBy(r => r.Job).ThenBy(r => r.Level).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Ressource par id ou par nom (exact d'abord, sinon qui contient ; la plus basse en niveau en cas d'égalité).</summary>
    public ResourceInfo? Resolve(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        if (int.TryParse(idOrName.Trim(), out int id) && Resources.FirstOrDefault(r => r.ItemId == id) is { } byId) return byId;
        string k = Key(idOrName);
        return Resources.FirstOrDefault(r => Key(r.Name) == k) ?? Find(idOrName).FirstOrDefault();
    }

    public IReadOnlyList<ResourceNode> NodesOf(int itemId) => _byItem[itemId].ToList();

    /// <summary>Origine de consultation : un intérieur sans calque (-1) peut hériter de sa sortie
    /// connue. Ne devine jamais le monde à partir des seules coordonnées (Incarnam et Astrub se superposent).</summary>
    public static (int X, int Y, int? World)? Origin(long mapId,
        Func<long, (int X, int Y)?> coordinates, Func<long, int?> world,
        Func<long, IEnumerable<long>> exits)
    {
        if (mapId <= 0 || coordinates(mapId) is not { } c) return null;
        int? layer = world(mapId);
        if (layer != -1) return (c.X, c.Y, layer);

        // Plusieurs portes peuvent mener au même extérieur ; des sorties divergentes
        // ne constituent pas une preuve suffisante pour choisir une origine.
        var outside = exits(mapId).Distinct()
            .Select(id => (Coord: coordinates(id), World: world(id)))
            .Where(p => p.Coord is not null && p.World is >= 0)
            .Select(p => (p.Coord!.Value.X, p.Coord.Value.Y, p.World)).Distinct().ToArray();
        return outside.Length == 1 ? outside[0] : (c.X, c.Y, layer);
    }

    /// <summary>Meilleures cartes pour une ressource. Avec une origine (x, y) : les plus proches d'abord (distance en cartes, à vol d'oiseau, même monde si <paramref name="world"/> est donné),
    /// à distance égale celles qui en ont le plus ; sans origine : celles qui en ont le plus.</summary>
    public IReadOnlyList<ResourceHit> Best(int itemId, int? fromX = null, int? fromY = null, int? world = null, bool includeIndoor = true, int limit = 10)
    {
        var q = _byItem[itemId].Where(n => (includeIndoor || !n.Indoor) && (world is null || n.World == world));
        var hits = q.Select(n => new ResourceHit(n, fromX is { } x && fromY is { } y ? Math.Abs(n.X - x) + Math.Abs(n.Y - y) : 0));
        var ordered = fromX is not null && fromY is not null
            ? hits.OrderBy(h => h.Distance).ThenByDescending(h => h.Node.Count)
            : hits.OrderByDescending(h => h.Node.Count).ThenBy(h => h.Node.MapId);
        return ordered.ThenBy(h => h.Node.MapId).Take(Math.Clamp(limit, 1, 200)).ToList();
    }
}
