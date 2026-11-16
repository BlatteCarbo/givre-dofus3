using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.WorldMap;

/// <summary>Une case de la carte du monde : la carte retenue pour la coordonnée (x, y) du calque.</summary>
public readonly record struct WorldTile(int X, int Y, long MapId);

/// <summary>Vraie carte du monde de DofusDB : une image (api.dofusdb.fr/img/maps/0.25/&lt;id&gt;.jpg) par coordonnée. Les positions viennent des données du jeu (hors ligne) ; les images sont
/// téléchargées à la demande, mises en cache sur disque, et un échec (404, réseau) n'est pas réessayé avant 10 minutes. Le choix de la carte par coordonnée est pur (testé).</summary>
public sealed class DofusDbWorldMap
{
    public const string ImageUrl = "https://api.dofusdb.fr/img/maps/0.25/{0}.jpg";
    public const int TileWidth = 477, TileHeight = 242;   // taille réelle des images (≈ 14×20 cellules en perspective)

    private readonly Dictionary<int, Dictionary<(int X, int Y), long>> _tiles = new();
    private readonly Dictionary<long, (int X, int Y, int World)> _positions;

    public DofusDbWorldMap(IReadOnlyDictionary<long, (int X, int Y, int World)> positions)
    {
        _positions = new Dictionary<long, (int X, int Y, int World)>(positions);
        foreach (var g in positions.GroupBy(p => p.Value.World))
        {
            var d = new Dictionary<(int, int), long>();
            foreach (var p in g.OrderBy(p => p.Key)) d.TryAdd((p.Value.X, p.Value.Y), p.Key);   // plus petit id = carte « extérieure » d'origine
            _tiles[g.Key] = d;
        }
    }

    public IEnumerable<int> Worlds => _tiles.Keys.OrderBy(w => w);

    public int TileCount(int world) => _tiles.TryGetValue(world, out var d) ? d.Count : 0;

    public long? MapAt(int world, int x, int y) => _tiles.TryGetValue(world, out var d) && d.TryGetValue((x, y), out var id) ? id : null;

    public (int X, int Y, int World)? PositionOf(long mapId) => _positions.TryGetValue(mapId, out var p) ? p : null;

    /// <summary>Cases du calque dans le rectangle de coordonnées (inclus).</summary>
    public IEnumerable<WorldTile> TilesIn(int world, int x0, int y0, int x1, int y1)
    {
        if (!_tiles.TryGetValue(world, out var d)) yield break;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (d.TryGetValue((x, y), out var id)) yield return new WorldTile(x, y, id);
    }

    public (int MinX, int MinY, int MaxX, int MaxY)? Bounds(int world)
    {
        if (!_tiles.TryGetValue(world, out var d) || d.Count == 0) return null;
        return (d.Keys.Min(k => k.X), d.Keys.Min(k => k.Y), d.Keys.Max(k => k.X), d.Keys.Max(k => k.Y));
    }

    /// <summary>Tuile rendue à l'écran : coordonnée monde → pixel (une case = TileWidth × TileHeight × zoom).</summary>
    public static (double X, double Y) ToScreen(int x, int y, double zoom, double offX, double offY) => (offX + x * TileWidth * zoom, offY + y * TileHeight * zoom);

    /// <summary>Coordonnée monde sous un pixel écran (inverse de <see cref="ToScreen"/>).</summary>
    public static (int X, int Y) FromScreen(double px, double py, double zoom, double offX, double offY) =>
        ((int)Math.Floor((px - offX) / (TileWidth * zoom)), (int)Math.Floor((py - offY) / (TileHeight * zoom)));

    // ---------------------------------------------------------------- images (cache disque + réseau)
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Gate = new(6);
    private static readonly ConcurrentDictionary<long, long> Failed = new();   // carte → tick du dernier échec
    private static readonly ConcurrentDictionary<long, Task<string?>> Pending = new();
    private const long RetryAfterMs = 10 * 60 * 1000;

    /// <summary>Dossier du cache d'images (l'application le fixe sous son dossier de données).</summary>
    public static string CacheFolder { get; set; } = Path.Combine(Path.GetTempPath(), "givre-worldmap");

    public const string FullImageUrl = "https://api.dofusdb.fr/img/maps/1/{0}.jpg";

    public static string FullCachePath(long mapId) => Path.Combine(CacheFolder, "full", mapId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");

    /// <summary>Image pleine taille (~1910×970) de la carte, même mécanisme (cache disque, échec mémorisé) ; null si indisponible.</summary>
    public static Task<string?> EnsureFullImageAsync(long mapId, CancellationToken ct = default)
    {
        string path = FullCachePath(mapId);
        if (File.Exists(path)) return Task.FromResult<string?>(path);
        long key = -mapId;   // clé distincte de la miniature
        if (Failed.TryGetValue(key, out var at) && Environment.TickCount64 - at < RetryAfterMs) return Task.FromResult<string?>(null);
        return Pending.GetOrAdd(key, _ => Download(mapId, path, FullImageUrl, key, ct));
    }

    public static string CachePath(long mapId) => Path.Combine(CacheFolder, mapId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");

    /// <summary>Chemin local de l'image (téléchargée si besoin) ; null si indisponible. Plusieurs demandes de la même image partagent un seul téléchargement.</summary>
    public static Task<string?> EnsureImageAsync(long mapId, CancellationToken ct = default)
    {
        string path = CachePath(mapId);
        if (File.Exists(path)) return Task.FromResult<string?>(path);
        if (Failed.TryGetValue(mapId, out var at) && Environment.TickCount64 - at < RetryAfterMs) return Task.FromResult<string?>(null);
        return Pending.GetOrAdd(mapId, id => Download(id, path, ImageUrl, id, ct));
    }

    private static async Task<string?> Download(long mapId, string path, string urlFormat, long key, CancellationToken ct)
    {
        try
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var resp = await Http.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, urlFormat, mapId), ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { Failed[key] = Environment.TickCount64; return null; }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 200) { Failed[key] = Environment.TickCount64; return null; }
                string tmp = path + ".part";
                await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
                return path;
            }
            finally { Gate.Release(); }
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException or UnauthorizedAccessException)
        {
            Failed[key] = Environment.TickCount64;
            return null;
        }
        finally { Pending.TryRemove(key, out _); }
    }
}

/// <summary>Marche par coordonnées : direction(s) de sortie à essayer pour aller de (cx,cy) vers (tx,ty), l'axe au plus grand écart d'abord ; vide si arrivé.</summary>
public static class CoordWalk
{
    public static string[] Directions(int cx, int cy, int tx, int ty)
    {
        int dx = tx - cx, dy = ty - cy;
        string? h = dx > 0 ? "right" : dx < 0 ? "left" : null, v = dy > 0 ? "bottom" : dy < 0 ? "top" : null;
        var list = Math.Abs(dx) >= Math.Abs(dy) ? new[] { h, v } : new[] { v, h };
        return list.Where(s => s is not null).Select(s => s!).ToArray();
    }
}
