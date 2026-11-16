using GivreClone.App.Services.Transport.Socket;
using Xunit;

namespace GivreClone.Tests;

public class ConnectionShapeTests
{
    [Fact]
    public void Shape_shows_field_numbers_small_integers_and_text_lengths_never_text_or_big_numbers()
    {
        // champ 1 = 354 (varint), champ 2 = "secret-nick" (11 octets), champ 3 = 123456789 (grand), champ 4 = sous-message {1=7}
        var body = new List<byte> { 0x08, 0xE2, 0x02 };
        body.Add(0x12); body.Add(11); body.AddRange(System.Text.Encoding.ASCII.GetBytes("secret-nick"));
        body.Add(0x18); body.AddRange(new byte[] { 0x95, 0x9A, 0xEF, 0x3A });
        body.Add(0x22); body.Add(2); body.Add(0x08); body.Add(0x07);
        var s = ConnectionMessages.Shape(body.ToArray());
        Assert.DoesNotContain("secret", s);
        Assert.DoesNotContain("123456789", s);
        Assert.Contains("#1=354", s);
        Assert.Contains("#3=<grand>", s);
        Assert.Contains("#4:{#1=7}", s);
        Assert.Contains("#2=<11 octets>", s);
    }

    [Fact]
    public void Shape_of_garbage_is_not_protobuf_instead_of_throwing()
        => Assert.Equal("<non protobuf>", ConnectionMessages.Shape(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }));
}
