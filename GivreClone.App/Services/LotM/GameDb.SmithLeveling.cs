using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services.LotM;

public sealed record SmithLevelingSupport(long Gid, int Level);

public sealed partial class GameDb
{
    public IReadOnlyList<int> ModifyingSkills(int itemType)
    {
        lock (ExpeditionGate)
            return !IsKnownTable("SkillData_modifiableItemTypeIds_junction") ? Array.Empty<int>()
                : Query("""
                    select SkillData_id from SkillData_modifiableItemTypeIds_junction
                    where cast(target_id as integer) = $0 order by rowid limit 50
                    """, itemType).Select(row => I(row[0])).ToArray();
    }

    public bool IsGatheredItem(long gid)
    {
        if (gid <= 0) return false;
        lock (ExpeditionGate)
            return IsKnownTable("SkillData") && Query(
                "select 1 from SkillData where gatheredRessourceItem = $0 limit 1", gid).Any();
    }

    /// <summary>Equipment admitted by the installed profession's forgemagus skill, never pets or cosmetic slots.</summary>
    public IReadOnlyList<SmithLevelingSupport> SmithLevelingSupports(int jobId, int professionLevel)
    {
        if (jobId <= 0 || professionLevel <= 0) return Array.Empty<SmithLevelingSupport>();
        lock (ExpeditionGate)
        {
            if (!IsKnownTable("SkillData_modifiableItemTypeIds_junction")
                || !IsKnownTable("ItemTypeData") || !IsKnownTable("ItemSuperTypeData"))
                return Array.Empty<SmithLevelingSupport>();
            return Query("""
                select distinct i.id, i.level, st.possiblePositions
                from ItemData i
                join ItemTypeData t on t.id = i.typeId
                join ItemSuperTypeData st on st.id = t.superTypeId
                join SkillData_modifiableItemTypeIds_junction j on cast(j.target_id as integer) = t.id
                join SkillData s on s.id = cast(j.SkillData_id as integer)
                where s.parentJobId = $0 and s.isForgemagus = 1 and i.level between 1 and $1
                order by i.level desc, i.id
                """, jobId, professionLevel)
                .Where(row => SmithSupportSlot(S(row[2])))
                .Select(row => new SmithLevelingSupport(L(row[0]), I(row[1]))).ToArray();
        }
    }

    private static bool SmithSupportSlot(string positions)
    {
        using var json = JsonDocument.Parse(positions);
        return json.RootElement.ValueKind == JsonValueKind.Array
            && json.RootElement.EnumerateArray().Any(value => value.TryGetInt32(out int slot)
                && (slot is >= 0 and <= 7 || slot == 15));
    }
}
