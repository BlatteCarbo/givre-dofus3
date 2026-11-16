using GivreClone.Engine.Recovery;
using Xunit;
namespace GivreClone.Tests;
public sealed class SmithMagicSessionBudgetTests
{
    private static readonly SmithTarget Target = new(118, 0, 10, 100, 20, 40);
    private static readonly SmithRune[] Runes = [new(1, 118, SmithRuneTier.Basic, 20, 60)];
    [Fact]
    public void Purchase_retry_does_not_reset_the_total_or_per_line_limit()
    {
        var session = new SmithMagicSessionBudget();
        var limits = new SmithMagicLimits(3, 1, 1000, true);
        Assert.True(session.ReserveNext(10, Target, Runes, limits).Ready);
        Assert.False(session.ReserveNext(10, Target, Runes, limits).Ready);
        Assert.True(session.ReserveNext(20, Target, Runes, limits).Ready);
        Assert.True(session.ReserveNext(30, Target, Runes, limits).Ready);
        Assert.False(session.ReserveNext(40, Target, Runes, limits).Ready);
        Assert.Equal(3, session.UsedTotal);
    }
    [Fact]
    public void Value_budget_is_shared_across_objects_and_survives_stock_refresh()
    {
        var session = new SmithMagicSessionBudget();
        var limits = new SmithMagicLimits(10, 10, 100, true);
        Assert.True(session.ReserveNext(10, Target, Runes, limits).Ready);
        Assert.False(session.ReserveNext(20, Target, Runes.ToArray(), limits).Ready);
        Assert.Equal(60, session.Spent);
        Assert.Equal(1, session.UsedTotal);
    }
}
