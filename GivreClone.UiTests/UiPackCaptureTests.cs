using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.App.Views.Classic;
using Xunit;

namespace GivreClone.UiTests;

/// <summary>UI-pack capture: renders the surfaces the design inventory marks MISSING (dark + light, 1200x800)
/// into the external pack folder. No session, no account, no bot command, no MainViewModel (its constructor
/// starts network/MCP/licence services), so data-bound lists show only the fixtures injected here.</summary>
public sealed class UiPackCaptureTests
{
    private static string PackDirectory => Path.Combine(AppContext.BaseDirectory, "TestResults");

    public static IEnumerable<object[]> Surfaces() =>
        new[] { "ClassicShellBody", "HdvUidOperatorWindow" }
            .SelectMany(s => new[] { new object[] { s, false }, new object[] { s, true } });

    [AvaloniaTheory]
    [MemberData(nameof(Surfaces))]
    public void Capture_missing_surface(string surface, bool light)
    {
        var window = surface switch
        {
            "ClassicShellBody" => ClassicShellWindow(),
            "HdvUidOperatorWindow" => new HdvUidOperatorWindow { DataContext = new HdvViewModel() },
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
        window.Width = 1200;
        window.Height = 800;
        window.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (window is HdvUidOperatorWindow { DataContext: HdvViewModel hdv })
            {
                // Opened clears the list; inject anonymised fixture rows afterwards.
                foreach (var row in HdvFixtureRows()) hdv.UidDiagnostics.Add(row);
                hdv.ScanStatusText = "Fixture QA : aucun scan lancé.";
            }
            if (window.Content is ClassicShellBody shell)
            {
                var pageHost = Assert.Single(shell.GetVisualDescendants().OfType<TransitioningContentControl>());
                pageHost.PageTransition = null;
                pageHost.Content = new SettingsViewModel();
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1200, window.ClientSize.Width);
            Assert.Equal(800, window.ClientSize.Height);

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory(PackDirectory);
            frame.Save(Path.Combine(PackDirectory, $"{surface}-{(light ? "light" : "dark")}.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    private static Window ClassicShellWindow()
    {
        var window = new Window { Content = new ClassicShellBody() };
        // Same skin wiring as production SkinService (palette before the window) and the real page resolver.
        foreach (var entry in new GivreClone.App.Skins.ClassicPalette())
            window.Resources[entry.Key] = entry.Value;
        window.Styles.Add(new GivreClone.App.Skins.ClassicControls());
        window.DataTemplates.Add(new ViewLocator());
        return window;
    }

    private static IEnumerable<HdvUidOperatorRow> HdvFixtureRows() => new[]
    {
        new HdvUidOperatorRow(4242, "Vente · demande", "1001", "?", "?",
            "Injection OK, aucun ACK impliqué.", "[hdv] pid 4242 : vente demande uid-sac=1001 quantite=10 inject=OK (aucun ACK impliqué)."),
        new HdvUidOperatorRow(4242, "Vente · candidat", "?", "5005", "?",
            "Candidat non corrélé.", "[hdv] pid 4242 : annonce reçue uid-lot=5005 (candidat, non corrélé)."),
        new HdvUidOperatorRow(4242, "Vente · résultat", "1001", "5005", "Confirmed",
            "Résultat moteur.", "[hdv] pid 4242 : vente uid-sac=1001 disposition=Confirmed sell_id=5005 event=ExchangeBidHouseItemAddedEvent."),
        new HdvUidOperatorRow(4242, "Retrait · demande", "?", "5005", "?",
            "Injection OK, aucun ACK impliqué.", "[hdv] pid 4242 : retrait demande sell_id=5005 quantite=10 inject=OK lien-sac-verifie=True (aucun ACK impliqué)."),
        new HdvUidOperatorRow(4242, "Retrait · résultat", "?", "5005", "NeedsReview",
            "À vérifier.", "[hdv] pid 4242 : retrait reponse event=timeout sell_id-demande=5005 disposition=NeedsReview lien-sac-verifie=False."),
    };
}
