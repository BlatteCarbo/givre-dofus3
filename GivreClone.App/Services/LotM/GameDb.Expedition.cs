using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services.LotM;

public sealed record ExpArea(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ExpSubArea(int Id, string Name, int Level, int MonsterCount)
{
    public string Caption => Level > 0 ? $"{Name}  ·  niv. {Level}" : Name;
}

public sealed record ExpMonster(int Id, string Name, int MinLevel, int MaxLevel)
{
    public string LevelText => MinLevel <= 0 ? "" : MinLevel == MaxLevel ? $"niv. {MinLevel}" : $"niv. {MinLevel}-{MaxLevel}";
}

public sealed record CreationPart(int Id, string Label, int Order, string AssetId = "");

public sealed record ExpDungeon(int Id, string Name, int Level)
{
    public string Caption => Level > 0 ? $"{Name}  ·  niv. {Level}" : Name;
    public override string ToString() => Name;
}

/// <summary>Lectures de l'onglet Expédition : zones -> sous-zones -> monstres/cartes. Requêtes courtes, à la demande (rien n'est préchargé).</summary>
public sealed partial class GameDb
{
    private static readonly object ExpeditionGate = new();

    private static IEnumerable<int> JsonInts(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { }
        if (doc is null) yield break;
        using (doc)
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v)) yield return v;
    }

    /// <summary>Zones qui ont au moins une sous-zone avec des monstres, triées par nom.</summary>
    public IReadOnlyList<ExpArea> ExpeditionAreas()
    {
        lock (ExpeditionGate)
        {
            if (!IsKnownTable("AreaData") || !IsKnownTable("SubAreaData")) return Array.Empty<ExpArea>();
            var populated = Query("select areaId, monsters from SubAreaData where monsters is not null")
                .Where(r => JsonInts(S(r[1])).Any(id => id > 0)).Select(r => I(r[0])).ToHashSet();
            return Query("select id, nameId from AreaData").Where(r => populated.Contains(I(r[0])))
                .Select(r => new ExpArea(I(r[0]), Text(L(r[1])) ?? ""))
                .Where(a => a.Name.Length > 0).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<ExpSubArea> ExpeditionSubAreas(int areaId)
    {
        lock (ExpeditionGate)
            return Query("select id, nameId, level, monsters from SubAreaData where areaId = $0 and monsters is not null and monsters <> '[]'", areaId)
                .Select(r => new ExpSubArea(I(r[0]), Text(L(r[1])) ?? "", I(r[2]), JsonInts(S(r[3])).Count()))
                .Where(s => s.Name.Length > 0 && s.MonsterCount > 0).OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Monstres qui apparaissent dans une sous-zone, avec leur plage de niveaux (grades).</summary>
    public IReadOnlyList<ExpMonster> ExpeditionMonsters(int subAreaId)
    {
        lock (ExpeditionGate)
        {
            var ids = JsonInts(S(Scalar("select monsters from SubAreaData where id = $0", subAreaId))).Distinct().ToList();
            var res = new List<ExpMonster>();
            foreach (int id in ids)
            {
                var row = Query("select nameId, grades from MonsterData where id = $0", id).FirstOrDefault();
                if (row is null) continue;
                string name = Text(L(row[0])) ?? "";
                if (name.Length == 0) continue;
                int min = 0, max = 0;
                try
                {
                    using var doc = JsonDocument.Parse(S(row[1]));
                    var lv = doc.RootElement.ValueKind == JsonValueKind.Array
                        ? doc.RootElement.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object && g.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number)
                            .Select(g => g.GetProperty("level").GetInt32()).Where(v => v > 0).ToList()
                        : new List<int>();
                    if (lv.Count > 0) { min = lv.Min(); max = lv.Max(); }
                }
                catch (JsonException) { }
                res.Add(new ExpMonster(id, name, min, max));
            }
            return res.OrderBy(m => m.MinLevel).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Drops d'un monstre (objet + % du grade 1), lus de MonsterData.drops ; vide si illisible.</summary>
    public IReadOnlyList<GivreClone.App.Services.Expedition.MonsterDrop> MonsterDrops(int monsterId)
    {
        lock (ExpeditionGate)
        {
            var res = new List<GivreClone.App.Services.Expedition.MonsterDrop>();
            try
            {
                using var doc = JsonDocument.Parse(S(Scalar("select drops from MonsterData where id = $0", monsterId)));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var d in doc.RootElement.EnumerateArray())
                        if (d.TryGetProperty("objectId", out var o) && o.ValueKind == JsonValueKind.Number &&
                            d.TryGetProperty("percentDropForGrade1", out var p) && p.ValueKind == JsonValueKind.Number)
                            res.Add(new(o.GetInt64(), p.GetDouble()));
            }
            catch (JsonException) { }
            return res;
        }
    }

    // ---- Création de personnage : têtes / corps proposés à la création et couleurs par défaut d'une classe (BreedData.id) ----
    public IReadOnlyList<CreationPart> CreationHeads(int breedId, int gender) => CreationParts("HeadData", breedId, gender);
    public IReadOnlyList<CreationPart> CreationBodies(int breedId, int gender) => CreationParts("BodyData", breedId, gender);

    private IReadOnlyList<CreationPart> CreationParts(string table, int breedId, int gender)
    {
        lock (ExpeditionGate)
        {
            if (!IsKnownTable(table)) return Array.Empty<CreationPart>();
            return Query($"select id, label, \"order\", assetId from {table} where breed = $0 and gender = $1 and availableAtCreation = 1 order by \"order\", id", breedId, gender)
                .Select(r => new CreationPart(I(r[0]), S(r[1]), I(r[2]), S(r[3]))).ToList();
        }
    }

    /// <summary>Six couleurs RVB par défaut de la classe (peau, cheveux, tenue 1-4) ; vide si inconnues.</summary>
    public IReadOnlyList<int> BreedDefaultColors(int breedId, int gender)
    {
        lock (ExpeditionGate)
            return JsonInts(S(Scalar($"select {(gender == 0 ? "maleColors" : "femaleColors")} from BreedData where id = $0", breedId))).ToList();
    }

    /// <summary>Objets dont le nom traduit contient le texte (insensible à la casse pour l'ASCII), préfixe d'abord, puis nom le plus court.</summary>
    public IReadOnlyList<(long Gid, string Name, int Level)> FindItems(string text, int limit)
    {
        text = (text ?? "").Trim().Replace("%", "").Replace("_", "");
        if (text.Length < 2) return Array.Empty<(long, string, int)>();
        lock (ExpeditionGate)
            return Query("select i.id, t.value, i.level from ItemData i join translations t on t.id = cast(i.nameId as text) and t.lang = $1 " +
                         $"where t.value like $0 order by (t.value like $2) desc, length(t.value), t.value limit {Math.Clamp(limit, 1, 200)}",
                         "%" + text + "%", _lang, text + "%")
                .Select(r => (L(r[0]), S(r[1]), I(r[2]))).ToList();
    }

    public int JobIconIdOf(int jobId) { lock (ExpeditionGate) return IsKnownTable("JobData") ? I(Scalar("select iconId from JobData where id = $0", jobId)) : 0; }

    public int ItemIconIdOf(long gid) { lock (ExpeditionGate) return I(Scalar("select iconId from ItemData where id = $0", gid)); }

    /// <summary>Panoplies (hors cosmétiques) dont le nom contient le texte : (id, nom, nombre de pièces).</summary>
    public IReadOnlyList<(int Id, string Name, int Pieces)> FindSets(string text, int limit)
    {
        text = (text ?? "").Trim().Replace("%", "").Replace("_", "");
        if (text.Length < 2 || !IsKnownTable("ItemSetData")) return Array.Empty<(int, string, int)>();
        lock (ExpeditionGate)
            return Query("select s.id, t.value, s.items from ItemSetData s join translations t on t.id = cast(s.nameId as text) and t.lang = $1 " +
                         $"where t.value like $0 and s.isCosmetic = 0 order by (t.value like $2) desc, length(t.value), t.value limit {Math.Clamp(limit, 1, 100)}",
                         "%" + text + "%", _lang, text + "%")
                .Select(r => (I(r[0]), S(r[1]), JsonInts(S(r[2])).Count())).ToList();
    }

    public IReadOnlyList<long> SetItems(int setId)
    {
        lock (ExpeditionGate) return JsonInts(S(Scalar("select items from ItemSetData where id = $0", setId))).Select(i => (long)i).ToList();
    }

    /// <summary>Ratio d'XP de craft d'un objet (1 = 100 %) : celui de l'objet, sinon celui de son type, sinon 100 %.</summary>
    public double CraftXpRatio(long gid)
    {
        lock (ExpeditionGate)
        {
            var r = Query("select i.craftXpRatio, t.craftXpRatio from ItemData i left join ItemTypeData t on t.id = i.typeId where i.id = $0", gid).FirstOrDefault();
            if (r is null) return 1;
            int item = r[0] is null or DBNull ? -1 : I(r[0]), type = r[1] is null or DBNull ? -1 : I(r[1]);
            return item >= 0 ? item / 100.0 : type >= 0 ? type / 100.0 : 1.0;
        }
    }

    /// <summary>Le monstre a-t-il une âme capturable (archimonstre, boss à âme) d'après les données du jeu ?</summary>
    public bool MonsterCapturable(int monsterId)
    {
        lock (ExpeditionGate)
        {
            string souls = S(Scalar("select souls from MonsterData where id = $0", monsterId));
            return souls.Length > 2 && souls != "[]";
        }
    }

    /// <summary>Monstres à âme capturable dont le nom contient le texte (archimonstres, boss à âme), triés par nom.</summary>
    public IReadOnlyList<(int Id, string Name)> CapturableMonsters(string? text, int limit)
    {
        string like = "%" + (text ?? "").Trim().Replace("%", "").Replace("_", "") + "%";
        lock (ExpeditionGate)
            return Query($"select m.id, t.value from MonsterData m join translations t on t.id = cast(m.nameId as text) and t.lang = $1 where m.souls is not null and length(m.souls) > 2 and t.value like $0 " +
                         $"order by t.value, m.id limit {Math.Clamp(limit, 1, 200)}", like, _lang).Select(r => (I(r[0]), S(r[1]))).ToList();
    }

    /// <summary>Position monde de TOUTES les cartes des données du jeu (id, x, y, calque) — les mêmes relevés que les cartes DofusDB. Sert de repli quand la table extraite du client ne connaît pas une carte.</summary>
    public IReadOnlyDictionary<long, (int X, int Y, int World)> AllMapPositions()
    {
        lock (ExpeditionGate)
        {
            var res = new Dictionary<long, (int, int, int)>();
            if (!IsKnownTable("MapInformationData")) return res;
            foreach (var r in Query("select id, posX, posY, worldMap from MapInformationData where posX is not null and posY is not null"))
                res[L(r[0])] = (I(r[1]), I(r[2]), r[3] is null or DBNull ? -1 : I(r[3]));
            return res;
        }
    }

    /// <summary>Cartes qui portent le zaap de leur sous-zone (SubAreaData.associatedZaapMapId).</summary>
    public IReadOnlyList<long> ZaapMaps()
    {
        lock (ExpeditionGate)
            return IsKnownTable("SubAreaData")
                ? Query("select distinct associatedZaapMapId from SubAreaData where associatedZaapMapId is not null and associatedZaapMapId > 0").Select(r => L(r[0])).ToList()
                : new List<long>();
    }

    public int ItemLevelOf(long gid) { lock (ExpeditionGate) return I(Scalar("select level from ItemData where id = $0", gid)); }

    private IReadOnlyDictionary<int, decimal>? _effectPowerRates;

    /// <summary>Poids de forgemagie par point, par id d'effet (EffectData.effectPowerRate, non nul), chargé une fois.</summary>
    public IReadOnlyDictionary<int, decimal> EffectPowerRates()
    {
        lock (ExpeditionGate)
        {
            if (_effectPowerRates is not null) return _effectPowerRates;
            var d = new Dictionary<int, decimal>();
            if (IsKnownTable("EffectData"))
                foreach (var r in Query("select id, effectPowerRate from EffectData where effectPowerRate is not null and effectPowerRate <> 0"))
                    d[I(r[0])] = Convert.ToDecimal(r[1], CultureInfo.InvariantCulture);
            return _effectPowerRates = d;
        }
    }

    public IReadOnlyList<long> ExpeditionMaps(int subAreaId)
    {
        lock (ExpeditionGate)
            return Query("select target_id from SubAreaData_mapIds_junction where SubAreaData_id = $0", subAreaId).Select(r => L(r[0])).Where(m => m > 0).ToList();
    }

    /// <summary>Donjons connus (nom + niveau conseillé), triés par niveau puis nom. Le détail (salles, clés, boss) : DungeonById.</summary>
    public IReadOnlyList<ExpDungeon> ExpeditionDungeons()
    {
        lock (ExpeditionGate)
        {
            if (!IsKnownTable("DungeonData")) return Array.Empty<ExpDungeon>();
            return Query("select id, nameId, optimalPlayerLevel from DungeonData")
                .Select(r => new ExpDungeon(I(r[0]), Text(L(r[1])) ?? "", I(r[2])))
                .Where(d => d.Name.Length > 0)
                .OrderBy(d => d.Level).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
