using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using GivreClone.App.ViewModels;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using Xunit;

namespace GivreClone.UiTests;

public sealed class AccountsFilterTests
{
    [AvaloniaTheory]
    [InlineData(" LOGIN@EXAMPLE.COM ")]
    [InlineData("banquier")]
    [InlineData("actuel")]
    [InlineData("ancien")]
    [InlineData("draconiros")]
    public void Search_matches_real_identity_character_and_server_fields(string query)
    {
        var vm = CreateViewModel();
        var row = Row("login@example.com");
        row.Model.DisplayName = "Banquier";
        row.CharacterName = "Actuel";
        row.LastCharacter = "Ancien";
        row.Server = "Draconiros";
        vm.Accounts.Add(row);
        vm.Accounts.Add(Row("other"));

        vm.AccountSearch = query;

        Assert.Same(row, Assert.Single(vm.VisibleAccounts));
        Assert.Equal(2, vm.Accounts.Count);
    }

    [AvaloniaFact]
    public void State_and_text_filters_combine_without_changing_source_or_selection()
    {
        var vm = CreateViewModel();
        var running = Row("match-running");
        running.Running = true;
        running.IsSelected = true;
        var stopped = Row("match-stopped");
        stopped.IsSelected = true;
        var other = Row("other");
        other.Running = true;
        vm.Accounts.Add(running);
        vm.Accounts.Add(stopped);
        vm.Accounts.Add(other);

        vm.AccountSearch = "match";
        vm.AccountStateFilter = 1;
        Assert.Same(running, Assert.Single(vm.VisibleAccounts));
        vm.AccountStateFilter = 2;
        Assert.Same(stopped, Assert.Single(vm.VisibleAccounts));
        Assert.Equal(new[] { running, stopped }, vm.SelectedRows);
        Assert.Equal(new[] { running, stopped, other }, vm.Accounts);
        Assert.True(running.Running);
        Assert.False(stopped.Running);

        vm.ClearAccountFiltersCommand.Execute(null);
        Assert.False(vm.HasAccountFilters);
        Assert.Equal(vm.Accounts, vm.VisibleAccounts);
        Assert.Equal(new[] { running, stopped }, vm.SelectedRows);
    }

    [AvaloniaFact]
    public void Live_updates_reconcile_existing_rows_and_publish_result_state()
    {
        var vm = CreateViewModel();
        var row = Row("original");
        vm.Accounts.Add(row);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.AccountSearch = "target";
        Assert.False(vm.HasVisibleAccounts);
        Assert.True(vm.NoMatchingAccounts);
        Assert.Contains(nameof(AccountsViewModel.AccountResultCount), changed);

        changed.Clear();
        row.Model.DisplayName = "Target";
        row.RaiseIdentityChanged();
        Assert.Same(row, Assert.Single(vm.VisibleAccounts));
        Assert.Contains(nameof(AccountsViewModel.AccountResultCount), changed);
        vm.AccountStateFilter = 1;
        Assert.Empty(vm.VisibleAccounts);
        row.Running = true;
        Assert.Same(row, Assert.Single(vm.VisibleAccounts));
        row.Model.DisplayName = null;
        row.RaiseIdentityChanged();
        Assert.Empty(vm.VisibleAccounts);
        row.Server = "Target";
        Assert.Same(row, Assert.Single(vm.VisibleAccounts));
        row.Server = "-";
        row.Character = "Target (12)";
        Assert.Same(row, Assert.Single(vm.VisibleAccounts));
        row.Character = "-";
        row.LastCharacter = "Target";
        Assert.Same(row, Assert.Single(vm.VisibleAccounts));

        vm.Accounts.Clear();
        Assert.False(vm.HasAccounts);
        Assert.False(vm.NoMatchingAccounts);
        Assert.Empty(vm.VisibleAccounts);
        int notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        row.Running = false;
        row.LastCharacter = "Detached";
        Assert.Equal(0, notifications);
    }

    [AvaloniaFact]
    public void Add_move_replace_and_remove_preserve_source_order_and_detach_old_rows()
    {
        var vm = CreateViewModel();
        var first = Row("first");
        var second = Row("second");
        vm.Accounts.Add(first);
        vm.Accounts.Insert(0, second);
        Assert.Equal(new[] { second, first }, vm.VisibleAccounts);
        vm.Accounts.Move(0, 1);
        Assert.Equal(new[] { first, second }, vm.VisibleAccounts);
        var replacement = Row("replacement");
        vm.Accounts[0] = replacement;
        Assert.Equal(new[] { replacement, second }, vm.VisibleAccounts);
        vm.Accounts.Remove(second);
        Assert.Same(replacement, Assert.Single(vm.VisibleAccounts));
        int notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        first.Server = "Detached";
        second.Running = true;
        Assert.Equal(0, notifications);
    }

    [AvaloniaTheory]
    [InlineData("eleve")]
    [InlineData("ÉLÈVE")]
    [InlineData("e\u0301le\u0300ve")]
    public void Search_ignores_case_and_composed_or_decomposed_accents(string query)
    {
        var vm = CreateViewModel();
        var fields = new[]
        {
            Row("Élève"), Row("label"), Row("character"),
            Row("current"), Row("last"), Row("server")
        };
        fields[1].Model.DisplayName = "Élève";
        fields[2].Character = "Élève";
        fields[3].CharacterName = "Élève";
        fields[4].LastCharacter = "Élève";
        fields[5].Server = "Élève";
        foreach (var row in fields) vm.Accounts.Add(row);
        vm.Accounts.Add(Row("unrelated"));

        vm.AccountSearch = query;

        Assert.Equal(fields, vm.VisibleAccounts);
        Assert.Equal(7, vm.Accounts.Count);
    }

    [AvaloniaFact]
    public void Server_choices_track_real_fields_and_preserve_then_clear_selection()
    {
        var vm = CreateViewModel();
        var first = Row("first");
        first.Server = " Draconiros ";
        first.IsSelected = true;
        var second = Row("second");
        second.Server = "draconiros";
        var other = Row("other");
        other.Server = "Imagiro";
        vm.Accounts.Add(first);
        vm.Accounts.Add(second);
        vm.Accounts.Add(other);
        vm.Accounts.Add(Row("unknown"));
        Assert.Equal(new[] { AccountsViewModel.AllAccountServers, "Draconiros", "Imagiro" }, vm.AccountServerChoices);

        vm.AccountServerFilter = "Draconiros";
        vm.AccountSearch = "first";
        vm.AccountStateFilter = 2;
        Assert.Same(first, Assert.Single(vm.VisibleAccounts));
        vm.AccountSearch = "";
        vm.AccountStateFilter = 0;
        first.Server = "-";
        Assert.Equal("Draconiros", vm.AccountServerFilter);
        Assert.Same(second, Assert.Single(vm.VisibleAccounts));
        other.Server = "Tal Kasha";
        Assert.Equal("Draconiros", vm.AccountServerFilter);
        vm.Accounts.Remove(second);
        Assert.Equal(AccountsViewModel.AllAccountServers, vm.AccountServerFilter);
        Assert.Equal(new[] { AccountsViewModel.AllAccountServers, "Tal Kasha" }, vm.AccountServerChoices);
        Assert.Equal(vm.Accounts, vm.VisibleAccounts);
        Assert.True(first.IsSelected);
        vm.AccountServerFilter = "Tal Kasha";
        other.Server = " ";
        Assert.Equal(AccountsViewModel.AllAccountServers, vm.AccountServerFilter);
        Assert.Equal(new[] { AccountsViewModel.AllAccountServers }, vm.AccountServerChoices);
        other.Server = "Imagiro";
        vm.AccountServerFilter = "Imagiro";
        vm.AccountSearch = "other";
        vm.AccountStateFilter = 2;
        vm.AccountSort = 3;
        vm.ClearAccountFiltersCommand.Execute(null);
        Assert.Equal(AccountsViewModel.AllAccountServers, vm.AccountServerFilter);
        Assert.Equal(0, vm.AccountSort);
        Assert.False(vm.HasAccountFilters);
        Assert.Equal(vm.Accounts, vm.VisibleAccounts);
        vm.Accounts.Clear();
        Assert.Equal(new[] { AccountsViewModel.AllAccountServers }, vm.AccountServerChoices);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Sorts_are_stable_reactive_projections_and_reset_restores_source(int sort)
    {
        var vm = CreateViewModel();
        var zulu = Row("zulu");
        zulu.CharacterName = "Zulu";
        zulu.Server = "Zulu";
        var first = Row("first");
        first.Model.DisplayName = "Alpha";
        first.CharacterName = "Alpha";
        first.Server = "Alpha";
        first.Running = true;
        first.IsSelected = true;
        var second = Row("second");
        second.Model.DisplayName = "alpha";
        second.CharacterName = "alpha";
        second.Server = "alpha";
        second.Running = true;
        vm.Accounts.Add(zulu);
        vm.Accounts.Add(first);
        vm.Accounts.Add(second);

        vm.AccountSort = sort;
        Assert.Equal(new[] { first, second, zulu }, vm.VisibleAccounts);
        Assert.Equal(new[] { zulu, first, second }, vm.Accounts);
        vm.Accounts.Move(2, 1);
        Assert.Equal(new[] { second, first, zulu }, vm.VisibleAccounts);
        switch (sort)
        {
            case 1: zulu.Model.DisplayName = "Aardvark"; zulu.RaiseIdentityChanged(); break;
            case 2: zulu.CharacterName = "Aardvark"; break;
            case 3: zulu.Server = "Aardvark"; break;
            case 4: zulu.Running = true; break;
        }
        Assert.Equal(new[] { zulu, second, first }, vm.VisibleAccounts);
        Assert.Equal(new[] { first }, vm.SelectedRows);
        Assert.True(vm.HasAccountFilters);
        vm.ClearAccountFiltersCommand.Execute(null);
        Assert.Equal(0, vm.AccountSort);
        Assert.False(vm.HasAccountFilters);
        Assert.Equal(vm.Accounts, vm.VisibleAccounts);
        Assert.Equal(new[] { first }, vm.SelectedRows);
    }

    [AvaloniaFact]
    public void State_counters_cover_all_accounts_and_notify_on_live_and_collection_changes()
    {
        var vm = CreateViewModel();
        var running = Row("running");
        running.Running = true;
        var stopped = Row("stopped");
        vm.Accounts.Add(running);
        vm.Accounts.Add(stopped);
        vm.AccountSearch = "no-match";
        vm.AccountStateFilter = 1;
        Assert.Empty(vm.VisibleAccounts);
        Assert.Equal(1, vm.AccountRunningCount);
        Assert.Equal(1, vm.AccountStoppedCount);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        stopped.Running = true;
        Assert.Equal(2, vm.AccountRunningCount);
        Assert.Equal(0, vm.AccountStoppedCount);
        Assert.Contains(nameof(AccountsViewModel.AccountRunningCount), changed);
        Assert.Contains(nameof(AccountsViewModel.AccountStoppedCount), changed);
        vm.Accounts.Remove(running);
        Assert.Equal(1, vm.AccountRunningCount);
        vm.Accounts.Clear();
        Assert.Equal(0, vm.AccountRunningCount);
        Assert.Equal(0, vm.AccountStoppedCount);
    }

    [AvaloniaFact]
    public void Character_sort_uses_last_character_when_current_name_is_empty()
    {
        var vm = CreateViewModel();
        var zulu = Row("first-source");
        zulu.CharacterName = "";
        zulu.LastCharacter = "Zulu";
        var alpha = Row("second-source");
        alpha.CharacterName = "-";
        alpha.LastCharacter = "Alpha";
        vm.Accounts.Add(zulu);
        vm.Accounts.Add(alpha);

        vm.AccountSort = 2;

        Assert.Equal(new[] { alpha, zulu }, vm.VisibleAccounts);
        Assert.Equal(new[] { zulu, alpha }, vm.Accounts);
    }

    private static AccountRow Row(string login)
        => new(new Account { Login = login }, new ObservableCollection<ProxyEntry>(), "");

    internal static AccountsViewModel CreateViewModel()
    {
        // Même isolation que SocketPickerUxTests : aucun service, timer ou lancement réel.
        var vm = (AccountsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AccountsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(AccountsViewModel).GetFields(hidden).Where(f =>
            f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)))
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        typeof(AccountsViewModel).GetField("_filterRows", hidden)!.SetValue(vm, new HashSet<AccountRow>());
        typeof(AccountsViewModel).GetMethod("WireAccountFilters", hidden)!.Invoke(vm, null);
        return vm;
    }
}
