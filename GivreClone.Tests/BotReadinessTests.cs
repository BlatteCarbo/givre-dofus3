using GivreClone.App.Services.Diagnostics;
using Xunit;

namespace GivreClone.Tests;

public sealed class BotReadinessTests
{
    private static BotReadiness.Input Online() => new(
        Status: "ingame", Pid: 101, MapId: 456, InFight: false,
        ScriptRunning: true, LinkSuspended: false, CellConfirmed: true,
        ProtocolVerified: null, ServerAcknowledgementVerified: null,
        HdvSellReview: false, HdvRemoveReview: false, HdvRepriceReview: false,
        DisconnectReason: null);

    [Fact]
    public void An_observed_map_never_claims_a_verified_protocol_or_ack()
    {
        var report = BotReadiness.Evaluate(Online());
        Assert.Equal("not-verified", report.Verdict);
        Assert.False(report.MayAssumeGameplayConfirmed);
        Assert.Contains(report.Checks, c => c.Id == "protocol" && c.State == "unknown");
        Assert.Contains(report.Checks, c => c.Id == "server-ack" && c.State == "unknown");
    }

    [Fact]
    public void Link_suspension_and_offline_bot_are_blocked()
    {
        Assert.Equal("blocked", BotReadiness.Evaluate(Online() with { LinkSuspended = true }).Verdict);
        Assert.Equal("blocked", BotReadiness.Evaluate(Online() with { Pid = 0, Status = "offline" }).Verdict);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Any_uncertain_market_transaction_requires_operator_review(bool sell, bool remove, bool reprice)
    {
        var report = BotReadiness.Evaluate(Online() with
        {
            HdvSellReview = sell, HdvRemoveReview = remove, HdvRepriceReview = reprice
        });
        Assert.Equal("needs-review", report.Verdict);
        Assert.Contains(report.Checks, c => c.Id == "market" && c.State == "review");
    }

    [Fact]
    public void Durable_buy_review_blocks_readiness_without_claiming_any_server_ack()
    {
        var report = BotReadiness.Evaluate(Online() with { HdvBuyReview = true });
        Assert.Equal("needs-review", report.Verdict);
        Assert.False(report.MayAssumeGameplayConfirmed);
        Assert.Contains(report.Checks, c =>
            c.Id == "market" && c.State == "review" &&
            c.NextAction.Contains("kamas", StringComparison.Ordinal));
        Assert.Contains(report.Checks, c => c.Id == "server-ack" && c.State == "unknown");
    }

    [Fact]
    public void Missing_cell_does_not_become_a_valid_position()
    {
        var report = BotReadiness.Evaluate(Online() with { CellConfirmed = false });
        Assert.Equal("not-verified", report.Verdict);
        Assert.Contains(report.Checks, c => c.Id == "map" && c.State == "unknown");
    }
}
