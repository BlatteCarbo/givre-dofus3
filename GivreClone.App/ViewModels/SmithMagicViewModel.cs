using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class SmithLineRow : ObservableObject
{
    public int EffectId { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] public partial int NaturalMaximum { get; set; }
    [ObservableProperty] public partial int Current { get; set; }
    [ObservableProperty] public partial int Min { get; set; }
    [ObservableProperty] public partial int Max { get; set; }
    [ObservableProperty] public partial int PaFrom { get; set; }
    [ObservableProperty] public partial int RaFrom { get; set; }
    public bool IsExo => NaturalMaximum == 0;
    public bool Reached => Current >= Min;
    /// <summary>Avancement vers l'objectif (0-100) pour la barre de la ligne.</summary>
    public double Progress => Min <= 0 ? 100 : Math.Clamp(Current * 100.0 / Min, 0, 100);
    public bool IsOver => NaturalMaximum > 0 && Current > NaturalMaximum;
    partial void OnCurrentChanged(int value) { OnPropertyChanged(nameof(Reached)); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(IsOver)); }
    partial void OnMinChanged(int value) { OnPropertyChanged(nameof(Reached)); OnPropertyChanged(nameof(Progress)); }
    partial void OnNaturalMaximumChanged(int value) { OnPropertyChanged(nameof(IsExo)); OnPropertyChanged(nameof(IsOver)); }
    public SmithTarget ToTarget() => new(EffectId, Current, Min, NaturalMaximum, PaFrom, RaFrom);
}

public sealed record SmithQueueRow(long ObjectUid, string Name, string Info, long Gid = 0, int Qty = 1);

/// <summary>Une ligne de l'historique de l'atelier : rune passée, issue et lignes modifiées (« +1 Chance », « -1 Agilité »).
/// Kind : success / neutral / fail / info -> couleur de la ligne.</summary>
public sealed record SmithLogRow(string Time, string Rune, string Result, string Kind)
{
    public bool IsSuccess => Kind == "success";
    public bool IsNeutral => Kind == "neutral";
    public bool IsFail => Kind == "fail";
    public bool IsInfo => Kind == "info";
}

/// <summary>Onglet Forgemagie : file d'objets du sac, objectifs par ligne (lus sur l'objet réel), runes du sac par palier, et exécution
/// à l'atelier de la carte (<see cref="LaunchService.ForgeAsync"/>) avec journal de chaque rune. S'il y a des étapes conditions,
/// Démarrer (Automatique, objectifs) suit <see cref="ForgeEngine.DecideConditionSteps"/> ; sinon <see cref="SmithMagicPlanner"/>.
/// ⚠ Séquence d'atelier pas encore confirmée par une capture en jeu.</summary>
public partial class SmithMagicViewModel : ViewModelBase
{
    public ObservableCollection<SmithLineRow> Lines { get; } = [];
    public ObservableCollection<SmithQueueRow> Queue { get; } = [];
    public ObservableCollection<SmithQueueRow> BagChoices { get; } = [];
    public ObservableCollection<SmithRune> Runes { get; } = [];
    public ObservableCollection<string> SmithConsole { get; } = [];
    [ObservableProperty] public partial bool ShowMain { get; set; } = true;
    [ObservableProperty] public partial bool ShowSettings { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Ajoute un objet du sac à la file, fixe les objectifs puis lance le trajet automatique vers un atelier compatible.";
    [ObservableProperty] public partial int BagIndex { get; set; } = -1;
    [ObservableProperty] public partial bool AllowSmallerRune { get; set; } = true;
    [ObservableProperty] public partial int MaxRunes { get; set; } = 100;
    [ObservableProperty] public partial int MaxRunesPerLine { get; set; } = 50;
    [ObservableProperty] public partial long MaxKamas { get; set; } = long.MaxValue;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanApply), nameof(CanCrush), nameof(Busy), nameof(CanStop))] public partial bool Running { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanApply), nameof(CanCrush), nameof(Busy), nameof(CanStop))] public partial bool Starting { get; set; }
    public bool Busy => Running || Starting;
    public bool CanStop => Busy || AdviceActive;
    [ObservableProperty] public partial string SessionText { get; set; } = "Aucune session de forgemagie.";
    // ---- Atelier : historique coloré, compteurs et reliquat de la session en cours ----
    public ObservableCollection<SmithLogRow> History { get; } = [];
    public bool HasHistory => History.Count > 0;
    [ObservableProperty] public partial int SuccessCount { get; set; }
    [ObservableProperty] public partial int NeutralCount { get; set; }
    [ObservableProperty] public partial int FailCount { get; set; }
    [ObservableProperty] public partial string PoolText { get; set; } = "—";
    [ObservableProperty] public partial string LoadedItemName { get; set; } = "";
    public bool HasLoadedItem => LoadedItemName.Length > 0;
    partial void OnLoadedItemNameChanged(string value) => OnPropertyChanged(nameof(HasLoadedItem));
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ClearHistory() { History.Clear(); SuccessCount = NeutralCount = FailCount = 0; OnPropertyChanged(nameof(HasHistory)); }
    private void AddHistory(SmithLogRow row)
    {
        History.Insert(0, row);   // plus récent en haut
        while (History.Count > 50) History.RemoveAt(History.Count - 1);
        OnPropertyChanged(nameof(HasHistory));
    }
    public bool CanStart => !Busy && (IsAdviceMode ? _pid > 0 && _loadedUid > 0 : (HasQueue || (Leveling && AdaptiveLeveling)) && _pid > 0 && (!Leveling || LevelingReady));
    public bool CanApply => !IsAdviceMode && !Busy && !Leveling && _pid > 0 && _loadedUid > 0;
    public bool HasQueue => Queue.Count > 0;
    public bool HasItem => Lines.Count > 0;
    // ---- Achat des runes manquantes (droit Hôtel de Vente) ----
    [ObservableProperty] public partial bool AutoBuyRunes { get; set; } = true;   // runes absentes : le bot va les acheter (budget et plafond ci-dessous)
    [ObservableProperty] public partial int BuyMaxPercent { get; set; } = 120;   // plafond par lot : % du prix moyen du serveur
    [ObservableProperty] public partial long BuyMaxKamas { get; set; } = 500_000;  // budget total d'une session
    /// <summary>Achète un plan de runes (HDV le plus proche, retour à l'atelier) ; fourni par BotsViewModel.</summary>
    /// <remarks>Reçoit le besoin et un planificateur : le plan est calculé à l'HDV, avec le prix lu sur place si la moyenne est inconnue.</remarks>
    public Func<uint, IReadOnlyList<(long Gid, int Quantity)>, Func<Func<long, long>, IReadOnlyList<RunePurchase>>, CancellationToken, Task<(int Bought, string Report)>>? BuyRunes { get; set; }
    public Func<uint, IReadOnlyList<(long Gid, int Quantity)>, Func<Func<long, long>, Func<long, int, long>, IReadOnlyList<RunePurchase>>, CancellationToken, Task<(int Bought, string Report)>>? BuyRunesAtLotPrices { get; set; }
    /// <summary>Signale un évènement de forgemagie (exo, over, objet terminé) : notification + webhook du perso ; fourni par BotsViewModel.</summary>
    public Action<uint, string>? Notify { get; set; }

    // Objectifs PAR OBJET (uid) : chaque objet de la file garde ses lignes.
    private readonly Dictionary<long, List<SmithLineRow>> _linesByItem = new();
    private long _loadedUid;
    private CancellationTokenSource? _cts;

    public SmithMagicViewModel()
    {
        InitializeAdvice();
        InitializeForgeHistory();
        InitializeConditionSteps();
        Queue.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasQueue)); OnPropertyChanged(nameof(CanStart)); };
        Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItem));
    }

    [RelayCommand] private void ShowMainView() { ShowMain = true; ShowSettings = false; }
    [RelayCommand] private void ShowSettingsView() { ShowMain = false; ShowSettings = true; }
    [RelayCommand] private void AddToQueue()
    {
        if (BagIndex < 0 || BagIndex >= BagChoices.Count) return;
        var item = BagChoices[BagIndex];
        if (item.ObjectUid <= 0 || Queue.Any(q => q.ObjectUid == item.ObjectUid)) return;
        if (!ItemBindingRules.CanForge(BindingOf((uint)_pid, item.ObjectUid))) { Log(SmithJournal.Skip(item.Name, SmithAdmission.Bound, null, null)); return; }
        Queue.Add(item);
        if (_loadedUid == 0) LoadItem(item);
        RefreshJobChoices();
        RefreshPickBlocks();
    }
    [RelayCommand] private void RemoveFromQueue(SmithQueueRow? item)
    {
        if (item is null) return;
        bool loaded = _loadedUid == item.ObjectUid;
        Queue.Remove(item); _linesByItem.Remove(item.ObjectUid);
        ForgetConditionItem(item.ObjectUid, loaded);
        if (loaded) { _loadedUid = 0; BindAdviceIdentity(itemChanged: true); Lines.Clear(); }
        RefreshJobChoices();
        RefreshPickBlocks();
    }
    [RelayCommand] private void RemoveLine(SmithLineRow? line)
    { if (line is not null) { Lines.Remove(line); SaveLines(); } }
    [RelayCommand] private void PerfectRoll()
    { foreach (var line in Lines) { line.Min = line.NaturalMaximum; line.Max = line.NaturalMaximum; } }

    /// <summary>Charge les lignes de l'objet : effets RÉELS lus sur l'objet du sac ; lignes runables seulement (famille de rune connue).
    /// Objectif par défaut = valeur actuelle (à monter à la main ou avec « jet parfait » une fois le maximum saisi).</summary>
    [RelayCommand]
    private void LoadItem(SmithQueueRow? item)
    {
        if (item is null || _pid <= 0) return;
        SaveLines();
        SaveConditionSteps();
        _loadedUid = item.ObjectUid;
        LoadConditionSteps();
        BindAdviceIdentity(itemChanged: true);
        LoadedItemName = item.Name;
        OnPropertyChanged(nameof(CanApply));
        Lines.Clear();
        if (_linesByItem.TryGetValue(item.ObjectUid, out var saved)) { foreach (var l in saved) Lines.Add(l); }
        else
        {
            foreach (var (id, value) in ForgeState.ById(Stores.Launch.ItemEffects((uint)_pid, (int)item.ObjectUid)))
                if (RuneCatalog.Of(id) is { } f)
                    Lines.Add(new SmithLineRow { EffectId = id, Name = f.Label, NaturalMaximum = Math.Max(value, 0), Current = value, Min = value, Max = value });
            SaveLines();
        }
        ApplyNaturalMaxima(item.Gid, item.ObjectUid);
        Status = Lines.Count == 0 ? $"{item.Name} : aucune ligne runable lue (effets inconnus ou objet absent du sac)." : $"{item.Name} : {Lines.Count} ligne(s) chargée(s). Fixe les objectifs.";
        RefreshRunes();
    }

    private void SaveLines() { if (_loadedUid > 0) _linesByItem[_loadedUid] = Lines.ToList(); }

    /// <summary>Remplace le maximum naturel de chaque ligne par celui du GABARIT de l'objet (jets théoriques). Sans ça il valait le jet
    /// courant : « jet parfait » visait déjà la valeur en place (file sautée, aucune rune passée) et tout gain réel passait pour un over.
    /// Gabarit inconnu (jamais consulté, réseau indisponible) : les lignes gardent leur repli et la session reste possible à la main.</summary>
    private void ApplyNaturalMaxima(long gid, long uid)
    {
        if (gid <= 0) return;
        if (ItemEffectRanges.TryGet(gid) is { } known) { Apply(known); return; }
        _ = Task.Run(async () =>
        {
            if (await ItemEffectRanges.LoadAsync(gid).ConfigureAwait(false) is not { } fetched) return;
            Dispatcher.UIThread.Post(() => { if (_loadedUid == uid) Apply(fetched); });
        });

        void Apply(IReadOnlyDictionary<int, (int From, int To)> ranges)
        {
            bool changed = false;
            foreach (var line in Lines)
                if (ranges.TryGetValue(line.EffectId, out var r) && r.To > 0 && line.NaturalMaximum != r.To)
                { line.NaturalMaximum = r.To; if (line.Max < r.To) line.Max = r.To; changed = true; }
            if (changed) SaveLines();
        }
    }

    /// <summary>Runes du sac (nom « Rune [Pa|Ra] Code » -> famille et palier), avec leur quantité.</summary>
    [RelayCommand]
    private void RefreshRunes()
    {
        Runes.Clear();
        if (_pid <= 0 || RuneItems() is not { Count: > 0 } map) { BankRuneNote = ""; return; }
        bool known = Stores.Launch.Bank.IsSeeded((uint)_pid);
        BankRuneNote = known ? "" : "Banque pas encore lue : seule la quantité du sac est comptée.";
        var bag = Stores.Launch.Inventory.Items((uint)_pid).Where(i => i.Position == 63 && i.Qty > 0).GroupBy(i => i.Gid).ToDictionary(g => g.Key, g => g.Sum(i => i.Qty));
        var bank = known
            ? Stores.Launch.Bank.Items((uint)_pid).Where(i => i.Qty > 0).GroupBy(i => i.Gid).ToDictionary(g => g.Key, g => g.Sum(i => i.Qty))
            : new Dictionary<long, int>();
        foreach (var gid in bag.Keys.Union(bank.Keys).OrderBy(id => id))
        {
            if (!map.TryGetValue(gid, out var r)) continue;
            int bagQty = bag.GetValueOrDefault(gid);
            int bankQty = known ? bank.GetValueOrDefault(gid) : 0;
            int visible = RuneStock.Visible(bagQty, bankQty, known);
            if (visible <= 0) continue;
            long price = Stores.Launch.AveragePrice((uint)_pid, (int)gid) is > 0 and var avg ? avg : (MaxKamas == long.MaxValue ? 0 : -1);
            var tier = r.Tier switch { "Pa" => SmithRuneTier.Pa, "Ra" => SmithRuneTier.Ra, _ => SmithRuneTier.Basic };
            Runes.Add(new SmithRune((int)gid, r.EffectId, tier, visible, price, bagQty, bankQty));
        }
    }

    private static IReadOnlyDictionary<long, (int EffectId, string Tier, string Name)>? RuneItems()
        => Stores.Launch.RuneItemsByGid() is { Count: > 0 } m ? m : null;

    public SmithMagicDecision Suggest(SmithLineRow line, int usedTotal = 0, int usedOnLine = 0, long spent = 0)
        => SmithMagicPlanner.Next(Threshold(line), Runes, new(MaxRunes, MaxRunesPerLine, MaxKamas, AllowSmallerRune), usedTotal, usedOnLine, spent);

    // Seuils de palier : ceux saisis (> 0), sinon la plus grosse rune qui ne dépasse pas l'écart restant.
    private static SmithTarget Threshold(SmithLineRow line)
    {
        if (line.PaFrom > 0 || line.RaFrom > 0 || RuneCatalog.Of(line.EffectId) is not { } f) return line.ToTarget();
        string tier = RuneCatalog.TierFor(f, line.Current, line.Min, line.Current, line.Current);
        int never = int.MaxValue;
        return new(line.EffectId, line.Current, line.Min, line.NaturalMaximum, tier == "Ba" ? never : line.Current, tier == "Ra" ? line.Current : never);
    }

    private long _stopVersion;
    private CancellationTokenSource? _historyWait;
    public Func<Task> ForgeHistoryReady { get; set; } = () => Stores.Launch.ForgeHistory.Ready;
    [RelayCommand] private void Stop() { StopAdvice(); _stopVersion++; _historyWait?.Cancel(); try { _cts?.Cancel(); } catch { } }

    [RelayCommand]
    private async Task Start()
    {
        if (IsAdviceMode) { if (CanStart) StartAdvice(); return; }
        if (!CanStart || !await WaitForForgeHistoryAsync()) return;
        if (CanStart) await (Leveling ? RunLevelingAsync() : RunAsync(Queue.ToList(), MaxRunes, AutoBuyRunes));
    }

    private async Task<bool> WaitForForgeHistoryAsync()
    {
        if (Busy) return false;
        int pid = _pid;
        long stopVersion = _stopVersion;
        using var waiting = new CancellationTokenSource();
        _historyWait = waiting;
        Starting = true;
        try { await ForgeHistoryReady().WaitAsync(waiting.Token); }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested) { return false; }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log("Archive FM illisible : " + e.Message);
            return false;
        }
        finally { _historyWait = null; Starting = false; }
        return pid == _pid && stopVersion == _stopVersion;
    }

    /// <summary>Passe UNE rune (la conseillée) sur l'objet chargé, à la main : même choix que le mode automatique, sans achat.</summary>
    [RelayCommand]
    private async Task ApplyOne()
    {
        if (IsAdviceMode) return;
        if (!await WaitForForgeHistoryAsync()) return;
        if (Running || _pid <= 0 || Queue.FirstOrDefault(q => q.ObjectUid == _loadedUid) is not { } item) { Status = "Charge d'abord les objectifs d'un objet de la file."; return; }
        await RunAsync(new[] { item }, 1, autoBuy: false);
    }

    private async Task RunAsync(IReadOnlyList<SmithQueueRow> items, int maxSteps, bool autoBuy)
    {
        SaveLines();
        int pid = _pid;
        var cts = new CancellationTokenSource(); _cts = cts;
        Running = true;
        var historySessionId = Stores.Launch.BeginForgeHistorySession((uint)pid,
            Stores.Launch.ScriptAccounts.FirstOrDefault(a => a.Pid == (uint)pid)?.Id ?? "", source: "ui-objectives");
        string historyStop = "aucun passage";
        int done = 0;
        SmithQueueRow? retry = null;
        try
        {
            _buyRounds = 0; _spentKamas = 0;
            var budget = new SmithMagicSessionBudget();
            int conditionUsed = 0;
            long conditionSpent = 0;
            var conditionLines = new Dictionary<int, int>();
            var pending = new Queue<SmithQueueRow>(items);
            while (pending.Count > 0 || retry is not null)
            {
                var item = retry ?? pending.Dequeue();
                retry = null;
                CheckSession((uint)pid, cts.Token);
                var admission = AdmitItem(item.ObjectUid, item.Gid, null);
                if (admission != SmithAdmission.Forge)
                { Log(SmithJournal.Skip(item.Name, admission, item.Gid > 0 && JobForItem(item.Gid) is int job ? JobLevelOf((uint)pid, job) : null, ItemLevelOf(item.Gid))); continue; }
                var rules = CopyConditionRules(item.ObjectUid);
                bool conditional = rules.Count > 0;
                if (!_linesByItem.TryGetValue(item.ObjectUid, out var lines)) lines = [];
                lines ??= [];
                if (!conditional && (lines.Count == 0 || lines.All(l => l.Reached)))
                { Log($"{item.Name} : aucun objectif à atteindre, objet passé."); continue; }
                if (!StillInBag((uint)pid, item.ObjectUid)) { Log($"{item.Name} n'est plus dans le sac : retiré de la file."); Queue.Remove(item); continue; }
                var workshop = await PrepareWorkshopAsync((uint)pid, item.Gid, cts.Token).ConfigureAwait(true);
                if (workshop is not { } ws) { historyStop = "atelier indisponible"; break; }
                CheckSession((uint)pid, cts.Token);
                long workshopMap = CurrentMap?.Invoke((uint)pid) ?? 0;
                LoadItem(item);
                rules = CopyConditionRules(item.ObjectUid);
                conditional = rules.Count > 0;
                if (_linesByItem.TryGetValue(item.ObjectUid, out var refreshed) && refreshed is not null) lines = refreshed;
                Log(conditional
                    ? $"— {item.Name} : début ({rules.Count} étape(s) condition)."
                    : $"— {item.Name} : début ({lines.Count(l => !l.Reached)} ligne(s) à monter).");
                // Décision HORS du fil d'interface : copies des lignes et du stock (jamais les collections liées à l'écran).
                var targets = lines.Select(l => (l.EffectId, l.Min, l.NaturalMaximum, l.PaFrom, l.RaFrom, Current: l.Current)).ToList();
                var stock = Runes.ToList();
                var limits = new SmithMagicLimits(MaxRunes, MaxRunesPerLine, MaxKamas, AllowSmallerRune);
                ForgeAction Decide(IReadOnlyDictionary<string, int> stats, int? pool)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (RuneCatalog.Of(targets[i].EffectId) is not { } f) continue;
                        if (stats.TryGetValue(f.Code, out var value)) targets[i] = targets[i] with { Current = value };
                        else if (!conditional) targets[i] = targets[i] with { Current = 0 };
                    }
                    var snapshot = targets.ToList();
                    Dispatcher.UIThread.Post(() => { if (cts.IsCancellationRequested || _pid != pid) return; foreach (var t in snapshot) if (lines.FirstOrDefault(l => l.EffectId == t.EffectId) is { } row) row.Current = t.Current; });
                    if (cts.IsCancellationRequested || _pid != pid) return ForgeAction.Finish;
                    if (conditional)
                    {
                        // DecideConditions ne porte pas le motif d'arrêt : DecideConditionSteps est la même décision.
                        var outcome = ForgeEngine.DecideConditionSteps(stats, pool, rules);
                        if (outcome.Halt is { Length: > 0 } reason) throw new ForgeConditionHaltException(reason);
                        var charge = SmithConditionBudget.Account(outcome.Action, stock, RuneItems(), limits, budget.UsedTotal + conditionUsed, budget.Spent + conditionSpent, conditionLines);
                        if (charge.Halt is { Length: > 0 } budgetReason) throw new ForgeConditionHaltException(budgetReason);
                        conditionUsed += charge.AddedRunes;
                        conditionSpent += charge.AddedKamas;
                        return outcome.Action;
                    }
                    var stepLimits = conditionUsed == 0 && conditionSpent == 0
                        ? limits
                        : new SmithMagicLimits(
                            conditionUsed >= limits.MaxRunes ? 0 : limits.MaxRunes - conditionUsed,
                            limits.MaxRunesPerLine,
                            conditionSpent >= limits.MaxKamas ? 0 : limits.MaxKamas - conditionSpent,
                            limits.AllowSmallerRune);
                    foreach (var t in targets.Where(t => t.Current < t.Min).OrderByDescending(t => (double)(t.Min - t.Current) / Math.Max(1, t.Min)))
                    {
                        var row = new SmithLineRow { EffectId = t.EffectId, NaturalMaximum = t.NaturalMaximum, Current = t.Current, Min = t.Min, PaFrom = t.PaFrom, RaFrom = t.RaFrom };
                        var d = budget.ReserveNext(item.ObjectUid, Threshold(row), stock, stepLimits);
                        if (!d.Ready || d.Rune is not { } rune || RuneItems()?.GetValueOrDefault(rune.ItemId).Name is not { Length: > 0 } name) continue;
                        int k = stock.IndexOf(rune);
                        if (k >= 0) stock[k] = rune with { Quantity = rune.Quantity - 1 };
                        return ForgeAction.Use(name);
                    }
                    return targets.All(t => t.Current >= t.Min) ? ForgeAction.Finish : ForgeAction.Skip;
                }
                // Exo (ligne absente de l'objet, maximum naturel 0) qui apparaît ; over (au-delà du maximum naturel) qui se produit.
                var prev = lines.ToDictionary(l => l.EffectId, l => l.Current);
                long started = Environment.TickCount64, kamasAtStart = Stores.Launch.Kamas((uint)pid);
                int passed = 0;
                void OnStep(LaunchService.ForgeStepReport r) => Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested || _pid != pid) return;
                    passed++;
                    Log($"{r.Rune} → {Verdict(r.Code)}{(r.Pool is { } p ? $" · puits {p}" : "")}");
                    // Historique façon atelier du jeu : ce qui a bougé sur chaque ligne (« +1 Chance », « -1 Agilité »), sinon l'issue.
                    var deltas = lines.Where(l => RuneCatalog.Of(l.EffectId) is not null)
                        .Select(l => (l.Name, d: r.Stats.GetValueOrDefault(RuneCatalog.Of(l.EffectId)!.Code) - prev.GetValueOrDefault(l.EffectId)))
                        .Where(x => x.d != 0).Select(x => $"{(x.d > 0 ? "+" : "")}{x.d} {x.Name}").ToList();
                    string kind = r.Code switch { InventoryReader.CraftSuccess => "success", InventoryReader.CraftNeutral => "neutral", InventoryReader.CraftFailed => "fail", _ => "info" };
                    if (kind == "success") SuccessCount++; else if (kind == "neutral") NeutralCount++; else if (kind == "fail") FailCount++;
                    PoolText = r.Pool?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "—";
                    AddHistory(new SmithLogRow(DateTime.Now.ToString("HH:mm:ss"), r.Rune, deltas.Count > 0 ? string.Join(", ", deltas) : Verdict(r.Code), kind));
                    foreach (var l in lines)
                    {
                        if (RuneCatalog.Of(l.EffectId) is not { } f) continue;
                        int v = r.Stats.GetValueOrDefault(f.Code), before = prev.GetValueOrDefault(l.EffectId);
                        l.Current = v;
                        if (l.IsExo && before <= 0 && v > 0) Notify?.Invoke((uint)pid, $"Exo {l.Name} +{v} réussi sur {item.Name} ({r.Rune}).");
                        else if (!l.IsExo && v > l.NaturalMaximum && before <= l.NaturalMaximum) Notify?.Invoke((uint)pid, $"Over {l.Name} {v} (max {l.NaturalMaximum}) sur {item.Name} ({r.Rune}).");
                        prev[l.EffectId] = v;
                    }
                    RefreshRunes();
                });
                var report = await Task.Run(() => Stores.Launch.ForgeAsync((uint)pid, ws.ElementId, ws.SkillUid, (int)item.ObjectUid,
                    Decide, Math.Clamp(maxSteps, 1, 1000), cts.Token, OnStep, historySessionId)).ConfigureAwait(true);
                historyStop = report.Stop;
                CheckSession((uint)pid, cts.Token);
                Log($"{item.Name} : arrêt « {report.Stop} » après {report.Steps.Count} rune(s).");
                if (report.Stop == "terminé")
                {
                    done++; Queue.Remove(item);
                    long kamasNow = Stores.Launch.Kamas((uint)pid);
                    string spent = kamasAtStart >= 0 && kamasNow >= 0 && kamasAtStart > kamasNow ? $", {kamasAtStart - kamasNow:N0} kamas" : "";
                    Notify?.Invoke((uint)pid, $"{item.Name} : objectifs atteints en {passed} rune(s){spent}, {(Environment.TickCount64 - started) / 60000} min {(Environment.TickCount64 - started) / 1000 % 60:00} s.");
                    continue;
                }
                if (conditional && report.Stop == "étape sautée")
                {
                    Log($"{item.Name} : étape sautée, objet laissé dans la file.");
                    continue;
                }
                bool missing = conditional
                    ? report.Stop.StartsWith("rune absente du sac : ", StringComparison.Ordinal)
                    : report.Stop.StartsWith("rune", StringComparison.Ordinal) || report.Stop == "étape sautée";
                if (!missing && !(report.Stop == "limite d'étapes" && maxSteps == 1)) break;   // atelier fermé, délai, permission… : on ne passe pas à l'objet suivant à l'aveugle
                if (report.Stop == "limite d'étapes" && maxSteps == 1) continue;   // passage manuel d'une rune : rien d'autre à faire
                if (!autoBuy || BuyRunes is null || _buyRounds >= 2) { Log("Runes manquantes pour continuer : rachète-les (ou coche l'achat automatique) puis relance."); continue; }
                _buyRounds++;
                var needs = (conditional ? MissingNamedRune(report.Stop) : MissingRunes(lines)).ToList();
                long budgetLeft = Math.Max(0, BuyMaxKamas - _spentKamas);
                Log($"Achat des runes manquantes : {needs.Sum(n => n.Quantity)} rune(s) à l'HDV des runes le plus proche (budget {budgetLeft:N0} kamas).");
                long kamasBefore = Stores.Launch.Kamas((uint)pid);
                var (bought, buyReport) = await BuyRunes((uint)pid, needs, price => RunePurchasePlan.Plan(needs, price, BuyMaxPercent, budgetLeft), cts.Token).ConfigureAwait(true);
                long kamasAfter = Stores.Launch.Kamas((uint)pid);
                if (kamasBefore >= 0 && kamasAfter >= 0 && kamasBefore > kamasAfter)
                {
                    long debit = kamasBefore - kamasAfter;
                    _spentKamas += debit;
                    Stores.Launch.RecordForgeCashExpense(historySessionId, debit, "achat runes");
                }
                CheckSession((uint)pid, cts.Token);
                if (bought > 0 && (CurrentMap?.Invoke((uint)pid) != workshopMap || RequiredSkill(item.Gid) is not { } returnSkill || CurrentWorkshop((uint)pid, returnSkill) is null))
                { Log("Retour à l'atelier non confirmé : aucune rune supplémentaire passée."); break; }
                Log($"Achat : {buyReport}.");
                RefreshRunes();
                if (bought > 0) retry = item;   // même objet, avec les runes achetées
                else break;
            }
        }
        catch (ForgeConditionHaltException halt) { historyStop = halt.Message; if (_pid == pid) Log(halt.Message); }
        catch (OperationCanceledException) { historyStop = "annulé"; if (_pid == pid) Log("Session annulée."); }
        catch (Exception e) { historyStop = "erreur"; if (_pid == pid) Log("Erreur : " + e.Message); }
        finally
        {
            Stores.Launch.EndForgeHistorySession(historySessionId, historyStop);
            Running = false; _cts = null; cts.Dispose();
            SessionText = $"Dernière session : {done} objet(s) terminé(s).";
        }
    }

    private int _buyRounds;
    private long _spentKamas;

    /// <summary>Runes à acheter pour finir les lignes : palier conseillé de chaque ligne, passages doublés, stock du sac déduit.</summary>
    private IEnumerable<(long Gid, int Quantity)> MissingRunes(IEnumerable<SmithLineRow> lines)
    {
        var map = RuneItems();
        if (map is null) yield break;
        var gidByName = map.ToDictionary(kv => kv.Value.Name, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines.Where(l => !l.Reached))
        {
            if (RuneCatalog.Of(l.EffectId) is not { } f) continue;
            string tier = RuneCatalog.TierFor(f, l.Current, l.Min, l.PaFrom > 0 ? l.PaFrom : l.Current, l.RaFrom > 0 ? l.RaFrom : l.Current);
            if (!gidByName.TryGetValue(RuneCatalog.ItemName(f, tier), out var gid)) continue;
            int have = Runes.Where(r => r.ItemId == gid).Sum(r => r.Quantity);
            int need = RunePurchasePlan.Needed(l.Min - l.Current, RuneCatalog.Amount(f, tier)) - have;
            if (need > 0) yield return (gid, need);
        }
    }

    private static string Verdict(int code) => code switch
    {
        InventoryReader.CraftSuccess => "succès", InventoryReader.CraftNeutral => "neutre", InventoryReader.CraftFailed => "échec",
        _ => $"code {code}",
    };

    private void Log(string line)
    {
        Status = line;
        if (SmithConsole.Count >= 300) SmithConsole.RemoveAt(0);
        SmithConsole.Add($"{DateTime.Now:HH:mm:ss}  {line}");
    }
}
