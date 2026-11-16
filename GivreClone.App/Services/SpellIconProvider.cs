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
/// Fournit l'ICONE d'un sort (Bitmap Avalonia) a partir de son iconId (spellsdataroot.iconId). Les textures sont
/// nommees "sort_&lt;iconId&gt;" dans Content/Picto/Spells/spell_assets_1x.bundle. Best-effort : bundle absent / icone
/// inconnue -&gt; null (l'UI n'affiche alors pas d'icone). Charge le bundle une seule fois, decode a la demande + cache.
/// </summary>
public sealed class SpellIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private AssetsFileInstance? _afInst;
    private Dictionary<string, AssetFileInfo>? _texByName;   // "sort_<iconId>" -> Texture2D
    private readonly ConcurrentDictionary<int, Bitmap?> _cache = new();   // iconId -> Bitmap

    /// <summary>Icone d'un sort par iconId (0 = aucune). Null si non resolue.</summary>
    public Bitmap? Get(int iconId)
    {
        if (iconId <= 0) return null;
        EnsureLoaded();
        if (_cache.TryGetValue(iconId, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_texByName is not null && _texByName.TryGetValue($"sort_{iconId}", out var info))
                bmp = Decode(info);
        }
        catch { bmp = null; }
        if (_loaded) _cache[iconId] = bmp;
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
                    var map = new Dictionary<string, AssetFileInfo>();
                    foreach (var inf in _afInst.file.AssetInfos)
                    {
                        if (inf.TypeId != 28) continue;   // Texture2D "sort_<iconId>"
                        var bf = _mgr.GetBaseField(_afInst, inf);
                        var name = bf["m_Name"].AsString;
                        if (name is not null && name.StartsWith("sort_", StringComparison.Ordinal)) map[name] = inf;
                    }
                    _texByName = map;
                }
            }
            catch { _texByName = null; }
            _loaded = true;
        }
    }

    private static string? FindBundle()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "Spells", "spell_assets_1x.bundle");
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
