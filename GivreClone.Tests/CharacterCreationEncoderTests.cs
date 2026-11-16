using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class CharacterCreationEncoderTests
{
    private static readonly CreationFieldNumbers F = new(Name: 2, Gender: 3, Breed: 4, Cosmetic: 5, Body: 7, Colors: 8);

    // Création réelle relevée sur le client 3.7.12.12 (sous-message modeling_information, après "0A 30") : « Test-Claude », femme, classe 9, tête 137, corps 22.
    private const string CapturedHex =
        "120B546573742D436C61756465" + "1801" + "2009" + "288901" + "3816" + "4218E9B69607FBF4D805ACDED503E7F6B705F491E20390AAEA07";

    [Fact]
    public void Reproduces_the_bytes_of_a_real_client_creation()
    {
        var spec = new CharacterCreationSpec("Test-Claude", 1, 9, 137, 22,
            new[] { 15047528, 11942522, 7696171, 11402086, 7899379, 16422159 }, true);
        Assert.Equal(CapturedHex, Convert.ToHexString(CharacterCreationEncoder.ModelingInfo(spec, F)));
    }

    [Fact]
    public void Male_gender_default_is_omitted()
    {
        var spec = new CharacterCreationSpec("Abc", 0, 1, 2, 3, new[] { 0, 1, 2, 3, 4, 5 }, false);
        Assert.Equal("1203416263" + "2001" + "2802" + "3803" + "4206010203040506", Convert.ToHexString(CharacterCreationEncoder.ModelingInfo(spec, F)));
    }

    [Theory]
    [InlineData(0, 2, 3, 6)]
    [InlineData(1, 0, 3, 6)]
    [InlineData(1, 2, 0, 6)]
    [InlineData(1, 2, 3, 5)]
    public void Rejects_incomplete_requests(int breed, int head, int body, int colors)
    {
        var spec = new CharacterCreationSpec("Abc", 1, breed, head, body, Enumerable.Repeat(0, colors).ToArray(), false);
        Assert.Throws<ArgumentException>(() => CharacterCreationEncoder.ModelingInfo(spec, F));
    }
}
