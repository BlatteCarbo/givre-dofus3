using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.Tests;

public sealed class SmithSessionBudgetTests
{
    [Fact]
    public void Cash_expense_includes_travel_and_does_not_charge_consumption_again()
    {
        var budget = new SmithSessionBudget(841448, 200000);
        Assert.Equal(641448, budget.Floor);
        Assert.Equal(12300, budget.Spent(829148));
        Assert.Equal(187700, budget.Remaining(829148));
        Assert.Equal(182700, budget.PurchaseBudget(829148, 5000));
        Assert.Equal(12300, budget.Spent(829148));
    }

    [Theory]
    [InlineData(641448, 0)]
    [InlineData(641000, 0)]
    [InlineData(900000, 200000)]
    public void Remaining_cash_is_bounded_by_the_session_cap(long cash, long expected)
    {
        var budget = new SmithSessionBudget(841448, 200000);
        Assert.Equal(expected, budget.Remaining(cash));
    }
}
