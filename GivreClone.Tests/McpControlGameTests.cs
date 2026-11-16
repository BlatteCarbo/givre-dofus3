using GivreClone.App.Services;
using GivreClone.App.Services.Mcp;
using Xunit;

namespace GivreClone.Tests;

public sealed class McpControlGameTests
{
    [Fact]
    public void Inventory_instances_are_resolved_by_pid_and_uid_not_by_catalogue_gid()
    {
        var store = new InventoryStore();
        store.Seed(10, new[]
        {
            new InventoryStore.Item(1001, 172, 2, 63),
            new InventoryStore.Item(1002, 172, 1, 7)
        });
        store.Seed(20, new[] { new InventoryStore.Item(1001, 421, 5, 63) });

        Assert.Equal(172, store.Get(10, 1001)?.Gid);
        Assert.Equal(421, store.Get(20, 1001)?.Gid);
        Assert.Equal(63, store.Get(10, 1001)?.Position);
        Assert.Equal(7, store.Get(10, 1002)?.Position);
        Assert.Null(store.Get(20, 1002));
        Assert.Equal(3, store.CountByGid(10, 172));
        Assert.Equal(0, store.CountByGid(20, 172));
    }

    [Fact]
    public void Dungeon_script_is_valid_and_contains_only_known_ordered_rooms_and_keys()
    {
        var warnings = new List<string>();
        var profile = new DungeonProfile(
            "Donjon d'essai",
            new long[] { 10010, 10020, 10020, 10030 },
            10000,
            new (long Gid, int Qty, string Name)[] { (42, 2, "Clé d'essai") },
            Runs: 3, MaxMinutes: 40);

        string lua = ExpeditionScript.BuildDungeon(profile, warnings);

        Assert.Contains("local ROOMS = { 10010, 10020, 10030 }", lua);
        Assert.Contains("local ENTRANCE = 10000", lua);
        Assert.Contains("local KEYS = { { 42, 2,", lua);
        Assert.Contains("local RUNS = 3", lua);
        Assert.Contains("local MAX_MINUTES = 40", lua);
        Assert.Null(SavedLuaScripts.SyntaxError(lua));
    }

    [Theory]
    [InlineData("player@example.com", true)]
    [InlineData("player.three", true)]
    [InlineData("otheruser", false)]
    public void Protected_accounts_are_identified_before_game_actions(string login, bool blocked)
        => Assert.Equal(blocked, McpGuards.IsBlocked(login, McpGuards.DefaultBlockedLogins));
}
