using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public sealed record SmithSessionHistoryRow(string Title, string Detail);

public partial class SmithMagicViewModel
{
    private int _historyPid;
    private int _forgeHistoryDirty = 1;
    private DispatcherTimer? _forgeHistoryTimer;
    public ObservableCollection<SmithSessionHistoryRow> SessionHistory { get; } = [];
    [ObservableProperty] public partial string ForgeHistoryNote { get; set; } = "Historique local mesure, sans donnees reconstituees.";

    private void InitializeForgeHistory()
    {
        // Network events only set a flag. A busy forge never queues dispatcher work per rune.
        Stores.Launch.ForgeHistory.Changed += RefreshForgeHistory;
        _forgeHistoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _forgeHistoryTimer.Tick += (_, _) => FlushForgeHistory();
        _forgeHistoryTimer.Start();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CanStart) || _historyPid == _pid) return;
            _historyPid = _pid;
            ClearHistory();
            SessionHistory.Clear();
            RefreshForgeHistory();
        };
    }

    [RelayCommand]
    private void RefreshForgeHistory() => Interlocked.Exchange(ref _forgeHistoryDirty, 1);

    public static string HistoryText(IEnumerable<SmithSessionHistoryRow> rows)
        => string.Join("\n\n", rows.Select(r => r.Title + "\n" + r.Detail));

    [RelayCommand]
    private async System.Threading.Tasks.Task CopyForgeHistoryAsync()
    {
        if (SessionHistory.Count == 0) { ForgeHistoryNote = "Rien à copier : aucune séance pour ce bot."; return; }
        await ClipboardService.CopyAsync(HistoryText(SessionHistory)).ConfigureAwait(true);
        ForgeHistoryNote = $"{SessionHistory.Count} séance(s) copiée(s) dans le presse-papiers.";
    }

    private void FlushForgeHistory()
    {
        if (Interlocked.Exchange(ref _forgeHistoryDirty, 0) == 0) return;
        var history = Stores.Launch.ForgeHistory;
        if (!history.IsReady)
        {
            ForgeHistoryNote = history.Ready.IsFaulted
                ? "Archive illisible : " + history.Ready.Exception!.GetBaseException().Message
                : "Chargement de l'archive en arriere-plan...";
            Interlocked.Exchange(ref _forgeHistoryDirty, 1);
            return;
        }
        if (_historyPid != _pid)
        {
            _historyPid = _pid;
            ClearHistory();
        }
        if (_pid <= 0)
        {
            SessionHistory.Clear();
            return;
        }
        string? account = Stores.Launch.ScriptAccounts.FirstOrDefault(a => a.Pid == (uint)_pid)?.Id;
        try { Stores.Launch.ReconcileSmithHistory(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ForgeHistoryNote = "Rapprochement historique impossible : " + e.Message;
            Interlocked.Exchange(ref _forgeHistoryDirty, 1);
            return;
        }
        var page = Stores.Launch.ForgeHistory.ReadRecent((uint)_pid, account);
        var rows = (account is null ? Array.Empty<SmithSessionHistoryRow>() :
            ProjectSmithObjectives(Stores.Launch.SmithObjectives.Read(account), Stores.Launch.Kamas((uint)_pid)))
            .Concat(ProjectForgeHistory(page)).ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i >= SessionHistory.Count) SessionHistory.Add(rows[i]);
            else if (SessionHistory[i] != rows[i]) SessionHistory[i] = rows[i];
        }
        while (SessionHistory.Count > rows.Length) SessionHistory.RemoveAt(SessionHistory.Count - 1);
        ForgeHistoryNote = Stores.Launch.ForgeHistory.HasArchive
            ? "Archive locale persistante. " + (page.Truncated
                ? "Vue recente limitee ; historique anterieur consultable par MCP."
                : "XP et depenses observees uniquement.")
            : "Memoire locale : 128 sessions / 2000 evenements maximum. " +
              (page.Truncated ? "Detail retenu partiel." : "XP et depenses observees uniquement.");
    }

    private static IEnumerable<SmithSessionHistoryRow> ProjectSmithObjectives(
        IReadOnlyList<SmithObjective> objectives, long wallet)
    {
        foreach (var objective in objectives)
            yield return new SmithSessionHistoryRow(
                $"Objectif metier {objective.Identity.JobId} -> {objective.TargetLevel} | " +
                (objective.NeedsReconciliation ? "rapprochement requis" : "budget conserve"),
                $"Plafond {objective.Limit:N0} k | plancher portefeuille {objective.WalletFloor:N0} k\n" +
                $"Depenses totales : {objective.TotalExpense?.ToString("N0") ?? "inconnues"} k " +
                $"(sous-total historique confirme : {objective.HistoricalConfirmedSubtotal:N0} k)\n" +
                $"Nouveaux debits confirmes : {objective.NewExpense:N0} k | reserves : {objective.Reserved:N0} k\n" +
                $"Disponible autorise maintenant : {objective.Available(wallet):N0} k");
    }

    private static IEnumerable<SmithSessionHistoryRow> ProjectForgeHistory(ForgeHistoryPage page)
    {
        var outcomes = page.Entries.Where(e => e.Kind == "rune").ToLookup(e => e.SessionId);
        foreach (var session in page.Sessions.Reverse().Take(128))
        {
            var jobs = session.JobId is { } job ? new[] { job } :
                session.InitialProgress.Keys.Union(session.LatestProgress.Keys).OrderBy(x => x).ToArray();
            var progress = jobs.Select(id =>
            {
                session.InitialProgress.TryGetValue(id, out var before);
                session.LatestProgress.TryGetValue(id, out var after);
                string xp = before?.Experience is { } start && after?.Experience is { } end && end >= start
                    ? $"+{end - start} XP mesuree" : "XP inconnue";
                return $"metier {id}: niv. {before?.Level.ToString() ?? "?"} -> {after?.Level.ToString() ?? "?"}, {xp}";
            });
            var runes = outcomes[session.Id].ToArray();
            string results = string.Join(", ", runes.GroupBy(e => e.Outcome).OrderBy(g => g.Key)
                .Select(g => $"code {g.Key?.ToString() ?? "?"}: {g.Count()}"));
            var events = page.Entries.Where(e => e.SessionId == session.Id).ToArray();
            string supports = string.Join(", ", events.Where(e => e.Kind == "support")
                .Select(e => $"GID {e.ItemGid} / UID {e.ItemUid}").Distinct());
            string consumption = string.Join(", ", runes.GroupBy(e => (e.RuneGid, e.Rune))
                .Select(g => $"{g.Key.Rune} (GID {g.Key.RuneGid?.ToString() ?? "?"}) x{g.Sum(e => e.Quantity ?? 1)}"));
            long stockValue = runes.Where(e => e.StockUnitValue.HasValue)
                .Sum(e => checked(e.StockUnitValue!.Value * (e.Quantity ?? 1)));
            int unknownValues = runes.Count(e => !e.StockUnitValue.HasValue);
            string purchases = string.Join(", ", events.Where(e => e.Kind == "purchase")
                .Select(e => $"GID {e.ItemGid?.ToString() ?? "?"} x{e.Quantity?.ToString() ?? "?"} a {e.UnitPrice?.ToString() ?? "?"} k/unite"));
            yield return new SmithSessionHistoryRow(
                $"{session.Started.LocalDateTime:dd/MM HH:mm:ss} | {session.Source} | {session.Stop ?? "en cours"}",
                $"{session.RuneCount} runes | {session.ActualCashExpense:N0} k debites enregistres\n" +
                string.Join("\n", progress) +
                $"\nSupports : {supports}\nConsommation : {consumption}" +
                $"\nValeur estimee du stock consomme : {stockValue:N0} k ; {unknownValues} valeur(s) inconnue(s), hors budget cash" +
                $"\nAchats : {purchases}\nIssues retenues ({runes.Length}/{session.RuneCount}): {results}" +
                $"\n{GivreClone.App.Services.Forgemagie.ForgeAttemptCost.Describe(session.RuneCount, session.ActualCashExpense, stockValue, unknownValues)}");
        }
    }
}
