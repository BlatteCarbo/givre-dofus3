using System;
using GivreClone.App.Services.Equipment;
using Xunit;

namespace GivreClone.Tests;

public sealed class GearComparisonTests
{
    [Fact]
    public void Equal_instances_with_complete_effects_have_no_delta()
    {
        var item = new GearItemSnapshot(6, 100, 77,
            new[] { new GearEffectSnapshot(11, 143m) }, EffectsComplete: true);
        var report = GearComparison.Compare(new GearSnapshot("old", new[] { item }),
                                            new GearSnapshot("new", new[] { item }));
        Assert.Single(report.Slots);
        Assert.Equal(GearSlotChangeKind.Unchanged, report.Slots[0].Kind);
        Assert.True(report.EffectsFullyComparable);
        Assert.Equal(0m, Assert.Single(report.Effects).Delta);
    }

    [Fact]
    public void Different_rolls_and_equipment_produce_exact_deltas_only_when_complete()
    {
        var before = new GearSnapshot("old", new[] {
            new GearItemSnapshot(6, 100, 77, new[] { new GearEffectSnapshot(11, 143m) }, true),
            new GearItemSnapshot(7, 200, 81, new[] { new GearEffectSnapshot(11, 4m) }, true),
        });
        var after = new GearSnapshot("new", new[] {
            new GearItemSnapshot(6, 101, 78, new[] { new GearEffectSnapshot(11, 160m) }, true),
        });
        var result = GearComparison.Compare(before, after);
        Assert.Contains(result.Slots, s => s.Position == 6 && s.Kind == GearSlotChangeKind.Replaced);
        Assert.Contains(result.Slots, s => s.Position == 7 && s.Kind == GearSlotChangeKind.Removed);
        Assert.Equal(13m, Assert.Single(result.Effects).Delta);
        Assert.True(result.EffectsFullyComparable);
    }

    [Fact]
    public void Missing_or_undecoded_rolls_never_claim_a_stat_gain()
    {
        var before = new GearSnapshot("old", new[] {
            new GearItemSnapshot(6, 100, 77, null, EffectsComplete: false) });
        var after = new GearSnapshot("new", new[] {
            new GearItemSnapshot(6, 101, 78, new[] { new GearEffectSnapshot(11, 160m) }, true) });
        var result = GearComparison.Compare(before, after);
        Assert.False(result.EffectsFullyComparable);
        Assert.True(result.HasUndescribedEffects);
        Assert.Null(Assert.Single(result.Effects).Delta);
    }

    [Fact]
    public void Duplicate_slots_and_invalid_instances_are_rejected()
    {
        var item = new GearItemSnapshot(6, 100);
        Assert.Throws<ArgumentException>(() => GearComparison.Compare(
            new GearSnapshot("bad", new[] { item, item }), new GearSnapshot("new", Array.Empty<GearItemSnapshot>())));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearComparison.Compare(
            new GearSnapshot("bad", new[] { new GearItemSnapshot(6, 0) }),
            new GearSnapshot("new", Array.Empty<GearItemSnapshot>())));
    }
}
