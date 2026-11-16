using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class BotsViewLifecycleTests
{
    [AvaloniaFact]
    public void Detached_bot_view_stops_its_fight_refresh_and_restarts_on_reattach()
    {
        var view = new BotsView();
        var timer = (DispatcherTimer)typeof(BotsView)
            .GetField("_fightRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(timer.IsEnabled);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(timer.IsEnabled);

            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            Assert.True(timer.IsEnabled);
        }
        finally { window.Close(); }
        Assert.False(timer.IsEnabled);
    }
}
