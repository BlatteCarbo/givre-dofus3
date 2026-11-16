using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class SmithMagicViewTests
{
    [AvaloniaTheory]
    [InlineData(1440, false)]
    [InlineData(760, true)]
    public void Workshop_renders_with_bounded_viewports_and_responsive_columns(int width, bool compact)
    {
        var view = new SmithMagicView();
        var window = new Window { Width = width, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Size(width, 900), window.ClientSize);
            Assert.Equal(width, view.Bounds.Width);
            Assert.Equal(900, view.Bounds.Height);
            var columns = view.FindControl<Grid>("WorkshopColumns")!;
            var wide = view.FindControl<ContentControl>("WideInventoryHost")!;
            var narrow = view.FindControl<ContentControl>("CompactInventoryHost")!;
            var tab = view.FindControl<TabItem>("CompactInventoryTab")!;
            Assert.Equal(compact, tab.IsVisible);
            Assert.Equal(!compact, wide.IsVisible);
            Assert.Equal(compact ? 152 : 176, columns.ColumnDefinitions[0].Width.Value);
            Assert.Equal(compact ? 0 : 264, columns.ColumnDefinitions[2].Width.Value);
            Assert.NotNull(compact ? narrow.Content : wide.Content);
            Assert.Null(compact ? wide.Content : narrow.Content);

            if (compact)
            {
                tab.IsSelected = true;
                Dispatcher.UIThread.RunJobs();
            }

            var inventory = Assert.IsType<Border>(compact ? narrow.Content : wide.Content);
            var origin = inventory.TranslatePoint(default, view);
            Assert.NotNull(origin);
            Assert.InRange(origin.Value.X, 0, width);
            Assert.InRange(origin.Value.X + inventory.Bounds.Width, 1, width);
            Assert.InRange(inventory.Bounds.Height, 1, 900);

            var viewports = view.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(control => control.IsEffectivelyVisible && double.IsFinite(control.MaxHeight))
                .ToArray();
            Assert.True(viewports.Length >= 2, "History and inventory must have bounded visible viewports.");
            Assert.All(viewports, control =>
            {
                Assert.InRange(control.Bounds.Height, 0, control.MaxHeight);
                Assert.InRange(control.MaxHeight, 1, 520);
                Assert.Equal(ScrollBarVisibility.Disabled, control.HorizontalScrollBarVisibility);
            });

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(width, 900), frame.PixelSize);
            using (var pixels = frame.Lock())
            {
                var bytes = new byte[pixels.RowBytes * pixels.Size.Height];
                Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>();
                for (var offset = 0; offset + 3 < bytes.Length; offset += 4)
                    colors.Add(BitConverter.ToInt32(bytes, offset));
                Assert.True(colors.Count > 8, "The actual workshop must render more than an empty surface.");
            }

            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"smith-magic-{width}.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resize_reparents_the_same_inventory_and_restores_the_object_tab()
    {
        var view = new SmithMagicView();
        var window = new Window { Width = 1440, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var wide = view.FindControl<ContentControl>("WideInventoryHost")!;
            var narrow = view.FindControl<ContentControl>("CompactInventoryHost")!;
            var tab = view.FindControl<TabItem>("CompactInventoryTab")!;
            var tabs = view.FindControl<TabControl>("WorkshopTabs")!;
            var inventory = Assert.IsType<Border>(wide.Content);
            var search = inventory.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetName(control) == "Recherche du sac");
            search.Text = "headless inventory state";

            window.Width = 760;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(wide.Content);
            Assert.Same(inventory, narrow.Content);
            tab.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsSelected);
            Assert.Equal("headless inventory state", search.Text);
            Assert.Contains(inventory, search.GetVisualAncestors());

            window.Width = 1440;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(narrow.Content);
            Assert.Same(inventory, wide.Content);
            Assert.False(tab.IsVisible);
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.Equal("headless inventory state", search.Text);

            window.Width = 760;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(inventory, narrow.Content);
            Assert.Null(wide.Content);
            Assert.Equal(0, tabs.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, false)]
    [InlineData(760, true)]
    public void Bilan_lays_out_the_cumulative_budget_outside_the_activity_column(int width, bool compact)
    {
        var view = new SmithMagicView();
        var window = new Window { Width = width, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columns = view.FindControl<Grid>("WorkshopColumns")!;
            var tabs = view.FindControl<TabControl>("WorkshopTabs")!;
            var bilan = view.FindControl<TabItem>("WorkshopBilanTab")!;
            var recap = view.FindControl<ItemsControl>("WorkshopBilanRows")!;
            var passages = view.FindControl<ItemsControl>("WorkshopRecentRows")!;
            Assert.Contains(bilan, tabs.Items.OfType<TabItem>());

            bilan.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            recap.ItemsSource = Recap;
            passages.ItemsSource = Passages;
            Dispatcher.UIThread.RunJobs();

            // Recent passages stay in the narrow activity column; the recap never does.
            var activity = columns.Children[0];
            Assert.Contains(activity, passages.GetVisualAncestors());
            Assert.DoesNotContain(activity, recap.GetVisualAncestors());
            Assert.InRange(passages.Bounds.Width, 1, compact ? 152 : 176);

            var origin = recap.TranslatePoint(default, view);
            Assert.NotNull(origin);
            Assert.InRange(recap.Bounds.Width, 400, width);
            Assert.InRange(origin.Value.X + recap.Bounds.Width, 400, width);

            var cards = recap.GetVisualDescendants().OfType<Border>()
                .Where(card => card.Classes.Contains("fmRow")).ToArray();
            Assert.Equal(Recap.Length, cards.Length);
            Assert.All(cards, card =>
            {
                var at = card.TranslatePoint(default, view);
                Assert.NotNull(at);
                Assert.InRange(at.Value.X, 0, width);
                Assert.InRange(at.Value.X + card.Bounds.Width, 400, width);
            });

            var details = recap.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Text is { } body && body.Contains('\n')).ToArray();
            Assert.Equal(Recap.Length, details.Length);
            Assert.All(details, text =>
            {
                var lines = text.Text!.Count(character => character == '\n') + 1;
                Assert.InRange(text.Bounds.Width, 360, width);
                // One wrap per logical line is the budget: the 176 px column wrapped these at ~12 characters.
                Assert.InRange(text.Bounds.Height, 1, lines * 2 * 20);
            });

            var refresh = view.FindControl<Button>("WorkshopBilanRefresh")!;
            var refreshAt = refresh.TranslatePoint(default, view);
            Assert.NotNull(refreshAt);
            Assert.InRange(refresh.Bounds.Height, 1, 900);
            Assert.InRange(refreshAt.Value.X + refresh.Bounds.Width, 1, width);

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(width, 900), frame.PixelSize);
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"smith-magic-bilan-{width}.png"));
        }
        finally
        {
            window.Close();
        }
    }

    // Shapes produced by SmithMagicViewModel.ProjectSmithObjectives and ProjectForgeHistory,
    // with realistic long values: this recap is the widest text the workshop has to lay out.
    private static readonly SmithSessionHistoryRow[] Recap =
    [
        new("Objectif metier 11 -> 200 | budget conserve",
            "Plafond 25,000,000 k | plancher portefeuille 1,500,000 k\n"
            + "Depenses totales : 18,427,500 k (sous-total historique confirme : 17,900,000 k)\n"
            + "Nouveaux debits confirmes : 527,500 k | reserves : 120,000 k\n"
            + "Disponible autorise maintenant : 6,572,500 k"),
        new("24/10 18:42:07 | atelier observe | arret demande par l'operateur",
            "412 runes | 3,180,000 k debites enregistres\n"
            + "metier 11: niv. 148 -> 162, +1,284,730 XP mesuree\n"
            + "Supports : GID 11204 / UID 884512003, GID 11204 / UID 884512044\n"
            + "Consommation : Rune Pa (GID 7508) x96, Rune Pme (GID 7510) x184, Rune Ini (GID 7534) x132\n"
            + "Valeur estimee du stock consomme : 2,940,000 k ; 7 valeur(s) inconnue(s), hors budget cash\n"
            + "Achats : GID 7508 x40 a 24,500 k/unite, GID 7510 x60 a 11,250 k/unite\n"
            + "Issues retenues (412/412): code 0: 128, code 1: 240, code 2: 44"),
        new("24/10 15:07:52 | atelier observe | en cours",
            "96 runes | 742,500 k debites enregistres\n"
            + "metier 11: niv. 141 -> 148, +318,940 XP mesuree\n"
            + "Supports : GID 11204 / UID 884511870\n"
            + "Consommation : Rune Ra Pa (GID 7509) x12, Rune Pod (GID 7520) x84\n"
            + "Valeur estimee du stock consomme : 611,000 k ; 2 valeur(s) inconnue(s), hors budget cash\n"
            + "Achats : aucun\n"
            + "Issues retenues (96/96): code 0: 41, code 1: 52, code 2: 3"),
    ];

    private static readonly SmithLogRow[] Passages =
    [
        new("18:42:07", "Rune Pa", "+1 PA", "success"),
        new("18:41:58", "Rune Pme", "sans effet", "neutral"),
        new("18:41:44", "Rune Ini", "-12 Initiative", "fail"),
    ];
}
