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

public sealed class LevelCapKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_level_cap_skips_closed_buttons_and_preserves_open_and_reopened_access(bool reverse)
    {
        // Existing offline Bots pattern: no constructor, stores, services or tick.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        var view = new LevelCapOverlay { DataContext = vm };
        var root = Assert.IsType<Grid>(view.Content);
        var overlay = Assert.IsType<Grid>(Assert.Single(root.Children));
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 900, Height = 800,
            Content = new StackPanel { Children = { before, view, after } },
        };
        try
        {
            window.Show();
            Flush();
            var buttons = overlay.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(2, buttons.Length);
            var close = Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.CloseLevelCapModalCommand));
            var offers = Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.OpenLevelCapOffersCommand));
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());
            foreach (var button in buttons) Assert.False(button.Focus());

            SetOpen(true);
            Assert.True(start.Focus());
            Tab();
            Assert.Same(reverse ? offers : close, window.FocusManager.GetFocusedElement());
            Tab();
            Assert.Same(reverse ? close : offers, window.FocusManager.GetFocusedElement());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());
            foreach (var owner in buttons)
            {
                Assert.True(owner.Focus());
                // Plain visibility setter has no callback; never execute close,
                // offers, ShowLevelCapModal, licensing or game actions.
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var button in buttons) Assert.False(button.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var button in buttons) Assert.True(button.Focus());
            }
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            vm.LevelCapModalVisible = open;
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
