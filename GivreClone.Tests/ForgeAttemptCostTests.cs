using GivreClone.App.Services.Forgemagie;
using Xunit;

namespace GivreClone.Tests;

public class ForgeAttemptCostTests
{
    [Fact]
    public void Cost_per_attempt_divides_measured_cash_and_stock_by_the_runes_placed()
        => Assert.Equal(1250m, ForgeAttemptCost.PerAttempt(4, 3000, 2000, 0));

    [Fact]
    public void A_rune_without_known_value_makes_the_average_unknown_but_keeps_a_lower_bound()
    {
        Assert.Null(ForgeAttemptCost.PerAttempt(4, 3000, 2000, 1));
        Assert.Contains("au moins 1", ForgeAttemptCost.Describe(4, 3000, 2000, 1));
    }

    [Fact]
    public void No_attempt_reports_no_cost() => Assert.Null(ForgeAttemptCost.PerAttempt(0, 500, 0, 0));
}
