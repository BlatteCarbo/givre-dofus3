using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

public sealed record FightMonster(long ActorId, int GenericId, int Grade, string? Appearance, long? Summoner);

/// <summary>Combat monster identity and structural appearance, resolved exclusively through the live field catalog.</summary>
public static class FightMonsterReader
{
    public static IReadOnlyList<FightMonster> Read(string name, IReadOnlyList<ProtoField> tree, Func<string, string, int?> field)
    {
        int F(string m, string f) => field(m, f) ?? -1;
        IReadOnlyList<ProtoField>? Sub(IReadOnlyList<ProtoField>? m, string type, string key) =>
            m?.FirstOrDefault(x => x.Field == F(type, key) && x.Message is not null)?.Message;
        long Int(IReadOnlyList<ProtoField>? m, string type, string key) =>
            m?.FirstOrDefault(x => x.Field == F(type, key) && x.Wire == WireType.Varint)?.AsSignedVarint ?? 0;
        FightMonster? Actor(IReadOnlyList<ProtoField> position, IReadOnlyList<ProtoField>? sharedLook = null, long owner = 0)
        {
            long id = Int(position, "ActorPositionInformation", "actor_id");
            if (id == 0) return null;
            var info = Sub(position, "ActorPositionInformation", "actor_information");
            var fighter = Sub(info, "ActorInformation", "fighter");
            var monster = Sub(Sub(fighter, "FightFighterInformation", "ai_fighter"), "AIFighterInformation", "monster_fighter_information");
            if (monster is null && id > 0) return null;
            long summoner = Int(Sub(fighter, "FightFighterInformation", "stats"), "FightCharacteristics", "summoner");
            var look = Sub(info, "ActorInformation", "look") ?? sharedLook;
            return new(id, (int)Int(monster, "MonsterFighter", "monster_gid"),
                (int)Int(monster, "MonsterFighter", "creature_grade"),
                look is null ? null : AppearanceKey(look, field), summoner != 0 ? summoner : owner != 0 ? owner : null);
        }
        var result = new List<FightMonster>();
        if (name is "FightFighterShowEvent" or "FightFighterRefreshEvent")
        {
            if (Sub(tree, name, "information") is { } pos && Actor(pos) is { } actor) result.Add(actor);
        }
        if (name != "GameActionFightEvent" || Sub(tree, name, "summons") is not { } summons) return result;
        long source = Int(tree, name, "source_id");
        if (Sub(summons, "Summons", "summons_by_actor") is { } byActor)
            foreach (var entry in byActor.Where(x => x.Field == F("SummonsByActor", "summons") && x.Message is not null))
                if (Actor(entry.Message!, owner: source) is { } actor) result.Add(actor);
        if (Sub(summons, "Summons", "summons_by_context_information") is { } byContext)
            foreach (var entry in byContext.Where(x => x.Field == F("SummonsByContextInformation", "summons") && x.Message is not null))
            {
                var context = entry.Message!;
                var look = Sub(context, "SummonContextInformation", "look");
                long owner = Int(Sub(context, "SummonContextInformation", "characteristics"), "FightCharacteristics", "summoner");
                foreach (var spawn in context.Where(x => x.Field == F("SummonContextInformation", "summons") && x.Message is not null))
                    if (Sub(spawn.Message, "SpawnInformation", "position") is { } pos
                        && Actor(pos, look, owner != 0 ? owner : source) is { } actor) result.Add(actor);
            }
        return result;
    }

    public static (long Actor, string? Appearance)? ReadLookChange(string name, IReadOnlyList<ProtoField> tree,
        Func<string, string, int?> field)
    {
        string type = name;
        var body = tree;
        if (name == "GameActionFightEvent")
        {
            body = tree.FirstOrDefault(x => x.Field == field(name, "change_look") && x.Message is not null)?.Message;
            type = "ChangeLook";
        }
        else if (name != "ContextRefreshEntityLookEvent") return null;
        if (body is null) return null;
        long id = body.FirstOrDefault(x => x.Field == field(type, type == "ChangeLook" ? "target_id" : "entity_id")
            && x.Wire == WireType.Varint)?.AsSignedVarint ?? 0;
        var look = body.FirstOrDefault(x => x.Field == field(type, "look") && x.Message is not null)?.Message;
        return id == 0 ? null : (id, look is null ? null : AppearanceKey(look, field));
    }

    // Legs are structural (bones/skins/sub-entities), not scale: the chest grows and cosmetic colors can vary.
    private static string? AppearanceKey(IReadOnlyList<ProtoField> tree, Func<string, string, int?> field)
    {
        if (EntityLookReader.Parse(tree, field) is not { } look) return null;
        var children = new List<string>();
        foreach (var child in tree.Where(x => x.Field == field("EntityLook", "sub_entities") && x.Message is not null))
        {
            var nested = child.Message!.FirstOrDefault(x => x.Field == field("SubEntityInformation", "sub_entity_look")
                && x.Message is not null)?.Message;
            if (nested is null || AppearanceKey(nested, field) is not { } key) return null;
            children.Add(key);
        }
        return $"{look.BonesId}:{string.Join(",", look.Skins)}[{string.Join(";", children.OrderBy(x => x, StringComparer.Ordinal))}]";
    }
}
