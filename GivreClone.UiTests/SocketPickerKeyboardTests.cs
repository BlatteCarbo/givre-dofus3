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

public sealed class SocketPickerKeyboardTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int step in new[] { 0, 1, 2, 3 })
            foreach (bool reverse in new[] { false, true })
                foreach (bool closeOnButton in new[] { false, true })
                    yield return new object[] { step, reverse, closeOnButton };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Compiled_picker_excludes_closed_controls_and_preserves_state(int step, bool reverse, bool closeOnButton)
    {
        var vm = AccountsFilterTests.CreateViewModel();
        vm.ServerPickTitle = "Offline picker";
        vm.ServerPickLogin = "offline-fixture";
        vm.ServerPickShowSearch = step < 2;
        vm.CreateAllowed = true;
        vm.CreateName = "";
        vm.DeleteAnswer = "";
        vm.DeleteName = "";
        var character = new SocketCharCard { Name = "Fixture", ServerId = 42, LevelText = "12", ClassName = "Fixture" };
        vm.ServerPickServers.Add(new SocketServerCard { ServerId = 42, Name = "Fixture", Initial = "F", Online = true });
        vm.ServerPickChars.Add(character);
        vm.DeleteTarget = character;
        vm.ServerPickStep = step;
        var view = new SocketServerPickOverlay { DataContext = vm };
        var overlay = Assert.IsType<Grid>(Assert.Single(Assert.IsType<Grid>(view.Content).Children));
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1000, Height = 1000,
            Content = new StackPanel { Children = { before, view, after } },
        };
        try
        {
            window.Show();
            Flush();
            // Identity comes from the actual compiled binding, never label attribution.
            var cancel = overlay.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, vm.CancelServerPickCommand));
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());
            Assert.False(cancel.Focus());

            SetOpen(true);
            Assert.Contains("modalOpen", overlay.Classes);
            var fields = overlay.GetVisualDescendants().OfType<TextBox>()
                .Where(field => field.IsEffectivelyVisible && field.IsEffectivelyEnabled).ToArray();
            Assert.NotEmpty(fields);
            var buttons = overlay.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Command is not null && button.IsEffectivelyVisible && button.IsEffectivelyEnabled
                && button.Focusable && KeyboardNavigation.GetIsTabStop(button)).ToArray();
            Assert.Contains(cancel, buttons);
            var expected = fields.Cast<InputElement>().Concat(buttons).ToHashSet();
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !expected.SetEquals(reached); index++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement focused && expected.Contains(focused))
                    reached.Add(focused);
            }
            Assert.True(expected.SetEquals(reached), $"Step {step}: reached {reached.Count}/{expected.Count} controls");
            Assert.True(fields[0].Focus());
            if (step >= 2)
            {
                // Search edits rebuild connection-backed lists; test only its
                // focus here. Creation/deletion text setters are startup-free.
                window.KeyTextInput("Fixture");
                Flush();
                Assert.Equal("Fixture", fields[0].Text);
                Assert.Equal("Fixture", step == 2 ? vm.CreateName : vm.DeleteAnswer);
            }
            string? retainedText = fields[0].Text;

            InputElement closingFocus = closeOnButton ? cancel : fields[0];
            Assert.True(closingFocus.Focus());
            SetOpen(false);
            Assert.False(overlay.IsKeyboardFocusWithin);
            Assert.False(closingFocus.Focus());
            foreach (var field in fields) Assert.False(field.Focus());
            foreach (var button in buttons) Assert.False(button.Focus());
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            SetOpen(true);
            Assert.Equal(step, vm.ServerPickStep);
            Assert.Equal(step == 2, vm.ServerPickOnCreate);
            Assert.Equal(step == 3, vm.ServerPickOnDelete);
            Assert.True(vm.CreateAllowed);
            Assert.Equal(retainedText, fields[0].Text);
            foreach (var field in fields) Assert.True(field.Focus());
            foreach (var button in buttons) Assert.True(button.Focus());
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            // No command is executed: no server/character choice, cancellation,
            // creation, deletion, account or network operation is permitted here.
        }
        finally { window.Close(); }

        void SetOpen(bool open) { vm.ServerPickVisible = open; Flush(); }
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
