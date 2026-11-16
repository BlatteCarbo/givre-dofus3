using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// Correlates a listing UID to the originating inventory UID using two independent
/// inbound effects: the exact inventory stack must lose the requested quantity,
/// and a NEW seller listing must announce the same GID, lot size and price.
/// Neither an ItemAdded alone nor matching GID/price alone establishes this link.
/// One instance represents one serialized operation in one HDV session.
/// </summary>
public sealed class HdvSellCorrelation
{
    private readonly int _bagUid, _initialQty, _quantity;
    private readonly long _gid, _price;
    private readonly HashSet<int> _existingListings;
    private readonly Func<string, string, int?> _fieldNumber;
    private bool _inventoryDebit, _ambiguous;

    public int? SellId { get; private set; }

    public HdvSellCorrelation(int bagUid, long gid, int initialQty, int quantity, long price,
        IEnumerable<int> existingListingUids, Func<string, string, int?> fieldNumber)
    {
        _bagUid = bagUid;
        _gid = gid;
        _initialQty = initialQty;
        _quantity = quantity;
        _price = price;
        _existingListings = new HashSet<int>(existingListingUids);
        _fieldNumber = fieldNumber;
    }

    private ulong? Var(string type, string name, IReadOnlyList<ProtoField> payload)
    {
        int? field = _fieldNumber(type, name);
        if (field is not > 0) return null;
        return payload.FirstOrDefault(x => x.Field == field && x.Wire == WireType.Varint)?.Varint;
    }

    private IReadOnlyList<ProtoField>? Nested(string type, string name, IReadOnlyList<ProtoField> payload)
    {
        int? field = _fieldNumber(type, name);
        return field is > 0 ? payload.FirstOrDefault(x => x.Field == field)?.Message : null;
    }

    private void ObserveQuantity(IReadOnlyList<ProtoField> objectFields)
    {
        var uid = Var("ObjectUidWithQuantity", "object_uid", objectFields);
        var quantity = Var("ObjectUidWithQuantity", "quantity", objectFields) ?? 0;
        if (uid == (ulong)_bagUid && _initialQty >= _quantity &&
            quantity == (ulong)(_initialQty - _quantity))
            _inventoryDebit = true;
    }

    private static bool PackedContainsUid(byte[]? bytes, ulong uid)
    {
        if (bytes is not { Length: > 0 and <= 65536 }) return false;
        int pos = 0;
        bool found = false;
        while (pos < bytes.Length)
        {
            ulong value = 0;
            bool complete = false;
            for (int index = 0; index < 10 && pos < bytes.Length; index++)
            {
                byte b = bytes[pos++];
                if (index == 9 && b > 1) return false;
                value |= (ulong)(b & 0x7f) << (7 * index);
                if ((b & 0x80) == 0) { complete = true; break; }
            }
            if (!complete) return false;
            if (value == uid) found = true;
        }
        return found;
    }

    public HdvSellDisposition Observe(string? eventName, IReadOnlyList<ProtoField> payload)
    {
        if (eventName is "ExchangeErrorEvent" or "ObjectErrorEvent")
            return HdvSellDisposition.Refused;

        if (eventName == "ObjectQuantityEvent" && Nested(eventName, "object", payload) is { } quantity)
            ObserveQuantity(quantity);
        else if (eventName == "ObjectsQuantityEvent")
        {
            int? field = _fieldNumber(eventName, "object");
            if (field is > 0)
                foreach (var entry in payload.Where(x => x.Field == field && x.Message is not null))
                    ObserveQuantity(entry.Message!);
        }
        else if (eventName == "ObjectDeletedEvent")
        {
            if (_initialQty == _quantity && Var(eventName, "object_uid", payload) == (ulong)_bagUid)
                _inventoryDebit = true;
        }
        else if (eventName == "ObjectsDeletedEvent" && _initialQty == _quantity)
        {
            int? field = _fieldNumber(eventName, "objects_uid");
            if (field is > 0 && payload.Any(x => x.Field == field &&
                (x.Wire == WireType.Varint && x.Varint == (ulong)_bagUid ||
                 x.Wire == WireType.Len && PackedContainsUid(x.Bytes, (ulong)_bagUid))))
                _inventoryDebit = true;
        }
        else if (eventName == "ExchangeBidHouseItemAddedEvent" &&
            Nested(eventName, "item", payload) is { } item)
        {
            var uid = Var("BidItem", "uid", item);
            var gid = Var("BidItem", "gid", item);
            var qty = Var("BidItem", "quantity", item);
            var price = Var(eventName, "price", payload);
            if (uid is > 0 and <= int.MaxValue && gid == (ulong)_gid &&
                qty == (ulong)_quantity && price == (ulong)_price &&
                !_existingListings.Contains((int)uid.Value))
            {
                if (SellId is { } old && old != (int)uid.Value) _ambiguous = true;
                else SellId = (int)uid.Value;
            }
        }

        return HdvSellEvidence.Classify(SellId is > 0 ? "ExchangeBidHouseItemAddedEvent" : eventName,
            !_ambiguous && _inventoryDebit && SellId is > 0);
    }
}
