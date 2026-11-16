using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class CombatImportLogicTests
{
    [Fact]
    public void Automatic_settings_survive_clone_and_are_imported_only_when_selected()
    {
        var source = new CombatProfile
        {
            AutoSpells = true,
            AutoElements = GivreClone.App.Services.Combat.AutoCombatElements.Fire
                | GivreClone.App.Services.Combat.AutoCombatElements.Air
        };
        var target = new CombatProfile();

        CombatImportLogic.Apply(target, source.Clone(), new HashSet<string> { "AutoElements" },
            new HashSet<int>(), replace: false);

        Assert.Equal(source.AutoElements, target.AutoElements);
        Assert.False(target.AutoSpells);
    }

    private static SpellConfig Sp(int id, int prio = 1, int perTurn = 1, int cd = 0, bool on = true)
        => new() { SpellId = id, Priority = prio, PerTurn = perTurn, Cooldown = cd, Enabled = on };

    private static CombatProfile P(params SpellConfig[] spells) => new() { Spells = spells.ToList() };

    [Fact]
    public void Identical_profiles_have_no_change()
    {
        var a = P(Sp(1), Sp(2, 2));
        var plan = CombatImportLogic.Diff(a, a.Clone(), replace: true);
        Assert.True(plan.Identical);
        Assert.Equal("Profils identiques", CombatImportLogic.Summary(plan));
    }

    [Fact]
    public void Diff_lists_changed_settings_and_changed_or_new_spells()
    {
        var cur = P(Sp(1), Sp(2, 2));
        var src = P(Sp(1, perTurn: 3), Sp(3, 3));
        src.Style = CombatStyle.Fleeing;
        src.MaxCastsPerTurn = 4;
        var plan = CombatImportLogic.Diff(cur, src, replace: false);
        Assert.Equal(new[] { "Style", "MaxCastsPerTurn" }, plan.Settings.Where(s => s.Differs).Select(s => s.Key));
        var style = plan.Settings.Single(s => s.Key == "Style");
        Assert.Equal(("Aggressive", "Fleeing"), (style.Current, style.Incoming));
        Assert.Equal(new[] { 1, 3 }, plan.Spells.Where(s => s.Differs).Select(s => s.SpellId));
        Assert.True(plan.Spells.Single(s => s.SpellId == 3).IsNew);
        Assert.False(plan.Spells.Single(s => s.SpellId == 1).IsNew);
        Assert.Empty(plan.Removed);   // fusion : rien n'est retiré
        Assert.Equal("2 réglage(s), 2 sort(s) différent(s)", CombatImportLogic.Summary(plan));
    }

    [Fact]
    public void Replace_mode_reports_the_spells_that_would_be_removed()
    {
        var plan = CombatImportLogic.Diff(P(Sp(1), Sp(2, 2)), P(Sp(1)), replace: true);
        Assert.Equal(new[] { 2 }, plan.Removed.Select(s => s.SpellId));
        Assert.False(plan.Identical);
        Assert.Equal("1 sort(s) retiré(s)", CombatImportLogic.Summary(plan));
    }

    [Fact]
    public void Merge_applies_only_checked_items_and_keeps_the_rest()
    {
        var cur = P(Sp(1), Sp(2, 2));
        var src = P(Sp(1, perTurn: 3), Sp(3, 3), Sp(4, 4));
        src.Style = CombatStyle.Passive; src.Speed = CombatSpeed.Turbo;
        var r = CombatImportLogic.Apply(cur, src, new HashSet<string> { "Style" }, new HashSet<int> { 1, 3 }, replace: false);
        Assert.Equal(CombatStyle.Passive, cur.Style);
        Assert.NotEqual(CombatSpeed.Turbo, cur.Speed);            // non coché : intact
        Assert.Equal(new[] { 1, 2, 3 }, cur.Spells.Select(s => s.SpellId));
        Assert.Equal(3, cur.Spells.Single(s => s.SpellId == 1).PerTurn);   // remplacé
        Assert.DoesNotContain(cur.Spells, s => s.SpellId == 4);            // non coché
        Assert.Equal((1, 1, 1, 0), (r.Settings, r.SpellsAdded, r.SpellsReplaced, r.SpellsRemoved));
    }

    [Fact]
    public void Replace_makes_the_rotation_exactly_the_selection_ordered_by_priority()
    {
        var cur = P(Sp(1), Sp(2, 2));
        var src = P(Sp(3, 5), Sp(1, 2, perTurn: 2), Sp(9, 1));
        var r = CombatImportLogic.Apply(cur, src, new HashSet<string>(), new HashSet<int> { 3, 1 }, replace: true);
        Assert.Equal(new[] { 1, 3 }, cur.Spells.Select(s => s.SpellId));   // prio 2 puis 5
        Assert.Equal((0, 1, 1, 1), (r.Settings, r.SpellsAdded, r.SpellsReplaced, r.SpellsRemoved));   // 2 retiré
    }

    [Fact]
    public void Applied_spells_are_copies_not_shared_instances()
    {
        var src = P(Sp(1));
        var cur = P();
        CombatImportLogic.Apply(cur, src, new HashSet<string>(), new HashSet<int> { 1 }, replace: false);
        cur.Spells[0].PerTurn = 99;
        Assert.Equal(1, src.Spells[0].PerTurn);
    }

    [Fact]
    public void Challenge_lists_are_copied_by_value()
    {
        var src = new CombatProfile { ChallengeOn = { 3, 1 }, ChallengeOff = { 7 } };
        var cur = new CombatProfile();
        CombatImportLogic.Apply(cur, src, new HashSet<string> { "ChallengeLists" }, new HashSet<int>(), replace: false);
        Assert.Equal(new[] { 3, 1 }, cur.ChallengeOn);
        cur.ChallengeOn.Add(5);
        Assert.Equal(2, src.ChallengeOn.Count);
    }

    [Fact]
    public void Unknown_keys_and_ids_are_ignored()
    {
        var cur = P(Sp(1));
        var r = CombatImportLogic.Apply(cur, P(Sp(2)), new HashSet<string> { "Nope" }, new HashSet<int> { 42 }, replace: false);
        Assert.Equal((0, 0, 0, 0), (r.Settings, r.SpellsAdded, r.SpellsReplaced, r.SpellsRemoved));
        Assert.Single(cur.Spells);
    }

    [Fact]
    public void New_lock_and_turbo_fields_round_trip_through_clone_and_import()
    {
        var src = new CombatProfile { LockJoin = true, LockSpectators = true, TurboEagerReady = true, ChallengeAbandonHp = 25 };
        var c = src.Clone();
        Assert.True(c.LockJoin && c.LockSpectators && c.TurboEagerReady && !c.LockParty && !c.TurboAckAll);
        Assert.Equal(25, c.ChallengeAbandonHp);
        var cur = new CombatProfile();
        CombatImportLogic.Apply(cur, src, new HashSet<string> { "LockJoin", "ChallengeAbandonHp" }, new HashSet<int>(), false);
        Assert.True(cur.LockJoin);
        Assert.False(cur.LockSpectators);
        Assert.Equal(25, cur.ChallengeAbandonHp);
    }

    [Fact]
    public void Spell_description()
    {
        Assert.Equal("ennemi · prio 2 · x3/tour · CD 4", CombatImportLogic.Describe(Sp(1, 2, 3, 4)));
        Assert.Equal("ennemi · prio 1 · x1/tour · désactivé", CombatImportLogic.Describe(Sp(1, on: false)));
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, false)]
    [InlineData(true, false, false, false, false, false, true)]   // script seul
    [InlineData(false, false, false, false, true, false, true)]   // profil seul : rejoindre
    public void Fight_locks_merge_script_and_profile(bool sSpec, bool sJoin, bool sParty, bool pSpec, bool pJoin, bool pParty, bool any)
    {
        var m = FightLocks.Merge(new(sSpec, sJoin, sParty), new CombatProfile { LockSpectators = pSpec, LockJoin = pJoin, LockParty = pParty });
        Assert.Equal(any, m.Any);
        Assert.Equal(sSpec || pSpec, m.Spectators);
        Assert.Equal(sJoin || pJoin, m.Join);
        Assert.Equal(sParty || pParty, m.Party);
    }

    [Fact]
    public void Fight_locks_without_a_profile_keep_the_script_policy()
        => Assert.Equal(new FightLocks(false, true, false), FightLocks.Merge(new(false, true, false), null));
}
