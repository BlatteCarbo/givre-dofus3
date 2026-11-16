using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class RoadRecorderKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Compiled_recorder_panel_skips_closed_controls_and_reopens(bool ask, bool reverse)
    {
        var (vm, view, overlay) = Create(ask);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = Host(before, view, after);
        try
        {
            window.Show();
            Flush(window);
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            SendKey(window, Key.Tab, reverse);
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());

            SetOpen(true);
            var controls = overlay.GetVisualDescendants().OfType<InputElement>()
                .Where(control => control is Button or CheckBox or TextBox
                    && control.Focusable && control.IsEffectivelyEnabled && control.IsEffectivelyVisible
                    && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            Assert.Equal(ask ? 3 : 4, controls.Count);
            Assert.Equal(2, controls.OfType<Button>().Count(button => button.Command is not null));
            if (ask) Assert.Single(controls.OfType<TextBox>());
            else Assert.Equal(2, controls.OfType<CheckBox>().Count());
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !reached.SetEquals(controls); index++)
            {
                SendKey(window, Key.Tab, reverse);
                if (window.FocusManager.GetFocusedElement() is InputElement current && controls.Contains(current))
                    reached.Add(current);
            }
            Assert.True(controls.SetEquals(reached), $"Reached {reached.Count}/{controls.Count}");
            foreach (var owner in new[] { controls.First(control => control is TextBox or CheckBox), controls.OfType<Button>().First(button => button.Command is not null) })
            {
                Assert.True(owner.Focus());
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in controls) Assert.False(control.Focus());
                Assert.True(start.Focus());
                SendKey(window, Key.Tab, reverse);
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in controls) Assert.True(control.Focus());
            }
            Assert.False(vm.MapPlanGather);
            Assert.False(vm.MapPlanFight);
            Assert.Equal("Offline answer", vm.AskAnswer);
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void SetOpen(bool open)
        {
            if (ask) vm.AskVisible = open;
            else vm.MapPlanVisible = open;
            Flush(window);
            Assert.Equal(open, overlay.Classes.Contains("modalOpen"));
        }
    }

    [AvaloniaFact]
    public void Closed_ask_default_Enter_is_inert_but_open_and_reopened_Enter_reaches_confirm()
    {
        var (vm, view, overlay) = Create(true);
        var outside = new TextBox { Text = "Outside" };
        var window = Host(outside, view, new TextBox());
        // Templates are materialized only when shown; find the actual compiled
        // default button afterward. Its command is disconnected before any Enter.
        int clicks = 0;
        try
        {
            window.Show();
            Flush(window);
            var confirm = overlay.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault);
            Assert.Same(vm.AskConfirmCommand, confirm.Command);
            confirm.Bind(Button.CommandProperty, new Avalonia.Data.Binding { Source = new { Command = (System.Windows.Input.ICommand?)null }, Path = "Command" });
            Assert.Null(confirm.Command);
            confirm.Click += (_, args) => { clicks++; args.Handled = true; };

            Assert.True(outside.Focus());
            SendKey(window, Key.Enter);
            Assert.Equal(0, clicks);
            vm.AskVisible = true;
            Flush(window);
            Assert.True(outside.Focus());
            SendKey(window, Key.Enter);
            Assert.Equal(1, clicks);
            Assert.True(vm.AskVisible);
            vm.AskVisible = false;
            Flush(window);
            Assert.True(outside.Focus());
            SendKey(window, Key.Enter);
            Assert.Equal(1, clicks);
            vm.AskVisible = true;
            Flush(window);
            Assert.True(outside.Focus());
            SendKey(window, Key.Enter);
            Assert.Equal(2, clicks);
            Assert.True(vm.AskVisible);
            Assert.Equal("Offline answer", vm.AskAnswer);
        }
        finally { window.Close(); }
    }

    private static (BotsViewModel Vm, RoadRecorderOverlays View, Grid Overlay) Create(bool ask)
    {
        // Bypass Bots constructor and its RoadRecorder/Stores field initializer,
        // following BotsKeyboardAccessTests. No script, TCS, timer or services.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        vm.AskAnswer = "Offline answer";
        vm.AskTitle = "Offline question";
        vm.AskPrompt = "Offline prompt";
        vm.MapPlanTitle = "Offline map";
        var view = new RoadRecorderOverlays { DataContext = vm };
        var root = Assert.IsType<Grid>(view.Content);
        var overlay = Assert.IsType<Grid>(root.Children[ask ? 1 : 0]);
        foreach (var child in root.Children.Where(child => !ReferenceEquals(child, overlay)).ToArray())
            root.Children.Remove(child);
        return (vm, view, overlay);
    }

    private static Window Host(Control before, Control view, Control after) => new()
    {
        Width = 1000, Height = 800,
        Content = new StackPanel { Children = { before, view, after } },
    };

    private static void Flush(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static void SendKey(Window window, Key key, bool reverse = false)
    {
        var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
        if (reverse) window.KeyPress(Key.LeftShift, modifiers, PhysicalKey.ShiftLeft, null);
        var physical = key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter;
        window.KeyPress(key, modifiers, physical, key == Key.Tab ? "\t" : "\r");
        window.KeyRelease(key, modifiers, physical, key == Key.Tab ? "\t" : "\r");
        if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        Flush(window);
    }
}
