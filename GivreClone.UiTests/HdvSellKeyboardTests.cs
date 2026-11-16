using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class HdvSellKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_sell_skips_closed_controls_and_reopens_without_commands(bool reverse)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        // No constructors: Bots starts services/timers and HDV subscribes to launch
        // events. Only empty dictionaries used by presentation getters are needed.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        var hdv = (HdvViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HdvViewModel));
        foreach (var owner in new object[] { vm, hdv })
            foreach (var field in owner.GetType().GetFields(hidden).Where(field =>
                field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
                field.SetValue(owner, Activator.CreateInstance(field.FieldType));
        var launch = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_hdvOpen", "_hdvMaxSlots", "_hdvTaxPct" })
        {
            var field = typeof(LaunchService).GetField(name, hidden)!;
            field.SetValue(launch, Activator.CreateInstance(field.FieldType));
        }
        typeof(HdvViewModel).GetField("_launch", hidden)!.SetValue(hdv, launch);
        // Numeric templates can normalize their initial value. Use the real load
        // guard so their existing callbacks cannot persist configuration.
        typeof(HdvViewModel).GetField("_loading", hidden)!.SetValue(hdv, true);
        typeof(HdvViewModel).GetField("_lotOptions", hidden)!.SetValue(hdv, new[] { 1 });
        hdv.TaxHintText = "";
        typeof(BotsViewModel).GetField("_hdv", hidden)!.SetValue(vm, hdv);
        var view = new HdvSellOverlay { DataContext = vm };
        var overlay = Assert.IsType<Grid>(view.Content);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1500, Height = 1200,
            Content = new StackPanel { Children = { before, view, after } },
        };
        try
        {
            window.Show();
            Flush();
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());

            SetOpen(true);
            var controls = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                control is Button or TextBox or ComboBox or NumericUpDown
                && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            var search = Assert.Single(overlay.GetVisualDescendants().OfType<TextBox>(), field =>
                ReferenceEquals(field.DataContext, hdv) && field.Parent is Grid grid && Grid.GetRow(grid) == 1);
            var refresh = Assert.Single(controls.OfType<Button>(), button => ReferenceEquals(button.Command, hdv.RefreshBankStockCommand));
            var diagnostic = Assert.Single(controls.OfType<Button>(), button => ReferenceEquals(button.Command, hdv.OpenUidOperatorCommand));
            Assert.Contains(controls.OfType<Button>(), button => ReferenceEquals(button.Command, hdv.CheckVisibleCommand));
            Assert.Equal(3, controls.OfType<ComboBox>().Count());
            Assert.Contains(search, controls);
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !controls.SetEquals(reached); index++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement current && controls.Contains(current)) reached.Add(current);
            }
            Assert.True(controls.SetEquals(reached), $"Reached {reached.Count}/{controls.Count} sell controls");
            foreach (var owner in new InputElement[] { search, refresh, diagnostic })
            {
                Assert.True(owner.Focus());
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in controls) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in controls) Assert.True(control.Focus());
            }
            Assert.Empty(hdv.SellRows);
            Assert.Empty(hdv.SellRowsView);
            Assert.Null(hdv.SelectedSellRow);
            Assert.False(hdv.BankStockLoading);
            Assert.False(hdv.BulkRunning);
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            hdv.SellModalVisible = open;
            Flush();
            Assert.Equal(open, overlay.Classes.Contains("modalOpen"));
        }
        void Tab()
        {
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            if (reverse) window.KeyPress(Key.LeftShift, modifiers, PhysicalKey.ShiftLeft, null);
            window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            Flush();
        }
    }
}
