using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services.Social;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class CompactRouteCaptureTests
{
    public static IEnumerable<object[]> Routes()
    {
        foreach (string route in new[] { "teams", "social", "adapters", "atlas", "craft", "professions",
            "market", "creation", "soulsync", "myaccount", "accounts", "planning", "bots", "dashboard", "settings", "proxys" })
            foreach (var size in new[] { (Width: 720, Height: 640), (Width: 1200, Height: 800) })
                foreach (bool light in new[] { false, true })
                    yield return new object[] { route, size.Width, size.Height, light };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Routes))]
    public void Inactive_route_captures_without_starting_a_session(string route, int width, int height, bool light)
    {
        UserControl view = route switch
        {
            "teams" => new TeamsView(),
            "social" => new SocialView(),
            "adapters" => new AdaptersView(),
            "atlas" => new WorldAtlasPanel(),
            "craft" => new ItemCraftPlannerPanel(),
            "professions" => new ProfessionPlannerPanel(),
            "market" => new MarketInsightsPanel(),
            "creation" => new CharacterCreationTabView(),
            "soulsync" => new SoulSyncView(),
            "myaccount" => new MyAccountView(),
            "accounts" => new AccountsView(),
            "planning" => new PlanningEditor(),
            "bots" => new BotsView(),
            "dashboard" => new DashboardView(),
            "settings" => new SettingsView(),
            "proxys" => new ProxysView(),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        if (view is SettingsView settings)
        {
            settings.FindControl<TextBox>("RemoteTokenBox")!.Text = "";
            settings.FindControl<TextBlock>("RemoteAddresses")!.Text = "";
        }
        if (route == "social")
        {
            var social = new SocialViewModel();
            for (int index = 0; index < 40; index++)
            {
                social.Rows.Add(new ProfileRow($"Statistique {index}",
                    $"Valeur {index} : texte long de profil pour vérifier le retour à la ligne dans la fenêtre compacte."));
                social.Messages.Add(new SocialChatLine(0, $"Message {index}",
                    $"Texte observé {index} : une longue ligne de discussion doit rester accessible après défilement.",
                    "", false, null, Array.Empty<SocialLinkVm>()));
            }
            view.DataContext = social;
        }
        if (view is WorldAtlasPanel atlas)
        {
            var evidence = new WorldAtlasEvidence();
            Assert.True(atlas.LoadSnapshot(new WorldAtlasSnapshot("Fixture QA non vérifiée", evidence,
                Enumerable.Range(1, 40).Select(index => new WorldAtlasMapRecord(index, index, 0,
                    "Zone de test locale", WorldAtlasLayer.Surface,
                    $"Carte {index} : libellé long consultatif sans preuve de jeu", evidence)).ToArray(),
                Array.Empty<WorldAtlasTransitionRecord>())));
        }
        if (view is ItemCraftPlannerPanel craft)
            craft.LoadCatalog(Enumerable.Range(1, 40).Select(index => new ItemCraftCatalogEntry(
                index.ToString(), $"Objet de test {index} : libellé long pour la matrice compacte",
                "Objet", "Fixture QA non vérifiée", false)));
        if (view is ProfessionPlannerPanel professions)
            professions.LoadCatalog(Enumerable.Range(1, 40).Select(index => new ProfessionCatalogRecipe(
                index.ToString(), "Mineur", $"Recette locale {index} : libellé long pour la fenêtre compacte",
                "Fixture QA non vérifiée", false)));
        var window = new Window { Content = view, Width = width, Height = height,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        string capturePrefix = $"route-{route}-{width}x{height}-{NativeThemeTests.Mode(light)}";
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(window.ClientSize.Width, view.Bounds.Width);
            Assert.Equal(window.ClientSize.Height, view.Bounds.Height);
            foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(control => control.IsEffectivelyVisible && control.Bounds.Height > 0))
            {
                Assert.InRange(scroll.Viewport.Width, 0, window.ClientSize.Width);
                if (!scroll.GetVisualAncestors().OfType<ScrollViewer>().Any(parent =>
                    parent.IsEffectivelyVisible && parent.Extent.Height > parent.Viewport.Height))
                    Assert.True(scroll.Viewport.Height <= window.ClientSize.Height,
                        $"{route}: viewport {scroll.Viewport.Height} exceeds window without a scrolling ancestor");
            }
            NativeThemeTests.Save(window, $"{capturePrefix}-inactive.png");
            var verifiedLists = new HashSet<ListBox>();
            bool horizontalScrollVerified = false;
            var horizontallyAccessibleStates = new HashSet<string>();
            void CaptureState(string state)
            {
                NativeThemeTests.Save(window, $"{capturePrefix}-{state}.png");
                foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(control => control.IsEffectivelyVisible && control.Viewport.Width > 0
                        && control.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled))
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                        $"{route}/{state}: horizontal overflow {scroll.Extent.Width} > {scroll.Viewport.Width} with scrolling disabled");
                foreach (var horizontal in view.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(control => control.IsEffectivelyVisible && control.Extent.Width > control.Viewport.Width
                        && control.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled).ToArray())
                {
                    var previous = horizontal.Offset;
                    horizontal.Offset = new Vector(horizontal.Extent.Width - horizontal.Viewport.Width, previous.Y);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(horizontal.Offset.X > 0);
                    horizontalScrollVerified = true;
                    horizontallyAccessibleStates.Add(state);
                    NativeThemeTests.Save(window, $"{capturePrefix}-{state}-right.png");
                    horizontal.Offset = previous;
                    Dispatcher.UIThread.RunJobs();
                }
                int scrollIndex = 0;
                foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(control => control.IsEffectivelyVisible && control.Extent.Height > control.Viewport.Height).ToArray())
                {
                    var previous = scroll.Offset;
                    scroll.ScrollToEnd();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(scroll.Offset.Y > 0);
                    if (route == "social" && scroll.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() is { Items.Count: 40 } list)
                    {
                        var last = list.ContainerFromIndex(39);
                        Assert.NotNull(last);
                        var point = last.TranslatePoint(default, window);
                        Assert.NotNull(point);
                        Assert.InRange(point.Value.Y, 0, window.ClientSize.Height - last.Bounds.Height);
                        verifiedLists.Add(list);
                    }
                    NativeThemeTests.Save(window, $"{capturePrefix}-{state}-scroll-{scrollIndex++}-end.png");
                    scroll.Offset = previous;
                    Dispatcher.UIThread.RunJobs();
                }
            }
            void CaptureTabs(Control owner, string path)
            {
                var children = owner.GetVisualDescendants().OfType<TabControl>()
                    .Where(tabs => ReferenceEquals(tabs.GetVisualAncestors().OfType<TabControl>().FirstOrDefault(),
                        owner as TabControl)).ToArray();
                for (int tabIndex = 0; tabIndex < children.Length; tabIndex++)
                {
                    var tabs = children[tabIndex];
                    int previous = tabs.SelectedIndex;
                    for (int index = 0; index < tabs.Items.Count; index++)
                    {
                        tabs.SelectedIndex = index;
                        Dispatcher.UIThread.RunJobs();
                        foreach (var input in tabs.GetVisualDescendants().OfType<TextBox>()
                            .Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0))
                        {
                            var point = input.TranslatePoint(default, window);
                            Assert.NotNull(point);
                            bool scrollAccessible = input.GetVisualAncestors().OfType<ScrollViewer>().Any(parent =>
                                parent.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                                && parent.Extent.Width > parent.Viewport.Width);
                            if (scrollAccessible)
                            {
                                if (route == "bots" && input.IsEffectivelyEnabled && input.Focusable)
                                {
                                    Assert.True(input.Focus());
                                    Dispatcher.UIThread.RunJobs();
                                    window.UpdateLayout();
                                    var focusedPoint = input.TranslatePoint(default, window)!.Value;
                                    Assert.True(focusedPoint.X >= 0 && focusedPoint.X + input.Bounds.Width <= window.ClientSize.Width,
                                        $"Focused Bots field is clipped in {path}/{tabIndex}/{index}: x={focusedPoint.X}, width={input.Bounds.Width}");
                                }
                                foreach (var parent in input.GetVisualAncestors().OfType<ScrollViewer>().Where(parent =>
                                    parent.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                                    && parent.Extent.Width > parent.Viewport.Width))
                                {
                                    var relative = input.TranslatePoint(default, parent)!.Value;
                                    double delta = relative.X < 0 ? relative.X
                                        : Math.Max(0, relative.X + input.Bounds.Width - parent.Viewport.Width);
                                    parent.Offset = new Vector(parent.Offset.X + delta, parent.Offset.Y);
                                    Dispatcher.UIThread.RunJobs();
                                    window.UpdateLayout();
                                }
                                point = input.TranslatePoint(default, window);
                                Assert.NotNull(point);
                            }
                            Assert.True(point.Value.X >= 0 && point.Value.X + input.Bounds.Width <= window.ClientSize.Width,
                                $"{route}/{path}/{tabIndex}/{index}: input {input.Name} ({input.PlaceholderText}) at {point.Value.X}, width {input.Bounds.Width}, window {window.ClientSize.Width}; expanders: {string.Join(" | ", input.GetVisualAncestors().OfType<Expander>().Select(parent => $"{parent.Header}: expanded={parent.IsExpanded}"))}; owners: {string.Join(" | ", input.GetVisualAncestors().OfType<ScrollViewer>().Select(parent => $"{parent.Name}: bounds={parent.Bounds}, extent={parent.Extent}, viewport={parent.Viewport}, offset={parent.Offset}, horizontal={parent.HorizontalScrollBarVisibility}"))}");
                        }
                        string state = $"{path}-{tabIndex}-{index}";
                        CaptureState(state);
                        CaptureTabs(tabs, state);
                    }
                    tabs.SelectedIndex = previous;
                    Dispatcher.UIThread.RunJobs();
                }
            }
            CaptureState("initial");
            CaptureTabs(view, "tabs");
            if (route == "social") Assert.Equal(2, verifiedLists.Count);
            if (route == "atlas" && width == 720) Assert.True(horizontalScrollVerified);
            if (route == "bots" && width == 720)
                foreach (string tag in new[] { "road", "map", "inventory" })
                    Assert.Contains($"tabs-0-{GivreClone.App.Services.UiNavigation.QuickNavigationCatalog.BotTabIndexOf(tag)}", horizontallyAccessibleStates);
        }
        finally { window.Close(); }
    }
}
