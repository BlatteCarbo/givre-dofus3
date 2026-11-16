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
/// Fournit l'ICONE d'un element interactif (Bitmap Avalonia) a partir de son skill_id, via la chaine
/// skill -> gatheredRessourceItem -> item.iconId (GameNameResolver.InteractiveIconId), puis Texture2D "iconId" du
/// bundle Content/Picto/Items/item_assets_1x.bundle (meme format que les icones de monstres). Best-effort : pas de
/// ressource associee (zaap) ou bundle absent -> null (l'UI garde le losange). Meme patron que MonsterIconProvider.
/// </summary>
public sealed class InteractiveIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private AssetsFileInstance? _afInst;
    private Dictionary<int, AssetFileInfo>? _texByIcon;   // iconId -> Texture2D
    private Func<int, int?>? _skillToIcon;
    private readonly ConcurrentDictionary<int, Bitmap?> _cache = new();   // skill_id -> Bitmap
    private readonly ConcurrentDictionary<int, Bitmap?> _iconCache = new();   // iconId -> Bitmap (acces direct)

    public void Init(Func<int, int?> skillToIcon) => _skillToIcon = skillToIcon;

    /// <summary>Le bundle d'icones est-il charge ? (avant, Get/GetByIcon renvoient null -> l'appelant doit re-resoudre ensuite).</summary>
    public bool IsLoaded => _loaded;

    /// <summary>Icone d'une ressource par son iconId d'objet DIRECT (ex recolte : GameNameResolver.ItemIconId(gid)). Null
    /// si non chargee/introuvable. Resultat cache. Reutilise le meme pipeline de textures que <see cref="Get"/>.</summary>
    public Bitmap? GetByIcon(int iconId)
    {
        if (iconId <= 0) return null;
        if (_iconCache.TryGetValue(iconId, out var cached)) return cached;
        Bitmap? bmp = null;
        try { if (_loaded && _texByIcon is not null && _texByIcon.TryGetValue(iconId, out var info)) bmp = Decode(info); }
        catch { bmp = null; }
        if (_loaded) _iconCache[iconId] = bmp;
        return bmp;
    }

    /// <summary>Icone de l'interactif par skill_id (du 1er enabled_skill), ou null. Resultat cache.</summary>
    public Bitmap? Get(int skillId)
    {
        if (_cache.TryGetValue(skillId, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_loaded && _texByIcon is not null && _skillToIcon is not null
                && _skillToIcon(skillId) is int icon && _texByIcon.TryGetValue(icon, out var info))
                bmp = Decode(info);
        }
        catch { bmp = null; }
        if (_loaded) _cache[skillId] = bmp;
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
                        if (inf.TypeId != 28) continue;   // Texture2D (nommee par iconId)
                        var bf = _mgr.GetBaseField(_afInst, inf);
                        if (int.TryParse(bf["m_Name"].AsString, out var icon)) map[icon] = inf;
                    }
                    _texByIcon = map;
                }
            }
            catch { _texByIcon = null; }
            _loaded = true;
        }
    }

    private static string? FindBundle()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "Items", "item_assets_1x.bundle");
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

        int stride = w * 4;
        var flip = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Array.Copy(bgra, y * stride, flip, (h - 1 - y) * stride, stride);

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock()) Marshal.Copy(flip, 0, fb.Address, flip.Length);
        return wb;
    }
}
