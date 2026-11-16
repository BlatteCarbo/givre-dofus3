using System;
using System.Collections.Generic;
using System.Text;
using GivreClone.App.Services;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvUidDiagnosticTests
{
    private static Func<string, string, int?> Fields(int itemField, int uidField) =>
        (type, name) => (type, name) switch
        {
            ("ExchangeBidHouseItemAddedEvent", "item") => itemField,
            ("BidItem", "uid") => uidField,
            _ => null
        };

    private static ProtoField Item(int outerField, int uidField, ulong uid) => new()
    {
        Field = outerField, Wire = WireType.Len,
        Message = new List<ProtoField>
        {
            new() { Field = uidField, Wire = WireType.Varint, Varint = uid }
        }
    };

    [Theory]
    [InlineData(3, 1, 901)]
    [InlineData(27, 18, 81234)]
    public void Listing_uid_and_logged_value_follow_variable_field_mapping(int itemField, int uidField, int expectedUid)
    {
        const long hiddenPrice = 9876543210;
        const string hiddenAccount = "account-marker-private";
        var payload = new[]
        {
            Item(itemField + 1, uidField, 456), // unrelated nested message
            Item(itemField, uidField + 1, 789), // wrong nested field
            new ProtoField { Field = 98, Wire = WireType.Varint, Varint = (ulong)hiddenPrice },
            new ProtoField { Field = 99, Wire = WireType.Len, Bytes = Encoding.UTF8.GetBytes(hiddenAccount) },
            Item(itemField, uidField, (ulong)expectedUid)
        };

        int? uid = HdvUidDiagnostic.ListingUid(payload, Fields(itemField, uidField));
        Assert.Equal(expectedUid, uid);
        string received = HdvUidDiagnostic.SaleListing(100, uid);
        string repriced = HdvUidDiagnostic.RepriceListing(100, 800, uid);
        Assert.Equal($"[hdv] pid 100 : annonce reçue uid-lot={expectedUid} (candidat, non corrélé).", received);
        Assert.Contains($"uid-lot-annonce={expectedUid}", repriced);
        Assert.DoesNotContain(hiddenPrice.ToString(), received + repriced);
        Assert.DoesNotContain(hiddenAccount, received + repriced);
    }

    [Fact]
    public void Unknown_or_unusable_listing_uid_is_logged_as_question_mark()
    {
        var mapping = Fields(17, 7);
        var invalidPackets = new IReadOnlyList<ProtoField>[]
        {
            Array.Empty<ProtoField>(),
            new[] { Item(17, 8, 555) }, // unknown UID field
            new[] { Item(17, 7, 0) },
            new[] { Item(17, 7, (ulong)int.MaxValue + 1) },
            new[] { Item(17, 7, 901), Item(17, 7, 902) }, // multiple competing candidates
            new[] { new ProtoField { Field = 17, Wire = WireType.Varint, Varint = 555 } },
            new[] { new ProtoField { Field = 17, Wire = WireType.Len, Message = new List<ProtoField>
                { new() { Field = 7, Wire = WireType.Len, Bytes = new byte[] { 0x92, 0x07 } } } } }
        };
        foreach (var payload in invalidPackets)
        {
            int? uid = HdvUidDiagnostic.ListingUid(payload, mapping);
            Assert.Null(uid);
            Assert.Contains("uid-lot=? (candidat, non corrélé)", HdvUidDiagnostic.SaleListing(100, uid));
            Assert.Contains("uid-lot-annonce=? (réponse non corrélée", HdvUidDiagnostic.RepriceListing(100, 800, uid));
        }

        var validPacket = new[] { Item(17, 7, 901) };
        Assert.Null(HdvUidDiagnostic.ListingUid(validPacket, (_, _) => null));
        Assert.Null(HdvUidDiagnostic.ListingUid(validPacket,
            (type, name) => type == "BidItem" && name == "uid" ? 7 : null));
        Assert.Null(HdvUidDiagnostic.ListingUid(validPacket,
            (type, name) => type == "ExchangeBidHouseItemAddedEvent" && name == "item" ? 17 : 0));
    }

    [Fact]
    public void All_new_uid_traces_exclude_price_and_account_payload_and_do_not_invent_ack()
    {
        const string accountMarker = "account-secret-private";
        const string priceMarker = "77712345678";
        var logs = new[]
        {
            HdvUidDiagnostic.SaleRequest(100, 42, 1, true),
            HdvUidDiagnostic.SaleListing(100, 901),
            HdvUidDiagnostic.SaleListing(100, null),
            HdvUidDiagnostic.SaleResult(100, 42, HdvSellDisposition.NeedsReview, null, accountMarker),
            HdvUidDiagnostic.SaleResult(100, 42, HdvSellDisposition.Confirmed, 901, "ExchangeBidHouseItemAddedEvent"),
            HdvUidDiagnostic.RepriceListing(100, 901, 902),
            HdvUidDiagnostic.RemoveRequest(100, 902, 1, true, false),
            HdvUidDiagnostic.RemoveResponse(100, accountMarker, 902, HdvSellDisposition.NeedsReview, false)
        };

        foreach (string line in logs)
        {
            Assert.StartsWith("[hdv] pid 100 : ", line);
            Assert.DoesNotContain(priceMarker, line);
            Assert.DoesNotContain(accountMarker, line);
        }
        Assert.Contains("uid-sac=42", logs[0]);
        Assert.Contains("uid-lot=901", logs[1]);
        Assert.Contains("sell_id=?", logs[3]);
        Assert.Contains("event=other", logs[3]);
        Assert.Contains("sell_id=901", logs[4]);
        Assert.Contains("uid-lot-annonce=902", logs[5]);
        Assert.Contains("lien-sac-verifie=False", logs[6]);
        Assert.Contains("event=other", logs[7]);
        Assert.Contains("aucun ACK impliqué", logs[0]);
        Assert.Contains("aucun ACK impliqué", logs[6]);
    }
}
