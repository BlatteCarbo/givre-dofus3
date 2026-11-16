using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

[Collection("F08InventoryMapping")]
public sealed class F08InventoryBankTests
{
    private const uint Pid = 8;
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Mapping.FieldNumber(message, field)!.Value;
    private static ProtoField V(int field, long value) => new() { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(int field, params ProtoField[] children) => new() { Field = field, Wire = WireType.Len, Message = children.ToList() };
    private static ProtoField[] Movement(int uid, int position) => position == 0
        ? new[] { V(F("ObjectMovementEvent", "object_uid"), uid) }
        : new[] { V(F("ObjectMovementEvent", "object_uid"), uid), V(F("ObjectMovementEvent", "position"), position) };
    private static ProtoField[] Quantity(int uid, int qty) => new[]
    {
        M(F("ObjectQuantityEvent", "object"), V(F("ObjectUidWithQuantity", "object_uid"), uid), V(F("ObjectUidWithQuantity", "quantity"), qty))
    };
    private static ProtoField[] Deleted(int uid) => new[] { V(F("ObjectDeletedEvent", "object_uid"), uid) };
    private static ProtoField[] Added(int uid, long gid, int qty, int position, int effectValue = 0)
    {
        var item = new List<ProtoField> { V(F("ObjectItem", "uid"), uid), V(F("ObjectItem", "gid"), gid), V(F("ObjectItem", "quantity"), qty) };
        if (effectValue != 0) item.Add(M(F("ObjectItem", "effects"), V(F("ObjectEffect", "action"), 118), V(F("ObjectEffect", "value_int"), effectValue)));
        return new[] { M(F("ObjectAddedEvent", "object"), M(F("ObjectItemInventory", "item"), item.ToArray()), V(F("ObjectItemInventory", "position"), position)) };
    }
    private static DecodedPacket Packet(string name, ProtoField[] payload, uint pid = Pid, bool outbound = false)
        => new(pid, outbound, "fixture", name, payload, Array.Empty<byte>());
    private static InventoryOperationReceipt Receipt(InventoryStore.Item source, int? position, params InventoryStore.Item[] others)
        => new(source, position, new[] { source }.Concat(others).ToArray(), _ => Array.Empty<InventoryReader.ItemEffect>());

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(28)]
    [InlineData(63)]
    public void Movement_requires_exact_uid_and_target_including_proto3_zero(int target)
    {
        var source = new InventoryStore.Item(10, 2476, 1, target == 63 ? 5 : 63);
        var receipt = Receipt(source, target);
        Assert.False(receipt.Observe("ObjectMovementEvent", Movement(99, target)));
        Assert.False(receipt.Observe("ObjectMovementEvent", Movement(10, target == 2 ? 5 : 2)));
        Assert.True(receipt.Observe("ObjectMovementEvent", Movement(10, target)));
        Assert.Equal(10, receipt.ResultUid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void F02_split_confirms_new_uid_only_after_both_server_events(bool addedFirst)
    {
        // 24/10 10:36:14 : demande UID 5480257, ajout UID 2505714, GID 2476, position 5.
        var receipt = Receipt(new(5480257, 2476, 2), 5);
        var added = Added(2505714, 2476, 1, 5);
        var quantity = Quantity(5480257, 1);
        Assert.False(receipt.Observe(addedFirst ? "ObjectAddedEvent" : "ObjectQuantityEvent", addedFirst ? added : quantity));
        Assert.True(receipt.Observe(addedFirst ? "ObjectQuantityEvent" : "ObjectAddedEvent", addedFirst ? quantity : added));
        Assert.Equal(2505714, receipt.ResultUid);
    }

    [Fact]
    public void Split_does_not_accept_a_different_effect_roll_or_unrelated_gid()
    {
        var source = new InventoryStore.Item(10, 2476, 2);
        var effect = new InventoryReader.ItemEffect(118, 5, 0, 0, 0, null, 0);
        var receipt = new InventoryOperationReceipt(source, 5, new[] { source }, _ => new[] { effect });
        Assert.False(receipt.Observe("ObjectQuantityEvent", Quantity(10, 1)));
        Assert.False(receipt.Observe("ObjectAddedEvent", Added(20, 2477, 1, 5, 5)));
        Assert.False(receipt.Observe("ObjectAddedEvent", Added(21, 2476, 1, 5, 6)));
        Assert.True(receipt.Observe("ObjectAddedEvent", Added(22, 2476, 1, 5, 5)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unequip_merge_requires_source_deletion_and_matching_bag_delta(bool deleteFirst)
    {
        var receipt = Receipt(new(10, 2476, 1, 5), 63, new InventoryStore.Item(20, 2476, 4));
        Assert.False(receipt.Observe(deleteFirst ? "ObjectDeletedEvent" : "ObjectQuantityEvent", deleteFirst ? Deleted(10) : Quantity(20, 5)));
        Assert.True(receipt.Observe(deleteFirst ? "ObjectQuantityEvent" : "ObjectDeletedEvent", deleteFirst ? Quantity(20, 5) : Deleted(10)));
        Assert.Equal(20, receipt.ResultUid);
    }

    [Fact]
    public void Use_requires_consumption_of_requested_uid_and_does_not_mutate_store()
    {
        var store = new InventoryStore();
        store.Seed(Pid, new[] { new InventoryStore.Item(10, 2476, 2) });
        var receipt = Receipt(store.Get(Pid, 10)!.Value, null);
        Assert.False(receipt.Observe("ObjectQuantityEvent", Quantity(20, 0)));
        Assert.False(receipt.Observe("ObjectQuantityEvent", Quantity(10, 3)));
        Assert.True(receipt.Observe("ObjectQuantityEvent", Quantity(10, 1)));
        Assert.Equal(2, store.Get(Pid, 10)?.Qty);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Item_service_never_promotes_an_accepted_send_to_server_confirmation(bool send, bool response, bool expected)
    {
        var service = new LaunchService();
        service.Inventory.Upsert(Pid, new(10, 2476, 1));
        service.Relay.Accept = send;
        service.Responses.Add(Packet("ObjectMovementEvent", Movement(10, 5), pid: 99));
        service.Responses.Add(Packet("ObjectMovementEvent", Movement(10, 5), outbound: true));
        if (response) service.Responses.Add(Packet("ObjectMovementEvent", Movement(10, 5)));
        var result = await service.InventoryItemConfirmedAsync(Pid, 10, 5);
        Assert.Equal(send, result.Sent);
        Assert.Equal(expected, result.ServerConfirmed);
        Assert.Equal(63, service.Inventory.Get(Pid, 10)?.Position); // aucun déplacement optimiste
    }

    [Fact]
    public async Task Object_error_refuses_item_operation_without_erasing_inventory()
    {
        var service = new LaunchService();
        service.Inventory.Upsert(Pid, new(10, 2476, 2));
        service.Responses.Add(Packet("ObjectErrorEvent", Array.Empty<ProtoField>()));
        var result = await service.InventoryItemConfirmedAsync(Pid, 10, 5);
        Assert.True(result.Rejected);
        Assert.False(result.ServerConfirmed);
        Assert.Equal(2, service.Inventory.Get(Pid, 10)?.Qty);
    }

    [Theory]
    [InlineData(14964, true, true)]
    [InlineData(14964, false, false)]
    [InlineData(2476, true, false)]
    public async Task Companion_equipment_requires_item_slot_permission_and_server_confirmation(long gid, bool response, bool confirmed)
    {
        var service = new LaunchService();
        service.Inventory.Upsert(Pid, new(10, gid, 1));
        if (response) service.Responses.Add(Packet("ObjectMovementEvent", Movement(10, 28)));

        var result = await service.InventoryItemConfirmedAsync(Pid, 10, 28);

        Assert.Equal(gid == 14964, result.Sent);
        Assert.Equal(confirmed, result.ServerConfirmed);
        Assert.Equal(63, service.Inventory.Get(Pid, 10)?.Position);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Bank_close_preserves_open_state_until_server_leave(bool send, bool response, bool stillOpen)
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Bank.Upsert(Pid, new(10, 2476, 20));
        service.Relay.Accept = send;
        if (response) service.Responses.Add(Packet("ExchangeLeaveEvent", Array.Empty<ProtoField>()));
        Assert.Equal(send && response, await service.BankCloseAsync(Pid));
        Assert.Equal(stillOpen, service.StorageOpen(Pid));
        Assert.Equal(20, service.Bank.Get(Pid, 10)?.Qty);
    }

    [Fact]
    public async Task Close_receipt_does_not_erase_a_bank_reopened_before_the_continuation()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Responses.Add(Packet("ExchangeLeaveEvent", Array.Empty<ProtoField>()));
        service.BeforeWaitReturn = () => service.OpenBank(Pid);
        Assert.True(await service.BankCloseAsync(Pid));
        Assert.True(service.StorageOpen(Pid));
    }

    [Fact]
    public async Task Already_open_empty_bag_deposit_is_a_noop_followed_by_confirmed_close()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Inventory.Seed(Pid, Array.Empty<InventoryStore.Item>());
        service.Responses.Add(Packet("ExchangeLeaveEvent", Array.Empty<ProtoField>()));
        Assert.True(await service.BankDepositAllAsync(Pid, service.Map));
        Assert.Equal(1, service.Relay.Sends); // fermeture seule, aucun dialogue ou transfert supplémentaire
        Assert.False(service.StorageOpen(Pid));
    }

    [Fact]
    public async Task Deposit_refuses_permission_revoked_during_bank_open_and_still_closes()
    {
        LicenseService.GatherAllowed = true;
        var service = new LaunchService();
        service.Inventory.Seed(Pid, new[] { new InventoryStore.Item(10, 2476, 2) });
        service.Responses.Add(Packet("NpcDialogQuestionEvent", Array.Empty<ProtoField>()));
        service.Responses.Add(Packet("StorageInventoryContentEvent", Array.Empty<ProtoField>()));
        service.Responses.Add(Packet("ExchangeLeaveEvent", Array.Empty<ProtoField>()));
        service.BeforeWaitReturn = () =>
        {
            if (service.RequestedNames.Last().Contains("StorageInventoryContentEvent"))
                LicenseService.GatherAllowed = false;
        };
        service.Relay.OnSend = () =>
        {
            if (!LicenseService.GatherAllowed)
                Assert.Equal(new[] { "ExchangeLeaveEvent" }, service.RequestedNames.Last());
        };

        try
        {
            Assert.False(await service.BankDepositAllAsync(Pid, service.Map));
            Assert.False(service.StorageOpen(Pid));
            Assert.Equal(3, service.Relay.Sends);
            Assert.Equal(2, service.Inventory.Get(Pid, 10)?.Qty);
        }
        finally { LicenseService.GatherAllowed = true; }
    }

    [Fact]
    public void Bank_verbs_refuse_a_closed_bank_even_with_a_known_inventory()
    {
        var service = new LaunchService();
        service.Inventory.Upsert(Pid, new(10, 2476, 2));
        Assert.False(service.BankDepositAll(Pid));
        Assert.False(service.BankDepositExisting(Pid));
        Assert.False(service.BankWithdrawAll(Pid));
        Assert.False(service.BankWithdrawExisting(Pid));
        Assert.False(service.BankDepositList(Pid, new[] { 10 }));
        Assert.False(service.BankDepositItem(Pid, 2476, 1));
        Assert.Equal(0, service.Relay.Sends);
    }

    [Fact]
    public async Task Unknown_inventory_cannot_be_treated_as_an_empty_successful_deposit()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        Assert.False(await service.BankDepositAllAsync(Pid, service.Map));
        Assert.True(service.StorageOpen(Pid));
        Assert.Equal(0, service.Relay.Sends);
    }

    [Fact]
    public void Leaving_and_returning_to_same_map_never_resurrects_a_bank_session()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Emit(Packet("DialogLeaveEvent", Array.Empty<ProtoField>())); // fin de dialogue PNJ, pas du coffre
        service.Emit(Packet("MapCurrentEvent", Array.Empty<ProtoField>(), outbound: true));
        Assert.True(service.StorageOpen(Pid));
        service.Emit(Packet("MapCurrentEvent", Array.Empty<ProtoField>()));
        service.Map++;
        Assert.False(service.StorageOpen(Pid));
        service.Map--;
        Assert.False(service.StorageOpen(Pid));
        Assert.Equal(100, service.BankKamas(Pid)); // historique conservé, sans prétendre être ouvert
    }

    [Fact]
    public async Task Partial_deposit_never_reports_success_or_retries_uncertain_transfers()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Inventory.Seed(Pid, new[] { new InventoryStore.Item(10, 100, 10), new(20, 200, 20) });
        using var cancellation = new CancellationTokenSource();
        service.Relay.OnSend = () =>
        {
            service.Emit(Packet("ObjectDeletedEvent", Deleted(10)));
            cancellation.Cancel(); // clôt l'attente incomplète sans attendre le délai réel de 3 secondes
        };
        service.Responses.Add(Packet("ExchangeLeaveEvent", Array.Empty<ProtoField>()));
        Assert.False(await service.BankDepositAllAsync(Pid, service.Map, cancellation.Token));
        Assert.Equal(2, service.Relay.Sends); // transfert unique + fermeture
        Assert.Equal(20, service.Inventory.Get(Pid, 20)?.Qty);
        Assert.False(service.StorageOpen(Pid));
    }

    [Fact]
    public async Task Confirmed_withdrawal_publishes_exact_remaining_quantity_before_next_script_call()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Bank.Upsert(Pid, new(10, 2476, 10));
        ProtoField[] Update(int qty) => new[] { M(F("StorageObjectUpdateEvent", "object"), Added(10, 2476, qty, 63)[0].Message!.ToArray()) };
        service.Responses.Add(Packet("StorageObjectUpdateEvent", Update(7)));
        Assert.Equal(3, await service.BankWithdrawAsync(Pid, 2476, 3));
        Assert.Equal(7, service.Bank.Get(Pid, 10)?.Qty);
        service.Responses.Clear();
        service.Responses.Add(Packet("StorageObjectUpdateEvent", Update(4)));
        Assert.Equal(3, await service.BankWithdrawAsync(Pid, 2476, 3));
        Assert.Equal(4, service.Bank.Get(Pid, 10)?.Qty);
    }

    [Fact]
    public async Task Receipt_publication_does_not_overwrite_newer_server_quantities_or_kamas()
    {
        var service = new LaunchService();
        service.OpenBank(Pid, 100);
        service.Responses.Add(Packet("StorageKamasUpdateEvent", new[] { V(F("StorageKamasUpdateEvent", "kamas"), 110) }));
        service.BeforeWaitReturn = () => service.OpenBank(Pid, 120);
        Assert.True(await service.BankMoveKamasAsync(Pid, 10));
        Assert.Equal(120, service.BankKamas(Pid));
        service.Responses.Clear();
        service.Bank.Upsert(Pid, new(10, 2476, 10));
        service.Responses.Add(Packet("StorageObjectUpdateEvent", new[] {
            M(F("StorageObjectUpdateEvent", "object"), Added(10, 2476, 7, 63)[0].Message!.ToArray()) }));
        service.BeforeWaitReturn = () => service.Bank.SetQuantity(Pid, 10, 4);
        Assert.Equal(3, await service.BankWithdrawAsync(Pid, 2476, 3));
        Assert.Equal(4, service.Bank.Get(Pid, 10)?.Qty);
    }

    [Theory]
    [InlineData(110, true)]
    [InlineData(100, false)]
    [InlineData(105, false)]
    public async Task Kamas_receipt_requires_exact_delta_not_just_event_name(long total, bool confirmed)
    {
        var service = new LaunchService();
        service.OpenBank(Pid, 100);
        service.Responses.Add(Packet("StorageKamasUpdateEvent", new[] { V(F("StorageKamasUpdateEvent", "kamas"), total) }));
        Assert.Equal(confirmed, await service.BankMoveKamasAsync(Pid, 10));
        Assert.Equal(confirmed ? 110 : 100, service.BankKamas(Pid));
    }

    [Fact]
    public async Task Kamas_zero_is_proto3_default_but_overdraft_and_overflow_are_refused_before_send()
    {
        var service = new LaunchService();
        service.OpenBank(Pid, 100);
        service.Responses.Add(Packet("StorageKamasUpdateEvent", Array.Empty<ProtoField>()));
        Assert.True(await service.BankMoveKamasAsync(Pid, -100));
        Assert.Equal(0, service.BankKamas(Pid));
        int sent = service.Relay.Sends;
        Assert.False(await service.BankMoveKamasAsync(Pid, -101));
        Assert.False(await service.BankMoveKamasAsync(Pid, long.MaxValue));
        Assert.False(await service.BankMoveKamasAsync(Pid, long.MinValue));
        Assert.False(await service.BankMoveKamasAsync(Pid, service.Wallet + 1));
        Assert.Equal(sent, service.Relay.Sends);
        Assert.False(MZP25BankReceipt.ConfirmKamas(Array.Empty<ProtoField>(), 0, (_, _) => null));
    }

    [Fact]
    public async Task Cancelled_close_and_item_action_do_not_send_or_erase_state()
    {
        var service = new LaunchService();
        service.OpenBank(Pid);
        service.Inventory.Upsert(Pid, new(10, 2476, 1));
        var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BankCloseAsync(Pid, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InventoryItemConfirmedAsync(Pid, 10, 5, ct));
        Assert.True(service.StorageOpen(Pid));
        Assert.Equal(0, service.Relay.Sends);
    }

    [Fact]
    public void Use_plan_spans_bag_stacks_excludes_equipment_and_refuses_insufficient_stock()
    {
        var service = new LaunchService();
        service.Inventory.Seed(Pid, new[] { new InventoryStore.Item(10, 100, 1, 5), new(20, 100, 2), new(30, 100, 3) });
        Assert.Equal(new[] { (30, 3), (20, 1) }, service.Inventory.PlanBagUse(Pid, 100, 4));
        Assert.False(service.UseInventoryItem(Pid, 100, 6));
        Assert.Equal(0, service.Relay.Sends);
        Assert.True(service.UseInventoryItem(Pid, 100, 4));
        Assert.Equal(4, service.Relay.Sends);
        service.Relay.Accept = false;
        Assert.False(service.UseInventoryItem(Pid, 100, 1));
        Assert.Equal(6, service.Inventory.CountByGid(Pid, 100));
    }

    [Fact]
    public void Empty_server_entries_are_removed_and_concurrent_quantity_position_updates_are_not_lost()
    {
        var store = new InventoryStore();
        store.Upsert(Pid, new(10, 100, 2));
        store.Upsert(Pid, new(10, 100, 0));
        Assert.Null(store.Get(Pid, 10));
        store.Seed(Pid, new[] { new InventoryStore.Item(10, 100, 0), new(20, 100, 1) });
        Assert.Equal(new[] { 20 }, store.InventoryUids(Pid));
        for (int i = 0; i < 1000; i++)
        {
            int uid = i + 100;
            store.Upsert(Pid, new(uid, 100, 2));
            Parallel.Invoke(() => store.SetPosition(Pid, uid, 5), () => store.SetQuantity(Pid, uid, 1));
            Assert.Equal(new InventoryStore.Item(uid, 100, 1, 5), store.Get(Pid, uid));
        }
    }
}
