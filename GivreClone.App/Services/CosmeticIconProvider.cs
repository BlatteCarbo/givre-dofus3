using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GivreClone.App.Services;

/// <summary>Portraits de TÊTE (« cosmetics/2x/&lt;assetId&gt;.png ») et silhouettes de CORPS (« cosmetics/idles/2x/&lt;assetId&gt;.png ») du bundle
/// Content/Picto/UI/cosmetic_assets_2x.bundle (repli 1x), clés = HeadData.assetId / BodyData.assetId (ex. « 10_1 »). Les deux dossiers
/// réutilisent les mêmes noms de texture : on les distingue par le chemin du conteneur du bundle. Best-effort : bundle absent -> null
/// (l'interface garde le libellé texte).</summary>
public sealed class CosmeticIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private AssetsFileInstance? _af;
    private readonly Dictionary<string, long> _paths = new();   // "head:10_1" / "body:10_1" -> PathId de la Texture2D
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    public bool Loaded => _loaded;
    public int Count => _paths.Count;

    public Bitmap? Head(string assetId) => Get("head:" + assetId);
    public Bitmap? Body(string assetId) => Get("body:" + assetId);

    private Bitmap? Get(string key)
    {
        if (string.IsNullOrEmpty(key) || !_loaded) return null;
        return _cache.GetOrAdd(key, k =>
        {
            try { return _paths.TryGetValue(k, out var id) && _af!.file.GetAssetInfo(id) is { } info ? Decode(info) : null; }
            catch { return null; }
        });
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            try
            {
                var bundle = FindBundle("cosmetic_assets_2x.bundle") ?? FindBundle("cosmetic_assets_1x.bundle");
                if (bundle is not null)
                {
                    _mgr = new AssetsManager();
                    var bun = _mgr.LoadBundleFile(bundle, true);
                    _af = _mgr.LoadAssetsFileFromBundle(bun, 0, false);
                    var ab = _af.file.AssetInfos.FirstOrDefault(i => i.TypeId == 142);   // AssetBundle : table chemin -> objet
                    if (ab is not null)
                        foreach (var e in _mgr.GetBaseField(_af, ab)["m_Container.Array"].Children)
                        {
                            string path = e["first"].AsString.Replace("\\", "/");
                            long id = e["second.asset.m_PathID"].AsLong;
                            if (_af.file.GetAssetInfo(id) is not { TypeId: 28 }) continue;   // la Texture2D, pas le Sprite
                            string name = Path.GetFileNameWithoutExtension(path);
                            _paths[(path.Contains("/idles/", StringComparison.OrdinalIgnoreCase) ? "body:" : "head:") + name] = id;
                        }
                }
            }
            catch { /* best-effort */ }
            _loaded = true;
        }
    }

    private static string? FindBundle(string file)
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "UI", file);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    private Bitmap? Decode(AssetFileInfo info)
    {
        var tex = TextureFile.ReadTextureFile(_mgr!.GetBaseField(_af!, info));
        byte[] bgra = tex.DecodeTextureRaw(tex.FillPictureData(_af), true);
        int w = tex.m_Width, h = tex.m_Height;
        if (bgra is null || w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;
        int stride = w * 4;
        var flip = new byte[w * h * 4];   // les textures Unity sont stockées à l'envers
        for (int y = 0; y < h; y++) Array.Copy(bgra, y * stride, flip, (h - 1 - y) * stride, stride);
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock()) Marshal.Copy(flip, 0, fb.Address, flip.Length);
        return wb;
    }
}
