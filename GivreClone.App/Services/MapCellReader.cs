using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Lit AU RUNTIME la geometrie des cellules d'une map (mov / los / floor) depuis les bundles Unity du client
/// Dofus 3 (Content/Map/Data/mapdata_assets_world_*.bundle), via AssetsTools.NET - meme technique que les
/// positions. Chaque map = MonoBehaviour "map_&lt;id&gt;" portant mapData.cellsData[560] (champs bool nommes mov/los,
/// int floor). Type tree embarque = aucun Assembly-CSharp requis.
///
/// Le numero de pack du bundle N'EST PAS le map_id -> on construit UNE fois un index map_id -&gt; bundle en lisant
/// seulement le m_Container de l'objet AssetBundle de chaque bundle (rapide : ~3 s pour 569 bundles), puis on lit
/// les cellules a la demande (cache par map_id). Tout est best-effort : client/bundle absent -&gt; null (grille plate).
/// </summary>
public sealed class MapCellReader
{
    private readonly string? _dir;
    private readonly object _indexLock = new();
    private Dictionary<long, string>? _index;                               // map_id -> chemin du bundle
    private readonly ConcurrentDictionary<long, MapCell[]> _cache = new();   // map_id -> cellules
    private readonly ConcurrentDictionary<long, MapExits> _exitsCache = new(); // map_id -> sorties (voisins + fleches)
    private readonly ConcurrentDictionary<long, IReadOnlyDictionary<int, int>> _interPosCache = new(); // map_id -> (element_id -> cellId)

    // Dernier bundle DECOMPRESSE gardé ouvert (LRU de taille 1) : les cartes voisines sont souvent dans le meme
    // bundle -> on evite de re-decompresser ~22 Mo a chaque changement de map dans la meme zone.
    private readonly object _bundleLock = new();
    private string? _openPath;
    private AssetsManager? _openMgr;
    private AssetsFileInstance? _openAf;

    // Cache DISQUE des cartes deja parsees (%APPDATA%\GivreClone\mapcache) : un fichier binaire compact par carte +
    // un manifeste d'empreinte des bundles. Une carte lue une fois n'est plus jamais re-decompressee (meme apres
    // redemarrage) -> chargement instantane. Invalide en bloc quand un patch Dofus change les bundles source.
    private readonly string? _cacheDir;
    private const uint CacheMagic = 0x314D_424D;    // "MBM1" : sentinelle d'entete d'un fichier de carte
    private const int CacheFormat = 7;              // v7 : altitude des ClientMapData sans champ floor ; invalide les anciens reliefs aplatis.
    // Empreinte (FNV-1a 64) des bundles source, ESTAMPILLEE dans chaque .mbin. A la lecture on rejette tout fichier dont
    // l'estampille differe de l'empreinte ACTUELLE -> apres un patch Dofus, les .mbin perimes sont ecartes d'office (meme
    // dans la fenetre avant le wipe), on relit le bundle a jour. Rend l'invalidation sure et sans course. Calcule 1x.
    private long? _stamp;
    private (string Root, List<MapCacheBundle> Bundles)? _fingerprint;
    private bool _indexComplete = true;

    /// <summary>Threads de l'extraction / de l'indexation (un coeur laissé libre pour l'UI et le jeu).</summary>
    private static int Workers => int.TryParse(Environment.GetEnvironmentVariable("GIVRE_MAP_WORKERS"), out var w) && w > 0 ? Math.Min(w, 32) : Math.Clamp(Environment.ProcessorCount - 1, 1, 8);

    public MapCellReader()
    {
        _dir = FindMapDataDir();
        try
        {
            _cacheDir = Environment.GetEnvironmentVariable("GIVRE_MAPCACHE_DIR") is { Length: > 0 } over && Path.IsPathFullyQualified(over)
                ? over : Path.Combine(MZP19LocalClone.DataRoot, "mapcache");
            Directory.CreateDirectory(_cacheDir);
        }
        catch { _cacheDir = null; }   // best-effort : pas de cache disque -> repli sur la lecture des bundles
    }

    public bool Available => _dir is not null;

    /// <summary>Diagnostic léger du manifeste local : nombre de cartes DÉCLARÉES et
    /// drapeau de complétude historique. N'atteste pas l'intégrité des bundles ou
    /// la version du client actif ; ces contrôles sont faits séparément.</summary>
    public (int DeclaredMaps, bool DeclaredComplete) ManifestSummary()
    {
        var manifest = ReadManifest();
        return manifest is { Version: CacheFormat, MapCount: >= 0 }
            ? (manifest.MapCount, manifest.Complete) : (0, false);
    }

    /// <summary>Construit l'index map_id -&gt; bundle en avance (a appeler en tache de fond au demarrage) pour que le
    /// PREMIER changement de carte ne paie pas les ~3 s de scan des bundles. Best-effort, silencieux.</summary>
    public void Prewarm() { try { EnsureIndex(); } catch { /* best-effort */ } }

    private static string? FindMapDataDir()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var d = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content", "Map", "Data");
            if (Directory.Exists(d)) return d;
        }
        return null;
    }

    /// <summary>Cellules (0-559) d'une map, ou null si indisponible (client absent, map introuvable, illisible).</summary>
    public MapCell[]? LoadCells(long mapId)
    {
        if (_cache.TryGetValue(mapId, out var cached)) return cached;
        EnsureMapLoaded(mapId);
        return _cache.TryGetValue(mapId, out var c) ? c : null;
    }

    // Index map_id -> bundle, construit une seule fois : on lit UNIQUEMENT le m_Container de l'AssetBundle (142)
    // de chaque bundle (pas le corps des maps) -> rapide.
    private void EnsureIndex()
    {
        if (_index is not null) return;
        lock (_indexLock)
        {
            if (_index is not null) return;
            var idxBag = new ConcurrentDictionary<long, string>();
            int incomplete = 0;
            var bundlePaths = Directory.EnumerateFiles(_dir!, "mapdata_assets_world_*.bundle").ToList();
            Parallel.ForEach(bundlePaths, new ParallelOptions { MaxDegreeOfParallelism = Workers }, path =>
            {
                try
                {
                    var mgr = new AssetsManager();
                    var bun = mgr.LoadBundleFile(path, true);
                    var afInst = mgr.LoadAssetsFileFromBundle(bun, 0, false);
                    bool indexed = false;
                    foreach (var inf in afInst.file.AssetInfos)
                    {
                        if (inf.TypeId != 142) continue;   // AssetBundle
                        indexed = true;
                        var ab = mgr.GetBaseField(afInst, inf);
                        foreach (var e in ab["m_Container"]["Array"].Children)
                        {
                            var name = e["first"].AsString;   // "map_<id>.asset"
                            if (name is null || !name.StartsWith("map_", StringComparison.Ordinal)) continue;
                            var digits = new string(name.Where(char.IsDigit).ToArray());
                            if (long.TryParse(digits, out var id)) idxBag[id] = path;
                        }
                        break;
                    }
                    if (!indexed) Interlocked.Exchange(ref incomplete, 1);
                    mgr.UnloadAll();
                }
                catch { Interlocked.Exchange(ref incomplete, 1); /* bundle illisible -> index incomplet, jamais sceller */ }
            });
            _indexComplete = incomplete == 0;
            _index = new Dictionary<long, string>(idxBag);
        }
    }

    /// <summary>Sorties (voisins + fleches de bord) d'une carte, depuis le client, ou null si indisponible.</summary>
    public MapExits? LoadExits(long mapId)
    {
        if (_exitsCache.TryGetValue(mapId, out var cached)) return cached;
        EnsureMapLoaded(mapId);
        return _exitsCache.TryGetValue(mapId, out var e) ? e : null;
    }

    /// <summary>Positions des elements interactifs de la carte : element_id (m_interactionId) -> cellId, lues des
    /// references managees de la mapData (ClientInteractiveElementTransform + ...AnimatedElementTransform). Source de
    /// cellule pour les interactifs SANS StatedElement (zaaps). C'est la cellule D'ANCRAGE de l'élément (= sa cellule
    /// protocolaire), PAS une case où se tenir pour l'utiliser ; peut contenir des éléments de cartes voisines.
    /// Null si indisponible. Cache par map_id.</summary>
    public IReadOnlyDictionary<int, int>? LoadInteractivePositions(long mapId)
    {
        if (_interPosCache.TryGetValue(mapId, out var cached)) return cached;
        EnsureMapLoaded(mapId);
        return _interPosCache.TryGetValue(mapId, out var p) ? p : null;
    }

    // Lit UNE SEULE FOIS le MonoBehaviour de la carte et remplit d'un coup les 3 caches (cellules, sorties, positions
    // interactifs). Avant : 3 lectures separees (une par Load*) = 3x GetBaseField (deserialisation complete, dont la
    // lourde references-registry) sur la MEME carte a chaque changement -> lent. Maintenant : 1 deserialisation / carte.
    private void EnsureMapLoaded(long mapId)
    {
        if (_dir is null) return;
        if (_cache.ContainsKey(mapId) && _exitsCache.ContainsKey(mapId) && _interPosCache.ContainsKey(mapId)) return;
        try
        {
            // 1) cache DISQUE (microsecondes, persiste entre sessions) -> aucune decompression de bundle.
            if (TryLoadMapFromDisk(mapId)) return;
            // 2) sinon on lit le bundle (lent la 1re fois) ET on ecrit le cache disque pour les prochaines sessions.
            EnsureIndex();
            if (_index is null || !_index.TryGetValue(mapId, out var bundlePath)) return;
            ReadMapAll(bundlePath, mapId);
        }
        catch { /* best-effort : client/bundle absent -> caches vides -> grille plate */ }
    }

    private void ReadMapAll(string bundlePath, long mapId)
    {
        lock (_bundleLock)
        {
            if (_cache.ContainsKey(mapId) && _exitsCache.ContainsKey(mapId) && _interPosCache.ContainsKey(mapId)) return;
            // Reutilise le bundle deja ouvert (LRU 1) : les cartes voisines sont souvent dans le meme bundle.
            if (_openPath != bundlePath || _openMgr is null || _openAf is null)
            {
                _openMgr?.UnloadAll();
                _openMgr = new AssetsManager();
                var bun = _openMgr.LoadBundleFile(bundlePath, true);
                _openAf = _openMgr.LoadAssetsFileFromBundle(bun, 0, false);
                _openPath = bundlePath;
            }

            var target = "map_" + mapId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var inf in _openAf.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;   // MonoBehaviour
                var bf = _openMgr.GetBaseField(_openAf, inf);
                if (bf["m_Name"].AsString != target) continue;

                var parsed = ParseMapFields(bf);
                if (parsed is null) return;
                var (cells, exits, inter) = parsed.Value;
                _cache[mapId] = cells;
                _exitsCache[mapId] = exits;
                _interPosCache[mapId] = inter;
                TryWriteMapToDisk(mapId, cells, exits, inter);   // persiste -> plus jamais de decompression pour cette carte
                return;   // bundle GARDE ouvert (cache) pour la prochaine map de la meme zone
            }
        }
    }

    // Parse PUR d'une carte (aucun cache, aucun log) : cellules (mov/los/floor/secteurs) + sorties + positions des
    // interactifs. Reutilise par la lecture paresseuse (une carte) ET par la pre-extraction en masse (toutes les cartes
    // d'un bundle en une decompression). Renvoie null si la mapData est absente (asset dummy).
    private static (MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? ParseMapFields(AssetTypeValueField bf)
    {
        var md = bf["mapData"];
        // Certaines cartes (ex. 191106052, Astrub) portent cellsData/voisins/boundingBoxes DIRECTEMENT à la racine de l'asset, sans enveloppe mapData.
        if (md.IsDummy && !bf["cellsData"].IsDummy) md = bf;
        if (md.IsDummy) return null;

        // (1) cellules (mov/los/floor/mapChangeData + secteurs).
        var arr = md["cellsData"]["Array"];
        var cells = new MapCell[arr.Children.Count];
        for (int i = 0; i < arr.Children.Count; i++)
        {
            var c = arr.Children[i];
            // Secteur de mouvement Dofus 3: le VRAI secteur vient de `linkedZone`
            // (Zone = (linkedZone & 0xF8) >> 4) ; `moveZone` est un BOOLEEN (CanMoveZone = moveZone == 1).
            // Deux cellules marchables de secteurs differents ne sont PAS connectees. Lecture defensive (repli 0).
            int moveZone = 0, linkedZone = 0;
            bool nwf = false;
            try { var mz = c["moveZone"]; if (!mz.IsDummy) moveZone = mz.AsInt; } catch { }
            try { var lz = c["linkedZone"]; if (!lz.IsDummy) linkedZone = lz.AsInt; } catch { }
            // Case bloquée EN COMBAT (marchable hors combat). Champ Dofus 3 `nonWalkableDuringFight`;
            // lecture défensive (nom faux/absent -> false -> aucune régression, on retombe sur Mov seul).
            try { var nw = c["nonWalkableDuringFight"]; if (!nw.IsDummy) nwf = nw.AsBool; } catch { }
            int floor = 0, mapChange = 0;
            try
            {
                var fl = c["floor"];
                if (fl.IsDummy) fl = c["altitude"];   // ClientMapData (ex. Sufokia 90703360) : altitude, pas floor
                if (!fl.IsDummy) floor = fl.AsInt;
            }
            catch { }
            try { var mc = c["mapChangeData"]; if (!mc.IsDummy) mapChange = mc.AsInt; } catch { }
            cells[i] = new MapCell(c["cellNumber"].AsInt, c["mov"].AsBool, c["los"].AsBool, floor, mapChange, moveZone, linkedZone, nwf);
        }

        // (2) sorties (voisins + fleches de bord).
        var exits = new MapExits(
            md["topNeighbourId"].AsInt, md["bottomNeighbourId"].AsInt,
            md["leftNeighbourId"].AsInt, md["rightNeighbourId"].AsInt,
            ArrowCells(md, "topArrowCellList"), ArrowCells(md, "bottomArrowCellList"),
            ArrowCells(md, "leftArrowCellList"), ArrowCells(md, "rightArrowCellList"));

        // (3) positions des interactifs : boundingBoxes (inline, source des ZAAPS) + references managees
        //     (ClientInteractive*Transform, interactifs statiques). m_interactionId -> cellId. 0/559 = coins.
        var map = new Dictionary<int, int>();
        var bbox = md["boundingBoxes"];
        var bboxArr = bbox.IsDummy ? null : bbox["Array"];
        if (bboxArr is not null && !bboxArr.IsDummy)
            foreach (var e in bboxArr.Children)
                try
                {
                    int elementId = e["m_interactionId"].AsInt, cell = e["cellId"].AsInt;
                    if (elementId != 0 && cell > 0 && cell < 559) map[elementId] = cell;
                }
                catch { /* champ absent -> ignore */ }
        // ClientMapData (la grande majorité des cartes) : l'identifiant est <interactiveId>k__BackingField (pas m_interactionId,
        // propre à MapMetadata) et un élément NON animé n'a pas de cellId à lui : il est sur la cellule du groupe middleground
        // qui le liste. Sans ça, aucune position n'était lue pour ces cartes et tout interactif sans StatedElement (pupitre de la
        // Malle aux Trésors, portes, ateliers…) était jeté par MapInteractivesReader : « on ne voit pas certains interactifs ».
        var groupCell = new Dictionary<long, int>();
        var mg = md["middlegroundMapElements"];
        var mgArr = mg.IsDummy ? null : mg["Array"];
        if (mgArr is not null && !mgArr.IsDummy)
            foreach (var grp in mgArr.Children)
                try
                {
                    var gc = grp["cellId"]; var els = grp["mapElements"];
                    if (gc.IsDummy || els.IsDummy || els["Array"] is not { IsDummy: false } elArr) continue;
                    foreach (var e in elArr.Children)
                        if (e["rid"] is { IsDummy: false } rid) groupCell[rid.AsLong] = gc.AsInt;
                }
                catch { /* groupe illisible -> ignore */ }
        var refsField = bf["references"];
        var registry = refsField.IsDummy ? null : refsField.Value?.AsManagedReferencesRegistry;
        if (registry is not null)
            foreach (var r in registry.references)
            {
                if (!(r.type?.ClassName ?? "").Contains("Interactive", StringComparison.Ordinal)) continue;
                try
                {
                    var idField = r.data["m_interactionId"];
                    if (idField.IsDummy) idField = r.data["<interactiveId>k__BackingField"];
                    if (idField.IsDummy) continue;
                    var cellField = r.data["cellId"];
                    int elementId = idField.AsInt;
                    int cell = !cellField.IsDummy ? cellField.AsInt : groupCell.TryGetValue(r.rid, out var gcell) ? gcell : -1;
                    if (elementId != 0 && cell > 0 && cell < 559) map.TryAdd(elementId, cell);
                }
                catch { /* champ absent -> ignore */ }
            }
        return (cells, exits, map);
    }

    // =========================== Cache DISQUE (fichier binaire compact par carte) ===========================

    private string CacheFile(long mapId) => Path.Combine(_cacheDir!, mapId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".mbin");

    // Ecrit une carte parsee en binaire compact (~9 Ko) via un temp + rename atomique (facon AdapterSettingsStore).
    private bool TryWriteMapToDisk(long mapId, MapCell[] cells, MapExits exits, IReadOnlyDictionary<int, int> inter, bool atomic = true)
    {
        if (_cacheDir is null) return false;
        try
        {
            var path = CacheFile(mapId);
            // atomic=false (extraction en masse) : écriture directe, 2 opérations disque de moins par carte. Un fichier tronqué par un arrêt brutal
            // est rejeté par CachedMapHeaderValid (taille minimale) et re-extrait au prochain lancement ; un lecteur concurrent échoue sur le
            // FileShare.None et retombe sur la lecture du bundle.
            var tmp = atomic ? path + ".tmp" : path;
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(CacheMagic);
                w.Write(CacheFormat);
                w.Write(Stamp());            // empreinte des bundles au moment de l'ecriture
                w.Write(cells.Length);
                foreach (var c in cells)
                {
                    w.Write((short)c.CellId);                            // 0-559 -> short
                    w.Write((byte)((c.Mov ? 1 : 0) | (c.Los ? 2 : 0) | (c.NonWalkableDuringFight ? 4 : 0))); // mov/los/nwf packes
                    w.Write((short)c.Floor);
                    w.Write(c.MapChangeData);
                    w.Write(c.MoveZone);
                    w.Write(c.LinkedZone);
                }
                w.Write(exits.TopNeighbour); w.Write(exits.BottomNeighbour);
                w.Write(exits.LeftNeighbour); w.Write(exits.RightNeighbour);
                WriteInts(w, exits.TopArrows); WriteInts(w, exits.BottomArrows);
                WriteInts(w, exits.LeftArrows); WriteInts(w, exits.RightArrows);
                w.Write(inter.Count);
                foreach (var kv in inter) { w.Write(kv.Key); w.Write(kv.Value); }
            }
            if (atomic) File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch { return false; /* disque plein / verrou -> cache incomplet, aucun manifeste chaud */ }
    }

    // Validation peu coûteuse SANS charger 15 000 cartes dans la mémoire managée.
    // Les fichiers tronqués/anciens ne comptent jamais comme pré-extraits.
    private bool CachedMapHeaderValid(long mapId, long sourceStamp)
    {
        if (_cacheDir is null) return false;
        try
        {
            using var fs = new FileStream(CacheFile(mapId), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != CacheMagic || r.ReadInt32() != CacheFormat ||
                r.ReadInt64() != sourceStamp) return false;
            int cells = r.ReadInt32();
            return cells == 560 && fs.Length >= 20L + cells * 17L + 16L + 4L * 4L + 4L;
        }
        catch { return false; }
    }

    // Relit une carte depuis le cache disque -> remplit les 3 caches memoire. false si absente/corrompue (repli bundle).
    private bool TryLoadMapFromDisk(long mapId)
    {
        if (_cacheDir is null) return false;
        var path = CacheFile(mapId);
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != CacheMagic || r.ReadInt32() != CacheFormat) return false;
            if (r.ReadInt64() != Stamp()) return false;   // .mbin perime (patch Dofus) -> on ecarte, relecture bundle
            int n = r.ReadInt32();
            if (n < 0 || n > 4096) return false;                        // garde-fou anti-corruption
            var cells = new MapCell[n];
            for (int i = 0; i < n; i++)
            {
                int cellId = r.ReadInt16();
                byte flags = r.ReadByte();
                int floor = r.ReadInt16();
                int mcd = r.ReadInt32();
                int mz = r.ReadInt32();
                int lz = r.ReadInt32();
                cells[i] = new MapCell(cellId, (flags & 1) != 0, (flags & 2) != 0, floor, mcd, mz, lz, (flags & 4) != 0);
            }
            int top = r.ReadInt32(), bot = r.ReadInt32(), left = r.ReadInt32(), right = r.ReadInt32();
            var ta = ReadInts(r); var ba = ReadInts(r); var la = ReadInts(r); var ra = ReadInts(r);
            int ic = r.ReadInt32();
            if (ic < 0 || ic > 100000) return false;
            var inter = new Dictionary<int, int>(ic);
            for (int i = 0; i < ic; i++) { int k = r.ReadInt32(), v = r.ReadInt32(); inter[k] = v; }

            _cache[mapId] = cells;
            _exitsCache[mapId] = new MapExits(top, bot, left, right, ta, ba, la, ra);
            _interPosCache[mapId] = inter;
            return true;
        }
        catch { return false; }
    }

    private static void WriteInts(BinaryWriter w, IReadOnlyList<int> list)
    {
        w.Write(list.Count);
        foreach (var v in list) w.Write(v);
    }

    private static List<int> ReadInts(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > 4096) throw new InvalidDataException("liste de cases invalide");
        var list = new List<int>(n);
        for (int i = 0; i < n; i++) list.Add(r.ReadInt32());
        return list;
    }

    // =========================== Manifeste + invalidation (patch Dofus) ===========================

    // Empreinte actuelle des bundles source : SHA256 du CONTENU et métadonnées. Calculée une fois par instance.
    private (string Root, List<MapCacheBundle> Bundles)? CurrentFingerprint()
    {
        if (_dir is null) return null;
        if (_fingerprint is { } cached) return cached;
        var list = new List<MapCacheBundle>();
        try
        {
            var files = Directory.EnumerateFiles(_dir, "mapdata_assets_world_*.bundle").ToList();
            var inspected = new ConcurrentBag<MapCacheBundle>();
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Workers }, p => inspected.Add(MapCacheBundle.Inspect(p)));
            list.AddRange(inspected);
        }
        catch { return null; }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return _fingerprint = (_dir, list);
    }

    // Empreinte 64 bits (FNV-1a) de l'etat actuel des bundles (racine + nom/taille/date de chacun). Estampillee dans
    // chaque .mbin ; toute divergence a la lecture = fichier perime -> rejete. Calculee une fois puis memorisee (course
    // benigne : deux threads calculent la meme valeur). 0 quand le client est absent (aucun .mbin ne sera ecrit/lu).
    private long Stamp()
    {
        if (_stamp is { } s) return s;
        long h = unchecked((long)14695981039346656037UL);   // FNV-1a 64 offset basis
        void Mix(long v) { h = unchecked((h ^ v) * 1099511628211L); }
        var fp = CurrentFingerprint();
        if (fp is not null)
        {
            foreach (char c in fp.Value.Root) Mix(c);
            foreach (var b in fp.Value.Bundles)
            {
                foreach (char c in b.Name) Mix(c);
                Mix(b.Length); Mix(b.MTimeUtcTicks);
                foreach (char c in b.Sha256) Mix(c);
            }
        }
        _stamp = h;
        return h;
    }

    private MapCacheManifest? ReadManifest()
    {
        if (_cacheDir is null) return null;
        var path = Path.Combine(_cacheDir, "manifest.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.MapCacheManifest); }
        catch { return null; }
    }

    private void WriteManifest(string root, List<MapCacheBundle> bundles, int mapCount)
    {
        if (_cacheDir is null) return;
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var m = new MapCacheManifest { Version = CacheFormat, Root = root, MapCount = mapCount, Complete = true, Bundles = bundles };
            var path = Path.Combine(_cacheDir, "manifest.json");
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(m, AppJsonContext.Default.MapCacheManifest));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* best-effort */ }
    }

    // Le cache est-il A JOUR ? true seulement si le manifeste correspond EXACTEMENT a l'empreinte actuelle des bundles
    // (racine + meme liste de bundles avec meme taille/date). Un patch Dofus fait diverger -> false -> re-extraction.
    private bool IsCacheWarm()
    {
        var fp = CurrentFingerprint();
        if (fp is null) return false;
        var man = ReadManifest();
        if (man is null || man.Version != CacheFormat) return false;
        if (!string.Equals(man.Root, fp.Value.Root, StringComparison.OrdinalIgnoreCase)) return false;
        var a = man.Bundles; var b = fp.Value.Bundles;
        if (a.Count != b.Count || b.Count == 0) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.OrdinalIgnoreCase)
                || a[i].Length != b[i].Length || a[i].MTimeUtcTicks != b[i].MTimeUtcTicks ||
                !string.Equals(a[i].Sha256, b[i].Sha256, StringComparison.Ordinal))
                return false;
        EnsureIndex();
        if (!_indexComplete || _index is null) return false;
        long stamp = Stamp();
        return man.Covers(_index.Count, _index.Keys.Count(id => CachedMapHeaderValid(id, stamp)));
    }

    // =========================== Pre-extraction en masse (pilotee par le modal) ===========================

    /// <summary>Nombre de cartes a (re)extraire : 0 si le cache disque est deja a jour, sinon le total attendu (pour la
    /// barre de progression). Construit l'index map-&gt;bundle au passage (les ~3 s de scan). Best-effort.</summary>
    public int PrewarmPendingCount()
    {
        if (_dir is null || _cacheDir is null) return 0;
        try
        {
            if (IsCacheWarm()) return 0;
            EnsureIndex();
            return _index?.Count ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>Pre-extrait TOUTES les cartes du client, bundle par bundle (chaque bundle DECOMPRESSE une seule fois,
    /// toutes ses cartes parsees d'un coup), vers le cache disque, puis scelle le manifeste. Idempotent : si le cache est
    /// deja a jour on ne fait rien. Rapporte (fait, total). Best-effort, annulable.</summary>
    public async Task ExtractAllAsync(IProgress<(int done, int total)>? progress, CancellationToken ct = default)
    {
        if (_dir is null || _cacheDir is null) return;
        var fp = CurrentFingerprint();
        if (fp is null) return;
        if (IsCacheWarm()) return;
        EnsureIndex();
        if (_index is null || _index.Count == 0) return;
        var index = _index;   // snapshot (immuable une fois construit)

        await Task.Run(() =>
        {
            try { Directory.CreateDirectory(_cacheDir); } catch { }
            // Reprise sûre : conserver les fichiers valides du même hash source, ignorer ceux d'un ancien patch.
            try { var mp = Path.Combine(_cacheDir, "manifest.json"); if (File.Exists(mp)) File.Delete(mp); } catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool skipRefsDiag = Environment.GetEnvironmentVariable("GIVRE_MAP_NOREFS") == "1";   // diagnostic uniquement
            long tLoad = 0, tBase = 0, tParse = 0, tWrite = 0, tTmpl = 0;   // ticks cumules (diagnostic)
            int verifyLeft = int.TryParse(Environment.GetEnvironmentVariable("GIVRE_MAP_VERIFY"), out var vv) && vv > 0 ? vv : 0;   // diagnostic : compare N cartes lecture rapide / complete
            int verified = 0, mismatches = 0, slowFallbacks = 0;
            int total = index.Count, done = 0;
            progress?.Report((0, total));

            // Regroupe les cartes par bundle -> une seule decompression par bundle.
            var byBundle = new Dictionary<string, List<long>>();
            foreach (var kv in index)
            {
                if (!byBundle.TryGetValue(kv.Value, out var l)) { l = new List<long>(); byBundle[kv.Value] = l; }
                l.Add(kv.Key);
            }

            long stamp = Stamp();
            // Un bundle par thread (586 bundles independants, chacun avec son AssetsManager) : l'extraction etait sequentielle.
            var bundleList = byBundle.AsEnumerable();
            if (int.TryParse(Environment.GetEnvironmentVariable("GIVRE_MAP_LIMIT"), out var lim) && lim > 0) bundleList = bundleList.Take(lim);   // diagnostic : N bundles seulement
            Parallel.ForEach(bundleList, new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = ct }, grp =>
            {
                var wanted = new HashSet<long>(grp.Value.Where(id => !CachedMapHeaderValid(id, stamp)));
                int already = grp.Value.Count - wanted.Count;
                if (already > 0) Interlocked.Add(ref done, already);
                if (wanted.Count == 0) { progress?.Report((Volatile.Read(ref done), total)); return; }
                AssetsManager? mgr = null;
                try
                {
                    long q0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    mgr = new AssetsManager();
                    var bun = mgr.LoadBundleFile(grp.Key, true);
                    var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
                    Interlocked.Add(ref tLoad, System.Diagnostics.Stopwatch.GetTimestamp() - q0);
                    var refMan = mgr.GetRefTypeManager(af);
                    var refTemplates = new Dictionary<string, AssetTypeTemplateField?>(StringComparer.Ordinal);
                    bool bundleTrusted = false, bundleDistrust = false;   // première carte du bundle recomparée à la lecture complète (garde-fou si Dofus change la structure)
                    var typeTemplates = new Dictionary<(int, ushort), AssetTypeTemplateField>();   // modèle de types des cartes : identique d'une carte à l'autre
                    foreach (var inf in af.file.AssetInfos)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (inf.TypeId != 114) continue;   // MonoBehaviour
                        long q1 = System.Diagnostics.Stopwatch.GetTimestamp();
                        long mid = 0;
                        // Lecture sélective (MapFastReader) : seul le nécessaire est matérialisé, le reste est sauté sans allocation.
                        MapFastReader.Result res;
                        try
                        {
                            long qt0 = System.Diagnostics.Stopwatch.GetTimestamp();
                            var tkey = ((int)inf.TypeIdOrIndex, af.file.GetScriptIndex(inf));
                            if (!typeTemplates.TryGetValue(tkey, out var tmpl)) typeTemplates[tkey] = tmpl = mgr.GetTemplateBaseField(af, inf, AssetReadFlags.None);
                            Interlocked.Add(ref tTmpl, System.Diagnostics.Stopwatch.GetTimestamp() - qt0);
                            res = MapFastReader.Read(af.file.Reader, tmpl, refMan, refTemplates, inf.GetAbsoluteByteOffset(af.file), name =>
                            {
                                if (name is null || !name.StartsWith("map_", StringComparison.Ordinal)) return false;
                                var digits = new string(name.Where(char.IsDigit).ToArray());
                                return long.TryParse(digits, out mid) && wanted.Contains(mid);   // deja en cache : on s'arrete la
                            });
                        }
                        catch { continue; }
                        Interlocked.Add(ref tBase, System.Diagnostics.Stopwatch.GetTimestamp() - q1);
                        if (!res.Accepted) continue;
                        long q2 = System.Diagnostics.Stopwatch.GetTimestamp();
                        (MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? parsed = bundleDistrust ? null : res.Data;
                        bool verifyThis = (!bundleTrusted && !bundleDistrust && parsed is not null) || (verifyLeft > 0 && Interlocked.Decrement(ref verifyLeft) >= 0);
                        if (parsed is null || verifyThis)
                        {
                            // Structure inattendue pour le lecteur rapide (ou vérification croisée demandée) : ancienne lecture complète.
                            if (parsed is null) { int sf = Interlocked.Increment(ref slowFallbacks); if (sf <= 3) { var why = MapFastReader.LastError ?? "structure inattendue"; GivreLauncher.Core.Transport.TransportLog.Line("[map] repli lecture complete (carte " + mid + ") : " + why); } }
                            (MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? slow = null;
                            try { slow = ParseMapFields(ReadMapBase(mgr, af, inf, false)); } catch { }
                            if (verifyThis)
                            {
                                Interlocked.Increment(ref verified);
                                bool same = SameParsed(parsed, slow);
                                if (same) bundleTrusted = true;
                                else { bundleDistrust = true; parsed = slow; }
                                if (!same) { int mm = Interlocked.Increment(ref mismatches); if (mm <= 5) GivreLauncher.Core.Transport.TransportLog.Line($"[map] VERIFICATION : carte {mid} différente entre lecture rapide et lecture complète"); }
                            }
                            parsed ??= slow;
                        }
                        long q3 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref tParse, q3 - q2);
                        if (parsed is null) continue;
                        var (cells, exits, inter) = parsed.Value;
                        bool wroteOk = TryWriteMapToDisk(mid, cells, exits, inter, atomic: false);
                        Interlocked.Add(ref tWrite, System.Diagnostics.Stopwatch.GetTimestamp() - q3);
                        if (!wroteOk) continue;
                        // Rapport throttle (1er + tous les 100 ; plusieurs threads cadencent le compteur) : eviter des dizaines de milliers de Post sur l'UI ;
                        // le rapport final (total,total) ci-dessous rattrape le reste.
                        if (wanted.Remove(mid)) { int n = Interlocked.Increment(ref done); if (n == 1 || n % 100 == 0) progress?.Report((n, total)); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* bundle illisible -> on continue avec les autres */ }
                finally { try { mgr?.UnloadAll(); } catch { } }
            });
            GivreLauncher.Core.Transport.TransportLog.Line($"[map] extraction des cartes : {done}/{total} en {sw.Elapsed.TotalSeconds:0.0} s ({Workers} threads) ; lecture complete de repli : {slowFallbacks}, verifiees : {verified}, differences : {mismatches} ; temps cumule (s, tous threads) : modele de types {tTmpl / (double)System.Diagnostics.Stopwatch.Frequency:0.0}, chargement bundles {tLoad / (double)System.Diagnostics.Stopwatch.Frequency:0.0}, GetBaseField {tBase / (double)System.Diagnostics.Stopwatch.Frequency:0.0}, ParseMapFields {tParse / (double)System.Diagnostics.Stopwatch.Frequency:0.0}, ecriture {tWrite / (double)System.Diagnostics.Stopwatch.Frequency:0.0}");

            // Ne JAMAIS annoncer 100 % ni sceller si une seule carte manque ou si l'index était incomplet.
            if (_indexComplete && done == total && index.Keys.All(id => CachedMapHeaderValid(id, stamp)))
                WriteManifest(fp.Value.Root, fp.Value.Bundles, done);
            progress?.Report((done, total));
        }, ct).ConfigureAwait(false);
    }

    private static bool SameParsed((MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? a, (MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? b)
    {
        if (a is null || b is null) return a is null && b is null;
        var (ca, ea, ia) = a.Value; var (cb, eb, ib) = b.Value;
        if (ca.Length != cb.Length) return false;
        for (int i = 0; i < ca.Length; i++) if (!ca[i].Equals(cb[i])) return false;
        if (ea.TopNeighbour != eb.TopNeighbour || ea.BottomNeighbour != eb.BottomNeighbour || ea.LeftNeighbour != eb.LeftNeighbour || ea.RightNeighbour != eb.RightNeighbour) return false;
        if (!ea.TopArrows.SequenceEqual(eb.TopArrows) || !ea.BottomArrows.SequenceEqual(eb.BottomArrows) || !ea.LeftArrows.SequenceEqual(eb.LeftArrows) || !ea.RightArrows.SequenceEqual(eb.RightArrows)) return false;
        if (ia.Count != ib.Count) return false;
        foreach (var kv in ia) if (!ib.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        return true;
    }

    /// <summary>Désérialise un MonoBehaviour de carte. <paramref name="skipRefs"/> = ne pas lire le registre des références managées (champ final « references »,
    /// des milliers d'objets de décor par carte) : diagnostic de coût.</summary>
    private static AssetTypeValueField ReadMapBase(AssetsManager mgr, AssetsFileInstance af, AssetFileInfo inf, bool skipRefs)
    {
        if (!skipRefs) return mgr.GetBaseField(af, inf);
        var tmpl = mgr.GetTemplateBaseField(af, inf, AssetReadFlags.None);
        tmpl.Children.RemoveAll(c => c.Name == "references");
        return tmpl.MakeValue(af.file.Reader, inf.GetAbsoluteByteOffset(af.file), null);
    }

    // Liste de cellules-fleches : soit des int, soit des objets (cellId). Souvent vide (traversee libre par le bord).
    private static IReadOnlyList<int> ArrowCells(AssetTypeValueField md, string field)
    {
        var arr = md[field];
        if (arr.IsDummy) return Array.Empty<int>();
        var items = arr["Array"];
        if (items.IsDummy) return Array.Empty<int>();
        var list = new List<int>();
        foreach (var c in items.Children)
        {
            try { list.Add(c.Children.Count == 0 ? c.AsInt : c["cellId"].AsInt); } catch { /* champ inattendu */ }
        }
        return list;
    }
}
