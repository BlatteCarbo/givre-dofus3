using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvRepriceCorrelationTests
{
    private static int? Field(string type, string name) => (type, name) switch
    {
        ("ExchangeBidHouseItemRemovedEvent", "sell_id") => 12,
        ("ExchangeBidHouseItemAddedEvent", "item") => 15,
        ("ExchangeBidHouseItemAddedEvent", "price") => 16,
        ("BidItem", "uid") => 3,
        ("BidItem", "gid") => 4,
        ("BidItem", "quantity") => 5,
        _ => null
    };
    private static ProtoField Var(int field, long value) =>
        new() { Field = field, Wire = WireType.Varint, Varint = (ulong)value };
    private static ProtoField[] Removed(int uid) => new[] { Var(12, uid) };
    private static ProtoField[] Added(int uid = 902, long gid = 42, int qty = 10, long price = 12345) =>
        new[] { new ProtoField { Field = 15, Wire = WireType.Len, Message = new List<ProtoField>
                    { Var(3, uid), Var(4, gid), Var(5, qty) } }, Var(16, price) };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Matching_old_removal_and_unique_new_listing_are_required(bool addedFirst)
    {
        var c = new HdvRepriceCorrelation(901, 42, 10, 12345, Field);
        var first = addedFirst ? c.Observe("ExchangeBidHouseItemAddedEvent", Added())
                               : c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(901));
        Assert.Equal(HdvSellDisposition.NeedsReview, first);
        Assert.Equal(HdvSellDisposition.Confirmed,
            addedFirst ? c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(901))
                       : c.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        Assert.Equal(902, c.NewUid);
    }

    [Fact]
    public void Unrelated_events_cannot_confirm_reprice()
    {
        foreach (var unrelated in new[] {
            Added(uid: 0), Added(gid: 99), Added(qty: 1), Added(price: 999) })
        {
            var c = new HdvRepriceCorrelation(901, 42, 10, 12345, Field);
            c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(901));
            Assert.Equal(HdvSellDisposition.NeedsReview, c.Observe("ExchangeBidHouseItemAddedEvent", unrelated));
        }
        var another = new HdvRepriceCorrelation(901, 42, 10, 12345, Field);
        another.Observe("ExchangeBidHouseItemAddedEvent", Added());
        Assert.Equal(HdvSellDisposition.NeedsReview, another.Observe("ExchangeBidHouseItemRemovedEvent", Removed(999)));
        Assert.Equal(HdvSellDisposition.Refused, another.Observe("ExchangeErrorEvent", Array.Empty<ProtoField>()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Server_can_preserve_uid_when_both_exact_events_confirm_reprice(bool addedFirst)
    {
        var c = new HdvRepriceCorrelation(953325, 442, 1000, 173964, Field);
        var first = addedFirst
            ? c.Observe("ExchangeBidHouseItemAddedEvent", Added(953325, 442, 1000, 173964))
            : c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(953325));
        Assert.Equal(HdvSellDisposition.NeedsReview, first);
        var second = addedFirst
            ? c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(953325))
            : c.Observe("ExchangeBidHouseItemAddedEvent", Added(953325, 442, 1000, 173964));
        Assert.Equal(HdvSellDisposition.Confirmed, second);
        Assert.Equal(953325, c.NewUid);
    }

    [Fact]
    public void Missing_mapping_and_conflicting_listings_are_not_confirmed()
    {
        var noMapping = new HdvRepriceCorrelation(901, 42, 10, 12345, (_, _) => null);
        Assert.Equal(HdvSellDisposition.NeedsReview, noMapping.Observe("ExchangeBidHouseItemRemovedEvent", Removed(901)));
        Assert.Equal(HdvSellDisposition.NeedsReview, noMapping.Observe("ExchangeBidHouseItemAddedEvent", Added()));
        var c = new HdvRepriceCorrelation(901, 42, 10, 12345, Field);
        c.Observe("ExchangeBidHouseItemAddedEvent", Added());
        c.Observe("ExchangeBidHouseItemAddedEvent", Added(uid: 903));
        Assert.Equal(HdvSellDisposition.NeedsReview, c.Observe("ExchangeBidHouseItemRemovedEvent", Removed(901)));
        Assert.Null(c.NewUid);
    }
}
