using Avalonia.Controls;

namespace GivreClone.App.Views;

/// <summary>
/// Read-only, source-backed market observations for the selected bot's HDV.
/// It deliberately reuses the existing HdvViewModel and does not duplicate
/// state, initiate background queries, buy offers, or scrape the game UI.
/// </summary>
public partial class MarketInsightsPanel : UserControl
{
    public MarketInsightsPanel() => InitializeComponent();
}
