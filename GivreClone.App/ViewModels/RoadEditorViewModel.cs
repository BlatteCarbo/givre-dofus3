using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Controls;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de la liste des points du trajet (carte, actions, état de l'étape vers le point suivant).</summary>
public sealed partial class RoadWaypointRow : ObservableObject
{
    private static readonly IBrush OkBrush = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush TeleportBrush = new ImmutableSolidColorBrush(Color.Parse("#F2A541"));
    private static readonly IBrush BadBrush = new ImmutableSolidColorBrush(Color.Parse("#E05A50"));
    private static readonly IBrush NoneBrush = new ImmutableSolidColorBrush(Color.Parse("#8892A0"));

    public RoadEditorViewModel Owner { get; }
    public long MapId { get; }
    public (int X, int Y)? Coord { get; }
    public string CoordText { get; }
    public string Place { get; }

    [ObservableProperty] public partial int Number { get; set; }
    [ObservableProperty] public partial bool Gather { get; set; }
    [ObservableProperty] public partial bool Fight { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial bool Duplicate { get; set; }
    [ObservableProperty] public partial RoadLegState? Leg { get; set; }
    [ObservableProperty] public partial int LegHops { get; set; }

    public string LegText => Leg switch
    {
        RoadLegState.Ok => Loc.Fmt("L.Road.LegOk", LegHops),
        RoadLegState.Teleport => Loc.T("L.Road.LegTeleport"),
        RoadLegState.Unreachable => Loc.T("L.Road.LegBad"),
        RoadLegState.Unknown => Loc.T("L.Road.LegUnknown"),
        _ => Loc.T("L.Road.LegEnd"),
    };
    public IBrush LegBrush => Leg switch { RoadLegState.Ok => OkBrush, RoadLegState.Teleport => TeleportBrush, null => NoneBrush, _ => BadBrush };
    partial void OnLegChanged(RoadLegState? value) { OnPropertyChanged(nameof(LegText)); OnPropertyChanged(nameof(LegBrush)); }
    partial void OnLegHopsChanged(int value) => OnPropertyChanged(nameof(LegText));
    private readonly bool _ready;   // pas de revalidation pendant la construction (la ligne n'est pas encore dans la liste)
    partial void OnGatherChanged(bool value) { if (_ready) Owner.Changed(); }
    partial void OnFightChanged(bool value) { if (_ready) Owner.Changed(); }

    public RoadWaypointRow(RoadEditorViewModel owner, long mapId, (int X, int Y)? coord, string place, bool gather, bool fight)
    {
        Owner = owner; MapId = mapId; Coord = coord; Place = place;
        CoordText = coord is (int x, int y) ? $"{x},{y}" : mapId.ToString(CultureInfo.InvariantCulture);
        Gather = gather; Fight = fight;
        _ready = true;
    }
}

/// <summary>
/// Éditeur VISUEL de trajet (onglet Trajets) : on clique les cartes sur la mini-carte du monde au lieu de parcourir le trajet à la main.
/// Chaque étape (point -&gt; point suivant) est vérifiée sur le graphe de navigation du jeu (vert = à pied, orange = zaap / havre-sac,
/// rouge = injoignable), le trajet se génère en un script déclaratif (<see cref="RoadDesign"/>) qu'on envoie dans l'éditeur Lua,
/// qu'on enregistre dans la bibliothèque de scripts ou qu'on lance directement. Un trajet généré se rouvre à l'identique.
/// </summary>
public sealed partial class RoadEditorViewModel : ObservableObject
{
    private readonly Func<uint> _pid;
    private readonly Func<string, bool> _apply;
    private readonly Func<string> _currentScript;
    private readonly Action _run;
    private readonly Func<HdvPlan?> _hdvPlan;
    private bool _bulk;   // modifications groupées : une seule revalidation à la fin

    public RoadEditorViewModel(Func<uint> pid, Func<string, bool> apply, Func<string> currentScript, Action run, Func<HdvPlan?> hdvPlan)
    {
        _pid = pid; _apply = apply; _currentScript = currentScript; _run = run; _hdvPlan = hdvPlan;
        Waypoints.CollectionChanged += (_, _) => Changed();
    }

    public ObservableCollection<RoadWaypointRow> Waypoints { get; } = new();
    public bool HasWaypoints => Waypoints.Count > 0;
    public bool IsEmpty => Waypoints.Count == 0;

    [ObservableProperty] public partial string RoadName { get; set; } = "Trajet";
    [ObservableProperty] public partial bool Loop { get; set; } = true;
    [ObservableProperty] public partial bool AutoBank { get; set; } = true;
    [ObservableProperty] public partial decimal? MaxPods { get; set; } = 90;
    [ObservableProperty] public partial double MinMonsters { get; set; } = 1;
    [ObservableProperty] public partial double MaxMonsters { get; set; } = 8;
    partial void OnLoopChanged(bool value) => Changed();
    partial void OnAutoBankChanged(bool value) => Changed();
    /// <summary>Pods pleins : HDV cible du perso + liste de remise en vente (onglet HDV) avant la banque.</summary>
    [ObservableProperty] public partial bool UseHdv { get; set; }
    partial void OnUseHdvChanged(bool value) => Changed();
    [ObservableProperty] public partial string HdvText { get; set; } = "";
    partial void OnMinMonstersChanged(double value) { if (value > MaxMonsters) MaxMonsters = value; }
    partial void OnMaxMonstersChanged(double value) { if (value < MinMonsters) MinMonsters = value; }

    [ObservableProperty] public partial RouteMapModel? MapModel { get; set; }
    [ObservableProperty] public partial (int X, int Y, int Seq)? Center { get; set; }
    [ObservableProperty] public partial RouteCell? HoverCell { get; set; }
    partial void OnHoverCellChanged(RouteCell? value) => OnPropertyChanged(nameof(HoverText));
    public string HoverText => HoverCell is { } c ? $"{c.X},{c.Y}" + (PlaceAt(c) is { Length: > 0 } p ? " · " + p : "") : Loc.T("L.Road.MapHint");

    [ObservableProperty] public partial RoadWaypointRow? SelectedWaypoint { get; set; }
    partial void OnSelectedWaypointChanged(RoadWaypointRow? oldValue, RoadWaypointRow? newValue)
    {
        if (oldValue is not null) oldValue.Selected = false;
        if (newValue is not null) { newValue.Selected = true; if (newValue.Coord is (int x, int y)) CenterAt(x, y); }
        RebuildMap();
    }

    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool HasProblems { get; set; }
    public ObservableCollection<string> Issues { get; } = new();
    public bool HasIssues => Issues.Count > 0;
    [ObservableProperty] public partial string BankText { get; set; } = "";

    private int _world = 1;
    private int _centerSeq;
    private (long Map, int Door, long Inside)? _bank;

    // ---------------------------------------------------------------- ouverture / carte

    /// <summary>Premier affichage de l'onglet : calque et centrage sur le perso (ou le 1er point).</summary>
    public void Open()
    {
        uint pid = _pid();
        var l = Stores.Launch;
        if (Waypoints.Count == 0 && pid > 0 && l.CurrentMap(pid) is long m)
        {
            _world = l.WorldMapOf(m) ?? 1;
            if (l.MapCoordOf(m) is (int x, int y)) CenterAt(x, y);
        }
        RebuildMap();
    }

    private void CenterAt(int x, int y) => Center = (x, y, ++_centerSeq);

    private (int X, int Y)? BotCoord()
    {
        uint pid = _pid();
        return pid > 0 && Stores.Launch.CurrentMap(pid) is long m && Stores.Launch.WorldMapOf(m) == _world ? Stores.Launch.MapCoordOf(m) : null;
    }

    private static string PlaceOf(long map)
    {
        var l = Stores.Launch;
        int sub = l.SubAreaOf(map);
        try { return sub > 0 && l.GameData?.SubArea(sub) is { } s ? s.Name : ""; } catch { return ""; }
    }

    private string PlaceAt(RouteCell c) => Stores.Launch.MapIdAt(c.X, c.Y, _world) is long m ? PlaceOf(m) : "";

    private void RebuildMap()
    {
        var coords = Stores.Launch.WorldCoords(_world);
        var pts = new List<RoutePoint>();
        var lines = new List<RouteLine>();
        for (int i = 0; i < Waypoints.Count; i++)
        {
            var w = Waypoints[i];
            if (w.Coord is not (int x, int y)) continue;
            bool problem = w.Duplicate || w.Leg is RoadLegState.Unreachable or RoadLegState.Unknown;
            pts.Add(new RoutePoint(x, y, i + 1, w.Selected, w.Gather, w.Fight, problem));
            bool hasNext = i + 1 < Waypoints.Count || (Loop && Waypoints.Count > 1);
            if (hasNext && Waypoints[(i + 1) % Waypoints.Count].Coord is (int nx, int ny) && w.Leg is { } st)
                lines.Add(new RouteLine(x, y, nx, ny, st));
        }
        HashSet<(int, int)>? marks = null;
        if (_bank is { } b && Stores.Launch.MapCoordOf(b.Map) is (int bx, int by)) marks = new() { (bx, by) };
        MapModel = new RouteMapModel(coords, pts, lines, BotCoord(), marks);
    }

    // ---------------------------------------------------------------- édition

    /// <summary>Clic gauche sur la mini-carte : sélectionne le point de cette carte, sinon l'ajoute à la fin du trajet.</summary>
    [RelayCommand]
    private void CellClicked(RouteCell? c)
    {
        if (c is null) return;
        var existing = Waypoints.FirstOrDefault(w => w.Coord is (int x, int y) && x == c.X && y == c.Y);
        if (existing is not null) { SelectedWaypoint = existing; return; }
        if (Stores.Launch.MapIdAt(c.X, c.Y, _world) is not long map) { Status = Loc.Fmt("L.Road.NoMapAt", c.X, c.Y); return; }
        Add(map);
    }

    /// <summary>Clic droit sur la mini-carte : retire le point de cette carte.</summary>
    [RelayCommand]
    private void CellRemove(RouteCell? c)
    {
        if (c is null) return;
        var hit = Waypoints.LastOrDefault(w => w.Coord is (int x, int y) && x == c.X && y == c.Y);
        if (hit is not null) Remove(hit);
    }

    private RoadWaypointRow Row(long map, bool gather, bool fight)
        => new(this, map, Stores.Launch.MapCoordOf(map), PlaceOf(map), gather, fight);

    private void Add(long map)
    {
        if (Waypoints.Count == 0) _world = Stores.Launch.WorldMapOf(map) ?? _world;
        // Par défaut : les mêmes actions que le point précédent (on enchaîne des cartes de récolte ou de combat).
        var prev = Waypoints.LastOrDefault();
        var row = Row(map, prev?.Gather ?? true, prev?.Fight ?? false);
        Waypoints.Add(row);
        SelectedWaypoint = row;
        Status = Loc.Fmt("L.Road.Added", row.CoordText, row.Place);
    }

    [RelayCommand]
    private void AddHere()
    {
        uint pid = _pid();
        if (pid == 0 || Stores.Launch.CurrentMap(pid) is not long m || m == 0) { Status = Loc.T("L.Road.NoBot"); return; }
        if (Waypoints.Count > 0 && Waypoints[^1].MapId == m) { Status = Loc.T("L.Road.AlreadyLast"); return; }
        Add(m);
    }

    [RelayCommand]
    private void Remove(RoadWaypointRow? row)
    {
        if (row is null) return;
        int i = Waypoints.IndexOf(row);
        if (i < 0) return;
        if (ReferenceEquals(SelectedWaypoint, row)) SelectedWaypoint = null;
        Waypoints.RemoveAt(i);
    }

    [RelayCommand] private void MoveUp(RoadWaypointRow? row) => Move(row, -1);
    [RelayCommand] private void MoveDown(RoadWaypointRow? row) => Move(row, +1);
    private void Move(RoadWaypointRow? row, int delta)
    {
        if (row is null) return;
        int i = Waypoints.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= Waypoints.Count) return;
        Waypoints.Move(i, j);
    }

    [RelayCommand]
    private void Reverse()
    {
        if (Waypoints.Count < 2) return;
        Replace(Waypoints.Reverse().Select(w => (w.MapId, w.Gather, w.Fight)).ToList());
        Status = Loc.T("L.Road.Reversed");
    }

    /// <summary>Réordonne au plus court (plus proche voisin depuis le 1er point) : moins d'allers-retours.</summary>
    [RelayCommand]
    private void Optimize()
    {
        if (Waypoints.Count < 3) return;
        var before = Waypoints.Select(w => new RoadWaypoint(w.MapId, w.Gather, w.Fight)).ToList();
        var after = RoadDesign.Optimize(before, id => Stores.Launch.MapCoordOf(id));
        Replace(after.Select(w => (w.MapId, w.Gather, w.Fight)).ToList());
        Status = Loc.T("L.Road.Optimized");
    }

    [RelayCommand]
    private void Clear()
    {
        SelectedWaypoint = null;
        Waypoints.Clear();
        Status = "";
    }

    [RelayCommand]
    private void CenterOnBot()
    {
        uint pid = _pid();
        if (pid > 0 && Stores.Launch.CurrentMap(pid) is long m && Stores.Launch.MapCoordOf(m) is (int x, int y))
        {
            _world = Stores.Launch.WorldMapOf(m) ?? _world;
            CenterAt(x, y);
            RebuildMap();
        }
        else Status = Loc.T("L.Road.NoBot");
    }

    [RelayCommand] private void AllGather(string? on) { _bulk = true; foreach (var w in Waypoints) w.Gather = on == "1"; _bulk = false; Changed(); }
    [RelayCommand] private void AllFight(string? on) { _bulk = true; foreach (var w in Waypoints) w.Fight = on == "1"; _bulk = false; Changed(); }

    private void Replace(IReadOnlyList<(long Map, bool Gather, bool Fight)> items)
    {
        _bulk = true;
        SelectedWaypoint = null;
        Waypoints.Clear();
        foreach (var (m, g, f) in items) Waypoints.Add(Row(m, g, f));
        _bulk = false;
        Changed();
    }

    // ---------------------------------------------------------------- vérification

    /// <summary>Revalide le trajet (graphe du jeu, doublons, banque) et redessine la carte.</summary>
    internal void Changed()
    {
        if (_bulk) return;
        for (int i = 0; i < Waypoints.Count; i++) Waypoints[i].Number = i + 1;
        OnPropertyChanged(nameof(HasWaypoints)); OnPropertyChanged(nameof(IsEmpty));
        var l = Stores.Launch;
        uint pid = _pid();
        var wps = Waypoints.Select(w => new RoadWaypoint(w.MapId, w.Gather, w.Fight)).ToList();
        var legs = RoadDesign.Validate(wps, Loop, (a, b) => l.WalkHops(pid, a, b), m => l.WorldGraphKnows((int)m), m => l.WorldMapOf(m));
        var dup = new HashSet<int>(RoadDesign.Duplicates(wps));
        foreach (var w in Waypoints) { w.Leg = null; w.LegHops = 0; }
        for (int i = 0; i < Waypoints.Count; i++) Waypoints[i].Duplicate = dup.Contains(i);
        foreach (var leg in legs) { Waypoints[leg.From].Leg = leg.State; Waypoints[leg.From].LegHops = leg.Hops; }

        Issues.Clear();
        if (dup.Count > 0) Issues.Add(Loc.Fmt("L.Road.IssueDuplicate", string.Join(", ", dup.Select(i => i + 1))));
        foreach (var leg in legs.Where(g => g.State is RoadLegState.Unreachable or RoadLegState.Unknown))
            Issues.Add(Loc.Fmt(leg.State == RoadLegState.Unknown ? "L.Road.IssueUnknown" : "L.Road.IssueUnreachable", leg.From + 1, leg.To + 1));
        if (Waypoints.Count > 0 && !Waypoints.Any(w => w.Gather || w.Fight)) Issues.Add(Loc.T("L.Road.IssueNothing"));
        if (Loop && legs.Count > 0 && legs[^1].State == RoadLegState.Teleport) Issues.Add(Loc.T("L.Road.IssueLoopTeleport"));

        _bank = AutoBank && Waypoints.Count > 0 ? BotsViewModel.NearestBank(Waypoints[0].MapId) : null;
        BankText = !AutoBank ? Loc.T("L.Road.BankOff")
                 : _bank is { } b ? Loc.Fmt("L.Road.BankAt", Stores.Launch.MapCoordOf(b.Map) is (int bx, int by) ? $"{bx},{by}" : b.Map.ToString(CultureInfo.InvariantCulture), PlaceOf(b.Map))
                 : Waypoints.Count > 0 ? Loc.T("L.Road.BankNone") : "";
        if (AutoBank && Waypoints.Count > 0 && _bank is null) Issues.Add(Loc.T("L.Road.BankNone"));
        var hdv = _hdvPlan();
        HdvText = BotsViewModel.HdvPlanText(hdv);
        if (UseHdv && hdv is null && Waypoints.Count > 0) Issues.Add(Loc.T("L.Exp.HdvMissing"));

        int ok = legs.Count(g => g.State == RoadLegState.Ok), tp = legs.Count(g => g.State == RoadLegState.Teleport);
        int bad = legs.Count - ok - tp;
        int walk = legs.Where(g => g.State == RoadLegState.Ok).Sum(g => Math.Max(0, g.Hops));
        Summary = Waypoints.Count == 0 ? "" : Loc.Fmt("L.Road.Summary", Waypoints.Count, walk, tp, bad);
        HasProblems = bad > 0 || dup.Count > 0;
        OnPropertyChanged(nameof(HasIssues));
        RebuildMap();
    }

    // ---------------------------------------------------------------- génération / import

    private RoadOptions Options() => new(
        string.IsNullOrWhiteSpace(RoadName) ? "Trajet" : RoadName.Trim(), Loop, (int)(MaxPods ?? 90), (int)MinMonsters, (int)MaxMonsters,
        null, _bank?.Map ?? 0, _bank?.Door ?? -1, _bank?.Inside ?? 0, UseHdv ? _hdvPlan() : null);

    private string? Build()
    {
        if (Waypoints.Count == 0) { Status = Loc.T("L.Road.Empty"); return null; }
        var wps = Waypoints.Select(w => new RoadWaypoint(w.MapId, w.Gather, w.Fight)).ToList();
        var labels = Waypoints.GroupBy(w => w.MapId).ToDictionary(g => g.Key, g => (g.First().CoordText + " " + g.First().Place).Trim());
        return RoadDesign.Generate(wps, Options(), id => labels.TryGetValue(id, out var t) ? t : id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Envoie le trajet généré dans l'éditeur Lua du perso (relecture, Ctrl+Z possible).</summary>
    [RelayCommand]
    private void SendToEditor()
    {
        if (Build() is not { } lua) return;
        Status = _apply(lua) ? Loc.T("L.Road.Sent") : Loc.T("L.Road.SendRefused");
    }

    /// <summary>Génère, envoie dans l'éditeur et lance tout de suite. Refusé si une étape est injoignable (le bot resterait bloqué).</summary>
    [RelayCommand]
    private void Run()
    {
        if (HasProblems) { Status = Loc.T("L.Road.FixFirst"); return; }
        if (Build() is not { } lua) return;
        if (!_apply(lua)) { Status = Loc.T("L.Road.SendRefused"); return; }
        _run();
        Status = Loc.T("L.Road.Started");
    }

    /// <summary>Enregistre le trajet dans la bibliothèque de scripts (dossier scripts, fichier Road_&lt;nom&gt;.lua).</summary>
    [RelayCommand]
    private void Save()
    {
        if (Build() is not { } lua) return;
        try
        {
            string dir = Path.Combine(MZP19LocalClone.DataRoot, "scripts");
            Directory.CreateDirectory(dir);
            string safe = new string(RoadName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
            if (safe.Length == 0) safe = "Trajet";
            string path = Path.Combine(dir, "Road_" + safe + ".lua");
            File.WriteAllText(path, lua, new System.Text.UTF8Encoding(false));
            Status = Loc.Fmt("L.Road.Saved", path);
        }
        catch (Exception e) { Status = Loc.Fmt("L.Road.SaveFailed", e.Message); }
    }

    /// <summary>Rouvre le trajet affiché dans l'éditeur Lua (généré ici, enregistré par l'enregistreur ou écrit à la main).</summary>
    [RelayCommand]
    private void ImportFromEditor()
    {
        var l = Stores.Launch;
        string script = _currentScript();
        var imp = RoadDesign.Parse(script, (x, y) => l.MapIdAt(x, y, _world));
        if (imp is null) { Status = Loc.T("L.Road.ImportNothing"); return; }
        var o = imp.Options;
        if (imp.Waypoints.Count > 0) _world = l.WorldMapOf(imp.Waypoints[0].MapId) ?? _world;
        RoadName = o.Name; Loop = o.Loop; MaxPods = o.MaxPods; MinMonsters = o.MinMonsters; MaxMonsters = o.MaxMonsters;
        AutoBank = o.BankMap > 0 || AutoBank;
        UseHdv = script.Contains("sellAtHdv", StringComparison.Ordinal);
        Replace(imp.Waypoints.Select(w => (w.MapId, w.Gather, w.Fight)).ToList());
        if (Waypoints.FirstOrDefault()?.Coord is (int x, int y)) CenterAt(x, y);
        Status = Loc.Fmt("L.Road.Imported", imp.Waypoints.Count) + (imp.Notes.Count > 0 ? " — " + string.Join(" ; ", imp.Notes.Take(3)) : "");
    }

    /// <summary>Appelé au tick de l'UI (onglet visible) : suit la position du perso sur la mini-carte.</summary>
    public void Tick()
    {
        if (MapModel is { } m && m.Bot != BotCoord()) RebuildMap();
    }
}
