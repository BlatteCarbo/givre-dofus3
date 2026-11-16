extern alias F06App;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;
using BuyRow = F06App::GivreClone.App.ViewModels.HdvBuyRow;
using BuyLot = F06App::GivreClone.App.ViewModels.HdvBuyLot;
using Pick = F06App::GivreClone.App.ViewModels.HdvItemPick;

namespace GivreClone.Tests;

[Collection("Transport journal and timing")]
public sealed class HdvLiveUiTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Set(object instance, string name, object value)
        => instance.GetType().GetField(name, Hidden)!.SetValue(instance, value);

    private static void Call(HdvVm vm, string name)
        => typeof(HdvVm).GetMethod(name, Hidden)!.Invoke(vm, null);

    private static (HdvVm Vm, Launch Service) Model()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[] { "<Inventory>k__BackingField", "<Hdv>k__BackingField" })
        {
            var field = typeof(Launch).GetField(name, Hidden)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        Set(vm, "_launch", service);
        Set(vm, "_pid", 9u);
        Set(vm, "_loading", true);
        Set(vm, "<BuyRows>k__BackingField", new ObservableCollection<BuyRow>());
        Set(vm, "<BuySuggestions>k__BackingField", new ObservableCollection<Pick>());
        Set(vm, "<RelistSuggestions>k__BackingField", new ObservableCollection<Pick>());
        return (vm, service);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bulk_sale_skips_the_inflated_item_without_falling_back_to_smaller_lots(bool inflated)
    {
        var (vm, service) = Model();
        foreach (string name in new[] { "_serverInfo", "_avgPrices", "_hdvTaxPct", "_hdvOpen", "_hdvLotSizes", "_itemEffects", "_hdvSaleLimits", "<Bank>k__BackingField" })
        {
            var field = typeof(Launch).GetField(name, Hidden)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        service.Hdv.SetAverage(9, 13487, 621);
        service.Hdv.SetMarketPrices(9, 13487, [147, 1746, inflated ? 4999999 : 15000, 399999]);
        ((System.Collections.Concurrent.ConcurrentDictionary<uint, float>)typeof(Launch)
            .GetField("_hdvTaxPct", Hidden)!.GetValue(service)!)[9] = 2f;
        service.Inventory.Seed(9, [new(10, 13487, 100, 63), new(11, 886, 100, 63)]);
        service.Hdv.SetAverage(9, 886, 100);
        service.Hdv.SetMarketPrices(9, 886, [100, 1000, 10000, 100000]);
        var method = typeof(HdvVm).GetMethod("LotsFor", Hidden)!;
        var mine = Array.Empty<F06App::GivreClone.App.Services.BidHouseStore.Listing>();

        var result = (List<(int Uid, int Size, long Price, bool Derived)>)method.Invoke(vm, [9u, 13487L, 5, mine])!;

        if (inflated) Assert.Empty(result);
        else Assert.Equal(100, Assert.Single(result).Size);
        var next = (List<(int Uid, int Size, long Price, bool Derived)>)method.Invoke(vm, [9u, 886L, 5, mine])!;
        Assert.Equal(11, Assert.Single(next).Uid);
    }

    [Theory]
    [InlineData(44100, 621, 2, 3, 10000, false)]
    [InlineData(44101, 621, 2, 3, 10000, true)]
    [InlineData(4999999, 621, 2, 10, 0, true)]
    [InlineData(147000, 621, 2, 10, 0, false)]
    [InlineData(147001, 621, 2, 10, 0, true)]
    [InlineData(4999999, 621, 2, 0, 0, false)]
    [InlineData(500000, 10000, 2, 3, 10000, false)]
    [InlineData(500001, 10000, 2, 3, 10000, true)]
    [InlineData(5000000, 10000, 2, 10, 0, false)]
    [InlineData(100, 621, 0, 3, 10000, true)]
    [InlineData(100, 621, 0, 3, 0, false)]
    public void Sale_guard_applies_only_the_user_chosen_price_and_tax_limits(long price, long average, int tax,
        double ceiling, long maxTax, bool refused)
    {
        long[] quotes = average == 621 ? [147, 1746, 4999999, 399999] : [];

        var reason = F06App::GivreClone.App.Services.HdvSalePriceGuard.Refusal(100, price, average, quotes, tax,
            (decimal)ceiling, maxTax);

        Assert.Equal(refused, reason is not null);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void Requested_lot_quote_cannot_be_its_own_price_reference(long price, bool refused)
    {
        var reason = F06App::GivreClone.App.Services.HdvSalePriceGuard.Refusal(
            100, price, 0, [1, 0, 100, 0], 2m, 1m);

        Assert.Equal(refused, reason is not null);
    }

    [Fact]
    public void Missing_independent_reference_no_longer_blocks_a_legitimate_sale()
    {
        var reason = F06App::GivreClone.App.Services.HdvSalePriceGuard.Refusal(
            100, 100, 0, [0, 0, 100, 0], 2m);

        Assert.Null(reason);
    }

    [Theory]
    [InlineData(44100, false)]
    [InlineData(44101, true)]
    public void Other_lot_size_can_bound_price_without_server_average(long price, bool refused)
    {
        var reason = F06App::GivreClone.App.Services.HdvSalePriceGuard.Refusal(
            100, price, 0, [147, 0, 4999999, 0], 2m, 3m);

        Assert.Equal(refused, reason is not null);
    }

    [Fact]
    public void Observed_quote_sets_max_with_visible_margin_and_survives_offer_invalidation()
    {
        var (vm, service) = Model();
        var lot = new BuyLot { Qty = 1, Allowed = true, Max = 500 };
        var unknown = new BuyLot { Qty = 10, Max = 5000 };
        vm.BuyRows.Add(new BuyRow { Gid = 1519, Needed = 1, Lots = [lot, unknown] });
        vm.BuyTolerancePct = 10;
        service.Hdv.SetOffer(9, 1519, 88, [279, 0, 0]);

        Call(vm, "FillBuyMaxFromPrices");

        Assert.Equal(279, lot.Price);
        Assert.Equal(306, lot.Max);
        Assert.Equal(5000, unknown.Max);
        service.Hdv.ClearOffer(9, 1519);
        Call(vm, "RefreshBuyLive");
        Assert.Equal(279, lot.Price);
        Call(vm, "ScaleBuyMax");
        Assert.Equal(336, lot.Max);
    }

    [Fact]
    public void Late_inventory_snapshot_refreshes_existing_suggestions_and_rows_without_double_counting_purchases()
    {
        var (vm, service) = Model();
        var pick = new Pick { Gid = 1519 };
        var relist = new Pick { Gid = 1519 };
        var row = new BuyRow { Gid = 1519, Needed = 100 };
        vm.BuySuggestions.Add(pick);
        vm.RelistSuggestions.Add(relist);
        vm.BuyRows.Add(row);
        vm.BuyDeductStock = true;
        var notified = new List<string?>();
        pick.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        service.Inventory.Seed(9, [new(10, 1519, 75)]);
        Call(vm, "RefreshBuyLive");

        Assert.Equal(75, pick.Owned);
        Assert.Equal(75, relist.Owned);
        Assert.Equal(75, row.Owned);
        Assert.Equal(25, row.ToBuy);
        Assert.Contains(nameof(Pick.OwnedText), notified);
        row.Bought = 5;
        service.Inventory.SetQuantity(9, 10, 80);
        Call(vm, "RefreshBuyLive");
        Assert.Equal(80, pick.Owned);
        Assert.Equal(75, row.Owned);
        Assert.Equal(20, row.ToBuy);
        vm.BuyDeductStock = false;
        Call(vm, "RefreshBuyLive");
        Assert.Equal(0, row.Owned);
        Assert.Equal(95, row.ToBuy);
    }

    [Fact]
    public async Task Running_buy_command_remains_enabled_and_stop_cancels_the_retry_delay()
    {
        var (vm, _) = Model();
        using var stop = new CancellationTokenSource();
        Set(vm, "_buyCts", stop);
        vm.BuyRunning = true;
        var command = vm.ToggleBuyCommand;
        Task retry = Task.Delay(TimeSpan.FromSeconds(30), stop.Token);
        // Model the pending execution without starting a live purchase or a license session.
        command.GetType().GetProperty("ExecutionTask")!.SetValue(command, retry);

        Assert.True(command.IsRunning);
        Assert.True(command.CanExecute(null));
        await command.ExecuteAsync(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => retry.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stop.IsCancellationRequested);
    }

    [Theory]
    [InlineData("L.Bots.Hdv.MaxFromMarket", "{Binding Hdv.FillBuyMaxFromAvgCommand}")]
    [InlineData("L.Bots.Hdv.MaxFromSeen", "{Binding Hdv.FillBuyMaxFromPricesCommand}")]
    [InlineData("L.Bots.Hdv.MaxFromCurrent", "{Binding Hdv.ScaleBuyMaxCommand}")]
    public void Bulk_price_buttons_route_to_the_matching_action(string resource, string command)
    {
        const string relative = "GivreClone.App/Views/BotsView.axaml";
        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, relative)));
        Assert.NotNull(root);
        var document = XDocument.Load(Path.Combine(root!.FullName, relative));
        var label = Assert.Single(document.Descendants().Where(e =>
            e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{DynamicResource " + resource + "}"));
        var button = label.Ancestors().First(e => e.Name.LocalName == "Button");
        Assert.Equal(command, (string?)button.Attribute("Command"));
    }

    [Fact]
    public void Estimated_plan_shares_remaining_budget_across_rows_and_excludes_covered_rows()
    {
        var (vm, _) = Model();
        vm.BuyBudget = 100;
        vm.BuySpentSession = 10;
        vm.BuyRows.Add(new BuyRow { Needed = 2, Lots = [new BuyLot { Qty = 1, Allowed = true, Price = 30 }] });
        vm.BuyRows.Add(new BuyRow { Needed = 2, Lots = [new BuyLot { Qty = 1, Allowed = true, Price = 30 }] });
        vm.BuyRows.Add(new BuyRow { Needed = 2, Owned = 2, Lots = [new BuyLot { Qty = 1, Allowed = true, Price = 30 }] });

        Assert.Equal(2, vm.BuyRowsToBuy);
        Assert.Equal(90, vm.BuyRemainingBudget);
        Assert.Equal(90, vm.BuyEstimatedCost);
        vm.BuyRows[0].Lots[0].Max = 20;
        Assert.Equal(60, vm.BuyEstimatedCost);
        vm.BuyBudget = 0;
        Assert.Equal(0, vm.BuyEstimatedCost);
    }

    [Fact]
    public async Task Price_progress_advances_only_after_each_row_and_counts_missing_quotes()
    {
        var (vm, _) = Model();
        var first = new BuyRow { Lots = [new BuyLot { Qty = 1, Allowed = true }] };
        var second = new BuyRow { Lots = [new BuyLot { Qty = 10, Allowed = true }] };
        vm.BuyRows.Add(first);
        vm.BuyRows.Add(second);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Fetch(BuyRow row)
        {
            if (row == first)
            {
                await firstDone.Task;
                row.Lots[0].Price = 30;
            }
            else
            {
                secondStarted.SetResult();
                await secondDone.Task;
            }
        }

        var run = (Task)typeof(HdvVm).GetMethod("FetchBuyPricesAsync", Hidden)!
            .Invoke(vm, [new Func<BuyRow, Task>(Fetch)])!;
        Assert.True(vm.BuySearching);
        Assert.Equal(2, vm.BuyPricesTotal);
        Assert.Equal(0, vm.BuyPricesCompleted);
        firstDone.SetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, vm.BuyPricesCompleted);
        Assert.Equal(1, vm.BuyPricesFound);
        Assert.Contains(nameof(HdvVm.BuySummaryText), changed);
        secondDone.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.BuySearching);
        Assert.Equal(2, vm.BuyPricesCompleted);
        Assert.Equal(1, vm.BuyPricesFound);
    }

    [Fact]
    public void Numeric_focus_behavior_selects_all_text_and_can_be_detached()
    {
        var text = new Avalonia.Controls.TextBox { Text = "20 000" };
        F06App::GivreClone.App.Behaviors.NumericInputBehavior.SetSelectOnFocus(text, true);
        text.RaiseEvent(new Avalonia.Input.FocusChangedEventArgs(Avalonia.Input.InputElement.GotFocusEvent)
        {
            NavigationMethod = Avalonia.Input.NavigationMethod.Tab
        });
        Assert.Equal(0, text.SelectionStart);
        Assert.Equal(text.Text.Length, text.SelectionEnd);
        F06App::GivreClone.App.Behaviors.NumericInputBehavior.SetSelectOnFocus(text, false);
        text.SelectionEnd = 0;
        text.RaiseEvent(new Avalonia.Input.FocusChangedEventArgs(Avalonia.Input.InputElement.GotFocusEvent));
        Assert.Equal(0, text.SelectionEnd);
    }
}
