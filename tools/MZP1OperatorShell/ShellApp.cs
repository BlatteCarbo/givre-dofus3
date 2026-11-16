using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using GivreClone.App.Controls;

namespace MZP1OperatorShell;

/// <summary>
/// Truly runnable offline operator surface using the real MZP0 Avalonia panel.
/// No license bypass, client discovery, game process, socket or packet sender.
/// All session/ACK states intentionally remain unknown.
/// This is NOT a substitute for the unavailable full GivreClone executable.
/// </summary>
public sealed class ShellApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        static SolidColorBrush Brush(string value) => new(Color.Parse(value));
        Resources["Bg1Brush"] = Brush("#06070A");
        Resources["Surface1Brush"] = Brush("#13161D");
        Resources["Surface2Brush"] = Brush("#1A1E27");
        Resources["TextPrimaryBrush"] = Brush("#F2F5FB");
        Resources["TextSecondaryBrush"] = Brush("#A7B0C0");
        Resources["TextMutedBrush"] = Brush("#8A93A3");
        Resources["StrokeSoftBrush"] = Brush("#0AFFFFFF");
        Resources["StrokeMedBrush"] = Brush("#15FFFFFF");
        Resources["AmberBrush"] = Brush("#E0A53F");
        Resources["AccentBrightBrush"] = Brush("#8EAFF2");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "MZP1 · Diagnostics hors ligne (aucun bot connecté)",
                Width = 1140,
                Height = 780,
                MinWidth = 750,
                MinHeight = 520,
                Background = new SolidColorBrush(Color.Parse("#06070A")),
                Content = new MZP0OperatorPanel()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
