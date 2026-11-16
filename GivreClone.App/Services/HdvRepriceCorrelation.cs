using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// A reprice is confirmed only after the exact old listing disappears and a
/// listing appears with the requested item, lot size and price. The server may
/// preserve its UID (observed on 2026-10-27) or assign a different one.
/// An unrelated ItemAdded event must never acknowledge this request.
/// </summary>
public sealed class HdvRepriceCorrelation
{
    private readonly int _oldUid;
    private readonly long _gid, _price;
    private readonly int _quantity;
    private readonly Func<string, string, int?> _field;
    private bool _oldRemoved, _ambiguous;
    private int? _newUid;

    public int? NewUid => _oldRemoved && !_ambiguous ? _newUid : null;

    public HdvRepriceCorrelation(int oldUid, long gid, int quantity, long price, Func<string, string, int?> field)
    {
        _oldUid = oldUid;
        _gid = gid;
        _quantity = quantity;
        _price = price;
        _field = field;
    }

    private ulong? Number(string type, string name, IReadOnlyList<ProtoField> payload)
    {
        int? field = _field(type, name);
        return field is > 0 ? payload.FirstOrDefault(f => f.Field == field && f.Wire == WireType.Varint)?.Varint : null;
    }

    // Numeric HDV evidence only: no inventory effects, account or packet bytes.
    public string Describe(string? name, IReadOnlyList<ProtoField> payload)
    {
        var itemField = name == "ExchangeBidHouseItemAddedEvent" ? _field(name, "item") : null;
        var item = itemField is > 0 ? payload.FirstOrDefault(f => f.Field == itemField)?.Message : null;
        return $"event={name} removed={(name == "ExchangeBidHouseItemRemovedEvent" ? Number("ExchangeBidHouseItemRemovedEvent", "sell_id", payload) : null)} " +
            $"uid={(item is null ? null : Number("BidItem", "uid", item))} " +
            $"gid={(item is null ? null : Number("BidItem", "gid", item))} " +
            $"qty={(item is null ? null : Number("BidItem", "quantity", item))} " +
            $"price={(name == "ExchangeBidHouseItemAddedEvent" ? Number("ExchangeBidHouseItemAddedEvent", "price", payload) : null)} " +
            $"oldRemoved={_oldRemoved} ambiguous={_ambiguous}";
    }

    public HdvSellDisposition Observe(string? name, IReadOnlyList<ProtoField> payload)
    {
        if (name is "ExchangeErrorEvent" or "ObjectErrorEvent")
            return HdvSellDisposition.Refused;

        if (name == "ExchangeBidHouseItemRemovedEvent" &&
            Number("ExchangeBidHouseItemRemovedEvent", "sell_id", payload) == (ulong)_oldUid)
            _oldRemoved = true;

        int? itemField = name == "ExchangeBidHouseItemAddedEvent" ? _field(name, "item") : null;
        if (itemField is > 0 &&
            payload.FirstOrDefault(f => f.Field == itemField.Value)?.Message is { } item)
        {
            var uid = Number("BidItem", "uid", item);
            var gid = Number("BidItem", "gid", item);
            var quantity = Number("BidItem", "quantity", item);
            var price = Number("ExchangeBidHouseItemAddedEvent", "price", payload);
            if (uid is > 0 and <= int.MaxValue &&
                gid == (ulong)_gid && quantity == (ulong)_quantity && price == (ulong)_price)
            {
                if (_newUid is { } previous && previous != (int)uid.Value) _ambiguous = true;
                else _newUid = (int)uid.Value;
            }
        }

        return _oldRemoved && !_ambiguous && _newUid is > 0
            ? HdvSellDisposition.Confirmed : HdvSellDisposition.NeedsReview;
    }
}
