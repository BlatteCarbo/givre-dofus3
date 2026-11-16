using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace MZP3ReplicaWorkbench;

public sealed class ReplicaApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        static SolidColorBrush Brush(string color) => new(Color.Parse(color));
        Resources["Bg1Brush"] = Brush("#0B0F16");
        Resources["Surface1Brush"] = Brush("#141A25");
        Resources["Surface2Brush"] = Brush("#1B2230");
        Resources["TextPrimaryBrush"] = Brush("#EBF0FA");
        Resources["TextSecondaryBrush"] = Brush("#B3BDD0");
        Resources["TextMutedBrush"] = Brush("#8593AA");
        Resources["StrokeSoftBrush"] = Brush("#152B4260");
        Resources["StrokeMedBrush"] = Brush("#37465C");
        Resources["AmberBrush"] = Brush("#E2B673");
        Resources["AccentBrightBrush"] = Brush("#92B6FF");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ReplicaWorkbenchWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
