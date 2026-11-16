using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.Tests;

public sealed class SmithWorkPolicyTests
{
    [Theory]
    [InlineData(40, 30, true)]
    [InlineData(30, 30, true)]
    [InlineData(29, 30, false)]
    public void Profession_must_be_at_least_the_item_level(int profession, int item, bool forge)
    {
        var admission = SmithProfessionGate.Admit(44, null, item, profession);
        Assert.Equal(forge ? SmithAdmission.Forge : SmithAdmission.ItemAboveProfession, admission);
    }

    [Fact]
    public void Unknown_levels_and_other_professions_are_not_forgeable()
    {
        Assert.Equal(SmithAdmission.ProfessionLevelUnknown, SmithProfessionGate.Admit(44, null, 30, null));
        Assert.Equal(SmithAdmission.ItemLevelUnknown, SmithProfessionGate.Admit(44, null, null, 40));
        Assert.Equal(SmithAdmission.NotForgeable, SmithProfessionGate.Admit(null, null, 30, 40));
        Assert.Equal(SmithAdmission.WrongProfession, SmithProfessionGate.Admit(48, 44, 30, 40));
        Assert.Equal(SmithAdmission.Forge, SmithProfessionGate.Admit(44, 44, 30, 40));
    }

    [Fact]
    public void Leveling_does_not_travel_when_the_profession_or_rune_cannot_start()
    {
        Assert.Equal(SmithLevelingStop.ProfessionUnknown, SmithLeveling.Gate(null, 50, true, 10));
        Assert.Equal(SmithLevelingStop.AlreadyAtTarget, SmithLeveling.Gate(50, 50, true, 10));
        Assert.Equal(SmithLevelingStop.AlreadyAtTarget, SmithLeveling.Gate(51, 50, true, 10));
        Assert.Equal(SmithLevelingStop.NoRune, SmithLeveling.Gate(40, 50, true, 0));
        Assert.Equal(SmithLevelingStop.MissingTarget, SmithLeveling.Gate(40, 0, true, 10));
        Assert.Equal(SmithLevelingStop.MissingRune, SmithLeveling.Gate(40, 50, false, 10));
        Assert.Equal(SmithLevelingStop.Continue, SmithLeveling.Gate(40, 50, true, 10));
        Assert.Contains("aucun trajet", SmithJournal.Leveling(SmithLevelingStop.ProfessionUnknown));
        Assert.Contains("aucun trajet", SmithJournal.Leveling(SmithLevelingStop.AlreadyAtTarget));
        Assert.Contains("aucune rune pass", SmithJournal.Leveling(SmithLevelingStop.NoForgeableItem));
    }

    [Fact]
    public void Leveling_stays_on_the_current_item_then_takes_the_next_forgeable_one()
    {
        var items = new LevelingItem[]
        {
            new(1, true, SmithAdmission.ItemAboveProfession),
            new(2, true, SmithAdmission.Forge),
            new(3, true, SmithAdmission.Forge),
        };
        Assert.Equal(2, SmithLeveling.NextUid(items, null));
        Assert.Equal(2, SmithLeveling.NextUid(items, 2));
        var gone = new LevelingItem[] { items[0], items[1] with { InBag = false }, items[2] };
        Assert.Equal(3, SmithLeveling.NextUid(gone, 2));
        Assert.Null(SmithLeveling.NextUid(items.Select(i => i with { Admission = SmithAdmission.ItemAboveProfession }).ToArray(), null));
    }

    [Fact]
    public void Leveling_moves_to_the_highest_level_forgeable_item_once_admitted()
    {
        var low = new LevelingItem(1, true, SmithAdmission.Forge, 5);
        var high = new LevelingItem(2, true, SmithAdmission.ItemAboveProfession, 20);
        Assert.Equal(1, SmithLeveling.NextUid(new[] { low, high }, 1));
        Assert.Equal(2, SmithLeveling.NextUid(new[] { low, high with { Admission = SmithAdmission.Forge } }, 1));
        var twin = new LevelingItem(3, true, SmithAdmission.Forge, 20);
        Assert.Equal(3, SmithLeveling.NextUid(new[] { low, high with { Admission = SmithAdmission.Forge }, twin }, 3));
    }

    [Fact]
    public void Kamas_cap_stops_a_rune_without_blocking_an_uncapped_session()
    {
        Assert.True(SmithLeveling.CanSpendRune(0, 100, long.MaxValue));
        Assert.True(SmithLeveling.CanSpendRune(0, -1, long.MaxValue));
        Assert.False(SmithLeveling.CanSpendRune(90, 20, 100));
        Assert.True(SmithLeveling.CanSpendRune(80, 20, 100));
        Assert.False(SmithLeveling.CanSpendRune(0, -1, 100));
    }

    [Fact]
    public void Search_keeps_forbidden_rows_and_matches_name_level_and_type()
    {
        var rows = new BagPick[]
        {
            new(1, "Anneau du Bouftou", 30, "Anneau", null),
            new(2, "Ceinture du Bouftou", 30, "Ceinture", "mauvais métier"),
            new(3, "Amulette", 12, "Amulette", null),
        };
        var found = BagPicker.Filter(rows, "  BOUFTOU   30 ");
        Assert.Equal(new long[] { 1, 2 }, found.Select(r => r.Uid));
        Assert.Equal("mauvais métier", found[1].BlockReason);
        Assert.Equal(new long[] { 3 }, BagPicker.Filter(rows, "amu").Select(r => r.Uid));
    }

    [Fact]
    public void Selection_skips_forbidden_rows_for_range_all_and_enter()
    {
        var rows = new BagPick[]
        {
            new(1, "A", 1, "Anneau", null),
            new(2, "B", 2, "Anneau", "équipé"),
            new(3, "C", 3, "Anneau", null),
            new(4, "D", 4, "Rune", "rune"),
        };
        Assert.Equal(new long[] { 1, 3 }, BagPicker.VisibleAllowed(rows));
        Assert.Equal(new long[] { 1, 3 }, BagPicker.ShiftRange(rows, 1, 3));
        Assert.Equal(new long[] { 3 }, BagPicker.AddOrder(rows, new HashSet<long> { 2, 3, 4 }));
        Assert.Null(BagPicker.Enter(rows));
        Assert.Equal(3, BagPicker.Enter(BagPicker.Filter(rows, "C")));
    }

    [Fact]
    public void Crush_stops_before_any_item_when_closed_and_continues_after_one_failure()
    {
        int calls = 0;
        var closed = CrushSequence.Run(new long[] { 5, 6 }, false, _ => { calls++; return new CrushOne(true, true); });
        Assert.Equal(0, calls);
        Assert.False(closed.Opened);
        Assert.Equal(0, closed.Crushed);
        Assert.Equal(2, closed.Remaining);

        var report = CrushSequence.Run(new long[] { 5, 6, 7 }, true, uid => uid switch
        {
            5 => new CrushOne(false, true),
            6 => new CrushOne(true, false),
            _ => new CrushOne(true, true),
        });
        Assert.Equal(1, report.Crushed);
        Assert.Equal(new long[] { 5 }, report.Failed);
        Assert.Equal(1, report.Remaining);
    }

    [Fact]
    public void Rune_stock_adds_a_known_bank_and_uses_the_bag_first()
    {
        Assert.Equal(8, RuneStock.Visible(3, 5, true));
        Assert.Equal(3, RuneStock.Visible(3, 5, false));
        Assert.Equal(4, RuneStock.Uid(4, 9));
        Assert.Equal(9, RuneStock.Uid(null, 9));
        Assert.Null(RuneStock.Uid(null, null));
    }

    [Fact]
    public void Skip_text_names_the_level_gap()
    {
        var text = SmithJournal.Skip("Koliet", SmithAdmission.ItemAboveProfession, 29, 30);
        Assert.Contains("29", text);
        Assert.Contains("30", text);
        Assert.Contains("pass", text);
    }
}
