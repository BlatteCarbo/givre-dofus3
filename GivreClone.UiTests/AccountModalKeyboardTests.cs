using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class AccountModalKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void Compiled_account_modal_excludes_closed_descendants_and_reopens(bool shield, bool reverse, bool closeOnButton)
    {
        var vm = AccountsFilterTests.CreateViewModel();
        vm.EditIsKeyData = true;
        vm.EditLogin = "";
        vm.EditLabel = "";
        vm.EditPassword = "";
        vm.ShieldCode = "";
        var view = new AccountOverlays { DataContext = vm };
        var root = Assert.IsType<Grid>(view.Content);
        var overlay = Assert.IsType<Grid>(root.Children[shield ? 1 : 2]);
        // Keep the real compiled target in its original owner; remove unrelated
        // sibling modals so their existing keyboard defects cannot mask this one.
        foreach (var sibling in root.Children.Where(child => !ReferenceEquals(child, overlay)).ToArray())
            root.Children.Remove(sibling);
        var fields = overlay.GetVisualDescendants().OfType<TextBox>().ToArray();
        var buttons = overlay.GetVisualDescendants().OfType<Button>().Where(button => button.Command is not null).ToArray();
        Assert.NotEmpty(fields);
        Assert.Equal(2, buttons.Length);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 800, Height = 1000,
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

            SetOpen(true);
            Assert.Contains("modalOpen", overlay.Classes);
            var expected = fields.Cast<InputElement>().Concat(buttons).ToHashSet();
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int step = 0; step < budget && !expected.SetEquals(reached); step++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement focused && expected.Contains(focused))
                    reached.Add(focused);
            }
            Assert.True(expected.SetEquals(reached), $"Reached {reached.Count}/{expected.Count} open fields/buttons");
            Assert.True(fields[0].Focus());
            window.KeyTextInput("1234");
            Flush();
            Assert.Equal("1234", fields[0].Text);
            Assert.Equal("1234", shield ? vm.ShieldCode : vm.EditLogin);

            InputElement closingFocus = closeOnButton ? buttons.Last() : fields[0];
            Assert.True(closingFocus.Focus());
            SetOpen(false);
            Assert.False(overlay.IsKeyboardFocusWithin);
            Assert.False(closingFocus.Focus());
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            SetOpen(true);
            foreach (var field in fields) Assert.True(field.Focus());
            foreach (var button in buttons) Assert.True(button.Focus());
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            // Never press Space/Enter on production buttons: commands remain bound,
            // but Shield confirmation and account persistence must not be invoked.
        }
        finally { window.Close(); }

        void SetOpen(bool open)
        {
            if (shield) vm.ShieldVisible = open;
            else vm.EditVisible = open;
            Flush();
        }
        void Flush()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
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
