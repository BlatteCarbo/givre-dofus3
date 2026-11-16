using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class InteractionLogicTests
{
    [Fact]
    public void Trade_wins_over_bank_and_shop()
    {
        Assert.Equal(ExchangeKind.Trade, InteractionLogic.Resolve(storageOpen: true, tradeOpen: true, shopOpen: true));
        Assert.Equal(ExchangeKind.Bank, InteractionLogic.Resolve(true, false, true));
        Assert.Equal(ExchangeKind.Shop, InteractionLogic.Resolve(false, false, true));
        Assert.Equal(ExchangeKind.None, InteractionLogic.Resolve(false, false, false));
    }

    [Theory]
    [InlineData(0, 12, 12)]    // 0 = toute la pile
    [InlineData(-3, 12, 12)]
    [InlineData(5, 12, 5)]
    [InlineData(50, 12, 12)]   // jamais plus que la pile
    [InlineData(5, 0, 0)]
    public void Quantity_is_clamped_to_the_stack(int requested, int available, int expected)
        => Assert.Equal(expected, InteractionLogic.ClampQty(requested, available));

    [Fact]
    public void Spread_takes_biggest_stacks_first_and_stops_at_the_wanted_amount()
    {
        var plan = InteractionLogic.Spread(new[] { (1, 3), (2, 10), (0, 99), (3, 0), (4, 5) }, 12);
        Assert.Equal(new[] { (2, 10), (4, 2) }, plan);
        Assert.Equal(18, InteractionLogic.Spread(new[] { (1, 3), (2, 10), (4, 5) }, 0).Sum(x => x.Qty));
    }

    [Fact]
    public void Slot_keys_cover_the_sixteen_base_slots_only()
    {
        Assert.Equal("L.Inv.Slot.Amulet", InteractionLogic.SlotKey(0));
        Assert.Equal("L.Inv.Slot.Dofus", InteractionLogic.SlotKey(12));
        Assert.Equal("L.Inv.Slot.Shield", InteractionLogic.SlotKey(15));
        Assert.Null(InteractionLogic.SlotKey(63));
        var all = InteractionLogic.LeftSlots.Concat(InteractionLogic.RightSlots).Concat(InteractionLogic.DofusSlots).ToList();
        Assert.Equal(16, all.Distinct().Count());
        Assert.All(all, p => Assert.NotNull(InteractionLogic.SlotKey(p)));
    }

    [Fact]
    public void Trade_content_tracks_both_sides_and_any_change_cancels_ready()
    {
        var c = new TradeContent();
        c.Upsert(remote: false, new[] { (10, 289L, 50) });
        c.Upsert(remote: true, new[] { (20, 303L, 7) });
        c.SetKamas(remote: true, 1500);
        c.SetReady(remote: false, true);
        c.SetReady(remote: true, true);
        Assert.True(c.MeReady && c.TheyReady);
        Assert.Equal((10, 289L, 50), c.Mine.Single());
        Assert.Equal((20, 303L, 7), c.Theirs.Single());
        Assert.Equal(1500, c.TheirKamas);

        c.Upsert(remote: true, new[] { (20, 303L, 0) });   // quantité 0 = retiré
        Assert.Empty(c.Theirs);
        Assert.False(c.MeReady || c.TheyReady);

        c.SetKamas(remote: false, -5);
        Assert.Equal(0, c.MyKamas);
        c.Remove(remote: false, new[] { 10 });
        Assert.Empty(c.Mine);
    }

    [Fact]
    public void Gear_sets_round_trip_and_reject_garbage()
    {
        var g = new GearSet("Récolte | PP", new[] { (0, 8231L), (1, 44L), (15, 0L) });
        string text = GearSetCodec.Format(g);
        Assert.Equal("Récolte / PP|0:8231,1:44", text);   // « | » interdit dans le nom, gid 0 ignoré
        var back = GearSetCodec.Parse(text)!;
        Assert.Equal("Récolte / PP", back.Name);
        Assert.Equal(new[] { (0, 8231L), (1, 44L) }, back.Items);
        Assert.Null(GearSetCodec.Parse("rien"));
        Assert.Null(GearSetCodec.Parse("x|63:5,a:b"));    // position 63 = sac : pas un emplacement
    }
}

