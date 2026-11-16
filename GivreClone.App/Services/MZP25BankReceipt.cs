using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// Pure server-event correlation for bank operations. An event TYPE alone is
/// never an acknowledgement of the requested UID/quantity; a packet sent to
/// the server is not a successful bank transfer.
/// </summary>
public static class MZP25BankReceipt
{
    /// <summary>Total exact attendu ; pas de Math.Abs (long.MinValue), ni dépassement arithmétique.</summary>
    public static long? ExpectedKamas(long before, long signedQty)
    {
        if (before < 0 || signedQty == 0) return null;
        try { long after = checked(before + signedQty); return after >= 0 ? after : null; }
        catch (OverflowException) { return null; }
    }

    public static bool ConfirmKamas(IReadOnlyList<ProtoField> payload, long expected,
        Func<string, string, int?> lookup)
    {
        int field = Field(lookup, "StorageKamasUpdateEvent", "kamas");
        return field > 0 && expected >= 0 && (Number(payload, field) ?? 0) == expected;
    }

    private static int Field(Func<string, string, int?> lookup, string message, string property)
        => lookup(message, property) is int n && n > 0 ? n : -1;

    private static long? Number(IReadOnlyList<ProtoField> fields, int index)
        => index > 0 && fields.FirstOrDefault(x => x.Field == index && x.Wire == WireType.Varint) is { } f
            ? unchecked((long)f.Varint) : null;

    private static IReadOnlyList<ProtoField>? Message(IReadOnlyList<ProtoField> fields, int index)
        => index > 0 ? fields.FirstOrDefault(x => x.Field == index && x.Message is not null)?.Message : null;

    private static InventoryReader.Item? UpdatedItem(IReadOnlyList<ProtoField> fields, int field,
        Func<string, string, int?> lookup)
    {
        var inventory = Message(fields, field);
        var item = inventory is null ? null : Message(inventory, Field(lookup, "ObjectItemInventory", "item"));
        if (item is null) return null;
        var uid = Number(item, Field(lookup, "ObjectItem", "uid"));
        var gid = Number(item, Field(lookup, "ObjectItem", "gid"));
        int quantityField = Field(lookup, "ObjectItem", "quantity");
        if (quantityField <= 0) return null;
        var qty = Number(item, quantityField) ?? 0;
        if (uid is null or <= 0 || gid is null or <= 0 || qty < 0 || qty > int.MaxValue)
            return null;
        return new InventoryReader.Item((int)uid.Value, gid.Value, (int)qty, 63);
    }

    /// <summary>
    /// Number of objects demonstrably removed from exactly this bank stack by
    /// this inbound event. Any missing field/uid/gid or impossible quantity
    /// rejects the receipt. Matching a different stack is not enough.
    /// </summary>
    public static int ConfirmWithdrawal(string? name, IReadOnlyList<ProtoField> payload,
        int uid, long gid, int before, int requested,
        Func<string, string, int?> lookup)
    {
        if (uid <= 0 || gid <= 0 || before <= 0 || requested <= 0 || requested > before) return 0;
        if (name == "StorageObjectRemovedEvent")
        {
            int f = Field(lookup, name, "object_uid");
            return Number(payload, f) == uid && requested == before ? before : 0;
        }
        if (name == "StorageObjectsRemovedEvent")
        {
            int f = Field(lookup, name, "objects_uid");
            return f > 0 && InventoryReader.ReadUids(payload, f).Contains(uid) && requested == before
                ? before : 0;
        }
        int content = name switch
        {
            "StorageObjectUpdateEvent" => Field(lookup, name, "object"),
            "StorageObjectsUpdateEvent" => Field(lookup, name, "objects"),
            _ => -1
        };
        if (content <= 0) return 0;
        foreach (var field in payload.Where(f => f.Field == content && f.Message is not null))
        {
            var item = UpdatedItem(new[] { field }, content, lookup);
            if (item is not { } it || it.Uid != uid || it.Gid != gid) continue;
            int delta = before - it.Qty;
            if (delta > 0 && delta <= requested) return delta;
        }
        return 0;
    }

    /// <summary>
    /// A bag UID was completely transferred if the server removes the selected
    /// UID, or sets that UID's quantity to zero. Deposit confirmation is based
    /// on bag events, not arbitrary StorageObjectUpdateEvent of another item.
    /// </summary>
    public static IReadOnlyCollection<int> DepositedBagUids(string? name,
        IReadOnlyList<ProtoField> payload, Func<string, string, int?> lookup)
    {
        if (name == "ObjectDeletedEvent")
        {
            var u = Number(payload, Field(lookup, name, "object_uid"));
            return u is > 0 and <= int.MaxValue ? new[] { (int)u.Value } : Array.Empty<int>();
        }
        if (name == "ObjectsDeletedEvent")
        {
            int f = Field(lookup, name, "objects_uid");
            return f > 0 ? InventoryReader.ReadUids(payload, f) : Array.Empty<int>();
        }
        int objectField = name switch
        {
            "ObjectQuantityEvent" => Field(lookup, name, "object"),
            "ObjectsQuantityEvent" => Field(lookup, name, "object"),
            _ => -1,
        };
        if (objectField <= 0) return Array.Empty<int>();
        int uidField = Field(lookup, "ObjectUidWithQuantity", "object_uid");
        int qtyField = Field(lookup, "ObjectUidWithQuantity", "quantity");
        if (uidField <= 0 || qtyField <= 0) return Array.Empty<int>();
        var result = new List<int>();
        foreach (var f in payload)
        {
            if (f.Field != objectField || f.Message is not { } entry) continue;
            var u = Number(entry, uidField);
            // Proto3 omits zero quantity; this is safe when field mapping is known.
            var q = Number(entry, qtyField) ?? 0;
            if (u is > 0 and <= int.MaxValue && q == 0) result.Add((int)u.Value);
        }
        return result;
    }
}
