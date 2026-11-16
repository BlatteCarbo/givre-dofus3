using System.Xml.Linq;
using Nav = GivreClone.App.Services.UiNavigation;
using Xunit;

namespace GivreClone.Tests;

public sealed class NavigationShortcutsTests
{
    [Fact]
    public void Back_returns_to_previous_pages_in_order_with_bounded_history()
    {
        var history = new Nav.NavigationHistory(capacity: 3);
        history.Record("dashboard", "accounts");
        history.Record("accounts", "accounts");   // pas un changement
        history.Record("accounts", "bots");
        history.Record("bots", "bots/2");
        Assert.Equal(new[] { "dashboard", "accounts", "bots" }, history.Keys);

        Assert.True(history.TryPop("bots/2", out var back));
        Assert.Equal("bots", back);
        Assert.True(history.TryPop("bots", out back));
        Assert.Equal("accounts", back);

        for (int i = 0; i < 10; i++) history.Record("p" + i, "p" + (i + 1));
        Assert.Equal(3, history.Count);
        Assert.Equal(new[] { "p7", "p8", "p9" }, history.Keys);
        while (history.TryPop("", out _)) { }
        Assert.False(history.TryPop("x", out _));
    }

    [Fact]
    public void Back_skips_the_page_already_shown()
    {
        var history = new Nav.NavigationHistory();
        history.Record("dashboard", "settings");
        history.Record("settings", "dashboard");
        Assert.True(history.TryPop("dashboard", out var back));
        Assert.Equal("settings", back);
        Assert.True(history.TryPop("settings", out back));
        Assert.Equal("dashboard", back);
    }

    [Fact]
    public void Shortcut_help_lists_the_wired_gestures_and_every_description_is_localized()
    {
        var gestures = Nav.KeyboardShortcutCatalog.All.Select(s => s.Gesture).ToArray();
        Assert.Equal(gestures.Length, gestures.Distinct().Count());
        foreach (var wired in new[] { "F1", "Ctrl+K", "Alt+←", "Esc", "Ctrl+PageUp", "Ctrl+PageDown" })
            Assert.Contains(wired, gestures);

        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "GivreClone.App/Lang/fr.axaml")))!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var lang in new[] { "fr", "en", "es" })
        {
            var keys = XDocument.Load(Path.Combine(root.FullName, $"GivreClone.App/Lang/{lang}.axaml"))
                .Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")).ToHashSet();
            Assert.All(Nav.KeyboardShortcutCatalog.All, s => Assert.Contains(s.DescriptionKey, keys));
        }
    }
}
