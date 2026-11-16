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

public sealed class CombatSimKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_simulator_skips_closed_controls_and_reopens(bool reverse)
    {
        // Bypass Bots startup and stores. The real standalone simulator creates
        // an inactive timer and a local board; no Setup/Run/Play command is used.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        var sim = new CombatSimViewModel();
        typeof(BotsViewModel).GetField("<CombatSim>k__BackingField", hidden)!.SetValue(vm, sim);
        var timer = (DispatcherTimer)typeof(CombatSimViewModel).GetField("_timer", hidden)!.GetValue(sim)!;
        var view = new CombatSimOverlay { DataContext = vm };
        var overlay = Assert.IsType<Grid>(view.Content);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1100, Height = 1000,
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
            var radios = overlay.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.Equal(3, radios.Length);
            var controls = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                control is Button or TextBox && control is not RadioButton
                && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            Assert.Equal(6, controls.OfType<TextBox>().Count());
            Assert.Contains(controls.OfType<Button>(), button => ReferenceEquals(button.Command, sim.RunCommand));
            var close = controls.OfType<Button>().First(button => button.Command is null);
            var field = controls.OfType<TextBox>().First();
            foreach (var mode in Enum.GetValues<CombatSimViewModel.EditMode>())
            {
                // Property callback only notifies mode/cursor; no SetMode command.
                sim.Mode = mode;
                Flush();
                var selected = Assert.Single(radios, radio => radio.IsChecked == true);
                var expected = controls.Append(selected).ToHashSet();
                var reached = new HashSet<InputElement>();
                Assert.True(start.Focus());
                int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
                for (int index = 0; index < budget && !expected.SetEquals(reached); index++)
                {
                    Tab();
                    if (window.FocusManager.GetFocusedElement() is InputElement current && expected.Contains(current))
                        reached.Add(current);
                }
                Assert.True(expected.SetEquals(reached), $"{mode}: reached {reached.Count}/{expected.Count}");
            }
            foreach (var owner in new InputElement[] { field, close, radios[0] })
            {
                Assert.True(owner.Focus());
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in controls.Concat(radios)) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in controls.Concat(radios)) Assert.True(control.Focus());
            }
            Assert.False(timer.IsEnabled);
            Assert.False(sim.HasResult);
            Assert.False(sim.IsPlaying);
            Assert.Empty(sim.Log);
            Assert.Equal(6, sim.SelfAp);
            Assert.Equal(3, sim.SelfMp);
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            vm.CombatSimVisible = open;
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
