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
/// Icônes UI DU JEU par NOM (Texture2D), depuis plusieurs bundles aa/StandaloneWindows64 (uiassets_1x prioritaire, puis
/// uielements/icons/uidarkstone) : onglets (map, inventory, bank, character…), catégories d'HDV (auctionHouse*),
/// caractéristiques (tx_actionPoints, tx_pods…), états (pin_mark_red/yellow, check_green, subhint_selected), POI. Même pipeline de
/// décodage que <see cref="InteractiveIconProvider"/> mais indexé par NOM au lieu de l'iconId numérique. Best-effort :
/// bundle absent / nom inconnu -> null (l'appelant garde son repli vectoriel/PNG). Cf mémoire game-asset-icons.
/// </summary>
public sealed class UiGameIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private Dictionary<string, (AssetsFileInstance Af, AssetFileInfo Info)>? _texByName;   // nom d'asset -> (fichier, Texture2D)
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new();
    private int _kicked;   // 0/1 : garde le lancement UNIQUE du préchargement de fond (le thread UI ne doit jamais ouvrir les bundles Unity)

    public bool IsLoaded => _loaded;

    /// <summary>Icône par nom d'asset (ex « map », « bank », « auctionHouseResources », « tx_pods »), ou null. Cachée.
    /// ⚠ Le thread UI ne DOIT JAMAIS ouvrir/scanner les bundles Unity (LoadBundle + GetBaseField sur chaque Texture2D =
    /// gel de plusieurs secondes au 1er clic sur un onglet). Si le préchargement de fond n'a pas encore tourné, on l'amorce
    /// en tâche de fond et on renvoie null IMMÉDIATEMENT (l'icône apparaît une fois le cache chaud). Le préchargement est
    /// amorcé au démarrage (App.OnFrameworkInitializationCompleted) et à l'ouverture de session -> ce chemin froid est rare.</summary>
    public Bitmap? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_cache.TryGetValue(name, out var cached)) return cached;
        if (!_loaded) { KickBackgroundLoad(); return null; }   // JAMAIS de LoadBundle sur le thread UI -> plus de gel de l'onglet
        Bitmap? bmp = null;
        try { if (_texByName is not null && _texByName.TryGetValue(name, out var e)) bmp = Decode(e.Af, e.Info); }
        catch { bmp = null; }
        _cache[name] = bmp;
        return bmp;
    }

    /// <summary>Amorce EnsureLoaded en tâche de fond UNE SEULE FOIS (ouverture + scan des bundles Unity HORS thread UI).
    /// Idempotent : appels multiples (démarrage, ouverture de session, 1er Get froid) ne lancent qu'un seul chargement.</summary>
    public void KickBackgroundLoad()
    {
        if (_loaded || System.Threading.Interlocked.Exchange(ref _kicked, 1) != 0) return;
        System.Threading.Tasks.Task.Run(EnsureLoaded);
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            try
            {
                var bundles = FindBundles();
                if (bundles.Count > 0)
                {
                    _mgr = new AssetsManager();
                    var map = new Dictionary<string, (AssetsFileInstance, AssetFileInfo)>();
                    foreach (var bundle in bundles)
                    {
                        BundleFileInstance bun;
                        try { bun = _mgr.LoadBundleFile(bundle, true); } catch { continue; }
                        for (int fi = 0; fi < bun.file.BlockAndDirInfo.DirectoryInfos.Count; fi++)
                        {
                            AssetsFileInstance af;
                            try { af = _mgr.LoadAssetsFileFromBundle(bun, fi, false); } catch { continue; }
                            if (af?.file?.AssetInfos is null) continue;
                            foreach (var inf in af.file.AssetInfos)
                            {
                                if (inf.TypeId != 28) continue;   // Texture2D (le Sprite homonyme n'est pas nécessaire)
                                AssetTypeValueField bf;
                                try { bf = _mgr.GetBaseField(af, inf); } catch { continue; }
                                var nm = bf["m_Name"].AsString ?? "";
                                // 1er bundle qui déclare un nom GAGNE (uiassets prioritaire) -> les onglets/tx_*/auctionHouse*
                                // gardent leur icône même si un homonyme existe dans icons/uidarkstone (ex « house », « flag »).
                                if (!string.IsNullOrEmpty(nm) && !map.ContainsKey(nm)) map[nm] = (af, inf);
                            }
                        }
                    }
                    _texByName = map.Count > 0 ? map : null;
                }
            }
            catch { _texByName = null; }
            _loaded = true;
        }
    }

    // Bundles UI porteurs d'icônes NOMMÉES, par PRIORITÉ (1er gagne en cas d'homonyme) : uiassets (onglets, tx_*, auctionHouse*)
    // puis uielements (pin_mark_*, check_green, subhint_selected, boutons/cadres), icons (POI minimap), uidarkstone (marqueurs monde).
    private static List<string> FindBundles()
    {
        string[] names = { "uiassets_assets_1x.bundle", "uielements_assets_all.bundle", "icons_assets_1x.bundle", "uidarkstone_assets_all.bundle" };
        var found = new List<string>();
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var dir = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "aa", "StandaloneWindows64");
            foreach (var n in names)
            {
                var f = Path.Combine(dir, n);
                if (File.Exists(f) && !found.Contains(f)) found.Add(f);
            }
            if (found.Count > 0) break;   // premier install root qui porte les bundles UI
        }
        return found;
    }

    private Bitmap? Decode(AssetsFileInstance af, AssetFileInfo info)
    {
        var field = _mgr!.GetBaseField(af, info);
        var tex = TextureFile.ReadTextureFile(field);
        byte[] raw = tex.FillPictureData(af);
        byte[] bgra = tex.DecodeTextureRaw(raw, true);
        int w = tex.m_Width, h = tex.m_Height;
        if (bgra is null || w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;

        int stride = w * 4;
        var flip = new byte[w * h * 4];   // Unity stocke bottom-up -> on retourne pour Avalonia
        for (int y = 0; y < h; y++) Array.Copy(bgra, y * stride, flip, (h - 1 - y) * stride, stride);

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock()) Marshal.Copy(flip, 0, fb.Address, flip.Length);
        return wb;
    }
}
