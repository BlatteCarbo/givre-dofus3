using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.UiTests;

public sealed class MapPreparationKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_map_preparation_skips_closed_action_but_preserves_open_and_reopened_focus(bool reverse)
    {
        // No Main constructor, startup prewarm, service or extraction command.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declaration = new XElement(document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "MapExtractOverlay"));
        var hostDeclaration = new XElement(declaration.Name,
            document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
            document.Root.Attribute(x + "DataType"), new XAttribute(x + "CompileBindings", "True"), declaration);
        var host = Assert.IsType<Grid>(AvaloniaRuntimeXamlLoader.Load(hostDeclaration.ToString(), typeof(MainViewModel).Assembly));
        var overlay = Assert.IsType<Grid>(Assert.Single(host.Children));
        host.Children.Remove(overlay);
        var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
        var content = Assert.IsType<StackPanel>(card.Child);
        var action = Assert.Single(content.Children.OfType<Button>());
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 900, Height = 800, DataContext = vm,
            Content = new StackPanel { Children = { before, overlay, after } },
        };
        try
        {
            window.Show();
            Flush();
            Assert.Same(vm.CloseMapExtractCommand, action.Command);
            Assert.False(vm.MapExtractVisible);
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());
            Assert.False(action.Focus());

            // Both busy and finished-but-still-open states must retain access.
            // These plain presentation setters never start prewarm or a timer.
            vm.MapExtractBusy = true;
            SetOpen(true);
            Assert.True(start.Focus());
            Tab();
            Assert.Same(action, window.FocusManager.GetFocusedElement());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());
            Assert.True(action.Focus());
            SetOpen(false);
            Assert.False(overlay.IsKeyboardFocusWithin);
            Assert.False(action.Focus());
            Assert.True(vm.MapExtractBusy);
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            vm.MapExtractBusy = false;
            vm.MapExtractDoneOk = true;
            SetOpen(true);
            Assert.True(start.Focus());
            Tab();
            Assert.Same(action, window.FocusManager.GetFocusedElement());
            Assert.True(action.Focus());
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
            Assert.Equal(0, vm.MapExtractDone);
            Assert.Equal(0, vm.MapExtractTotal);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            vm.MapExtractVisible = open;
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
