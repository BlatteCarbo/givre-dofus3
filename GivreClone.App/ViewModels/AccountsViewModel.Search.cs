using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class AccountsViewModel
{
    // Projection d'affichage uniquement : commandes globales et sélection gardent Accounts.
    public ObservableCollection<AccountRow> VisibleAccounts { get; } = new();
    private readonly HashSet<AccountRow> _filterRows = new();
    public const string AllAccountServers = "Tous les serveurs";
    public ObservableCollection<string> AccountServerChoices { get; } = new();

    [ObservableProperty] public partial string AccountSearch { get; set; } = "";
    // SelectedIndex : tous / en cours / arrêtés. Running reste la source de vérité des sessions.
    [ObservableProperty] public partial int AccountStateFilter { get; set; }
    [ObservableProperty] public partial string? AccountServerFilter { get; set; } = AllAccountServers;
    // SelectedIndex : ordre source / identité / personnage / serveur / en cours d'abord.
    [ObservableProperty] public partial int AccountSort { get; set; }

    public bool HasAccountFilters => !string.IsNullOrWhiteSpace(AccountSearch) || AccountStateFilter != 0
        || HasAccountServerFilter || AccountSort != 0;
    private bool HasAccountServerFilter => !string.IsNullOrEmpty(AccountServerFilter) && AccountServerFilter != AllAccountServers;
    public int AccountRunningCount => Accounts.Count(row => row.Running);
    public int AccountStoppedCount => Accounts.Count - AccountRunningCount;
    public bool HasVisibleAccounts => VisibleAccounts.Count > 0;
    public bool NoMatchingAccounts => HasAccounts && !HasVisibleAccounts;
    public string AccountResultCount => $"{VisibleAccounts.Count} / {Accounts.Count} compte(s)";

    private void WireAccountFilters()
    {
        Accounts.CollectionChanged += OnAccountsForFilter;
        SyncAccountFilters();
    }

    private void OnAccountsForFilter(object? sender, NotifyCollectionChangedEventArgs e) => SyncAccountFilters();

    private void SyncAccountFilters()
    {
        // Le Reset de la collection ne fournit pas les anciennes lignes : les détacher aussi.
        foreach (var row in _filterRows.Where(row => !Accounts.Contains(row)).ToArray())
        {
            row.PropertyChanged -= OnAccountForFilter;
            _filterRows.Remove(row);
        }
        foreach (var row in Accounts)
            if (_filterRows.Add(row)) row.PropertyChanged += OnAccountForFilter;
        SyncAccountServerChoices();
        RefreshAccountFilter();
        OnPropertyChanged(nameof(HasAccounts));
    }

    private void OnAccountForFilter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(AccountRow.Server))
            SyncAccountServerChoices();
        if (e.PropertyName is null or "" or nameof(AccountRow.Login) or nameof(AccountRow.Label)
            or nameof(AccountRow.Character) or nameof(AccountRow.CharacterName) or nameof(AccountRow.LastCharacter)
            or nameof(AccountRow.Server) or nameof(AccountRow.Running))
            RefreshAccountFilter();
    }

    partial void OnAccountSearchChanged(string value) => RefreshAccountFilter();
    partial void OnAccountStateFilterChanged(int value) => RefreshAccountFilter();
    partial void OnAccountServerFilterChanged(string? value) => RefreshAccountFilter();
    partial void OnAccountSortChanged(int value) => RefreshAccountFilter();

    private void SyncAccountServerChoices()
    {
        var servers = Accounts.Select(row => row.Server?.Trim())
            .Where(server => !string.IsNullOrEmpty(server) && server != "-" && server != AllAccountServers)
            .Select(server => server!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(server => AccountServerChoices.FirstOrDefault(choice =>
                string.Equals(choice, server, StringComparison.OrdinalIgnoreCase)) ?? server)
            .OrderBy(server => server, StringComparer.OrdinalIgnoreCase).ToArray();
        var desired = new[] { AllAccountServers }.Concat(servers).ToArray();
        // Ne pas vider la liste : le ComboBox doit conserver son SelectedItem encore présent.
        for (int i = 0; i < desired.Length; i++)
        {
            int current = AccountServerChoices.IndexOf(desired[i]);
            if (current < 0) AccountServerChoices.Insert(i, desired[i]);
            else if (current != i) AccountServerChoices.Move(current, i);
        }
        if (HasAccountServerFilter && !servers.Contains(AccountServerFilter!, StringComparer.OrdinalIgnoreCase))
            AccountServerFilter = AllAccountServers;
        for (int i = AccountServerChoices.Count - 1; i >= desired.Length; i--)
            AccountServerChoices.RemoveAt(i);
    }

    private void RefreshAccountFilter()
    {
        // The shipped app uses invariant globalization: CompareOptions.IgnoreNonSpace
        // does not provide accent folding there. Unicode normalization is explicit.
        static string SearchKey(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark))
            .ToUpperInvariant();
        var query = SearchKey(AccountSearch?.Trim() ?? "");
        var desired = Accounts.Where(row =>
            (AccountStateFilter != 1 || row.Running) &&
            (AccountStateFilter != 2 || !row.Running) &&
            (!HasAccountServerFilter || string.Equals(row.Server?.Trim(), AccountServerFilter, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 ||
             Matches(row.Login) || Matches(row.Label) || Matches(row.Character) ||
             Matches(row.CharacterName) || Matches(row.LastCharacter) || Matches(row.Server)));

        bool Matches(string? value) => value is not null && SearchKey(value).Contains(query, StringComparison.Ordinal);

        // OrderBy est stable : les clés égales suivent toujours l'ordre de Accounts.
        var ordered = AccountSort switch
        {
            1 => desired.OrderBy(row => string.IsNullOrWhiteSpace(row.Label) ? row.Login : row.Label, StringComparer.OrdinalIgnoreCase),
            2 => desired.OrderBy(row => !string.IsNullOrWhiteSpace(row.CharacterName) && row.CharacterName != "-"
                ? row.CharacterName : !string.IsNullOrWhiteSpace(row.LastCharacter) && row.LastCharacter != "-"
                    ? row.LastCharacter : row.Character, StringComparer.OrdinalIgnoreCase),
            3 => desired.OrderBy(row => row.Server, StringComparer.OrdinalIgnoreCase),
            4 => desired.OrderByDescending(row => row.Running),
            _ => desired
        };
        var rows = ordered.ToArray();

        // Réconciliation en place : ni mutation de la source, ni perte de l'état des cartes conservées.
        var keep = rows.ToHashSet();
        for (int i = VisibleAccounts.Count - 1; i >= 0; i--)
            if (!keep.Contains(VisibleAccounts[i])) VisibleAccounts.RemoveAt(i);
        for (int i = 0; i < rows.Length; i++)
        {
            int current = VisibleAccounts.IndexOf(rows[i]);
            if (current < 0) VisibleAccounts.Insert(i, rows[i]);
            else if (current != i) VisibleAccounts.Move(current, i);
        }
        OnPropertyChanged(nameof(HasAccountFilters));
        OnPropertyChanged(nameof(HasVisibleAccounts));
        OnPropertyChanged(nameof(NoMatchingAccounts));
        OnPropertyChanged(nameof(AccountResultCount));
        OnPropertyChanged(nameof(AccountRunningCount));
        OnPropertyChanged(nameof(AccountStoppedCount));
    }

    [RelayCommand]
    private void ClearAccountFilters()
    {
        AccountSearch = "";
        AccountStateFilter = 0;
        AccountServerFilter = AllAccountServers;
        AccountSort = 0;
    }
}
