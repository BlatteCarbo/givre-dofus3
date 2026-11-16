using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class NotebookSearchTests
{
    [Fact]
    public void Search_matches_accented_names_and_words_across_fields()
    {
        Assert.True(NotebookSearch.Matches("ile recolte", "Île", "Récolte de minerai"));
        Assert.True(NotebookSearch.Matches("  ZAAP   -18 ", "Zaap Astrub", "[4,-18]"));
        Assert.False(NotebookSearch.Matches("zaap banque", "Zaap Astrub", "Atelier"));
        Assert.True(NotebookSearch.Matches(" ", ""));
    }

    [Theory]
    [InlineData("-2147483648,0")]
    [InlineData("0,-2147483648")]
    [InlineData("999999999999,0")]
    public void Extreme_coordinate_input_is_rejected_without_crashing(string input)
        => Assert.False(Notebook.TryParseCoordinates(input, out _, out _));
}
