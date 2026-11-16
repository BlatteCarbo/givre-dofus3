using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class AnomalyClockTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static long Epoch(DateTime d) => (long)(d - DateTime.UnixEpoch).TotalSeconds;

    [Fact]
    public void Epoch_seconds_milliseconds_and_relative_values_are_told_apart_by_magnitude()
    {
        long inTenMin = Epoch(Now) + 600;
        Assert.Equal(600, AnomalyClock.RemainingSeconds(inTenMin, Now, Now));
        Assert.Equal(600, AnomalyClock.RemainingSeconds(inTenMin * 1000, Now, Now));
        Assert.Equal(600 - 120, AnomalyClock.RemainingSeconds(600, Now.AddSeconds(-120), Now));   // relatif à la réception
    }

    [Fact]
    public void Remaining_never_goes_negative_and_unknown_is_zero()
    {
        Assert.Equal(0, AnomalyClock.RemainingSeconds(Epoch(Now) - 50, Now, Now));
        Assert.Equal(0, AnomalyClock.RemainingSeconds(0, Now, Now));
        Assert.Equal(0, AnomalyClock.RemainingSeconds(-5, Now, Now));
        Assert.Equal(0, AnomalyClock.RemainingSeconds(100, Now.AddSeconds(-500), Now));
    }

    [Fact]
    public void Closing_instant_in_epoch_seconds()
    {
        Assert.Equal(1_800_000_000, AnomalyClock.ClosingEpochSeconds(1_800_000_000, Now));
        Assert.Equal(1_800_000_000, AnomalyClock.ClosingEpochSeconds(1_800_000_000_000, Now));
        Assert.Equal(Epoch(Now) + 300, AnomalyClock.ClosingEpochSeconds(300, Now));
        Assert.Equal(0, AnomalyClock.ClosingEpochSeconds(0, Now));
    }
}

public class CriterionEvalTests
{
    private static CriterionResult Eval(string? c, int level = 50, int[]? done = null, int[]? active = null)
        => CriterionEval.Evaluate(c, level, q => done?.Contains(q) ?? false, q => active?.Contains(q) ?? false);

    [Fact]
    public void Empty_is_satisfied()
    {
        Assert.True(Eval("").Satisfied);
        Assert.True(Eval(null).Satisfied);
        Assert.True(Eval("  ").Satisfied);
    }

    [Theory]
    [InlineData("PL>4", 5, true)]
    [InlineData("PL>4", 4, false)]
    [InlineData("PL>=4", 4, true)]
    [InlineData("PL<10", 10, false)]
    [InlineData("PL<=10", 10, true)]
    [InlineData("PL=10", 10, true)]
    [InlineData("PL!=10", 10, false)]
    [InlineData("PL!=11", 10, true)]
    public void Level_comparisons(string c, int level, bool ok) => Assert.Equal(ok, Eval(c, level).Satisfied);

    [Fact]
    public void Quest_finished_and_active_terms()
    {
        Assert.True(Eval("Qf=230", done: new[] { 230 }).Satisfied);
        var r = Eval("Qf=230", done: new[] { 231 });
        Assert.False(r.Satisfied);
        Assert.Equal(new[] { "Qf=230" }, r.BlockedBy);
        Assert.True(Eval("Qa=5", active: new[] { 5 }).Satisfied);
        Assert.True(Eval("Qf!230", done: Array.Empty<int>()).Satisfied);
    }

    [Fact]
    public void Unknown_terms_are_reported_not_guessed_and_do_not_block()
    {
        var r = Eval("BT=1&PL>9&Qf=7", level: 5, done: new[] { 7 });
        Assert.False(r.Satisfied);                       // PL>9 bloque
        Assert.Equal(new[] { "PL>9" }, r.BlockedBy);
        Assert.Equal(new[] { "BT=1" }, r.NotChecked);    // jamais deviné
        Assert.True(Eval("BT=1").Satisfied);
        Assert.Equal(new[] { "BT=1" }, Eval("BT=1").NotChecked);
    }

    [Fact]
    public void Or_groups_and_parentheses_are_not_evaluated()
    {
        var r = Eval("PL>99|Qf=1");
        Assert.True(r.Satisfied);
        Assert.Equal(new[] { "PL>99|Qf=1" }, r.NotChecked);
        Assert.Equal(new[] { "(PL>99)" }, Eval("(PL>99)").NotChecked);
    }

    [Fact]
    public void Malformed_terms_are_not_checked()
    {
        Assert.Equal(new[] { "PL>x" }, Eval("PL>x").NotChecked);
        Assert.Equal(new[] { "garbage" }, Eval("garbage").NotChecked);
    }
}

public class KeepOnSalePlannerTests
{
    private static KeepMarket Market(long avg = 0, Dictionary<int, long>? market = null, Dictionary<int, long>? mine = null, Dictionary<int, int>? count = null)
        => new(avg, s => market?.GetValueOrDefault(s, -1) ?? -1, s => mine?.GetValueOrDefault(s, -1) ?? -1, s => count?.GetValueOrDefault(s, 0) ?? 0);

    [Theory]
    [InlineData(1, 1)] [InlineData(10, 10)] [InlineData(100, 100)] [InlineData(1000, 1000)]
    [InlineData(2, 10)] [InlineData(3, 100)] [InlineData(4, 1000)]
    public void Valid_sizes_and_givre_indexes(long raw, int size) => Assert.Equal(size, KeepOnSalePlanner.NormalizeSize(raw));

    [Theory] [InlineData(0)] [InlineData(5)] [InlineData(7)] [InlineData(50)] [InlineData(-1)] [InlineData(10000)]
    public void Invalid_sizes(long raw) => Assert.Null(KeepOnSalePlanner.NormalizeSize(raw));

    [Fact]
    public void Explicit_price_wins_and_min_price_is_a_floor_on_top()
    {
        var p = KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 2, 4000, null) }, 1, Market(avg: 10, market: new() { [100] = 900 }));
        Assert.Equal(new[] { new KeepStep(100, 4000), new KeepStep(100, 4000) }, p.Steps);
        Assert.Null(p.Reason);
        var floored = KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 1, 4000, 4500) }, 1, Market());
        Assert.Equal(4500, floored.Steps.Single().Price);
    }

    [Fact]
    public void Undercuts_the_market_but_never_below_half_the_average_and_never_below_min_price()
    {
        var m = Market(avg: 100, market: new() { [10] = 800 });
        Assert.Equal(799, KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, null) }, 1, m).Steps.Single().Price);
        Assert.Equal(790, KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, null) }, 10, m).Steps.Single().Price);
        // marché très bas : plancher = moitié du prix moyen × taille = 100 × 10 / 2 = 500
        var low = Market(avg: 100, market: new() { [10] = 300 });
        Assert.Equal(500, KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, null) }, 1, low).Steps.Single().Price);
        // minPrice plus haut que tout
        Assert.Equal(900, KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, 900) }, 1, m).Steps.Single().Price);
    }

    [Fact]
    public void Aligns_with_my_own_lot_when_it_is_already_the_cheapest_of_its_size()
    {
        var m = Market(avg: 100, market: new() { [100] = 5000 }, mine: new() { [100] = 4800 });
        Assert.Equal(4800, KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 2, null, null) }, 1, m).Steps[0].Price);
        var beaten = Market(avg: 10, market: new() { [100] = 4000 }, mine: new() { [100] = 4800 });   // quelqu'un est moins cher : on sous-cote (plancher moitié de la moyenne = 500)
        Assert.Equal(3999, KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 1, null, null) }, 1, beaten).Steps.Single().Price);
        // …mais le plancher « moitié du prix moyen × taille » l'emporte quand le marché est trop bas
        var floorWins = Market(avg: 100, market: new() { [100] = 4000 }, mine: new() { [100] = 4800 });
        Assert.Equal(5000, KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 1, null, null) }, 1, floorWins).Steps.Single().Price);
    }

    [Fact]
    public void Without_competitor_uses_average_times_size_and_without_any_data_gives_noprice()
    {
        Assert.Equal(1000, KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, null) }, 1, Market(avg: 100)).Steps.Single().Price);
        var none = KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, null, null) }, 1, Market());
        Assert.Empty(none.Steps);
        Assert.Equal("noprice", none.Reason);
    }

    [Fact]
    public void Only_the_missing_lots_are_planned_largest_sizes_first()
    {
        var m = Market(avg: 10, count: new() { [100] = 1, [1000] = 1 });
        var p = KeepOnSalePlanner.Plan(new[] { new KeepSize(1, 2, 20, null), new KeepSize(100, 3, 900, null), new KeepSize(1000, 1, 9000, null) }, 1, m);
        Assert.Equal(new[] { new KeepStep(100, 900), new KeepStep(100, 900), new KeepStep(1, 20), new KeepStep(1, 20) }, p.Steps);   // 1000 déjà complet, 100 : 3-1, 1 : 2-0
    }

    [Fact]
    public void A_priceless_size_does_not_block_the_others_but_the_reason_is_reported()
    {
        var m = Market(avg: 0, market: new() { [10] = 500 });
        var p = KeepOnSalePlanner.Plan(new[] { new KeepSize(100, 1, null, null), new KeepSize(10, 1, null, null) }, 1, m);
        Assert.Equal(new[] { new KeepStep(10, 499) }, p.Steps);
        Assert.Equal("noprice", p.Reason);
    }

    [Fact]
    public void Bad_arguments()
    {
        Assert.Equal("badargs", KeepOnSalePlanner.Plan(Array.Empty<KeepSize>(), 1, Market()).Reason);
        Assert.Equal("badargs", KeepOnSalePlanner.Plan(new[] { new KeepSize(10, -1, null, null) }, 1, Market(avg: 1)).Reason);
        Assert.Equal("badargs", KeepOnSalePlanner.Plan(new[] { new KeepSize(10, 1, -5, null) }, 1, Market(avg: 1)).Reason);
    }
}

public class AchievementLedgerTests
{
    [Fact]
    public void Seeded_achievements_are_done_but_not_rewardable()
    {
        var l = new AchievementLedger();
        l.Seed(new[] { 3, 1, 2 });
        Assert.Equal(new[] { 1, 2, 3 }, l.Finished());
        Assert.True(l.IsDone(2));
        Assert.False(l.HasReward());
        Assert.Equal(0, l.RewardCount());
    }

    [Fact]
    public void A_new_finish_becomes_a_pending_reward_with_the_level_a_repeat_does_not()
    {
        var l = new AchievementLedger();
        Assert.True(l.OnFinished(10, 42));
        Assert.False(l.OnFinished(10, 99));   // déjà terminé : pas de nouvelle attente
        Assert.Equal(new[] { (10, 42) }, l.Rewardable());
        Assert.True(l.HasReward()); Assert.True(l.HasReward(10)); Assert.False(l.HasReward(11));
        Assert.Equal(1, l.RewardCount());
    }

    [Fact]
    public void Already_seeded_achievements_never_become_rewardable_again()
    {
        var l = new AchievementLedger();
        l.Seed(new[] { 5 });
        Assert.False(l.OnFinished(5, 10));
        Assert.False(l.HasReward(5));
    }

    [Fact]
    public void A_granted_reward_leaves_the_queue_a_refused_one_stays_but_is_not_auto_claimed()
    {
        var l = new AchievementLedger();
        l.OnFinished(1, 10); l.OnFinished(2, 10); l.OnFinished(3, 10);
        l.OnRewardResult(1, true);
        l.OnRewardResult(2, false);
        Assert.Equal(new[] { 2, 3 }, l.Rewardable().Select(r => r.Id));
        Assert.Equal(new[] { 3 }, l.AutoClaimable());          // 2 refusé : jamais redemandé automatiquement
        Assert.True(l.HasReward(2));                            // mais toujours visible
    }

    [Fact]
    public void Reset_forgets_everything_and_the_auto_claim_delay_is_three_to_six_seconds()
    {
        var l = new AchievementLedger();
        l.OnFinished(1, 1); l.Reset();
        Assert.Empty(l.Finished()); Assert.False(l.HasReward());
        for (int i = 0; i < 200; i++) Assert.InRange(AchievementLedger.AutoClaimDelay(new Random(i)).TotalSeconds, 3.0, 6.0);
    }
}

public class NpcSightingStoreTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Most_recent_first_and_one_entry_per_map()
    {
        var s = new NpcSightingStore();
        s.Record(7, 100, 12, 4, -17, T0, "3.7.7.6");
        s.Record(7, 200, 33, 5, -17, T0.AddHours(1), "3.7.7.6");
        s.Record(7, 100, 14, 4, -17, T0.AddHours(2), "3.7.7.6");   // revu sur la même carte : mis à jour
        var l = s.Locate(7);
        Assert.Equal(new[] { 100L, 200L }, l.Select(x => x.MapId));
        Assert.Equal(14, l[0].Cell);
        Assert.Empty(s.Locate(8));
    }

    [Fact]
    public void Invalid_ids_are_ignored_and_each_npc_is_capped()
    {
        var s = new NpcSightingStore();
        s.Record(0, 100, 1, 0, 0, T0, ""); s.Record(5, 0, 1, 0, 0, T0, "");
        Assert.Empty(s.Locate(0)); Assert.Empty(s.Locate(5));
        for (int i = 1; i <= NpcSightingStore.MaxPerNpc + 10; i++) s.Record(9, i, 0, 0, 0, T0.AddMinutes(i), "");
        Assert.Equal(NpcSightingStore.MaxPerNpc, s.Locate(9).Count);
        Assert.Equal(NpcSightingStore.MaxPerNpc + 10, s.Locate(9)[0].MapId);   // les plus récentes sont gardées
    }

    [Fact]
    public void Json_round_trip_and_corrupt_input_is_harmless()
    {
        var s = new NpcSightingStore();
        s.Record(7, 100, 12, 4, -17, T0, "3.7.7.6");
        var back = NpcSightingStore.FromJson(s.ToJson());
        var x = back.Locate(7).Single();
        Assert.Equal((100L, 12, 4, -17, T0, "3.7.7.6"), (x.MapId, x.Cell, x.X, x.Y, x.SeenUtc, x.GameVersion));
        Assert.Empty(NpcSightingStore.FromJson("{ pas du json").Locate(7));
        Assert.Empty(NpcSightingStore.FromJson(null).Locate(7));
        Assert.Empty(NpcSightingStore.FromJson("").Locate(7));
    }
}
