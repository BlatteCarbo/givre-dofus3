using System;
using System.IO;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class InventorySessionLifecycleTests
{
    [Fact]
    public void A_reused_pid_must_not_inherit_inventory_stacks()
    {
        var inventory = new InventoryStore();
        const uint pid = 18181;
        inventory.Seed(pid, new[] { new InventoryStore.Item(10, 500, 2) });
        Assert.Equal(2, inventory.CountByGid(pid, 500));
        inventory.Clear(pid);
        Assert.Equal(0, inventory.CountByGid(pid, 500));
        Assert.Null(inventory.Get(pid, 10));
        inventory.Seed(pid, new[] { new InventoryStore.Item(11, 501, 1) });
        Assert.Equal(0, inventory.CountByGid(pid, 500));
        Assert.Equal(1, inventory.CountByGid(pid, 501));
    }

    [Fact]
    public void Closing_a_stack_in_one_store_does_not_delete_the_other_store_snapshot()
    {
        var bag = new InventoryStore();
        var bank = new InventoryStore();
        const uint pid = 18181;
        bank.Seed(pid, new[] { new InventoryStore.Item(11, 501, 10) });
        bag.Seed(pid, new[] { new InventoryStore.Item(11, 501, 10) });
        bag.Remove(pid, 11);
        Assert.Null(bag.Get(pid, 11));
        Assert.NotNull(bank.Get(pid, 11));
    }

    private static string Source(string name)
    {
        var d = MappingScan.WalkUp(dir => Directory.Exists(Path.Combine(dir.FullName, "GivreClone.App")));
        if (d is null) throw new DirectoryNotFoundException("GivreClone.App introuvable");
        return File.ReadAllText(Path.Combine(d.FullName, "GivreClone.App", "Services", name));
    }

    [Fact]
    public void Session_forget_and_character_selection_clear_sensitive_pid_scoped_state()
    {
        var lifecycle = Source("LaunchService.cs");
        var events = Source("LaunchService.Packets.cs");
        var hdv = Source("LaunchService.HdvStatus.cs");
        Assert.Contains("Inventory.Clear(pid); Bank.Clear(pid); ForgetItemEffects(pid);", lifecycle);
        Assert.Contains("_kamas.TryRemove(pid, out _); _bankKamas.TryRemove(pid, out _);", lifecycle);
        Assert.Contains("oldChar != sel", events);
        Assert.Contains("ForgetItemEffects(p.Pid)", events);
        Assert.Contains("_hdvSellNeedsReview.TryRemove(pid, out _);", hdv);
        Assert.Contains("_hdvRepriceNeedsReview.TryRemove(pid, out _);", hdv);
    }

    [Fact]
    public void Item_effect_cache_must_be_pruned_after_deletes_and_snapshots()
    {
        var effects = Source("LaunchService.ItemEffects.cs");
        var events = Source("LaunchService.Packets.cs");
        Assert.Contains("Inventory.Get(pid, key.Uid) is null && Bank.Get(pid, key.Uid) is null", effects);
        Assert.Contains("PruneItemEffects(p.Pid);", events);
        Assert.Contains("case \"InventoryContentEvent\":", effects);
        Assert.Contains("p.Name.StartsWith(\"Storage\", StringComparison.Ordinal) && _chestSession.ContainsKey(p.Pid)", effects);
    }
}
