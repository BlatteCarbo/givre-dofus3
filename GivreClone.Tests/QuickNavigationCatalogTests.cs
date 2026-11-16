using GivreClone.App.Services.UiNavigation;
using Xunit;

namespace GivreClone.Tests;

public class QuickNavigationCatalogTests
{
    [Fact]
    public void Empty_query_shows_unique_known_actions()
    {
        var actions = QuickNavigationCatalog.Search(null);
        Assert.Equal(31, actions.Count);   // 14 pages + 17 onglets Bots
        Assert.Equal(actions.Count, actions.Select(a => a.Key).Distinct().Count());
        Assert.All(actions, a => Assert.True(QuickNavigationCatalog.IsKnown(a.Key)));
        Assert.False(QuickNavigationCatalog.IsKnown("send-packet"));
    }

    [Theory]
    [InlineData("PARAMETRES", "settings")]
    [InlineData("  réseau  ", "adapters")]
    [InlineData("mini carte", "visualmods")]
    [InlineData("bijoutier 20 30", "professions")]
    [InlineData("pano bouftou", "itemcraft")]
    [InlineData("historique ventes", "market")]
    [InlineData("  licence   profil", "myaccount")]
    [InlineData("expedition", "bots/2")]
    [InlineData("forgemagie", "bots/15")]
    [InlineData("onglet hdv", "bots/7")]
    [InlineData("console", "bots/0")]
    [InlineData("activite mcp", "bots/16")]
    public void Accent_insensitive_search_matches_expected_action(string query, string key)
    {
        Assert.Contains(QuickNavigationCatalog.Search(query), a => a.Key == key);
    }

    [Fact]
    public void Bot_tab_keys_map_to_indices_and_reject_garbage()
    {
        Assert.Equal(3, QuickNavigationCatalog.BotTabIndex("bots/3"));
        Assert.Equal(1, QuickNavigationCatalog.BotTabIndex("bots/combat"));
        Assert.Null(QuickNavigationCatalog.BotTabIndex("bots"));
        Assert.Null(QuickNavigationCatalog.BotTabIndex("bots/17"));
        Assert.Null(QuickNavigationCatalog.BotTabIndex("bots/nope"));
        Assert.Null(QuickNavigationCatalog.BotTabIndex("bots/-1"));
        Assert.Null(QuickNavigationCatalog.BotTabIndex("bots/x"));
        Assert.False(QuickNavigationCatalog.IsKnown("bots/99"));
    }

    [Fact]
    public void Bot_tabs_are_keyed_by_stable_tags_with_console_first_and_mcp_last()
    {
        var tags = QuickNavigationCatalog.BotTabTags;
        Assert.Equal(17, tags.Count);
        Assert.Equal(tags.Count, tags.Distinct().Count());
        Assert.Equal("console", tags[0]);
        Assert.Equal("combat", tags[1]);
        Assert.Equal("mcp", tags[^1]);
        Assert.Equal("bots/1", QuickNavigationCatalog.BotTabKey("combat"));
        Assert.Equal("combat", QuickNavigationCatalog.BotTabTag("bots/1"));
        Assert.Equal("packets", QuickNavigationCatalog.BotTabTag("bots/packets"));
        Assert.Null(QuickNavigationCatalog.BotTabTagAt(17));
        // Every tab action resolves to its own tag: the palette and the view agree on one key per tab.
        var botActions = QuickNavigationCatalog.All.Where(a => a.Key.StartsWith(QuickNavigationCatalog.BotTabPrefix)).ToArray();
        Assert.Equal(tags, botActions.Select(a => QuickNavigationCatalog.BotTabTag(a.Key)));
    }

    [Fact]
    public void Unknown_search_is_empty_and_long_query_is_bounded()
    {
        Assert.Empty(QuickNavigationCatalog.Search("xyznotarealaction"));
        Assert.Empty(QuickNavigationCatalog.Search(new string('z', 1_000_000)));
    }

    [Fact]
    public void No_gameplay_or_unverified_teleport_actions_exposed()
    {
        Assert.DoesNotContain(QuickNavigationCatalog.All, a =>
            a.Key.Contains("teleport", StringComparison.OrdinalIgnoreCase) ||
            a.Key.Contains("combat", StringComparison.OrdinalIgnoreCase) ||
            a.Key.Contains("hdv-buy", StringComparison.OrdinalIgnoreCase));
    }
}
