using System;
using System.Collections.Generic;
using GivreClone.App.Services;
using GivreClone.App.Services.SpellParity;
using Xunit;

namespace GivreClone.Tests;

public class SpellParityDescriptionTests
{
    private static SpellConfig Baseline() => new()
    {
        SpellId = 500, Priority = 1, PerTurn = 2, Cooldown = 0, Enabled = true,
        OnMe = false, TargetKind = SpellTargetKind.Enemy, SelfBuff = false,
        IsSummon = false, MaxSummons = 1, MinAp = 0, MinTurn = 0,
        MinEnemies = 0, MinAoeHits = 0, SelfHpMin = 0, SelfHpMax = 100,
        TgtHpMin = 0, TgtHpMax = 100, CastRangeMin = 0,
        CastRangeMax = 0, AllowApproach = true
    };

    public static IEnumerable<object[]> EveryRule =>
    [
        ["sort", (Action<SpellConfig>)(s => s.SpellId++)],
        ["priorité", (Action<SpellConfig>)(s => s.Priority++)],
        ["lancers/tour", (Action<SpellConfig>)(s => s.PerTurn++)],
        ["relance", (Action<SpellConfig>)(s => s.Cooldown++)],
        ["activé", (Action<SpellConfig>)(s => s.Enabled = false)],
        ["sur soi", (Action<SpellConfig>)(s => s.OnMe = true)],
        ["cible", (Action<SpellConfig>)(s => s.TargetKind = SpellTargetKind.Ally)],
        ["buff sur soi", (Action<SpellConfig>)(s => s.SelfBuff = true)],
        ["invocation", (Action<SpellConfig>)(s => s.IsSummon = true)],
        ["invocations max", (Action<SpellConfig>)(s => s.MaxSummons++)],
        ["PA min", (Action<SpellConfig>)(s => s.MinAp++)],
        ["tour min", (Action<SpellConfig>)(s => s.MinTurn++)],
        ["ennemis min", (Action<SpellConfig>)(s => s.MinEnemies++)],
        ["cibles zone min", (Action<SpellConfig>)(s => s.MinAoeHits++)],
        ["PV soi min", (Action<SpellConfig>)(s => s.SelfHpMin++)],
        ["PV soi max", (Action<SpellConfig>)(s => s.SelfHpMax--)],
        ["PV cible min", (Action<SpellConfig>)(s => s.TgtHpMin++)],
        ["PV cible max", (Action<SpellConfig>)(s => s.TgtHpMax--)],
        ["portée min", (Action<SpellConfig>)(s => s.CastRangeMin++)],
        ["portée max", (Action<SpellConfig>)(s => s.CastRangeMax++)],
        ["approche", (Action<SpellConfig>)(s => s.AllowApproach = false)],
    ];

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void Every_changed_rule_explains_why_import_differs(string field, Action<SpellConfig> edit)
    {
        var original = Baseline();
        var imported = original.Clone();
        edit(imported);

        Assert.False(CombatImportLogic.SameSpell(original, imported));
        Assert.Contains(field, SpellParityDisplay.ChangedFields(original, imported));

        var currentText = CombatImportLogic.Describe(original, imported);
        var futureText = CombatImportLogic.Describe(imported, original);
        Assert.Contains(" | ", currentText);
        Assert.Contains(" | ", futureText);
        Assert.NotEqual(currentText, futureText);
        Assert.Contains(field switch
        {
            "lancers/tour" => "x2/tour",
            "cibles zone min" => "zone min=",
            "activé" => "activé",
            "priorité" => "prio ",
            "relance" => "CD ",
            "buff sur soi" => "buff=",
            "sort" => "sort ",
            _ => field,
        }, currentText.Split(" | ")[0]);
    }

    [Fact]
    public void Identical_spells_have_no_false_differences_and_no_mutation()
    {
        var original = Baseline();
        var copy = original.Clone();
        Assert.Empty(SpellParityDisplay.ChangedFields(original, copy));
        Assert.True(CombatImportLogic.SameSpell(original, copy));
        Assert.Equal(CombatImportLogic.Describe(original), CombatImportLogic.Describe(copy, original));
        Assert.Equal(SpellTargetKind.Enemy, original.TargetKind);
        Assert.Equal(2, original.PerTurn);
    }

    [Fact]
    public void Target_kind_change_is_visible_before_priority()
    {
        var original = Baseline();
        var incoming = original.Clone();
        incoming.TargetKind = SpellTargetKind.Ally;
        incoming.Priority = 50;
        var current = CombatImportLogic.Describe(original, incoming);
        var future = CombatImportLogic.Describe(incoming, original);
        Assert.StartsWith("cible Enemy", current);
        Assert.StartsWith("cible Ally", future);
        Assert.True(future.IndexOf("cible Ally", StringComparison.Ordinal) <
                    future.IndexOf("prio 50", StringComparison.Ordinal));
    }
}
