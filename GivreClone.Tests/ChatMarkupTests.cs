using GivreClone.App.Services.Chat;
using Xunit;

namespace GivreClone.Tests;

public class ChatMarkupTests
{
    [Fact]
    public void Resolves_item_model_after_instance_and_preserves_surrounding_text()
    {
        var parts = ChatMarkup.Parse("Vends &gt; {chatitem,5389040,8270} !");
        Assert.Equal("Vends > ", parts[0].Text);
        Assert.Equal("chatitem", parts[1].Kind);
        Assert.Equal(8270, parts[1].Id);
        Assert.Equal(" !", parts[2].Text);
    }

    [Fact]
    public void Handles_adjacent_links_and_lobby_without_exposing_payload()
    {
        var parts = ChatMarkup.Parse("{chatmonster,1159}{chatachievement,3074}{lobby,secret,123}");
        Assert.Equal(1159, parts[0].Id);
        Assert.Equal(3074, parts[1].Id);
        Assert.Equal("Groupe", parts[2].Text);
    }

    [Theory]
    [InlineData("{chatitem,x,y}")]
    [InlineData("{chatmonster,99999999999999}")]
    [InlineData("{unknown,1}")]
    [InlineData("<b>aucun HTML actif</b>")]
    public void Preserves_unrecognized_or_invalid_input(string input)
        => Assert.Equal(input, Assert.Single(ChatMarkup.Parse(input)).Text);
}
