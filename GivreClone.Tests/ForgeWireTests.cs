using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Forme relevée sur le client officiel 3.7.12.12 (atelier Joaillomager, 23/10/2026, 45 runes) :
/// ExchangeObjectMoveAndSetReadyRequest = 08 B49A8E02 10 01 20 01 -> #1 uid de la rune, #2 quantité, #4 prêt.</summary>
public class ForgeWireTests
{
    private static List<ProtoField> Payload(byte[] framed)
    {
        var frames = RawProtobuf.ConsumeFrames(framed, out _);
        Assert.Single(frames);
        var any = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0])).Single();
        return any.Payload!.ToList();
    }

    [Fact]
    public void Rune_move_and_ready_matches_the_captured_client_frame()
    {
        var p = Payload(MoveForge.BuildExchangeObjectMoveAndSetReadyFramed(4427060, 1));
        Assert.Equal(new[] { 1, 2, 4 }, p.Select(f => f.Field).ToArray());
        Assert.Equal(4427060UL, p[0].Varint);
        Assert.Equal(1UL, p[1].Varint);
        Assert.Equal(1UL, p[2].Varint);
    }
}
