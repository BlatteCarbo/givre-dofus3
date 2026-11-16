using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class NativeTeamFollowTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static IReadOnlyList<ProtoField> Payload(byte[] frame, string name)
    {
        var frames = RawProtobuf.ConsumeFrames(frame, out var rest);
        Assert.Empty(rest);
        var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(Assert.Single(frames))));
        Assert.Equal(Catalog.CodeForName(name), hit.Code);
        return (IReadOnlyList<ProtoField>?)hit.Payload ?? Array.Empty<ProtoField>();
    }

    [Fact]
    public void Follow_and_stop_use_only_named_documented_fields()
    {
        var follow = Payload(MoveForge.BuildPartyFollowFramed(46448378210, 123), "PartyFollowMemberRequest");
        Assert.Equal(3, follow.Count);
        Assert.Equal(46448378210L, PayloadReader.Int(follow, Catalog.FieldNumber("PartyFollowMemberRequest", "player_id")!.Value, 0));
        Assert.Equal(123, PayloadReader.Int(follow, Catalog.FieldNumber("PartyFollowMemberRequest", "party_id")!.Value, 0));
        Assert.Equal(1, PayloadReader.Int(follow, Catalog.FieldNumber("PartyFollowMemberRequest", "enabled")!.Value, 0));
        var stop = Payload(MoveForge.BuildPartyStopFollowFramed(46448378210, 123), "PartyStopFollowMemberRequest");
        Assert.Equal(2, stop.Count);
        Assert.Equal(46448378210L, PayloadReader.Int(stop, Catalog.FieldNumber("PartyStopFollowMemberRequest", "player_id")!.Value, 0));
        Assert.Equal(123, PayloadReader.Int(stop, Catalog.FieldNumber("PartyStopFollowMemberRequest", "party_id")!.Value, 0));
        Assert.Empty(Payload(MoveForge.BuildAutoFollowActivationFramed(), "AutoFollowActivationRequest"));
        Assert.Empty(Payload(MoveForge.BuildFightAutoJoinActivationFramed(), "FightAutoJoinActivationRequest"));
    }

    [Theory]
    [InlineData(343, 274)]
    [InlineData(274, 274)]
    [InlineData(288, 274)]
    public void Fallback_stops_adjacent_and_never_on_the_leader(int from, int leader)
    {
        int target = TeamFollowPosition.AdjacentDestination(from, leader, _ => true);
        Assert.NotEqual(leader, target);
        Assert.Equal(1, MapPathfinder.CellDistance(target, leader));
        if (MapPathfinder.CellDistance(from, leader) == 1) Assert.Equal(from, target);
    }

    [Fact]
    public void Exact_walk_prefix_survives_key_cell_compression()
    {
        var path = new List<int> { 343, 328, 315, 301 };
        var compressed = MapPathfinder.Compress(path);
        Assert.Equal(path, MapPathfinder.ExpandPath(compressed.Select(c => c & 0xFFF).ToList()));
    }

    [Fact]
    public void Fallback_does_not_move_when_all_adjacent_cells_are_blocked()
        => Assert.Equal(-1, TeamFollowPosition.AdjacentDestination(343, 274, _ => false));
}
