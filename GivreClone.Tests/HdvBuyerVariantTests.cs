extern alias F06App;
using Store = F06App::GivreClone.App.Services.BidHouseStore;
using GivreClone.Protocol;
using Xunit;
namespace GivreClone.Tests;
public class HdvBuyerVariantTests
{
    private static Store.BuyerOffer Offer(int uid, long price, int ap) => new(uid, 2469, new[] {price},
        ap == 0 ? Array.Empty<InventoryReader.ItemEffect>() : new[] {new InventoryReader.ItemEffect(111,ap,0,0,0,null,0)});
    [Fact] public void Variants_keep_individual_prices_and_pa_instead_of_last_row()
    {
        var s = new Store();
        s.SeedBuyerOffers(1,2469,new[] {Offer(10,100,0),Offer(11,200,1),Offer(12,999,1)});
        Assert.Equal(3,s.BuyerOffers(1,2469).Count);
        Assert.Empty(s.BuyerOffers(1,2469)[0].Effects);
        Assert.Equal(111,s.BuyerOffers(1,2469)[1].Effects.Single().ActionId);
        Assert.Equal(100,s.OfferPriceFor(1,2469,0));
        Assert.Equal(2469,s.GidForOfferUid(1,11));
    }
    [Fact] public void Average_and_unknown_price_are_never_used_as_buyer_quotes()
    {
        var s = new Store(); s.SetAverage(1,2469,4200000);
        s.SeedBuyerOffers(1,2469,new[] {Offer(10,0,1),Offer(11,200,1)});
        Assert.Equal(200,s.OfferPriceFor(1,2469,0));
        Assert.Equal(-1,s.OfferPriceFor(1,2469,1));
    }
    [Fact] public void Refresh_and_session_clear_drop_all_stale_variants()
    {
        var s = new Store();s.SeedBuyerOffers(1,2469,new[] {Offer(10,100,1)});
        s.SeedBuyerOffers(2,2469,new[] {Offer(20,200,1)});
        s.ClearOffer(1,2469);Assert.Empty(s.BuyerOffers(1,2469));
        Assert.Single(s.BuyerOffers(2,2469));
        s.Clear(2);Assert.Empty(s.BuyerOffers(2,2469));
    }
}
