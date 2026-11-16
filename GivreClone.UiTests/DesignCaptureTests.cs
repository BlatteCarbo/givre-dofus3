using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

/// <summary>Design-review capture (opt-in with GIVRE_DESIGN_CAPTURE=1): renders every overlay/panel that the
/// route matrix does not cover, and opens each modal card one at a time by forcing its `modalOpen` class.
/// No session, no account, no bot command. PNGs land in bin/.../TestResults/design-*.png.</summary>
public sealed class DesignCaptureTests
{
    public static IEnumerable<object[]> Views() => new[]
    {
        "AccountOverlays", "CombatOverlays", "CombatSimOverlay", "EcoToolsPanel", "HdvSellOverlay", "LevelCapOverlay",
        "LuaEditorOverlays", "NoCodeWizardOverlay", "PlannedOfflineCard", "PlanningDisconnectOverlay", "PlanningOverlays",
        "ProxysOverlays", "RoadRecorderOverlays", "SmithMagicView", "SocketServerPickOverlay", "TreasureView", "VisualModsPanel",
    }.SelectMany(v => new[] { new object[] { v, false }, new object[] { v, true } });

    private static UserControl Create(string name) => name switch
    {
        "AccountOverlays" => new AccountOverlays(),
        "CombatOverlays" => new CombatOverlays(),
        "CombatSimOverlay" => new CombatSimOverlay(),
        "EcoToolsPanel" => new EcoToolsPanel(),
        "HdvSellOverlay" => new HdvSellOverlay(),
        "LevelCapOverlay" => new LevelCapOverlay(),
        "LuaEditorOverlays" => new LuaEditorOverlays(),
        "NoCodeWizardOverlay" => new NoCodeWizardOverlay(),
        "PlannedOfflineCard" => new PlannedOfflineCard(),
        "PlanningDisconnectOverlay" => new PlanningDisconnectOverlay(),
        "PlanningOverlays" => new PlanningOverlays(),
        "ProxysOverlays" => new ProxysOverlays(),
        "RoadRecorderOverlays" => new RoadRecorderOverlays(),
        "SmithMagicView" => new SmithMagicView(),
        "SocketServerPickOverlay" => new SocketServerPickOverlay(),
        "TreasureView" => new TreasureView(),
        "VisualModsPanel" => new VisualModsPanel(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public void Capture_view_and_each_modal(string name, bool light)
    {
        if (Environment.GetEnvironmentVariable("GIVRE_DESIGN_CAPTURE") != "1") return;
        var view = Create(name);
        var window = new Window { Content = view, Width = 1200, Height = 800,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        string prefix = $"design-{name}-{NativeThemeTests.Mode(light)}";
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            NativeThemeTests.Save(window, $"{prefix}-base.png");
            // Modal containers are the grids whose direct child is a `modalScrim` border.
            var modals = view.GetVisualDescendants().OfType<Grid>()
                .Where(g => g.Children.OfType<Border>().Any(b => b.Classes.Contains("modalScrim"))).ToArray();
            for (int i = 0; i < modals.Length; i++)
            {
                foreach (var other in modals) { other.Classes.Set("modalOpen", false); other.IsHitTestVisible = false; }
                modals[i].Classes.Set("modalOpen", true);
                modals[i].IsHitTestVisible = true; modals[i].IsEnabled = true;
                Dispatcher.UIThread.RunJobs();
                NativeThemeTests.Save(window, $"{prefix}-modal{i + 1:00}.png");
            }
        }
        finally { window.Close(); }
    }
}
