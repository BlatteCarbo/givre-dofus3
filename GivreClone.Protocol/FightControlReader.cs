using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

public sealed record ControlledFighter(long MasterId, long ActorId, IReadOnlyDictionary<int, int> Spells);

public static class FightControlReader
{
    public static ControlledFighter? Read(IReadOnlyList<ProtoField> tree, Func<string, string, int?> FN)
    {
        long Int(int? number) => number is int n
            ? unchecked((long)(tree.FirstOrDefault(x => x.Field == n && x.Wire == WireType.Varint)?.Varint ?? 0)) : 0;
        long master = Int(FN("FightControlEvent", "fvxb")), actor = Int(FN("FightControlEvent", "fvwx"));
        if (master == 0) return null;
        // No slave_id means the server restored the master's context.
        if (actor == 0) actor = master;
        var spells = new Dictionary<int, int>();
        if (FN("FightControlEvent", "spells") is int list
            && FN("SpellItem", "spell_id") is int idField
            && FN("SpellItem", "spell_level") is int gradeField)
            foreach (var item in tree.Where(x => x.Field == list && x.Message is not null))
            {
                int id = (int)(item.Message!.FirstOrDefault(x => x.Field == idField && x.Wire == WireType.Varint)?.Varint ?? 0);
                int grade = (int)(item.Message!.FirstOrDefault(x => x.Field == gradeField && x.Wire == WireType.Varint)?.Varint ?? 1);
                if (id > 0) spells[id] = Math.Max(1, grade);
            }
        return new(master, actor, spells);
    }
}
