using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.App.Views.Classic;
using Xunit;

namespace GivreClone.UiTests;

public sealed class ClassicPageResolutionTests
{
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Settings_page_resolves_with_or_without_keyed_row_templates(bool keepRowTemplates, bool classicStyles)
    {
        var shell = new ClassicShellBody();
        Assert.Null(shell.DataContext);
        Assert.IsAssignableFrom<IDataTemplate>(shell.Resources["BotRowTemplate"]);
        Assert.IsAssignableFrom<IDataTemplate>(shell.Resources["OfflineRowTemplate"]);
        if (!keepRowTemplates)
        {
            shell.Resources.Remove("BotRowTemplate");
            shell.Resources.Remove("OfflineRowTemplate");
        }

        var window = new Window { Content = shell, Width = 1200, Height = 720 };
        if (classicStyles)
        {
            foreach (var entry in new GivreClone.App.Skins.ClassicPalette())
                window.Resources[entry.Key] = entry.Value;
            window.Styles.Add(new GivreClone.App.Skins.ClassicControls());
        }
        // TestApp deliberately omits production App startup and its ViewLocator.
        // Restore only the real page resolver, scoped to this window.
        window.DataTemplates.Add(new ViewLocator());
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var pageHost = Assert.Single(shell.GetVisualDescendants()
                .OfType<TransitioningContentControl>());
            // Exercise the shipped host without constructing MainViewModel or
            // waiting for the production cross-fade animation.
            pageHost.PageTransition = null;
            var page = new SettingsViewModel();
            pageHost.Content = page;
            Dispatcher.UIThread.RunJobs();

            var presenter = Assert.Single(pageHost.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Where(p => ReferenceEquals(p.Content, page)));
            var view = Assert.IsType<SettingsView>(presenter.Child);
            Assert.Same(page, view.DataContext);
            Assert.True(view.IsEffectivelyVisible);
            Assert.True(view.Bounds.Width > 0);
            Assert.True(view.Bounds.Height > 0);
            Assert.Null(shell.DataContext);
        }
        finally { window.Close(); }
    }
}
