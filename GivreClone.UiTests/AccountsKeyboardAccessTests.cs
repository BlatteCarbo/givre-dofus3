using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using System.Collections.ObjectModel;
using Xunit;

namespace GivreClone.UiTests;

public sealed class AccountsKeyboardAccessTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool reverse in new[] { false, true })
            foreach (int count in new[] { 0, 1, 12 })
                foreach (bool light in new[] { false, true })
                    yield return new object[] { reverse, count, light };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Tab_reaches_account_fields_and_scrolls_them_into_the_compact_viewport(bool reverse, int count, bool light)
    {
        // The populated fixture bypasses startup; no button is activated.
        var view = new AccountsView();
        if (count > 0)
        {
            var vm = AccountsFilterTests.CreateViewModel();
            for (int index = 0; index < count; index++)
                vm.Accounts.Add(new AccountRow(new Account { Login = $"keyboard-fixture-{index}" }, new ObservableCollection<ProxyEntry>(), ""));
            view.DataContext = vm;
        }
        var window = new Window { Content = view, Width = 720, Height = 640,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var fields = view.GetVisualDescendants().OfType<InputElement>().Where(field =>
                (field is TextBox or ComboBox or Button or CheckBox) &&
                field.IsEffectivelyVisible && field.IsEffectivelyEnabled && field.Focusable
                && KeyboardNavigation.GetIsTabStop(field) && field.Bounds.Width > 0).ToHashSet();
            Assert.NotEmpty(fields);
            if (count > 0)
                Assert.Contains(fields, field => field.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Rechercher des comptes");
            var first = reverse ? fields.Last() : fields.First();
            Assert.True(first.Focus());
            var reached = new HashSet<InputElement> { first };
            var visited = new HashSet<IInputElement> { first };
            int budget = view.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            for (int step = 0; step < budget && !fields.SetEquals(reached); step++)
            {
                if (reverse) window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
                window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
                if (reverse) window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var focused = window.FocusManager!.GetFocusedElement();
                Assert.NotNull(focused);
                if (!visited.Add(focused)) break;
                if (focused is InputElement field && fields.Contains(field))
                {
                    reached.Add(field);
                    var point = field.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.X, 0, window.ClientSize.Width - field.Bounds.Width);
                    Assert.InRange(point.Y, 0, window.ClientSize.Height - field.Bounds.Height);
                }
            }
            Assert.True(fields.SetEquals(reached), $"Tab reached {reached.Count}/{fields.Count} account fields, reverse={reverse}");
            NativeThemeTests.Save(window, $"accounts-720x640-keyboard-{(reverse ? "reverse" : "forward")}-{count}-{NativeThemeTests.Mode(light)}.png");
        }
        finally { window.Close(); }
    }
}
