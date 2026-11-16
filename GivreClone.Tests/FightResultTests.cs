using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot C2 : bilan de combat (FightEndEvent -> FightResultReader), propositions et résultats de défi (FightPrepReader).
/// Le catalogue RÉEL est branché par TestSetup : les numéros de champ imbriqués sont résolus PAR NOM (fields.json),
/// pas seulement par le repli littéral.
/// </summary>
public class FightResultTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Catalog.FieldNumber(message, field)
        ?? throw new Xunit.Sdk.XunitException($"Mapping 3.7 absent : {message}.{field}");
    // --- encodeur protobuf minimal (même principe que Patch361112Tests) ---
    private static byte[] Cat(params byte[][] parts)
    {
        var l = new List<byte>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }
    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    // Fixture synthétique à partir des numéros du catalogue livré 3.7.11.11, pas une capture wire.
    private static IReadOnlyList<ProtoField> FightEnd(bool withOutcome = true, long kamas = 1250) => Parse(
        I(F("FightEndEvent", "duration"), 37),
        M(F("FightEndEvent", "results"),
            M(F("FightResultListEntry", "fighter_list_entry"),
                I(F("FighterListEntry", "fighter_id"), 123),
                M(F("FighterListEntry", "player_list_entry"),
                    I(F("PlayerListEntry", "level"), 50),
                    M(F("PlayerListEntry", "additional"),
                        M(F("FightResultAdditionalData", "experience_data"),
                            I(F("ExperienceData", "experience_fight_delta"), 4500),
                            I(F("ExperienceData", "gdbj"), 999999))))),
            M(F("FightResultListEntry", "rewards"),
                M(F("FightReward", "rewards"),
                    M(F("FightLoot", "objects"),
                        I(F("Object", "object_gid"), 287), I(F("Object", "quantity"), 3)),
                    M(F("FightLoot", "objects"),
                        I(F("Object", "object_gid"), 287), I(F("Object", "quantity"), 2))),
                M(F("FightReward", "rewards"),
                    M(F("FightLoot", "objects"), I(F("Object", "object_gid"), 1771))),
                kamas != 0 ? I(F("FightReward", "kamas"), kamas) : Array.Empty<byte>()),
            withOutcome ? I(F("FightResultListEntry", "outcome"), 2) : Array.Empty<byte>()),
        M(F("FightEndEvent", "results"),
            M(F("FightResultListEntry", "fighter_list_entry"), I(F("FighterListEntry", "fighter_id"), -5)),
            M(F("FightResultListEntry", "rewards"),
                M(F("FightReward", "rewards"),
                    M(F("FightLoot", "objects"), I(F("Object", "object_gid"), 42),
                        I(F("Object", "quantity"), 9))), I(F("FightReward", "kamas"), 77))));

    [Fact]
    public void Fight_result_reads_my_entry_xp_kamas_and_summed_loot()
    {
        var s = FightResultReader.Read(FightEnd(), 123);
        Assert.True(s.Found);
        Assert.Equal(2, s.Outcome);
        Assert.Equal(4500, s.Xp);
        Assert.Equal(1250, s.Kamas);              // FightReward.kamas (jusqu'au 04/10 : toujours null, lu sur FightLoot)
        Assert.Equal(37, s.ServerDuration);
        Assert.Equal(new[] { (287, 5), (1771, 1) }, s.Loot.Select(l => (l.Gid, l.Qty)).ToArray());
    }

    [Fact]
    public void Fight_result_omitted_kamas_is_zero()
    {
        Assert.Equal(0, FightResultReader.Read(FightEnd(kamas: 0), 123).Kamas);   // proto3 : 0 kamas = champ omis
    }

    [Fact]
    public void Fight_result_not_found_for_unknown_self()
    {
        var s = FightResultReader.Read(FightEnd(), 999);
        Assert.False(s.Found);
        Assert.Equal(-1, s.Outcome);
        Assert.Null(s.Xp);
        Assert.Empty(s.Loot);
        Assert.Equal(37, s.ServerDuration);
    }

    [Fact]
    public void Fight_result_omitted_outcome_is_a_loss()
    {
        var s = FightResultReader.Read(FightEnd(withOutcome: false), 123);
        Assert.True(s.Found);
        Assert.Equal(0, s.Outcome);               // proto3 : 0 (défaite) omis
    }

    [Fact]
    public void Challenge_proposals_are_fully_decoded()
    {
        var tree = Parse(
            M(F("ChallengeProposalEvent", "challenge_proposals"),
                I(F("Challenge", "challenge_id"), 40), I(F("Challenge", "drop_bonus"), 65),
                I(F("Challenge", "xp_bonus"), 65), I(F("Challenge", "state"), 2)),
            M(F("ChallengeProposalEvent", "challenge_proposals"),
                I(F("Challenge", "challenge_id"), 970),
                M(F("Challenge", "targets"), I(F("ChallengeTarget", "target_id"), -5),
                    I(F("ChallengeTarget", "target_cell"), 312))),
            I(F("ChallengeProposalEvent", "timer"), 30));
        var props = FightPrepReader.ReadChallengeProposals(tree);
        Assert.Equal(2, props.Count);
        Assert.Equal(40, props[0].Id); Assert.Equal(65, props[0].XpBonus); Assert.Equal(65, props[0].DropBonus); Assert.Equal(2, props[0].State);
        Assert.Empty(props[0].Targets);
        Assert.Equal(970, props[1].Id); Assert.Equal(0, props[1].XpBonus); Assert.Equal(0, props[1].DropBonus); Assert.Equal(0, props[1].State);
        Assert.Equal(new[] { (-5L, 312) }, props[1].Targets.ToArray());
        Assert.Equal(new[] { 40, 970 }, FightPrepReader.ReadChallengeProposalIds(tree));   // lecteur historique inchangé
    }

    [Fact]
    public void Challenge_result_success_and_proto3_failure()
    {
        Assert.Equal((40, true), FightPrepReader.ReadChallengeResult(Parse(
            I(F("ChallengeResultEvent", "challenge_id"), 40), I(F("ChallengeResultEvent", "success"), 1))));
        Assert.Equal((40, false), FightPrepReader.ReadChallengeResult(Parse(
            I(F("ChallengeResultEvent", "challenge_id"), 40))));   // success omis = échoué
        Assert.Null(FightPrepReader.ReadChallengeResult(Parse()));
    }
}
