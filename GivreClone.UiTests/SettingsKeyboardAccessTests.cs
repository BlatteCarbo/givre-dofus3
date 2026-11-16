using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class SettingsKeyboardAccessTests
{
    [AvaloniaFact]
    public void Focused_sound_volume_exposes_its_displayed_label_without_changing_value()
    {
        var view = new SettingsView();
        view.Resources["L.Settings.UiSoundsVolume"] = "sound-volume-accessible-name";
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var slider = view.FindControl<Slider>("SfxVolumeSlider")!;
            var grid = Assert.IsType<Grid>(slider.Parent);
            var label = grid.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First();
            var before = slider.Value;
            Assert.True(slider.IsEffectivelyVisible);
            Assert.True(slider.IsEffectivelyEnabled);
            Assert.True(slider.Focus());
            Assert.Same(slider, window.FocusManager!.GetFocusedElement());
            Assert.False(string.IsNullOrWhiteSpace(label.Text));
            Assert.Equal(label.Text, Avalonia.Automation.AutomationProperties.GetName(slider));
            Assert.Equal(before, slider.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("SkinToggle")]
    [InlineData("RemoteToggle")]
    [InlineData("RemoteLanToggle")]
    [InlineData("SfxToggle")]
    [InlineData("AmbientToggle")]
    [InlineData("ParticlesToggle")]
    [InlineData("SeasonalToggle")]
    [InlineData("ModsToggle")]
    [InlineData("PacketDumpToggle")]
    [InlineData("PacketDumpAllToggle")]
    [InlineData("ArchDmToggle")]
    [InlineData("RichPresenceToggle")]
    public void Focused_settings_toggle_exposes_its_displayed_label(string controlName)
    {
        var view = new SettingsView();
        // Inspect developer-only cards without enabling capture or any setting.
        foreach (string key in new[] { "SkinSnow", "UiSounds", "AmbientMusic", "BgParticles", "ModsInject", "PacketDump", "PacketDumpAll", "ArchDm", "RichPresence" })
            view.Resources[$"L.Settings.{key}"] = $"accessible-{key}";
        view.FindControl<Border>("PacketDumpCard")!.IsVisible = true;
        view.FindControl<Border>("PacketDumpAllCard")!.IsVisible = true;
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = view.FindControl<ToggleSwitch>(controlName)!;
            var grid = Assert.IsType<Grid>(toggle.Parent);
            var label = grid.Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First();
            var before = toggle.IsChecked;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsEffectivelyEnabled);
            Assert.True(toggle.Focus());
            Assert.Same(toggle, window.FocusManager!.GetFocusedElement());
            Assert.False(string.IsNullOrWhiteSpace(label.Text));
            Assert.Equal(label.Text, Avalonia.Automation.AutomationProperties.GetName(toggle));
            Assert.Equal(before, toggle.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Focused_stream_toggle_exposes_its_localized_name_without_toggling()
    {
        var view = new SettingsView();
        view.Resources["L.Settings.StreamMode"] = "stream-mode-accessible-name";
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = view.FindControl<ToggleSwitch>("StreamToggle")!;
            var before = toggle.IsChecked;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsEffectivelyEnabled);
            Assert.True(toggle.Focus());
            Assert.Same(toggle, window.FocusManager!.GetFocusedElement());
            Assert.Equal("stream-mode-accessible-name", Avalonia.Automation.AutomationProperties.GetName(toggle));
            Assert.Equal(before, toggle.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("PauseTurnsBox", "PauseTurnsLabel")]
    [InlineData("AbandonTurnsBox", "AbandonTurnsLabel")]
    [InlineData("FightPauseMinBox", "FightPauseMinLabel")]
    [InlineData("FightPauseMaxBox", "FightPauseMaxLabel")]
    public void Focused_combat_safety_input_exposes_its_displayed_label(string controlName, string labelName)
    {
        var view = new SettingsView();
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var numeric = view.FindControl<NumericUpDown>(controlName)!;
            var input = numeric.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(input.IsEffectivelyVisible);
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(input.Focus());
            Assert.Same(input, window.FocusManager!.GetFocusedElement());
            var label = view.FindControl<TextBlock>(labelName)!;
            Assert.False(string.IsNullOrWhiteSpace(label.Text));
            Assert.Equal(label.Text, Avalonia.Automation.AutomationProperties.GetName(input));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_reaches_settings_buttons_without_activating_them(bool reverse)
    {
        var view = new SettingsView();
        view.FindControl<TextBox>("RemoteTokenBox")!.Text = "";
        view.FindControl<TextBlock>("RemoteAddresses")!.Text = "";
        var window = new Window { Content = view, Width = 720, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(button =>
                button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.Focusable
                && KeyboardNavigation.GetIsTabStop(button) && button.Bounds.Width > 0).ToHashSet();
            Assert.NotEmpty(buttons);
            var first = reverse ? buttons.Last() : buttons.First();
            Assert.True(first.Focus());
            var reached = new HashSet<Button> { first };
            var visited = new HashSet<IInputElement> { first };
            var modifiers = reverse ? RawInputModifiers.Shift : RawInputModifiers.None;
            int budget = view.GetVisualDescendants().OfType<InputElement>().Count() + 1;
            for (int step = 0; step < budget && !buttons.SetEquals(reached); step++)
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
                if (focused is Button button && buttons.Contains(button))
                {
                    reached.Add(button);
                    var point = button.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.X, 0, window.ClientSize.Width - button.Bounds.Width);
                    Assert.InRange(point.Y, 0, window.ClientSize.Height - button.Bounds.Height);
                }
            }
            Assert.True(buttons.SetEquals(reached),
                $"Tab reached {reached.Count}/{buttons.Count} settings buttons, reverse={reverse}; missing: "
                + string.Join(" | ", buttons.Except(reached).Select(button =>
                    $"{button.GetType().Name}#{button.Name}: tabStop={KeyboardNavigation.GetIsTabStop(button)}, owner={button.TemplatedParent?.GetType().Name}")));
            NativeThemeTests.Save(window, $"settings-720x640-keyboard-{(reverse ? "reverse" : "forward")}.png");
        }
        finally { window.Close(); }
    }
}
