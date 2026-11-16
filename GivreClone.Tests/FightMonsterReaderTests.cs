using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class FightMonsterReaderTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Catalog.FieldNumber(message, field)!.Value;
    private static ProtoField V(string m, string f, long value) => new() { Field = F(m, f), Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(string m, string f, params ProtoField[] children) => new() { Field = F(m, f), Wire = WireType.Len, Message = children.ToList() };
    private static ProtoField[] Look(int skin, int scale = 100) => new[]
    {
        V("EntityLook", "bones_id", 1), V("EntityLook", "skins", skin), V("EntityLook", "scales", scale)
    };
    private static ProtoField[] Position(long actor, int skin) => new[]
    {
        V("ActorPositionInformation", "actor_id", actor),
        M("ActorPositionInformation", "actor_information",
            M("ActorInformation", "look", Look(skin)),
            M("ActorInformation", "fighter",
                M("FightFighterInformation", "ai_fighter",
                    M("AIFighterInformation", "monster_fighter_information",
                        V("MonsterFighter", "monster_gid", 1234), V("MonsterFighter", "creature_grade", 2)))))
    };

    [Fact]
    public void Show_reads_monster_id_grade_and_structural_look()
    {
        var tree = new[] { M("FightFighterShowEvent", "information", Position(-5, 42)) };
        var monster = Assert.Single(FightMonsterReader.Read("FightFighterShowEvent", tree, Catalog.FieldNumber));
        Assert.Equal(-5, monster.ActorId);
        Assert.Equal(1234, monster.GenericId);
        Assert.Equal(2, monster.Grade);
        Assert.NotNull(monster.Appearance);
    }

    [Fact]
    public void Four_actor_summons_can_share_id_and_grade_but_differ_by_skin()
    {
        var summons = Enumerable.Range(0, 4).Select(i => M("SummonsByActor", "summons", Position(-i - 2, i == 3 ? 99 : 42))).ToArray();
        var tree = new[]
        {
            V("GameActionFightEvent", "source_id", -1),
            M("GameActionFightEvent", "summons", M("Summons", "summons_by_actor", summons))
        };
        var monsters = FightMonsterReader.Read("GameActionFightEvent", tree, Catalog.FieldNumber);
        Assert.Equal(4, monsters.Count);
        Assert.Single(monsters.Select(m => m.GenericId).Distinct());
        Assert.Single(monsters.Select(m => m.Grade).Distinct());
        Assert.All(monsters, m => Assert.Equal(-1, m.Summoner));
        Assert.Equal(monsters[0].Appearance, monsters[2].Appearance);
        Assert.NotEqual(monsters[0].Appearance, monsters[3].Appearance);
    }

    [Fact]
    public void Context_summons_use_shared_look_and_summoner_without_requiring_monster_gid()
    {
        var tree = new[]
        {
            V("GameActionFightEvent", "source_id", -1),
            M("GameActionFightEvent", "summons", M("Summons", "summons_by_context_information",
                M("SummonsByContextInformation", "summons",
                    M("SummonContextInformation", "look", Look(42)),
                    M("SummonContextInformation", "summons",
                        M("SpawnInformation", "position", V("ActorPositionInformation", "actor_id", -2))))))
        };
        var monster = Assert.Single(FightMonsterReader.Read("GameActionFightEvent", tree, Catalog.FieldNumber));
        Assert.Equal(-2, monster.ActorId);
        Assert.Equal(-1, monster.Summoner);
        Assert.NotNull(monster.Appearance);
    }

    [Fact]
    public void Observed_guard_look_change_preserves_the_distinct_skeleton_from_wire_field_1()
    {
        var change = FightMonsterReader.ReadLookChange("GameActionFightEvent", new[]
        {
            M("GameActionFightEvent", "change_look",
                V("ChangeLook", "target_id", -4),
                M("ChangeLook", "look",
                    new ProtoField { Field = 1, Wire = WireType.Varint, Varint = 2672 },
                    new ProtoField { Field = 4, Wire = WireType.Varint, Varint = 3 }))
        }, Catalog.FieldNumber);
        Assert.Equal(-4, change!.Value.Actor);
        Assert.StartsWith("2672:", change.Value.Appearance);
    }

    [Fact]
    public void Look_updates_ignore_growth_but_detect_skin_changes_and_unknown_look()
    {
        (long Actor, string? Appearance)? Read(int skin, int scale) => FightMonsterReader.ReadLookChange("GameActionFightEvent",
            new[] { M("GameActionFightEvent", "change_look", V("ChangeLook", "target_id", -5), M("ChangeLook", "look", Look(skin, scale))) },
            Catalog.FieldNumber);
        Assert.Equal(Read(42, 100), Read(42, 200));
        Assert.NotEqual(Read(42, 100), Read(99, 100));
        Assert.Null(FightMonsterReader.ReadLookChange("GameActionFightEvent", new[]
        {
            M("GameActionFightEvent", "change_look", V("ChangeLook", "target_id", -5), M("ChangeLook", "look"))
        }, Catalog.FieldNumber)!.Value.Appearance);
        Assert.Empty(FightMonsterReader.Read("FightFighterShowEvent", new[]
        {
            M("FightFighterShowEvent", "information", Position(-5, 42))
        }, (_, _) => null));
    }
}
