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

public sealed class ProxyModalKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Compiled_proxy_edit_excludes_closed_controls_and_reopens(bool adapter, bool reverse, bool closeOnButton)
    {
        // As in AccountsFilterTests: bypass constructor subscriptions, adapter
        // enumeration and persisted proxy store. No proxy entry or service exists.
        var vm = (ProxysViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ProxysViewModel));
        typeof(ProxysViewModel).GetField("<Protocols>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new[] { "SOCKS5", "HTTP" });
        vm.EditName = "";
        vm.EditHost = "";
        vm.EditPort = "";
        vm.EditUser = "";
        vm.EditPass = "";
        vm.EditProtocol = "SOCKS5";
        vm.EditIsAdapter = adapter;
        var view = new ProxysOverlays { DataContext = vm };
        var overlay = Assert.IsType<Grid>(Assert.Single(Assert.IsType<Grid>(view.Content).Children));
        var fields = overlay.GetVisualDescendants().OfType<TextBox>().ToArray();
        var buttons = overlay.GetVisualDescendants().OfType<Button>().Where(button => button.Command is not null).ToArray();
        var protocol = Assert.Single(overlay.GetVisualDescendants().OfType<ComboBox>());
        Assert.Equal(5, fields.Length);
        Assert.Equal(2, buttons.Length);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 800, Height = 900,
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
            foreach (var field in fields) Assert.False(field.Focus());
            foreach (var button in buttons) Assert.False(button.Focus());
            Assert.False(protocol.Focus());

            SetOpen(true);
            Assert.Contains("modalOpen", overlay.Classes);
            var visibleFields = fields.Where(field => field.IsEffectivelyVisible).ToArray();
            Assert.Equal(adapter ? 1 : 5, visibleFields.Length);
            var expected = visibleFields.Cast<InputElement>().Concat(buttons).ToHashSet();
            if (!adapter) expected.Add(protocol);
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int step = 0; step < budget && !expected.SetEquals(reached); step++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement focused && expected.Contains(focused))
                    reached.Add(focused);
            }
            Assert.True(expected.SetEquals(reached), $"Reached {reached.Count}/{expected.Count} open proxy controls");
            Assert.True(visibleFields[0].Focus());
            window.KeyTextInput("fixture-name");
            Flush();
            Assert.Equal("fixture-name", visibleFields[0].Text);
            Assert.Equal("fixture-name", vm.EditName);
            Assert.Equal("SOCKS5", protocol.SelectedItem);

            InputElement closingFocus = closeOnButton ? buttons.Last() : visibleFields[0];
            Assert.True(closingFocus.Focus());
            SetOpen(false);
            Assert.False(overlay.IsKeyboardFocusWithin);
            Assert.False(closingFocus.Focus());
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            SetOpen(true);
            foreach (var field in visibleFields) Assert.True(field.Focus());
            foreach (var button in buttons) Assert.True(button.Focus());
            if (!adapter) Assert.True(protocol.Focus());
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            // No activation keys on production buttons: Save/Cancel/Test and
            // persisted-store or proxy-network operations must never execute.
        }
        finally { window.Close(); }

        void SetOpen(bool open) { vm.EditVisible = open; Flush(); }
        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
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
