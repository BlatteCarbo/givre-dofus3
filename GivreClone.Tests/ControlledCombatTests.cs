extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using GameNames = F06App::GivreClone.App.Services.GameNameResolver;
using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class ControlledCombatTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo Field(string name) => typeof(LaunchService).GetField(name, Private)!;
    private static object? Call(LaunchService s, string name, params object[] args) => typeof(LaunchService).GetMethod(name, Private)!.Invoke(s, args);
    private static IDictionary Map(LaunchService s, string name) => (IDictionary)Field(name).GetValue(s)!;
    private static LaunchService Service()
    {
        var s = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_controlledCombat", "_controlledHistory", "_engineControlled", "_serverTurnActor",
            "_controlledTurnStarted", "_automaticTurnStop", "_myTurnOpen", "_turnEpoch", "_charId", "_inFight", "_allies", "_fighters",
            "_charSpells", "_charSpellLevels", "_castLedgers", "_fighterStats", "_selfChars", "_selfRange", "_confirmedEnemies", "_fighterTeam",
            "_fightMonsters", "_treasureEncounters", "_treasureAttacked", "_treasure", "_fightTurnNo", "_legendaryTreasure",
            "_fightId", "_teamIntents", "_teamIntentRound", "_teamIntentFightKeys", "_selfInfo", "_characters" })
            Field(name).SetValue(s, Activator.CreateInstance(Field(name).FieldType));
        Field("_gameNames").SetValue(s, new GameNames());
        Map(s, "_charId")[(uint)1] = 999L;
        Map(s, "_inFight")[(uint)1] = true;
        Map(s, "_fighters")[(uint)1] = new List<(int Cell, long ActorId)> { (100, 999), (101, -20000) };
        Map(s, "_charSpells")[(uint)1] = new[] { 111 };
        Map(s, "_charSpellLevels")[(uint)1] = new Dictionary<int, int> { [111] = 2 };
        Map(s, "_fighterStats")[(uint)1] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(999, new(999, 12, 6, 100, 100, 12, 6)),
            new KeyValuePair<long, FighterStats>(-20000, new(-20000, 4, 3, 50, 50, 4, 3))
        });
        return s;
    }

    [Fact]
    public void Legendary_hunt_uses_ordinary_combat_instead_of_the_four_guard_policy()
    {
        var s = Service();
        SeedTreasure(s);
        Map(s, "_legendaryTreasure")[(uint)1] = (byte)0;
        Assert.Equal(false, Call(s, "IsTreasureFight", (uint)1));
    }

    [Fact]
    public void Reconnected_legendary_hunt_does_not_require_a_local_launch_marker()
    {
        var s = Service();
        Map(s, "_treasure")[(uint)1] = new LaunchService.TreasureHunt(2, 123, 1, 0, 1, 4,
            Array.Empty<LaunchService.TreasureFlag>(),
            new[] { new LaunchService.TreasureStep("fight", 0, 0, 0, 0) }, 1);

        Assert.Equal(false, Call(s, "IsTreasureFight", (uint)1));
    }

    [Fact]
    public void Treasure_target_follows_special_actor_after_swap_and_never_reuses_the_old_cell()
    {
        var s = Service();
        SeedTreasure(s);
        Assert.Equal(true, Call(s, "IsTreasureFight", (uint)1));
        Assert.Equal(new TreasureFightPolicy.Guard(-5, 104, "special"), Call(s, "TreasureTarget", (uint)1));
        Map(s, "_treasureAttacked")[(uint)1] = (-5L, 104, 0);
        Assert.Null(Call(s, "TreasureTarget", (uint)1));  // No movement event yet.
        Map(s, "_fighters")[(uint)1] = new List<(int Cell, long ActorId)> { (100, 999), (200, -1), (101, -2), (102, -3), (104, -4), (103, -5) };
        Assert.Equal(new TreasureFightPolicy.Guard(-5, 103, "special"), Call(s, "TreasureTarget", (uint)1));
        Assert.Empty(Map(s, "_treasureAttacked"));
    }

    [Fact]
    public void Treasure_guard_can_be_hit_again_on_the_next_confirmed_turn_without_a_swap()
    {
        var s = Service();
        SeedTreasure(s);
        Map(s, "_fightTurnNo")[(uint)1] = 1;
        Map(s, "_treasureAttacked")[(uint)1] = (-5L, 104, 1);
        Assert.Null(Call(s, "TreasureTarget", (uint)1));
        Map(s, "_fightTurnNo")[(uint)1] = 2;
        Assert.Equal(new TreasureFightPolicy.Guard(-5, 104, "special"), Call(s, "TreasureTarget", (uint)1));
    }

    [Fact]
    public void Treasure_missing_or_ambiguous_appearance_and_partial_teleport_do_not_select_any_target()
    {
        var s = Service();
        SeedTreasure(s);
        var monsters = (ConcurrentDictionary<long, FightMonster>)Map(s, "_fightMonsters")[(uint)1]!;
        monsters[-5] = monsters[-5] with { Appearance = null };
        Assert.Null(Call(s, "TreasureTarget", (uint)1));
        monsters[-5] = monsters[-5] with { Appearance = "normal" };
        Assert.Null(Call(s, "TreasureTarget", (uint)1));
        monsters[-5] = monsters[-5] with { Appearance = "special" };
        Map(s, "_fighters")[(uint)1] = new List<(int Cell, long ActorId)> { (100, 999), (200, -1), (101, -2), (102, -3), (103, -4), (103, -5) };
        Assert.Null(Call(s, "TreasureTarget", (uint)1));
    }

    [Fact]
    public void Ordinary_one_HP_losses_do_not_enable_treasure_mode()
    {
        var s = Service();
        Call(s, "ApplyObservedLifeLoss", (uint)1, new FightStatsReader.LifeLoss(-20000, 1, 0));
        Assert.Equal(false, Call(s, "IsTreasureFight", (uint)1));
    }

    private static void SeedTreasure(LaunchService s)
    {
        // Synthetic GIDs: production resolves the names from client bundles, not from these test numbers.
        var names = (GameNames)Field("_gameNames").GetValue(s)!;
        var cache = (ConcurrentDictionary<int, string>)typeof(GameNames).GetField("_cacheMonster", Private)!.GetValue(names)!;
        cache[1] = "Trésor"; cache[2] = "Vigicoffre";
        Map(s, "_fightMonsters")[(uint)1] = new ConcurrentDictionary<long, FightMonster>(new[]
        {
            new KeyValuePair<long, FightMonster>(-1, new(-1, 1, 1, "chest", null)),
            new KeyValuePair<long, FightMonster>(-2, new(-2, 2, 1, "normal", -1)),
            new KeyValuePair<long, FightMonster>(-3, new(-3, 2, 1, "normal", -1)),
            new KeyValuePair<long, FightMonster>(-4, new(-4, 2, 1, "normal", -1)),
            new KeyValuePair<long, FightMonster>(-5, new(-5, 2, 1, "special", -1)),
        });
        Map(s, "_fighters")[(uint)1] = new List<(int Cell, long ActorId)> { (100, 999), (200, -1), (101, -2), (102, -3), (103, -4), (104, -5) };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Control_uses_own_identity_stats_and_spellbook_only_inside_the_engine_context(bool characteristicsKnown)
    {
        var s = Service();
        Map(s, "_selfChars")[(uint)1] = new Dictionary<int, int> { [10] = 400 };
        Map(s, "_selfRange")[(uint)1] = 6;
        var stats = (ConcurrentDictionary<long, FighterStats>)Map(s, "_fighterStats")[(uint)1]!;
        if (characteristicsKnown)
            stats[-20000] = stats[-20000] with
            { Characteristics = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty.Add(10, 150).Add(FightStatsReader.IdRange, -2) };
        Call(s, "ObserveControlledFighter", (uint)1, new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 }));
        Assert.Equal(false, Call(s, "IsEnemy", (uint)1, -20000L));
        Assert.Equal(999L, Call(s, "SelfActorId", (uint)1));
        var context = Map(s, "_controlledCombat")[(uint)1]!;
        var local = Field("_engineControlled").GetValue(s)!;
        var value = local.GetType().GetProperty("Value")!;
        value.SetValue(local, context);
        try
        {
            Assert.Equal(-20000L, Call(s, "SelfActorId", (uint)1));
            Assert.Equal(4, s.SelfStats(1)!.Value.Pa);
            if (characteristicsKnown) Assert.Equal(150, s.SelfCharacteristics(1)[10]);
            else Assert.Empty(s.SelfCharacteristics(1));
            Assert.Equal(characteristicsKnown ? -2 : 0, s.SelfRange(1));
            Assert.Equal(new[] { 222 }, s.LearnedSpells(1));
            Assert.Equal(3, s.LearnedSpellLevel(1, 222));
            Assert.Empty(s.LearnedSpells(2));
            Assert.Equal(999, s.CharacterId(1));
        }
        finally { value.SetValue(local, null); }
        Assert.Equal(12, s.SelfStats(1)!.Value.Pa);
        Assert.Equal(400, s.SelfCharacteristics(1)[10]);
        Assert.Equal(6, s.SelfRange(1));
        Assert.Equal(new[] { 111 }, s.LearnedSpells(1));
        Assert.Equal(2, s.LearnedSpellLevel(1, 111));
    }

    [Fact]
    public void Another_master_and_out_of_fight_packets_do_not_grant_control()
    {
        var s = Service();
        Call(s, "ObserveControlledFighter", (uint)1, new ControlledFighter(998, -20000, new Dictionary<int, int>()));
        Assert.Empty(Map(s, "_controlledCombat"));
        Map(s, "_inFight")[(uint)1] = false;
        Call(s, "ObserveControlledFighter", (uint)1, new ControlledFighter(999, -20000, new Dictionary<int, int>()));
        Assert.Empty(Map(s, "_controlledCombat"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Master_and_server_controlled_companion_share_intentions_but_late_turn_cannot_publish(bool companionTurn, bool revoke)
    {
        var s = Service();
        Map(s, "_fightId")[1u] = 42L;
        Map(s, "_teamIntentRound")[1u] = 1;
        Call(s, "ObserveControlledFighter", 1u,
            new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 }));
        s.PublishTeamIntent(1, new(999, 1, Array.Empty<F06App::GivreClone.App.Services.Combat.TeamTargetIntent>(),
            100, Array.Empty<F06App::GivreClone.App.Services.Combat.TeamSupportIntent>(), 1));
        var local = Field("_engineControlled").GetValue(s)!;
        var value = local.GetType().GetProperty("Value")!;
        value.SetValue(local, Map(s, "_controlledCombat")[1u]);
        Map(s, "_serverTurnActor")[1u] = companionTurn ? -20000L : 999L;
        try
        {
            Assert.Equal(999, Assert.Single(s.ReadTeamView(1).Intents).SelfActorId);
            if (revoke) Call(s, "ReleaseControlledFighter", 1u);
            s.PublishTeamIntent(1, new(-20000, 1,
                Array.Empty<F06App::GivreClone.App.Services.Combat.TeamTargetIntent>(), 101,
                new[] { new F06App::GivreClone.App.Services.Combat.TeamSupportIntent(999, 20, 10) }, 2));
        }
        finally { value.SetValue(local, null); }

        var view = s.ReadTeamView(1);
        if (companionTurn && !revoke)
        {
            var intent = Assert.Single(view.Intents);
            Assert.Equal(-20000, intent.SelfActorId);
            Assert.Equal(101, intent.EndCell);
            var support = Assert.Single(intent.Support);
            Assert.Equal(999, support.AllyActorId);
            Assert.Equal(20, support.Heal);
            Assert.Equal(10, support.Shield);
        }
        else Assert.Empty(view.Intents);
    }

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 4, 3)]
    public void Companion_budget_requires_its_own_observed_resources(bool missing, int ap, int mp)
    {
        var service = Service();
        Call(service, "ObserveControlledFighter", 1u,
            new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 }));
        var stats = (ConcurrentDictionary<long, FighterStats>)Map(service, "_fighterStats")[1u]!;
        if (missing) stats.TryRemove(-20000, out _);
        else stats[-20000] = new(-20000, ap, mp, 50, 50, 4, 3);
        var local = Field("_engineControlled").GetValue(service)!;
        var value = local.GetType().GetProperty("Value")!;
        value.SetValue(local, Map(service, "_controlledCombat")[1u]);
        try
        {
            Assert.Equal((ap, mp), Call(service, "TurnStartBudget", 1u));
        }
        finally { value.SetValue(local, null); }
    }

    [Fact]
    public void Returning_to_master_keeps_companion_cooldowns_until_fight_reset()
    {
        var s = Service();
        var control = new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 });
        Call(s, "ObserveControlledFighter", (uint)1, control);
        var context = Map(s, "_controlledCombat")[(uint)1]!;
        var ledger = (F06App::GivreClone.App.Services.Combat.CastLedger)context.GetType().GetField("Ledger")!.GetValue(context)!;
        ledger.RecordCast(222, 100, 2);
        Call(s, "ObserveControlledFighter", (uint)1, new ControlledFighter(999, 999, new Dictionary<int, int>()));
        Assert.Empty(Map(s, "_controlledCombat"));
        Call(s, "ObserveControlledFighter", (uint)1, control);
        Assert.Same(context, Map(s, "_controlledCombat")[(uint)1]);
        Assert.True(ledger.IsRechargeActive(222, 3, 3, 0, 0));
        Call(s, "ResetControlledCombat", (uint)1);
        Assert.Empty(Map(s, "_controlledCombat"));
        Assert.Empty(Map(s, "_controlledHistory"));
    }

    [Theory]
    [InlineData(-20000, true, true)]
    [InlineData(-20000, false, false)]
    [InlineData(-20001, true, false)]
    [InlineData(999, true, false)]
    public void Server_turn_restores_only_a_previously_granted_living_companion(long actor, bool present, bool controlled)
    {
        var s = Service();
        Call(s, "ObserveControlledFighter", 1u,
            new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 }));
        Call(s, "ObserveControlledFighter", 1u,
            new ControlledFighter(999, 999, new Dictionary<int, int>()));
        if (!present) Map(s, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (100, 999) };

        Call(s, "ObserveServerFightTurn", 1u, actor);

        Assert.Equal(controlled, Call(s, "HasControlledTurn", 1u));
    }

    [Fact]
    public void Explicit_release_prevents_restoration_until_a_new_server_grant()
    {
        var s = Service();
        var control = new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 });
        Call(s, "ObserveControlledFighter", 1u, control);
        Call(s, "ObserveControlledFighter", 1u, new ControlledFighter(999, 999, new Dictionary<int, int>()));
        Call(s, "RevokeControlledFighter", 1u, 999L, -20000L);

        Call(s, "ObserveServerFightTurn", 1u, -20000L);

        Assert.Equal(false, Call(s, "HasControlledTurn", 1u));
        Call(s, "ObserveControlledFighter", 1u, control);
        Assert.Equal(true, Call(s, "HasControlledTurn", 1u));
    }

    [Fact]
    public void Replacing_automatic_turn_cancels_old_wait_and_a_late_cleanup_cannot_cancel_its_replacement()
    {
        var s = Service();
        using var old = new CancellationTokenSource();
        using var next = new CancellationTokenSource();
        var stops = (ConcurrentDictionary<uint, CancellationTokenSource>)Field("_automaticTurnStop").GetValue(s)!;
        stops[1] = old;
        Call(s, "CancelAutomaticFightTurn", (uint)1);
        Assert.True(old.IsCancellationRequested);
        stops[1] = next;
        Assert.False(stops.TryRemove(new KeyValuePair<uint, CancellationTokenSource>(1, old)));
        Assert.False(next.IsCancellationRequested);
        Call(s, "ResetControlledCombat", (uint)1);
        Assert.True(next.IsCancellationRequested);
    }
}
