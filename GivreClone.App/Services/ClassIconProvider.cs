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
/// Fournit le SYMBOLE DE CLASSE (Bitmap Avalonia) d'un personnage a partir de son index de classe (0-18, ordre de
/// l'enum Breed de CharacterInfoReader). Ce sont les emblemes monochromes "symbol_&lt;breedId&gt;" (ex Pandawa = symbol_12,
/// Sram = symbol_4) du bundle Content/Picto/UI/class_assets_.bundle - PAS les portraits couleur "Head_N". Un seul par
/// classe (independant du sexe). Best-effort : bundle absent / classe inconnue -> null (l'UI garde l'icone generique).
/// </summary>
public sealed class ClassIconProvider
{
    private readonly object _lock = new();
    private bool _loaded;
    private AssetsManager? _mgr;
    private AssetsFileInstance? _afInst;
    private Dictionary<string, AssetFileInfo>? _texByName;   // "symbol_N" -> Texture2D
    private readonly ConcurrentDictionary<int, Bitmap?> _cache = new();   // breedId -> Bitmap

    // Index d'enum Breed (0-18) -> id de classe Dofus (1-based ; Forgelance = 20, pas 19). Public : reutilise pour
    // retrouver les sorts de la classe (GameNameResolver.SpellsForBreed) dans l'onglet Combat.
    public static int BreedId(int index) => index < 0 ? 0 : index == 18 ? 20 : index + 1;

    /// <summary>Nombre de symboles "symbol_&lt;N&gt;" indexes dans le bundle (diagnostic). 0 = bundle absent, pas encore
    /// charge, ou textures renommees par un patch -> aucune icone de classe ne se resoudra.</summary>
    public int SymbolCount => _texByName?.Count ?? 0;
    public bool Loaded => _loaded;

    /// <summary>Symbole par ID de classe Dofus (1-20) DIRECTEMENT (pour un membre HORS LIGNE dont on a persisté le BreedId,
    /// sans repasser par l'index d'enum). Sexe ignoré (un emblème par classe). Null si inconnu / bundle absent.</summary>
    public Bitmap? GetById(int breedId)
    {
        if (breedId <= 0) return null;
        if (_cache.TryGetValue(breedId, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_loaded && _texByName is not null && _texByName.TryGetValue($"symbol_{breedId}", out var info))
                bmp = Decode(info);
        }
        catch { bmp = null; }
        if (_loaded) _cache[breedId] = bmp;
        return bmp;
    }

    /// <summary>Symbole de classe par index d'enum (0-18) ; le sexe est ignore (un embleme par classe). Null si inconnu.</summary>
    public Bitmap? Get(int breedIndex, int gender)
    {
        int id = BreedId(breedIndex);
        if (id == 0) return null;
        if (_cache.TryGetValue(id, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (_loaded && _texByName is not null && _texByName.TryGetValue($"symbol_{id}", out var info))
                bmp = Decode(info);
        }
        catch { bmp = null; }
        if (_loaded) _cache[id] = bmp;
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
                        if (inf.TypeId != 28) continue;   // Texture2D "symbol_<N>"
                        var bf = _mgr.GetBaseField(_afInst, inf);
                        var name = bf["m_Name"].AsString;
                        if (name is not null && name.StartsWith("symbol_", StringComparison.Ordinal)) map[name] = inf;
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
            var f = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Picto", "UI", "class_assets_.bundle");
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
