using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Only an inbound buy result for the exact requested lot UID may resolve an HDV buy.</summary>
public enum HdvBuyReceiptOutcome { Unrelated, Rejected, Confirmed }

public static class MZP26HdvBuyReceipt
{
    public static HdvBuyReceiptOutcome Classify(string? messageName, IReadOnlyList<ProtoField> payload,
        int expectedUid, Func<string, string, int?> lookup)
    {
        const string message = "ExchangeBidHouseBuyResultEvent";
        if (messageName != message || expectedUid <= 0) return HdvBuyReceiptOutcome.Unrelated;
        int? uidField = lookup(message, "object_uid"), boughtField = lookup(message, "bought");
        if (uidField is not > 0 || boughtField is not > 0) return HdvBuyReceiptOutcome.Unrelated;

        var uid = payload.FirstOrDefault(f => f.Field == uidField && f.Wire == WireType.Varint);
        if (uid is null || uid.Varint != (ulong)expectedUid) return HdvBuyReceiptOutcome.Unrelated;

        // Proto3 may omit `bought=false`; absence is a refusal, never success.
        var flag = payload.FirstOrDefault(f => f.Field == boughtField && f.Wire == WireType.Varint);
        return flag?.Varint == 1UL ? HdvBuyReceiptOutcome.Confirmed : HdvBuyReceiptOutcome.Rejected;
    }
}
