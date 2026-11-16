using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    [ObservableProperty] public partial int CompareRuneGid { get; set; } = 1545;
    [ObservableProperty] public partial int CompareRuneQuantity { get; set; } = 10;
    [ObservableProperty] public partial long FusionTravelCost { get; set; } = -1;
    [ObservableProperty] public partial string RuneComparisonText { get; set; } = "Comparaison sur les offres du serveur. Trajet fusion -1 = inconnu.";
    [ObservableProperty] public partial string CrushTrialText { get; set; } = "Le premier lancement brise un seul exemplaire et mesure les runes reçues.";
    private CrushTrialPolicy? _crushTrial;
    private CrushSelection? _crushTrialSelection;
    private uint _crushTrialPid;
    private int _crushTrialBagQuantity;
    private sealed record CrushChoice(SmithCrushOwner Owner, long Gid, int Quantity, bool Explicit);
    private readonly Dictionary<long, CrushChoice> _crushChoices = [];
    private SmithCrushOwner? _crushOwner;
    private int _crushHistoryPid;
    private long _crushHistoryCursor;
    private static readonly Lazy<SmithCrushProvenanceStore> DefaultCrushProvenance = new(() =>
        new(Path.Combine(MZP19LocalClone.DataRoot, "forge", "crush-provenance.json")));
    private SmithCrushProvenanceStore? _crushProvenance;
    public SmithCrushProvenanceStore CrushProvenance
    {
        get => _crushProvenance ??= DefaultCrushProvenance.Value;
        set => _crushProvenance = value;
    }
    public Func<uint, SmithCrushOwner?> CrushOwnerOf { get; set; } = pid =>
    {
        var launch = Stores.Launch;
        if (!launch.ForgeHistory.IsReady) return null;
        var owner = new SmithCrushOwner(launch.ScriptAccounts.FirstOrDefault(a => a.Pid == pid)?.Id ?? "",
            launch.ServerIdOf(pid), launch.CharacterId(pid));
        return SmithCrushProvenanceStore.ValidOwner(owner) ? owner : null;
    };
    public Func<uint, string, long> CrushHistoryHeadOf { get; set; } =
        (pid, account) => Stores.Launch.ForgeHistory.ReadRecent(pid, account).Cursor;
    public Func<uint, string, long, ForgeHistoryPage> CrushHistorySince { get; set; } =
        (pid, account, cursor) => Stores.Launch.ForgeHistory.Read(pid, account, cursor, 2000);
    public Func<uint, long, int> CrushQuantityOf { get; set; } = (pid, uid) =>
        Stores.Launch.Inventory.Items(pid).Where(i => i.Uid == uid && i.Position == 63).Sum(i => i.Qty);
    public Func<uint, long, long?> CrushGidOf { get; set; } = (pid, uid) =>
        Stores.Launch.Inventory.Items(pid).Where(i => i.Uid == uid && i.Position == 63)
            .Select(i => (long?)i.Gid).FirstOrDefault();
    public bool CanContinueCrushTrial => !Running && _crushTrialPid == (uint)_pid
        && _crushTrial?.MeasuredYield is not null && _crushTrialSelection is { Quantity: > 1 };

    private void RefreshCrushProvenance()
    {
        var owner = _pid > 0 ? CrushOwnerOf((uint)_pid) : null;
        if (_crushOwner is { } previous && (previous != owner || _crushHistoryPid != _pid))
            CrushProvenance.Reconcile(previous, _ => (null, 0));
        if (owner is null)
        {
            _crushOwner = null;
            CrushProvenanceNote = "Provenance inconnue : identité du personnage indisponible.";
            return;
        }
        long head = CrushHistoryHeadOf((uint)_pid, owner.AccountId);
        if (_crushOwner != owner || _crushHistoryPid != _pid)
        {
            _crushOwner = owner;
            _crushHistoryPid = _pid;
            // Prior events prove a placement, not that its unit is still owned now.
            _crushHistoryCursor = head;
        }
        if (head > _crushHistoryCursor)
        {
            var page = CrushHistorySince((uint)_pid, owner.AccountId, _crushHistoryCursor);
            if (!page.Truncated)
                foreach (var entry in page.Entries.Where(e => e.Pid == (uint)_pid && e.ItemUid is > 0))
                    CrushProvenance.Observe(owner, entry, CrushGidOf((uint)_pid, entry.ItemUid!.Value),
                        CrushQuantityOf((uint)_pid, entry.ItemUid.Value));
            _crushHistoryCursor = page.Cursor;
        }
        CrushProvenance.Reconcile(owner, uid => (CrushGidOf((uint)_pid, uid), CrushQuantityOf((uint)_pid, uid)));
        CrushProvenanceNote = CrushProvenance.StorageError ??
            "Par défaut : un exemplaire prouvé par support observé. Historique ancien, pile modifiée ou reprise : provenance inconnue, choix explicite requis.";
    }

    private bool CrushChoiceValid(SmithQueueRow row)
    {
        RefreshCrushProvenance();
        return _crushOwner is { } owner && CrushProvenance.StorageError is null
            && _crushChoices.TryGetValue(row.ObjectUid, out var choice) && choice.Owner == owner
            && choice.Gid == row.Gid && choice.Quantity == row.Qty
            && (choice.Explicit || row.Qty <= CrushProvenance.Available(owner, row.ObjectUid, row.Gid,
                CrushQuantityOf((uint)_pid, row.ObjectUid)));
    }

    [RelayCommand]
    private Task ContinueCrushTrial() => CanContinueCrushTrial ? RunCrushAsync(true) : Task.CompletedTask;

    private async Task RunCrushAsync(bool approveRemaining = false)
    {
        int pid = _pid;
        if (CrushQueue.FirstOrDefault() is not { } row) return;
        if (!CrushChoiceValid(row))
        { Log("Provenance ou sélection inconnue : sélectionner à nouveau les quantités avant brisage."); return; }
        var owner = _crushOwner!;
        var launch = Stores.Launch;
        int BagQuantity() => CrushQuantityOf((uint)pid, row.ObjectUid);
        System.Collections.Generic.Dictionary<long, int> RunesInBag() =>
            launch.Inventory.Items((uint)pid).Where(i => i.Position == 63 && launch.RuneItemsByGid().ContainsKey(i.Gid))
                .GroupBy(i => i.Gid).ToDictionary(g => g.Key, g => g.Sum(i => i.Qty));
        int beforeQuantity = BagQuantity();
        if (row.ObjectUid is <= 0 or > int.MaxValue || row.Gid <= 0 || row.Qty <= 0 ||
            beforeQuantity < row.Qty || CrushGidOf((uint)pid, row.ObjectUid) != row.Gid)
        { Log("Objet ou quantité sélectionnée absent du sac : aucun brisage."); return; }
        CrushSelection selected;
        if (approveRemaining)
        {
            if (_crushTrialPid != (uint)pid || _crushTrialSelection is not { } original ||
                row.ObjectUid != original.Uid || row.Gid != original.Gid || row.Qty != original.Quantity - 1 ||
                beforeQuantity != _crushTrialBagQuantity - 1) return;
            selected = new((int)row.ObjectUid, row.Gid, row.Qty);
        }
        else
        {
            _crushTrialSelection = new((int)row.ObjectUid, row.Gid, row.Qty);
            _crushTrial = new(_crushTrialSelection);
            _crushTrialPid = (uint)pid;
            _crushTrialBagQuantity = beforeQuantity;
            selected = _crushTrial.Trial;
        }
        using var cts = new CancellationTokenSource();
        _cts = cts;
        Running = true;
        try
        {
            var workshop = await PrepareCrusherAsync((uint)pid, cts.Token).ConfigureAwait(true);
            if (workshop is not { } ws || CrushItems is null) return;
            CheckSession((uint)pid, cts.Token);
            if (CrushQueue.FirstOrDefault() != row || BagQuantity() != beforeQuantity ||
                CrushGidOf((uint)pid, row.ObjectUid) != row.Gid || !CrushChoiceValid(row) || _crushOwner != owner)
            { Log("Sélection ou inventaire modifié : aucun brisage."); return; }
            if (approveRemaining && _crushTrial?.AuthorizeRemaining(selected, true) is null) return;
            var beforeRunes = RunesInBag();
            if (!CrushProvenance.Retire(owner, row.ObjectUid, row.Gid))
            { Log(CrushProvenance.StorageError ?? "Provenance non enregistrée : aucun brisage."); return; }
            var dispatchedChoice = _crushChoices[row.ObjectUid];
            _crushChoices.Remove(row.ObjectUid);
            var report = await CrushItems((uint)pid, ws.ElementId, ws.SkillUid,
                new[] { (selected.Uid, selected.Quantity) }, cts.Token).ConfigureAwait(true);
            CheckSession((uint)pid, cts.Token);
            if (!report.Opened) { Log("Concasseur non ouvert : aucun objet cassé."); return; }
            int remaining = BagQuantity();
            int consumed = beforeQuantity - remaining;
            bool confirmed = report.Crushed == 1 && report.Failed.Count == 0 && consumed == selected.Quantity;
            if (!approveRemaining && _crushTrial is { } trial)
            {
                var afterRunes = RunesInBag();
                var yield = afterRunes.Where(r => r.Value > beforeRunes.GetValueOrDefault(r.Key))
                    .ToDictionary(r => r.Key, r => r.Value - beforeRunes.GetValueOrDefault(r.Key));
                if (trial.RecordTrial(selected.Uid, consumed, confirmed, yield,
                    gid => launch.AveragePrice((uint)pid, (int)gid) is > 0 and var p ? p : null))
                {
                    CrushTrialText = "Essai confirmé : " + string.Join(", ", yield.Select(r =>
                        $"{launch.GameNames.ItemName((int)r.Key) ?? r.Key.ToString()} x{r.Value}"))
                        + $". Valeur moyenne : {trial.MeasuredYield?.KnownValue?.ToString() ?? "inconnue"} kamas."
                        + " Le reste ne sera brisé qu'avec le bouton de confirmation.";
                }
                else CrushTrialText = "Résultat ou consommation non confirmés : aucun lot supplémentaire autorisé.";
                Log(CrushTrialText);
            }
            if (confirmed)
            {
                int index = CrushQueue.IndexOf(row);
                if (index >= 0)
                {
                    CrushQueue.RemoveAt(index);
                    int selectedRemaining = row.Qty - selected.Quantity;
                    if (selectedRemaining > 0)
                    {
                        _crushChoices[row.ObjectUid] = dispatchedChoice with { Quantity = selectedRemaining };
                        CrushQueue.Insert(index, row with { Qty = selectedRemaining, Info = "Quantité : " + selectedRemaining });
                    }
                    else _crushChoices.Remove(row.ObjectUid);
                }
            }
            else _crushChoices.Remove(row.ObjectUid);
            RefreshPickBlocks();
        }
        catch (OperationCanceledException) { Log("Brisage annulé."); }
        catch (Exception e) { Log("Erreur de brisage : " + e.Message); }
        finally
        {
            Running = false;
            _cts = null;
            if (approveRemaining) { _crushTrial = null; _crushTrialSelection = null; }
            OnPropertyChanged(nameof(CanCrush));
            OnPropertyChanged(nameof(CanContinueCrushTrial));
        }
    }

    [RelayCommand]
    private async Task CompareRuneCosts()
    {
        if (_pid <= 0 || Running || CompareRuneQuantity is < 1 or > 10000) return;
        try
        {
            var comparison = await Stores.Launch.CompareRuneAcquisitionAsync((uint)_pid, CompareRuneGid,
                CompareRuneQuantity, FusionTravelCost < 0 ? null : FusionTravelCost, CancellationToken.None);
            RuneComparisonText = comparison.Routes.Count == 0 ? "Aucune route chiffrable avec les offres reçues."
                : string.Join(Environment.NewLine, comparison.Routes.OrderBy(r => r.TotalCost).Select(r =>
                    $"{r.SourceGrade} : {r.TotalCost} kamas, {r.PurchasedInputs} runes achetées, {r.FusionOperations} fusion(s)."))
                    + Environment.NewLine + "Devis uniquement : stock et prix à revalider avant achat. Aucune fusion exécutée.";
        }
        catch (Exception e) { RuneComparisonText = e.Message; }
    }
}
