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

public sealed class PlanningOverlayKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_planning_excludes_closed_controls_even_before_editor_disable_and_reopens(bool reverse)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        // Existing offline Bots pattern: bypass startup, services and tick timer.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        // No explicit constructor, store access or timer in PlanningViewModel's
        // initializers. Keep the genuine unloaded model, never Load or edit it.
        var planning = new PlanningViewModel { ShowEditorActions = false };
        typeof(BotsViewModel).GetField("<OfflinePlanning>k__BackingField", hidden)!.SetValue(vm, planning);
        vm.OfflinePlanningTarget = new PlannedOfflineRow("Offline fixture") { CanConnect = true };
        vm.OfflineEditorEnabled = true;
        var view = new PlanningOverlays { DataContext = vm };
        var root = Assert.IsType<Grid>(view.Content);
        var overlay = Assert.IsType<Grid>(Assert.Single(root.Children));
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1100, Height = 1100,
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
            vm.OfflineEditorEnabled = false;
            Flush();
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            vm.OfflineEditorEnabled = true;
            SetOpen(true);
            var controls = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                control is Button or TextBox or ToggleSwitch
                && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            var field = Assert.Single(controls.OfType<TextBox>());
            var buttons = controls.OfType<Button>().ToArray();
            Assert.Equal(2, buttons.Count(button => ReferenceEquals(button.Command, vm.CloseOfflinePlanningCommand)));
            Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.ConnectPlannedNowCommand));
            Assert.Contains(buttons, button => ReferenceEquals(button.Command, planning.AddSlotCommand));
            Assert.Single(controls.OfType<ToggleSwitch>());
            Assert.Equal(7, controls.Count);
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !controls.SetEquals(reached); index++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement current && controls.Contains(current)) reached.Add(current);
            }
            Assert.True(controls.SetEquals(reached), $"Reached {reached.Count}/{controls.Count} planning controls");
            foreach (var owner in new InputElement[] { field, buttons.First(button => ReferenceEquals(button.Command, vm.CloseOfflinePlanningCommand)),
                buttons.Single(button => ReferenceEquals(button.Command, vm.ConnectPlannedNowCommand)) })
            {
                Assert.True(owner.Focus());
                // Direct state change deliberately leaves the delayed editor gate
                // true. No CloseOfflinePlanning command or 250 ms timer is used.
                SetOpen(false);
                Assert.True(vm.OfflineEditorEnabled);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in controls) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in controls) Assert.True(control.Focus());
            }
            Assert.Equal(0, planning.JitterMin);
            Assert.False(planning.Enabled);
            Assert.False(planning.AutoConnect);
            Assert.Empty(planning.Slots);
            Assert.Empty(planning.Scripts);
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            vm.OfflinePlanningVisible = open;
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
