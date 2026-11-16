using System.Collections.ObjectModel;
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

public sealed class SocketPickerUxTests
{
    [AvaloniaTheory]
    [InlineData(1460, 920)]
    [InlineData(960, 600)]
    public void Delete_opens_a_named_confirmation_and_play_keeps_the_window_normal(int width, int height)
    {
        Loc.Apply("fr");
        var vm = (AccountsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AccountsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(AccountsViewModel).GetFields(hidden).Where(f =>
            f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        var pick = new TaskCompletionSource<(int ServerId, string? Character)?>();
        typeof(AccountsViewModel).GetField("_serverPickTcs", hidden)!.SetValue(vm, pick);
        vm.ServerPickVisible = true;
        vm.ServerPickStep = 1;
        vm.ServerPickTitle = "Choisir un personnage";
        vm.ServerPickLogin = "Compte de test hors ligne";
        vm.CreateAllowed = true;
        var character = new SocketCharCard { Name = "Testeur", ServerId = 42, LevelText = "Niv. 12", ClassName = "Crâ" };
        vm.ServerPickChars.Add(character);
        var view = new SocketServerPickOverlay { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var delete = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Command == vm.AskDeleteSocketCharacterCommand);
            Assert.Contains(character.Name, Assert.IsType<string>(ToolTip.GetTip(delete)));
            Save(window, $"socket-picker-{width}.png");
            Click(window, delete);
            Assert.True(vm.ServerPickOnDelete);
            Assert.False(pick.Task.IsCompleted);
            var name = view.GetVisualDescendants().OfType<TextBox>()
                .Single(t => Equals(t.Watermark, character.Name));
            Assert.True(name.IsEffectivelyVisible);
            AssertInside(name, window);
            Save(window, $"socket-delete-{width}.png");

            vm.ServerPickStep = 1;
            Dispatcher.UIThread.RunJobs();
            var play = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Command == vm.PickSocketCharacterCommand);
            Click(window, play);
            Assert.True(pick.Task.IsCompletedSuccessfully);
            Assert.Equal((42, "Testeur"), pick.Task.Result);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        AssertInside(control, window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertInside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.InRange(point.X, 0, window.ClientSize.Width - control.Bounds.Width);
        Assert.InRange(point.Y, 0, window.ClientSize.Height - control.Bounds.Height);
    }

    private static void Save(Window window, string filename)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, filename));
    }
}
