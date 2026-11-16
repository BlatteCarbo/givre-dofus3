using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

/// <summary>Rend le vrai EcoToolsPanel avec des offres et un brisage fictifs (aucun bot, aucun HDV) : vérifie que les textes du conseiller
/// sont affichés, sans chevauchement hors cadre, et sauve des PNG dans TestResults.</summary>
public sealed class EcoToolsAdvisorCaptureTests
{
    private static string[] Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToArray();

    private static void Select(EcoToolsPanel panel, string header)
    {
        var tab = panel.GetVisualDescendants().OfType<TabItem>().First(t => Equals(t.Header, header));
        tab.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Panel_shows_batch_splits_and_brisage_advice(bool light)
    {
        // Icônes chargées d'avance : EnsureLoaded du panneau ne reposte alors pas RefreshIcons (qui viderait les offres fictives).
        Stores.InteractiveIcons.EnsureLoaded();
        var panel = new EcoToolsPanel();
        var window = new Window { Content = panel, Width = 1200, Height = 900, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        string mode = NativeThemeTests.Mode(light);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var vm = panel.ViewModel;

            // --- Bonnes affaires : un lot sous la moyenne + une découpe 100 -> 10 ---
            vm.Deals.Add(new EcoDealRow("Fer", "×10", "80,000", "100", "\u221220 %", "+200", "5 min", 17, 80, 10, 800));
            var split = MarketDeals.FindBatchSplits(new[] { new MarketQuote(42, "Blé", new long[] { -1, 1000, 8000, -1 }, 0, 60_000) }, 5, 100);
            vm.BatchSplitDeals.Add(new EcoBatchSplitRow(Assert.Single(split), "1 min"));
            vm.BatchSplitStatus = "1 découpe(s) rentable(s) après taxe de 2 %. Consultatif.";
            Select(panel, "Bonnes affaires");
            var deals = Texts(panel);
            Assert.Contains(deals, t => t.Contains("Acheter lot ×100") && t.Contains("Revendre 10 × lot ×10"));
            Assert.Contains("+1 800", deals.Select(t => t.Replace('\u00A0', ' ').Replace('\u202F', ' ')));
            Save(window, $"eco-advisor-{mode}-deals.png");

            // --- Forgemagie : Focus recommandé (coût connu, ancre) ---
            Select(panel, "Forgemagie");
            vm.BrisageCost = 100;
            vm.ShowBrisage(200, new[] { new BrisageStatLine("Force", 10, 1, 10m), new BrisageStatLine("Sagesse", 10, 1, 1m) }, "Anneau fictif");
            Dispatcher.UIThread.RunJobs();
            var focus = Texts(panel);
            Assert.Contains(focus, t => t == "Focus recommandé : Force");
            Assert.Contains(focus, t => t.StartsWith("Seuil C_be 68 %")); // 100 / 147
            Assert.Contains(focus, t => t == "Équilibré");
            Assert.Contains(focus, t => t.Contains("147 k"));
            Save(window, $"eco-advisor-{mode}-focus.png");

            // --- Sans focus + Ancre ---
            vm.BrisageCost = 50;
            vm.ShowBrisage(200, new[] { new BrisageStatLine("Force", 50, 1, 10m), new BrisageStatLine("Sagesse", 50, 1, 10m) }, "Cape fictive");
            Dispatcher.UIThread.RunJobs();
            var anchor = Texts(panel);
            Assert.Contains(anchor, t => t == "Sans focus");
            Assert.Contains(anchor, t => t == "Ancre");
            Save(window, $"eco-advisor-{mode}-nofocus.png");

            // --- Prix de rune manquant : aucun verdict inventé ---
            vm.ShowBrisage(200, new[] { new BrisageStatLine("Force", 10, 1, 10m), new BrisageStatLine("Sagesse", 10, 1, null) }, "Amulette fictive");
            Dispatcher.UIThread.RunJobs();
            var missing = Texts(panel);
            Assert.Contains(missing, t => t == "Prix rune manquant : Sagesse");
            Assert.DoesNotContain(missing, t => t.StartsWith("Focus recommandé") || t == "Sans focus");
            Assert.DoesNotContain(missing, t => t.StartsWith("Seuil C_be") && t.Contains('%'));
            Save(window, $"eco-advisor-{mode}-missing.png");

            // Les cartes restent dans la fenêtre.
            var card = panel.GetVisualDescendants().OfType<Border>().First(b => b.Name == "BrisageCard");
            Assert.InRange(card.Bounds.Width, 1, 1200);
        }
        finally { window.Close(); }
    }

    private static void Save(Window window, string name) => NativeThemeTests.Save(window, name);
}
