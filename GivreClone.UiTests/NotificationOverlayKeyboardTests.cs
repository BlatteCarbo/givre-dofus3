using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.UiTests;

public sealed class NotificationOverlayKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Actual_notification_panel_skips_closed_controls_and_recovers_focus(bool detail, bool reverse)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        // Bypass Main startup and notification store load / Notifier.Sink wiring.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var notifications = (NotificationCenterViewModel)RuntimeHelpers.GetUninitializedObject(typeof(NotificationCenterViewModel));
        typeof(NotificationCenterViewModel).GetField("<Items>k__BackingField", hidden)!
            .SetValue(notifications, new ObservableCollection<NotifRow>());
        typeof(MainViewModel).GetField("<Notifications>k__BackingField", hidden)!.SetValue(vm, notifications);
        var row = new NotifRow(new NotifItem
        {
            Id = "keyboard-fixture", Ts = 1700000000000, Title = "Offline detail", Body = "Detached notification body",
            Source = "Offline fixture", Kind = NotifKind.Warning,
        });
        notifications.Detail = row; // No Open command: no acknowledgement or persistence.

        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string visibility = detail ? "{Binding Notifications.DetailVisible}" : "{Binding Notifications.IsOpen}";
        var declaration = new XElement(document.Descendants().Single(element =>
            (string?)element.Attribute("Classes.modalOpen") == visibility));
        // Only the shell event handler is omitted. All production controls,
        // bindings, styles and templates are interpreted by Avalonia unchanged.
        foreach (var attribute in declaration.DescendantsAndSelf().Attributes("PointerPressed").ToArray()) attribute.Remove();
        var hostDeclaration = new XElement(declaration.Name,
            document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
            document.Root.Attribute(x + "DataType"), new XAttribute(x + "CompileBindings", "True"), declaration);
        var host = Assert.IsType<Grid>(AvaloniaRuntimeXamlLoader.Load(hostDeclaration.ToString(), typeof(MainViewModel).Assembly));
        var overlay = Assert.IsType<Grid>(Assert.Single(host.Children));
        host.Children.Remove(overlay);
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var window = new Window
        {
            Width = 1100, Height = 1000, DataContext = vm,
            Content = new StackPanel { Children = { before, overlay, after } },
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
            var controls = overlay.GetVisualDescendants().OfType<InputElement>().Where(control =>
                control is Button or CheckBox or TextBox
                && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && KeyboardNavigation.GetIsTabStop(control)).ToHashSet();
            if (detail)
            {
                Assert.Equal(2, controls.Count);
                Assert.Single(controls.OfType<Button>(), button => ReferenceEquals(button.Command, notifications.CopyDetailsCommand));
                Assert.Single(controls.OfType<Button>(), button => ReferenceEquals(button.Command, notifications.CloseDetailCommand));
            }
            else
            {
                Assert.Equal(5, controls.Count);
                Assert.Equal(3, controls.OfType<CheckBox>().Count());
                Assert.Equal(2, controls.OfType<TextBox>().Count());
            }
            var reached = new HashSet<InputElement>();
            Assert.True(start.Focus());
            int budget = window.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int index = 0; index < budget && !controls.SetEquals(reached); index++)
            {
                Tab();
                if (window.FocusManager.GetFocusedElement() is InputElement current && controls.Contains(current)) reached.Add(current);
            }
            Assert.True(controls.SetEquals(reached), $"Reached {reached.Count}/{controls.Count} notification controls");
            foreach (var owner in controls.Where(control => detail || control is TextBox).ToArray())
            {
                Assert.True(owner.Focus());
                SetOpen(false);
                Assert.False(overlay.IsKeyboardFocusWithin);
                foreach (var control in controls) Assert.False(control.Focus());
                Assert.True(start.Focus());
                Tab();
                Assert.Same(destination, window.FocusManager.GetFocusedElement());
                SetOpen(true);
                foreach (var control in controls) Assert.True(control.Focus());
            }
            Assert.False(row.Read);
            Assert.False(row.Model.Read);
            Assert.Empty(notifications.Items);
            Assert.False(notifications.UnreadOnly || notifications.IncidentsOnly || notifications.SeverityFirst);
            Assert.True(string.IsNullOrEmpty(notifications.NotificationQuery));
            Assert.True(string.IsNullOrEmpty(notifications.SourceQuery));
            var card = overlay.Children.OfType<Border>().Single(border => border.Classes.Contains("modalCard"));
            Assert.NotNull(card.Transitions);
            Assert.NotEmpty(card.Transitions);
        }
        finally { window.Close(); }

        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void SetOpen(bool open)
        {
            if (detail) notifications.DetailVisible = open;
            else notifications.IsOpen = open;
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
