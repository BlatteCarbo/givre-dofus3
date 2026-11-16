using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP27BankDepositTrackerTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static ProtoField Uid(int uid) => new()
    {
        Field = Mapping.FieldNumber("ObjectDeletedEvent", "object_uid")!.Value,
        Wire = WireType.Varint,
        Varint = (ulong)uid
    };

    [Fact]
    public void Every_expected_stack_requires_its_own_server_receipt()
    {
        var receipt = new MZP27BankDepositTracker(new[] { 21, 22 });
        Assert.False(receipt.IsComplete);
        Assert.False(receipt.Observe("ObjectDeletedEvent", new[] { Uid(999) }, Mapping.FieldNumber));
        Assert.False(receipt.Observe("ObjectDeletedEvent", new[] { Uid(21) }, Mapping.FieldNumber));
        Assert.False(receipt.Observe("ObjectDeletedEvent", new[] { Uid(21) }, Mapping.FieldNumber));
        Assert.Equal(1, receipt.ConfirmedCount);
        Assert.True(receipt.Observe("ObjectDeletedEvent", new[] { Uid(22) }, Mapping.FieldNumber));
        Assert.True(receipt.IsComplete);
        Assert.Equal(2, receipt.ConfirmedCount);
    }

    [Fact]
    public void Unmapped_and_storage_only_events_do_not_confirm_deposit()
    {
        var receipt = new MZP27BankDepositTracker(new[] { 21 });
        Assert.False(receipt.Observe("ObjectDeletedEvent", new[] { Uid(21) }, (_, _) => null));
        Assert.False(receipt.Observe("StorageObjectUpdateEvent", new[] { Uid(21) }, Mapping.FieldNumber));
        Assert.Equal(0, receipt.ConfirmedCount);
    }

    [Fact]
    public void Zero_quantity_server_event_confirms_only_the_requested_uid()
    {
        int outer = Mapping.FieldNumber("ObjectQuantityEvent", "object")!.Value;
        int uid = Mapping.FieldNumber("ObjectUidWithQuantity", "object_uid")!.Value;
        int quantity = Mapping.FieldNumber("ObjectUidWithQuantity", "quantity")!.Value;
        static ProtoField V(int field, ulong value) =>
            new() { Field = field, Wire = WireType.Varint, Varint = value };
        ProtoField Item(int id, int qty) => new()
        {
            Field = outer, Wire = WireType.Len, Message =
                new List<ProtoField> { V(uid, (ulong)id), V(quantity, (ulong)qty) }
        };
        var receipt = new MZP27BankDepositTracker(new[] { 41, 42 });
        Assert.False(receipt.Observe("ObjectQuantityEvent", new[] { Item(41, 1) }, Mapping.FieldNumber));
        Assert.False(receipt.Observe("ObjectQuantityEvent", new[] { Item(99, 0) }, Mapping.FieldNumber));
        Assert.False(receipt.Observe("ObjectQuantityEvent", new[] { Item(41, 0) }, Mapping.FieldNumber));
        Assert.False(receipt.Observe("ObjectQuantityEvent", new[] { Item(41, 0) }, Mapping.FieldNumber));
        Assert.Equal(1, receipt.ConfirmedCount);
        Assert.True(receipt.Observe("ObjectQuantityEvent", new[] { Item(42, 0) }, Mapping.FieldNumber));
        Assert.Equal(2, receipt.ConfirmedCount);
    }

    [Fact]
    public void Empty_or_duplicate_expected_uid_set_cannot_be_counted_as_success()
    {
        var empty = new MZP27BankDepositTracker(new[] { 0, -1, 0 });
        Assert.Equal(0, empty.ExpectedCount);
        Assert.False(empty.Observe("ObjectDeletedEvent", new[] { Uid(1) }, Mapping.FieldNumber));
        Assert.False(empty.IsComplete);

        var duplicates = new MZP27BankDepositTracker(new[] { 17, 17, -4, 18 });
        Assert.Equal(2, duplicates.ExpectedCount);
        Assert.False(duplicates.Observe("ObjectDeletedEvent", new[] { Uid(17) }, Mapping.FieldNumber));
        Assert.False(duplicates.Observe("ObjectDeletedEvent", new[] { Uid(17) }, Mapping.FieldNumber));
        Assert.True(duplicates.Observe("ObjectDeletedEvent", new[] { Uid(18) }, Mapping.FieldNumber));
        Assert.Equal(2, duplicates.ConfirmedCount);
    }
}
