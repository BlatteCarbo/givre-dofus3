using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services.LotM;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace GivreClone.UiTests;

public sealed class ExpeditionLayoutTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData(720, 640)]
    [InlineData(1200, 800)]
    public void Populated_expedition_tab_keeps_every_control_reachable_without_horizontal_overflow(int width, int height)
    {
        // BotsView remembers the last tab in a static; restore it so later tests keep their own starting tab.
        var lastTab = typeof(BotsView).GetField("_lastTab", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousTab = lastTab.GetValue(null);
        var vm = CreatePopulatedViewModel();
        var view = new BotsView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        // Shipped French labels, scoped to this window (no global Loc.Apply / AppConfig write).
        window.Resources.MergedDictionaries.Add(new GivreClone.App.Lang.Fr());
        try
        {
            window.Show();
            var layout = view.FindControl<Grid>("ExpeditionLayout")!;
            var tab = layout.FindLogicalAncestorOfType<TabItem>()!;
            view.FindControl<TabControl>("BotTabs")!.SelectedItem = tab;
            Dispatcher.UIThread.RunJobs();
            var route = view.FindControl<Expander>("ExpeditionRoutePreview")!;
            route.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(tab.IsSelected);
            Assert.True(layout.IsEffectivelyVisible);
            Assert.True(route.IsExpanded && route.IsEffectivelyVisible);
            var routeText = route.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.ExpRouteText);
            Assert.True(routeText.Bounds.Height > 200, $"route fixture not rendered expanded: {routeText.Bounds}");

            // No ScrollViewer of the tab may hide content sideways: the page and the tab disable horizontal scrolling.
            foreach (var scroll in tab.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.Viewport.Width > 0))
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                    $"{width}x{height}: ScrollViewer#{scroll.Name} extent {scroll.Extent.Width} > viewport {scroll.Viewport.Width} (horizontal={scroll.HorizontalScrollBarVisibility})");

            var settings = view.FindControl<StackPanel>("ExpeditionSettings")!;
            var details = view.FindControl<ScrollViewer>("ExpeditionDetails")!;
            Assert.False(InWindow(settings, window).Intersects(InWindow(details, window)),
                $"{width}x{height}: settings {InWindow(settings, window)} overlaps details {InWindow(details, window)}");

            var targets = new List<Control>
            {
                view.FindControl<ListBox>("ExpSubAreaList")!,
                view.FindControl<Button>("ExpeditionStartButton")!,
                view.FindControl<Button>("ExpeditionStopButton")!,
                view.FindControl<TextBlock>("ExpeditionRouteLastVisit")!,
                routeText,
            };
            targets.AddRange(layout.GetVisualDescendants().OfType<Control>().Where(c =>
                (c is Button or CheckBox or ComboBox or NumericUpDown or Slider) && c.IsEffectivelyVisible && c.Bounds.Width > 0));
            Assert.Contains(targets, c => c is Button { DataContext: AnomalyRow });
            foreach (var target in targets.Distinct())
            {
                Assert.True(target.IsEffectivelyVisible, $"{Describe(target)} hidden");
                foreach (bool bottom in new[] { false, true })
                {
                    Reveal(target, bottom, window);
                    var clip = Clip(target, window);
                    var edge = target.TranslatePoint(new Point(0, bottom ? target.Bounds.Height : 0), window)!.Value;
                    Assert.True(edge.X >= clip.Left - 0.5 && edge.X + target.Bounds.Width <= clip.Right + 0.5
                        && edge.Y >= clip.Top - 0.5 && edge.Y <= clip.Bottom + 0.5,
                        $"{width}x{height}: {Describe(target)} {(bottom ? "bottom" : "top")} edge at {edge}, width {target.Bounds.Width}, unreachable in clip {clip}; scrollers: {string.Join(" | ", target.GetVisualAncestors().OfType<ScrollViewer>().Select(s => $"{s.Name} offset={s.Offset} viewport={s.Viewport} extent={s.Extent} v={s.VerticalScrollBarVisibility}"))}");
                }
            }
            Reveal(view.FindControl<Button>("ExpeditionStartButton")!, true, window);
            NativeThemeTests.Save(window, $"bots-expedition-populated-{width}x{height}-start.png");
        }
        finally
        {
            window.Close();
            lastTab.SetValue(null, previousTab);
        }
    }

    // Scrolls each enabled vertical ancestor (innermost first) until the requested edge of the target is inside its viewport.
    private static void Reveal(Control target, bool bottom, Window window)
    {
        foreach (var scroll in target.GetVisualAncestors().OfType<ScrollViewer>().ToArray())
        {
            if (scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled || scroll.Extent.Height <= scroll.Viewport.Height) continue;
            var point = target.TranslatePoint(new Point(0, bottom ? target.Bounds.Height : 0), scroll)!.Value;
            double delta = bottom ? point.Y - scroll.Viewport.Height : point.Y;
            double y = Math.Clamp(scroll.Offset.Y + delta, 0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, y);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static Rect Clip(Visual target, Window window) => target.GetVisualAncestors().OfType<Visual>()
        .Where(a => a.ClipToBounds).Aggregate(new Rect(window.ClientSize), (acc, a) => acc.Intersect(InWindow(a, window)));

    private static Rect InWindow(Visual visual, Window window) => new(visual.TranslatePoint(default, window)!.Value, visual.Bounds.Size);

    private static string Describe(Control control) => $"{control.GetType().Name}#{control.Name}[{control.DataContext?.GetType().Name}]";

    private static BotsViewModel CreatePopulatedViewModel()
    {
        // Same isolation as BotsKeyboardAccessTests.CreateOfflineViewModel: constructor skipped (no services, timers,
        // packets or launches). State is written to backing fields / collections only, so no generated On*Changed hook
        // (ExpeditionOpen, RefreshAnomalies, SaveElixir, GameData queries) runs.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        foreach (var field in typeof(BotsViewModel).GetFields(Hidden).Where(f =>
            f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        vm.RequestedTab = -1;
        void Set(string property, object? value) => typeof(BotsViewModel).GetField($"<{property}>k__BackingField", Hidden)!.SetValue(vm, value);

        // Offline pid 0: ExpHdvText / ExpRouteProgress take their no-character branch without touching launch state.
        Set(nameof(BotsViewModel.SelectedBot), new BotRowViewModel(new AccountRow(new Account { Login = "expedition-layout-fixture" },
            new ObservableCollection<ProxyEntry>(), "")));
        Set(nameof(BotsViewModel.ExpTabSelected), true);   // TwoWay push from TabItem.IsSelected is then a no-op
        const string longZone = "Territoire des Bandits Masqués de la Lisière Orientale et Marécages Nauséabonds";
        var area = new ExpArea(1, longZone);
        vm.ExpAreas.Add(area);
        Set(nameof(BotsViewModel.ExpArea), area);
        for (int i = 1; i <= 24; i++)
            vm.ExpSubAreas.Add(new ExpSubArea(i, $"Sous-zone {i} : {longZone}", 40 + i, 6));
        for (int i = 1; i <= 3; i++)
            vm.ExpAnomalies.Add(new AnomalyRow(i, 1, $"Anomalie {i} : {longZone}", longZone, 120 + i, 35 * i, 3600 * i + 600));
        for (int i = 1; i <= 9; i++)
            vm.ExpMonsters.Add(new ExpMonsterCard(new ExpMonster(10_000 + i, $"Bouftou Royal Légendaire Masqué {i}", 50 + i, 60 + i)));
        Set(nameof(BotsViewModel.ExpSummary), "148 carte(s) · groupes de 1 à 8 monstres · 3 obligatoire(s) · 2 interdit(s) · récolte · banque auto · HDV avant banque · " + longZone);
        Set(nameof(BotsViewModel.ExpValueText), "Valeur moyenne ≈ 12 345 kamas/monstre (prix HDV locaux, 4 drop(s) sans prix) · meilleurs : " + longZone);
        Set(nameof(BotsViewModel.ExpRouteText), "148 cartes retenues · 12 écartées\n"
            + string.Join("\n", Enumerable.Range(1, 16).Select(i => $"{i}. [{-30 + i},{12 - i}] · {191_104_000 + i} · {longZone}"))
            + "\n… et 132 autres cartes");
        Set(nameof(BotsViewModel.ExpStatus), "Dernier arrêt : pods pleins sur la carte la plus éloignée de " + longZone + ", retour banque puis reprise.");
        Set(nameof(BotsViewModel.ExpElixirState), "Élixir uchronique majeur actif · 4 combat(s)\nFragments : 12");
        Set(nameof(BotsViewModel.ExpElixirRank), 3);   // shows the max-price / stock NumericUpDowns
        foreach (var (name, value) in new (string, object?)[]
        {
            (nameof(BotsViewModel.ExpMinGroup), 1d), (nameof(BotsViewModel.ExpMaxGroup), 8d), (nameof(BotsViewModel.ExpMaxFights), 0m),
            (nameof(BotsViewModel.ExpMaxMinutes), 0m), (nameof(BotsViewModel.ExpStopPods), 95d), (nameof(BotsViewModel.ExpBank), true),
            (nameof(BotsViewModel.ExpFight), true), (nameof(BotsViewModel.ExpGather), true), (nameof(BotsViewModel.ExpHdv), true),
            (nameof(BotsViewModel.ExpElixirMaxPrice), 250_000m), (nameof(BotsViewModel.ExpElixirStock), 2m),
            (nameof(BotsViewModel.ExpRuns), 1m), (nameof(BotsViewModel.ExpDungeonFilter), ""), (nameof(BotsViewModel.ExpDungeonDetail), ""),
            (nameof(BotsViewModel.ExpDungeonBudgetText), ""),
        })
            Set(name, value);
        return vm;
    }
}
