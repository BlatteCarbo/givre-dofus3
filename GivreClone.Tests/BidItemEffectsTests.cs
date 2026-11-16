using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class BidItemEffectsTests
{
    [Fact]
    public void Reads_market_effects_with_the_market_field_not_the_bag_field()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int Field(string message, string name) => cat.FieldNumber(message, name) ?? throw new InvalidOperationException(message + "." + name);
        var item = new List<ProtoField>
        {
            new() { Field = Field("BidItem", "effects"), Wire = WireType.Len, Message = new()
            {
                new() { Field = Field("ObjectEffect", "action"), Wire = WireType.Varint, Varint = 125 },
                new() { Field = Field("ObjectEffect", "value_int"), Wire = WireType.Varint, Varint = 143 }
            } }
        };
        var fx = Assert.Single(InventoryReader.ReadEffects(item, "BidItem"));
        Assert.Equal(125, fx.ActionId);
        Assert.Equal(143, fx.Value);
        Assert.Empty(InventoryReader.ReadEffects(item));
    }
}
