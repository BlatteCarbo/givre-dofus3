extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Style = F06App::GivreClone.App.Services.CombatStyle;

namespace GivreClone.Tests;

public sealed class QaCombatHuntBudgetPlacementTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Private)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static object? Call(Launch service, string method, params object[] args)
        => typeof(Launch).GetMethod(method, Private)!.Invoke(service, args);

    private static Launch Service()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[] { "_engineControlled", "_controlledHistory", "_charId", "_fighters",
            "_fighterStats", "_fighterTeam", "_allies", "_confirmedEnemies", "_fightMonsters", "_selfLife", "_selfRange", "_inFight" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        Map(service, "_charId")[1u] = 999L;
        Map(service, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (500, 999), (200, -1) };
        return service;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    public void Live_remaining_points_are_not_replaced_by_character_sheet_maximums(int ap, int mp)
    {
        var service = Service();
        Map(service, "_fighterStats")[1u] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(0, new(0, 12, 6, 100, 100, 12, 6)),
            new KeyValuePair<long, FighterStats>(999, new(999, ap, mp, 100, 100, 12, 6))
        });

        Assert.Equal((ap, mp), Call(service, "TurnStartBudget", 1u));
    }

    [Fact]
    public void Missing_fighter_stats_use_character_sheet_totals()
    {
        var service = Service();
        Map(service, "_fighterStats")[1u] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(0, new(0, 12, 6, 100, 100, 12, 6))
        });
        Assert.Equal((12, 6), Call(service, "TurnStartBudget", 1u));
    }

    [Fact]
    public void Missing_all_stats_keep_the_existing_default_budget()
    {
        Assert.Equal((6, 3), Call(Service(), "TurnStartBudget", 1u));
    }

    [Theory]
    [InlineData(Style.Aggressive)]
    [InlineData(Style.Fleeing)]
    [InlineData(Style.Passive)]
    public void Placement_skips_another_fighters_cell_even_when_it_is_best_for_the_style(Style style)
    {
        var service = Service();
        var offered = new[] { 100, 101, 102 };
        var ordered = style == Style.Aggressive
            ? offered.OrderBy(c => MapPathfinder.CellDistance(c, 200)).ToArray()
            : offered.OrderByDescending(c => MapPathfinder.CellDistance(c, 200)).ToArray();
        Map(service, "_fighters")[1u] = new List<(int Cell, long ActorId)>
        {
            (500, 999), (ordered[0], 1000), (200, -1)
        };

        Assert.Equal(ordered[1], Call(service, "ChoosePlacementCell", 1u, offered, style));
    }

    [Fact]
    public void Placement_can_keep_own_cell_and_returns_no_cell_if_all_offers_are_occupied()
    {
        var service = Service();
        Map(service, "_fighters")[1u] = new List<(int Cell, long ActorId)>
        {
            (100, 999), (101, 1000), (200, -1)
        };
        Assert.Equal(100, Call(service, "ChoosePlacementCell", 1u, new[] { 100, 101 }, Style.Aggressive));
        Assert.Equal(-1, Call(service, "ChoosePlacementCell", 1u, new[] { 101 }, Style.Aggressive));
    }

    [Fact]
    public void Placement_without_known_enemies_still_skips_occupied_cells()
    {
        var service = Service();
        Map(service, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (500, 999), (100, 1000) };
        Assert.Equal(101, Call(service, "ChoosePlacementCell", 1u, new[] { 100, 101 }, Style.Aggressive));
    }
}
