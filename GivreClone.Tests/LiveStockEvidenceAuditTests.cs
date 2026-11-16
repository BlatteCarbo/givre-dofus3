using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.App.Services.UnifiedStock;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline regression contracts for the LiveStockEvidence/LaunchService seam.
/// Tests cannot instantiate the production LaunchService (Avalonia/transport app)
/// from the pure Tests project; inspect event order in the REAL source and
/// exercise the downstream stock allocator with adversarial observations.
/// </summary>
public sealed class LiveStockEvidenceAuditTests
{
    private static string ReadSource(string relative)
    {
        var dir = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName,
            "GivreClone.App", "Services", "LaunchService.Packets.cs")));
        if (dir is null) throw new DirectoryNotFoundException("Cannot find repository root.");
        return File.ReadAllText(Path.Combine(dir.FullName, "GivreClone.App", "Services", relative));
    }

    [Fact]
    public void PacketDecoded_is_prestore_so_bag_witness_must_wait_for_HdvUpdated()
    {
        var launch = ReadSource("LaunchService.Packets.cs");
        int seed = launch.IndexOf("SeedHdvOrBagInventory(p);", StringComparison.Ordinal);
        Assert.True(seed >= 0 && launch.IndexOf("PacketDecoded?.Invoke(p);", StringComparison.Ordinal) < seed);
        var inventory = ReadSource("LaunchService.HdvInventory.cs");
        Assert.Contains("Inventory.Seed(p.Pid, items", inventory);
        Assert.Contains("_hdvMergedInventory.Seed(p.Pid, items);", inventory);
        Assert.Contains("HdvUpdated?.Invoke(p.Pid);", launch);
        var witness = ReadSource("UnifiedStock/LiveStockEvidence.cs");
        Assert.Contains("launch.HdvUpdated += OnBagStoreUpdated;", witness);
        Assert.Contains("_waitingBag[packet.Pid] = new Pending(seed, expected);", witness);
        Assert.Contains("Matches(pending.Expected, launch.Inventory.Items(pid))", witness);
        Assert.Contains("_bags[pid] = pending.Seed;", witness);
    }

    [Fact]
    public void Storage_must_be_committed_and_checked_against_house_chest_before_publishing()
    {
        var launch = ReadSource("LaunchService.Packets.cs");
        Assert.True(launch.IndexOf("PacketDecoded?.Invoke(p);", StringComparison.Ordinal) <
                    launch.IndexOf("ApplyStorageContent(p);", StringComparison.Ordinal));
        Assert.Contains("_chestSession.ContainsKey(p.Pid)", launch);
        var witness = ReadSource("UnifiedStock/LiveStockEvidence.cs");
        Assert.Contains("launch.BankUpdated += OnBankStoreUpdated;", witness);
        Assert.Contains("launch.ChestSessionActive(packet.Pid)", witness);
        Assert.Contains("launch.ChestSessionActive(pid)", witness);
        Assert.Contains("launch.InHavenBag(pid)", witness);
        Assert.Contains("launch.StorageOpen(pid)", witness);
        Assert.Contains("Matches(pending.Expected, launch.Bank.Items(pid))", witness);
        Assert.DoesNotContain("StockLocation.HavenBag,", witness);
    }

    [Fact]
    public void Login_and_character_switch_revoke_both_stock_seeds_before_new_snapshot()
    {
        var witness = ReadSource("UnifiedStock/LiveStockEvidence.cs");
        Assert.Contains("CharacterSelectionRequest", witness);
        Assert.Contains("CharacterSelectionEvent", witness);
        Assert.Contains("AuthenticationTicketAcceptedEvent", witness);
        Assert.Contains("launch.GameLinkLosing += OnLinkLosing;", witness);
        Assert.Contains("launch.GameLinkLost += OnLinkLost;", witness);
        Assert.Contains("launch.GameLinkRestored += Clear;", witness);
        Assert.Contains("_bags.TryRemove(pid, out _);", witness);
        Assert.Contains("_banks.TryRemove(pid, out _);", witness);
        Assert.Contains("launch.IsLinkSuspended(pid)", witness);
        Assert.Contains("launch.LotMGameVersion, exactGameVersion", witness);
    }

    [Fact]
    public void Incomplete_witness_cannot_be_upgraded_to_trusted_stock_downstream()
    {
        var now = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
        var accepted = new StockObservation("account", 40, StockLocation.Inventory, "bag:40",
            "real-full-bag", now, "3.7.12.12", StockConfidence.Fresh, true,
            new[] { new StockStack(101, "uid:1", 3) });
        var invalidBank = new StockObservation("account", 40, StockLocation.Bank, "bank:account",
            "premature-StorageInventoryContentEvent", now, "3.7.12.12",
            StockConfidence.Unverified, false, new[] { new StockStack(101, "uid:2", 999) });
        var result = UnifiedStockPlanner.Allocate("account", 40, "3.7.12.12",
            now, new[] { accepted, invalidBank }, new Dictionary<long, long> { [101] = 20 });
        var item = Assert.Single(result.Items);
        Assert.Equal(3, item.FromInventory);
        Assert.Equal(0, item.FromBank);
        Assert.Equal(17, item.Missing);
        Assert.False(result.AllSourcesFresh);
    }

    [Fact]
    public void Cross_version_character_and_duplicate_UID_observations_do_not_accumulate()
    {
        var now = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
        var bag = new StockObservation("account", 40, StockLocation.Inventory, "bag:40",
            "session-40", now, "3.7.12.12", StockConfidence.Fresh, true,
            new[] { new StockStack(101, "uid:1", 2) });
        var bank = bag with { Location = StockLocation.Bank, StorageIdentity = "bank:account",
            ObservationId = "bank-40" };
        var otherCharacter = bag with { CharacterId = 41,
            Stacks = new[] { new StockStack(101, "uid:2", 100) } };
        var otherVersion = bag with { GameVersion = "3.7.11.11",
            Stacks = new[] { new StockStack(101, "uid:3", 100) } };
        var result = UnifiedStockPlanner.Allocate("account", 40, "3.7.12.12", now,
            new[] { bag, bank, otherCharacter, otherVersion },
            new Dictionary<long, long> { [101] = 10 });
        var item = Assert.Single(result.Items);
        Assert.Equal(2, item.FromInventory);
        Assert.Equal(0, item.FromBank);
        Assert.Equal(8, item.Missing);
        Assert.Contains(result.Warnings, x => x.Contains("UID", StringComparison.Ordinal));
    }
}
