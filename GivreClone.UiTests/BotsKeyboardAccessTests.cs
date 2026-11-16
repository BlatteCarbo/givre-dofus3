using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.App.Views;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace GivreClone.UiTests;

public sealed class BotsKeyboardAccessTests
{
    [AvaloniaFact]
    public void Challenge_threshold_focused_input_exposes_its_accessible_name()
    {
        var vm = CreateOfflineViewModel();
        typeof(BotsViewModel).GetField("_profile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new CombatProfile { ChallengeRules = true });
        typeof(BotsViewModel).GetField("_profileKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, "keyboard-fixture");
        var bot = new BotRowViewModel(new AccountRow(new Account { Login = "keyboard-fixture" }, new ObservableCollection<ProxyEntry>(), ""));
        typeof(BotsViewModel).GetField("<SelectedBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, bot);
        var view = new BotsView { DataContext = vm };
        view.Resources["L.Bots.Combat.ChallengeAbandon"] = "threshold-accessible-name";
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            SelectBotTab(view, "combat");   // Console is index 0, Combat index 1: select by stable Tag, not position
            Dispatcher.UIThread.RunJobs();
            var threshold = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(c => c.Classes.Contains("challengeHpThreshold"));
            // A real resource key drives both names; the test does not pin translated prose.
            var input = threshold.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(input.IsEffectivelyVisible);
            Assert.True(input.Focus());
            Assert.Same(input, window.FocusManager!.GetFocusedElement());
            Assert.Equal("threshold-accessible-name", Avalonia.Automation.AutomationProperties.GetName(input));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.Right, "next")]
    [InlineData(Key.Down, "next")]
    [InlineData(Key.Left, "prev")]
    [InlineData(Key.Up, "prev")]
    [InlineData(Key.Escape, "escape")]
    public void Map_inspection_keys_keep_focus_and_deliver_the_selected_action(Key key, string expected)
    {
        var map = new GivreClone.App.Controls.IsoMapControl { Focusable = true };
        var actions = new List<string>();
        map.KeyAction += actions.Add;
        var window = new Window { Content = map, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(map.Focus());

            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { expected }, actions);
            Assert.Same(map, window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Tab_leaves_the_map_in_both_directions_even_with_an_inspector(bool reverse, bool inspector)
    {
        var before = new TextBox();
        var map = new GivreClone.App.Controls.IsoMapControl { Focusable = true, Height = 200 };
        var after = new TextBox();
        var actions = new List<string>();
        if (inspector) map.KeyAction += actions.Add;
        var panel = new StackPanel();
        panel.Children.Add(before);
        panel.Children.Add(map);
        panel.Children.Add(after);
        var window = new Window { Content = panel, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(map.Focus());

            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(reverse ? before : after, window.FocusManager!.GetFocusedElement());
            Assert.Empty(actions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reverse_tab_input_moves_across_three_plain_fields()
    {
        var first = new TextBox();
        var middle = new TextBox();
        var last = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(middle);
        panel.Children.Add(last);
        var window = new Window { Content = panel, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(last.Focus());
            foreach (var expected in new[] { middle, first })
            {
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                Dispatcher.UIThread.RunJobs();
                Assert.Same(expected, window.FocusManager!.GetFocusedElement());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("expedition", false)]
    [InlineData("road", false)]
    [InlineData("map", false)]
    [InlineData("inventory", false)]
    [InlineData("expedition", true)]
    [InlineData("road", true)]
    [InlineData("map", true)]
    [InlineData("inventory", true)]
    public void Tab_can_reach_every_enabled_field_in_the_selected_compact_panel(string tabTag, bool reverse)
    {
        // Production binds BotsView to a BotsViewModel with a selected bot (the tabs are hidden otherwise);
        // without one, SelectedBot-gated map overlays such as the closed interaction window fall back to IsVisible=true.
        var vm = CreateOfflineViewModel();
        var bot = new BotRowViewModel(new AccountRow(new Account { Login = "keyboard-fixture" }, new ObservableCollection<ProxyEntry>(), ""));
        typeof(BotsViewModel).GetField("<SelectedBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, bot);
        var view = new BotsView { DataContext = vm };
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Assert.Same(bot, vm.SelectedBot);
            Assert.False(bot.Interaction.IsVisible);
            var tabs = SelectBotTab(view, tabTag);
            Dispatcher.UIThread.RunJobs();
            var fields = tabs.GetVisualDescendants().OfType<TextBox>().Where(field =>
                field.IsEffectivelyVisible && field.IsEffectivelyEnabled && field.Focusable
                && field.Bounds.Width > 0).ToHashSet();
            Assert.NotEmpty(fields);
            var first = reverse ? fields.Last() : fields.First();
            Assert.True(first.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var initialPoint = first.TranslatePoint(default, window)!.Value;
            Assert.InRange(initialPoint.X, 0, window.ClientSize.Width - first.Bounds.Width);
            Assert.InRange(initialPoint.Y, 0, window.ClientSize.Height - first.Bounds.Height);
            var reached = new HashSet<TextBox> { first };
            var visited = new HashSet<IInputElement> { first };
            var trace = new List<string>();
            int budget = view.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            for (int step = 0; step < budget && !fields.SetEquals(reached); step++)
            {
                if (reverse) window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
                window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var focused = window.FocusManager!.GetFocusedElement();
                Assert.NotNull(focused);
                trace.Add(focused is Control control ? $"{control.GetType().Name}#{control.Name}" : focused.GetType().Name);
                if (!visited.Add(focused)) break;
                if (focused is TextBox field && fields.Contains(field))
                {
                    reached.Add(field);
                    var point = field.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.X, 0, window.ClientSize.Width - field.Bounds.Width);
                    Assert.True(point.Y >= 0 && point.Y + field.Bounds.Height <= window.ClientSize.Height,
                        $"Offscreen {field.Name} Y={point.Y}, height={field.Bounds.Height}; ancestors: {string.Join(" -> ", field.GetVisualAncestors().OfType<Control>().Select(c => c is ScrollViewer s ? $"Scroll#{s.Name} offset={s.Offset} viewport={s.Viewport} extent={s.Extent}" : $"{c.GetType().Name}#{c.Name}[visible={c.IsVisible},dc={c.DataContext?.GetType().Name ?? "null"}]"))}");
                }
            }
            Assert.True(fields.SetEquals(reached), $"Tab reached {reached.Count}/{fields.Count} fields in Bots tab {tabTag}, reverse={reverse}; cycle: {string.Join(" -> ", trace)}");
            NativeThemeTests.Save(window, $"bots-tab-{tabTag}-720x640-keyboard-{(reverse ? "reverse" : "forward")}-cycle.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("StorageInventoryContentEvent", false)]
    [InlineData("StorageInventoryContentEvent", true)]
    [InlineData("ExchangeStartedWithPodsEvent", false)]
    [InlineData("ExchangeStartedWithPodsEvent", true)]
    public void Tab_reaches_every_open_interaction_field_inside_the_map(string openEvent, bool reverse)
    {
        // Synthetic offline pid: never a launched client. The offline BotsViewModel skips its constructor, so the 300 ms
        // timer whose Tick calls Interaction.Poll() does not exist; the only Poll is the explicit one below.
        const uint pid = 2_000_000_011;
        const long map = 191104002;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var launch = GivreClone.App.Services.Stores.Launch;
        var vm = CreateOfflineViewModel();
        var row = new AccountRow(new Account { Login = "interaction-fixture" }, new ObservableCollection<ProxyEntry>(), "") { Pid = (int)pid };
        var bot = new BotRowViewModel(row);
        typeof(BotsViewModel).GetField("<SelectedBot>k__BackingField", hidden)!.SetValue(vm, bot);
        var view = new BotsView { DataContext = vm };
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            // Server truth through the production writers: roleplay map ticket, then the decoder entry for the open event.
            typeof(LaunchService).GetMethod("BeginRoleplayMap", hidden)!.Invoke(launch, new object[] { pid, map });
            launch.Inventory.Seed(pid, Enumerable.Range(1, 12).Select(i => new InventoryStore.Item(i, 289 + i, i)));
            typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(launch, new object[] { new DecodedPacket(pid, false, "",
                openEvent, Array.Empty<GivreCore.Protobuf.ProtoField>(), Array.Empty<byte>()) });
            var expected = openEvent == "StorageInventoryContentEvent" ? ExchangeKind.Bank : ExchangeKind.Trade;
            Assert.Equal(expected, launch.CurrentExchange(pid));

            window.Show();
            SelectBotTab(view, "map");
            bot.Interaction.Poll();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(expected, bot.Interaction.Kind);
            Assert.True(bot.Interaction.IsVisible);

            var mapBorder = view.FindControl<Border>("MapTabBorder")!;
            Rect InWindow(Visual v) => new(v.TranslatePoint(default, window)!.Value, v.Bounds.Size);
            var overlay = mapBorder.GetVisualDescendants().OfType<Border>().First(b => ReferenceEquals(b.DataContext, bot.Interaction));
            Assert.True(overlay.IsEffectivelyVisible);
            var kamas = overlay.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Increment == 1000);
            Assert.True(kamas.IsEffectivelyVisible);
            var fields = overlay.GetVisualDescendants().OfType<TextBox>().Where(field =>
                field.IsEffectivelyVisible && field.IsEffectivelyEnabled && field.Focusable && field.Bounds.Width > 0).ToList();
            var kamasField = kamas.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Contains(kamasField, fields);

            var first = reverse ? fields.Last() : fields.First();
            Assert.True(first.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var reached = new HashSet<TextBox>();
            var visited = new HashSet<IInputElement>();
            var trace = new List<string>();
            void Check(TextBox field)
            {
                reached.Add(field);
                // Recomputed per focus: an outer ScrollViewer may have moved the map since the previous field.
                var mapRect = InWindow(mapBorder).Intersect(new Rect(window.ClientSize));
                var clip = field.GetVisualAncestors().OfType<Visual>().Where(a => a.ClipToBounds)
                    .Aggregate(new Rect(window.ClientSize), (acc, a) => acc.Intersect(InWindow(a)));
                var r = InWindow(field);
                Assert.True(mapRect.Contains(r) && clip.Contains(r),
                    $"{openEvent} field {fields.IndexOf(field)}/{fields.Count} (kamas={ReferenceEquals(field, kamasField)}) at {r}; map {mapRect}; clip {clip}; overlay {InWindow(overlay)}; clipping ancestors not containing it: {string.Join(" | ", field.GetVisualAncestors().OfType<Visual>().Where(a => a.ClipToBounds && !InWindow(a).Contains(r)).Select(a => $"{a.GetType().Name}#{(a as Control)?.Name} {InWindow(a)}"))}");
            }
            Check(first);
            visited.Add(first);
            int budget = view.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            for (int step = 0; step < budget && reached.Count < fields.Count; step++)
            {
                if (reverse) window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
                window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var focused = window.FocusManager!.GetFocusedElement();
                Assert.NotNull(focused);
                trace.Add(focused is Control c ? $"{c.GetType().Name}#{c.Name}" : focused.GetType().Name);
                if (!visited.Add(focused)) break;
                if (focused is TextBox field && fields.Contains(field)) Check(field);
            }
            Assert.True(reached.Count == fields.Count, $"{openEvent} reverse={reverse}: Tab reached {reached.Count}/{fields.Count}; cycle: {string.Join(" -> ", trace)}");
            NativeThemeTests.Save(window, $"bots-map-{expected.ToString().ToLowerInvariant()}-720x640-keyboard-{(reverse ? "reverse" : "forward")}.png");
        }
        finally
        {
            window.Close();
            launch.Forget(pid);
        }
    }

    // BotTabs items carry stable Tags (QuickNavigationCatalog.BotTabTags): never select a Bots tab by its XAML position.
    private static TabControl SelectBotTab(BotsView view, string tag)
    {
        var tabs = view.FindControl<TabControl>("BotTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Tag, tag));
        Assert.Equal(tag, (tabs.SelectedItem as TabItem)?.Tag);
        return tabs;
    }

    private static BotsViewModel CreateOfflineViewModel()
    {
        // Same isolation as AccountsFilterTests.CreateViewModel: no services, timers, packets or launches.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(f =>
            f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        vm.RequestedTab = -1;
        return vm;
    }
}
