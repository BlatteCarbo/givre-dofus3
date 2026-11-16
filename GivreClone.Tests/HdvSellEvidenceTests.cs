using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvSellEvidenceTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string type, string name) => Catalog.FieldNumber(type, name)!.Value;
    private static ProtoField V(string type, string name, long value) =>
        new() { Field = F(type, name), Wire = WireType.Varint, Varint = (ulong)value };
    private static ProtoField M(string type, string name, params ProtoField[] children) =>
        new() { Field = F(type, name), Wire = WireType.Len, Message = children.ToList() };
    private static HdvSellCorrelation NewSale(params int[] existing) =>
        new(42, 5678, 20, 10, 12345, existing, Catalog.FieldNumber);
    private static ProtoField[] Added(int uid = 901, long gid = 5678, int quantity = 10, long price = 12345) =>
        new[] {
            M("ExchangeBidHouseItemAddedEvent", "item", V("BidItem", "uid", uid),
                V("BidItem", "gid", gid), V("BidItem", "quantity", quantity)),
            V("ExchangeBidHouseItemAddedEvent", "price", price)
        };
    private static ProtoField[] Debit(int uid = 42, int left = 10) =>
        new[] { M("ObjectQuantityEvent", "object", V("ObjectUidWithQuantity", "object_uid", uid),
                  V("ObjectUidWithQuantity", "quantity", left)) };

    [Theory]
    [InlineData(null)]
    [InlineData("ExchangeBidHouseItemAddedEvent")]
    [InlineData("ExchangeBidHouseInListUpdatedEvent")]
    public void AnUncorrelatedPacketIsNeverAConfirmedSale(string? eventName)
    {
        Assert.Equal(HdvSellDisposition.NeedsReview,
            HdvSellEvidence.Classify(eventName, transactionCorrelated: false));
    }

    [Fact]
    public void ExplicitlyCorrelatedResultsCanBeDistinguishedInFuture()
    {
        Assert.Equal(HdvSellDisposition.Confirmed,
            HdvSellEvidence.Classify("ExchangeBidHouseItemAddedEvent", transactionCorrelated: true));
        Assert.Equal(HdvSellDisposition.Refused,
            HdvSellEvidence.Classify("ObjectErrorEvent", transactionCorrelated: true));
        Assert.Equal(HdvSellDisposition.NeedsReview,
            HdvSellEvidence.Classify(null, transactionCorrelated: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sale_is_confirmed_only_after_same_bag_debit_and_new_listing(bool listingFirst)
    {
        var correlation = NewSale();
        if (listingFirst)
            Assert.Equal(HdvSellDisposition.NeedsReview, correlation.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        else
            Assert.Equal(HdvSellDisposition.NeedsReview, correlation.Observe("ObjectQuantityEvent", Debit()));

        Assert.Equal(HdvSellDisposition.Confirmed,
            listingFirst ? correlation.Observe("ObjectQuantityEvent", Debit()) :
                           correlation.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        Assert.Equal(901, correlation.SellId);
    }

    [Fact]
    public void Foreign_bag_uid_or_existing_listing_never_confirm_a_sale()
    {
        var unrelatedBag = NewSale();
        Assert.Equal(HdvSellDisposition.NeedsReview, unrelatedBag.Observe("ObjectQuantityEvent", Debit(uid: 50)));
        Assert.Equal(HdvSellDisposition.NeedsReview, unrelatedBag.Observe("ExchangeBidHouseItemAddedEvent", Added()));

        var oldListing = NewSale(901);
        Assert.Equal(HdvSellDisposition.NeedsReview, oldListing.Observe("ObjectQuantityEvent", Debit()));
        Assert.Equal(HdvSellDisposition.NeedsReview, oldListing.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        Assert.Null(oldListing.SellId);
    }

    [Fact]
    public void Listing_must_match_gid_quantity_and_price_and_the_requested_debit()
    {
        foreach (var wrong in new[] { Added(gid: 44), Added(quantity: 1), Added(price: 999) })
        {
            var correlation = NewSale();
            correlation.Observe("ObjectQuantityEvent", Debit());
            Assert.Equal(HdvSellDisposition.NeedsReview, correlation.Observe("ExchangeBidHouseItemAddedEvent", wrong));
        }
        var wrongAmount = NewSale();
        wrongAmount.Observe("ExchangeBidHouseItemAddedEvent", Added());
        Assert.Equal(HdvSellDisposition.NeedsReview, wrongAmount.Observe("ObjectQuantityEvent", Debit(left: 15)));
    }

    [Fact]
    public void A_full_stack_deletion_may_confirm_a_full_stack_listing()
    {
        var sale = new HdvSellCorrelation(42, 5678, 10, 10, 12345, Array.Empty<int>(), Catalog.FieldNumber);
        Assert.Equal(HdvSellDisposition.NeedsReview, sale.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        Assert.Equal(HdvSellDisposition.Confirmed, sale.Observe("ObjectDeletedEvent",
            new[] { V("ObjectDeletedEvent", "object_uid", 42) }));
    }

    [Fact]
    public void Packed_batch_inventory_deletion_is_supported_without_accepting_malformed_varints()
    {
        var sale = new HdvSellCorrelation(42, 5678, 10, 10, 12345, Array.Empty<int>(), Catalog.FieldNumber);
        sale.Observe("ExchangeBidHouseItemAddedEvent", Added());
        int number = F("ObjectsDeletedEvent", "objects_uid");
        Assert.Equal(HdvSellDisposition.NeedsReview, sale.Observe("ObjectsDeletedEvent",
            new[] { new ProtoField { Field = number, Wire = WireType.Len, Bytes = new byte[] { 42, 0x80 } } }));
        Assert.Equal(HdvSellDisposition.Confirmed, sale.Observe("ObjectsDeletedEvent",
            new[] { new ProtoField { Field = number, Wire = WireType.Len, Bytes = new byte[] { 7, 42, 50 } } }));
    }

    [Fact]
    public void A_batch_quantity_update_can_prove_the_requested_bag_uid()
    {
        var sale = NewSale();
        Assert.Equal(HdvSellDisposition.NeedsReview,
            sale.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        var batch = new[] {
            M("ObjectsQuantityEvent", "object", V("ObjectUidWithQuantity", "object_uid", 987),
                V("ObjectUidWithQuantity", "quantity", 10)),
            M("ObjectsQuantityEvent", "object", V("ObjectUidWithQuantity", "object_uid", 42),
                V("ObjectUidWithQuantity", "quantity", 10))
        };
        Assert.Equal(HdvSellDisposition.Confirmed, sale.Observe("ObjectsQuantityEvent", batch));
    }

    [Fact]
    public void Two_new_matching_listing_ids_make_the_sale_ambiguous()
    {
        var sale = NewSale();
        sale.Observe("ExchangeBidHouseItemAddedEvent", Added(901));
        sale.Observe("ExchangeBidHouseItemAddedEvent", Added(902));
        Assert.Equal(HdvSellDisposition.NeedsReview,
            sale.Observe("ObjectQuantityEvent", Debit()));
    }

    [Fact]
    public void Timeout_is_needs_review_and_explicit_server_error_is_refused()
    {
        Assert.Equal(HdvSellDisposition.NeedsReview, HdvSellEvidence.Classify(null, false));
        Assert.Equal(HdvSellDisposition.Refused, HdvSellEvidence.Classify("ExchangeErrorEvent", false));
        Assert.Equal(HdvSellDisposition.Refused, HdvSellEvidence.Classify("ObjectErrorEvent", false));
        var correlation = NewSale();
        Assert.Equal(HdvSellDisposition.Refused, correlation.Observe("ExchangeErrorEvent", Array.Empty<ProtoField>()));
    }

    [Fact]
    public void Removing_someone_elses_listing_never_confirms_this_withdrawal()
    {
        var anotherUid = new[] { V("ExchangeBidHouseInListRemovedEvent", "object_uid", 900) };
        var exactUid = new[] { V("ExchangeBidHouseInListRemovedEvent", "object_uid", 901) };
        Assert.Equal(HdvSellDisposition.NeedsReview,
            HdvRemoveEvidence.Classify("ExchangeBidHouseInListRemovedEvent", anotherUid, 901, false, Catalog.FieldNumber));
        Assert.Equal(HdvSellDisposition.Confirmed,
            HdvRemoveEvidence.Classify("ExchangeBidHouseInListRemovedEvent", exactUid, 901, false, Catalog.FieldNumber));
    }

    [Fact]
    public void Removed_sell_id_is_only_confirmation_when_prior_link_exists()
    {
        var itemRemoved = new[] { V("ExchangeBidHouseItemRemovedEvent", "sell_id", 901) };
        Assert.Equal(HdvSellDisposition.NeedsReview,
            HdvRemoveEvidence.Classify("ExchangeBidHouseItemRemovedEvent", itemRemoved, 901, false, Catalog.FieldNumber));
        Assert.Equal(HdvSellDisposition.Confirmed,
            HdvRemoveEvidence.Classify("ExchangeBidHouseItemRemovedEvent", itemRemoved, 901, true, Catalog.FieldNumber));
        Assert.Equal(HdvSellDisposition.NeedsReview,
            HdvRemoveEvidence.Classify("ExchangeBidHouseItemRemovedEvent", itemRemoved, 902, true, Catalog.FieldNumber));
        Assert.Equal(HdvSellDisposition.Refused,
            HdvRemoveEvidence.Classify("ObjectErrorEvent", Array.Empty<ProtoField>(), 901, false, Catalog.FieldNumber));
    }
}
