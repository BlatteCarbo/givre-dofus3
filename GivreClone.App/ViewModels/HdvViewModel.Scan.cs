using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.ViewModels;

public partial class HdvViewModel
{
    public string ScanDatasetPath { get; set; } = "";
    private CancellationTokenSource? _scanCts;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    private async Task RunLocalCraftScanAsync()
    {
        if (ScanBusy) { CancelScan(); return; }
        ScanRows.Clear();
        ScanNoMatchText = "";
        if (!IsScanCraft) { ScanStatusText = "Revente indisponible : aucun relevé multi-objets local comparable."; return; }
        if (ScanLevelMin < 1 || ScanLevelMax < ScanLevelMin || ScanQty < 1 || ScanQty > 1000)
        { ScanStatusText = "Niveaux ou quantité de scan invalides."; return; }
        uint pid = _pid;
        string? character = _charName;
        int server = _launch.ServerIdOf(pid);
        if (pid == 0 || (ScanServer is { Id: > 0 } selected && selected.Id != server))
        { ScanStatusText = "Sélectionne le personnage/serveur dont les prix locaux sont disponibles."; return; }
        string? path = string.IsNullOrWhiteSpace(ScanDatasetPath) ? _launch.GameData?.Path : ScanDatasetPath.Trim();
        if (path is null) { ScanStatusText = "Base de recettes locale introuvable (dofus.sqlite)."; return; }
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var ct = cts.Token;
        bool Current() => ReferenceEquals(_scanCts, cts) && pid == _pid && character == _charName && server == _launch.ServerIdOf(pid);
        int minLevel = ScanLevelMin, maxLevel = ScanLevelMax, qty = ScanQty;
        int job = ScanJob?.Id ?? 0, type = ScanType?.Id ?? 0, sort = ScanSortIndex, minPct = ScanMinPct;
        long minGain = ScanMinGain;
        string query = ScanQuery.Trim();
        bool decompose = ScanDecompose;
        decimal saleTax = (decimal)Math.Clamp(_launch.HdvTaxPercent(pid), 0f, 50f);
        ScanBusy = true;
        ScanStatusText = "Analyse locale des recettes et prix en cache… (relancer pour annuler)";
        try
        {
            var estimates = await Task.Run(() =>
            {
                // A separate read-only connection avoids sharing GameDb's connection and text cache across threads.
                using var db = GameDb.TryOpen(path) ?? throw new InvalidOperationException("Base locale illisible.");
                var recipes = new List<CraftScanRecipe>();
                foreach (var j in db.Jobs().Where(j => job <= 0 || j.Id == job))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var r in db.RecipesOfJob(j.Id, maxLevel))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (r.Level < minLevel || !r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                        if (type > 0)
                        {
                            var item = db.Row("ItemData", r.ResultId.ToString(CultureInfo.InvariantCulture));
                            if (item is null || !item.TryGetValue("typeId", out var t) || Convert.ToInt32(t, CultureInfo.InvariantCulture) != type) continue;
                        }
                        recipes.Add(new(r.ResultId, r.Name, r.Level, r.JobId,
                            r.Ingredients.Select(i => new CraftScanIngredient(i.Gid, i.Quantity)).ToArray()));
                    }
                }
                // Décomposition : toutes les recettes du jeu (par objet produit), pour fabriquer un ingrédient plutôt que l'acheter.
                Dictionary<long, CraftScanRecipe>? sub = null;
                if (decompose)
                {
                    sub = new();
                    foreach (var j in db.Jobs())
                        foreach (var r in db.RecipesOfJob(j.Id, 200))
                        {
                            ct.ThrowIfCancellationRequested();
                            sub.TryAdd(r.ResultId, new(r.ResultId, r.Name, r.Level, r.JobId, r.Ingredients.Select(i => new CraftScanIngredient(i.Gid, i.Quantity)).ToArray()));
                        }
                }
                var prices = new Dictionary<long, CraftPrice>();
                var wanted = recipes.Select(r => r.Gid).Concat(recipes.SelectMany(r => r.Ingredients.Select(i => i.Gid)));
                if (sub is not null) wanted = wanted.Concat(sub.Keys).Concat(sub.Values.SelectMany(r => r.Ingredients.Select(i => i.Gid)));
                foreach (long gid in wanted.Distinct())
                {
                    ct.ThrowIfCancellationRequested();
                    long average = _launch.Hdv.AverageFor(pid, gid);
                    if (average <= 0 && gid > 0 && gid <= int.MaxValue) average = _launch.AveragePrice(pid, (int)gid);
                    prices[gid] = HdvCraftScanner.SelectPrice(_launch.Hdv.MarketFor(pid, gid, 0), _launch.Hdv.PriceAgeMs(pid, gid), average);
                }
                return HdvCraftScanner.Estimate(recipes, prices, qty, ct, sub, saleTax);
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (!Current()) return;
            int unknown = estimates.Count(e => e.Cost is null || e.Value is null);
            int overflow = 0;
            foreach (var result in HdvCraftScanner.Profits(estimates, minGain, minPct, sort))
            {
                ct.ThrowIfCancellationRequested();
                // Conservative display rounding; calculations and thresholds retain decimal precision.
                if (result.Cost > long.MaxValue || result.Value > long.MaxValue) { overflow++; continue; }
                ScanRows.Add(new HdvScanRow
                {
                    Gid = result.Recipe.Gid, Name = result.Recipe.Name,
                    Cost = (long)decimal.Ceiling(result.Cost!.Value), Value = (long)decimal.Floor(result.Value!.Value),
                    Detail = result.Evidence, Seen = 0, Suspect = true,
                });
            }
            ScanNoMatchText = ScanRows.Count == 0 ? "Aucun bénéfice estimable avec ces filtres et les prix locaux connus." : "";
            ScanStatusText = $"{estimates.Count} recette(s), {ScanRows.Count} résultat(s), {unknown} prix incomplet(s), {overflow} hors capacité. " +
                "Estimations brutes, fraîcheur indiquée par prix; aucun rafraîchissement réseau ni vente garantie.";
        }
        catch (OperationCanceledException) { if (Current()) { ScanRows.Clear(); ScanStatusText = "Scan annulé."; } }
        catch (Exception ex) { if (Current()) { ScanRows.Clear(); ScanStatusText = $"Scan local impossible : {ex.Message}"; } }
        finally
        {
            if (ReferenceEquals(_scanCts, cts))
            {
                if (!Current()) { ScanRows.Clear(); ScanStatusText = "Scan abandonné : personnage ou serveur modifié."; }
                _scanCts = null;
                ScanBusy = false;
            }
            cts.Dispose();
        }
    }
}
