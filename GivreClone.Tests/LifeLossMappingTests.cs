using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class LifeLossMappingTests
{
    [Fact]
    public void Water_hit_of_31_is_not_reduced_to_its_element_id_of_3()
    {
        // Scénario reconstruit depuis la capture Lua/MCP du 24/10, 09:51:22,
        // et les numéros du catalogue livré : pas un rejeu d'octets réseau.
        // Action 96, Eau=3, perte 31, érosion 3 : trois noms étaient permutés.
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Assert.Equal(6, catalog.FieldNumber("LifePointsLost", "loss"));
        Assert.Equal(4, catalog.FieldNumber("LifePointsLost", "element_id"));
        Assert.Equal(1, catalog.FieldNumber("LifePointsLost", "permanent_damages"));
        int outer = catalog.FieldNumber("GameActionFightEvent", "life_points_lost")!.Value;
        var hit = new List<ProtoField>
        {
            new() { Field = outer, Wire = WireType.Len, Message = new List<ProtoField>
            {
                new() { Field = 1, Wire = WireType.Varint, Varint = 3 },
                new() { Field = 4, Wire = WireType.Varint, Varint = 3 },
                new() { Field = 5, Wire = WireType.Varint, Varint = 80480567649 },
                new() { Field = 6, Wire = WireType.Varint, Varint = 31 }
            } }
        };
        Assert.Equal((80480567649L, 31), FightStatsReader.ReadLifeLoss(hit));
    }
}
