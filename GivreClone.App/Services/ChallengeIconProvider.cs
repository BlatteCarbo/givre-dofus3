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
/// Fournit l'ICÔNE d'un challenge (Bitmap Avalonia) à partir de son iconId (challengesdataroot.iconId). Les textures sont
/// nommées par l'iconId seul (« 6 », « 974 », « 0 » pour Duo/Trio/Quatuor/Chrono…) dans Content/Picto/UI/challenge_assets_2x
/// (64 px, net à l'écran) — repli 1x (32 px). Best-effort : bundle absent / icône inconnue -&gt; null (pas d'icône affichée).
/// Charge le bundle une seule fois, décode à la demande + cache. Les textures sont des SILHOUETTES quasi noires dont seul
/// l'alpha porte le dessin (le jeu les teinte) : <see cref="Get(int, uint)"/> les rend dans une couleur choisie.
/// </summary>
public sealed class ChallengeIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private Dictionary<string, (AssetsFileInstance File, AssetFileInfo Info)>? _texByName;   // "<iconId>" -> Texture2D
    private readonly ConcurrentDictionary<(int Icon, uint Tint), Bitmap?> _cache = new();   // (iconId, teinte) -> Bitmap

    /// <summary>Icône brute par iconId (0 est un iconId valide). Null si non résolue.</summary>
    public Bitmap? Get(int iconId) => Get(iconId, 0);

    /// <summary>Icône TEINTE : chaque pixel prend la couleur <paramref name="tintRgb"/> (0xRRGGBB) en gardant son alpha.
    /// 0 = couleurs d'origine.</summary>
    public Bitmap? Get(int iconId, uint tintRgb)
    {
        if (iconId < 0) return null;
        EnsureLoaded();
        if (_cache.TryGetValue((iconId, tintRgb), out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_texByName is not null && _texByName.TryGetValue(iconId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var t))
                lock (_lock) bmp = Decode(t.File, t.Info, tintRgb);
        }
        catch { bmp = null; }
        if (_loaded) _cache[(iconId, tintRgb)] = bmp;
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
                    var map = new Dictionary<string, (AssetsFileInstance, AssetFileInfo)>();
                    // Les Texture2D peuvent vivre dans n'importe quel sous-fichier du bundle : on les parcourt tous.
                    for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                    {
                        AssetsFileInstance? af;
                        try { af = _mgr.LoadAssetsFileFromBundle(bun, i, false); } catch { continue; }
                        if (af is null) continue;
                        foreach (var inf in af.file.AssetInfos)
                        {
                            if (inf.TypeId != 28) continue;   // Texture2D nommée par iconId
                            var name = _mgr.GetBaseField(af, inf)["m_Name"].AsString;
                            if (!string.IsNullOrEmpty(name)) map.TryAdd(name, (af, inf));
                        }
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
            foreach (var variant in new[] { "challenge_assets_2x.bundle", "challenge_assets_1x.bundle" })
            {
                var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "UI", variant);
                if (File.Exists(f)) return f;
            }
        return null;
    }

    private Bitmap? Decode(AssetsFileInstance af, AssetFileInfo info, uint tintRgb)
    {
        var field = _mgr!.GetBaseField(af, info);
        var tex = TextureFile.ReadTextureFile(field);
        byte[] raw = tex.FillPictureData(af);
        byte[] bgra = tex.DecodeTextureRaw(raw, true);
        int w = tex.m_Width, h = tex.m_Height;
        if (bgra is null || w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;

        int stride = w * 4;
        var flip = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Array.Copy(bgra, y * stride, flip, (h - 1 - y) * stride, stride);
        if (tintRgb != 0)
            for (int i = 0; i < flip.Length; i += 4)
            {
                flip[i] = (byte)(tintRgb & 0xFF); flip[i + 1] = (byte)((tintRgb >> 8) & 0xFF); flip[i + 2] = (byte)((tintRgb >> 16) & 0xFF);
            }

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock()) Marshal.Copy(flip, 0, fb.Address, flip.Length);
        return wb;
    }
}
