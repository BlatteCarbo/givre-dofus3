using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    [RelayCommand]
    private Task StartCrush() => CanCrush ? RunCrushAsync() : Task.CompletedTask;

    private async Task RunLevelingAsync()
    {
        if (AdaptiveLeveling) { await RunAdaptiveLevelingAsync(); return; }
        int pid = _pid;
        var cts = new CancellationTokenSource();
        _cts = cts;
        Running = true;
        int finished = 0;
        try
        {
            if (SelectedJob is not { JobId: > 0 } job) { Log("Choisis le métier à monter."); return; }
            if (LevelingRune is not { ItemId: > 0 } rune) { Log(SmithJournal.Leveling(SmithLevelingStop.MissingRune)); return; }
            int? level = JobLevelOf((uint)pid, job.JobId);
            var gate = SmithLeveling.Gate(level, TargetJobLevel, true, 1);
            if (gate is SmithLevelingStop.ProfessionUnknown or SmithLevelingStop.AlreadyAtTarget or SmithLevelingStop.MissingTarget)
            { Log(SmithJournal.Leveling(gate)); return; }
            _buyRounds = 0;
            _spentKamas = 0;
            _loggedSkips.Clear();
            long? stay = null;

            while (!cts.IsCancellationRequested)
            {
                CheckSession((uint)pid, cts.Token);
                level = JobLevelOf((uint)pid, job.JobId);
                if (level is int reached && reached >= TargetJobLevel)
                { Log(SmithJournal.Leveling(SmithLevelingStop.AlreadyAtTarget)); break; }
                LogSkips(job.JobId);
                var items = Queue.Select(row => new LevelingItem(row.ObjectUid, StillInBag((uint)pid, row.ObjectUid), AdmitItem(row.ObjectUid, row.Gid, job.JobId), ItemLevelOf(row.Gid) ?? 0)).ToList();
                long? next = SmithLeveling.NextUid(items, stay);
                if (next is null) { Log(SmithJournal.Leveling(SmithLevelingStop.NoForgeableItem)); break; }
                var item = Queue.First(row => row.ObjectUid == next.Value);
                if (stay is long previous && previous != item.ObjectUid)
                    Log("Passage à " + item.Name + " (objet forgeable du plus haut niveau).");
                stay = item.ObjectUid;
                var stock = RuneQuantities((uint)pid, rune.ItemId);
                if (!stock.Known && !_bankNoteSent) { _bankNoteSent = true; Log("Banque pas encore lue : seule la quantité du sac est comptée."); }
                int qty = RuneStock.Visible(stock.Bag, stock.Bank, stock.Known);
                if (qty <= 0)
                {
                    if (!await BuyLevelingRuneAsync((uint)pid, rune.ItemId, cts.Token).ConfigureAwait(true))
                    { Log(SmithJournal.Leveling(SmithLevelingStop.NoRune)); break; }
                    continue;
                }
                long price = LevelingRune?.UnitPrice ?? rune.UnitPrice;
                if (!SmithLeveling.CanSpendRune(_spentKamas, price, MaxKamas))
                { Log("Plafond de kamas atteint : aucune rune supplémentaire."); break; }
                int steps = Math.Min(Math.Max(1, MaxRunes), qty);
                if (MaxKamas != long.MaxValue && price > 0)
                    steps = (int)Math.Min((long)steps, (MaxKamas - _spentKamas) / price);
                if (steps <= 0) { Log("Plafond de kamas atteint : aucune rune supplémentaire."); break; }

                var workshop = await PrepareWorkshopAsync((uint)pid, item.Gid, cts.Token).ConfigureAwait(true);
                if (workshop is not { } ws) { Log(SmithJournal.Leveling(SmithLevelingStop.WorkshopFailed)); break; }
                CheckSession((uint)pid, cts.Token);
                string? runeName = LevelingRuneName(rune.ItemId);
                if (string.IsNullOrWhiteSpace(runeName)) { Log("Rune choisie inconnue du catalogue : aucune rune passée."); break; }
                int jobId = job.JobId;
                int target = TargetJobLevel;
                ForgeAction Decide(IReadOnlyDictionary<string, int> _, int? __)
                {
                    if (cts.IsCancellationRequested || _pid != pid) return ForgeAction.Finish;
                    if (JobLevelOf((uint)pid, jobId) is int now && now >= target) return ForgeAction.Finish;
                    return ForgeAction.Use(runeName);
                }
                var report = await Task.Run(() => Stores.Launch.ForgeAsync((uint)pid, ws.ElementId, ws.SkillUid, (int)item.ObjectUid, Decide, steps, cts.Token)).ConfigureAwait(true);
                CheckSession((uint)pid, cts.Token);
                _spentKamas += Math.Max(0, price) * report.Steps.Count;
                Log(item.Name + " : arrêt après " + report.Steps.Count.ToString() + " rune(s), " + report.Stop + ".");
                if (report.Stop.StartsWith("rune refus", StringComparison.Ordinal)) { Log("Rune refusée : session arrêtée, file conservée."); break; }
                if (report.Stop.StartsWith("rune absente", StringComparison.Ordinal))
                {
                    if (!await BuyLevelingRuneAsync((uint)pid, rune.ItemId, cts.Token).ConfigureAwait(true))
                    { Log(SmithJournal.Leveling(SmithLevelingStop.NoRune)); break; }
                    continue;
                }
                if (report.Stop.StartsWith("atelier non", StringComparison.Ordinal) || report.Stop.StartsWith("objet refus", StringComparison.Ordinal) || report.Stop.StartsWith("atelier ferm", StringComparison.Ordinal) || report.Stop.Contains("lai d", StringComparison.Ordinal) || report.Stop is "permission" or "relay" || report.Stop.StartsWith("annul", StringComparison.Ordinal))
                    break;
                if (!StillInBag((uint)pid, item.ObjectUid))
                { Log(item.Name + " a quitté le sac, passage au suivant."); stay = null; continue; }
                if (report.Steps.Count == 0) break;
                finished++;
            }
        }
        catch (OperationCanceledException) { if (_pid == pid) Log("Session annulée."); }
        catch (Exception e) { if (_pid == pid) Log("Erreur : " + e.Message); }
        finally
        {
            Running = false;
            _cts = null;
            cts.Dispose();
            SessionText = "Dernière montée : " + finished.ToString() + " série(s) de runes.";
            OnPropertyChanged(nameof(CanCrush));
        }
    }

    private void LogSkips(int selectedJob)
    {
        foreach (var row in Queue.ToList())
        {
            var admission = AdmitItem(row.ObjectUid, row.Gid, selectedJob);
            if (admission == SmithAdmission.Forge) { _loggedSkips.Remove(row.ObjectUid); continue; }
            if (!_loggedSkips.Add(row.ObjectUid)) continue;
            Log(SmithJournal.Skip(row.Name, admission, JobLevelOf((uint)_pid, selectedJob), ItemLevelOf(row.Gid)));
        }
    }

    private async Task<bool> BuyLevelingRuneAsync(uint pid, int runeId, CancellationToken ct)
    {
        if (!AutoBuyRunes || BuyRunes is null || _buyRounds >= 2) return false;
        _buyRounds++;
        var needs = new[] { ((long)runeId, Math.Max(1, MaxRunes)) };
        long budgetLeft = Math.Max(0, BuyMaxKamas - _spentKamas);
        if (budgetLeft <= 0) { Log("Achat impossible : budget épuisé."); return false; }
        long before = Stores.Launch.Kamas(pid);
        var (bought, report) = await BuyRunes(pid, needs, price => RunePurchasePlan.Plan(needs, price, BuyMaxPercent, budgetLeft), ct).ConfigureAwait(true);
        CheckSession(pid, ct);
        long after = Stores.Launch.Kamas(pid);
        if (before >= 0 && after >= 0) _spentKamas += Math.Max(0, before - after);
        Log("Achat : " + report + ".");
        return bought > 0;
    }

}
