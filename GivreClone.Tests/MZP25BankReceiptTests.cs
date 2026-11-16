using GivreClone.App.Services;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP25BankReceiptTests
{
    private static readonly Dictionary<(string, string), int> Fields = new()
    {
        [("StorageObjectRemovedEvent", "object_uid")] = 1,
        [("StorageObjectsRemovedEvent", "objects_uid")] = 2,
        [("StorageObjectUpdateEvent", "object")] = 1,
        [("StorageObjectsUpdateEvent", "objects")] = 1,
        [("ObjectItemInventory", "item")] = 1,
        [("ObjectItem", "uid")] = 4,
        [("ObjectItem", "gid")] = 1,
        [("ObjectItem", "quantity")] = 3,
        [("ObjectDeletedEvent", "object_uid")] = 1,
        [("ObjectsDeletedEvent", "objects_uid")] = 1,
        [("ObjectQuantityEvent", "object")] = 3,
        [("ObjectsQuantityEvent", "object")] = 1,
        [("ObjectUidWithQuantity", "object_uid")] = 2,
        [("ObjectUidWithQuantity", "quantity")] = 3,
    };

    private static int? F(string msg, string field) => Fields.GetValueOrDefault((msg, field), -1);
    private static ProtoField V(int id, long value) => new() { Field = id, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(int id, params ProtoField[] children) =>
        new() { Field = id, Wire = WireType.Len, Message = children.ToList() };
    private static ProtoField Entry(int uid, long gid, int qty) =>
        M(1, M(1, V(4, uid), V(1, gid), V(3, qty)));

    [Fact]
    public void Unrelated_uid_or_name_can_never_acknowledge_a_withdrawal()
    {
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectRemovedEvent",
            new[] { V(1, 999) }, 123, 421, 100, 100, F));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("UnrelatedEvent",
            new[] { V(1, 123) }, 123, 421, 100, 100, F));
    }

    [Fact]
    public void Full_removal_only_confirms_a_full_stack_request()
    {
        Assert.Equal(100, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectRemovedEvent",
            new[] { V(1, 123) }, 123, 421, 100, 100, F));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectRemovedEvent",
            new[] { V(1, 123) }, 123, 421, 100, 10, F));
    }

    [Fact]
    public void Update_confirms_only_matching_uid_gid_and_positive_delta()
    {
        Assert.Equal(8, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectUpdateEvent",
            new[] { Entry(123, 421, 92) }, 123, 421, 100, 10, F));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectUpdateEvent",
            new[] { Entry(123, 999, 92) }, 123, 421, 100, 10, F));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectUpdateEvent",
            new[] { Entry(123, 421, 80) }, 123, 421, 100, 10, F));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectUpdateEvent",
            new[] { Entry(123, 421, 101) }, 123, 421, 100, 10, F));
    }

    [Fact]
    public void Batched_update_can_ignore_unrelated_stacks()
    {
        Assert.Equal(5, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectsUpdateEvent",
            new[] { Entry(999, 999, 5), Entry(123, 421, 95) },
            123, 421, 100, 5, F));
    }

    [Fact]
    public void Deposit_requires_a_bag_delta_not_any_storage_update()
    {
        Assert.Empty(MZP25BankReceipt.DepositedBagUids("StorageObjectUpdateEvent", new[] { Entry(123, 421, 10) }, F));
        Assert.Equal(new[] { 123 }, MZP25BankReceipt.DepositedBagUids(
            "ObjectDeletedEvent", new[] { V(1, 123) }, F));
        Assert.Equal(new[] { 123 }, MZP25BankReceipt.DepositedBagUids(
            "ObjectQuantityEvent", new[] { M(3, V(2, 123)) }, F));
        Assert.Empty(MZP25BankReceipt.DepositedBagUids(
            "ObjectQuantityEvent", new[] { M(3, V(2, 123), V(3, 4)) }, F));
    }

    [Fact]
    public void Unknown_mapping_fails_closed()
    {
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectUpdateEvent",
            new[] { Entry(123, 421, 0) }, 123, 421, 100, 100,
            (message, field) => (message, field) == ("ObjectItem", "quantity") ? null : F(message, field)));
        Assert.Equal(0, MZP25BankReceipt.ConfirmWithdrawal("StorageObjectRemovedEvent",
            new[] { V(1, 123) }, 123, 421, 100, 100, (_, _) => null));
        Assert.Empty(MZP25BankReceipt.DepositedBagUids("ObjectDeletedEvent",
            new[] { V(1, 123) }, (_, _) => null));
    }
}
