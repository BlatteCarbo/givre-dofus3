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

public sealed class CombatModalKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Compiled_combat_panel_excludes_closed_controls_and_reopens(int panelIndex, bool reverse)
    {
        // Same startup-free pattern as BotsKeyboardAccessTests: no Bots constructor,
        // stores, live profile, timer or transport. Only bound collections exist.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        int edits = 0;
        vm.EditingSpell = new SpellRowViewModel(new SpellConfig(), "Offline spell", null, () => edits++);
        vm.ChallengeChoices.Add(new ChallengeChoiceRow(1, "Offline challenge", "Fixture rule",
            ChallengeSupport.Natural, true, _ => edits++));
        // Import deliberately retains its real no-source state. Setting From or
        // toggling merge/replace rebuilds from CombatProfileStore; do neither.
        var view = new CombatOverlays { DataContext = vm };
        var root = Assert.IsType<Panel>(view.Content);
        var overlay = Assert.IsType<Grid>(root.Children[panelIndex]);
        foreach (var sibling in root.Children.Where(child => !ReferenceEquals(child, overlay)).ToArray())
            root.Children.Remove(sibling);
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
            var closeCommand = panelIndex switch
            {
                0 => vm.CloseSpellConfigCommand,
                1 => vm.CloseChallengeListCommand,
                _ => vm.CloseCombatImportCommand,
            };
            var close = overlay.GetVisualDescendants().OfType<Button>()
                .First(button => ReferenceEquals(button.Command, closeCommand));
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());
            Assert.False(close.Focus());

            SetOpen(true);
            Assert.Contains("modalOpen", overlay.Classes);
            var expected = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                (control is TextBox or CheckBox || control is Button { Command: not null })
                && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            Assert.Contains(close, expected);
            if (panelIndex == 0)
            {
                Assert.Contains(expected, control => control is TextBox);
                var essential = view.FindControl<RadioButton>("cfgEssentiel")!;
                Assert.True(essential.Focus());
            }
            if (panelIndex == 1) Assert.Contains(expected, control => control is CheckBox);
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !expected.SetEquals(reached); index++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement focused && expected.Contains(focused))
                    reached.Add(focused);
            }
            Assert.True(expected.SetEquals(reached), $"Panel {panelIndex}: reached {reached.Count}/{expected.Count} controls");

            // Each case closes once on its content control (if any), then on a
            // real command button. Focus only: never mutate spell/challenge data.
            var content = expected.FirstOrDefault(control => control is TextBox or CheckBox);
            foreach (var closingFocus in content is null ? new InputElement[] { close } : new[] { content, close })
            {
                Assert.True(closingFocus.Focus());
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in expected) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in expected) Assert.True(control.Focus());
            }
            Assert.Equal(0, edits);
            Assert.Null(vm.CombatImportFrom);
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            // No activation keys, pointer scrim clicks, save, reset or import commands.
        }
        finally { window.Close(); }

        void SetOpen(bool open)
        {
            if (panelIndex == 0) vm.SpellConfigVisible = open;
            else if (panelIndex == 1) vm.ChallengeListVisible = open;
            else vm.CombatImportVisible = open;
            Flush();
        }
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
