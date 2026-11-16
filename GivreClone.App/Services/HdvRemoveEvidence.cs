using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Refuse a withdrawal ACK for an unrelated listing. A bare sell_id is
/// sufficient only when the bag-to-listing association was previously proven.</summary>
public static class HdvRemoveEvidence
{
    public static HdvSellDisposition Classify(string? eventName, IReadOnlyList<ProtoField> payload,
        int expectedExchangeUid, bool sellIdLinked, Func<string, string, int?> fieldNumber)
    {
        if (eventName is "ExchangeErrorEvent" or "ObjectErrorEvent") return HdvSellDisposition.Refused;
        if (expectedExchangeUid <= 0 || eventName is null) return HdvSellDisposition.NeedsReview;
        var fieldName = eventName switch
        {
            "ExchangeBidHouseInListRemovedEvent" => "object_uid",
            "ExchangeBidHouseItemRemovedEvent" when sellIdLinked => "sell_id",
            _ => null
        };
        if (fieldName is null) return HdvSellDisposition.NeedsReview;
        int? field = fieldNumber(eventName, fieldName);
        if (field is not > 0) return HdvSellDisposition.NeedsReview;
        return payload.Any(x => x.Field == field.Value && x.Wire == WireType.Varint &&
                                x.Varint == (ulong)expectedExchangeUid)
            ? HdvSellDisposition.Confirmed : HdvSellDisposition.NeedsReview;
    }
}
