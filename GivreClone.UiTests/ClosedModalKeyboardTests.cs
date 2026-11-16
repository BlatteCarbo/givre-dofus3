using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Xunit;

namespace GivreClone.UiTests;

public sealed class ClosedModalKeyboardTests
{
    [AvaloniaTheory]
    [InlineData("LangOverlay", false)]
    [InlineData("LangOverlay", true)]
    [InlineData("ConfirmOverlay", false)]
    [InlineData("ConfirmOverlay", true)]
    public void Production_gate_excludes_closed_buttons_and_allows_reopening(string name, bool reverse)
    {
        // Narrow production-declaration/style test, not MainWindow startup or handler coverage.
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declaration = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        // Interpret the actual Grid attributes with Avalonia, rather than parsing
        // binding spelling or reconstructing its implementation. Only production
        // descendants are omitted, to avoid MainWindow handlers and services.
        var standalone = new XElement(declaration.Name, declaration.Attributes());
        // Retain MainWindow's inherited data type and production default of compiled
        // bindings; dropping that context made the earlier fixture too narrow.
        var hostDeclaration = new XElement(declaration.Name,
            document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
            document.Root.Attribute(x + "DataType"),
            new XAttribute(x + "CompileBindings", "True"), standalone);
        var host = Assert.IsType<Grid>(AvaloniaRuntimeXamlLoader.Load(hostDeclaration.ToString(),
            typeof(GivreClone.App.Views.MainWindow).Assembly));
        var overlay = Assert.IsType<Grid>(Assert.Single(host.Children));
        host.Children.Remove(overlay);
        var first = new Button { Content = "First modal action" };
        var last = new Button { Content = "Last modal action" };
        var card = new Border { Child = new StackPanel { Children = { first, last } } };
        card.Classes.Add("modalCard");
        overlay.Children.Add(card);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 600, Height = 400,
            Content = new StackPanel { Children = { before, overlay, after } },
        };
        int clicks = 0;
        first.Click += (_, _) => clicks++;
        last.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var start = reverse ? after : before;
            var destination = reverse ? before : after;
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager!.GetFocusedElement());
            Assert.False(first.Focus());
            Assert.False(last.Focus());

            Open();
            Assert.True((reverse ? last : first).Focus());
            Tab();
            Assert.Same(reverse ? first : last, window.FocusManager.GetFocusedElement());
            Space();
            Assert.Equal(1, clicks);
            // Close while a descendant owns keyboard focus, then try activation before Tab.
            overlay.Classes.Remove("modalOpen");
            overlay.IsHitTestVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(first.IsKeyboardFocusWithin || last.IsKeyboardFocusWithin);
            Space();
            Assert.Equal(1, clicks);
            Assert.True(start.Focus());
            Tab();
            Assert.Same(destination, window.FocusManager.GetFocusedElement());

            Open();
            Assert.True(first.Focus());
            Space();
            Assert.Equal(2, clicks);
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Open()
        {
            overlay.Classes.Add("modalOpen");
            overlay.IsHitTestVisible = true;
            Dispatcher.UIThread.RunJobs();
        }
        void Tab()
        {
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            if (reverse) window.KeyPress(Key.LeftShift, modifiers, PhysicalKey.ShiftLeft, null);
            window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
            if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            Dispatcher.UIThread.RunJobs();
        }
        void Space()
        {
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
        }
    }
}
