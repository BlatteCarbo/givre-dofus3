using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace GivreClone.App.Services;

/// <summary>Dégâts d'un sort (à un grade) lus de spelllevelsdataroot : Min/Max = somme des dés élémentaires,
/// Element = effectElement dominant (voir <see cref="GameNameResolver.ElementName"/>), MaxPerTurn/MaxPerTarget =
/// limites de cast réelles du jeu. Avg = moyenne des dégâts.</summary>
public readonly record struct SpellDamageInfo(int Min, int Max, int Element, int MaxPerTurn, int MaxPerTarget)
{
    public int Avg => (Min + Max) / 2;
}

/// <summary>
/// Resout les NOMS de monstres et de PNJ a partir de leur id, en 2 sauts cote client:
///   1) id -> nameId : bundles Unity Content/Data/*monstersdataroot* et *npcsdataroot* (AssetsTools.NET, type tree
///      embarque ; champs id + nameId ; ~5134 monstres, ~6467 PNJ).
///   2) nameId -> texte localise : Content/I18n/&lt;lang&gt;.bin (binaire Ankama : [byte len + nom][uint count]
///      [count*(int nameId, uint offset)] puis a l'offset une chaine 7-bit-length + UTF-8).
/// Verifie en reel : monstre 99 -> "Grand Pa Wabbit" ; PNJ 730 -> "Nacf Virgil", 100 -> "Banquier".
/// Best-effort : donnees absentes ou pas encore chargees -> null. EnsureLoaded() est idempotent (a lancer en fond).
/// </summary>
public sealed class GameNameResolver
{
    private readonly object _lock = new();
    private bool _initDone;
    private Dictionary<int, int>? _monsterToNameId;
    private Dictionary<int, int>? _monsterGfx;      // genericId -> gfxId (icone)
    private Dictionary<int, int>? _npcToNameId;
    private Dictionary<int, int>? _interactiveToNameId;  // element_type_id -> nameId (nom de l'interactif : Frene, Zaap...)
    private Dictionary<int, int>? _skillToItem;     // skill_id -> gatheredRessourceItem (item recolte)
    private Dictionary<int, int>? _skillToNameId;   // skill_id -> nameId (verbe de l'action : Couper, Pecher, Utiliser, Entrer...)
    private Dictionary<int, int>? _skillToRange;    // skill_id -> range (portee d'interaction : 1=adjacent, peche=10)
    private Dictionary<int, int>? _itemToIcon;      // item_id -> iconId (icone dans item_assets)
    private Dictionary<int, int>? _itemToNameId;    // item_id -> nameId (nom de l'objet recolte : Goujon, Ortie...)
    private Dictionary<int, int>? _itemToType;         // item_id -> typeId (ItemType) : cle de CATEGORIE (onglet Inventaire/Banque)
    private Dictionary<int, int>? _typeToNameId;       // typeId -> nameId (nom du TYPE : Bois, Minerai, Epee, Poudre, Pain...) : c'est la
                                                       // categorie affichee. Verifie sur le datacenter : ItemSuperType/categoryId n'ont PAS
                                                       // de nom, seul ItemType.nameId resout -> on groupe par type (utile pour un farmer).
    private Dictionary<int, Dictionary<int, (int Ap, int MinRange, int Range, int Flags, bool Rangeable)>>? _spellCombat;  // spell_id -> (grade -> cout PA + portee + m_flags + rangeCanBeBoosted)
    private Dictionary<int, Dictionary<int, SpellDamageInfo>>? _spellDamage;  // spell_id -> (grade -> degats/element/limites de cast)
    private Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>>? _spellDescriptors;
    private Dictionary<int, Dictionary<int, (int Shape, int Size, int MinSize)>>? _spellZone;  // spell_id -> (grade -> forme AoE : shape ASCII Ankama + rayon + rayon min)
    private Dictionary<int, Dictionary<int, bool>>? _singleTargetSpells;
    private Dictionary<int, Dictionary<int, List<GivreClone.Engine.SpellModifierEffect>>>? _spellModifiers;   // spell_id -> grade -> effets qui MODIFIENT d'autres sorts (portée, PA…)
    private static bool _zoneFieldsDumped;   // dump UNIQUE des noms de champs d'un effet (diagnostic : caler le nom du champ de zone)
    private Dictionary<int, Dictionary<int, string>>? _spellCriterion;  // spell_id -> (grade -> statesCriterion : EXPRESSION de condition d'états pour lancer)
    private static bool _levelFieldsDumped;  // dump UNIQUE des noms de champs d'un NIVEAU de sort (diagnostic : le vrai champ = statesCriterion)
    private static int _criterionSamplesDumped;   // échantillons de statesCriterion loggés (pour connaître le format d'expression)
    private Dictionary<int, int>? _spellToNameId;   // spell_id -> nameId (nom du sort : Sournoiserie, Bond...) pour l'onglet Combat
    private Dictionary<int, int>? _stateToNameId;   // state_id -> nameId (nom d'état : Invisible, Enraciné...) pour statesCriterion + UI
    private Dictionary<int, int>? _spellToIcon;     // spell_id -> iconId (texture "sort_<iconId>" dans spell_assets)
    private Dictionary<int, HashSet<int>>? _breedSpells;  // breedId -> spellIds de la classe (spellvariants) : liste du picker Combat
    private Dictionary<int, int[]>? _variantPairs;         // spellId -> paire de variantes (spellvariants) : Lot E (combat:setSpellVariant)
    private Dictionary<int, int>? _serverToNameId;  // serverId -> nameId (nom du serveur)
    // Panneaux directionnels : element_id -> SignData. skillId 361 = via reference hint (Param -> hintsdataroot),
    // 360 = texte libre (TextKey -> I18n). Donne le VRAI texte du panneau ("Atelier des sculpteurs", "Fermé"...).
    private Dictionary<int, (int SkillId, int Param, int TextKey)>? _signByElement;
    private Dictionary<int, int>? _hintName;   // hint id -> nameId (Atelier des sculpteurs, Hotel de vente des creatures...)
    private Dictionary<int, int>? _poiName;    // point-of-interest id -> nameId (repli)
    // Lot I : données statiques (niveau/poids d'objet, emplacements d'équipement, métiers, sous-zones/zones, quêtes).
    private Dictionary<int, int>? _itemToLevel;       // item_id -> level
    private Dictionary<int, int>? _itemToWeight;      // item_id -> realWeight (poids UNITAIRE)
    private Dictionary<int, int>? _typeToSuper;       // typeId (ItemType) -> superTypeId
    private Dictionary<int, int[]>? _superPositions;  // superTypeId -> possiblePositions (vide = non équipable)
    private Dictionary<int, int>? _jobToNameId;
    private Dictionary<int, int>? _subAreaToNameId;
    private Dictionary<int, int>? _subAreaToArea;     // subAreaId -> areaId (0 = Amakna, VALIDE)
    private Dictionary<int, int>? _areaToNameId;      // areaId (commence à 0) -> nameId
    private Dictionary<int, int>? _questToNameId;
    private Dictionary<int, int>? _stepToNameId;
    private Dictionary<int, int>? _stepToDescId;        // questStepId -> descriptionId (quest:stepDescription)
    private Dictionary<int, int>? _challengeToNameId;   // challengeId -> nameId (quest:challengeName / fightChallenge:challengeName)
    private Dictionary<int, int>? _challengeToDescId;   // challengeId -> descriptionId (règle affichée dans « Challenges acceptés »)
    private Dictionary<int, int>? _challengeToIconId;   // challengeId -> iconId (Picto/UI/challenge_assets, texture nommée par iconId)
    private Dictionary<int, int>? _objTypeToNameId;     // questObjectiveTypeId -> nameId (quest:objectiveTypeName)
    private Dictionary<int, EffectDef>? _effectDefs;    // effectId -> texte, bonus/malus, visibilité (infobulles d'objets)
    private Dictionary<int, int>? _npcMessageToTextId;  // npcMessageId -> clé I18n du texte (npc:questionText)
    private Dictionary<long, int>? _infoMessageToTextId; // (typeId, messageId) -> clé I18n (TextInformationEvent)
    private Dictionary<int, long>? _nameIdToOffset;
    private byte[]? _bin;
    private readonly ConcurrentDictionary<int, string> _cacheMonster = new();
    private readonly ConcurrentDictionary<int, string> _cacheNpc = new();

    private static string? ContentDir()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var d = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content");
            if (Directory.Exists(d)) return d;
        }
        return null;
    }

    private readonly object _combatLock = new();
    private volatile bool _combatDone;
    private static readonly ConcurrentDictionary<string, Lazy<GameNameResolver>> s_combatCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<GameNameResolver>> s_fullCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tables utilisées par le moteur de combat (PA/portée/ligne de vue, dégâts, zones, critères d'états des sorts),
    /// chargées SEULES et en premier. Le tour n'attend plus le chargement complet (monstres, objets, i18n…) : 30 s seul, 1 à
    /// 3 min quand le client démarre en même temps — le 1er combat d'une session restait bloqué 43 s (log du 24/10 12:47).</summary>
    public void EnsureCombatLoaded()
    {
        if (_combatDone) return;
        lock (_combatLock)
        {
            if (_combatDone) return;
            try
            {
                var content = ContentDir();
                if (content is not null)
                {
                    var donor = s_combatCache.GetOrAdd(content, dir => new Lazy<GameNameResolver>(() =>
                    {
                        var g = new GameNameResolver();
                        g.LoadCombatCore(dir);
                        return g;
                    })).Value;
                    _spellCombat = donor._spellCombat;
                    _spellDamage = donor._spellDamage;
                    _spellDescriptors = donor._spellDescriptors;
                    _singleTargetSpells = donor._singleTargetSpells;
                    _spellModifiers = donor._spellModifiers;
                    _spellZone = donor._spellZone;
                    _spellCriterion = donor._spellCriterion;
                }
            }
            catch { /* best-effort */ }
            _combatDone = true;
        }
    }

    private void LoadCombatCore(string content)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var levels = Glob(Path.Combine(content, "Data"), "*spelllevelsdataroot*.asset.bundle");
        _spellCombat = ReadSpellLevels(levels);
        try
        {
            var zones = new Dictionary<int, Dictionary<int, (int Shape, int Size, int MinSize)>>();
            var criteria = new Dictionary<int, Dictionary<int, string>>();
            var mods = new Dictionary<int, Dictionary<int, List<GivreClone.Engine.SpellModifierEffect>>>();
            var singleTarget = new Dictionary<int, Dictionary<int, bool>>();
            var descriptors = new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>>();
            _spellDamage = ReadSpellDamage(levels, zones, criteria, mods, singleTarget, descriptors);
            _spellDescriptors = descriptors;
            _singleTargetSpells = singleTarget;
            _spellModifiers = mods;
            _spellZone = zones;
            _spellCriterion = criteria;
        }
        catch (System.Exception ex) { GivreLauncher.Core.Transport.TransportLog.Line($"[sort] lecture dégâts ÉCHEC : {ex.Message}"); }
        GivreLauncher.Core.Transport.TransportLog.Line($"[sort] données de dégâts chargées pour {_spellDamage?.Count ?? -1} sort(s), zones AoE pour {_spellZone?.Count ?? -1}, critères d'états (statesCriterion) pour {_spellCriterion?.Count ?? -1} sort(s) (-1 = échec/bundle absent) en {sw.ElapsedMilliseconds} ms.");
        _combatDone = true;
    }

    public void EnsureLoaded()
    {
        if (_initDone) return;
        lock (_lock)
        {
            if (_initDone) return;
            try
            {
                var content = ContentDir();
                if (content is not null)
                {
                    string cacheKey = content + "|" + (I18nPath(content) ?? "");
                    var donor = s_fullCache.GetOrAdd(cacheKey, _ => new Lazy<GameNameResolver>(() =>
                    {
                        var g = new GameNameResolver();
                        g.LoadFullCore(content);
                        return g;
                    })).Value;
                    CopyFullFrom(donor);
                    EnsureCombatLoaded();
                }
            }
            catch { /* best-effort */ }
            _initDone = true;
        }
    }

    private void CopyFullFrom(GameNameResolver d)
    {
        _monsterToNameId = d._monsterToNameId; _monsterGfx = d._monsterGfx;
        _npcToNameId = d._npcToNameId; _interactiveToNameId = d._interactiveToNameId;
        _skillToItem = d._skillToItem; _skillToNameId = d._skillToNameId; _skillToRange = d._skillToRange;
        _itemToIcon = d._itemToIcon; _itemToNameId = d._itemToNameId; _itemToType = d._itemToType; _typeToNameId = d._typeToNameId;
        _serverToNameId = d._serverToNameId; _spellToNameId = d._spellToNameId; _stateToNameId = d._stateToNameId;
        _spellToIcon = d._spellToIcon; _breedSpells = d._breedSpells; _variantPairs = d._variantPairs;
        _signByElement = d._signByElement; _hintName = d._hintName; _poiName = d._poiName;
        _itemToLevel = d._itemToLevel; _itemToWeight = d._itemToWeight; _typeToSuper = d._typeToSuper; _superPositions = d._superPositions;
        _jobToNameId = d._jobToNameId; _subAreaToNameId = d._subAreaToNameId; _subAreaToArea = d._subAreaToArea; _areaToNameId = d._areaToNameId;
        _questToNameId = d._questToNameId; _stepToNameId = d._stepToNameId; _stepToDescId = d._stepToDescId;
        _challengeToNameId = d._challengeToNameId; _challengeToDescId = d._challengeToDescId; _challengeToIconId = d._challengeToIconId;
        _objTypeToNameId = d._objTypeToNameId; _npcMessageToTextId = d._npcMessageToTextId; _infoMessageToTextId = d._infoMessageToTextId;
        _effectDefs = d._effectDefs; _bin = d._bin; _nameIdToOffset = d._nameIdToOffset;
    }

    private void LoadFullCore(string content)
    {
        var dataDir = Path.Combine(content, "Data");
        var lapWatch = System.Diagnostics.Stopwatch.StartNew(); long lapPrev = 0; var laps = new List<string>();
        void Lap(string what) { long now = lapWatch.ElapsedMilliseconds; laps.Add($"{what}={now - lapPrev}"); lapPrev = now; }
        _monsterToNameId = new Dictionary<int, int>();
        _monsterGfx = new Dictionary<int, int>();
        ReadMonsters(Glob(dataDir, "*monstersdataroot*.asset.bundle"), _monsterToNameId, _monsterGfx);
        Lap("monstres");
        _npcToNameId = ReadIdNameId(Glob(dataDir, "*npcsdataroot*.asset.bundle"));
        _interactiveToNameId = ReadIdNameId(Glob(dataDir, "*interactivesdataroot*.asset.bundle"));
        var skillsBundle = Glob(dataDir, "*skillsdataroot*.asset.bundle");
        _skillToItem = ReadIdInt(skillsBundle, "gatheredRessourceItem");
        _skillToNameId = ReadIdInt(skillsBundle, "nameId");   // verbe (Couper/Pecher/Utiliser...) pour nommer portes/escaliers (type -1)
        _skillToRange = ReadIdInt(skillsBundle, "range");     // portee : recolte=1 (adjacent), peche=10 (a distance)
        Lap("pnj+interactifs+skills");
        var itemsBundle = Glob(dataDir, "*itemsdataroot*.asset.bundle");
        // Bundle des objets (~21 500 entrées) lu UNE fois pour tous ses champs : il était relu 5 fois (31 s
        // pendant le démarrage du client, mesuré le 24/10).
        var itemFields = ReadIdInts(itemsBundle, "iconId", "nameId", "typeId", "level", "realWeight");
        _itemToIcon = itemFields?["iconId"];
        _itemToNameId = itemFields?["nameId"];   // nom de l'objet recolte (Goujon, Ortie...)
        // CATEGORIE d'objet (onglet Inventaire/Banque) : item -> typeId (ItemType) -> nom du type. Glob PRECIS
        // "*_itemtypesdataroot" pour NE PAS matcher "evolutiveitemtypesdataroot" (piege signale par l'audit).
        _itemToType = itemFields?["typeId"];
        _typeToNameId = ReadIdNameId(Glob(dataDir, "*_itemtypesdataroot.asset.bundle"));
        Lap("objets");
        GivreLauncher.Core.Transport.TransportLog.Line($"[items] categories : {_itemToType?.Count ?? -1} items types, {_typeToNameId?.Count ?? -1} types nommes (-1 = bundle absent/champ inconnu).");
        _serverToNameId = ReadIdNameId(Glob(dataDir, "*serversdataroot*.asset.bundle"));
        _spellToNameId = ReadIdNameId(Glob(dataDir, "*_spellsdataroot.asset.bundle"));   // nom des sorts (onglet Combat)
        _stateToNameId = ReadStateNames(Glob(dataDir, "*spellstatesdataroot*.asset.bundle"));   // état_id -> nom (statesCriterion)
        GivreLauncher.Core.Transport.TransportLog.Line($"[état] noms d'états chargés (spellstatesdataroot) : {_stateToNameId?.Count ?? -1} (-1 = bundle absent ; 0 = champs id/nameId non reconnus, voir dump).");
        _spellToIcon = ReadIdInt(Glob(dataDir, "*_spellsdataroot.asset.bundle"), "iconId");   // icone des sorts
        _breedSpells = ReadSpellVariants(Glob(dataDir, "*spellvariantsdataroot.asset.bundle"), out _variantPairs);  // sorts par classe (picker)
        _signByElement = ReadSigns(Glob(dataDir, "*signsdataroot*.asset.bundle"));
        _hintName = ReadIdNameId(Glob(dataDir, "*hintsdataroot*.asset.bundle"));
        _poiName = ReadIdNameId(Glob(dataDir, "*pointsofinterestdataroot*.asset.bundle"));
        Lap("sorts/etats/variantes/panneaux");
        // Lot I : chaque lecture est isolée (un bundle renommé/absent n'empêche pas les autres ni l'I18n).
        _itemToLevel = itemFields?["level"]; _itemToWeight = itemFields?["realWeight"];
        try { _typeToSuper = ReadIdInt(Glob(dataDir, "*_itemtypesdataroot.asset.bundle"), "superTypeId"); } catch { }
        try { _superPositions = ReadSuperTypePositions(Glob(dataDir, "*itemsupertypesdataroot*.asset.bundle")); } catch { }
        try { _jobToNameId = ReadIdNameId(Glob(dataDir, "*_jobsdataroot*.asset.bundle")); } catch { }
        try { var sub = Glob(dataDir, "*_subareasdataroot*.asset.bundle"); _subAreaToNameId = ReadIdNameId(sub); _subAreaToArea = ReadIdInt(sub, "areaId"); } catch { }
        // "*_areasdataroot*" : le caractère avant "areas" doit être '_' -> ne matche NI subareas NI superareas.
        try { _areaToNameId = ReadIdNameId(Glob(dataDir, "*_areasdataroot*.asset.bundle")); } catch { }
        try { _questToNameId = ReadIdNameId(Glob(dataDir, "*_questsdataroot*.asset.bundle")); } catch { }
        try { _stepToNameId = ReadIdNameId(Glob(dataDir, "*_queststepsdataroot*.asset.bundle")); } catch { }
        // Compat un bot tiers (quest:/npc:/fightChallenge:/treasure:): chaque lecture isolée, absente = texte vide.
        try { _stepToDescId = ReadIdInt(Glob(dataDir, "*_queststepsdataroot*.asset.bundle"), "descriptionId"); } catch { }
        try { _challengeToNameId = ReadIdNameId(Glob(dataDir, "*_challengesdataroot*.asset.bundle") ?? Glob(dataDir, "*challengesdataroot*.asset.bundle")); } catch { }
        try { _challengeToDescId = ReadIdInt(Glob(dataDir, "*_challengesdataroot*.asset.bundle") ?? Glob(dataDir, "*challengesdataroot*.asset.bundle"), "descriptionId"); } catch { }
        try { _challengeToIconId = ReadIdInt(Glob(dataDir, "*_challengesdataroot*.asset.bundle") ?? Glob(dataDir, "*challengesdataroot*.asset.bundle"), "iconId"); } catch { }
        try { _objTypeToNameId = ReadIdNameId(Glob(dataDir, "*questobjectivetypesdataroot*.asset.bundle")); } catch { }
        try { _npcMessageToTextId = ReadIdInt(Glob(dataDir, "*npcmessagesdataroot*.asset.bundle"), "messageId"); } catch { }
        try { _infoMessageToTextId = ReadInfoMessages(Glob(dataDir, "*infomessagesdataroot*.asset.bundle")); } catch { }
        try { _effectDefs = ReadEffectDefs(EffectsBundle(dataDir)); } catch { }
        Lap("divers");
        GivreLauncher.Core.Transport.TransportLog.Line($"[données] niveaux d'objets {_itemToLevel?.Count ?? -1}, emplacements {_superPositions?.Count ?? -1}, sous-zones {_subAreaToNameId?.Count ?? -1}, zones {_areaToNameId?.Count ?? -1}, quêtes {_questToNameId?.Count ?? -1} (-1 = absent).");
        LoadI18n(content);
        Lap("i18n");
        GivreLauncher.Core.Transport.TransportLog.Line($"[données] chargement {lapWatch.ElapsedMilliseconds} ms : {string.Join(" ", laps)}");
        _initDone = true;
    }

    private static string? Glob(string dir, string pattern) => Directory.EnumerateFiles(dir, pattern).FirstOrDefault();

    private static Dictionary<int, int>? ReadIdNameId(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            foreach (var r in reg.references)
                try { map[r.data["id"].AsInt] = r.data["nameId"].AsInt; } catch { }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // États (spellstatesdataroot) : state_id -> nameId, avec un dump 1-shot des champs (pour valider id/nameId au 1er lancement).
    private static Dictionary<int, int>? ReadStateNames(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        bool dumped = false;
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d?.Children is null) continue;
                if (!dumped) { dumped = true; GivreLauncher.Core.Transport.TransportLog.Line($"[état] diag champs spellstatesdataroot : {string.Join(",", d.Children.Select(c => c.FieldName))}"); }
                int id = 0, nameId = 0;
                foreach (var c in d.Children)
                    try { switch (c.FieldName) { case "id": id = c.AsInt; break; case "nameId": nameId = c.AsInt; break; } } catch { }
                if (id != 0 && nameId != 0) map[id] = nameId;
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Panneaux (signsdataroot) : SignData.id (= element_id) -> (skillId, skillParams entier, textKey). La liste est
    // dans un champ imbrique "signs".Array (SignsData). skillParams est une chaine -> parsee en int (id de hint).
    private static Dictionary<int, (int, int, int)>? ReadSigns(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, (int, int, int)>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data;
                var arr = d?.Children.FirstOrDefault(c => c.FieldName == "signs")?
                           .Children.FirstOrDefault(c => c.FieldName == "Array");
                if (arr is null) continue;
                foreach (var e in arr.Children)
                {
                    int id = 0, sk = 0, tk = 0, param = -1;
                    foreach (var c in e.Children)
                        try
                        {
                            switch (c.FieldName)
                            {
                                case "id": id = c.AsInt; break;
                                case "skillId": sk = c.AsInt; break;
                                case "textKey": tk = c.AsInt; break;
                                case "skillParams": int.TryParse(c.AsString, out param); break;
                            }
                        }
                        catch { }
                    if (id != 0) map[id] = (sk, param, tk);
                }
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Sorts par CLASSE (spellvariantsdataroot) : chaque variante = { breedId, spellIds[] (paire) }. On UNIONNE tous les
    // spellIds par breedId -> le repertoire de sorts de la classe (sert a peupler le picker de l'onglet Combat).
    // Lot E : renvoie AUSSI les paires (chaque spellId de la variante -> le tableau entier) pour activer une variante.
    private static Dictionary<int, HashSet<int>>? ReadSpellVariants(string? bundle, out Dictionary<int, int[]>? pairs)
    {
        pairs = null;
        if (bundle is null) return null;
        var map = new Dictionary<int, HashSet<int>>();
        var pairMap = new Dictionary<int, int[]>();
        pairs = pairMap;
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
                try
                {
                    var d = r.data;
                    int breed = d.Children.First(c => c.FieldName == "breedId").AsInt;
                    var spv = d.Children.First(c => c.FieldName == "spellIds");
                    var arr = spv.Children.FirstOrDefault(c => c.FieldName == "Array") ?? spv;
                    if (!map.TryGetValue(breed, out var set)) { set = new HashSet<int>(); map[breed] = set; }
                    var ids = new List<int>();
                    foreach (var c in arr.Children) { try { int id = c.AsInt; if (id > 0) { set.Add(id); ids.Add(id); } } catch { } }
                    if (ids.Count > 1) { var arrIds = ids.ToArray(); foreach (var id in arrIds) pairMap[id] = arrIds; }
                }
                catch { }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    /// <summary>Paire de variantes contenant `spellId` (lui compris), ou liste vide si inconnu / données non chargées.</summary>
    public IReadOnlyList<int> SpellVariantPair(int spellId)
        => _initDone && _variantPairs is { } vp && vp.TryGetValue(spellId, out var arr) ? arr : System.Array.Empty<int>();

    /// <summary>Sorts de la classe `breedId` (spell_id + nom), triés par nom, pour le picker de l'onglet Combat. Seuls
    /// les sorts avec un nom résolu sont renvoyés. Liste vide si la classe est inconnue ou les données non chargées.</summary>
    public IReadOnlyList<(int Id, string Name)> SpellsForBreed(int breedId)
    {
        if (!_initDone || _breedSpells is null || !_breedSpells.TryGetValue(breedId, out var ids)) return System.Array.Empty<(int, string)>();
        var list = new List<(int Id, string Name)>();
        foreach (var id in ids) if (SpellName(id) is { } n) list.Add((id, n));
        list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        return list;
    }

    // Sorts (spelllevelsdataroot) : spell_id -> (apCost, minRange, range). Un sort a plusieurs grades (niveaux) ; on
    // Stocke le cout PA / portee / m_flags PAR GRADE (plus d'agregation MAX : sinon on prend la portee d'un grade
    // superieur -> le bot tire hors de portee, ex "portee 1-6 mais vise a 7"). Sert au ciblage de l'auto-combat.
    private static Dictionary<int, Dictionary<int, (int Ap, int Min, int Range, int Flags, bool Rangeable)>>? ReadSpellLevels(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, Dictionary<int, (int Ap, int Min, int Range, int Flags, bool Rangeable)>>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d is null) continue;
                int sid = 0, grade = 1, ap = 0, min = 0, rng = 0, flags = 0;
                bool rangeable = false;
                foreach (var c in d.Children)
                    try
                    {
                        switch (c.FieldName)
                        {
                            case "spellId": sid = c.AsInt; break;
                            case "grade": grade = c.AsInt; break;
                            case "apCost": ap = c.AsInt; break;
                            case "minRange": min = c.AsInt; break;
                            case "range": rng = c.AsInt; break;
                            case "m_flags": flags = c.AsInt; break;
                            case "rangeCanBeBoosted": rangeable = c.AsBool; break;   // PO boostable par la caractéristique Portée
                        }
                    }
                    catch { }
                if (sid == 0) continue;
                if ((flags & 64) != 0) rangeable = true;
                if (!map.TryGetValue(sid, out var byGrade)) map[sid] = byGrade = new Dictionary<int, (int, int, int, int, bool)>();
                byGrade[grade] = (ap, min, rng, flags, rangeable);
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    /// <summary>Cout PA + portee (min/max) + contraintes d'un sort au GRADE demandé (sinon grade &lt;= demandé le plus
    /// haut, sinon le plus haut dispo), ou null. castInLine = m_flags bit0 ; castInDiagonal = bit1 ; castTestLos = bit2.
    /// Les 3 bits sont des CONTRAINTES géométriques posées par-dessus la portée diamant (CanCast).</summary>
    public (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable)? SpellCombat(int spellId, int grade)
    {
        if (_spellCombat is null || !_spellCombat.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0) return null;
        if (!byGrade.TryGetValue(grade, out var v))
        {
            // grade exact absent : prendre le plus haut grade <= demandé qui EXISTE ; si aucun (grade < min connu),
            // prendre le grade MIN — JAMAIS le max (sinon on lit la portée d'un grade supérieur -> tir hors portée,
            // ex serveur "portée 0 à 0 / 1 à 2 mais vous visez à 4"). C'est le repli qui gonflait effRange.
            var le = byGrade.Keys.Where(k => k <= grade).ToList();
            int g = le.Count > 0 ? le.Max() : byGrade.Keys.Min();
            v = byGrade[g];
        }
        return (v.Ap, v.MinRange, v.Range, (v.Flags & 1) != 0, (v.Flags & 2) != 0, (v.Flags & 4) != 0, v.Rangeable);
    }

    /// <summary>Contraintes de lancer packées dans m_flags:
    /// castInDiagonal = bit1 (&amp;2), needFreeCell = bit3 (&amp;8), needTakenCell = bit4 (&amp;16). null si donnée absente.
    /// Résolution du grade identique à SpellCombat. Aucun numéro/bit deviné: layout vérifié sur le parseur D2O un bot tiers.</summary>
    public (bool CastInDiagonal, bool NeedFreeCell, bool NeedTakenCell)? SpellCastConstraints(int spellId, int grade)
    {
        if (_spellCombat is null || !_spellCombat.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0) return null;
        if (!byGrade.TryGetValue(grade, out var v))
        {
            var le = byGrade.Keys.Where(k => k <= grade).ToList();
            v = byGrade[le.Count > 0 ? le.Max() : byGrade.Keys.Min()];
        }
        return ((v.Flags & 2) != 0, (v.Flags & 8) != 0, (v.Flags & 16) != 0);
    }

    /// <summary>Dégâts + élément + limites de cast d'un sort, au GRADE demandé (sinon grade &lt;= demandé le plus haut,
    /// sinon le plus haut disponible). null si pas de dégâts élémentaires (buff/utilitaire). Voir <see cref="ElementName"/>.</summary>
    public SpellDamageInfo? GetSpellDamage(int spellId, int grade)
    {
        if (_spellDamage is null || !_spellDamage.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0) return null;
        if (byGrade.TryGetValue(grade, out var exact)) return exact;
        int g = byGrade.Keys.Where(k => k <= grade).DefaultIfEmpty(byGrade.Keys.Max()).Max();
        return byGrade[g];
    }

    public Combat.SpellCastDescriptor? GetSpellDescriptor(int spellId, int grade)
    {
        return _spellDescriptors is not null
            && _spellDescriptors.TryGetValue(spellId, out var grades)
            && grades.TryGetValue(grade, out var exact) ? exact : null;
    }

    /// <summary>Forme d'AoE (shape ASCII Ankama + rayon + rayon min) du sort au GRADE demandé (repli grade &lt;= demandé),
    /// ou null si mono-cible / zone inconnue. Alimente <see cref="GivreClone.Protocol.CombatZones"/> (ciblage AoE réel +
    /// test « allié dans la zone »). null -&gt; l'appelant retombe sur son approximation (aucune régression).</summary>
    public (int Shape, int Size, int MinSize)? SpellZone(int spellId, int grade)
    {
        if (_spellZone is null || !_spellZone.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0) return null;
        if (byGrade.TryGetValue(grade, out var exact)) return exact;
        int g = byGrade.Keys.Where(k => k <= grade).DefaultIfEmpty(byGrade.Keys.Max()).Max();
        return byGrade[g];
    }

    /// <summary>Effets de modification d'autres sorts portés par <paramref name="spellId"/> au grade demandé (repli grade &lt;= demandé) :
    /// portée min/max, coût en PA, lancers par tour/cible… Vide si le sort n'en porte pas (la plupart) ou si les données manquent.</summary>
    public IReadOnlyList<GivreClone.Engine.SpellModifierEffect> SpellModifierEffects(int spellId, int grade)
    {
        if (_spellModifiers is null || !_spellModifiers.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0)
            return System.Array.Empty<GivreClone.Engine.SpellModifierEffect>();
        if (byGrade.TryGetValue(grade, out var exact)) return exact;
        int g = byGrade.Keys.Where(k => k <= grade).DefaultIfEmpty(byGrade.Keys.Max()).Max();
        return byGrade[g];
    }

    /// <summary>Expression `statesCriterion` du sort (au grade donné) : la CONDITION d'états à satisfaire pour lancer (Dofus
    /// encode requis ET interdits dans UNE expression, pas deux listes). Chaîne vide/null = aucune condition. L'évaluation
    /// (parseur de critère + états actifs du combattant) reste à câbler ; pour l'instant l'accès est fourni pour diagnostic.</summary>
    public string? SpellStatesCriterion(int spellId, int grade)
    {
        if (_spellCriterion is null || !_spellCriterion.TryGetValue(spellId, out var byGrade) || byGrade.Count == 0) return null;
        if (byGrade.TryGetValue(grade, out var exact)) return exact;
        int g = byGrade.Keys.Where(k => k <= grade).DefaultIfEmpty(byGrade.Keys.Max()).Max();
        return byGrade[g];
    }

    // ElementId et effets du bundle installé vérifiés le 03/11/2026 (actions 96..100).
    public static string ElementName(int e) => e switch
    { 1 => "terre", 2 => "feu", 3 => "eau", 4 => "air", 0 => "neutre", _ => "?" };

    // Sorts (spelllevelsdataroot) : lit les EFFETS de chaque grade -> dégâts (somme des dés élémentaires), élément
    // dominant, et limites de cast RÉELLES (maxCastPerTurn/Target). EffectInstanceDice : effectElement (>=0 = élément
    // -> dégât ; -1 = non élémentaire), diceNum=min, diceSide=max (0 => valeur fixe = diceNum).
    /// <summary>Lit un champ `statesCriterion` du bundle : chaîne d'expression si possible, sinon dump de sa structure (pour
    /// diagnostic). Vide si champ absent/vide.</summary>
    private static string ReadCriterion(AssetsTools.NET.AssetTypeValueField field)
    {
        try
        {
            var s = field.AsString;
            if (!string.IsNullOrEmpty(s)) return s;
        }
        catch { }
        try   // pas une chaîne simple -> sérialise les champs internes pour comprendre le format
        {
            if (field.Children is { Count: > 0 } kids)
                return "{" + string.Join(",", kids.Select(c => c.FieldName)) + "}";
        }
        catch { }
        return "";
    }

    /// <summary>Strict safety proof for treasure guards: every normal AND critical effect is explicitly a point.
    /// The ordinary SpellZone accessor chooses one representative zone and is not sufficient for this check.</summary>
    public bool SpellIsSingleTarget(int spellId, int grade) => _singleTargetSpells is not null
        && _singleTargetSpells.TryGetValue(spellId, out var grades) && grades.GetValueOrDefault(grade);

    private static List<int> EffectShapes(AssetsTools.NET.AssetTypeValueField effects)
    {
        var result = new List<int>();
        var array = effects.Children?.FirstOrDefault(x => x.FieldName == "Array");
        foreach (var effect in array?.Children ?? Enumerable.Empty<AssetsTools.NET.AssetTypeValueField>())
        {
            int shape = 0;
            try
            {
                foreach (var field in effect.Children)
                    switch (field.FieldName)
                    {
                        case "zoneShape": case "zoneShapeType": case "shape": shape = field.AsInt; break;
                        case "zoneDescr":
                            shape = field.Children.FirstOrDefault(x => x.FieldName == "shape")?.AsInt ?? 0; break;
                    }
            }
            catch { shape = 0; }
            result.Add(shape);   // Unknown effects are unsafe, not silently dropped.
        }
        return result;
    }

    private static Dictionary<int, Dictionary<int, SpellDamageInfo>>? ReadSpellDamage(string? bundle, Dictionary<int, Dictionary<int, (int Shape, int Size, int MinSize)>> zoneOut, Dictionary<int, Dictionary<int, string>> criterionOut, Dictionary<int, Dictionary<int, List<GivreClone.Engine.SpellModifierEffect>>> modsOut, Dictionary<int, Dictionary<int, bool>> singleTargetOut, Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>> descriptorOut)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, Dictionary<int, SpellDamageInfo>>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d?.Children is null) continue;
                // DUMP UNIQUE des noms de champs d'un NIVEAU de sort : a révélé que le vrai champ de condition d'états est
                // `statesCriterion` (une EXPRESSION unique, pas deux listes statesRequired/Forbidden).
                if (!_levelFieldsDumped && d.Children is { Count: > 0 })
                {
                    _levelFieldsDumped = true;
                    GivreLauncher.Core.Transport.TransportLog.Line($"[sort] diag champs de NIVEAU : {string.Join(",", d.Children.Select(c => c.FieldName))}");
                }
                int sid = 0, grade = 1, mpt = 0, mptg = 0;
                int apCost = 0, minRange = 0, range = 0, flags = 0, interval = 0, initial = 0;
                bool rangeable = false, uncertain = false;
                AssetsTools.NET.AssetTypeValueField effects = null;
                string criterion = "";
                foreach (var c in d.Children)
                    try
                    {
                        switch (c.FieldName)
                        {
                            case "spellId": sid = c.AsInt; break;
                            case "grade": grade = c.AsInt; break;
                            case "maxCastPerTurn": mpt = c.AsInt; break;
                            case "maxCastPerTarget": mptg = c.AsInt; break;
                            case "apCost": apCost = c.AsInt; break;
                            case "minRange": minRange = c.AsInt; break;
                            case "range": range = c.AsInt; break;
                            case "m_flags": flags = c.AsInt; break;
                            case "rangeCanBeBoosted": rangeable = c.AsBool; break;
                            case "minCastInterval": interval = c.AsInt; break;
                            case "initialCooldown": initial = c.AsInt; break;
                            case "globalCooldown": uncertain |= c.AsInt != 0; break;
                            case "effects": effects = c; break;
                            // Condition d'états pour lancer : EXPRESSION unique (requis+interdits combinés). Nom confirmé par le dump.
                            case "statesCriterion": criterion = ReadCriterion(c); break;
                        }
                    }
                    catch { uncertain = true; }
                if (sid == 0) continue;
                if ((flags & 64) != 0) rangeable = true;
                // Missing critical-effect data is uncertainty, not proof that a spell is single-target.
                var criticalEffects = d.Children.FirstOrDefault(c => c.FieldName == "criticalEffect");
                if (!singleTargetOut.TryGetValue(sid, out var singleGrades)) singleTargetOut[sid] = singleGrades = new();
                singleGrades[grade] = effects is not null && criticalEffects is not null && GivreClone.Engine.TreasureFightPolicy.SingleTargetEffects(
                    EffectShapes(effects), EffectShapes(criticalEffects));
                if (!string.IsNullOrEmpty(criterion))
                {
                    if (!criterionOut.TryGetValue(sid, out var crByGrade)) criterionOut[sid] = crByGrade = new Dictionary<int, string>();
                    crByGrade[grade] = criterion;
                    if (_criterionSamplesDumped < 8)   // échantillons pour connaître le FORMAT de l'expression de critère
                    {
                        _criterionSamplesDumped++;
                        GivreLauncher.Core.Transport.TransportLog.Line($"[sort] statesCriterion échantillon (sort {sid} grade {grade}) : {criterion}");
                    }
                }

                int min = 0, max = 0, elem = -1, best = 0;
                // Forme AoE : zone des DÉGÂTS d'abord (un effet « lanceur » en point ne masque plus le carré de Déchaînement), cf SpellZoneSelection.
                var zoneCands = new List<GivreClone.App.Services.CombatParity.SpellZoneCandidate>();
                var retained = System.Collections.Immutable.ImmutableArray.CreateBuilder<Combat.SpellEffectDescriptor>();
                var arr = effects?.Children?.FirstOrDefault(x => x.FieldName == "Array");
                foreach (var e in arr?.Children ?? Enumerable.Empty<AssetsTools.NET.AssetTypeValueField>())
                {
                    try
                    {
                        // EffectInstanceData.forClientOnly reads bit 4 of m_flags.
                        // These rows describe client previews, not server effects.
                        if (e.Children?.FirstOrDefault(c => c.FieldName == "m_flags") is { } effectFlags
                            && (effectFlags.AsInt & 16) != 0) continue;
                        if (!_zoneFieldsDumped && e.Children is { Count: > 0 })
                        {
                            _zoneFieldsDumped = true;
                            GivreLauncher.Core.Transport.TransportLog.Line($"[sort] diag champs d'effet (pour caler la zone AoE) : {string.Join(",", e.Children.Select(c => c.FieldName))}");
                        }
                        int eElem = -1, dNum = 0, dSide = 0, eShape = 0, eSize = 0, eMinR = 0, eAction = 0, eValue = 0, eDuration = 0;
                        string? eMask = null;
                        int eDelay = 0, eRandom = 0, eTriggerDuration = 0;
                        string eTriggers = "", eCondition = "";
                        bool conditional = false;
                        foreach (var ec in e.Children)
                            switch (ec.FieldName)
                            {
                                case "actionId": try { eAction = ec.AsInt; } catch { } break;
                                case "value": try { eValue = ec.AsInt; } catch { } break;
                                case "duration": try { eDuration = ec.AsInt; } catch { } break;
                                case "effectTriggerDuration": eTriggerDuration = ec.AsInt; break;
                                case "delay": eDelay = ec.AsInt; conditional |= eDelay != 0; break;
                                case "random": case "randomWeight": eRandom = ec.AsInt; conditional |= eRandom != 0; break;
                                case "triggers":
                                    var trigger = eTriggers = ReadCriterion(ec).Trim();
                                    conditional |= trigger.Length > 0 && trigger != "I";
                                    break;
                                case "effectCondition": eCondition = ReadCriterion(ec); conditional |= !string.IsNullOrWhiteSpace(eCondition); break;
                                case "effectElement": eElem = ec.AsInt; break;
                                case "diceNum": dNum = ec.AsInt; break;
                                case "diceSide": dSide = ec.AsInt; break;
                                case "targetMask": try { eMask = ec.AsString; } catch { } break;
                                // Zone AoE : format Unity direct (zoneShape/zoneSize/zoneMinSize) OU ancien d2o imbriqué
                                // (zoneDescr.shape/param1/param2). Plusieurs orthographes tolérées (fail-safe : zone absente
                                // -> SpellZone null -> repli sur l'approximation existante, pas de régression).
                                case "zoneShape": case "zoneShapeType": case "shape": try { eShape = ec.AsInt; } catch { } break;
                                case "zoneSize": case "zoneRadius": try { eSize = ec.AsInt; } catch { } break;
                                case "zoneMinSize": case "zoneMinRadius": try { eMinR = ec.AsInt; } catch { } break;
                                case "zoneDescr":
                                    foreach (var zc in ec.Children ?? Enumerable.Empty<AssetsTools.NET.AssetTypeValueField>())
                                        try { switch (zc.FieldName) { case "shape": eShape = zc.AsInt; break; case "param1": eSize = zc.AsInt; break; case "param2": eMinR = zc.AsInt; break; } } catch { }
                                    break;
                            }
                        var kind = Combat.SpellEffectDescriptor.Classify(eAction);
                        retained.Add(new(eAction, kind, dNum != 0 ? dNum : eValue,
                            dSide > dNum ? dSide : dNum != 0 ? dNum : eValue, eElem, eDuration,
                            eMask ?? "", eShape, eSize, eMinR, conditional,
                            dNum, dSide, eValue, eDelay, eRandom, eTriggers, eCondition, eTriggerDuration));
                        if (eShape != 0) zoneCands.Add(new(eShape, eSize, eMinR, eElem >= 0, eMask));
                        // Modification d'AUTRES sorts (portée, PA, lancers…) : diceNum = sort modifié, value = quantité. Cf SpellModifierActions.
                        if (GivreClone.Engine.SpellModifierActions.IsModifier(eAction) && dNum > 0)
                        {
                            if (!modsOut.TryGetValue(sid, out var mByGrade)) modsOut[sid] = mByGrade = new Dictionary<int, List<GivreClone.Engine.SpellModifierEffect>>();
                            if (!mByGrade.TryGetValue(grade, out var mList)) mByGrade[grade] = mList = new List<GivreClone.Engine.SpellModifierEffect>();
                            mList.Add(new GivreClone.Engine.SpellModifierEffect(eAction, dNum, eValue, eDuration));
                        }
                        if (eElem < 0 || kind != Combat.SpellEffectKind.Damage) continue;
                        int eMin = dNum, eMax = dSide > dNum ? dSide : dNum;
                        min += eMin; max += eMax;
                        int avg = (eMin + eMax) / 2;
                        if (avg > best) { best = avg; elem = eElem; }
                    }
                    catch { uncertain = true; }
                }
                if (!descriptorOut.TryGetValue(sid, out var descriptorGrades)) descriptorOut[sid] = descriptorGrades = new();
                descriptorGrades[grade] = new(sid, grade, apCost, minRange, range, flags, rangeable,
                    mpt, mptg, interval, initial, criterion, retained.ToImmutable(), uncertain);
                if (GivreClone.App.Services.CombatParity.SpellZoneSelection.Pick(zoneCands) is { } zPick)   // stockée MÊME pour un sort utilitaire (buff/soin AoE)
                {
                    if (!zoneOut.TryGetValue(sid, out var zByGrade)) zoneOut[sid] = zByGrade = new Dictionary<int, (int, int, int)>();
                    zByGrade[grade] = zPick;
                }
                if (max <= 0) continue;   // aucun dégât élémentaire -> sort utilitaire
                if (!map.TryGetValue(sid, out var byGrade)) map[sid] = byGrade = new Dictionary<int, SpellDamageInfo>();
                byGrade[grade] = new SpellDamageInfo(min, max, elem, mpt, mptg);
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Generique : id -> <field int> depuis un data-root (references managees). Reutilise pour skill/item/job.
    private static Dictionary<int, int>? ReadIdInt(string? bundle, string field)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            foreach (var r in reg.references)
            {
                var d = r.data;
                if (d is null) continue;
                AssetTypeValueField? idF = null, vF = null;   // recherche par nom (pas d'exception si le champ manque)
                foreach (var c in d.Children)
                {
                    if (c.FieldName == "id") idF = c;
                    else if (c.FieldName == field) vF = c;
                }
                if (idF is not null && vF is not null) try { map[idF.AsInt] = vF.AsInt; } catch { }
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Bundle des EFFETS : glob précis, le nom doit être « effectsdataroot » à lui seul (pas « spelleffects... » ni autre
    // table qui finirait par le même mot).
    private static string? EffectsBundle(string dir)
        => Directory.EnumerateFiles(dir, "*effectsdataroot*.asset.bundle").FirstOrDefault(f =>
           {
               var n = Path.GetFileName(f).ToLowerInvariant();
               return n.StartsWith("effectsdataroot", StringComparison.Ordinal) || n.Contains("_effectsdataroot", StringComparison.Ordinal);
           });

    /// <summary>Définition d'un effet : clé du texte (descriptionId), bonusType (&lt; 0 = malus, affiché en rouge), ordre
    /// d'affichage (effectPriority) et visibilité dans l'infobulle (showInTooltip).</summary>
    public readonly record struct EffectDef(int DescriptionId, int BonusType, int Priority, bool ShowInTooltip, bool ShowInSet = false)
    {
        // Les bonus de caractéristiques (118, 125…) ont showInTooltip=false et showInSet=true.
        public bool ShowOnItem => ShowInTooltip || ShowInSet;
    }

    private static Dictionary<int, EffectDef>? ReadEffectDefs(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, EffectDef>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d?.Children is null) continue;
                int id = -1, desc = 0, bonus = 0, prio = 0; bool show = true, showSet = false;
                foreach (var c in d.Children)
                    try
                    {
                        switch (c.FieldName)
                        {
                            case "id": id = c.AsInt; break;
                            case "descriptionId": desc = c.AsInt; break;
                            case "bonusType": bonus = c.AsInt; break;
                            case "effectPriority": prio = c.AsInt; break;
                            case "showInTooltip": show = c.AsBool; break;
                            case "showInSet": showSet = c.AsBool; break;
                        }
                    }
                    catch { }
                if (id > 0) map[id] = new EffectDef(desc, bonus, prio, show, showSet);
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Messages d'information (infomessagesdataroot) : pas d'« id », la clé est le couple (typeId, messageId) -> textId.
    private static Dictionary<long, int>? ReadInfoMessages(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<long, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d?.Children is null) continue;
                int type = -1, msg = -1, text = 0;
                foreach (var c in d.Children)
                    try { switch (c.FieldName) { case "typeId": type = c.AsInt; break; case "messageId": msg = c.AsInt; break; case "textId": text = c.AsInt; break; } } catch { }
                if (type >= 0 && msg >= 0 && text > 0) map[InfoKey(type, msg)] = text;
            }
            break;
        }
        mgr.UnloadAll();
        return map;
    }
    private static long InfoKey(int type, int msg) => ((long)type << 32) | (uint)msg;

    // Lot I : comme ReadIdInt mais remplit PLUSIEURS champs en UNE passe (le bundle d'objets compte ~66k références et
    // est déjà lu plusieurs fois). Chaque table = id -> valeur (champ absent sur une référence = pas d'entrée).
    private static Dictionary<string, Dictionary<int, int>>? ReadIdInts(string? bundle, params string[] fields)
    {
        if (bundle is null) return null;
        var res = new Dictionary<string, Dictionary<int, int>>();
        foreach (var f in fields) res[f] = new Dictionary<int, int>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
            {
                var d = r.data; if (d?.Children is null) continue;
                AssetTypeValueField? idF = null;
                foreach (var c in d.Children) if (c.FieldName == "id") { idF = c; break; }
                if (idF is null) continue;
                int id; try { id = idF.AsInt; } catch { continue; }
                foreach (var c in d.Children)
                    if (res.TryGetValue(c.FieldName, out var map)) try { map[id] = c.AsInt; } catch { }
            }
            break;
        }
        mgr.UnloadAll();
        return res;
    }

    // Lot I : superTypeId -> possiblePositions (tableau d'entiers ; vide = type non équipable, ignoré).
    private static Dictionary<int, int[]>? ReadSuperTypePositions(string? bundle)
    {
        if (bundle is null) return null;
        var map = new Dictionary<int, int[]>();
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
            if (reg is null) continue;
            foreach (var r in reg.references)
                try
                {
                    var d = r.data; if (d?.Children is null) continue;
                    int id = d.Children.First(c => c.FieldName == "id").AsInt;
                    var pp = d.Children.FirstOrDefault(c => c.FieldName == "possiblePositions");
                    var arr = pp?.Children.FirstOrDefault(c => c.FieldName == "Array") ?? pp;
                    if (arr?.Children is null) continue;
                    var list = new List<int>();
                    foreach (var c in arr.Children) { try { list.Add(c.AsInt); } catch { } }
                    if (list.Count > 0) map[id] = list.ToArray();
                }
                catch { }
            break;
        }
        mgr.UnloadAll();
        return map;
    }

    // Monstres : id -> nameId ET id -> gfxId (pour l'icone) en une passe.
    private static void ReadMonsters(string? bundle, Dictionary<int, int> nameId, Dictionary<int, int> gfx)
    {
        if (bundle is null) return;
        var mgr = new AssetsManager();
        var bun = mgr.LoadBundleFile(bundle, true);
        var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
        foreach (var inf in af.file.AssetInfos)
        {
            if (inf.TypeId != 114) continue;
            var bf = mgr.GetBaseField(af, inf);
            var reg = bf["references"].Value.AsManagedReferencesRegistry;
            foreach (var r in reg.references)
                try { int id = r.data["id"].AsInt; nameId[id] = r.data["nameId"].AsInt; gfx[id] = r.data["gfxId"].AsInt; } catch { }
            break;
        }
        mgr.UnloadAll();
    }

    /// <summary>gfxId (nom de l'icone dans le bundle Picto) d'un monstre par genericId, ou null.</summary>
    public int? MonsterGfx(int genericId) => _monsterGfx is not null && _monsterGfx.TryGetValue(genericId, out var g) ? g : null;

    // Fichier I18n à lire : langue de l'UI, sinon fr, sinon le premier .bin présent ; null si aucun.
    private static string? I18nPath(string content)
    {
        var dir = Path.Combine(content, "I18n");
        if (!Directory.Exists(dir)) return null;
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        string path = Path.Combine(dir, lang + ".bin");
        if (!File.Exists(path)) path = Path.Combine(dir, "fr.bin");
        if (!File.Exists(path)) path = Directory.EnumerateFiles(dir, "*.bin").FirstOrDefault() ?? "";
        return File.Exists(path) ? path : null;
    }

    private void LoadI18n(string content)
    {
        if (I18nPath(content) is not { } path) return;

        var bytes = File.ReadAllBytes(path);
        int pos = 0;
        int len = bytes[pos++];
        pos += len;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        if (count == 0 || count > 5_000_000) return;
        var idx = new Dictionary<int, long>((int)count);
        for (uint i = 0; i < count; i++)
        {
            int nameId = BitConverter.ToInt32(bytes, pos); pos += 4;
            uint off = BitConverter.ToUInt32(bytes, pos); pos += 4;
            idx[nameId] = off;
        }
        _bin = bytes;
        _nameIdToOffset = idx;
    }

    /// <summary>Nom d'un monstre par genericId, ou null si non resolu.</summary>
    public string? MonsterName(int genericId) => Resolve(_cacheMonster, _monsterToNameId, genericId);

    /// <summary>Nom d'un PNJ par npcId, ou null si non resolu.</summary>
    public string? NpcName(int npcId) => Resolve(_cacheNpc, _npcToNameId, npcId);

    private readonly ConcurrentDictionary<int, string> _cacheInteractive = new();
    /// <summary>Nom d'un element interactif par element_type_id (ex 1 -> "Frene", 16 -> "Zaap"), ou null si non resolu.</summary>
    public string? InteractiveName(int elementTypeId) => Resolve(_cacheInteractive, _interactiveToNameId, elementTypeId);

    private readonly ConcurrentDictionary<int, string> _cacheSkill = new();
    /// <summary>Verbe d'action d'un skill (ex 184 -> "Utiliser", 68 -> "Cueillir", 124 -> "Pecher"), ou null. Sert a
    /// nommer les portes/escaliers/transports (element_type_id = -1, sans nom de type propre).</summary>
    public string? SkillName(int skillId) => Resolve(_cacheSkill, _skillToNameId, skillId);

    /// <summary>Portee d'interaction d'un skill en cases (defaut 1 = adjacent). La peche vaut 10 -&gt; on recolte de loin.</summary>
    public int SkillRange(int skillId) => _skillToRange is not null && _skillToRange.TryGetValue(skillId, out var r) && r > 0 ? r : 1;

    private readonly ConcurrentDictionary<int, string> _cacheSpell = new();
    /// <summary>Nom d'un sort par spell_id (ex 12904 -&gt; "Sournoiserie"), pour l'onglet Combat. Null si non resolu.</summary>
    public string? SpellName(int spellId) => Resolve(_cacheSpell, _spellToNameId, spellId);
    private readonly ConcurrentDictionary<int, string> _cacheState = new();
    /// <summary>Nom localisé d'un état de combat (Invisible, Enraciné, Stabilisé...) depuis spellstatesdataroot, ou null.
    /// Sert à rendre lisible le critère `statesCriterion` (HS!17 -> « ne doit pas avoir : &lt;nom de 17&gt; »).</summary>
    public string? StateName(int stateId) => Resolve(_cacheState, _stateToNameId, stateId);

    /// <summary>iconId d'un sort (texture "sort_&lt;iconId&gt;" dans spell_assets) par spell_id. 0 si non resolu.</summary>
    public int SpellIconId(int spellId) => _spellToIcon is not null && _spellToIcon.TryGetValue(spellId, out var i) ? i : 0;

    /// <summary>Texte REEL d'un panneau directionnel (par element_id) : "Atelier des sculpteurs", "Épicerie", "Fermé"...
    /// skill 361 = via reference hint (skillParams -&gt; hintsdataroot), 360 = texte libre (textKey -&gt; I18n). Null si
    /// l'element n'est pas un panneau. Sert a remplacer le type generique "Panneau directionnel".</summary>
    public string? SignText(int elementId)
    {
        if (!_initDone || _signByElement is null || !_signByElement.TryGetValue(elementId, out var s)) return null;
        // 361 = via reference hint : skillParams est un id de hint (repli POI).
        if (s.SkillId == 361 && s.Param > 0)
        {
            if (_hintName is not null && _hintName.TryGetValue(s.Param, out var hn) && Text(hn) is { } h) return h;
            if (_poiName is not null && _poiName.TryGetValue(s.Param, out var pn) && Text(pn) is { } p) return p;
        }
        // 360 = texte libre : textKey est directement une cle I18n.
        if (s.TextKey > 0 && Text(s.TextKey) is { } t) return t;
        // repli : param comme hint meme si skillId inattendu.
        if (s.Param > 0 && _hintName is not null && _hintName.TryGetValue(s.Param, out var hn2)) return Text(hn2);
        return null;
    }

    /// <summary>Texte localisé brut d'une clé I18n (quest:text / d2data:text), ou null.</summary>
    public string? I18nText(int nameId) { EnsureLoaded(); return Text(nameId); }

    /// <summary>Nom d'un défi de combat (challengesdataroot), ou null.</summary>
    public string? ChallengeName(int id) { EnsureLoaded(); return _challengeToNameId is not null && _challengeToNameId.TryGetValue(id, out var n) ? Text(n) : null; }
    /// <summary>nameId d'un défi (rapproche une variante de donjon du défi générique homonyme). null si inconnu.</summary>
    public int? ChallengeNameId(int id) { EnsureLoaded(); return _challengeToNameId is not null && _challengeToNameId.TryGetValue(id, out var n) ? n : null; }
    /// <summary>iconId d'un challenge (0 est valide : Duo/Trio/Quatuor/Chrono). null si inconnu.</summary>
    public int? ChallengeIconId(int id) { EnsureLoaded(); return _challengeToIconId is not null && _challengeToIconId.TryGetValue(id, out var n) ? n : null; }
    /// <summary>Règle du défi (texte de la description, langue du client). null si inconnue.</summary>
    public string? ChallengeDescription(int id) { EnsureLoaded(); return _challengeToDescId is not null && _challengeToDescId.TryGetValue(id, out var n) ? Text(n) : null; }

    /// <summary>Nom d'un type d'objectif de quête (questobjectivetypesdataroot), ou null.</summary>
    public string? QuestObjectiveTypeName(int id) { EnsureLoaded(); return _objTypeToNameId is not null && _objTypeToNameId.TryGetValue(id, out var n) ? Text(n) : null; }

    /// <summary>Description d'une étape de quête (queststepsdataroot.descriptionId), ou null.</summary>
    public string? QuestStepDescription(int id) { EnsureLoaded(); return _stepToDescId is not null && _stepToDescId.TryGetValue(id, out var n) ? Text(n) : null; }

    /// <summary>Nom d'un point d'intérêt (chasse au trésor : pointsofinterestdataroot), ou null.</summary>
    public string? PoiName(int id) { EnsureLoaded(); return _poiName is not null && _poiName.TryGetValue(id, out var n) ? Text(n) : null; }

    /// <summary>Texte d'un message de PNJ (NpcDialogQuestionEvent.message_id -&gt; npcmessagesdataroot), paramètres « #1 », « #2 »…
    /// remplacés par <paramref name="args"/>. null si inconnu.</summary>
    public string? NpcMessageText(int messageId, IReadOnlyList<string>? args = null)
    {
        EnsureLoaded();
        if (_npcMessageToTextId is null || !_npcMessageToTextId.TryGetValue(messageId, out var key) || Text(key) is not { } t) return null;
        if (args is not null)
            for (int i = args.Count; i >= 1; i--) t = t.Replace("#" + i, args[i - 1]);
        return t;
    }

    /// <summary>Texte d'un message système du jeu (TextInformationEvent : type + message_id -&gt; infomessagesdataroot), paramètres
    /// « %1 », « %2 »… remplacés par <paramref name="args"/>. null si inconnu.</summary>
    public string? InfoMessageText(int type, int messageId, IReadOnlyList<string>? args = null)
    {
        EnsureLoaded();
        if (_infoMessageToTextId is null || !_infoMessageToTextId.TryGetValue(InfoKey(type, messageId), out var key) || Text(key) is not { } t) return null;
        if (args is not null)
            for (int i = args.Count; i >= 1; i--) t = t.Replace("%" + i, args[i - 1]);
        return t;
    }

    // nameId -> texte localise (I18n), direct (sans table id->nameId).
    private string? Text(int nameId)
        => nameId > 0 && _nameIdToOffset is not null && _bin is not null && _nameIdToOffset.TryGetValue(nameId, out var o)
           ? ReadString(_bin, (int)o) : null;

    /// <summary>iconId de la ressource recoltee par un skill (skill -> gatheredRessourceItem -> item.iconId), pour
    /// afficher l'icone de l'interactif dans item_assets. 0 si non resolu (ex zaap : pas de ressource associee).</summary>
    public int InteractiveIconId(int skillId)
        => _skillToItem is not null && _skillToItem.TryGetValue(skillId, out var item) && item > 0
           && _itemToIcon is not null && _itemToIcon.TryGetValue(item, out var icon) ? icon : 0;

    /// <summary>iconId d'un objet par son gid (item.iconId), pour afficher l'icone d'une ressource recoltee (itn -> gid).
    /// 0 si non resolu.</summary>
    public int ItemIconId(int itemGid)
        => _itemToIcon is not null && itemGid > 0 && _itemToIcon.TryGetValue(itemGid, out var icon) ? icon : 0;

    /// <summary>gid de l'objet RECOLTE par un skill (skill -> gatheredRessourceItem), &gt;0 UNIQUEMENT pour un vrai skill de
    /// RECOLTE ("Couper"/"Collecter"/"Pecher"...). 0 pour un skill generique/craft (Utiliser/Forger/Cuire) ou un
    /// interactif sans recolte (zaap, coffre, atelier, porte). Sert a distinguer une ressource d'un mobilier interactif.</summary>
    public int GatheredItemForSkill(int skillId)
        => _skillToItem is not null && _skillToItem.TryGetValue(skillId, out var item) && item > 0 ? item : 0;

    private HashSet<int>? _gatherableGids;
    /// <summary>Vrai si le gid est une RESSOURCE récoltée (apparaît comme gatheredRessourceItem d'un skill) donc vendable au
    /// HDV. Ensemble dérivé de <see cref="_skillToItem"/> (mêmes données que element-to-item.json du kit web), construit à la
    /// demande. false si les données de skills ne sont pas chargées (install introuvable) -&gt; l'appelant peut replier sur tout.</summary>
    public bool IsGatherableItem(int gid)
    {
        if (gid <= 0) return false;
        var set = _gatherableGids;
        if (set is null)
        {
            if (_skillToItem is null) return false;
            set = new HashSet<int>();
            foreach (var v in _skillToItem.Values) if (v > 0) set.Add(v);
            _gatherableGids = set;
        }
        return set.Contains(gid);
    }

    /// <summary>Vrai si l'objet est une RESSOURCE : récoltée (skill de récolte) OU de super-type « Ressources » (9 dans les données
    /// du jeu, comme Dofus 2). Sert à « tout mettre en vente » sans jamais y inclure un équipement ou un consommable.</summary>
    public bool IsResourceItem(int gid)
    {
        if (IsGatherableItem(gid)) return true;
        int type = ItemTypeId(gid);
        return type != 0 && _typeToSuper is not null && _typeToSuper.TryGetValue(type, out var sup) && sup == 9;
    }

    /// <summary>Vrai si les donnees de skills de recolte sont chargees (bundles du jeu lus). Si faux (install introuvable),
    /// les appelants doivent replier sur l'ancien comportement plutot que de tout exclure.</summary>
    public bool SkillDataLoaded => _skillToItem is { Count: > 0 };

    /// <summary>Vrai si les données d'items (catégories item->type) sont chargées.</summary>
    public bool ItemDataLoaded => _itemToType is { Count: > 0 };

    /// <summary>Vrai si le gid est un objet CATALOGUÉ (possède un type d'objet), donc vendable au HDV : ressources
    /// (récoltées OU non), consommables, équipements, runes, etc. Élargit <see cref="IsGatherableItem"/> qui ne couvrait
    /// QUE les ressources récoltées. false si non catalogué OU données non chargées — PAS de repli permissif : l'appelant
    /// ne doit pas proposer un gid qu'on ne sait pas classer.</summary>
    public bool IsCataloguedItem(int gid) => gid > 0 && _itemToType is not null && _itemToType.ContainsKey(gid);

    private readonly ConcurrentDictionary<int, string> _cacheServer = new();
    /// <summary>Nom du serveur par serverId (ex 295 -> "Draconiros"), ou null si non resolu. Au 1er appel non résolu, lit
    /// les SEULS noms de serveur (<see cref="EnsureServerNamesLoaded"/>, ~70 ms) au lieu d'attendre le chargement complet.</summary>
    public string? ServerName(int serverId)
    {
        if (serverId == 0) return null;
        if (_cacheServer.TryGetValue(serverId, out var c)) return c;
        if (!_serverNamesDone)
        {
            EnsureServerNamesLoaded();
            if (_cacheServer.TryGetValue(serverId, out c)) return c;
        }
        return Resolve(_cacheServer, _serverToNameId, serverId);
    }

    private volatile bool _serverNamesDone;
    private readonly object _serverNamesLock = new();
    private readonly ConcurrentDictionary<int, string> _cacheServerFr = new();

    /// <summary>Nom FRANÇAIS du serveur, quelle que soit la langue de Windows (« Ombre », jamais « Shadow » / « Schatten ») :
    /// identifiant attendu par le relais archimonstres du serveur (liste de serveurs connus, noms normalisés). Repli sur
    /// <see cref="ServerName"/> ; null si non résolu.</summary>
    public string? CanonicalServerName(int serverId)
    {
        if (serverId == 0) return null;
        EnsureServerNamesLoaded();
        return _cacheServerFr.TryGetValue(serverId, out var s) ? s : ServerName(serverId);
    }

    /// <summary>Serveurs dont le nom a été lu dans le jeu. Pas de filtre « public » ajouté ici.</summary>
    public IReadOnlyList<(int Id, string Name)> KnownServers()
    {
        EnsureServerNamesLoaded();
        return _cacheServer
            .Select(kv => (Id: kv.Key, Name: string.IsNullOrWhiteSpace(kv.Value) ? "Serveur " + kv.Key.ToString(CultureInfo.InvariantCulture) : kv.Value))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Chargement RAPIDE des seuls noms de serveur : bundle serversdataroot + recherche CIBLÉE de leurs nameId dans
    /// l'index I18n (~70 ms mesurés), au lieu de <see cref="EnsureLoaded"/> qui lit ~30 bundles du jeu + tout l'I18n (~12 s au
    /// 1er lancement) — c'était la cause des « Serveur 355 » (modal Socket, carte, Bots) et d'un WhoIs non résolu en MITM tant
    /// que le chargement complet n'était pas fini. Remplit le cache de ServerName. Idempotent, best-effort.</summary>
    public void EnsureServerNamesLoaded()
    {
        if (_serverNamesDone) return;
        lock (_serverNamesLock)
        {
            if (_serverNamesDone) return;
            try
            {
                if (ContentDir() is { } content
                    && ReadIdNameId(Glob(Path.Combine(content, "Data"), "*serversdataroot*.asset.bundle")) is { Count: > 0 } ids
                    && I18nPath(content) is { } path)
                {
                    var names = ReadI18nTexts(path, ids);
                    foreach (var (sid, text) in names) _cacheServer.TryAdd(sid, text);
                    // Noms FRANÇAIS (canoniques) : mêmes si l'I18n choisi est déjà fr.bin, sinon une 2e lecture ciblée (~20 ms).
                    var fr = Path.Combine(content, "I18n", "fr.bin");
                    var frNames = !File.Exists(fr) || string.Equals(Path.GetFullPath(fr), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                        ? names : ReadI18nTexts(fr, ids);
                    foreach (var (sid, text) in frNames) _cacheServerFr.TryAdd(sid, text);
                }
            }
            catch { /* best-effort : repli sur EnsureLoaded / « Serveur N » */ }
            _serverNamesDone = true;
        }
    }

    // Lecture CIBLÉE de quelques textes I18n (même format que LoadI18n : [len][en-tête][count][count × (nameId, offset)], puis
    // chaînes varint-longueur + UTF-8) SANS charger le fichier (26 Mo) : on parcourt l'index puis on saute aux offsets voulus.
    // idToNameId : id -> nameId ; renvoie id -> texte.
    private static Dictionary<int, string> ReadI18nTexts(string path, Dictionary<int, int> idToNameId)
    {
        var res = new Dictionary<int, string>();
        var offsets = new Dictionary<int, long>();
        foreach (var nid in idToNameId.Values) offsets[nid] = -1;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        using var br = new BinaryReader(fs);
        int len = br.ReadByte();
        br.ReadBytes(len);
        uint count = br.ReadUInt32();
        if (count == 0 || count > 5_000_000) return res;
        for (uint i = 0; i < count; i++)
        {
            int nameId = br.ReadInt32();
            uint off = br.ReadUInt32();
            if (offsets.ContainsKey(nameId)) offsets[nameId] = off;   // doublon : le dernier gagne, comme LoadI18n
        }
        foreach (var (id, nid) in idToNameId)
        {
            if (!offsets.TryGetValue(nid, out var off) || off < 0 || off >= fs.Length) continue;
            fs.Position = off;
            int n = 0, shift = 0;
            bool ok = true;
            while (true)
            {
                int x = fs.ReadByte();
                if (x < 0 || shift > 35) { ok = false; break; }
                n |= (x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            if (!ok || n < 0 || fs.Position + n > fs.Length) continue;
            var buf = new byte[n];
            fs.ReadExactly(buf);
            res[id] = Encoding.UTF8.GetString(buf);
        }
        return res;
    }

    private readonly ConcurrentDictionary<int, string> _cacheItem = new();
    /// <summary>Nom d'un objet par gid (ex 1782 -> "Goujon", 421 -> "Ortie"), pour l'affichage des recoltes. Null sinon.</summary>
    public string? ItemName(int itemGid) => Resolve(_cacheItem, _itemToNameId, itemGid);

    /// <summary>Recherche d'objets du catalogue par fragment de nom (insensible à la casse), au plus <paramref name="limit"/> résultats,
    /// triés par nom. Vide tant que les données d'objets ne sont pas chargées. (MZP23 : suggestions de l'onglet HDV.)</summary>
    public IReadOnlyList<(int Gid, string Name, int Level)> SearchItems(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || _itemToType is null || limit <= 0) return Array.Empty<(int, string, int)>();
        var q = query.Trim();
        var hits = new List<(int, string, int)>();
        foreach (var gid in _itemToType.Keys)
        {
            var n = ItemName(gid);
            if (n is not null && n.Contains(q, StringComparison.CurrentCultureIgnoreCase)) hits.Add((gid, n, ItemLevel(gid)));
        }
        return hits.OrderBy(h => h.Item2, StringComparer.CurrentCultureIgnoreCase).Take(limit).ToList();
    }

    private readonly ConcurrentDictionary<int, string> _cacheType = new();
    /// <summary>typeId (ItemType) d'un objet par gid : cle stable de sa categorie. 0 si non resolu.</summary>
    public int ItemTypeId(int itemGid)
        => _itemToType is not null && itemGid > 0 && _itemToType.TryGetValue(itemGid, out var t) ? t : 0;
    /// <summary>Nom de la categorie (type d'objet : "Bois", "Minerai", "Epee", "Poudre"...) d'un objet par gid. Null sinon.</summary>
    public string? ItemCategory(int itemGid) => Resolve(_cacheType, _typeToNameId, ItemTypeId(itemGid));

    // ------------------------------------------------------------------ Lot I : données statiques
    /// <summary>Niveau d'un objet par gid, -1 si inconnu (données non chargées ou gid absent).</summary>
    public int ItemLevel(int gid) => _initDone && _itemToLevel is not null && _itemToLevel.TryGetValue(gid, out var l) ? l : -1;

    /// <summary>Poids UNITAIRE (realWeight, en pods) d'un objet par gid, -1 si inconnu.</summary>
    public int ItemWeight(int gid) => _initDone && _itemToWeight is not null && _itemToWeight.TryGetValue(gid, out var w) ? w : -1;

    /// <summary>Positions d'équipement possibles d'un objet (typeId -&gt; superTypeId -&gt; possiblePositions). Vide si non
    /// équipable ou inconnu.</summary>
    public IReadOnlyList<int> EquipPositions(int gid)
    {
        if (!_initDone || _typeToSuper is null || _superPositions is null) return Array.Empty<int>();
        int type = ItemTypeId(gid);
        return type != 0 && _typeToSuper.TryGetValue(type, out var sup) && _superPositions.TryGetValue(sup, out var pos) ? pos : Array.Empty<int>();
    }

    /// <summary>Données du client chargées (noms, effets...) : avant, les résolutions rendent null.</summary>
    public bool IsReady => _initDone;

    /// <summary>Définition d'un effet d'objet par id, null si inconnue (données non chargées).</summary>
    public EffectDef? Effect(int effectId)
        => _initDone && _effectDefs is not null && _effectDefs.TryGetValue(effectId, out var e) ? e : null;

    private readonly ConcurrentDictionary<int, string> _cacheEffect = new();
    /// <summary>Gabarit du texte d'un effet (ex « #1{~1~2 à }#2 Vitalité »), null si inconnu.</summary>
    public string? EffectTemplate(int effectId)
    {
        if (effectId <= 0) return null;
        if (_cacheEffect.TryGetValue(effectId, out var c)) return c;
        if (Effect(effectId) is not { DescriptionId: > 0 } e || _nameIdToOffset is null || _bin is null) return null;
        if (!_nameIdToOffset.TryGetValue(e.DescriptionId, out var off)) return null;
        var s = ReadString(_bin, (int)off);
        if (s is not null) _cacheEffect[effectId] = s;
        return s;
    }

    private readonly ConcurrentDictionary<int, string> _cacheJob = new();
    /// <summary>Nom d'un métier par id (ex 24 -&gt; « Mineur »), null si inconnu.</summary>
    public string? JobName(int id) => Resolve(_cacheJob, _jobToNameId, id);

    private readonly ConcurrentDictionary<int, string> _cacheQuest = new();
    /// <summary>Nom d'une quête par id, null si inconnu.</summary>
    public string? QuestName(int id) => Resolve(_cacheQuest, _questToNameId, id);

    private readonly ConcurrentDictionary<int, string> _cacheQuestStep = new();
    /// <summary>Nom d'une étape de quête par id, null si inconnu.</summary>
    public string? QuestStepName(int id) => Resolve(_cacheQuestStep, _stepToNameId, id);

    private readonly ConcurrentDictionary<int, string> _cacheSubArea = new();
    /// <summary>Nom d'une sous-zone par id, null si inconnu.</summary>
    public string? SubAreaName(int id) => Resolve(_cacheSubArea, _subAreaToNameId, id);

    /// <summary>Zone (area) d'une sous-zone, -1 si inconnue. ⚠ 0 = Amakna est une zone VALIDE.</summary>
    public int AreaOfSubArea(int subAreaId)
        => _initDone && subAreaId > 0 && _subAreaToArea is not null && _subAreaToArea.TryGetValue(subAreaId, out var a) ? a : -1;

    private readonly ConcurrentDictionary<int, string> _cacheArea = new();
    /// <summary>Nom d'une zone par id (0 = Amakna VALIDE). Ne passe PAS par Resolve (qui écarte l'id 0).</summary>
    public string? AreaName(int areaId)
    {
        if (areaId < 0) return null;
        if (_cacheArea.TryGetValue(areaId, out var c)) return c;
        if (!_initDone || _areaToNameId is null || _nameIdToOffset is null || _bin is null) return null;
        if (!_areaToNameId.TryGetValue(areaId, out var nameId) || !_nameIdToOffset.TryGetValue(nameId, out var off)) return null;
        var s = ReadString(_bin, (int)off);
        if (s is not null) _cacheArea[areaId] = s;
        return s;
    }

    private string? Resolve(ConcurrentDictionary<int, string> cache, Dictionary<int, int>? table, int id)
    {
        if (id == 0) return null;
        if (cache.TryGetValue(id, out var c)) return c;
        if (!_initDone || table is null || _nameIdToOffset is null || _bin is null) return null;
        if (!table.TryGetValue(id, out var nameId)) return null;
        if (!_nameIdToOffset.TryGetValue(nameId, out var off)) return null;
        var s = ReadString(_bin, (int)off);
        if (s is not null) cache[id] = s;
        return s;
    }

    private static string? ReadString(byte[] b, int pos)
    {
        if (pos < 0 || pos >= b.Length) return null;
        int n = 0, shift = 0;
        while (true)
        {
            if (pos >= b.Length) return null;
            byte x = b[pos++];
            n |= (x & 0x7F) << shift;
            if ((x & 0x80) == 0) break;
            shift += 7;
            if (shift > 35) return null;
        }
        if (n < 0 || pos + n > b.Length) return null;
        return Encoding.UTF8.GetString(b, pos, n);
    }
}
