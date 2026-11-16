using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;

namespace ForgeUiStress;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("GIVRE_CLONE_RUN_NAME", "forge-ui-stress");
        AppBuilder.Configure<PreviewApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class PreviewApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://GivreClone/"))
            { Source = new Uri("avares://GivreClone/Styles/Theme.axaml") });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://GivreClone/"))
            { Source = new Uri("avares://GivreClone/Styles/Icons.axaml") });
        Resources.MergedDictionaries.Add(new GivreClone.App.Themes.Blue());
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://GivreClone/"))
            { Source = new Uri("avares://GivreClone/Styles/Controls.axaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new SmithMagicViewModel();
            typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, 101);
            vm.Lines.Add(new SmithLineRow { Name = "Vitalité", EffectId = 125, NaturalMaximum = 150, Current = 146, Min = 150, Max = 150 });
            vm.Lines.Add(new SmithLineRow { Name = "Intelligence", EffectId = 126, NaturalMaximum = 60, Current = 60, Min = 60, Max = 60 });
            vm.Lines.Add(new SmithLineRow { Name = "Agilité", EffectId = 119, NaturalMaximum = 60, Current = 58, Min = 60, Max = 60 });
            vm.Lines.Add(new SmithLineRow { Name = "Sagesse", EffectId = 124, NaturalMaximum = 50, Current = 18, Min = 50, Max = 50 });
            vm.Status = "QA hors jeu : 10 000 résultats simulés, aucune rune consommée.";
            var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.SessionHistory.CollectionChanged += (_, _) =>
            {
                if (vm.SessionHistory.Count > 0) visible.TrySetResult();
            };
            var window = new Window
            {
                Title = "QA atelier - donnees simulees, aucun compte connecte",
                Width = 1440, Height = 900, MinWidth = 960, MinHeight = 600,
                Background = (Avalonia.Media.IBrush)Resources["Bg1Brush"]!,
                Content = new SmithMagicView { DataContext = vm }
            };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    long allocation = GC.GetTotalAllocatedBytes();
                    await Task.Run(() =>
                    {
                        var store = Stores.Launch.ForgeHistory;
                        var progress = new Dictionary<int, JobProgressSnapshot> { [64] = new(10, 900) };
                        var id = store.Begin(101, "offline-qa", 64, "QA hors jeu", progress);
                        for (int i = 0; i < 10000; i++)
                            store.Record(id, "rune", progress, itemUid: 1, itemGid: 940,
                                rune: i % 2 == 0 ? "Rune Fo" : "Rune Cha", outcome: i % 3 == 0 ? 1 : 2);
                        store.Record(id, "end", progress, reason: "charge QA terminee");
                    });
                    vm.RefreshForgeHistoryCommand.Execute(null);
                    await visible.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    if (vm.SessionHistory.Count != 1)
                        throw new InvalidOperationException("Unbounded UI history: " + vm.SessionHistory.Count);
                    Console.WriteLine($"STRESS_PASS events=10000 rows={vm.SessionHistory.Count} elapsedMs={watch.ElapsedMilliseconds} allocatedBytes={GC.GetTotalAllocatedBytes() - allocation}");
                }
                catch (Exception e)
                {
                    Console.WriteLine("STRESS_FAIL " + e);
                    desktop.Shutdown(1);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
