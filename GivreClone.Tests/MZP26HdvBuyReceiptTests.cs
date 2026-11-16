using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP26HdvBuyReceiptTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private const string Event = "ExchangeBidHouseBuyResultEvent";
    private static int F(string field) => Catalog.FieldNumber(Event, field)!.Value;
    private static ProtoField V(int field, long n) =>
        new() { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)n) };

    [Fact]
    public void Foreign_receipt_does_not_confirm_a_different_offer()
    {
        var inbound = new[] { V(F("object_uid"), 999), V(F("bought"), 1) };
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify(Event, inbound, 123, Catalog.FieldNumber));
    }

    [Fact]
    public void Refusal_and_missing_confirmation_are_never_success()
    {
        var reject = new[] { V(F("object_uid"), 123), V(F("bought"), 0) };
        var noFlag = new[] { V(F("object_uid"), 123) };
        Assert.Equal(HdvBuyReceiptOutcome.Rejected,
            MZP26HdvBuyReceipt.Classify(Event, reject, 123, Catalog.FieldNumber));
        Assert.Equal(HdvBuyReceiptOutcome.Rejected,
            MZP26HdvBuyReceipt.Classify(Event, noFlag, 123, Catalog.FieldNumber));
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify("KamasUpdateEvent", reject, 123, Catalog.FieldNumber));
    }

    [Fact]
    public void A_matching_bought_receipt_confirms_its_uid_only()
    {
        var receipt = new[] { V(F("object_uid"), 123), V(F("bought"), 1) };
        Assert.Equal(HdvBuyReceiptOutcome.Confirmed,
            MZP26HdvBuyReceipt.Classify(Event, receipt, 123, Catalog.FieldNumber));
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify(Event, receipt, 123, (_, _) => null));
    }

    [Fact]
    public void Malformed_wrong_direction_or_missing_uid_never_authorizes_buy()
    {
        var missingUid = new[] { V(F("bought"), 1) };
        var invalidWire = new[] {
            new ProtoField { Field = F("object_uid"), Wire = WireType.Len, Bytes = new byte[] { 123 } },
            V(F("bought"), 1)
        };
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify(Event, missingUid, 123, Catalog.FieldNumber));
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify(Event, invalidWire, 123, Catalog.FieldNumber));
        Assert.Equal(HdvBuyReceiptOutcome.Unrelated,
            MZP26HdvBuyReceipt.Classify(Event, new[] { V(F("object_uid"), 123), V(F("bought"), 1) },
                0, Catalog.FieldNumber));
    }

    [Fact]
    public void Confirmation_cannot_establish_quantity_or_price_that_wire_does_not_echo()
    {
        Assert.Null(Catalog.FieldNumber(Event, "quantity"));
        Assert.Null(Catalog.FieldNumber(Event, "price"));
        var receipt = new[] { V(F("object_uid"), 123), V(F("bought"), 1) };
        Assert.Equal(HdvBuyReceiptOutcome.Confirmed,
            MZP26HdvBuyReceipt.Classify(Event, receipt, 123, Catalog.FieldNumber));
        // This is a UID-level acknowledgment, not proof of the invoiced amount.
    }
}
