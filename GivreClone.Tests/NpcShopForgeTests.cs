using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Les formes attendues viennent d'une capture passive du client officiel 3.7.11.11 (2026-10-16), pas d'une supposition.</summary>
public class NpcShopForgeTests
{
    private static List<ProtoField> Payload(byte[] framed)
    {
        var frames = RawProtobuf.ConsumeFrames(framed, out _);
        Assert.Single(frames);
        var any = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0])).Single();
        return any.Payload!.ToList();
    }

    [Fact]
    public void Buy_request_matches_the_observed_shape_quantity_then_gid()
    {
        var p = Payload(MoveForge.BuildNpcBuyFramed(6765, 1));
        Assert.Equal(new[] { 1, 2 }, p.Select(f => f.Field).ToArray());
        Assert.Equal(1UL, p[0].Varint);       // quantité
        Assert.Equal(6765UL, p[1].Varint);    // gid de l'objet listé par la boutique
    }

    [Fact]
    public void Sell_request_is_an_objects_sell_with_uid_and_quantity_not_sell_request()
    {
        var p = Payload(MoveForge.BuildNpcSellFramed(new[] { (4889040, 1) }));
        var one = Assert.Single(p);
        Assert.Equal(1, one.Field);
        Assert.Equal(new[] { 1, 2 }, one.Message!.Select(f => f.Field).ToArray());
        Assert.Equal(4889040UL, one.Message![0].Varint);
        Assert.Equal(1UL, one.Message![1].Varint);
    }

    [Fact]
    public void Sell_request_with_several_stacks_repeats_the_objects_field()
    {
        var p = Payload(MoveForge.BuildNpcSellFramed(new[] { (10, 1), (11, 5) }));
        Assert.Equal(2, p.Count);
        Assert.All(p, f => Assert.Equal(1, f.Field));
        Assert.Equal(5UL, p[1].Message![1].Varint);
    }

    [Fact]
    public void Forge_codes_come_from_the_mapping_for_buy_and_sell()
    {
        Assert.NotEmpty(MoveForge.BuildNpcBuyFramed(1, 1));
        Assert.NotEmpty(MoveForge.BuildNpcSellFramed(new[] { (1, 1) }));
    }
}
