using System;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class SoulCaptureTests
{
    private static SoulSituation S(int level = 90, bool capturable = true, int equipped = 9688, bool ready = true, long hp = 1000, double dpt = 800,
                                   int ap = 12, int spellAp = 3, SoulStoneStack[]? bag = null, int window = 2, double margin = 1.25)
        => new(level, capturable, equipped, bag ?? Array.Empty<SoulStoneStack>(), ready, hp, dpt, ap, spellAp, window, margin);

    [Theory]
    [InlineData(1, 9686)] [InlineData(50, 9686)] [InlineData(51, 9687)] [InlineData(100, 9687)] [InlineData(101, 9688)]
    [InlineData(150, 9688)] [InlineData(151, 9689)] [InlineData(190, 9689)] [InlineData(191, 9690)] [InlineData(1000, 9690)]
    public void RequiredStone_is_the_smallest_stone_that_holds_the_level(int level, int gid) => Assert.Equal(gid, SoulCapture.RequiredStone(level)!.GenericId);

    [Fact]
    public void RequiredStone_is_null_for_unknown_or_out_of_range_levels()
    {
        Assert.Null(SoulCapture.RequiredStone(0));
        Assert.Null(SoulCapture.RequiredStone(1001));
    }

    [Theory] [InlineData(0, 0, true)] [InlineData(5, 0, true)] [InlineData(5, 3, false)] [InlineData(6, 3, true)] [InlineData(4, 3, false)]
    public void Cooldown_is_three_turns(int turn, int last, bool ready) => Assert.Equal(ready, SoulCapture.CooldownElapsed(turn, last));

    [Fact]
    public void No_capturable_monster_means_never_anything()
        => Assert.Equal(SoulAction.None, SoulCapture.Advise(S(capturable: false)).Action);

    [Fact]
    public void Unknown_level_waits_and_too_high_a_level_gives_up()
    {
        Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(level: 0)).Action);
        Assert.Equal(SoulAction.None, SoulCapture.Advise(S(level: 1500)).Action);
    }

    [Fact]
    public void Equips_the_smallest_sufficient_stone_from_the_bag()
    {
        var bag = new[] { new SoulStoneStack(9690, 70, 1), new SoulStoneStack(9689, 60, 2), new SoulStoneStack(9686, 50, 9) };
        var a = SoulCapture.Advise(S(level: 120, equipped: 0, bag: bag));       // niveau 120 -> grande (150) minimum ; l'énorme (190) est la plus petite du sac qui convient
        Assert.Equal(SoulAction.Equip, a.Action);
        Assert.Equal((9689, 60), (a.StoneGid, a.StoneUid));
    }

    [Fact]
    public void An_equipped_stone_too_weak_is_replaced_and_missing_stones_stop_everything()
    {
        var a = SoulCapture.Advise(S(level: 120, equipped: 9686, bag: new[] { new SoulStoneStack(9688, 5, 1) }));
        Assert.Equal((SoulAction.Equip, 9688), (a.Action, a.StoneGid));
        Assert.Equal(SoulAction.None, SoulCapture.Advise(S(level: 120, equipped: 9686, bag: new[] { new SoulStoneStack(9686, 5, 4) })).Action);
        Assert.Equal(SoulAction.None, SoulCapture.Advise(S(level: 120, equipped: 0)).Action);
        Assert.Equal(SoulAction.None, SoulCapture.Advise(S(level: 120, equipped: 0, bag: new[] { new SoulStoneStack(9688, 5, 0) })).Action);   // pile vide
    }

    [Fact]
    public void Waits_while_the_spell_is_not_ready()
        => Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(ready: false)).Action);

    [Fact]
    public void Waits_for_a_damage_measurement_before_the_first_cast()
        => Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(dpt: 0)).Action);

    [Fact]
    public void Casts_only_when_the_expected_damage_over_the_state_window_covers_the_hp()
    {
        // 12 PA, sort à 3 PA : le tour du lancer vaut 75 % ; fenêtre 2 tours -> 800 * (0,75 + 1) = 1400 dégâts attendus
        Assert.Equal(SoulAction.Cast, SoulCapture.Advise(S(hp: 1000)).Action);      // 1400 >= 1000 * 1,25
        Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(hp: 1200)).Action);      // 1400 <  1500 : pas assez sûr
        Assert.Equal(SoulAction.Cast, SoulCapture.Advise(S(hp: 1200, margin: 1.0)).Action);
        Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(hp: 1000, window: 1)).Action);   // un seul tour : 600 < 1250
        Assert.Equal(SoulAction.None, SoulCapture.Advise(S(hp: 0)).Action);        // plus d'ennemi
    }

    [Fact]
    public void Margin_below_one_is_ignored_so_the_decision_is_never_optimistic()
        => Assert.Equal(SoulAction.Wait, SoulCapture.Advise(S(hp: 1500, margin: 0.1)).Action);   // 1400 < 1500 malgré la marge demandée de 0,1

    [Fact]
    public void Measured_damage_is_the_more_cautious_of_the_last_two_turns()
    {
        Assert.Equal(0, SoulCapture.MeasureDamagePerTurn(new long[] { 5000 }));
        Assert.Equal(1000, SoulCapture.MeasureDamagePerTurn(new long[] { 5000, 4000 }));
        Assert.Equal(500, SoulCapture.MeasureDamagePerTurn(new long[] { 5000, 3500, 3000 }));   // min(1500, 500)
        Assert.Equal(0, SoulCapture.MeasureDamagePerTurn(new long[] { 5000, 5000, 5000 }));     // aucun dégât : pas de lancer
        Assert.Equal(0, SoulCapture.MeasureDamagePerTurn(new long[] { 3000, 4000 }));           // les ennemis ont soigné : jamais négatif
    }
}

public class SoulScriptTests
{
    [Fact]
    public void Build_produces_valid_lua_that_targets_only_the_listed_monsters()
    {
        string code = SoulScript.Build(new[] { (101, "Arch Bouftou"), (202, "Arch Piou") }, 85);
        Assert.Null(GivreClone.App.Services.Mcp.SavedLuaScripts.SyntaxError(code));
        Assert.Contains("FORCE_MONSTERS = { 101, 202 }", code);
        Assert.Contains("MAX_PODS = 85", code);
        Assert.Contains("soul.tryCapture(", code);
        Assert.Contains("combat:playTurn()", code);
        Assert.Contains("map:forceFight()", code);
    }

    [Fact]
    public void Build_refuses_an_empty_list_and_keeps_names_on_one_comment_line()
    {
        Assert.Throws<ArgumentException>(() => SoulScript.Build(Array.Empty<(int, string)>()));
        string code = SoulScript.Build(new[] { (1, "Nom\nsur deux lignes") });
        Assert.Null(GivreClone.App.Services.Mcp.SavedLuaScripts.SyntaxError(code));
    }
}
