extern alias F06App;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Account = GivreClone.Engine.Accounts.Account;
using AccountsVm = F06App::GivreClone.App.ViewModels.AccountsViewModel;
using Row = F06App::GivreClone.App.ViewModels.AccountRow;
using Proxy = GivreClone.Engine.Proxies.ProxyEntry;

namespace GivreClone.Tests;

public sealed class AccountServerFilterTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int RunningOnly = 1, StoppedOnly = 2;

    private static (AccountsVm Vm, Row[] Rows) Accounts()
    {
        var vm = (AccountsVm)RuntimeHelpers.GetUninitializedObject(typeof(AccountsVm));
        foreach (var (field, value) in new (string, object)[]
        {
            ("<Accounts>k__BackingField", new ObservableCollection<Row>()),
            ("<VisibleAccounts>k__BackingField", new ObservableCollection<Row>()),
            ("<AccountServerChoices>k__BackingField", new ObservableCollection<string>()),
            ("_filterRows", new HashSet<Row>()),
        })
            typeof(AccountsVm).GetField(field, Hidden)!.SetValue(vm, value);
        vm.AccountSearch = "";
        vm.AccountServerFilter = AccountsVm.AllAccountServers;
        Row Make(string login, string server, bool running)
        {
            var row = new Row(new Account { Login = login }, new ObservableCollection<Proxy>(), "") { Server = server, Running = running };
            return row;
        }
        var rows = new[] { Make("alpha", "Draconiros", true), Make("beta", "draconiros ", false), Make("gamma", "Imagiro", true), Make("delta", "-", false) };
        typeof(AccountsVm).GetMethod("WireAccountFilters", Hidden)!.Invoke(vm, null);
        foreach (var row in rows) vm.Accounts.Add(row);
        return (vm, rows);
    }

    [Fact]
    public void Server_choices_merge_case_and_spacing_and_skip_unknown_servers()
    {
        var (vm, _) = Accounts();
        Assert.Equal(new[] { AccountsVm.AllAccountServers, "Draconiros", "Imagiro" }, vm.AccountServerChoices);
    }

    [Fact]
    public void Server_filter_intersects_with_state_and_search_then_resets()
    {
        var (vm, rows) = Accounts();
        vm.AccountServerFilter = "Draconiros";
        Assert.Equal(new[] { rows[0], rows[1] }, vm.VisibleAccounts);
        vm.AccountStateFilter = RunningOnly;
        Assert.Equal(new[] { rows[0] }, vm.VisibleAccounts);
        vm.AccountStateFilter = StoppedOnly;
        Assert.Equal(new[] { rows[1] }, vm.VisibleAccounts);
        vm.AccountSearch = "alp";
        Assert.Empty(vm.VisibleAccounts);
        Assert.True(vm.NoMatchingAccounts);
        vm.ClearAccountFiltersCommand.Execute(null);
        Assert.Equal(rows, vm.VisibleAccounts);
        Assert.False(vm.HasAccountFilters);
    }

    [Fact]
    public void Filter_on_a_server_that_disappears_falls_back_to_all_servers()
    {
        var (vm, rows) = Accounts();
        vm.AccountServerFilter = "Imagiro";
        Assert.Equal(new[] { rows[2] }, vm.VisibleAccounts);
        rows[2].Server = "Draconiros";
        Assert.Equal(AccountsVm.AllAccountServers, vm.AccountServerFilter);
        Assert.Equal(rows, vm.VisibleAccounts);
    }
}
