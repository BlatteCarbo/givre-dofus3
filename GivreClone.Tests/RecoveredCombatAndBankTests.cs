extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Store = F06App::GivreClone.App.Services.InventoryStore;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using SpellRow = F06App::GivreClone.App.ViewModels.SpellRowViewModel;
using SpellConfig = F06App::GivreClone.App.Services.SpellConfig;
using Profile = F06App::GivreClone.App.Services.CombatProfile;

namespace GivreClone.Tests;

[Collection("F08InventoryMapping")]
public sealed class RecoveredCombatAndBankTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Flags)!;
    private static IDictionary Map(Launch s, string name) => (IDictionary)Field(name).GetValue(s)!;
    private static object? Call(Launch s, string method, params object[] args) => typeof(Launch).GetMethod(method, Flags)!.Invoke(s, args);
    private static Launch Service()
    {
        var s = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[] { "_hdvOpen", "_hdvAnyOpen", "_hdvBuyerOpen", "_hdvBuyerType", "_hdvSellLinks",
            "_hdvMergedRequested", "_hdvMergedInventory", "<Inventory>k__BackingField", "<Bank>k__BackingField",
            "_manualOwner", "_automaticTurnStop", "_autoFightOverride", "_combat", "_combatOverrides", "_scriptTurnHook", "_scriptPlacementHook" })
            Field(name).SetValue(s, Activator.CreateInstance(Field(name).FieldType));
        return s;
    }
    private static ProtoField V(int field, ulong value) => new() { Field = field, Wire = WireType.Varint, Varint = value };
    private static ProtoField M(int field, params ProtoField[] children) => new() { Field = field, Wire = WireType.Len, Message = children.ToList() };
    private static int F(string message, string field) => Mapping.FieldNumber(message, field)!.Value;
    private static ProtoField Item(int uid, int gid, int qty, int position = 63) => M(F("InventoryContentEvent", "objects"),
        V(F("ObjectItemInventory", "position"), (ulong)position), M(F("ObjectItemInventory", "item"),
            V(F("ObjectItem", "uid"), (ulong)uid), V(F("ObjectItem", "gid"), (ulong)gid), V(F("ObjectItem", "quantity"), (ulong)qty)));
    private static void Seed(Launch s, uint pid, params ProtoField[] items)
    {
        InventoryReader.UseCatalog(Mapping.FieldNumber);
        Call(s, "SeedHdvOrBagInventory", new Packet(pid, false, "", "InventoryContentEvent", items, []));
    }

    [Fact]
    public void Late_spell_data_refresh_notifies_variant_even_if_name_and_icon_did_not_change()
    {
        int saved = 0;
        var row = new SpellRow(new SpellConfig { SpellId = 1 }, "Même nom", null, () => saved++);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        row.Refresh("Même nom", null);
        Assert.Contains(nameof(SpellRow.AltSpellId), changed);
        Assert.Contains(nameof(SpellRow.HasVariant), changed);
        Assert.Contains(nameof(SpellRow.AltName), changed);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void Switching_variant_preserves_rotation_settings_and_saves_once()
    {
        int saves = 0;
        var config = new SpellConfig { SpellId = 1, Priority = 4, PerTurn = 3, PerTarget = 2, Cooldown = 5, OnMe = true };
        var row = new SpellRow(config, "Avant", null, () => saves++);
        row.SwitchTo(2, "Après", null);
        Assert.Equal(2, row.SpellId); Assert.Equal("Après", row.Name);
        Assert.Equal(4, row.Priority); Assert.Equal(3, row.PerTurn); Assert.Equal(2, row.MaxPerTarget);
        Assert.Equal(5, row.Cooldown); Assert.True(row.OnMe); Assert.Equal(1, saves);
    }

    [Fact]
    public void Manual_mode_preserves_script_overrides_hooks_and_other_characters()
    {
        var s = Service(); var owner = new object();
        s.SetAutoFightOverride(1, owner, true);
        Map(s, "_scriptTurnHook")[1u] = owner;
        using var turn = new CancellationTokenSource();
        Map(s, "_automaticTurnStop")[1u] = turn;
        s.SetManualCombat(2, true);
        s.SetManualCombat(1, true);
        Assert.True(turn.IsCancellationRequested);
        Assert.False(s.IsAutoFightEffective(1));
        Assert.False((bool)Call(s, "ScriptDrivesTurns", 1u)!);
        Assert.True(s.AutoFightOverride(1));
        s.SetManualCombat(1, false);
        Assert.False(s.ManualCombatOn(1)); Assert.True(s.ManualCombatOn(2));
        Assert.True(s.AutoFightOverride(1)); Assert.Same(owner, Map(s, "_scriptTurnHook")[1u]);
        Assert.Empty(Map(s, "_combatOverrides"));
    }

    [Fact]
    public void Leaving_manual_mode_restores_profile_autoplay_without_another_profile_edit()
    {
        var s = Service();
        Map(s, "_combat")[1u] = new Profile { AutoFight = true };
        Assert.True(s.IsAutoFightEffective(1));
        s.SetManualCombat(1, true); Assert.False(s.IsAutoFightEffective(1));
        s.SetManualCombat(1, false); Assert.True(s.IsAutoFightEffective(1));
        Map(s, "_combat")[1u] = new Profile { AutoFight = false };
        s.SetManualCombat(1, true); s.SetManualCombat(1, false);
        Assert.False(s.IsAutoFightEffective(1));
    }

    [Fact]
    public void Old_override_owner_cannot_release_a_new_script_owner()
    {
        var s = Service(); var old = new object(); var current = new object();
        s.SetAutoFightOverride(1, old, false); s.SetAutoFightOverride(1, current, true);
        s.SetAutoFightOverride(1, old, null); Assert.True(s.AutoFightOverride(1));
        s.SetAutoFightOverride(1, current, null); Assert.Null(s.AutoFightOverride(1));
    }

    [Fact]
    public void Fresh_merged_stock_has_bank_provenance_without_polluting_the_bag()
    {
        var s = Service();
        s.Inventory.Seed(1, [new(10, 100, 8), new(11, 200, 1, 0)]);
        s.Bank.Seed(1, [new(20, 100, 999), new(30, 300, 999)]); // cache ancien : ne pas l'ajouter au snapshot frais
        s.Inventory.Seed(2, [new(99, 999, 7)]);
        Map(s, "_hdvOpen")[1u] = true; Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1, Item(10, 100, 5), Item(11, 200, 1, 0), Item(20, 100, 40));
        Assert.True(s.HdvBankInventoryReady(1));
        Assert.Equal(5, s.Inventory.Get(1, 10)!.Value.Qty); Assert.Null(s.Inventory.Get(1, 20));
        var rows = s.HdvMergedItems(1);
        Assert.Equal(3, rows.Count);
        Assert.True(rows.Single(x => x.Item.Uid == 20).InBank);
        Assert.Equal(40, rows.Single(x => x.Item.Uid == 20).Item.Qty);
        Assert.Equal(40, s.HdvSellableQuantity(1, 20));
        Assert.Equal(0, s.HdvSellableQuantity(1, 11));
        Assert.False(rows.Single(x => x.Item.Uid == 10).InBank);
        Assert.Equal(0, rows.Single(x => x.Item.Uid == 11).Item.Position);
        Assert.Equal(7, s.Inventory.Get(2, 99)!.Value.Qty);
    }

    [Fact]
    public void Closing_hdv_keeps_fresh_bag_quantities_and_never_restores_sold_stock()
    {
        var s = Service(); s.Inventory.Seed(1, [new(10, 100, 8)]);
        Map(s, "_hdvOpen")[1u] = true; Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1, Item(10, 100, 3), Item(20, 100, 40));
        Call(s, "ResetHdvState", 1u);
        Assert.False(s.HdvBankInventoryReady(1));
        Assert.Equal(3, s.Inventory.Get(1, 10)!.Value.Qty); Assert.Null(s.Inventory.Get(1, 20));
        Assert.Single(s.HdvMergedItems(1));
    }

    [Fact]
    public void Bank_sale_increment_updates_the_merged_quantity_without_adding_it_to_the_bag()
    {
        var s = Service(); s.Inventory.Seed(1, [new(10, 100, 8)]);
        Map(s, "_hdvOpen")[1u] = true; Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1, Item(10, 100, 8), Item(20, 100, 40));
        var debit = M(F("ObjectQuantityEvent", "object"),
            V(F("ObjectUidWithQuantity", "object_uid"), 20), V(F("ObjectUidWithQuantity", "quantity"), 30));
        Call(s, "ObserveHdvMergedInventory", new Packet(1, false, "", "ObjectQuantityEvent", [debit], []));
        Assert.Equal(30, s.HdvSellableQuantity(1, 20)); Assert.Null(s.Inventory.Get(1, 20));
        Call(s, "ObserveHdvMergedInventory", new Packet(1, false, "", "ObjectDeletedEvent", [V(F("ObjectDeletedEvent", "object_uid"), 20)], []));
        Assert.Equal(0, s.HdvSellableQuantity(1, 20));
    }

    [Fact]
    public async Task Seller_readiness_waits_for_post_store_publication_and_ignores_other_pids()
    {
        var s = Service();
        var ready = (Task<bool>)Call(s, "WaitForHdvSellerReadyAsync", 1u, CancellationToken.None)!;
        Assert.False(ready.IsCompleted);
        Call(s, "SetHdvBuyerMode", 2u);
        Assert.False(ready.IsCompleted);
        ((Action<uint>)Field("HdvUpdated").GetValue(s)!)(1); // autre rafraîchissement, vendeur pas encore publié
        Assert.False(ready.IsCompleted);
        Map(s, "_hdvOpen")[1u] = true;
        ((Action<uint>)Field("HdvUpdated").GetValue(s)!)(1);
        Assert.True(await ready);
        Assert.Null(Field("HdvUpdated").GetValue(s));
    }

    [Fact]
    public async Task Seller_readiness_cancellation_unsubscribes_without_claiming_success()
    {
        var s = Service();
        using var stop = new CancellationTokenSource();
        var ready = (Task<bool>)Call(s, "WaitForHdvSellerReadyAsync", 1u, stop.Token)!;
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ready);
        Assert.Null(Field("HdvUpdated").GetValue(s));
    }

    [Fact]
    public void Switching_to_buyer_invalidates_seller_stock_and_requires_a_new_bank_snapshot()
    {
        var s = Service(); s.Inventory.Seed(1, [new(10, 100, 8)]);
        Map(s, "_hdvOpen")[1u] = true; Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1, Item(10, 100, 3), Item(20, 100, 40));
        Call(s, "SetHdvBuyerMode", 1u);
        Assert.True(s.HdvAnyOpen(1)); Assert.True(s.HdvBuyerOpen(1)); Assert.False(s.HdvSellerOpen(1));
        Assert.False(s.HdvBankInventoryReady(1)); Assert.Equal(0, s.HdvSellableQuantity(1, 20));
        Assert.Equal(3, s.Inventory.Get(1, 10)!.Value.Qty);
        Map(s, "_hdvOpen")[1u] = true;
        Assert.False(s.HdvBankInventoryReady(1));
    }

    [Fact]
    public void Empty_merged_snapshot_keeps_the_known_bag()
    {
        var s = Service();
        s.Inventory.Seed(1, [new(10, 100, 8), new(11, 200, 1, 0)]);
        Map(s, "_hdvOpen")[1u] = true;
        Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1);
        Assert.Equal(8, s.Inventory.Get(1, 10)!.Value.Qty);
        Assert.Equal(0, s.Inventory.Get(1, 11)!.Value.Position);
        Assert.False(s.HdvBankInventoryReady(1));
        Assert.Equal(2, s.HdvMergedItems(1).Count);
        Assert.Contains(s.HdvMergedItems(1), row => row.Item.Uid == 10 && !row.InBank);
    }

    [Fact]
    public void Bank_snapshot_replaces_instead_of_appending_removed_remote_objects()
    {
        var s = Service(); s.Inventory.Seed(1, [new(10, 100, 8)]);
        Map(s, "_hdvOpen")[1u] = true; Map(s, "_hdvMergedRequested")[1u] = true;
        Seed(s, 1, Item(10, 100, 8), Item(20, 100, 40));
        Seed(s, 1, Item(10, 100, 2));
        Assert.Single(s.HdvMergedItems(1)); Assert.Equal(2, s.Inventory.Get(1, 10)!.Value.Qty);
        Assert.Null(Call(s, "HdvSellStack", 1u, 20));
    }
}
