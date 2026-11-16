extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using CombatProfile = F06App::GivreClone.App.Services.CombatProfile;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;

namespace GivreClone.Tests;

public class CompanionChallengeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(LaunchService).GetField(name, Private)!;
    private static IDictionary Map(LaunchService s, string name) => (IDictionary)Field(name).GetValue(s)!;
    private static object? Call(LaunchService s, string name, params object[] args) => typeof(LaunchService).GetMethod(name, Private)!.Invoke(s, args);

    private static LaunchService Service(int challenge)
    {
        var s = new LaunchService();
        Map(s, "_charId")[1u] = 999L;
        Map(s, "_inFight")[1u] = true;
        Map(s, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (100, 999), (101, -20000), (200, -1), (201, -2) };
        Call(s, "ForgetChallengeFight", 1u);
        Map(s, "_challenges")[1u] = new ConcurrentDictionary<int, FightPrepReader.FightChallenge>(new[]
        {
            new KeyValuePair<int, FightPrepReader.FightChallenge>(challenge, new(challenge, 0, 0, 0, Array.Empty<FightPrepReader.ChallengeTargetInfo>()))
        });
        Call(s, "ObserveControlledFighter", 1u, new ControlledFighter(999, -20000, new Dictionary<int, int>()));
        return s;
    }

    private static void Actor(LaunchService s, bool companion)
    {
        Call(s, "ObserveServerFightTurn", 1u, companion ? -20000L : 999L);
        var local = Field("_engineControlled").GetValue(s)!;
        local.GetType().GetProperty("Value")!.SetValue(local, companion ? Map(s, "_controlledCombat")[1u] : null);
    }

    private static object Plan(LaunchService s) => Call(s, "BuildChallengePlan", 1u, new CombatProfile { ChallengeRules = true },
        100, (Func<int, bool>)(_ => true), (Func<int, bool>)(_ => false), 0L)!;
    private static bool Target(object plan, long target) => (bool)plan.GetType().GetMethod("TargetOk")!.Invoke(plan, [target])!;

    private static void Damage(LaunchService s, long source, long target)
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string m, string f) => catalog.FieldNumber(m, f) ?? throw new InvalidOperationException($"Missing {m}.{f}");
        ProtoField I(int f, long value) => new() { Field = f, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        var packet = new DecodedPacket(1, false, "jwe", "GameActionFightEvent", new[]
        {
            I(F("GameActionFightEvent", "source_id"), source),
            new ProtoField { Field = F("GameActionFightEvent", "life_points_lost"), Wire = WireType.Len, Message = new()
                { I(F("LifePointsLost", "target_id"), target), I(F("LifePointsLost", "loss"), 10) } }
        }, Array.Empty<byte>());
        Call(s, "OnDecoded", packet);
    }

    [Fact]
    public void Duel_remembers_observed_companion_hits_without_server_attacker_updates()
    {
        var s = Service(45);
        Actor(s, true);
        var companion = Plan(s);
        Assert.True(Target(companion, -1));
        // Network callbacks have no engine AsyncLocal identity; the action source is authoritative.
        Actor(s, false);
        Damage(s, -20000, -1);
        var master = Plan(s);
        Assert.False(Target(master, -1));
        Assert.True(Target(master, -2));
        Damage(s, 999, -2);
        Actor(s, true);
        Assert.True(Target(companion, -1));
        Assert.False(Target(companion, -2));
        Actor(s, false);
    }

    [Fact]
    public void Duel_server_attackers_never_allow_an_enemy_shared_with_another_actor()
    {
        var s = Service(45);
        var challenges = (ConcurrentDictionary<int, FightPrepReader.FightChallenge>)Map(s, "_challenges")[1u]!;
        challenges[45] = challenges[45] with { Targets = new[]
            { new FightPrepReader.ChallengeTargetInfo(-1, 200, new long[] { 999, -20000 }) } };
        Actor(s, true);
        Assert.False(Target(Plan(s), -1));
        Actor(s, false);
        Assert.False(Target(Plan(s), -1));
    }

    [Fact]
    public void Duel_enemy_damage_does_not_claim_a_target_and_fight_reset_clears_claims()
    {
        var s = Service(45);
        Actor(s, false);
        Damage(s, -2, -1);
        Assert.True(Target(Plan(s), -1));
        Damage(s, -20000, -1);
        Assert.False(Target(Plan(s), -1));
        Call(s, "ForgetChallengeFight", 1u);
        Assert.True(Target(Plan(s), -1));
    }

    [Fact]
    public void Focus_keeps_the_first_observed_area_damage_target_for_the_companion()
    {
        var s = Service(31);
        Actor(s, false);
        Damage(s, 999, -2);
        Actor(s, true);

        var plan = Plan(s);

        Assert.True(Target(plan, -2));
        Assert.False(Target(plan, -1));
        Actor(s, false);
    }

    [Fact]
    public void Same_lineage_uses_the_controlled_actor_and_live_master_position()
    {
        var s = Service(964);
        Actor(s, true);
        var plan = Plan(s);
        bool End(int cell) => (bool)plan.GetType().GetMethod("EndCellOk")!.Invoke(plan, [cell])!;
        foreach (int cell in Enumerable.Range(0, 560))
            Assert.Equal(MapPathfinder.Aligned(cell, 100), End(cell));
        Map(s, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (316, 999), (315, -20000), (192, -1) };
        Assert.Equal(MapPathfinder.Aligned(315, 316), End(315));
        foreach (int cell in Enumerable.Range(0, 560))
            Assert.Equal(MapPathfinder.Aligned(cell, 316), End(cell));
        Actor(s, false);
    }

}
