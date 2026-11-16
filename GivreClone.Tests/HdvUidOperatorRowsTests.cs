using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvUidOperatorRowsTests
{
    [Fact]
    public void FormatterOutputsRemainReadableWithoutInventingAcknowledgements()
    {
        var request = HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleRequest(42, 101, 1, true))!;
        Assert.Equal((uint)42, request.Pid);
        Assert.Equal("101", request.BagUid);
        Assert.Equal("?", request.ListingUid);
        Assert.Equal("?", request.Disposition);
        var candidate = HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleListing(42, 202))!;
        Assert.Equal("?", candidate.BagUid);
        Assert.Equal("202", candidate.ListingUid);
        Assert.Equal("?", candidate.Disposition);
        var result = HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleResult(42, 101,
            HdvSellDisposition.NeedsReview, null, null))!;
        Assert.Equal("NeedsReview", result.Disposition);
        Assert.Equal("?", result.ListingUid);
    }

    [Fact]
    public void RepricePreservesOldAndCandidateUidsWithoutClaimingConfirmation()
    {
        var row = HdvUidOperatorRow.Parse(HdvUidDiagnostic.RepriceListing(42, 202, 303))!;
        Assert.Equal("202", row.ListingUid);
        Assert.Contains("303", row.Evidence);
        Assert.Equal("?", row.Disposition);
    }

    [Fact]
    public void RemovalKeepsRequestedListingSeparateFromUnknownBag()
    {
        var request = HdvUidOperatorRow.Parse(HdvUidDiagnostic.RemoveRequest(42, 303, 1, true, false))!;
        Assert.Equal("303", request.ListingUid);
        Assert.Equal("?", request.BagUid);
        var result = HdvUidOperatorRow.Parse(HdvUidDiagnostic.RemoveResponse(42,
            "ExchangeBidHouseInListRemovedEvent", 303, HdvSellDisposition.Confirmed, false))!;
        Assert.Equal("Confirmed", result.Disposition);
        Assert.Equal("?", result.BagUid);
    }

    [Theory]
    [InlineData(" token=private")]
    [InlineData("\nprivate")]
    [InlineData("\r\n")]
    public void RejectsAdditionalDataAfterAnOtherwiseAllowedDiagnostic(string suffix)
        => Assert.Null(HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleListing(42, 202) + suffix));

    [Fact]
    public void RejectsArbitraryEventAndOutOfRangePid()
    {
        Assert.Null(HdvUidOperatorRow.Parse("[hdv] pid 42 : vente uid-sac=1 disposition=Confirmed sell_id=2 event=private."));
        Assert.Null(HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleListing(42, 202).Replace("pid 42", "pid 9999999999")));
        Assert.Null(HdvUidOperatorRow.Parse(HdvUidDiagnostic.SaleListing(0, 202)));
        Assert.Null(HdvUidOperatorRow.Parse(new string('x', 10000)));
    }
}
