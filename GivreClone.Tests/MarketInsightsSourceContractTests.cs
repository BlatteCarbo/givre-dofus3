using System;
using System.IO;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Read-only source contracts. The raw wire payload decoder is independently
/// exercised by LotMReaderTests. These do not assert live 3.7.12.12 reception.
/// </summary>
public sealed class MarketInsightsSourceContractTests
{
    private static string Source(string relative)
    {
        var at = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, relative)));
        Assert.NotNull(at);
        return File.ReadAllText(Path.Combine(at!.FullName, relative));
    }

    [Fact]
    public void Market_panel_reuses_selected_bot_hdv_and_manual_read()
    {
        var shell = Source("GivreClone.App/Views/MainWindow.axaml");
        var panel = Source("GivreClone.App/Views/MarketInsightsPanel.axaml");
        Assert.Contains("MarketInsightsPanel DataContext=\"{Binding BotsVM}\"", shell);
        Assert.Contains("{Binding Hdv.ReadPriceHistoryCommand}", panel);
        Assert.Contains("{Binding Hdv.PriceHistorySelection", panel);
        Assert.Contains("{Binding Hdv.PriceHistoryLinePoints}", panel);
        Assert.DoesNotContain("BuyKamas", panel);
        Assert.DoesNotContain("BuyOgrines", panel);
    }

    [Fact]
    public void Chart_fails_closed_without_received_timestamps_or_sufficient_points()
    {
        var viewModel = Source("GivreClone.App/ViewModels/HdvViewModel.PriceHistory.cs");
        Assert.Contains("DateTimeOffset.TryParse(point.Date", viewModel);
        Assert.Contains("if (window.Length < 2)", viewModel);
        Assert.Contains("PriceHistoryHasChart = false;", viewModel);
        Assert.Contains("source.Points", viewModel);
        Assert.Contains("value is not (1 or 7 or 30)", viewModel);
    }

    [Fact]
    public void Existing_price_history_requires_manual_user_action_and_scope()
    {
        var viewModel = Source("GivreClone.App/ViewModels/HdvViewModel.PriceHistory.cs");
        Assert.Contains("[RelayCommand]", viewModel);
        Assert.Contains("!CanReadPriceHistory", viewModel);
        Assert.Contains("ListingsView.Contains(row)", viewModel);
        Assert.Contains("_launch.PriceHistoryAsync(pid, gid, request.Token)", viewModel);
        Assert.Contains("PriceHistoryCharacterScope", viewModel);
    }

    [Fact]
    public void Direct_market_rate_and_gifts_remain_unknown_not_mocked()
    {
        var panel = Source("GivreClone.App/Views/MarketInsightsPanel.axaml");
        Assert.Contains("ne sont pas encore raccordés", panel);
        Assert.Contains("Aucun bouton d'achat", panel);
        Assert.Contains("ne porte pas le GID", panel);
    }
}
