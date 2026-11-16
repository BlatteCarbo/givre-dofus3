using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using GivreClone.App.Controls.LuaEditor;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.UiTests;

public sealed class LuaOverlayKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    public void Compiled_lua_panel_excludes_closed_controls_and_reopens(int panel, bool reverse)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        foreach (var field in typeof(BotsViewModel).GetFields(hidden).Where(field =>
            field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        var document = new TextDocument("-- ephemeral fixture\nlocal value = 1\n");
        SetBacking("LuaDocument", document);
        SetBacking("LuaDiagnostics", Array.Empty<LuaDiagnostic>());
        SetBacking("LuaDeclarations", Array.Empty<(string, bool, int, string?)>());
        vm.LuaGoToText = "2";
        vm.LuaRecentFiles.Add(new LuaRecentRow("Offline.lua", "Fixture", "fixture-only.lua", "Fixture", true));
        // No file checks or recent/template/API commands. API query/category
        // setters load the catalog, so leave the genuine empty API state alone.
        var view = new LuaEditorOverlays { DataContext = vm };
        // TestApp omits the editor theme loaded by production App.axaml.
        // Keep this fixture local instead of changing shared test startup.
        view.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        var root = Assert.IsType<Grid>(view.Content);
        var overlay = Assert.IsType<Grid>(root.Children[panel]);
        foreach (var sibling in root.Children.Where(child => !ReferenceEquals(child, overlay)).ToArray())
            root.Children.Remove(sibling);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1500, Height = 1100,
            Content = new StackPanel { Children = { before, view, after } },
        };
        var properties = new[] { nameof(vm.LuaFullscreenOpen), nameof(vm.LuaTemplatesOpen), nameof(vm.LuaRecentOpen),
            nameof(vm.LuaApiRefOpen), nameof(vm.LuaGoToOpen), nameof(vm.LuaUnsavedOpen) };
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
            var expected = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                control is Button or TextBox && control is not RadioButton
                && control.Focusable && control.IsEffectivelyEnabled && control.IsEffectivelyVisible
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            Assert.NotEmpty(expected);
            if (panel is 3 or 4) Assert.Single(expected.OfType<TextBox>());
            if (panel == 5) Assert.Equal(3, expected.Count);
            if (panel == 2) Assert.Equal(4, expected.Count);
            var selectedRadio = overlay.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true);
            if (selectedRadio is not null) expected.Add(selectedRadio);
            var reached = new HashSet<InputElement>();
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            // AvaloniaEdit consumes Tab as indentation. Approach from both
            // boundaries, stopping before sending Tab inside a real TextArea.
            // This keeps the document unchanged and starts no folding timer.
            foreach (var backwards in new[] { reverse, !reverse })
            {
                Assert.True((backwards ? after : before).Focus());
                for (int index = 0; index < budget && !expected.SetEquals(reached); index++)
                {
                    if (window.FocusManager.GetFocusedElement() is AvaloniaEdit.Editing.TextArea) break;
                    Tab(backwards);
                    if (window.FocusManager.GetFocusedElement() is InputElement current && expected.Contains(current)) reached.Add(current);
                }
            }
            Assert.True(expected.SetEquals(reached), $"Panel {panel}: reached {reached.Count}/{expected.Count}");
            var editor = panel == 0 ? Assert.IsType<LuaCodeEditor>(view.FindControl<ContentControl>("FsEditorHost")!.Content) : null;
            if (editor is not null)
            {
                Assert.Same(document, editor.Editor.Document);
                Assert.True(editor.Editor.TextArea.Focus(), "Initial attached fullscreen TextArea focus");
            }
            var owners = new List<InputElement> { expected.OfType<Button>().First(button => button.Command is not null) };
            if (editor is not null) owners.Add(editor.Editor.TextArea);
            else if (expected.OfType<TextBox>().FirstOrDefault() is { } field) owners.Add(field);
            foreach (var owner in owners)
            {
                Assert.True((editor is not null && owner is not Button ? editor.Editor.TextArea : owner).Focus(), $"Closure owner {owner.GetType().Name} focus");
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in expected) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in expected.Where(control => control is Avalonia.Visual visual && visual.GetVisualAncestors().Contains(window)))
                    Assert.True(control.Focus(), $"Reopened {control.GetType().Name} focus");
                if (editor is not null)
                {
                    editor = Assert.IsType<LuaCodeEditor>(view.FindControl<ContentControl>("FsEditorHost")!.Content);
                    Assert.Same(document, editor.Editor.Document);
                    Assert.True(editor.Editor.TextArea.Focus(), "Reopened attached fullscreen TextArea focus");
                }
            }
            Assert.Equal("-- ephemeral fixture\nlocal value = 1\n", document.Text);
            Assert.Null(typeof(BotsViewModel).GetField("_luaAnalyzeTimer", hidden)!.GetValue(vm));
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            SetOpen(false);
        }
        finally { window.Close(); }

        void SetBacking(string name, object value) => typeof(BotsViewModel).GetField("<" + name + ">k__BackingField", hidden)!.SetValue(vm, value);
        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            typeof(BotsViewModel).GetProperty(properties[panel])!.SetValue(vm, open);
            Flush();
            Assert.Equal(open, overlay.Classes.Contains("modalOpen"));
        }
        void Tab(bool? backwards = null)
        {
            bool shift = backwards ?? reverse;
            var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            if (shift) window.KeyPress(Key.LeftShift, modifiers, PhysicalKey.ShiftLeft, null);
            window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            if (shift) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            Flush();
        }
    }
}
