#if MZP0_TESTS
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.App.Controls;

public sealed class MZP0OperatorNavigationTests
{
    [Fact]
    public void Diagnostics_is_hidden_before_any_navigation_and_session_is_unattested()
    {
        var host = new Border();
        var sidebar = new Button();
        var header = new Button();
        using var vm = new MZP0OperatorViewModel();

        MZP0OperatorNavigation.Apply(host, sidebar, header, false, false);

        Assert.False(host.IsVisible);
        Assert.DoesNotContain("active", sidebar.Classes);
        Assert.DoesNotContain("active", header.Classes);
        Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
        Assert.Equal("INCONNU", vm.AckStatus);
    }

    [Fact]
    public void Sidebar_or_title_tab_can_open_then_close_the_diagnostics_surface()
    {
        var host = new Border();
        var sidebar = new Button();
        var header = new Button();

        MZP0OperatorNavigation.Apply(host, sidebar, header, true, false);
        Assert.True(host.IsVisible);
        Assert.Contains("active", sidebar.Classes);
        Assert.Contains("active", header.Classes);
        Assert.Equal(1, Grid.GetColumn(host));
        Assert.Equal(1, Grid.GetColumnSpan(host));

        MZP0OperatorNavigation.Apply(host, sidebar, header, false, false);
        Assert.False(host.IsVisible);
        Assert.DoesNotContain("active", sidebar.Classes);
        Assert.DoesNotContain("active", header.Classes);
    }

    [Fact]
    public void Classic_skin_covers_both_content_columns_while_title_bar_remains_usable()
    {
        var host = new Border();
        var sidebar = new Button();
        var header = new Button();

        MZP0OperatorNavigation.Apply(host, sidebar, header, true, true);
        Assert.True(host.IsVisible);
        Assert.Equal(0, Grid.GetColumn(host));
        Assert.Equal(2, Grid.GetColumnSpan(host));
        Assert.Contains("active", header.Classes);

        MZP0OperatorNavigation.Apply(host, sidebar, header, false, true);
        Assert.False(host.IsVisible);
    }

    [Fact]
    public void Only_real_main_nav_buttons_trigger_leave()
    {
        var pages = Enumerable.Range(0, 7).Select(_ => new Button()).ToArray();
        foreach (var page in pages)
            Assert.True(MZP0OperatorNavigation.IsExistingPageNavigation(page, pages));
        Assert.False(MZP0OperatorNavigation.IsExistingPageNavigation(new Button(), pages));
        Assert.False(MZP0OperatorNavigation.IsExistingPageNavigation(null, pages));
    }

    [Fact]
    public void Main_shell_actually_contains_both_navigation_entries_and_original_page_host()
    {
        // Structural check of the real, untouched navigation contract. No MainWindow
        // instantiation: that constructor starts the app's unrelated license/bootstrap.
        var file = Path.Combine(AppContext.BaseDirectory, "MZP0_MainWindow.axaml");
        Assert.True(File.Exists(file), "MainWindow AXAML integration fixture missing");
        var xml = XDocument.Load(file);
        XElement Named(string name) => xml.Descendants()
            .Single(node => (string?)node.Attribute("Name") == name);

        var sidebar = Named("MZP0NavDiagnosticsBtn");
        var title = Named("MZP0TitleDiagnosticsBtn");
        var host = Named("MZP0DiagnosticsHost");
        var panel = Named("MZP0DiagnosticsPanel");
        var pageHost = Named("PageHost");
        var classic = xml.Descendants().Single(node => node.Name.LocalName == "ClassicShellBody");

        Assert.Equal("OnMZP0ToggleDiagnostics", (string?)sidebar.Attribute("Click"));
        Assert.Equal("OnMZP0ToggleDiagnostics", (string?)title.Attribute("Click"));
        Assert.Equal("False", (string?)host.Attribute("IsVisible"));
        Assert.Equal("{Binding DefaultPage}", (string?)pageHost.Attribute("Content"));
        Assert.Equal("{Binding IsClassicSkin}", (string?)classic.Attribute("IsVisible"));
        Assert.Equal("MZP0OperatorPanel", panel.Name.LocalName);
        Assert.Contains(panel, host.Descendants());
        Assert.True(host.Parent == classic.Parent);
        Assert.Contains(xml.Descendants(), node => node.Name.LocalName == "Border"
            && (string?)node.Attribute("Name") == "Sidebar");
    }
}
#endif
