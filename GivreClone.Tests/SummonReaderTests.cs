using System.Collections.Generic;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Report du 05/10 : « sur le Sacrieur, l'IA attaque les invocations des alliés » (l'épée). Le lecteur ne voyait que
/// l'action 181 et la 1re invocation, sans équipe ni invocateur : une invocation d'un coéquipier tombait dans le repli
/// « id négatif = monstre ». GameActionFightEvent.summons porte pourtant l'équipe de spawn et l'invocateur, sous deux formes.
/// Trames encodées avec les numéros du mapping v5 (data/fields.json), lues par le lecteur réel branché sur le catalogue.
/// </summary>
public class SummonReaderTests
{
    private const long Ally = 41234567890, Me = 623456789012;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string msg, string field) => Catalog.FieldNumber(msg, field)
        ?? throw new Xunit.Sdk.XunitException($"Mapping 3.7 absent : {msg}.{field}");

    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var b = new byte[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }
    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts) { var b = Cat(parts); return Cat(Tag(field, 2), Var((ulong)b.Length), b); }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    // Messages synthétiques bâtis depuis data/fields.json ; aucun payload réseau attesté.
    private static byte[] ActorSummon(long id, int cell, long summoner, int? team) =>
        M(F("SummonsByActor", "summons"),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "fighter"),
                    team is int t ? M(F("FightFighterInformation", "spawn_information"),
                        I(F("SpawnInformation", "alive"), 1),
                        t == 0 ? Array.Empty<byte>() : I(F("SpawnInformation", "team"), t)) : Array.Empty<byte>(),
                    M(F("FightFighterInformation", "stats"),
                        I(F("FightCharacteristics", "summoned"), 1),
                        I(F("FightCharacteristics", "summoner"), summoner)))),
            I(F("ActorPositionInformation", "actor_id"), id),
            M(F("ActorPositionInformation", "disposition"), I(F("EntityDisposition", "cell_id"), cell)));

    [Fact]
    public void Ally_summon_of_any_action_type_carries_its_team_and_summoner()
    {
        // épée d'un Sacrieur allié : action ≠ 181, équipe 0 (omise en proto3, spawn présent), invocateur = l'allié
        var tree = Parse(I(F("GameActionFightEvent", "source_id"), Ally),
            I(F("GameActionFightEvent", "action_id"), 1097),
            M(F("GameActionFightEvent", "summons"),
                M(F("Summons", "summons_by_actor"), ActorSummon(-5, 300, Ally, team: 0))));
        var s = Assert.Single(FightStatsReader.ReadSummons(tree));
        Assert.Equal(-5, s.Id);
        Assert.Equal(300, s.Cell);
        Assert.Equal(0, s.Team);
        Assert.Equal(Ally, s.Summoner);
    }

    [Fact]
    public void Every_summon_is_read_not_only_the_first()
    {
        var tree = Parse(I(F("GameActionFightEvent", "source_id"), Me),
            I(F("GameActionFightEvent", "action_id"), 181),
            M(F("GameActionFightEvent", "summons"),
                M(F("Summons", "summons_by_actor"),
                    ActorSummon(-5, 300, Me, team: 1), ActorSummon(-6, 301, Me, team: 1))));
        var all = FightStatsReader.ReadSummons(tree);
        Assert.Equal(new[] { -5L, -6L }, all.ConvertAll(x => x.Id));
        Assert.All(all, x => Assert.Equal(1, x.Team));
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, -1)]
    [InlineData(true, false, -1)]
    public void Omitted_zero_cell_requires_a_present_disposition(bool grouped, bool hasDisposition, int expected)
    {
        var position = Cat(I(F("ActorPositionInformation", "actor_id"), -9),
            hasDisposition ? M(F("ActorPositionInformation", "disposition")) : Array.Empty<byte>());
        var summons = grouped
            ? M(F("Summons", "summons_by_context_information"),
                M(F("SummonsByContextInformation", "summons"),
                    M(F("SummonContextInformation", "summons"),
                        M(F("SpawnInformation", "position"), position))))
            : M(F("Summons", "summons_by_actor"),
                M(F("SummonsByActor", "summons"), position));

        var result = Assert.Single(FightStatsReader.ReadSummons(Parse(
            M(F("GameActionFightEvent", "summons"), summons))));

        Assert.Equal(-9, result.Id);
        Assert.Equal(expected, result.Cell);
    }

    [Fact]
    public void Grouped_summons_share_their_summoner_and_keep_their_own_team()
    {
        byte[] Spawn(long id, int cell) =>
            M(F("SummonContextInformation", "summons"),
                M(F("SpawnInformation", "position"),
                    I(F("ActorPositionInformation", "actor_id"), id),
                    M(F("ActorPositionInformation", "disposition"),
                        I(F("EntityDisposition", "cell_id"), cell))),
                I(F("SpawnInformation", "team"), 1));
        var tree = Parse(I(F("GameActionFightEvent", "source_id"), -1),
            I(F("GameActionFightEvent", "action_id"), 1008),
            M(F("GameActionFightEvent", "summons"),
                M(F("Summons", "summons_by_context_information"),
                    M(F("SummonsByContextInformation", "summons"),
                        Spawn(-7, 120), Spawn(-8, 121),
                        M(F("SummonContextInformation", "characteristics"),
                            I(F("FightCharacteristics", "summoner"), -1))))));
        var all = FightStatsReader.ReadSummons(tree);
        Assert.Equal(2, all.Count);
        Assert.All(all, x => { Assert.Equal(1, x.Team); Assert.Equal(-1, x.Summoner); });
        Assert.Equal(new[] { 120, 121 }, all.ConvertAll(x => x.Cell));
    }

    [Fact]
    public void Missing_spawn_or_summoner_stays_unknown_and_other_actions_read_nothing()
    {
        var bare = Parse(I(F("GameActionFightEvent", "source_id"), Ally),
            I(F("GameActionFightEvent", "action_id"), 181),
            M(F("GameActionFightEvent", "summons"),
                M(F("Summons", "summons_by_actor"),
                    M(F("SummonsByActor", "summons"),
                        I(F("ActorPositionInformation", "actor_id"), -9),
                        M(F("ActorPositionInformation", "disposition"),
                            I(F("EntityDisposition", "cell_id"), 50))))));
        var s = Assert.Single(FightStatsReader.ReadSummons(bare));
        Assert.Null(s.Team);       // pas d'info de spawn : équipe inconnue (le moteur se rabat sur l'invocateur)
        Assert.Null(s.Summoner);
        Assert.Empty(FightStatsReader.ReadSummons(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            I(F("GameActionFightEvent", "action_id"), 5),
            M(F("GameActionFightEvent", "slide"),
                I(F("Slide", "start_cell"), 425), I(F("Slide", "end_cell"), 396),
                I(F("Slide", "target_id"), -1)))));   // poussée
    }
}
