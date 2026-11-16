using System.Linq;
using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class SpellChoicePlannerTests
{
    private static SpellChoicePlanner.Candidate C(int ap, double dmg, int hits = 1, int kills = 0) => new(ap, dmg, hits, kills);

    [Fact]
    public void Chest_prefers_three_cheap_hits_over_one_large_hit_but_normal_combat_does_not()
    {
        var spells = new[] { C(6, 1000), C(2, 10) };
        var chest = SpellChoicePlanner.ReorderOffensive(spells, _ => true,
            c => SpellChoicePlanner.HitCountScore(c.Ap, 6, 3));
        var normal = SpellChoicePlanner.ReorderOffensive(spells, _ => true, SpellChoicePlanner.Score);
        Assert.Equal(2, chest[0].Ap);
        Assert.Equal(6, normal[0].Ap);
    }

    [Theory]
    [InlineData(0, 6, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(2, 6, 0)]
    [InlineData(2, 6, -1)]
    public void Chest_spells_without_a_legal_affordable_cast_score_zero(int ap, int budget, int remaining)
        => Assert.Equal(0, SpellChoicePlanner.HitCountScore(ap, budget, remaining));

    [Fact]
    public void Chest_ranking_uses_remaining_casts_without_rewarding_damage_or_false_kills()
    {
        Assert.Equal(0.5, SpellChoicePlanner.HitCountScore(2, 6, 1));
        Assert.Equal(0.5, SpellChoicePlanner.HitCountScore(2, 6, 3));
        Assert.True(SpellChoicePlanner.HitCountScore(3, 6, 2) > SpellChoicePlanner.HitCountScore(2, 6, 0));
    }

    [Fact]
    public void On_a_single_target_the_best_damage_per_AP_wins()
    {
        // 100 pour 3 PA (33/PA) contre 60 pour 2 PA (30/PA)
        Assert.True(SpellChoicePlanner.Score(C(3, 100)) > SpellChoicePlanner.Score(C(2, 60)));
    }

    [Fact]
    public void A_zone_spell_hitting_two_enemies_beats_a_stronger_single_target_spell()
    {
        var single = C(3, 100);                 // 33/PA
        var zone = C(2, 60, hits: 2);           // 60/PA
        Assert.True(SpellChoicePlanner.Score(zone) > SpellChoicePlanner.Score(single));
    }

    [Fact]
    public void The_same_zone_spell_on_one_enemy_loses_its_advantage()
    {
        Assert.True(SpellChoicePlanner.Score(C(3, 100)) > SpellChoicePlanner.Score(C(2, 60, hits: 1)));   // 33,3 contre 30
    }

    [Fact]
    public void Killing_a_target_adds_value_so_a_finisher_is_preferred()
    {
        var finisher = C(2, 50, hits: 1, kills: 1);   // 50 x 1,5 / 2 = 37,5
        var bigger = C(2, 60, hits: 1, kills: 0);     // 30
        Assert.True(SpellChoicePlanner.Score(finisher) > SpellChoicePlanner.Score(bigger));
    }

    [Fact]
    public void Kills_are_capped_by_the_targets_actually_hit()
    {
        Assert.Equal(SpellChoicePlanner.Score(C(2, 50, 1, 1)), SpellChoicePlanner.Score(C(2, 50, 1, 9)));
    }

    [Theory]
    [InlineData(0, 100, 1)]   // gratuit : pas de division par zéro
    [InlineData(3, 0, 1)]     // aucun dégât
    [InlineData(3, 100, 0)]   // rien à toucher d'ici
    public void Spells_that_cannot_do_damage_score_zero(int ap, double dmg, int hits)
        => Assert.Equal(0, SpellChoicePlanner.Score(C(ap, dmg, hits)));

    [Fact]
    public void Only_offensive_slots_are_permuted_and_buffs_keep_their_place()
    {
        // (id, offensif, score) : le buff 1 puis l'invocation 2 restent en tête, les offensifs 3/4/5 se réordonnent entre eux.
        var items = new[] { (Id: 1, Off: false, S: 0.0), (Id: 2, Off: false, S: 0.0), (Id: 3, Off: true, S: 10.0), (Id: 4, Off: true, S: 50.0), (Id: 5, Off: true, S: 30.0) };
        var r = SpellChoicePlanner.ReorderOffensive(items, x => x.Off, x => x.S);
        Assert.Equal(new[] { 1, 2, 4, 5, 3 }, r.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void Offensive_spells_interleaved_with_buffs_stay_in_their_slots()
    {
        var items = new[] { (Id: 1, Off: true, S: 5.0), (Id: 2, Off: false, S: 0.0), (Id: 3, Off: true, S: 9.0) };
        var r = SpellChoicePlanner.ReorderOffensive(items, x => x.Off, x => x.S);
        Assert.Equal(new[] { 3, 2, 1 }, r.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void Equal_scores_keep_the_original_order_and_the_input_is_untouched()
    {
        var items = new[] { (Id: 1, S: 7.0), (Id: 2, S: 7.0), (Id: 3, S: 7.0) };
        var r = SpellChoicePlanner.ReorderOffensive(items, _ => true, x => x.S);
        Assert.Equal(new[] { 1, 2, 3 }, r.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, items.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void An_empty_or_fully_defensive_list_is_returned_unchanged()
    {
        Assert.Empty(SpellChoicePlanner.ReorderOffensive(System.Array.Empty<int>(), _ => true, _ => 0));
        var r = SpellChoicePlanner.ReorderOffensive(new[] { 3, 1, 2 }, _ => false, x => x);
        Assert.Equal(new[] { 3, 1, 2 }, r);
    }
}
