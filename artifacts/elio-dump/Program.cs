using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

public class SpellLevelData
{
    public int Id { get; set; }
    public int SpellId { get; set; }
    public int Grade { get; set; }
    public int MinPlayerLevel { get; set; }
    public int ApCost { get; set; }
    public int MinRange { get; set; }
    public int Range { get; set; }
    public int Flags { get; set; }
    public bool CastInLine { get; set; }
    public bool CastInDiagonal { get; set; }
    public bool CastTestLos { get; set; }
    public bool NeedFreeCell { get; set; }
    public bool NeedTakenCell { get; set; }
    public bool RangeCanBeBoosted { get; set; }
    public int CriticalHitProbability { get; set; }
    public int MaxCastPerTurn { get; set; }
    public int MaxCastPerTarget { get; set; }
    public int MaxGlobalCastPerTurn { get; set; }
    public int MaxGlobalCastPerTarget { get; set; }
    public int MinCastInterval { get; set; } // Cooldown
    public int InitialCooldown { get; set; }
    public int GlobalCooldown { get; set; }
    public string StatesCriterion { get; set; } = "";
    public List<EffectData> Effects { get; set; } = new();
    public List<EffectData> CriticalEffects { get; set; } = new();
}

public class EffectData
{
    public int ActionId { get; set; }
    public string ActionDescription { get; set; } = "";
    public int EffectElement { get; set; }
    public string ElementName { get; set; } = "";
    public int DiceNum { get; set; }
    public int DiceSide { get; set; }
    public int Value { get; set; }
    public int Duration { get; set; }
    public int Delay { get; set; }
    public float Random { get; set; }
    public string Triggers { get; set; } = "";
    public string EffectCondition { get; set; } = "";
    public string TargetMask { get; set; } = "";
    public int ZoneShape { get; set; }
    public char ZoneShapeChar => ZoneShape > 0 && ZoneShape < 128 ? (char)ZoneShape : '?';
    public int ZoneSize { get; set; }
    public int ZoneMinSize { get; set; }
}

public class SpellFullData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AdminName { get; set; } = "";
    public int IconId { get; set; }
    public int TypeId { get; set; }
    public int Order { get; set; }
    public int VariantId { get; set; }
    public int VariantPairSpellId { get; set; }
    public string VariantPairSpellName { get; set; } = "";
    public List<int> SpellLevelIds { get; set; } = new();
    public List<SpellLevelData> Levels { get; set; } = new();
}

class Program
{
    static string ContentDir = @"%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\Dofus_Data\StreamingAssets\Content";
    static string DataDir = Path.Combine(ContentDir, "Data");
    static string I18nDir = Path.Combine(ContentDir, "I18n");

    static Dictionary<int, string> i18n = new();

    static void LoadI18n()
    {
        var path = Path.Combine(I18nDir, "fr.bin");
        var bytes = File.ReadAllBytes(path);
        int pos = 0;
        int len = bytes[pos++];
        pos += len;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        var offsets = new Dictionary<int, long>((int)count);
        for (uint i = 0; i < count; i++)
        {
            int nameId = BitConverter.ToInt32(bytes, pos); pos += 4;
            uint off = BitConverter.ToUInt32(bytes, pos); pos += 4;
            offsets[nameId] = off;
        }

        foreach (var (nid, off) in offsets)
        {
            if (off < 0 || off >= bytes.Length) continue;
            int cur = (int)off;
            int n = 0, shift = 0;
            bool ok = true;
            while (true)
            {
                if (cur >= bytes.Length || shift > 35) { ok = false; break; }
                int x = bytes[cur++];
                n |= (x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            if (!ok || n < 0 || cur + n > bytes.Length) continue;
            i18n[nid] = Encoding.UTF8.GetString(bytes, cur, n);
        }
        Console.WriteLine($"Loaded {i18n.Count} I18n strings from fr.bin");
    }

    public static string GetText(int id) => i18n.TryGetValue(id, out var s) ? s : $"[Unknown {id}]";

    public static string GetElementName(int e) => e switch
    {
        0 => "Neutre",
        1 => "Terre",
        2 => "Feu",
        3 => "Eau",
        4 => "Air",
        _ => e >= 0 ? $"Elem_{e}" : "Non-élémentaire"
    };

    static void Main()
    {
        LoadI18n();

        // 1. Spell States
        var stateMap = new Dictionary<int, string>();
        {
            var statesBundle = Path.Combine(DataDir, "data_assets_spellstatesdataroot.asset.bundle");
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(statesBundle, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;
                var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
                if (reg == null) continue;
                foreach (var r in reg.references)
                {
                    int id = 0, nameId = 0;
                    foreach (var c in r.data.Children)
                    {
                        if (c.FieldName == "id") { try { id = c.AsInt; } catch {} }
                        else if (c.FieldName == "nameId") { try { nameId = c.AsInt; } catch {} }
                    }
                    if (id != 0) stateMap[id] = GetText(nameId);
                }
                break;
            }
            mgr.UnloadAll();
        }
        Console.WriteLine($"Loaded {stateMap.Count} spell states");
        File.WriteAllText("spell_states.json", JsonSerializer.Serialize(stateMap, new JsonSerializerOptions { WriteIndented = true }));

        // 2. Action definitions (effectsdataroot)
        var actionDescriptions = new Dictionary<int, string>();
        {
            var effBundle = Directory.EnumerateFiles(DataDir, "*effectsdataroot*.asset.bundle")
                .First(f => Path.GetFileName(f).Contains("effectsdataroot") && !Path.GetFileName(f).Contains("spelleffects"));
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(effBundle, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;
                var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
                if (reg == null) continue;
                foreach (var r in reg.references)
                {
                    int id = 0, descId = 0;
                    foreach (var c in r.data.Children)
                    {
                        if (c.FieldName == "id") { try { id = c.AsInt; } catch {} }
                        else if (c.FieldName == "descriptionId") { try { descId = c.AsInt; } catch {} }
                    }
                    if (id != 0 && descId != 0) actionDescriptions[id] = GetText(descId);
                }
                break;
            }
            mgr.UnloadAll();
        }
        Console.WriteLine($"Loaded {actionDescriptions.Count} action descriptions");

        // 3. Spell Variants for Breed 16 (Eliotrope)
        var variantPairs = new Dictionary<int, (int VariantId, int OtherSpellId)>();
        var elioSpellIds = new List<int>();
        var variantList = new List<object>();
        {
            var bundle = Path.Combine(DataDir, "data_assets_spellvariantsdataroot.asset.bundle");
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(bundle, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;
                var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
                if (reg == null) continue;
                foreach (var r in reg.references)
                {
                    int id = 0, breedId = 0;
                    var sids = new List<int>();
                    foreach (var c in r.data.Children)
                    {
                        if (c.FieldName == "id") { try { id = c.AsInt; } catch {} }
                        else if (c.FieldName == "breedId") { try { breedId = c.AsInt; } catch {} }
                        else if (c.FieldName == "spellIds")
                        {
                            var arr = c.Children.FirstOrDefault(x => x.FieldName == "Array") ?? c;
                            foreach (var s in arr.Children) { try { int sid = s.AsInt; if (sid > 0) sids.Add(sid); } catch {} }
                        }
                    }
                    if (breedId == 16) // ELIOTROPE
                    {
                        variantList.Add(new { Id = id, BreedId = breedId, SpellIds = sids });
                        foreach (var sid in sids) elioSpellIds.Add(sid);
                        if (sids.Count == 2)
                        {
                            variantPairs[sids[0]] = (id, sids[1]);
                            variantPairs[sids[1]] = (id, sids[0]);
                        }
                    }
                }
                break;
            }
            mgr.UnloadAll();
        }
        Console.WriteLine($"Found {elioSpellIds.Count} Eliotrope (breed 16) spell IDs across {variantPairs.Count / 2} variant pairs");
        File.WriteAllText("elio_variants_b16.json", JsonSerializer.Serialize(variantList, new JsonSerializerOptions { WriteIndented = true }));

        // 4. Spells (data_assets_spellsdataroot.asset.bundle)
        var spellsDict = new Dictionary<int, SpellFullData>();
        {
            var bundle = Path.Combine(DataDir, "data_assets_spellsdataroot.asset.bundle");
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(bundle, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;
                var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
                if (reg == null) continue;
                foreach (var r in reg.references)
                {
                    int id = 0, nameId = 0, descId = 0, iconId = 0, typeId = 0, order = 0;
                    string adminName = "";
                    var levelIds = new List<int>();
                    foreach (var c in r.data.Children)
                    {
                        if (c.FieldName == "id") { try { id = c.AsInt; } catch {} }
                        else if (c.FieldName == "nameId") { try { nameId = c.AsInt; } catch {} }
                        else if (c.FieldName == "descriptionId") { try { descId = c.AsInt; } catch {} }
                        else if (c.FieldName == "iconId") { try { iconId = c.AsInt; } catch {} }
                        else if (c.FieldName == "typeId") { try { typeId = c.AsInt; } catch {} }
                        else if (c.FieldName == "order") { try { order = c.AsInt; } catch {} }
                        else if (c.FieldName == "adminName") { try { adminName = c.AsString; } catch {} }
                        else if (c.FieldName == "spellLevels")
                        {
                            var arr = c.Children.FirstOrDefault(x => x.FieldName == "Array") ?? c;
                            foreach (var sc in arr.Children) { try { int slid = sc.AsInt; if (slid > 0) levelIds.Add(slid); } catch {} }
                        }
                    }
                    if (elioSpellIds.Contains(id))
                    {
                        var sp = new SpellFullData
                        {
                            Id = id,
                            Name = GetText(nameId),
                            Description = GetText(descId),
                            AdminName = adminName,
                            IconId = iconId,
                            TypeId = typeId,
                            Order = order,
                            SpellLevelIds = levelIds
                        };
                        if (variantPairs.TryGetValue(id, out var vp))
                        {
                            sp.VariantId = vp.VariantId;
                            sp.VariantPairSpellId = vp.OtherSpellId;
                        }
                        spellsDict[id] = sp;
                    }
                }
                break;
            }
            mgr.UnloadAll();
        }
        Console.WriteLine($"Loaded {spellsDict.Count} Eliotrope spells definitions");

        // Resolve variant pair spell names
        foreach (var sp in spellsDict.Values)
        {
            if (spellsDict.TryGetValue(sp.VariantPairSpellId, out var other))
            {
                sp.VariantPairSpellName = other.Name;
            }
        }

        // 5. Spell Levels (data_assets_spelllevelsdataroot.asset.bundle)
        {
            var bundle = Path.Combine(DataDir, "data_assets_spelllevelsdataroot.asset.bundle");
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(bundle, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;
                var reg = mgr.GetBaseField(af, inf)["references"].Value?.AsManagedReferencesRegistry;
                if (reg == null) continue;
                foreach (var r in reg.references)
                {
                    int id = 0, spellId = 0, grade = 0, minPlayerLevel = 0, apCost = 0, minRange = 0, range = 0, flags = 0;
                    int critHitProb = 0, maxCastPerTurn = 0, maxCastPerTarget = 0, maxGlobalTurn = 0, maxGlobalTarget = 0;
                    int minInterval = 0, initCd = 0, globalCd = 0;
                    string criterion = "";
                    AssetTypeValueField? effectsField = null, critEffectsField = null;

                    foreach (var c in r.data.Children)
                    {
                        switch (c.FieldName)
                        {
                            case "id": try { id = c.AsInt; } catch {} break;
                            case "spellId": try { spellId = c.AsInt; } catch {} break;
                            case "grade": try { grade = c.AsInt; } catch {} break;
                            case "minPlayerLevel": try { minPlayerLevel = c.AsInt; } catch {} break;
                            case "apCost": try { apCost = c.AsInt; } catch {} break;
                            case "minRange": try { minRange = c.AsInt; } catch {} break;
                            case "range": try { range = c.AsInt; } catch {} break;
                            case "m_flags": try { flags = c.AsInt; } catch {} break;
                            case "criticalHitProbability": try { critHitProb = c.AsInt; } catch {} break;
                            case "maxCastPerTurn": try { maxCastPerTurn = c.AsInt; } catch {} break;
                            case "maxCastPerTarget": try { maxCastPerTarget = c.AsInt; } catch {} break;
                            case "maxGlobalCastPerTurn": try { maxGlobalTurn = c.AsInt; } catch {} break;
                            case "maxGlobalCastPerTarget": try { maxGlobalTarget = c.AsInt; } catch {} break;
                            case "minCastInterval": try { minInterval = c.AsInt; } catch {} break;
                            case "initialCooldown": try { initCd = c.AsInt; } catch {} break;
                            case "globalCooldown": try { globalCd = c.AsInt; } catch {} break;
                            case "statesCriterion": try { criterion = c.AsString; } catch {} break;
                            case "effects": effectsField = c; break;
                            case "criticalEffect": critEffectsField = c; break;
                        }
                    }

                    if (spellsDict.TryGetValue(spellId, out var sp))
                    {
                        var lvl = new SpellLevelData
                        {
                            Id = id,
                            SpellId = spellId,
                            Grade = grade,
                            MinPlayerLevel = minPlayerLevel,
                            ApCost = apCost,
                            MinRange = minRange,
                            Range = range,
                            Flags = flags,
                            CastInLine = (flags & 1) != 0,
                            CastInDiagonal = (flags & 2) != 0,
                            CastTestLos = (flags & 4) != 0,
                            NeedFreeCell = (flags & 8) != 0,
                            NeedTakenCell = (flags & 16) != 0,
                            RangeCanBeBoosted = (flags & 64) != 0,
                            CriticalHitProbability = critHitProb,
                            MaxCastPerTurn = maxCastPerTurn,
                            MaxCastPerTarget = maxCastPerTarget,
                            MaxGlobalCastPerTurn = maxGlobalTurn,
                            MaxGlobalCastPerTarget = maxGlobalTarget,
                            MinCastInterval = minInterval,
                            InitialCooldown = initCd,
                            GlobalCooldown = globalCd,
                            StatesCriterion = criterion ?? ""
                        };

                        List<EffectData> ParseEffects(AssetTypeValueField? efField)
                        {
                            var list = new List<EffectData>();
                            if (efField == null) return list;
                            var arr = efField.Children.FirstOrDefault(x => x.FieldName == "Array");
                            if (arr == null) return list;
                            foreach (var ec in arr.Children)
                            {
                                var eff = new EffectData();
                                foreach (var c in ec.Children)
                                {
                                    switch (c.FieldName)
                                    {
                                        case "actionId": try { eff.ActionId = c.AsInt; } catch {} break;
                                        case "effectElement": try { eff.EffectElement = c.AsInt; } catch {} break;
                                        case "diceNum": try { eff.DiceNum = c.AsInt; } catch {} break;
                                        case "diceSide": try { eff.DiceSide = c.AsInt; } catch {} break;
                                        case "value": try { eff.Value = c.AsInt; } catch {} break;
                                        case "duration": try { eff.Duration = c.AsInt; } catch {} break;
                                        case "delay": try { eff.Delay = c.AsInt; } catch {} break;
                                        case "random": try { eff.Random = (float)c.AsDouble; } catch {} break;
                                        case "triggers": try { eff.Triggers = c.AsString; } catch {} break;
                                        case "effectCondition": try { eff.EffectCondition = c.AsString; } catch {} break;
                                        case "targetMask": try { eff.TargetMask = c.AsString; } catch {} break;
                                        case "zoneDescr":
                                            foreach (var zc in c.Children)
                                            {
                                                switch (zc.FieldName)
                                                {
                                                    case "shape": try { eff.ZoneShape = zc.AsInt; } catch {} break;
                                                    case "param1": try { eff.ZoneSize = zc.AsInt; } catch {} break;
                                                    case "param2": try { eff.ZoneMinSize = zc.AsInt; } catch {} break;
                                                }
                                            }
                                            break;
                                    }
                                }
                                eff.ElementName = GetElementName(eff.EffectElement);
                                if (actionDescriptions.TryGetValue(eff.ActionId, out var ad))
                                    eff.ActionDescription = ad;
                                list.Add(eff);
                            }
                            return list;
                        }

                        lvl.Effects = ParseEffects(effectsField);
                        lvl.CriticalEffects = ParseEffects(critEffectsField);
                        sp.Levels.Add(lvl);
                    }
                }
                break;
            }
            mgr.UnloadAll();
        }

        // Sort levels by Grade
        foreach (var sp in spellsDict.Values)
        {
            sp.Levels = sp.Levels.OrderBy(l => l.Grade).ToList();
        }

        Console.WriteLine("Saving complete eliotrope dataset (Breed 16)...");
        var outList = spellsDict.Values.OrderBy(s => s.Levels.FirstOrDefault()?.MinPlayerLevel ?? 999).ThenBy(s => s.Id).ToList();
        var json = JsonSerializer.Serialize(outList, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("elio_complete_data_b16.json", json);
        Console.WriteLine($"Dump complete! Total Eliotrope spells: {outList.Count}");
    }
}
