using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    // null leaves rune selection automatic; an empty set authorizes no rune.
    public IReadOnlySet<long>? AdaptiveAllowedRuneGids { get; set; }
    public IReadOnlySet<long>? AdaptiveAllowedSupportGids { get; set; }

    private async Task RunAdaptiveLevelingAsync()
    {
        int pid = _pid;
        if (SelectedJob is not { JobId: > 0 } job || TargetJobLevel is < 1 or > 200) return;
        if (JobLevelOf((uint)pid, job.JobId) is not int initialLevel)
        { Log("Niveau du métier inconnu : aucun achat."); return; }
        if (initialLevel >= TargetJobLevel) { Log("Niveau cible déjà atteint."); return; }
        var launch = Stores.Launch;
        long initialCash = launch.Kamas((uint)pid);
        int targetLevel = TargetJobLevel;
        string accountId = launch.ScriptAccounts.FirstOrDefault(a => a.Pid == (uint)pid)?.Id ?? "";
        var identity = new SmithObjectiveIdentity(accountId, launch.ServerIdOf((uint)pid), launch.CharacterId((uint)pid), job.JobId);
        long requestedLimit = Math.Min(MaxKamas, BuyMaxKamas);
        if (initialCash < 0 || requestedLimit <= 0 || accountId.Length == 0
            || identity.ServerId <= 0 || identity.CharacterId <= 0)
        { Log("Identité ou budget de montée inconnu : aucun achat."); return; }
        var existing = launch.SmithObjectives.Read(accountId).FirstOrDefault(o => o.Identity == identity);
        var objective = existing ?? launch.SmithObjectives.Open(identity, targetLevel, requestedLimit,
            Math.Max(0, initialCash - requestedLimit));
        if (objective.TargetLevel != targetLevel || objective.Limit != requestedLimit)
        { Log("Objectif existant différent : son budget est conservé, aucun nouveau départ."); return; }
        if (objective.NeedsReconciliation)
        { Log("Historique de dépenses à rapprocher : aucun nouveau débit autorisé."); return; }
        using var cts = new CancellationTokenSource();
        var historySession = launch.BeginForgeHistorySession((uint)pid,
            accountId, job.JobId, "adaptive-leveling", objective.Id);
        using var spending = launch.BeginSmithSpending((uint)pid, objective.Id, historySession);
        _cts = cts;
        Running = true;
        SmithObjective Budget() => launch.SmithObjectives.Read(accountId).Single(o => o.Id == objective.Id);
        long Remaining() => Budget().Available(launch.Kamas((uint)pid));
        int used = 0;
        var selectedSupports = Picks.Where(row => row.IsChecked && row.Allowed && !row.IsRune)
            .Select(row => row.ObjectUid).ToHashSet();
        var allowedRunes = AdaptiveAllowedRuneGids?.ToHashSet();
        var allowedSupportGids = AdaptiveAllowedSupportGids?.ToHashSet();
        RejectedWorkshopElements.Clear();
        int observationLevel = initialLevel;
        var observations = new List<SmithXpObservation>();
        long? previousUid = null;
        var upgradesTried = new HashSet<int>();
        // Local stop policy only; a blank result does not establish a saturation mechanic.
        var exhausted = new HashSet<(long Item, long Rune)>();
        string stop = "limite de runes";
        try
        {
            Log($"Montée adaptative : métier {job.Name}, cible {targetLevel}, objectif {objective.Id}, budget total {objective.Limit} kamas.");
            while (used < MaxRunes)
            {
                CheckSession((uint)pid, cts.Token);
                if (JobLevelOf((uint)pid, job.JobId) is not int level)
                { stop = "niveau du métier inconnu"; break; }
                if (level >= targetLevel) { stop = "niveau cible atteint"; break; }
                if (level != observationLevel) { observations.Clear(); exhausted.Clear(); observationLevel = level; }
                long cash = launch.Kamas((uint)pid);
                if (Budget().NeedsReconciliation) { stop = "dépense à rapprocher"; break; }

                RebuildPicks();
                BagPickRow[] EligibleSupports() => Picks.Where(row => row.InBag
                    && (selectedSupports.Count == 0 || selectedSupports.Contains(row.ObjectUid))
                    && SmithProfessionGate.Admit(row.ItemJob, job.JobId, row.ItemLevelValue, level) == SmithAdmission.Forge
                    && ItemBindingRules.CanForge(BindingOf((uint)pid, row.ObjectUid))).ToArray();
                var supports = EligibleSupports();
                BagPickRow? Best() => supports.OrderByDescending(row => row.Level).FirstOrDefault();
                var item = Best();
                // Reconsider an upgrade once per level; its XP benefit still needs measurement.
                if ((item is null || item.Level < level) && upgradesTried.Add(level)
                    && selectedSupports.Count == 0 && allowedSupportGids?.Count != 0
                    && Remaining() > 5000 && AutoBuyRunes && BuyLevelingSupport is not null)
                {
                    // Keep cash for the return journey, not a second charge for consumed runes.
                    var bought = await BuyLevelingSupport((uint)pid, job.JobId, level,
                        Math.Max(objective.WalletFloor, cash - Remaining()) + Math.Min(5000, Remaining()),
                        allowedSupportGids, allowedRunes, cts.Token).ConfigureAwait(true);
                    CheckSession((uint)pid, cts.Token);
                    launch.ForgeHistory.Record(historySession, "support-lookup", launch.JobProgress((uint)pid), reason: bought.Report);
                    Log("Support : " + bought.Report);
                    RebuildPicks();
                    supports = EligibleSupports();
                    item = Best();
                }
                if (item is null) { stop = "aucun support compatible accessible"; break; }
                // Compare cheap base runes beyond the four elemental characteristics.
                // Prices are the current server's; no guide-derived XP formula is assumed.
                var runes = launch.RuneItemsByGid().Where(r => r.Value.Tier == "Ba"
                    && (allowedRunes is null || allowedRunes.Contains(r.Key))
                    && r.Value.EffectId is 118 or 123 or 126 or 119 or 124 or 125 or 174 or 158)
                    .Select(r => (Gid: r.Key, r.Value.Name, Price: launch.AveragePrice((uint)pid, (int)r.Key), r.Value.EffectId))
                    .ToArray();
                // Native lines and price/density only order unmeasured exploration, never predict XP.
                var native = launch.ItemEffects((uint)pid, (int)item.ObjectUid).Select(e => e.ActionId).ToHashSet();
                // Published rune weights (DPLN): kamas per point of weight, not per rune, decide what is cheap.
                static decimal Weight(int effect) => effect switch { 124 => 3m, 158 => 2.5m, _ => 1m };
                runes = runes.OrderBy(r => native.Contains(r.EffectId) ? 0 : 1)
                    .ThenBy(r => r.Price > 0 ? r.Price / Weight(r.EffectId) : decimal.MaxValue).ThenBy(r => r.Gid).ToArray();
                if (runes.Length < 1) { stop = "aucune rune autorisée"; break; }
                var rune = runes[0];
                var runeStock = launch.Inventory.Items((uint)pid).Where(i => i.Position == 63)
                    .GroupBy(i => i.Gid).ToDictionary(g => g.Key, g => g.Sum(i => i.Qty));
                var choices = supports.SelectMany(row => runes.Select(r =>
                    {
                        int stock = runeStock.GetValueOrDefault(r.Gid);
                        return new SmithLevelingChoice(row.ObjectUid, r.Gid, Math.Min(5, stock),
                            stock > 0 ? 0 : null, row.ObjectUid == previousUid);
                    })).Where(c => !exhausted.Contains((c.ItemUid, c.RuneGid))).ToArray();
                var choice = SmithMeasuredPlanner.Choose(choices, observations, Remaining());
                if (choice is not null)
                {
                    item = Picks.Single(row => row.ObjectUid == choice.ItemUid);
                    rune = runes.Single(r => r.Gid == choice.RuneGid);
                }
                else if (choices.Any(c => c.Runes > 0) || choices.Length == 0)
                { stop = "aucun choix productif avec XP attribuable"; break; }
                if (previousUid != item.ObjectUid)
                {
                    Log($"Support : {item.Name}, niveau {item.Level} ; métier {level}.");
                    previousUid = item.ObjectUid;
                }
                int quantity = runeStock.GetValueOrDefault(rune.Gid);
                if (quantity <= 0)
                {
                    if (!AutoBuyRunes || BuyRunesAtLotPrices is null || Remaining() <= 5000)
                    { stop = "runes absentes ou budget achat épuisé"; break; }
                    var needs = runes.Where(r => choices.Any(c => c.RuneGid == r.Gid))
                        .Select(r => (r.Gid, Quantity: Math.Min(100, MaxRunes - used))).ToArray();
                    var bought = await BuyRunesAtLotPrices((uint)pid, needs,
                        (price, lotPrice) => RunePurchasePlan.PlanExploration(needs.Select(n => n.Gid), price, lotPrice,
                            BuyMaxPercent, Math.Max(0, Remaining() - 5000), Math.Min(5, MaxRunes - used)),
                        cts.Token).ConfigureAwait(true);
                    CheckSession((uint)pid, cts.Token);
                    launch.ForgeHistory.Record(historySession, "rune-purchase", launch.JobProgress((uint)pid), reason: bought.Report);
                    Log("Runes : " + bought.Report);
                    // A late receipt reports 0 bought while the runes did reach the bag: the bag decides.
                    if (bought.Bought <= 0 && !runes.Any(r => launch.Inventory.Items((uint)pid)
                        .Any(row => row.Position == 63 && row.Gid == r.Gid && row.Qty > 0)))
                    { stop = "aucune rune achetée sous le plafond"; break; }
                    continue;
                }
                var workshop = await PrepareWorkshopAsync((uint)pid, item.Gid, cts.Token).ConfigureAwait(true);
                if (workshop is not { } ws) { stop = "atelier compatible non rejoint"; break; }
                long? beforeXp = launch.JobProgress((uint)pid).GetValueOrDefault(job.JobId)?.Experience;
                int steps = Math.Min(Math.Min(5, quantity), MaxRunes - used);
                ForgeAction Decide(IReadOnlyDictionary<string, int> _, int? __)
                {
                    if (cts.IsCancellationRequested || _pid != pid
                        || JobLevelOf((uint)pid, job.JobId) is not int now || now >= targetLevel)
                        return ForgeAction.Finish;
                    return ForgeAction.Use(rune.Name);
                }
                long revision = launch.SmithJobProgressRevision((uint)pid, job.JobId);
                int freshUpdates = 0, expectedUpdates = int.MaxValue;
                JobProgressSnapshot? latest = null;
                var measured = new TaskCompletionSource<JobProgressSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                var measurementGate = new object();
                void OnProgress(uint source, int sourceJob, long updateRevision, JobProgressSnapshot snapshot)
                {
                    if (source != (uint)pid || sourceJob != job.JobId || updateRevision <= revision) return;
                    lock (measurementGate)
                    {
                        freshUpdates++;
                        latest = snapshot;
                        if (freshUpdates >= expectedUpdates) measured.TrySetResult(snapshot);
                    }
                }
                launch.SmithJobProgressUpdated += OnProgress;
                LaunchService.ForgeRunReport report;
                long? measuredXp = null;
                try
                {
                    report = await Task.Run(() => launch.ForgeAsync((uint)pid, ws.ElementId, ws.SkillUid,
                        (int)item.ObjectUid, Decide, steps, cts.Token, historySessionId: historySession)).ConfigureAwait(true);
                    lock (measurementGate)
                    {
                        // Failed runes do not emit a profession XP update. Count only
                        // XP-bearing results, not every consumed rune.
                        expectedUpdates = report.Steps.Count(s => s.Code is
                            GivreClone.Protocol.InventoryReader.CraftSuccess or
                            GivreClone.Protocol.InventoryReader.CraftNeutral);
                        if (expectedUpdates > 0 && freshUpdates >= expectedUpdates && latest is not null)
                            measured.TrySetResult(latest);
                    }
                    if (report.Steps.Count > 0 && report.Steps.All(s =>
                        s.Code == GivreClone.Protocol.InventoryReader.CraftFailed))
                        measuredXp = 0; // Confirmed failures, not an absent successful-result update.
                    else if (report.Steps.Count > 0)
                    {
                        try
                        {
                            var after = await measured.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(true);
                            if (beforeXp is long before && after.Experience is long xpAfter && xpAfter >= before)
                                measuredXp = xpAfter - before;
                        }
                        catch (TimeoutException) { /* Missing fresh XP remains unknown, never a zero observation. */ }
                    }
                }
                finally { launch.SmithJobProgressUpdated -= OnProgress; }
                CheckSession((uint)pid, cts.Token);
                if (!report.Opened && report.Steps.Count == 0 && report.Stop == "atelier non ouvert")
                {
                    RejectedWorkshopElements.Add(ws.ElementId);
                    Log($"Atelier {ws.ElementId} refuse : autre machine compatible recherchée, aucune rune passée.");
                    continue;
                }
                used += report.Steps.Count;
                AddAdaptiveHistoryBatch(report.Steps);
                var progress = launch.JobProgress((uint)pid).GetValueOrDefault(job.JobId);
                if (measuredXp == 0 && report.Steps.Count > 0)
                {
                    exhausted.Add((item.ObjectUid, rune.Gid));
                    Log($"{rune.Name} sans XP sur ce lot : choix écarté pour ce niveau, sans conclure à une saturation.");
                }
                observations.Add(new(item.ObjectUid, rune.Gid, report.Steps.Count, measuredXp));
                string xp = measuredXp?.ToString() ?? "?";
                _spentKamas = Budget().TotalExpense ?? 0;
                SessionText = $"Métier {progress?.Level.ToString() ?? "?"}/{targetLevel} · {used} runes · {_spentKamas}/{objective.Limit} kamas";
                Log($"{item.Name} : {report.Steps.Count} {rune.Name}, XP +{xp}, {report.Stop}.");
                if (progress?.Level >= targetLevel)
                { stop = "niveau cible atteint"; break; }
                if (measuredXp is null && report.Steps.Count > 0)
                { stop = "XP fraîche non attribuable : aucune nouvelle consommation"; break; }
                if (report.Steps.Count == 0 || report.Stop != "limite d'étapes")
                { stop = report.Stop; break; }
            }
        }
        catch (OperationCanceledException) { stop = "annulé"; }
        catch (Exception e) { stop = e.Message; }
        finally
        {
            try
            {
                launch.EndForgeHistorySession(historySession, stop);
                _spentKamas = Budget().TotalExpense ?? 0;
                Log($"Montée terminée : {stop}. {used} runes ; dépenses confirmées {_spentKamas}/{objective.Limit} kamas.");
            }
            finally
            {
                Running = false;
                _cts = null;
                RejectedWorkshopElements.Clear();
                OnPropertyChanged(nameof(CanCrush));
            }
        }
    }

    private void AddAdaptiveHistoryBatch(IReadOnlyList<LaunchService.ForgeStepReport> steps)
    {
        if (steps.Count == 0) return;
        int successes = steps.Count(s => s.Code == GivreClone.Protocol.InventoryReader.CraftSuccess);
        int neutrals = steps.Count(s => s.Code == GivreClone.Protocol.InventoryReader.CraftNeutral);
        int failures = steps.Count - successes - neutrals;
        SuccessCount += successes;
        NeutralCount += neutrals;
        FailCount += failures;
        PoolText = steps[^1].Pool?.ToString() ?? "—";
        string kind = successes == steps.Count ? "success" : neutrals == steps.Count ? "neutral"
            : failures == steps.Count ? "fail" : "info";
        AddHistory(new SmithLogRow(DateTime.Now.ToString("HH:mm:ss"),
            string.Join(", ", steps.Select(s => s.Rune).Distinct()),
            $"{steps.Count} runes | {successes} succès, {neutrals} neutres, {failures} échecs", kind));
    }
}
