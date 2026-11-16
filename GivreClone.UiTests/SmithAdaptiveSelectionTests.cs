using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.UiTests;

public sealed class SmithAdaptiveSelectionTests
{
    [AvaloniaTheory]
    [InlineData(1440, false)]
    [InlineData(760, false)]
    [InlineData(1440, true)]
    [InlineData(760, true)]
    public void Production_workshop_preserves_explicit_rune_gids_and_support_checks(int width, bool light)
    {
        var runeNameCalls = 0;
        var vm = new SmithMagicViewModel
        {
            Leveling = true,
            AdaptiveLeveling = true,
            LevelingRuneName = _ => { runeNameCalls++; return "Rune fictive"; },
            LoadedItemName = "Support fictif · contrôle headless",
        };
        var strength = new SmithRune(1519, 118, SmithRuneTier.Basic, 40, 1, 40);
        vm.Runes.Add(strength);
        vm.Runes.Add(new SmithRune(1521, 119, SmithRuneTier.Basic, 20, 1, 20));
        vm.Runes.Add(new SmithRune(1545, 118, SmithRuneTier.Pa, 5, 1, 5));
        var support = new BagPickRow
        {
            ObjectUid = 10001, Gid = 10, Name = "Support fictif", InBag = true, Allowed = true, IsChecked = true,
        };
        vm.Picks.Add(support);
        vm.FilteredPicks.Add(support);
        vm.Lines.Add(new SmithLineRow
        {
            EffectId = 118, Name = "Force · libellé volontairement long",
            Current = 45, NaturalMaximum = 60, Min = 60, Max = 100,
        });
        var view = new SmithMagicView { DataContext = vm };
        var window = new Window
        {
            Width = width, Height = 900, Content = view,
            Background = view.FindResource("Bg1Brush") as IBrush,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, runeNameCalls);
            var mode = view.FindControl<TabItem>("WorkshopModeTab")!;
            mode.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            var manual = view.FindControl<CheckBox>("ManualAdaptiveRunes")!;
            var runes = view.FindControl<TextBox>("AllowedRuneGidsBox")!;
            var manualSupports = view.FindControl<CheckBox>("ManualAdaptiveSupports")!;
            var supports = view.FindControl<TextBox>("AllowedSupportGidsBox")!;
            Assert.Null(vm.AdaptiveAllowedRuneGids);
            Assert.Null(vm.AdaptiveAllowedSupportGids);
            Assert.False(runes.IsEnabled);
            Assert.False(supports.IsEnabled);
            view.FindControl<Expander>("AdaptiveSelectionPicker")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            CaptureMode(window, mode, $"adaptive-{width}-{NativeThemeTests.Mode(light)}-automatic");

            manual.IsChecked = true;
            manualSupports.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.AdaptiveAllowedRuneGids);
            Assert.Empty(vm.AdaptiveAllowedRuneGids);
            Assert.NotNull(vm.AdaptiveAllowedSupportGids);
            Assert.Empty(vm.AdaptiveAllowedSupportGids);
            Assert.True(runes.IsEnabled);
            CaptureMode(window, mode, $"adaptive-{width}-{NativeThemeTests.Mode(light)}-empty");

            runes.Text = "1519, 1521, 999001";
            supports.Text = "987654, 987655";
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.AdaptiveAllowedRuneGids!.SetEquals([1519L, 1521L, 999001L]));
            Assert.True(vm.AdaptiveAllowedSupportGids!.SetEquals([987654L, 987655L]));
            Assert.DoesNotContain(vm.Picks, row => vm.AdaptiveAllowedSupportGids.Contains(row.Gid));
            Assert.True(support.IsChecked);
            CaptureMode(window, mode, $"adaptive-{width}-{NativeThemeTests.Mode(light)}-selected");

            vm.Runes.Remove(strength);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.AdaptiveAllowedRuneGids.SetEquals([1519L, 1521L, 999001L]));
            vm.Runes.Add(strength);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.AdaptiveAllowedRuneGids.SetEquals([1519L, 1521L, 999001L]));
            CaptureAllTabs(window, view, width, light);

            mode.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            Click(window, view.FindControl<Button>("ChooseAdaptiveSupports")!);
            Assert.Equal(0, view.FindControl<TabControl>("WorkshopInventoryTabs")!.SelectedIndex);
            Assert.Equal(width < 1040, view.FindControl<TabItem>("CompactInventoryTab")!.IsSelected);
            Assert.True(support.IsChecked);
            Assert.True(vm.AdaptiveAllowedRuneGids.SetEquals([1519L, 1521L, 999001L]));
            Assert.True(vm.AdaptiveAllowedSupportGids.SetEquals([987654L, 987655L]));

            mode.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            runes.Text = "-1";
            supports.Text = string.Join(",", Enumerable.Range(1, 33));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(vm.AdaptiveAllowedRuneGids);
            Assert.Empty(vm.AdaptiveAllowedSupportGids);
            runes.Text = "";
            supports.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(vm.AdaptiveAllowedRuneGids);
            manual.IsChecked = false;
            manualSupports.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.AdaptiveAllowedRuneGids);
            Assert.Null(vm.AdaptiveAllowedSupportGids);
            Assert.Equal(0, runeNameCalls);
            vm.Starting = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Grid>("WorkshopColumns")!.IsEffectivelyEnabled);
            Assert.False(manual.IsEffectivelyEnabled);
            Assert.False(view.FindControl<TabControl>("WorkshopInventoryTabs")!.IsEffectivelyEnabled);
            NativeThemeTests.Save(window, $"adaptive-{width}-{NativeThemeTests.Mode(light)}-starting.png");
            vm.Starting = false;
            vm.Running = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(manual.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    private static void CaptureMode(Window window, TabItem tab, string prefix)
    {
        var scroll = Assert.IsType<ScrollViewer>(tab.Content);
        scroll.Offset = default;
        Dispatcher.UIThread.RunJobs();
        NativeThemeTests.Save(window, prefix + "-top.png");
        scroll.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        NativeThemeTests.Save(window, prefix + "-end.png");
    }

    private static void CaptureAllTabs(Window window, SmithMagicView view, int width, bool light)
    {
        var main = view.FindControl<TabControl>("WorkshopTabs")!;
        foreach (var tab in main.Items.OfType<TabItem>().Where(t => t.IsVisible))
        {
            tab.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            NativeThemeTests.Save(window, $"workshop-{width}-{NativeThemeTests.Mode(light)}-{main.SelectedIndex}.png");
        }
        var compact = view.FindControl<TabItem>("CompactInventoryTab")!;
        if (compact.IsVisible) compact.IsSelected = true;
        else main.SelectedIndex = 0;
        var inventory = view.FindControl<TabControl>("WorkshopInventoryTabs")!;
        foreach (var tab in inventory.Items.OfType<TabItem>())
        {
            tab.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            NativeThemeTests.Save(window, $"inventory-{width}-{NativeThemeTests.Mode(light)}-{inventory.SelectedIndex}.png");
        }
    }

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var position = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(position);
        Assert.InRange(position.Value.X, 0, window.ClientSize.Width);
        Assert.InRange(position.Value.Y, 0, window.ClientSize.Height);
        window.MouseDown(position.Value, MouseButton.Left);
        window.MouseUp(position.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
