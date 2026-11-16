using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Expedition;
using GivreClone.App.Services.LotM;
using System.Globalization;
using System.Text;
using GivreClone.App.Services.QuantityFarm;
using GivreClone.App.Services.Resources;

namespace GivreClone.App.ViewModels;

/// <summary>Un monstre de la zone choisie. Un clic le fait passer : neutre -> OBLIGATOIRE (vert) -> INTERDIT (rouge) -> neutre.</summary>
public sealed partial class ExpMonsterCard : ObservableObject
{
    private static readonly IBrush Neutral = new ImmutableSolidColorBrush(Color.Parse("#2A303B"));
    private static readonly IBrush Required = new ImmutableSolidColorBrush(Color.Parse("#3FA45B"));
    private static readonly IBrush Forbidden = new ImmutableSolidColorBrush(Color.Parse("#C85450"));

    public ExpMonster Monster { get; }
    public ExpMonsterCard(ExpMonster m) => Monster = m;

    public string Name => Monster.Name;
    public string LevelText => Monster.LevelText;
    /// <summary>Icône du jeu (cache du fournisseur) ; null tant que le bundle n'est pas chargé -> la carte garde sa pastille.</summary>
    public Bitmap? Icon => Stores.MonsterIcons.Get(Monster.Id);
    public void RefreshIcon() => OnPropertyChanged(nameof(Icon));

    /// <summary>0 = neutre, 1 = obligatoire dans le groupe, 2 = interdit.</summary>
    [ObservableProperty] public partial int State { get; set; }
    public IBrush Border => State switch { 1 => Required, 2 => Forbidden, _ => Neutral };
    public string StateText => State switch { 1 => "obligatoire", 2 => "interdit", _ => "" };
    partial void OnStateChanged(int value) { OnPropertyChanged(nameof(Border)); OnPropertyChanged(nameof(StateText)); }

    [RelayCommand] private void Cycle() => State = (State + 1) % 3;
}
/// <summary>Un drop du monstre choisi pour le farm par quantité. Le pourcentage est celui du grade 1, pas un quota.</summary>
public sealed class ExpQuantityDrop
{
    public ExpQuantityDrop(long objectId, string label)
    {
        ObjectId = objectId;
        Label = label;
    }

    public long ObjectId { get; }
    public string Label { get; }
}

// ===================== Onglet Expédition : choisir une zone, cocher les monstres, lancer =====================
// Aucune saisie de script : zone -> sous-zone -> cartes (triées par proximité) + monstres cliquables + limites. Au lancement le
// profil devient un script (ExpeditionScript) que le moteur exécute comme n'importe quel script du bot sélectionné.
// Perf : rien n'est chargé tant que l'onglet n'est pas utilisé ; les requêtes sont courtes ; les icônes passent par le cache du fournisseur.
public partial class BotsViewModel
{
    public ObservableCollection<ExpArea> ExpAreas { get; } = new();
    public ObservableCollection<ExpSubArea> ExpSubAreas { get; } = new();
    public ObservableCollection<ExpMonsterCard> ExpMonsters { get; } = new();

    [ObservableProperty] public partial ExpArea? ExpArea { get; set; }
    [ObservableProperty] public partial ExpSubArea? ExpSubArea { get; set; }
    [ObservableProperty] public partial double ExpMinGroup { get; set; } = 1;
    [ObservableProperty] public partial double ExpMaxGroup { get; set; } = 8;
    [ObservableProperty] public partial decimal? ExpMaxFights { get; set; } = 0;
    [ObservableProperty] public partial decimal? ExpMaxMinutes { get; set; } = 0;
    [ObservableProperty] public partial double ExpStopPods { get; set; } = 95;
    [ObservableProperty] public partial int ExpMaxConsecutiveDefeats { get; set; } = 0;
    [ObservableProperty] public partial string ExpSummary { get; set; } = "";
    /// <summary>« Valeur moyenne ≈ 1 234 k / monstre (prix HDV locaux) · meilleurs : … » ; vide sans prix connus.</summary>
    [ObservableProperty] public partial string ExpValueText { get; set; } = "";
    public bool ExpHasValue => ExpValueText.Length > 0;
    partial void OnExpValueTextChanged(string value) => OnPropertyChanged(nameof(ExpHasValue));
    private List<GivreClone.App.Services.Expedition.MonsterDropSet> _expZoneDrops = new();
    [ObservableProperty] public partial string ExpStatus { get; set; } = "";
    public bool ExpHasMonsters => ExpMonsters.Count > 0;
    /// <summary>Lié à TabItem.IsSelected : les données de l'onglet ne se chargent qu'à sa première ouverture.</summary>
    /// <summary>Onglet demandé par la recherche rapide (Ctrl+K) : la vue Bots le sélectionne puis remet -1.</summary>
    [ObservableProperty] public partial int RequestedTab { get; set; } = -1;
    [ObservableProperty] public partial bool ExpTabSelected { get; set; }
    partial void OnExpTabSelectedChanged(bool value) { if (value) { ExpeditionOpen(); RefreshAnomalies(); } }

    private IReadOnlyList<long> _expMaps = Array.Empty<long>();
    private int _expCandidateCount;
    [ObservableProperty] public partial string ExpRouteText { get; set; } = "";
    private readonly Dictionary<int, string> _expProgressByPid = new();
    private readonly Dictionary<int, long> _expVisitsByPid = new();
    public string ExpRouteProgress => SelectedBot is { Pid: > 0 } b
        ? _expProgressByPid.GetValueOrDefault(b.Pid, "") : "";
    public ObservableCollection<ExpQuantityDrop> ExpQuantityDrops { get; } = new();
    [ObservableProperty] public partial ExpMonsterCard? ExpQuantityMonster { get; set; }
    [ObservableProperty] public partial ExpQuantityDrop? ExpQuantityDrop { get; set; }
    [ObservableProperty] public partial decimal? ExpQuantityTarget { get; set; } = 10;
    [ObservableProperty] public partial string ExpQuantityStatus { get; set; } = "";
    [ObservableProperty] public partial bool ExpQuantityBagOnlyOffer { get; set; }
    public string ExpQuantityProgress => SelectedBot is { Pid: > 0 } b && QuantityFarmBoard.TryGet(b.Pid, out var farm)
        ? QuantityFarmBoard.Format(farm) : "";
    private bool _quantityFarmActive;
    private bool _expLoaded;

    [RelayCommand]
    private void RefreshExpeditionCatalog()
    {
        if (ExpArea is not null) SaveExpedition();
        Stores.Launch.ReloadGameData();
        _expLoaded = false;
        ExpArea = null;
        ExpeditionOpen();
    }

    // ---- banque automatique (zone et donjon) ----
    /// <summary>Pods pleins -> aller déposer à la banque la plus proche puis reprendre, au lieu de s'arrêter.</summary>
    [ObservableProperty] public partial bool ExpBank { get; set; } = true;
    partial void OnExpBankChanged(bool value) => UpdateExpSummary();
    /// <summary>Défaite : continuer l'expédition (le perso repart directement après sa mort) au lieu de s'arrêter.</summary>
    [ObservableProperty] public partial bool ExpContinueAfterDeath { get; set; } = true;
    /// <summary>Récolter sur chaque carte parcourue (ressources accessibles au métier du perso).</summary>
    [ObservableProperty] public partial bool ExpGather { get; set; }
    partial void OnExpGatherChanged(bool value) => UpdateExpSummary();
    /// <summary>Combattre les groupes éligibles sur chaque carte parcourue (activé par défaut : comportement historique).</summary>
    [ObservableProperty] public partial bool ExpFight { get; set; } = true;
    partial void OnExpFightChanged(bool value) => UpdateExpSummary();
    public IReadOnlyList<ExpQuantityDrop> ExpGatherResources { get; } = new[] { new ExpQuantityDrop(0, "Toutes les ressources accessibles") }
        .Concat(ResourceMap.Default.Find("").Select(r => new ExpQuantityDrop(r.ItemId,
            $"{r.Name} · {ResourceMap.JobName(r.Job)} niv. {r.Level}"))).ToArray();
    [ObservableProperty] public partial ExpQuantityDrop? ExpGatherResource { get; set; }
    /// <summary>Pods pleins : passer d'abord à l'HDV cible du perso (onglet HDV) et mettre en vente sa liste de remise en vente.</summary>
    [ObservableProperty] public partial bool ExpHdv { get; set; }
    partial void OnExpHdvChanged(bool value) => UpdateExpSummary();
    public string ExpHdvText => HdvPlanText(HdvPlanFor(SelectedBot));

    /// <summary>HDV cible + liste de remise en vente du perso (onglet HDV), ou null si l'un des deux manque.</summary>
    internal static HdvPlan? HdvPlanFor(BotRowViewModel? bot)
    {
        if (bot is not { Pid: > 0 }) return null;
        var prof = HdvProfileStore.GetScoped(bot.Row.CharacterName, Stores.Launch.ServerIdOf((uint)bot.Pid));
        var rules = prof.Trade.RelistRules
            .Select(r => (r.Gid, Lot: KeepOnSalePlanner.NormalizeSize(r.Qty) ?? 0, r.MinPrice))
            .Where(r => r.Gid > 0 && r.Lot > 0).ToList();
        return prof.HdvMapId > 0 && rules.Count > 0 ? new HdvPlan(prof.HdvMapId, rules) : null;
    }

    internal static string HdvPlanText(HdvPlan? plan)
        => plan is null ? Loc.T("L.Exp.HdvMissing")
         : Loc.Fmt("L.Exp.HdvReady", HdvLocations.All.FirstOrDefault(h => h.MapId == plan.Map)?.Name ?? plan.Map.ToString(), plan.Rules.Count);

    // ---- mode DONJON ----
    /// <summary>Faux = zone (sous-zone + monstres), vrai = donjon (salles dans l'ordre, clés, boss).</summary>
    [ObservableProperty] public partial bool ExpDungeonMode { get; set; }
    public bool ExpZoneMode => !ExpDungeonMode;
    partial void OnExpDungeonModeChanged(bool value) { OnPropertyChanged(nameof(ExpZoneMode)); if (value) LoadDungeons(); UpdateExpSummary(); }
    [RelayCommand] private void ExpSetZoneMode() => ExpDungeonMode = false;
    [RelayCommand] private void ExpSetDungeonMode() => ExpDungeonMode = true;

    private List<ExpDungeon> _allDungeons = new();
    public ObservableCollection<ExpDungeon> ExpDungeons { get; } = new();
    [ObservableProperty] public partial string ExpDungeonFilter { get; set; } = "";
    partial void OnExpDungeonFilterChanged(string value) => FilterDungeons();
    [ObservableProperty] public partial ExpDungeon? ExpDungeon { get; set; }
    [ObservableProperty] public partial decimal? ExpRuns { get; set; } = 1;
    private int ExpDungeonRunCount() => (int)decimal.Clamp(ExpRuns ?? 1m, 0m, 100_000m);
    partial void OnExpRunsChanged(decimal? value) { RefreshExpDungeonKeys(); UpdateExpSummary(); }
    [ObservableProperty] public partial string ExpDungeonDetail { get; set; } = "";
    public ObservableCollection<string> ExpDungeonKeys { get; } = new();
    public bool ExpDungeonHasKeys => ExpDungeonKeys.Count > 0;
    private DungeonInfo? _expDungeonInfo;

    [ObservableProperty] public partial string ExpDungeonBudgetText { get; set; } = "";

    private void RefreshExpDungeonKeys()
    {
        ExpDungeonKeys.Clear();
        ExpDungeonBudgetText = "";
        if (_expDungeonInfo is not { } dungeon) { OnPropertyChanged(nameof(ExpDungeonHasKeys)); return; }
        uint pid = SelectedBot is { Pid: > 0 } b ? (uint)b.Pid : 0;
        if (pid == 0)
        {
            ExpDungeonBudgetText = "Inventaire non consulté : sélectionnez un personnage connecté pour connaître les clés présentes dans son sac.";
            OnPropertyChanged(nameof(ExpDungeonHasKeys));
            return;
        }
        int runs = ExpDungeonRunCount();
        bool unlimited = runs == 0;
        var required = dungeon.Keys.Where(k => k.Gid > 0 && k.Quantity >= 0)
            .Select(k => new GivreClone.App.Services.Expedition.ExpeditionMaterial(
            k.Gid, k.Name, k.Quantity,
            pid > 0 ? Stores.Launch.InventoryCount(pid, k.Gid) : 0, 0)).ToList();
        if (required.Count < dungeon.Keys.Count)
        {
            ExpDungeonBudgetText = "Certaines clés n'ont pas d'identifiant ou de quantité valide dans les données du donjon : bilan incomplet.";
            OnPropertyChanged(nameof(ExpDungeonHasKeys));
            return;
        }
        var budget = GivreClone.App.Services.Expedition.ExpeditionBudget.Estimate(required, runs, DateTimeOffset.UtcNow);
        foreach (var item in budget.Lines)
        {
            string qty = unlimited ? $"{dungeon.Keys.Where(k => k.Gid == item.Gid).Sum(k => k.Quantity)}/passage" : item.Required.ToString();
            ExpDungeonKeys.Add($"{(item.Missing == 0 ? "✔" : "✖")} {item.Name} ×{qty} (sac {item.Available})");
        }
        ExpDungeonBudgetText = unlimited
            ? "Passages illimités : coût et besoin total inconnus ; quantités affichées par passage."
            : budget.RequiresMaterials
                ? $"Clés non disponibles dans le sac : {budget.Lines.Sum(l => l.Missing)} · coffre non consulté, prix non relevés, budget total inconnu."
                : "Clés du donjon observées dans le sac pour le nombre de passages prévu.";
        if (pid > 0 && Stores.Launch.GameData is { } gdb)
        {
            var l = Stores.Launch;
            decimal? Price(long gid)
            {
                long a = l.Hdv.AverageFor(pid, gid);
                if (a <= 0 && gid > 0 && gid <= int.MaxValue) a = l.AveragePrice(pid, (int)gid);
                return a > 0 ? a : null;
            }
            var bosses = dungeon.Bosses.Select(b => new GivreClone.App.Services.Expedition.MonsterDropSet(b.Id, b.Name, gdb.MonsterDrops(b.Id))).ToList();
            var v = GivreClone.App.Services.Expedition.DungeonProfit.Estimate(dungeon.Keys.Select(k => (k.Gid, k.Quantity)), bosses, Price);
            if (v.KeyCost is { } kc && (kc > 0 || v.BossDropValue > 0))
                ExpDungeonBudgetText += $"\nPar passage : clés ≈ {kc:N0} kamas · drops de boss ≈ {v.BossDropValue:N0} kamas · net ≈ {v.Net:N0} (prix HDV locaux, boss seuls" + (v.UnpricedDrops > 0 ? $", {v.UnpricedDrops} drop(s) sans prix" : "") + ").";
            else if (v.UnpricedKeys > 0)
                ExpDungeonBudgetText += $"\n{v.UnpricedKeys} clé(s) sans prix HDV connu : rentabilité non calculée (ouvrez l'HDV pour relever les prix).";
        }
        OnPropertyChanged(nameof(ExpDungeonHasKeys));
    }

    private void LoadDungeons()
    {
        if (_allDungeons.Count > 0 || Stores.Launch.GameData is not { } db) return;
        try { _allDungeons = db.ExpeditionDungeons().ToList(); } catch { _allDungeons = new(); }
        FilterDungeons();
    }

    private void FilterDungeons()
    {
        var keep = ExpDungeon;
        string f = PlaceText.Normalize(ExpDungeonFilter ?? "");
        ExpDungeons.Clear();
        foreach (var d in _allDungeons)
            if (f.Length == 0 || PlaceText.Normalize(d.Name).Contains(f, StringComparison.Ordinal) || d.Level.ToString() == f) ExpDungeons.Add(d);
        if (keep is not null && ExpDungeons.Contains(keep)) ExpDungeon = keep;
    }

    partial void OnExpDungeonChanged(ExpDungeon? value)
    {
        _expDungeonInfo = null;
        ExpDungeonKeys.Clear();
        ExpDungeonBudgetText = "";
        ExpDungeonDetail = "";
        if (value is not null && Stores.Launch.GameData is { } db)
        {
            try { _expDungeonInfo = db.DungeonById(value.Id); } catch { _expDungeonInfo = null; }
            if (_expDungeonInfo is { } d)
            {
                RefreshExpDungeonKeys();
                ExpDungeonDetail = Loc.Fmt("L.Exp.DungeonDetail", d.MapIds.Count, d.OptimalLevel,
                    d.Bosses.Count > 0 ? string.Join(", ", d.Bosses.Select(x => x.Name).Where(n => n.Length > 0)) : "—");
            }
        }
        OnPropertyChanged(nameof(ExpDungeonHasKeys));
        UpdateExpSummary();
    }

    /// <summary>Banque la plus proche d'une carte (repères du jeu) : (carte, cellule de porte ou -1, intérieur atteint par la porte ou 0).
    /// null = aucune connue.</summary>
    internal static (long Map, int Door, long Inside)? NearestBank(long fromMap)
    {
        var launch = Stores.Launch;
        if (fromMap <= 0 || launch.GameData is not { } db || launch.MapCoordOf(fromMap) is not (int x, int y)) return null;
        var origin = new PlaceOrigin(x, y, launch.WorldMapOf(fromMap) ?? 1);
        foreach (var q in new[] { "banque", "bank", "banco" })
        {
            IReadOnlyList<WorldPlace>? found = null;
            try { found = db.Places(q, origin, 3); } catch { }
            if (found?.FirstOrDefault(p => p.MapId > 0 && p.Distance is not null) is { } bank)
            {
                var doors = launch.WorldPlaceDoors(bank.MapId);
                // Une seule porte sur la carte = celle de la banque ; plusieurs : ambigu -> banquier cherché sur la carte même.
                return doors.Count == 1 ? (bank.MapId, doors[0].Cell, doors[0].MapId) : (bank.MapId, -1, 0L);
            }
        }
        return null;
    }

    /// <summary>Appelé à l'ouverture de l'onglet : charge la liste des zones une fois, et lance le chargement des icônes en tâche de fond.</summary>
    [RelayCommand]
    private void ExpeditionOpen()
    {
        if (_expLoaded) return;
        var db = Stores.Launch.GameData;
        if (db is null)
        {
            // The Socket account is not a local Dofus.exe process. The offline
            // GameDb is shared across MITM/Socket and may be missing at startup;
            // do not permanently mark the tab as loaded when it was empty.
            ExpStatus = Loc.T("L.Exp.CatalogMissing");
            return;
        }
        ExpAreas.Clear();
        try
        {
            foreach (var a in db.ExpeditionAreas()) ExpAreas.Add(a);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ExpStatus = Loc.T("L.Exp.CatalogUnreadable");
            return;
        }
        if (ExpAreas.Count == 0) { ExpStatus = Loc.T("L.Exp.CatalogEmpty"); return; }
        ExpStatus = "";
        _expLoaded = true;
        RestoreExpedition();
        _ = Task.Run(() =>
        {
            Stores.Launch.EnsureMonsterIcons();
            Dispatcher.UIThread.Post(() => { foreach (var c in ExpMonsters) c.RefreshIcon(); });
        });
    }

    partial void OnExpAreaChanged(ExpArea? value)
    {
        ExpSubAreas.Clear(); ExpMonsters.Clear(); _expMaps = Array.Empty<long>(); _expCandidateCount = 0; ExpRouteText = ""; ExpSubArea = null;
        RebindQuantityMonster();
        if (value is null || Stores.Launch.GameData is not { } db) return;
        foreach (var s in db.ExpeditionSubAreas(value.Id)) ExpSubAreas.Add(s);
        OnPropertyChanged(nameof(ExpHasMonsters));
        UpdateExpSummary();
    }

    partial void OnExpSubAreaChanged(ExpSubArea? value)
    {
        if (value?.Id != ExpAnomalySubarea) ExpAnomalySubarea = 0;   // autre sous-zone choisie à la main : plus une anomalie
        ExpMonsters.Clear(); _expMaps = Array.Empty<long>(); _expCandidateCount = 0;
        if (value is not null && Stores.Launch.GameData is { } db)
        {
            foreach (var m in db.ExpeditionMonsters(value.Id)) { var c = new ExpMonsterCard(m); c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ExpMonsterCard.State)) UpdateExpSummary(); }; ExpMonsters.Add(c); }
            // Cartes visitables seulement (extérieurs du calque de la zone, une par coordonnée) : fin des « voyage impossible »
            // en boucle sur les intérieurs / sous-sols que la table du jeu range aussi dans la sous-zone.
            var launch = Stores.Launch;
            var candidates = db.ExpeditionMaps(value.Id).Distinct().ToList();
            _expCandidateCount = candidates.Count;
            _expMaps = ExpeditionScript.CleanMaps(candidates
                .Select(id => (id, launch.MapCoordOf(id) is (int x, int y) ? ((int, int)?)(x, y) : null, launch.WorldMapOf(id))).ToList(), launch.ExpeditionNavigationMap, launch.ExpeditionWalkScore);
        }
        _expZoneDrops = value is not null && Stores.Launch.GameData is { } gdb
            ? ExpMonsters.Select(c => new GivreClone.App.Services.Expedition.MonsterDropSet(c.Monster.Id, c.Monster.Name, gdb.MonsterDrops(c.Monster.Id))).ToList()
            : new();
        RefreshZoneValue();
        OnPropertyChanged(nameof(ExpHasMonsters));
        RebindQuantityMonster();
        UpdateExpSummary();
    }

    /// <summary>Sélection de PLUSIEURS sous-zones : la première charge comme avant, les autres ajoutent leurs cartes (visitables) et leurs monstres.</summary>
    public void SetExpSubAreas(IReadOnlyList<ExpSubArea> selected)
    {
        var states = ExpMonsters.ToDictionary(c => c.Monster.Id, c => c.State);
        // Toujours reconstruire : retirer une sous-zone doit aussi retirer ses cartes et monstres.
        if (selected.Count == 0)
        {
            ExpSubArea = null;
            ExpMonsters.Clear();
            _expMaps = Array.Empty<long>();
            _expCandidateCount = 0;
            _expZoneDrops.Clear();
            RefreshZoneValue();
            OnPropertyChanged(nameof(ExpHasMonsters));
        RebindQuantityMonster();
            UpdateExpSummary();
            return;
        }
        if (!ReferenceEquals(ExpSubArea, selected[0])) ExpSubArea = selected[0];
        if (Stores.Launch.GameData is not { } db) return;
        if (selected.Count > 1) ExpAnomalySubarea = 0;
        var launch = Stores.Launch;
        var zones = selected.Select(s => db.ExpeditionMaps(s.Id).Distinct()
            .Select(id => (id, launch.MapCoordOf(id) is (int x, int y) ? ((int, int)?)(x, y) : null, launch.WorldMapOf(id))).ToList()).ToList();
        _expCandidateCount = zones.SelectMany(ids => ids).Select(m => m.id).Distinct().Count();
        _expMaps = zones.SelectMany(ids => ExpeditionScript.CleanMaps(ids,
            launch.ExpeditionNavigationMap, launch.ExpeditionWalkScore)).Distinct().ToArray();
        ExpMonsters.Clear();
        foreach (var m in selected.SelectMany(s => db.ExpeditionMonsters(s.Id)).DistinctBy(m => m.Id))
        {
            var c = new ExpMonsterCard(m) { State = states.GetValueOrDefault(m.Id) };
            c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ExpMonsterCard.State)) { UpdateExpSummary(); RefreshZoneValue(); } };
            ExpMonsters.Add(c);
        }
        _expZoneDrops = ExpMonsters.Select(c => new MonsterDropSet(c.Monster.Id, c.Monster.Name, db.MonsterDrops(c.Monster.Id))).ToList();
        RefreshZoneValue();
        OnPropertyChanged(nameof(ExpHasMonsters));
        RebindQuantityMonster();
        UpdateExpSummary();
    }

    /// <summary>Valeur HDV attendue par monstre de la sous-zone choisie (prix locaux du bot sélectionné, tick lent).</summary>
    private void RefreshZoneValue()
    {
        if (SelectedBot is not { Pid: > 0 } b || _expZoneDrops.Count == 0 || ExpDungeonMode) { ExpValueText = ""; return; }
        uint pid = (uint)b.Pid;
        var l = Stores.Launch;
        decimal? Price(long gid)
        {
            long a = l.Hdv.AverageFor(pid, gid);
            if (a <= 0 && gid > 0 && gid <= int.MaxValue) a = l.AveragePrice(pid, (int)gid);
            return a > 0 ? a : null;
        }
        var kept = _expZoneDrops.Where(z => ExpMonsters.FirstOrDefault(c => c.Monster.Id == z.MonsterId) is not { State: 2 }).ToList();
        var r = GivreClone.App.Services.Expedition.ZoneValue.ForZone(kept, Price);
        ExpValueText = r is null || r.AveragePerMonster <= 0 ? ""
            : $"Valeur moyenne ≈ {r.AveragePerMonster:N0} kamas/monstre (prix HDV locaux" + (r.UnpricedDrops > 0 ? $", {r.UnpricedDrops} drop(s) sans prix" : "") + ") · meilleurs : "
              + string.Join(", ", r.Best.Where(v => v.Value > 0).Select(v => $"{v.Name} {v.Value:N0}"));
    }

    partial void OnExpMinGroupChanged(double value) { if (value > ExpMaxGroup) ExpMaxGroup = value; UpdateExpSummary(); }
    partial void OnExpMaxGroupChanged(double value) { if (value < ExpMinGroup) ExpMinGroup = value; UpdateExpSummary(); }

    private void UpdateExpSummary()
    {
        UpdateExpRoutePreview();
        OnPropertyChanged(nameof(ExpHdvText));   // l'HDV cible / la liste ont pu changer dans l'onglet HDV
        if (ExpDungeonMode)
        {
            ExpSummary = _expDungeonInfo is { } d ? Loc.Fmt("L.Exp.DungeonSummary", d.Name, d.MapIds.Count, ExpDungeonRunCount() == 0 ? "∞" : ExpDungeonRunCount().ToString()) : "";
            return;
        }
        if (ExpSubArea is null) { ExpSummary = ""; return; }
        int req = ExpMonsters.Count(m => m.State == 1), forb = ExpMonsters.Count(m => m.State == 2);
        ExpSummary = $"{_expMaps.Count} carte(s) · groupes de {(int)ExpMinGroup} à {(int)ExpMaxGroup} monstres"
                     + (req > 0 ? $" · {req} obligatoire(s)" : "") + (forb > 0 ? $" · {forb} interdit(s)" : "")
                     + (ExpGather ? " · récolte" : "") + (ExpFight ? "" : " · sans combat")
                     + (ExpBank ? " · banque auto" : "") + (ExpHdv ? " · HDV avant banque" : "")
                     + (ExpAnomalySubarea > 0 ? " · " + Loc.T("L.Exp.AnomalyMode") : "")
                     + (ExpElixirRank > 0 ? " · " + Loc.T("L.Exp.ElixirShort") : "");
    }

    private void UpdateExpRoutePreview()
    {
        OnPropertyChanged(nameof(ExpQuantityProgress));
        if (ExpDungeonMode || _expMaps.Count == 0) { ExpRouteText = ""; return; }
        var launch = Stores.Launch;
        var start = SelectedBot is { Pid: > 0 } b && launch.CurrentMap((uint)b.Pid) is long map
            ? launch.MapCoordOf(map) : null;
        var order = ExpeditionScript.NearestOrder(_expMaps.Select(id => (id, launch.MapCoordOf(id))).ToList(), start);
        var lines = order.Take(16).Select((id, i) => launch.MapCoordOf(id) is { } p
            ? $"{i + 1}. [{p.X},{p.Y}] · {id}" : $"{i + 1}. {id}");
        ExpRouteText = Loc.Fmt("L.Exp.RouteCounts", order.Count, Math.Max(0, _expCandidateCount - _expMaps.Count))
            + "\n" + string.Join("\n", lines)
            + (order.Count > 16 ? "\n" + Loc.Fmt("L.Exp.RouteMore", order.Count - 16) : "");
    }

    private void NoteExpeditionProgress(int pid, string message)
    {
        if (QuantityFarmProgress.TryParse(message, out var farm))
        {
            QuantityFarmBoard.Note(pid, farm);
            if (SelectedBot?.Pid == pid) OnPropertyChanged(nameof(ExpQuantityProgress));
            return;
        }
        if (!ExpeditionRouteProgress.TryParse(message, out var progress)) return;
        long visits = _expVisitsByPid.GetValueOrDefault(pid) + 1;
        _expVisitsByPid[pid] = visits;
        _expProgressByPid[pid] = Loc.Fmt("L.Exp.RouteProgress", progress.Index, progress.Total, progress.Map)
            + " · " + Loc.Fmt("L.Exp.RouteVisits", visits);
        if (SelectedBot?.Pid == pid) OnPropertyChanged(nameof(ExpRouteProgress));
    }

    private void ClearExpeditionProgress(int pid)
    {
        _expProgressByPid.Remove(pid);
        _expVisitsByPid.Remove(pid);
        if (SelectedBot?.Pid == pid) OnPropertyChanged(nameof(ExpRouteProgress));
    }

    [RelayCommand] private void ExpeditionResetMonsters() { foreach (var m in ExpMonsters) m.State = 0; }

    private ExpeditionProfile BuildExpeditionProfile(uint pid, List<string>? warnings = null)
    {
        var launch = Stores.Launch;
        (int X, int Y)? start = launch.CurrentMap(pid) is long cm ? launch.MapCoordOf(cm) : null;
        var ordered = ExpeditionScript.NearestOrder(_expMaps.Select(id => (id, launch.MapCoordOf(id) is (int x, int y) ? ((int, int)?)(x, y) : null)).ToList(), start);
        long first = ordered.Count > 0 ? ordered[0] : 0;
        var unreachableLegs = new List<(long From, long To)>();
        ordered = ExpeditionScript.TraversedMaps(ordered, launch.CurrentMap(pid) ?? 0,
            (from, to) => launch.WorldPath((int)from, (int)to, pid: pid).Select(t => (long)t.Arrival).ToArray(), unreachableLegs);
        foreach (var leg in unreachableLegs)
            warnings?.Add($"trajet {leg.From} -> {leg.To} sans chemin dans le graphe : destination conservée pour tentative par zaap / havre-sac");
        var bank = ExpBank && ordered.Count > 0 ? NearestBank(ordered[0]) : null;
        return new ExpeditionProfile(
            ExpSubArea?.Name ?? "Zone", string.Join("\n", ordered), (int)ExpMinGroup, (int)ExpMaxGroup,
            string.Join(" ", ExpMonsters.Where(m => m.State == 1).Select(m => m.Monster.Id)),
            string.Join(" ", ExpMonsters.Where(m => m.State == 2).Select(m => m.Monster.Id)), "",
            (int)(ExpMaxFights ?? 0), (int)(ExpMaxMinutes ?? 0), (int)ExpStopPods,
            bank?.Map ?? 0, bank?.Door ?? -1, bank?.Inside ?? 0, ExpHdv ? HdvPlanFor(SelectedBot) : null, ExpAnomalySubarea,
            ElixirPlanFor(ordered.Count > 0 ? ordered[0] : launch.CurrentMap(pid) ?? 0), ExpContinueAfterDeath,
            Gather: ExpGather, Fight: ExpFight, GatherItemId: ExpGatherResource?.ObjectId ?? 0,
            LoopStartIndex: Math.Max(1, ordered.IndexOf(first) + 1), ActivityMaps: _expMaps.ToArray(),
            MaxConsecutiveDefeats: Math.Max(0, ExpMaxConsecutiveDefeats));
    }

    private DungeonProfile? BuildDungeonProfile()
    {
        if (_expDungeonInfo is not { } d) return null;
        var bank = ExpBank ? NearestBank(d.EntranceMapId > 0 ? d.EntranceMapId : d.ExitMapId) : null;
        return new DungeonProfile(d.Name, d.MapIds, d.EntranceMapId, d.Keys.Select(k => (k.Gid, k.Quantity, k.Name)).ToList(),
            ExpDungeonRunCount(), (int)(ExpMaxMinutes ?? 0), bank?.Map ?? 0, bank?.Door ?? -1, (int)ExpStopPods, bank?.Inside ?? 0,
            ElixirPlanFor(d.EntranceMapId > 0 ? d.EntranceMapId : d.ExitMapId), ExpContinueAfterDeath);
    }

    /// <summary>Lance l'expédition sur le bot sélectionné (mêmes garde-fous que le bouton Lancer : licence, niveau, reconnexion).</summary>
    [RelayCommand]
    private void ExpeditionStart()
    {
        if (SelectedBot is not { Pid: > 0 } bot) { ExpStatus = "Sélectionne un bot connecté."; return; }
        if (!ExpDungeonMode && ExpGather && !LicenseService.HasPermission(LicenseService.Perm.Gather))
        { ExpStatus = "Récolte indisponible : droit Récolte absent."; return; }
        // The transport owns synthetic PIDs. Even if the row's connection-mode
        // flag is stale during a reconnect, NEVER send that PID down MITM path.
        bool socket = bot.Row.IsSocketSession || LaunchService.IsSocketPid((uint)bot.Pid);
        if (socket)
        {
            // A Socket bot has a synthetic PID and no injected Dofus.exe.
            // Read the real session/map/rights; never interpret "row.Running"
            // alone as proof that its game socket is ready for bot actions.
            uint pid = (uint)bot.Pid;
            string? blocked = ExpeditionSocketSessionGate.StartBlockReason(new ExpeditionSocketSessionSnapshot(
                IsSocketSession: true,
                AccountRunning: bot.Row.Running,
                SessionParked: bot.Row.SocketParked,
                SessionBackoff: bot.Row.SocketBackoff,
                Reconnecting: bot.Row.ReconnectInPlace,
                WaitingForNewSession: _socketWaiting.ContainsKey(bot.Pid),
                GameLinkDown: Stores.Launch.IsLinkDown(pid),
                HasServerMap: Stores.Launch.CurrentMap(pid) is > 0,
                HasFightPermission: (!ExpDungeonMode && !ExpFight) || LicenseService.HasPermission(LicenseService.Perm.Fight),
                BottingAuthorized: LicenseService.CanBot,
                LevelAllowed: !Stores.Launch.LevelBlocked(pid),
                IsFollowerLocked: bot.FollowerLocked,
                InjectorRegistered: Stores.Launch.HasSocketInjector(pid)));
            if (blocked is not null) { ExpStatus = blocked; return; }
            // Shared account-level diagnostic also validates Socket entitlement
            // and the effective protocol mapping. The local injector check above
            // alone cannot certify message codecs/framing or gameplay readiness.
            var control = _accounts.ObserveSocketControl(bot.Row);
            if (control.State != "channel-registered")
            {
                ExpStatus = $"Socket : canal de commandes indisponible ({control.State}). {control.Explanation}";
                return;
            }
        }
        var warnings = new List<string>();
        string script;
        if (ExpDungeonMode)
        {
            if (BuildDungeonProfile() is not { } dp) { ExpStatus = Loc.T("L.Exp.PickDungeon"); return; }
            script = ExpeditionScript.BuildDungeon(dp, warnings);
            if (dp.BankMap == 0 && ExpBank) warnings.Add("aucune banque connue près du donjon : arrêt à pods pleins");
            if (dp.Elixir is { MaxPrice: <= 0 }) warnings.Add(Loc.T("L.Exp.ElixirNoBuy"));
        }
        else
        {
            if (ExpSubArea is null || _expMaps.Count == 0) { ExpStatus = "Choisis d'abord une zone."; return; }
            var prof = BuildExpeditionProfile((uint)bot.Pid, warnings);
            script = ExpeditionScript.Build(prof, warnings);
            if (prof.BankMap == 0 && ExpBank) warnings.Add("aucune banque connue près de la zone : arrêt à pods pleins");
            if (ExpHdv && prof.Hdv is null) warnings.Add(Loc.T("L.Exp.HdvMissing"));
            if (prof.Elixir is { MaxPrice: <= 0 }) warnings.Add(Loc.T("L.Exp.ElixirNoBuy"));
        }
        // Un script tournait déjà (autre expédition, script perso) : RunLua le REMPLACE (host.Start arrête le run précédent).
        // Avant, « if (!LuaRunning) RunLua() » ne changeait que le texte de l'éditeur et l'ancien script continuait.
        LuaScript = script;
        SaveExpedition(bot.Row.CharacterName);
        if (!socket)
        {
            // Keep the established MITM launch path unchanged.
            TrackFarmedAnomaly(bot.Pid, ExpDungeonMode ? 0 : ExpAnomalySubarea);
            ExpStatus = (ExpDungeonMode ? "Donjon lancé." : "Expédition lancée.") + (warnings.Count > 0 ? " " + string.Join(" · ", warnings) : "");
        }
        RunLua();
        if (socket)
        {
            // RunLua is void and may refuse (license, reconnect, level or
            // syntax). Running is only a LOCAL Lua host observation, NEVER a
            // game-action ACK or proof that any movement/fight succeeded.
            if (!_luaHosts.TryGetValue(bot.Pid, out var host) || !host.Running)
            {
                ExpStatus = "Socket : script Lua non démarré. Consulte la console et les prérequis (session, licence, syntaxe). Aucun ordre serveur confirmé.";
                return;
            }
            TrackFarmedAnomaly(bot.Pid, ExpDungeonMode ? 0 : ExpAnomalySubarea);
            SaveExpedition();
            ExpStatus = (ExpDungeonMode ? "Donjon" : "Expédition")
                        + " : hôte Lua démarré sur session Socket ; déplacements et combats restent à confirmer par les réponses serveur."
                        + (warnings.Count > 0 ? " " + string.Join(" · ", warnings) : "");
        }
    }

    [RelayCommand]
    private void ExpeditionStop()
    {
        bool stopQuantity = _quantityFarmActive && LuaRunning;
        if (LuaRunning) StopLua();
        if (SelectedBot is { } b) TrackFarmedAnomaly(b.Pid, 0);   // arrêt volontaire : pas de notification de fermeture
        ClearQuantityFarm(stopQuantity);
        ExpStatus = ExpDungeonMode ? "Donjon arrêté." : "Expédition arrêtée.";
    }

    /// <summary>Mémorise la sélection (zone, sous-zone, monstres, limites) pour la retrouver à la prochaine ouverture de l'onglet ou pour ce bot.</summary>
    private void SaveExpedition(string? characterName = null)
    {
        var serialized = new ExpeditionState(ExpArea?.Id ?? 0, ExpSubArea?.Id ?? 0, (int)ExpMinGroup, (int)ExpMaxGroup,
            ExpMonsters.Where(m => m.State == 1).Select(m => m.Monster.Id).ToList(), ExpMonsters.Where(m => m.State == 2).Select(m => m.Monster.Id).ToList(),
            (int)(ExpMaxFights ?? 0), (int)(ExpMaxMinutes ?? 0), (int)ExpStopPods,
            ExpBank, ExpDungeon?.Id ?? 0, ExpDungeonRunCount(), ExpDungeonMode, ExpHdv, ExpContinueAfterDeath,
            ExpGather, ExpFight, ExpGatherResource?.ObjectId ?? 0, Math.Max(0, ExpMaxConsecutiveDefeats)).Serialize();

        AppConfig.Current.ExpeditionLast = serialized;

        string target = !string.IsNullOrWhiteSpace(characterName) ? characterName : SelectedBot?.Row.CharacterName ?? "";
        if (!string.IsNullOrWhiteSpace(target))
        {
            AppConfig.Current.ExpeditionOf[target] = serialized;
        }

        AppConfig.Save();
    }

    public void RestoreExpedition(string? characterName = null)
    {
        string? serialized = null;
        string target = !string.IsNullOrWhiteSpace(characterName) ? characterName : SelectedBot?.Row.CharacterName ?? "";
        if (!string.IsNullOrWhiteSpace(target) && AppConfig.Current.ExpeditionOf.TryGetValue(target, out var perChar) && !string.IsNullOrWhiteSpace(perChar))
        {
            serialized = perChar;
        }
        else if (!string.IsNullOrWhiteSpace(AppConfig.Current.ExpeditionLast))
        {
            serialized = AppConfig.Current.ExpeditionLast;
        }

        if (string.IsNullOrWhiteSpace(serialized) || ExpeditionState.Parse(serialized) is not { } s) return;
        ExpGatherResource = ExpGatherResources.FirstOrDefault(r => r.ObjectId == s.GatherItemId) ?? ExpGatherResources[0];
        ExpBank = s.Bank; ExpHdv = s.Hdv; ExpContinueAfterDeath = s.ContinueAfterDeath;
        ExpMaxConsecutiveDefeats = s.MaxConsecutiveDefeats;
        ExpGather = s.Gather; ExpFight = s.Fight; ExpRuns = s.Runs; ExpMaxMinutes = s.Minutes; ExpStopPods = s.Pods;
        if (s.DungeonMode || s.DungeonId > 0)
        {
            LoadDungeons();
            ExpDungeon = _allDungeons.FirstOrDefault(d => d.Id == s.DungeonId);
            ExpDungeonMode = s.DungeonMode;
        }
        if (ExpAreas.FirstOrDefault(a => a.Id == s.AreaId) is not { } area) return;
        ExpArea = area;                                                   // remplit les sous-zones
        if (ExpSubAreas.FirstOrDefault(x => x.Id == s.SubAreaId) is not { } sub) return;
        ExpSubArea = sub;                                                 // remplit les monstres et les cartes
        foreach (var c in ExpMonsters) c.State = s.Mandatory.Contains(c.Monster.Id) ? 1 : s.Forbidden.Contains(c.Monster.Id) ? 2 : 0;
        ExpMinGroup = s.Min; ExpMaxGroup = s.Max; ExpMaxFights = s.Fights; ExpMaxMinutes = s.Minutes; ExpStopPods = s.Pods;
    }

    partial void OnExpQuantityMonsterChanged(ExpMonsterCard? value)
    {
        long? keep = ExpQuantityDrop?.ObjectId;
        ExpQuantityBagOnlyOffer = false;
        ExpQuantityDrops.Clear();
        ExpQuantityDrop = null;
        if (value is null || Stores.Launch.GameData is not { } db) return;
        foreach (var drop in db.MonsterDrops(value.Monster.Id))
        {
            string gid = drop.Gid.ToString(CultureInfo.InvariantCulture);
            string percent = drop.Percent.ToString("0.##", CultureInfo.InvariantCulture);
            ExpQuantityDrops.Add(new ExpQuantityDrop(drop.Gid, gid + " · " + percent + " % grade 1"));
        }
        ExpQuantityDrop = keep is { } id
            ? ExpQuantityDrops.FirstOrDefault(d => d.ObjectId == id)
            : ExpQuantityDrops.Count == 1 ? ExpQuantityDrops[0] : null;
    }

    private void RebindQuantityMonster()
    {
        int? id = ExpQuantityMonster?.Monster.Id;
        var match = id is int keep ? ExpMonsters.FirstOrDefault(c => c.Monster.Id == keep) : null;
        if (!ReferenceEquals(ExpQuantityMonster, match)) ExpQuantityMonster = match;
        OnPropertyChanged(nameof(ExpQuantityProgress));
    }

    [RelayCommand] private void StartQuantityFarm() => LaunchQuantityFarm(false);
    [RelayCommand] private void AcceptQuantityBagOnly() => LaunchQuantityFarm(true);

    private void LaunchQuantityFarm(bool acceptBagOnly)
    {
        if (ExpDungeonMode)
        {
            ApplyQuantityDecision(new QuantityFarmDecision(QuantityFarmStatus.Refused, "Le farm par quantité se lance sur une zone, pas dans un donjon.", 0, 0, 0, 0, 0, null, null));
            return;
        }
        if (SelectedBot is not { Pid: > 0 } bot)
        {
            ApplyQuantityDecision(new QuantityFarmDecision(QuantityFarmStatus.Refused, "Sélectionne un bot connecté.", 0, 0, 0, 0, 0, null, null));
            return;
        }
        if (ExpQuantityMonster is not { } card || !ExpMonsters.Contains(card))
        {
            ApplyQuantityDecision(new QuantityFarmDecision(QuantityFarmStatus.Refused, "Choisis un monstre de la zone.", 0, 0, 0, 0, 0, null, null));
            return;
        }
        if (ExpQuantityDrop is not { } drop)
        {
            ApplyQuantityDecision(new QuantityFarmDecision(QuantityFarmStatus.Refused, "Choisis un drop de ce monstre.", 0, 0, 0, 0, 0, null, null));
            return;
        }
        if (!QuantityFarmPlanner.TryQuantity(ExpQuantityTarget, out long quantity))
        {
            ApplyQuantityDecision(new QuantityFarmDecision(QuantityFarmStatus.Refused, "Quantité entre 1 et 1 000 000 000.", 0, 0, 0, 0, 0, null, null));
            return;
        }
        var catalog = new List<(int MonsterId, long ObjectId)>();
        if (Stores.Launch.GameData is { } db)
            foreach (var row in db.MonsterDrops(card.Monster.Id))
                catalog.Add((card.Monster.Id, row.Gid));
        var decision = LaunchQuantity(bot, new QuantityFarmRequest
        {
            Kind = QuantityFarmKind.Monster,
            ItemGid = drop.ObjectId,
            Quantity = quantity,
            MonsterId = card.Monster.Id,
            DisplayName = card.Name + " · " + drop.ObjectId.ToString(CultureInfo.InvariantCulture),
            RouteMapIds = _expMaps.ToArray(),
            AcceptBagOnly = acceptBagOnly,
            DropCatalog = catalog,
            MinMonsters = (int)ExpMinGroup,
            MaxMonsters = (int)ExpMaxGroup,
            MaxPods = ExpStopPods < 1 ? 95 : (int)Math.Clamp(ExpStopPods, 1d, 100d)
        });
        ApplyQuantityDecision(decision);
    }

    /// <summary>Départ du farm ressource depuis l'atelier éco. Le circuit est déjà choisi ; cette méthode ne lance pas le script infini.</summary>
    public QuantityFarmDecision StartResourceQuantity(int itemId, string resourceName, long quantity, bool acceptBagOnly, IReadOnlyList<long> mapIds)
    {
        if (SelectedBot is not { Pid: > 0 } bot)
        {
            var missing = new QuantityFarmDecision(QuantityFarmStatus.Refused, "Sélectionne un bot connecté.", itemId, quantity, 0, 0, 0, null, null);
            ApplyQuantityDecision(missing);
            return missing;
        }
        var decision = LaunchQuantity(bot, new QuantityFarmRequest
        {
            Kind = QuantityFarmKind.Resource,
            ItemGid = itemId,
            Quantity = quantity,
            DisplayName = resourceName ?? "",
            RouteMapIds = mapIds ?? Array.Empty<long>(),
            AcceptBagOnly = acceptBagOnly,
            MaxPods = 95
        });
        ApplyQuantityDecision(decision);
        return decision;
    }

    public bool StopQuantityFarm()
    {
        bool running = _quantityFarmActive && LuaRunning;
        if (running) StopLua();
        ClearQuantityFarm(running);
        return running;
    }

    private void ClearQuantityFarm(bool stopped)
    {
        if (!_quantityFarmActive && !ExpQuantityBagOnlyOffer) return;
        _quantityFarmActive = false;
        ExpQuantityBagOnlyOffer = false;
        if (stopped) ExpQuantityStatus = "Farm quantité arrêté.";
    }

    private void ApplyQuantityDecision(QuantityFarmDecision decision)
    {
        ExpQuantityStatus = decision.Message;
        ExpQuantityBagOnlyOffer = decision.Status == QuantityFarmStatus.NeedsBagOnly;
        if ((decision.Status is QuantityFarmStatus.Completed or QuantityFarmStatus.NeedsBagOnly or QuantityFarmStatus.Ready)
            && SelectedBot is { Pid: > 0 } bot)
            QuantityFarmBoard.Note(bot.Pid, new QuantityFarmProgress(decision.ItemGid, decision.Owned, decision.Target, decision.Remaining));
        OnPropertyChanged(nameof(ExpQuantityProgress));
    }

    // LiveStockEvidence est privé : seul le sac déjà reçu est admis. Banque et havre-sac restent non confirmés.
    private QuantityFarmDecision LaunchQuantity(BotRowViewModel bot, QuantityFarmRequest request)
    {
        uint pid = (uint)bot.Pid;
        QuantityFarmDecision Refuse(string message) => new(QuantityFarmStatus.Refused, message, request.ItemGid, request.Quantity, 0, 0, 0, null, null);
        if (!Stores.Launch.Inventory.IsSeeded(pid))
            return Refuse("Sac non confirmé : l'inventaire du personnage n'est pas encore reçu.");
        long characterId = Stores.Launch.CharacterId(pid);
        if (characterId <= 0) return Refuse("Personnage non confirmé.");
        string account = bot.Row.Model.Id;
        if (string.IsNullOrWhiteSpace(account) || account.Length > 100) return Refuse("Compte non confirmé.");
        string version = Stores.Launch.LotMGameVersion;
        if (string.IsNullOrWhiteSpace(version)) return Refuse("Version du jeu non confirmée.");
        var stacks = new List<QuantityFarmStack>();
        foreach (var item in Stores.Launch.Inventory.Items(pid))
        {
            if (item.Gid <= 0 || item.Qty <= 0) continue;
            stacks.Add(new QuantityFarmStack(item.Gid, "uid:" + item.Uid.ToString(CultureInfo.InvariantCulture), item.Qty, item.Position));
        }
        var filled = request with
        {
            CanGather = LicenseService.HasPermission(LicenseService.Perm.Gather),
            CanFight = LicenseService.HasPermission(LicenseService.Perm.Fight),
            FollowerLocked = bot.FollowerLocked,
            SocketBlock = QuantitySocketBlock(bot, request.Kind == QuantityFarmKind.Monster),
            HasMap = Stores.Launch.CurrentMap(pid) is > 0,
            LevelAllowed = !Stores.Launch.LevelBlocked(pid),
            BottingAuthorized = LicenseService.CanBot,
            BagStacks = stacks,
            Observations = null,
            AccountKey = account,
            CharacterId = characterId,
            GameVersion = version,
            Now = DateTimeOffset.UtcNow,
            MaxPods = request.MaxPods < 1 ? 95 : Math.Clamp(request.MaxPods, 1, 100)
        };
        QuantityFarmDecision decision;
        try { decision = QuantityFarmPlanner.Evaluate(filled, QuantityFarmScript.Build); }
        catch (ArgumentException) { return Refuse("Farm refusé : quantité ou stock invalide."); }
        if (decision.Status != QuantityFarmStatus.Ready || decision.Script is not { Length: > 0 } script)
            return decision;
        if (!StartNamedScript(pid, QuantityScriptTitle(filled.DisplayName), script))
            return decision with { Status = QuantityFarmStatus.Refused, Message = "Le script n'a pas démarré (carte, licence, suiveur ou script déjà en cours).", Script = null };
        _quantityFarmActive = true;
        return decision with { Message = $"Farm lancé. Sac {decision.Owned}, reste {decision.Remaining}. Arrêt à l'objectif, ou avec Arrêter." };
    }

    private string? QuantitySocketBlock(BotRowViewModel bot, bool requireFight)
    {
        bool socket = bot.Row.IsSocketSession || LaunchService.IsSocketPid((uint)bot.Pid);
        if (!socket) return null;
        uint pid = (uint)bot.Pid;
        string? blocked = ExpeditionSocketSessionGate.StartBlockReason(new ExpeditionSocketSessionSnapshot(
            IsSocketSession: true,
            AccountRunning: bot.Row.Running,
            SessionParked: bot.Row.SocketParked,
            SessionBackoff: bot.Row.SocketBackoff,
            Reconnecting: bot.Row.ReconnectInPlace,
            WaitingForNewSession: _socketWaiting.ContainsKey(bot.Pid),
            GameLinkDown: Stores.Launch.IsLinkDown(pid),
            HasServerMap: Stores.Launch.CurrentMap(pid) is > 0,
            HasFightPermission: !requireFight || LicenseService.HasPermission(LicenseService.Perm.Fight),
            BottingAuthorized: LicenseService.CanBot,
            LevelAllowed: !Stores.Launch.LevelBlocked(pid),
            IsFollowerLocked: bot.FollowerLocked,
            InjectorRegistered: Stores.Launch.HasSocketInjector(pid)));
        if (blocked is not null) return blocked;
        var control = _accounts.ObserveSocketControl(bot.Row);
        return control.State == "channel-registered" ? null
            : $"Socket : canal de commandes indisponible ({control.State}). {control.Explanation}";
    }

    private static string QuantityScriptTitle(string? name)
    {
        var sb = new StringBuilder("Quantité ");
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (char c in name)
            {
                if (sb.Length >= 48) break;
                sb.Append(c < ' ' ? ' ' : c);
            }
        }
        return sb.ToString();
    }
}
