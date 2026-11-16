using System.Collections.Generic;
using System.Linq;
using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

// Crâ 24/10 : posture de tir 32465 = +3 portée minimale / +4 portée maximale sur chacun de ses sorts, durée 1.
// Sans ce modèle, 32443 (portée 1-5) était lancé à 1-3 cases après le buff et ignoré par le serveur (35 fois sur 102).
public class SpellModifierSetTests
{
    private static readonly SpellCastStats Barrage = new(3, 1, 5, false, false, true, true);

    private static SpellModifierEffect[] PostureDeTir(int spell) => new[]
    {
        new SpellModifierEffect(SpellModifierActions.MinRangeUp, spell, 3, 1),
        new SpellModifierEffect(SpellModifierActions.MaxRangeUp, spell, 4, 1),
    };

    [Fact]
    public void The_shooting_stance_moves_the_cast_window_from_1_5_to_4_9()
    {
        var set = new SpellModifierSet();
        Assert.Equal(Barrage, set.Adjust(32443, Barrage));
        set.Apply(PostureDeTir(32443), turn: 7);
        var s = set.Adjust(32443, Barrage);
        Assert.Equal(4, s.MinRange);
        Assert.Equal(9, s.Range);
        Assert.Equal(3, s.Ap);
    }

    [Fact]
    public void Only_the_targeted_spell_is_modified()
    {
        var set = new SpellModifierSet();
        set.Apply(PostureDeTir(32443), 1);
        Assert.Equal(Barrage, set.Adjust(32426, Barrage));
    }

    [Fact]
    public void A_one_turn_buff_lasts_for_the_rest_of_the_turn_it_was_cast_in_and_expires_at_the_next_turn()
    {
        var set = new SpellModifierSet();
        set.Apply(PostureDeTir(32443), turn: 7);
        set.ExpireAt(7);                                  // même tour : encore actif
        Assert.Equal(4, set.Adjust(32443, Barrage).MinRange);
        set.ExpireAt(8);                                  // début du tour suivant : expiré
        Assert.Equal(Barrage, set.Adjust(32443, Barrage));
        Assert.False(set.Any);
    }

    [Fact]
    public void A_longer_duration_survives_the_following_turns()
    {
        var set = new SpellModifierSet();
        set.Apply(new[] { new SpellModifierEffect(SpellModifierActions.MaxRangeUp, 5, 2, Duration: 3) }, turn: 4);
        set.ExpireAt(6); Assert.Equal(7, set.Adjust(5, new SpellCastStats(1, 0, 5, false, false, true, false)).Range);
        set.ExpireAt(7); Assert.Equal(5, set.Adjust(5, new SpellCastStats(1, 0, 5, false, false, true, false)).Range);
    }

    [Fact]
    public void Casting_the_same_buff_again_renews_it_instead_of_stacking()
    {
        var set = new SpellModifierSet();
        set.Apply(PostureDeTir(32443), 1);
        set.Apply(PostureDeTir(32443), 1);
        Assert.Equal(4, set.Adjust(32443, Barrage).MinRange);
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Ap_cost_range_down_and_flags_follow_the_game_rules()
    {
        var set = new SpellModifierSet();
        set.Apply(new[]
        {
            new SpellModifierEffect(SpellModifierActions.ApCostDown, 9, 1, 1),
            new SpellModifierEffect(SpellModifierActions.MaxRangeDown, 9, 2, 1),
            new SpellModifierEffect(SpellModifierActions.RangeModifiable, 9, 0, 1),
            new SpellModifierEffect(SpellModifierActions.LineOfSightOff, 9, 0, 1),
            new SpellModifierEffect(SpellModifierActions.CastInLineOff, 9, 0, 1),
            new SpellModifierEffect(SpellModifierActions.CastsPerTurnUp, 9, 2, 1),
        }, 1);
        var s = set.Adjust(9, new SpellCastStats(4, 1, 6, true, false, true, false));
        Assert.Equal(3, s.Ap);
        Assert.Equal(4, s.Range);
        Assert.True(s.Rangeable);
        Assert.False(s.CastTestLos);
        Assert.False(s.CastInLine);
        Assert.Equal(2, set.ExtraCastsPerTurn(9));
        Assert.Equal(0, set.ExtraCastsPerTarget(9));
    }

    [Fact]
    public void Values_never_go_negative_and_unknown_or_targetless_effects_are_ignored()
    {
        var set = new SpellModifierSet();
        set.Apply(new[]
        {
            new SpellModifierEffect(SpellModifierActions.ApCostDown, 9, 99, 1),
            new SpellModifierEffect(SpellModifierActions.MinRangeDown, 9, 99, 1),
            new SpellModifierEffect(SpellModifierActions.MinRangeUp, 0, 3, 1),      // sans sort cible
            new SpellModifierEffect(123, 9, 3, 1),                                   // pas un modificateur
        }, 1);
        var s = set.Adjust(9, new SpellCastStats(2, 1, 5, false, false, true, false));
        Assert.Equal(0, s.Ap);
        Assert.Equal(0, s.MinRange);
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Clear_forgets_everything_at_the_end_of_a_fight()
    {
        var set = new SpellModifierSet();
        set.Apply(PostureDeTir(32443), 1);
        set.Clear();
        Assert.Equal(Barrage, set.Adjust(32443, Barrage));
    }

    [Fact]
    public void Modifier_action_ids_match_the_client_texts()
    {
        foreach (int id in new[] { 280, 281, 282, 285, 288, 289, 290, 291, 294, 295, 296 }) Assert.True(SpellModifierActions.IsModifier(id));
        foreach (int id in new[] { 279, 283, 284, 286, 287, 292, 293, 297, 98, 99 }) Assert.False(SpellModifierActions.IsModifier(id));
    }

    [Fact]
    public void The_version_changes_only_when_the_active_set_changes()
    {
        var set = new SpellModifierSet();
        int v0 = set.Version;
        set.ExpireAt(5); set.Clear();                          // rien à retirer : version inchangée
        Assert.Equal(v0, set.Version);
        set.Apply(PostureDeTir(32443), 5);
        int v1 = set.Version;
        Assert.NotEqual(v0, v1);
        set.ExpireAt(5);                                       // pas expiré
        Assert.Equal(v1, set.Version);
        set.ExpireAt(6);                                       // expiré
        Assert.NotEqual(v1, set.Version);
    }

    // Le réseau pose les modificateurs pendant que le tour du bot les lit : aucune exception ni état incohérent.
    [Fact]
    public async System.Threading.Tasks.Task Concurrent_apply_expire_and_adjust_never_throw()
    {
        var set = new SpellModifierSet();
        var errors = new System.Collections.Concurrent.ConcurrentBag<System.Exception>();
        var writers = Enumerable.Range(0, 3).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            try { for (int t = 1; t <= 20000; t++) { set.Apply(PostureDeTir(32443), t); set.ExpireAt(t - 1); } }
            catch (System.Exception e) { errors.Add(e); }
        })).ToArray();
        var readers = Enumerable.Range(0, 3).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 20000; i++)
                {
                    var s = set.Adjust(32443, Barrage);
                    Assert.True(s.MinRange is 1 or 4);
                    Assert.True(s.Range is 5 or 9);
                    Assert.Equal(0, set.ExtraCastsPerTurn(32443));
                }
            }
            catch (System.Exception e) { errors.Add(e); }
        })).ToArray();
        await System.Threading.Tasks.Task.WhenAll(readers.Concat(writers)).WaitAsync(System.TimeSpan.FromSeconds(20));
        Assert.Empty(errors);
    }
}
