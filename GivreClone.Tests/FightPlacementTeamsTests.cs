using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class FightPlacementTeamsTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static int F(string message, string field) => Catalog.FieldNumber(message, field)
        ?? throw new InvalidOperationException($"Missing mapping: {message}.{field}");

    [Fact]
    public void Attacking_player_gets_challenger_cells_instead_of_enemy_cells()
    {
        var result = FightPrepReader.ReadPlacementCells(new List<ProtoField>
        {
            new() { Field = F("FightPlacementPossiblePositionsEvent", "starting_positions"), Wire = WireType.Len,
                Message = new List<ProtoField>
                {
                    new() { Field = F("FightStartingPositions", "defenders_positions"), Wire = WireType.Len, Bytes = new byte[] { 12, 25 } },
                    new() { Field = F("FightStartingPositions", "challengers_positions"), Wire = WireType.Len, Bytes = new byte[] { 41, 62 } }
                } }
        });

        Assert.NotNull(result);
        Assert.Equal(new[] { 41, 62 }, result.Value.Challengers);
        Assert.Equal(new[] { 12, 25 }, result.Value.Defenders);
    }
}
