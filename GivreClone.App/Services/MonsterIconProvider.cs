using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GivreClone.App.Services;

/// <summary>
/// Fournit l'ICONE d'un monstre (Bitmap Avalonia) a partir de son genericId, extraite AU RUNTIME du bundle Unity
/// du client (Content/Picto/Monsters/monster_assets_1x.bundle) : chaque Texture2D y est nommee par son gfxId (64x64,
/// DXT5 crunched). Chaine : genericId -> gfxId (GameNameResolver) -> Texture2D "gfxId" -> decode (AssetsTools.NET.
/// Texture) -> Bitmap. Verifie en reel. Best-effort : indisponible -> null (l'UI garde le carre placeholder).
/// Chargement en tache de fond ; Get() sur le thread UI (bundle deja charge, decode ~instantane, resultat cache).
/// </summary>
public sealed class MonsterIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private AssetsFileInstance? _afInst;
    private Dictionary<int, AssetFileInfo>? _texByGfx;    // gfxId -> Texture2D
    private Func<int, int?>? _genericToGfx;
    private readonly ConcurrentDictionary<int, Bitmap?> _cache = new();

    public void Init(Func<int, int?> genericToGfx) => _genericToGfx = genericToGfx;

    /// <summary>Icone d'un monstre par genericId, ou null (pas encore charge / introuvable). Resultat cache.</summary>
    public Bitmap? Get(int genericId)
    {
        if (_cache.TryGetValue(genericId, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_loaded && _texByGfx is not null && _genericToGfx is not null
                && _genericToGfx(genericId) is int gfx && _texByGfx.TryGetValue(gfx, out var info))
                bmp = Decode(info);
        }
        catch { bmp = null; }
        if (_loaded) _cache[genericId] = bmp;   // ne pas cacher un null tant que non charge
        return bmp;
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            try
            {
                var bundle = FindBundle();
                if (bundle is not null)
                {
                    _mgr = new AssetsManager();
                    var bun = _mgr.LoadBundleFile(bundle, true);
                    _afInst = _mgr.LoadAssetsFileFromBundle(bun, 0, false);
                    var map = new Dictionary<int, AssetFileInfo>();
                    foreach (var inf in _afInst.file.AssetInfos)
                    {
                        if (inf.TypeId != 28) continue;   // Texture2D (nommee par gfxId)
                        var bf = _mgr.GetBaseField(_afInst, inf);
                        if (int.TryParse(bf["m_Name"].AsString, out var gfx)) map[gfx] = inf;
                    }
                    _texByGfx = map;
                }
            }
            catch { _texByGfx = null; }
            _loaded = true;
        }
    }

    private static string? FindBundle()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "Monsters", "monster_assets_1x.bundle");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    private Bitmap? Decode(AssetFileInfo info)
    {
        var field = _mgr!.GetBaseField(_afInst!, info);
        var tex = TextureFile.ReadTextureFile(field);
        byte[] raw = tex.FillPictureData(_afInst);
        byte[] bgra = tex.DecodeTextureRaw(raw, true);
        int w = tex.m_Width, h = tex.m_Height;
        if (bgra is null || w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;

        // Unity = bottom-up : on retourne verticalement.
        int stride = w * 4;
        var flip = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Array.Copy(bgra, y * stride, flip, (h - 1 - y) * stride, stride);

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock()) Marshal.Copy(flip, 0, fb.Address, flip.Length);
        return wb;
    }
}
