using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
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

public sealed class TreasureReadinessTests
{
    [AvaloniaTheory]
    [InlineData(1000, 1000)]
    [InlineData(720, 640)]
    public async Task Mounted_hunt_panel_preserves_readiness_steps_and_restart_controls(int width, int height)
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (string name in new[] { "_treasure", "_treasureFinished", "_lastMap" })
        {
            var field = typeof(LaunchService).GetField(name, hidden)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        var maps = (IDictionary)typeof(LaunchService).GetField("_lastMap", hidden)!.GetValue(service)!;
        maps[7u] = 128452097L;
        var vm = new TreasureViewModel(service);
        int launches = 0;
        vm.StartAutoHuntScript = (_, _, _) => { launches++; return false; };
        var view = new TreasureView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var start = view.GetVisualDescendants().OfType<Button>().Single(button =>
                ReferenceEquals(button.Command, vm.StartAutoHuntCommand));
            var prerequisite = view.GetVisualDescendants().OfType<TextBlock>().Single(text =>
                text.Text == vm.AutoHuntPrerequisites);
            Assert.False(start.IsEffectivelyEnabled);
            Assert.Equal(AutoHuntReadiness.NoCharacter, vm.AutoHuntReadyState);

            vm.AttachPid(7);
            Dispatcher.UIThread.RunJobs();
            Assert.True(start.IsEffectivelyEnabled);
            Assert.Equal(AutoHuntReadiness.Ready, vm.AutoHuntReadyState);
            Assert.Equal(vm.AutoHuntPrerequisites, prerequisite.Text);

            await vm.StartAutoHuntCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, launches);
            Assert.False(vm.CanStopHunt);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.IsEffectivelyVisible && text.Text == vm.Status);

            var hunts = (IDictionary)typeof(LaunchService).GetField("_treasure", hidden)!.GetValue(service)!;
            hunts[7u] = new LaunchService.TreasureHunt(1, 100, 3, 0, 1, 3,
                Array.Empty<LaunchService.TreasureFlag>(),
                new[] { new LaunchService.TreasureStep("poi", 0, 0, 42, 0) }, 1);
            vm.Refresh();
            Dispatcher.UIThread.RunJobs();
            var current = view.GetVisualDescendants().OfType<TextBlock>().Single(text =>
                text.Text == vm.CurrentStepText);
            string firstStep = current.Text!;
            vm.SelectedIndex = 0;

            hunts[7u] = new LaunchService.TreasureHunt(1, 100, 3, 0, 1, 3,
                Array.Empty<LaunchService.TreasureFlag>(),
                new[] { new LaunchService.TreasureStep("poi", 0, 0, 42, 0),
                    new LaunchService.TreasureStep("poi", 4, 0, 99, 0) }, 2);
            vm.Refresh();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, vm.SelectedIndex);
            Assert.NotEqual(firstStep, current.Text);
            Assert.Equal(vm.CurrentStepText, current.Text);
            Assert.True(current.IsEffectivelyVisible);

            int travels = 0;
            vm.Travel = (_, _, _) => { travels++; return Task.FromResult(true); };
            vm.ResolveStepMap = (_, _, _) => Task.FromResult<(long?, long, string?)>(
                (null, 128452097L, "DofusDB : réponse de recherche indisponible"));
            await vm.GoToStepCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, travels);
            Assert.Equal("DofusDB : réponse de recherche indisponible", vm.Status);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.IsEffectivelyVisible && text.Text == vm.Status);

            bool running = false;
            vm.ScriptRunning = _ => running;
            vm.StartAutoHuntScript = (_, _, _) => { running = true; return true; };
            vm.StopHuntScript = (_, _) => { running = false; return true; };
            await vm.StartAutoHuntCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var stop = view.GetVisualDescendants().OfType<Button>().First(button =>
                ReferenceEquals(button.Command, vm.StopHuntCommand));
            Assert.True(stop.IsEffectivelyEnabled);
            Assert.False(start.IsEffectivelyEnabled);

            vm.StopHuntCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(start.IsEffectivelyEnabled);
            Assert.False(stop.IsEffectivelyEnabled);
            Assert.Equal(2, vm.Steps.Count);
            Assert.Equal(vm.CurrentStepText, current.Text);

            Assert.True(start.Focus());
            var focused = window.FocusManager!.GetFocusedElement();
            Assert.Same(start, focused);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.FocusManager.GetFocusedElement());
            Assert.NotSame(focused, window.FocusManager.GetFocusedElement());
            Assert.NotSame(stop, window.FocusManager.GetFocusedElement());
            NativeThemeTests.Save(window, $"treasure-{width}x{height}-keyboard.png");
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(start, window.FocusManager.GetFocusedElement());

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.True(running);
            Assert.True(stop.IsEffectivelyEnabled);
            Assert.False(start.IsEffectivelyEnabled);
            NativeThemeTests.Save(window, $"treasure-{width}x{height}-running.png");
            foreach (var control in new Control[] { start, stop, current, prerequisite })
            {
                var position = control.TranslatePoint(default, window);
                Assert.NotNull(position);
                Assert.InRange(position.Value.X, 0, window.ClientSize.Width - control.Bounds.Width);
                Assert.InRange(position.Value.Y, 0, window.ClientSize.Height - control.Bounds.Height);
            }
            Assert.True(stop.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.False(running);
            Assert.False(stop.IsEffectivelyEnabled);
            Assert.True(start.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }
}
