using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Metamob;

namespace GivreClone.App.ViewModels;

public sealed record ScopeOption(SoulScope Scope, string Label);

/// <summary>
/// Onglet Metamob. Le parent n'est pas branché ici : BotsViewModel expose SoulSync, appelle AttachPid
/// avec le personnage sélectionné (tick et changement de sélection), et BotsView.axaml ajoute un TabItem
/// dont le contenu est SoulSyncView avec DataContext SoulSync.
/// </summary>
public partial class SoulSyncViewModel : ViewModelBase
{
    private readonly ISoulHoldingsSource _holdings;
    private readonly Func<string?> _readKey;
    private readonly Func<string, MetamobClient> _open;
    private readonly int _pageSize;
    private readonly int _batchSize;
    private readonly List<MetamobQuest> _allQuests = new();
    private SoulSyncStop? _stop;
    private uint _pid;
    private bool _commandsReady;

    public SoulSyncViewModel() : this(new LaunchSoulHoldings(Stores.Launch), static () => MetamobKeyFile.TryRead(), null) { }

    public SoulSyncViewModel(ISoulHoldingsSource holdings, Func<string?> readKey, Func<string, MetamobClient>? openClient,
        int pageSize = MetamobLimits.MaxPage, int batchSize = MetamobLimits.MaxBatch)
    {
        _holdings = holdings;
        _readKey = readKey;
        _open = openClient ?? (key => new MetamobClient(key));
        _pageSize = pageSize;
        _batchSize = batchSize;
        SelectedScope = Scopes[2];
        _commandsReady = true;
    }

    public IReadOnlyList<ScopeOption> Scopes { get; } = new ScopeOption[]
    {
        new(SoulScope.Bag, "Sac"),
        new(SoulScope.Chest, "Coffre"),
        new(SoulScope.Both, "Sac et coffre"),
    };

    public ObservableCollection<MetamobServer> Servers { get; } = new();
    public ObservableCollection<MetamobQuest> Quests { get; } = new();
    public ObservableCollection<MetamobSyncRow> Rows { get; } = new();

    [ObservableProperty] public partial string Status { get; set; } = "Sélectionnez un personnage.";
    [ObservableProperty] public partial string CaptureNeeded { get; set; } = "";
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial MetamobServer? SelectedServer { get; set; }
    [ObservableProperty] public partial MetamobQuest? SelectedQuest { get; set; }
    [ObservableProperty] public partial ScopeOption? SelectedScope { get; set; }

    public bool HasCapture => CaptureNeeded.Length > 0;
    private bool CanSynchronize() => !Busy && _pid != 0 && SelectedServer is not null && SelectedQuest is not null && SelectedScope is not null;
    private bool CanRefresh() => !Busy;
    private bool CanStop() => Busy && _stop is not null;

    public void AttachPid(int pid)
    {
        var next = pid > 0 ? (uint)pid : 0;
        if (_pid == next) return;
        _pid = next;
        NotifyCommands();
        if (!Busy)
            Status = _pid == 0 ? "Sélectionnez un personnage." : "Choisissez le serveur, la quête et le périmètre, puis Synchroniser.";
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshListsAsync()
    {
        if (Busy) return;
        var key = _readKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Status = "Clé API absente.";
            return;
        }
        var previous = SelectedServer?.Id;
        var slug = SelectedQuest?.Slug;
        Busy = true;
        try
        {
            using var client = _open(key);
            await client.ConfirmKeyAsync(CancellationToken.None);
            var servers = await client.ListServersAsync(CancellationToken.None);
            var quests = await client.ListQuestsAsync(CancellationToken.None);
            SelectedServer = null;
            Servers.Clear();
            foreach (var server in servers)
                Servers.Add(server);
            _allQuests.Clear();
            _allQuests.AddRange(quests);
            SelectedServer = FindServer(previous) ?? First(Servers);
            if (slug is not null)
                SelectedQuest = FindQuest(slug) ?? SelectedQuest;
            Status = Servers.Count == 0 ? "Aucun serveur renvoyé." : "Listes à jour. Clé acceptée.";
        }
        catch (MetamobCallException ex) when (ex.Status is 401 or 403)
        {
            Status = "Clé refusée.";
        }
        catch (Exception ex)
        {
            Status = MetamobText.Scrub(ex.Message, key);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSynchronize))]
    public async Task SynchronizeAsync()
    {
        if (Busy || SelectedServer is null || SelectedQuest is null || SelectedScope is null || _pid == 0) return;
        Rows.Clear();
        CaptureNeeded = "";
        var key = _readKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Status = "Clé API absente. Aucune écriture.";
            return;
        }
        var stop = new SoulSyncStop();
        _stop = stop;
        Busy = true;
        Status = "Lecture du stock, puis envoi des quantités qui changent.";
        try
        {
            var holdings = _holdings.Read(_pid);
            using var client = _open(key);
            var report = await MetamobSync.ExecuteAsync(client, new MetamobSyncRequest(
                SelectedQuest.Slug, SelectedServer.Id, SelectedQuest.ServerId, SelectedScope.Scope, holdings, _pageSize, _batchSize), stop);
            Status = MetamobText.Scrub(report.Status, key);
            CaptureNeeded = MetamobText.Scrub(report.CaptureNeeded, key);
            foreach (var row in report.Rows)
                Rows.Add(row with { Detail = MetamobText.Scrub(row.Detail, key) });
        }
        catch (MetamobCallException ex) when (ex.Status is 401 or 403)
        {
            Status = "Clé refusée. Aucune écriture.";
        }
        catch (Exception ex)
        {
            Status = MetamobText.Scrub(ex.Message, key);
        }
        finally
        {
            _stop = null;
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public void Stop() => _stop?.Request();

    partial void OnBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedServerChanged(MetamobServer? value) => ApplyServerFilter();
    partial void OnSelectedQuestChanged(MetamobQuest? value) => NotifyCommands();
    partial void OnSelectedScopeChanged(ScopeOption? value) => NotifyCommands();
    partial void OnCaptureNeededChanged(string value) => OnPropertyChanged(nameof(HasCapture));

    private void ApplyServerFilter()
    {
        var slug = SelectedQuest?.Slug;
        Quests.Clear();
        if (SelectedServer is not null)
        {
            foreach (var quest in _allQuests)
                if (quest.ServerId == SelectedServer.Id)
                    Quests.Add(quest);
        }
        SelectedQuest = slug is null ? First(Quests) : FindQuest(slug) ?? First(Quests);
        NotifyCommands();
    }

    private MetamobServer? FindServer(int? id)
    {
        if (id is null) return null;
        foreach (var server in Servers)
            if (server.Id == id) return server;
        return null;
    }

    private MetamobQuest? FindQuest(string slug)
    {
        foreach (var quest in Quests)
            if (quest.Slug == slug) return quest;
        return null;
    }

    private static T? First<T>(IEnumerable<T> items) where T : class
    {
        foreach (var item in items) return item;
        return null;
    }

    private void NotifyCommands()
    {
        if (!_commandsReady) return;
        SynchronizeCommand.NotifyCanExecuteChanged();
        RefreshListsCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
