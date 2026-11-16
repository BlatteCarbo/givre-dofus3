using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class TurnOrderTests
{
    [Fact]
    public void Order_is_learned_over_the_first_round()
    {
        var t = new TurnOrder();
        t.OnTurn(10); t.OnTurn(-1); t.OnTurn(-2);
        Assert.False(t.Complete);
        t.OnTurn(10);
        Assert.True(t.Complete);
        Assert.Equal(new long[] { 10, -1, -2 }, t.Order);
        Assert.Equal(10, t.Current);
    }

    [Fact]
    public void A_late_summon_is_inserted_after_its_caster()
    {
        var t = new TurnOrder();
        foreach (var a in new long[] { 10, -1, -2, 10 }) t.OnTurn(a);
        t.OnTurn(99);   // invocation de 10, joue juste après lui
        t.OnTurn(-1);
        Assert.Equal(new long[] { 10, 99, -1, -2 }, t.Order);
    }

    [Fact]
    public void Zero_ids_are_ignored()
    {
        var t = new TurnOrder();
        t.OnTurn(0);
        Assert.Empty(t.Order);
    }
}
