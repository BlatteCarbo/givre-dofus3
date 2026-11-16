extern alias F06App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Défis : catalogue (règles des 43 défis génériques, acceptation, chance de réussite), choix du défi parmi les
/// propositions, et lecteurs ajoutés (niveau d'un monstre en combat, attaquants d'une cible de défi) contre le VRAI catalogue.</summary>
public class ChallengeTests
{
    private static readonly ChallengeContext Solo = new(Team: false, Melee: false, Rules: true, LevelsKnown: true);

    // =============================================================== catalogue
    [Fact]
    public void Every_generic_challenge_has_its_own_rule()
    {
        var ids = ChallengeCatalog.GenericIds;
        Assert.Equal(43, ids.Count);
        var rules = ids.Select(ChallengeCatalog.RuleOfGeneric).ToList();
        Assert.DoesNotContain(ChallengeRule.Unknown, rules);
        Assert.Equal(rules.Count, rules.Distinct().Count());
        foreach (var id in ids) Assert.Equal(id, ChallengeCatalog.GenericIdOf(ChallengeCatalog.RuleOfGeneric(id)));
        // Ids vérifiés dans les données du jeu (challengesdataroot, catégorie 1).
        Assert.Equal(ChallengeRule.Statue, ChallengeCatalog.RuleOfGeneric(2));
        Assert.Equal(ChallengeRule.Versatile, ChallengeCatalog.RuleOfGeneric(6));
        Assert.Equal(ChallengeRule.Prudent, ChallengeCatalog.RuleOfGeneric(40));
        Assert.Equal(ChallengeRule.Pins, ChallengeCatalog.RuleOfGeneric(970));
        Assert.Equal(ChallengeRule.Unknown, ChallengeCatalog.RuleOfGeneric(451));   // défi de donjon
    }

    [Fact]
    public void Defaults_accept_all_challenges_and_explicit_exclusions_win()
    {
        var none = new List<int>();
        Assert.True(ChallengeCatalog.Accepted(ChallengeRule.Statue, none, none));
        Assert.True(ChallengeCatalog.Accepted(ChallengeRule.Survivant, none, none));
        foreach (int id in ChallengeCatalog.GenericIds)
            Assert.True(ChallengeCatalog.Accepted(ChallengeCatalog.RuleOfGeneric(id), none, none));
        Assert.True(ChallengeCatalog.Accepted(ChallengeRule.Unknown, none, none));       // défi de donjon : rien à perdre
        Assert.False(ChallengeCatalog.Accepted(ChallengeRule.Statue, none, new List<int> { 2 }));
        Assert.True(ChallengeCatalog.Accepted(ChallengeRule.Barbare, new List<int> { 9 }, none));
    }

    [Fact]
    public void Enforced_rules_are_less_likely_without_ai_adaptation()
    {
        var off = Solo with { Rules = false };
        Assert.Equal(ChallengeCatalog.Likelihood(ChallengeRule.Versatile, Solo) / 2, ChallengeCatalog.Likelihood(ChallengeRule.Versatile, off));
        Assert.Equal(ChallengeCatalog.Likelihood(ChallengeRule.Survivant, Solo), ChallengeCatalog.Likelihood(ChallengeRule.Survivant, off));
        Assert.True(ChallengeCatalog.Likelihood(ChallengeRule.Hardi, Solo with { Melee = true }) > ChallengeCatalog.Likelihood(ChallengeRule.Hardi, Solo));
        Assert.True(ChallengeCatalog.Likelihood(ChallengeRule.Cruel, Solo) > ChallengeCatalog.Likelihood(ChallengeRule.Cruel, Solo with { LevelsKnown = false }));
    }

    // =============================================================== choix
    private static ChallengeOffer O(int id, int bonus) => new(id, ChallengeCatalog.RuleOfGeneric(id), bonus);

    [Fact]
    public void Pick_maximises_expected_bonus_among_accepted()
    {
        // Capture réelle : Prudent (40) à 65 % contre « J'en ai fait un pin's » (970) à 90 % -> Prudent (bien plus sûr).
        Assert.Equal(40, ChallengeCatalog.Pick(new[] { O(40, 65), O(970, 90) }, _ => true, Solo));
        // Deux défis tenus par l'IA : le mieux payé à chance proche l'emporte (Focus 85×60 > Versatile 90×40).
        Assert.Equal(31, ChallengeCatalog.Pick(new[] { O(6, 40), O(31, 60) }, _ => true, Solo));
    }

    [Fact]
    public void Pick_skips_refused_and_returns_null_when_none_accepted()
    {
        Assert.Equal(970, ChallengeCatalog.Pick(new[] { O(40, 65), O(970, 90) }, o => o.Id != 40, Solo));
        Assert.Null(ChallengeCatalog.Pick(new[] { O(9, 50), O(19, 80) }, o => ChallengeCatalog.Accepted(o.Rule, new List<int>(), new List<int> { 9, 19 }), Solo));
        Assert.Null(ChallengeCatalog.Pick(Array.Empty<ChallengeOffer>(), _ => true, Solo));
    }

    [Fact]
    public void Pick_prefers_the_safer_on_equal_score_and_handles_missing_bonus()
    {
        // Bonus inconnu (0) : on départage à la seule chance de réussite.
        Assert.Equal(6, ChallengeCatalog.Pick(new[] { O(2, 0), O(6, 0) }, _ => true, Solo));   // Versatile 90 > Statue 80
    }

    private static readonly Type PlanType = typeof(Launch).GetNestedType("ChallengePlan", BindingFlags.NonPublic)!;
    private static readonly MethodInfo HpGate = typeof(Launch).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(m => m.Name == "ApplyChallengeHpThreshold");

    private static object? ApplyHp(object plan, int threshold, int? hp, ref bool abandoned, out bool log)
    {
        object?[] args = { plan, threshold, hp, abandoned, false };
        object? result = HpGate.Invoke(null, args);
        abandoned = (bool)args[3]!;
        log = (bool)args[4]!;
        return result;
    }

    private static object RestrictedPlan()
    {
        var plan = Activator.CreateInstance(PlanType, nonPublic: true)!;
        foreach (string flag in new[] { "NoMove", "OncePerTurn", "OncePerFight", "ElementLock" })
            PlanType.GetField(flag)!.SetValue(plan, true);
        PlanType.GetField("Only")!.SetValue(plan, new HashSet<long> { -1 });
        PlanType.GetField("Avoid")!.SetValue(plan, new HashSet<long> { -2 });
        ((List<Func<int, bool>>)PlanType.GetField("EndCell")!.GetValue(plan)!).Add(c => c == 118);
        return plan;
    }

    [Theory]
    [InlineData(51)]
    [InlineData(50)]
    public void Challenge_constraints_remain_at_or_above_threshold(int hp)
    {
        var plan = RestrictedPlan();
        bool abandoned = false;
        var applied = ApplyHp(plan, 50, hp, ref abandoned, out bool log);
        Assert.Same(plan, applied);
        Assert.False(abandoned);
        Assert.False(log);
        foreach (string flag in new[] { "NoMove", "OncePerTurn", "OncePerFight", "ElementLock" })
            Assert.True((bool)PlanType.GetField(flag)!.GetValue(applied)!);
        Assert.False((bool)PlanType.GetMethod("TargetOk")!.Invoke(applied, new object[] { -2L })!);
        Assert.False((bool)PlanType.GetMethod("EndCellOk")!.Invoke(applied, new object[] { 132 })!);
    }

    [Fact]
    public void Challenge_constraints_drop_below_threshold_and_log_only_once_even_after_healing()
    {
        bool abandoned = false;
        Assert.Null(ApplyHp(RestrictedPlan(), 50, 49, ref abandoned, out bool firstLog));
        Assert.True(abandoned);
        Assert.True(firstLog);
        Assert.Null(ApplyHp(RestrictedPlan(), 50, 80, ref abandoned, out bool healedLog));
        Assert.False(healedLog);
        Assert.Null(ApplyHp(RestrictedPlan(), 50, 10, ref abandoned, out bool laterLog));
        Assert.False(laterLog);
        bool nextFightAbandoned = false;
        var nextPlan = RestrictedPlan();
        Assert.Same(nextPlan, ApplyHp(nextPlan, 50, 80, ref nextFightAbandoned, out _));
    }

    [Fact]
    public void Disabled_threshold_and_unknown_hp_never_abandon_challenges()
    {
        var plan = RestrictedPlan();
        bool abandoned = false;
        Assert.Same(plan, ApplyHp(plan, 0, 0, ref abandoned, out bool disabledLog));
        Assert.False(disabledLog);
        Assert.Same(plan, ApplyHp(plan, 50, null, ref abandoned, out bool unknownLog));
        Assert.False(unknownLog);
        Assert.False(abandoned);
    }

    [Fact]
    public void Challenge_threshold_roundtrips_per_character_and_defaults_to_disabled()
    {
        var profiles = new F06App::GivreClone.App.Services.CombatProfilesFile();
        profiles.Profiles["Bastionbot"] = new F06App::GivreClone.App.Services.CombatProfile { ChallengeAbandonHp = 37 };
        profiles.Profiles["Other"] = new F06App::GivreClone.App.Services.CombatProfile();
        var context = (System.Text.Json.Serialization.JsonSerializerContext)typeof(Launch).Assembly
            .GetType("GivreClone.App.Services.AppJsonContext")!.GetProperty("Default")!.GetValue(null)!;
        var fileType = profiles.GetType();
        string json = JsonSerializer.Serialize(profiles, fileType, context);
        var restored = (F06App::GivreClone.App.Services.CombatProfilesFile)JsonSerializer.Deserialize(json, fileType, context)!;
        Assert.Equal(37, restored.Profiles["Bastionbot"].ChallengeAbandonHp);
        Assert.Equal(37, restored.Profiles["Bastionbot"].Clone().ChallengeAbandonHp);
        Assert.Equal(0, restored.Profiles["Other"].ChallengeAbandonHp);
        var legacy = (F06App::GivreClone.App.Services.CombatProfilesFile)JsonSerializer.Deserialize("{\"Profiles\":{\"Legacy\":{}}}",
            fileType, context)!;
        Assert.Equal(0, legacy.Profiles["Legacy"].ChallengeAbandonHp);
    }

    // =============================================================== lecteurs (vrai catalogue data/)
    private static byte[] Cat(params byte[][] parts)
    {
        var b = new byte[parts.Sum(p => p.Length)];
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
    private static byte[] Packed(int field, params long[] vs) { var b = Cat(vs.Select(v => Var(unchecked((ulong)v))).ToArray()); return Cat(Tag(field, 2), Var((ulong)b.Length), b); }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
    private static int F(string msg, string field) => Mapping.FieldNumber(msg, field)
        ?? throw new System.InvalidOperationException($"Mapping missing {msg}.{field}");

    [Fact]
    public void Monster_level_is_read_by_name_from_the_fighter_show_event()
    {
        var tree = Parse(M(F("FightFighterShowEvent", "information"),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "fighter"),
                    M(F("FightFighterInformation", "ai_fighter"),
                        M(F("AIFighterInformation", "monster_fighter_information"),
                            I(F("MonsterFighter", "creature_level"), 42),
                            I(F("MonsterFighter", "monster_gid"), 31))))),
            I(F("ActorPositionInformation", "actor_id"), -3)));
        Assert.Equal((-3L, 42), FightStatsReader.ReadShowFighterLevel(tree));
        // Joueur (named_fighter, pas d'ai_fighter) : pas de niveau de monstre.
        Assert.Null(FightStatsReader.ReadShowFighterLevel(Parse(M(F("FightFighterShowEvent", "information"),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "fighter"),
                    M(F("FightFighterInformation", "named_fighter"), I(1, 1)))),
            I(F("ActorPositionInformation", "actor_id"), 77)))));
    }

    [Fact]
    public void Challenge_target_attackers_read_packed_and_unpacked()
    {
        int targets = F("Challenge", "targets"), challengeId = F("Challenge", "challenge_id");
        int targetId = F("ChallengeTarget", "target_id"), targetCell = F("ChallengeTarget", "target_cell");
        int attackers = F("ChallengeTarget", "attackers_id");
        var packed = FightPrepReader.ReadChallenge(Parse(M(targets,
            I(targetId, -7), I(targetCell, 200), Packed(attackers, 11, 12)), I(challengeId, 45)));
        Assert.NotNull(packed);
        Assert.Equal(new long[] { 11, 12 }, packed!.Targets[0].Attackers);
        var unpacked = FightPrepReader.ReadChallenge(Parse(M(targets,
            I(targetId, -7), I(attackers, 11), I(attackers, 12)), I(challengeId, 45)));
        Assert.Equal(new long[] { 11, 12 }, unpacked!.Targets[0].Attackers);
        var none = FightPrepReader.ReadChallenge(Parse(M(targets, I(targetId, -7)), I(challengeId, 45)));
        Assert.Empty(none!.Targets[0].Attackers!);
    }
}
