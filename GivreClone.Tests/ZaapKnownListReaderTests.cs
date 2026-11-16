using System.Linq;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class ZaapKnownListReaderTests
{
    [Fact]
    public void Packed_map_ids_decode_like_the_captured_teleport_target()
    {
        // 08 878CE04E = map_id du TeleportRequest capturé le 23/10 (165152263) ; la liste compacte n'a pas de tags.
        byte[] packed = { 0x87, 0x8C, 0xE0, 0x4E, 0x96, 0x01 };
        Assert.Equal(new long[] { 165152263, 150 }, ZaapKnownListReader.Packed(packed).ToArray());
    }

    [Fact]
    public void Truncated_varint_does_not_throw()
        => Assert.Equal(new long[] { 165152263 }, ZaapKnownListReader.Packed(new byte[] { 0x87, 0x8C, 0xE0, 0x4E, 0xFF }).Take(1).ToArray());
}
