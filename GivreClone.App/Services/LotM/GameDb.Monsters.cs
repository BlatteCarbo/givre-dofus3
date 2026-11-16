using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services.LotM;

public sealed record SummonGradeCharacteristics(int Life, int Ap, int Mp, IReadOnlyDictionary<int, int> Characteristics,
    IReadOnlyList<(int SpellId, int Grade)>? Spells = null, int SummonCost = 1);

public sealed partial class GameDb
{
    /// <summary>Exact summon rank. Level scaling is supported for the observed bonus-life summon shape;
    /// monster-shaped grades and derived dodge/removal statistics are not inferred.</summary>
    public SummonGradeCharacteristics? SummonGradeStats(int monsterId, int grade, int summonerLevel)
    {
        if (!IsKnownTable("MonsterData")) return null;
        var row = Query("select grades, spells, spellGrades, summonCost from MonsterData where id = $0", monsterId).FirstOrDefault();
        if (row is null || row[0] is not string json)
            return null;
        using var doc = JsonDocument.Parse(json);
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            if (!g.TryGetProperty("grade", out var rank) || rank.GetInt32() != grade) continue;
            if (!g.TryGetProperty("bonusCharacteristics", out var bonus)) return null;
            int Read(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
            if (Read(g, "lifePoints") != 0 || Read(bonus, "lifePoints") <= 0) return null;
            var stats = new Dictionary<int, int>();
            foreach (var (id, name) in new[] { (10, "strength"), (13, "chance"), (14, "agility"), (15, "intelligence") })
                stats[id] = (int)((long)Read(g, name) * (100 + Math.Max(1, summonerLevel)) / 100) + Read(bonus, name);
            foreach (var (id, name) in new[] { (33, "reductionEarth"), (34, "reductionFire"),
                (35, "reductionWater"), (36, "reductionAir"), (37, "reductionNeutral") })
                stats[id] = Read(g, name) + Read(bonus, name);
            var spells = new List<(int SpellId, int Grade)>();
            using var ids = JsonDocument.Parse(S(row[1]));
            using var ranks = JsonDocument.Parse(S(row[2]));
            for (int index = 0; index < ids.RootElement.GetArrayLength() && index < ranks.RootElement.GetArrayLength(); index++)
            {
                var choices = (ranks.RootElement[index].GetString() ?? "").Split(';');
                // Each entry corresponds to the monster rank; the second component is its level, not its rank.
                if (grade > choices.Length) continue;
                var choice = choices[grade - 1].Split(',');
                if (choice.Length == 2 && int.TryParse(choice[0], out int spellRank) && spellRank > 0
                    && int.TryParse(choice[1], out int requiredLevel) && requiredLevel <= Read(g, "level"))
                    spells.Add((ids.RootElement[index].GetInt32(), spellRank));
            }
            int life = (int)((long)Read(bonus, "lifePoints") * (Math.Max(1, summonerLevel) + 10) / 20);
            return new(life, Math.Max(0, Read(g, "actionPoints") + Read(bonus, "actionPoints")),
                Math.Max(0, Read(g, "movementPoints") + Read(bonus, "movementPoints")), stats, spells,
                Convert.ToInt32(row[3]));
        }
        return null;
    }

    /// <summary>XP de base (champ <c>gradeXp</c> de MonsterData.grades) d'un monstre, par NIVEAU de grade ; null si le monstre ou le champ est absent.
    /// C'est l'XP brute du jeu, avant sagesse, bonus de groupe, défis et malus de niveau.</summary>
    /// <summary>Statistiques de combat d'un monstre pour le grade le plus proche du niveau demandé (0 = plus bas) : PV et résistances % par élément. null si inconnu.</summary>
    public (int Level, int Life, IReadOnlyDictionary<int, int> ResistByElement)? MonsterGradeStats(int monsterId, int level)
    {
        try
        {
            if (!IsKnownTable("MonsterData") || Scalar("select grades from MonsterData where id = $0", monsterId) is not string json) return null;
            using var doc = JsonDocument.Parse(json);
            JsonElement? best = null; int bestGap = int.MaxValue;
            foreach (var g in doc.RootElement.EnumerateArray())
                if (g.TryGetProperty("level", out var lv) && lv.TryGetInt32(out int l))
                {
                    int gap = level <= 0 ? l : Math.Abs(l - level);
                    if (gap < bestGap) { bestGap = gap; best = g; }
                }
            if (best is not { } b) return null;
            int Get(string name) => b.TryGetProperty(name, out var v) && v.TryGetInt32(out int x) ? x : 0;
            // éléments du jeu (effectElement) : 2 feu, 3 eau, 4 air, 5 terre, 1 neutre
            var res = new Dictionary<int, int> { [1] = Get("reductionNeutral"), [2] = Get("reductionFire"), [3] = Get("reductionWater"), [4] = Get("reductionAir"), [5] = Get("reductionEarth") };
            return (Get("level"), Get("lifePoints"), res);
        }
        catch { return null; }
    }

    public IReadOnlyDictionary<int, long>? MonsterGradeXp(int monsterId)
    {
        try
        {
            if (!IsKnownTable("MonsterData")) return null;
            if (Scalar("select grades from MonsterData where id = $0", monsterId) is not string json) return null;
            using var doc = JsonDocument.Parse(json);
            var map = new Dictionary<int, long>();
            foreach (var g in doc.RootElement.EnumerateArray())
                if (g.TryGetProperty("level", out var lv) && g.TryGetProperty("gradeXp", out var xp) && lv.TryGetInt32(out int level) && xp.TryGetInt64(out long x) && x > 0)
                    map[level] = x;
            return map.Count > 0 ? map : null;
        }
        catch { return null; }   // base illisible : XP inconnue, jamais inventée
    }
}
