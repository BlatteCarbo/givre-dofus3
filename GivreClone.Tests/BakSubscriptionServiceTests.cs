using System;
using GivreClone.App.Services.Bak;
using Xunit;

namespace GivreClone.Tests;

public class BakSubscriptionServiceTests
{
    [Fact]
    public void Profitability_Evaluation_Calculates_Tiers_Correctly()
    {
        BakSubscriptionService.CurrentRate = 500;
        int avg = BakSubscriptionService.GetAverageHistoricalRate();

        Assert.Equal(BakProfitability.VeryProfitable, BakSubscriptionService.EvaluateProfitability((int)(avg * 0.85)));
        Assert.Equal(BakProfitability.Expensive, BakSubscriptionService.EvaluateProfitability((int)(avg * 1.15)));
        Assert.Equal(BakProfitability.Normal, BakSubscriptionService.EvaluateProfitability(avg));
    }

    [Fact]
    public void Costs_For_7_And_30_Days_Are_Accurate()
    {
        BakSubscriptionService.CurrentRate = 500;
        Assert.Equal(500L * 1400, BakSubscriptionService.Cost7Days);
        Assert.Equal(500L * 4000, BakSubscriptionService.Cost30Days);
    }

    [Fact]
    public void Subscription_Expiration_And_Remaining_Time_Calculated()
    {
        long futureMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
        BakSubscriptionService.UpdateExpiration("TestBot", futureMs);

        var (rem, _, active) = BakSubscriptionService.GetSubscriptionStatus("TestBot");
        Assert.True(active);
        Assert.InRange(rem.TotalDays, 6.9, 7.1);
    }

    [Fact]
    public void Bak_Frames_Match_Live_Captured_Bytes_Bit_For_Bit()
    {
        byte[] action = BakSubscriptionService.BuildBakActionFrame("lpk", 548, 1400);
        Assert.Equal(
            "32223008FFFFFFFFFFFFFFFFFF0112230A13747970652E616E6B616D612E636F6D2F6C706B120C080610A40418F80A20E0E92E",
            Convert.ToHexString(action));

        byte[] validation = BakSubscriptionService.BuildBakValidationFrame("lpo", "8086bda1-9dfc-5979-82a4-7533e8a9449b");
        Assert.Equal(
            "4C224A08FFFFFFFFFFFFFFFFFF01123D0A13747970652E616E6B616D612E636F6D2F6C706F1226122438303836626461312D396466632D353937392D383261342D373533336538613934343962",
            Convert.ToHexString(validation));
    }
}
