using GivreClone.App.Services.Forgemagie;
using Xunit;

namespace GivreClone.Tests;

public class FmSupportLinesTests
{
    [Fact]
    public void Every_line_without_a_modeled_rune_is_counted_and_named()
    {
        string text = FmSupportLines.Unmodeled(new[] { "+5% Résistance Feu", "+3 Dommages", "+5% Résistance Feu" });
        Assert.StartsWith("3 ligne(s)", text);
        Assert.Contains("+3 Dommages", text);
    }

    [Fact]
    public void Fully_modeled_item_shows_no_warning() => Assert.Equal("", FmSupportLines.Unmodeled(new string[0]));
}
