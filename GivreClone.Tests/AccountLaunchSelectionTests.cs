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

public sealed class AccountLaunchSelectionTests
{
    private static (AccountsVm Vm, Row[] Rows) Accounts()
    {
        var vm = (AccountsVm)RuntimeHelpers.GetUninitializedObject(typeof(AccountsVm));
        var rows = new[] { "alpha", "beta", "gamma" }
            .Select(login => new Row(new Account { Login = login }, new ObservableCollection<Proxy>(), "")).ToArray();
        typeof(AccountsVm).GetField("<Accounts>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new ObservableCollection<Row>(rows));
        return (vm, rows);
    }

    [Fact]
    public void Launch_selection_targets_only_checked_offline_accounts_and_is_disabled_without_selection()
    {
        var (vm, rows) = Accounts();
        Assert.Equal(0, vm.LaunchSelectedCount);
        Assert.False(vm.LaunchSelectedCommand.CanExecute(null));

        rows[0].IsSelected = true;
        rows[2].IsSelected = true;
        rows[2].Running = true;   // déjà en ligne : jamais relancé
        Assert.Equal(new[] { rows[0] }, vm.LaunchSelectionTargets());
        Assert.Equal(1, vm.LaunchSelectedCount);
        Assert.True(vm.LaunchSelectedCommand.CanExecute(null));

        rows[0].IsSelected = false;
        Assert.Empty(vm.LaunchSelectionTargets());
        Assert.False(vm.LaunchSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_resume_is_offered_only_while_pending_and_stops_the_socket_retry()
    {
        var (vm, rows) = Accounts();
        var row = rows[0];
        Assert.False(row.CanCancelResume);
        row.Running = true;
        Assert.False(row.CanCancelResume);   // session vivante : rien à annuler

        var wake = new TaskCompletionSource<bool>(7, TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(Row).GetField("SocketWake", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(row, wake);
        row.SocketBackoff = true;
        Assert.True(row.CanCancelResume);

        Assert.Equal(AccountsVm.ResumeCancelOutcome.SocketBackoff, vm.CancelResumeCore(row));
        Assert.False(await wake.Task.WaitAsync(TimeSpan.FromSeconds(5)));   // réveil à FAUX = la boucle abandonne
        Assert.Equal(AccountsVm.ResumeCancelOutcome.None, vm.CancelResumeCore(row));          // une seule annulation, pas de relance réveillée
    }
}
