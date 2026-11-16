using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class NativeThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_equipment_card_keeps_its_details_in_the_available_width(bool light)
    {
        // Load the shipped XAML, not a test reconstruction. No bot/transport model is attached.
        var owner = new BotsView();
        var image = owner.FindControl<Image>("SelfLookImage")!;
        var card = Assert.IsType<Border>(image.Parent!.Parent);
        Assert.IsType<Grid>(card.Parent).Children.Remove(card);
        var expander = Assert.IsType<StackPanel>(card.Child).Children.OfType<Expander>().Single();
        var window = new Window
        {
            Width = 250, Height = 800, Content = card, Background = owner.FindResource("Bg1Brush") as IBrush,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Background = window.FindResource("Bg1Brush") as IBrush;
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "ExpanderHeader");
            Assert.Equal(BrushColor(window.FindResource("Surface2Brush")), BrushColor(header.Background));
            Save(window, $"equipment-{Mode(light)}-collapsed.png");
            var headerWidth = expander.Bounds.Width;
            expander.IsExpanded = true;
            owner.FindControl<TextBlock>("EquipStats")!.Text =
                "16 objets équipés\n+345 Vitalité\n+125 Intelligence\n+72 Sagesse\n+2 Portée\nBonus de panoplie inclus";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(headerWidth, expander.Bounds.Width);
            Assert.InRange(expander.Bounds.Width, 1, card.Bounds.Width);
            var scroll = Assert.IsType<ScrollViewer>(expander.Content);
            Assert.Equal(192, scroll.MaxHeight);
            Assert.InRange(scroll.Bounds.Height, 1, scroll.MaxHeight);
            Save(window, $"equipment-{Mode(light)}-expanded.png");
        }
        finally { window.Close(); }
    }

    internal static string Mode(bool light) => light ? "light-host" : "dark";

    private static Color BrushColor(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;

    internal static void Save(Window window, string filename)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, filename), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
