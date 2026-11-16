using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Controls;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Simulateur de tour de combat (modal). L'utilisateur pose son perso + des ennemis (PV réglables) sur une grille
/// isométrique, règle PA/PM/portée/tour, puis « Simuler » : le <see cref="CombatSimulator"/> rejoue la rotation du
/// profil actif (dégâts + morts + retarget + multi-tours) et le VM ANIME la séquence pas-à-pas (déplacements, faisceaux,
/// chiffres de dégâts, barres de PV) avec un journal.
/// </summary>
public partial class CombatSimViewModel : ObservableObject
{
    public enum EditMode { Me, Enemy, Erase }

    /// <summary>Un ennemi posé sur le plateau : case + PV max (éditable).</summary>
    public sealed partial class EnemySpot : ObservableObject
    {
        public int Cell { get; init; }
        [ObservableProperty] public partial int MaxHp { get; set; }
    }

    private uint _pid;
    private CombatProfile _profile = new();
    private List<CombatSimulator.SimSpell> _rotation = new();
    private readonly DispatcherTimer _timer;

    // Un pas d'animation = un état complet du plateau (positions + PV + dégât affiché + budget).
    private sealed class Frame
    {
        public int MyCell;
        public IReadOnlyList<int> Move = Array.Empty<int>();
        public int Beam = -1;
        public IReadOnlyList<IsoGrid.EnemyToken> Enemies = Array.Empty<IsoGrid.EnemyToken>();
        public int DmgCell = -1, DmgAmount, DmgElement = -1, DmgSeq;
        public int Ap, Mp, TurnNo = 1, Alive;
        public string Log = "";
    }
    private List<Frame> _frames = new();
    private sealed class LiveEnemy { public int Cell; public int Hp; public int Max; public bool Alive = true; }

    public CombatSimViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _timer.Tick += (_, _) => Advance();
        // Plateau par défaut : moi au centre + 2 ennemis à portée moyenne.
        MyCell = 279;
        foreach (var c in FindCellsAtDistance(MyCell, new[] { 5, 6 })) Enemies.Add(new EnemySpot { Cell = c, MaxHp = DefaultEnemyHp });
        RenderInitial();
    }

    // --- Plateau éditable ---
    [ObservableProperty] public partial int MyCell { get; set; }
    public ObservableCollection<EnemySpot> Enemies { get; } = new();

    [ObservableProperty] public partial EditMode Mode { get; set; } = EditMode.Enemy;
    public bool ModeMe => Mode == EditMode.Me;
    public bool ModeEnemy => Mode == EditMode.Enemy;
    public bool ModeErase => Mode == EditMode.Erase;
    partial void OnModeChanged(EditMode value)
    {
        OnPropertyChanged(nameof(ModeMe)); OnPropertyChanged(nameof(ModeEnemy)); OnPropertyChanged(nameof(ModeErase));
        OnPropertyChanged(nameof(GridCursor));
    }

    // Curseur de la grille SELON le mode d'édition (retour visuel : viseur pour placer, main pour se placer, interdit pour gommer).
    private static readonly Cursor CurCross = new(StandardCursorType.Cross);
    private static readonly Cursor CurHand = new(StandardCursorType.Hand);
    private static readonly Cursor CurErase = new(StandardCursorType.No);
    public Cursor GridCursor => Mode switch { EditMode.Me => CurHand, EditMode.Erase => CurErase, _ => CurCross };

    // --- Budget de tour (réglable) ---
    [ObservableProperty] public partial int SelfAp { get; set; } = 6;
    [ObservableProperty] public partial int SelfMp { get; set; } = 3;
    [ObservableProperty] public partial int SelfRange { get; set; }
    [ObservableProperty] public partial int TurnNo { get; set; } = 1;

    // --- Ennemis + multi-tours ---
    [ObservableProperty] public partial int DefaultEnemyHp { get; set; } = 500;
    [ObservableProperty] public partial bool MultiTurn { get; set; } = true;
    [ObservableProperty] public partial int MaxTurns { get; set; } = 15;
    partial void OnDefaultEnemyHpChanged(int value)
    {
        foreach (var e in Enemies) e.MaxHp = Math.Max(1, value);
        StopAndReset();
    }
    partial void OnMultiTurnChanged(bool value) => StopAndReset();

    // --- Rendu (bindé à IsoGrid) ---
    [ObservableProperty] public partial int RenderMyCell { get; set; }
    [ObservableProperty] public partial IReadOnlyList<IsoGrid.EnemyToken> RenderEnemies { get; set; } = Array.Empty<IsoGrid.EnemyToken>();
    [ObservableProperty] public partial IReadOnlyList<int> MoveCells { get; set; } = Array.Empty<int>();
    [ObservableProperty] public partial int BeamTarget { get; set; } = -1;
    [ObservableProperty] public partial int DamageCell { get; set; } = -1;
    [ObservableProperty] public partial int DamageAmount { get; set; }
    [ObservableProperty] public partial int DamageElement { get; set; } = -1;
    [ObservableProperty] public partial int DamageSeq { get; set; }   // compteur monotone : re-déclenche l'anim même pour un dégât identique

    // --- Lecture / état ---
    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial bool HasResult { get; set; }
    [ObservableProperty] public partial string Hud { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    public ObservableCollection<string> Log { get; } = new();

    private int _stepIndex = -1;   // -1 = état initial ; sinon index du dernier pas joué

    /// <summary>Appelé par BotsViewModel à l'ouverture : rotation résolue + profil actif du bot édité.</summary>
    public void Setup(uint pid, CombatProfile profile, IReadOnlyList<CombatSimulator.SimSpell> rotation, int selfRange)
    {
        _pid = pid;
        _profile = profile ?? new CombatProfile();
        _rotation = rotation?.ToList() ?? new();
        if (selfRange > 0) SelfRange = selfRange;
        // Défaut PV « intelligent » : ~3 lancers du meilleur sort offensif -> mort et retarget visibles vite.
        int best = _rotation.Where(s => !s.IsBuff).Select(s => s.AvgDamage).DefaultIfEmpty(0).Max();
        DefaultEnemyHp = best > 0 ? Math.Clamp(best * 3, 100, 5000) : 500;
        foreach (var e in Enemies) e.MaxHp = DefaultEnemyHp;
        StopAndReset();
    }

    // ===== Édition du plateau =====
    [RelayCommand] private void SetMode(string mode)
        => Mode = mode switch { "me" => EditMode.Me, "erase" => EditMode.Erase, _ => EditMode.Enemy };

    [RelayCommand]
    private void Cell(int cell)
    {
        if ((uint)cell >= MapPathfinder.CellCount) return;
        switch (Mode)
        {
            case EditMode.Me:
                if (Enemies.All(e => e.Cell != cell)) MyCell = cell;
                break;
            case EditMode.Enemy:
                if (cell != MyCell && Enemies.All(e => e.Cell != cell)) Enemies.Add(new EnemySpot { Cell = cell, MaxHp = DefaultEnemyHp });
                break;
            case EditMode.Erase:
                if (Enemies.FirstOrDefault(e => e.Cell == cell) is { } rm) Enemies.Remove(rm);
                break;
        }
        StopAndReset();   // le plateau a changé -> résultat périmé, on revient à l'état initial
    }

    // ===== Lecture =====
    [RelayCommand]
    private void Run()
    {
        var state = new CombatSimulator.SimState
        {
            SelfActorId = 1,
            SelfCell = MyCell,
            Ap = Math.Max(0, SelfAp),
            Mp = Math.Max(0, SelfMp),
            SelfRange = Math.Max(0, SelfRange),
            TurnNo = Math.Max(1, TurnNo),
            Enemies = Enemies.Select((e, i) => new CombatSimulator.SimFighter
            { ActorId = -(i + 1), Cell = e.Cell, MaxHp = Math.Max(1, e.MaxHp), Hp = Math.Max(1, e.MaxHp), Alive = true }).ToList(),
        };
        var steps = MultiTurn
            ? CombatSimulator.SimulateFight(state, _rotation, _profile, Math.Max(1, MaxTurns))
            : CombatSimulator.SimulateTurn(state, _rotation, _profile);
        BuildFrames(state, steps);
        HasResult = true;
        _stepIndex = -1;
        ApplyFrame(0);
        Log.Clear();

        int casts = steps.Count(s => s.Kind == CombatSimulator.SimStepKind.Cast);
        int moves = steps.Count(s => s.Kind == CombatSimulator.SimStepKind.Move);
        int kills = steps.Count(s => s.Killed);
        int turns = steps.Count == 0 ? 1 : steps.Max(s => s.TurnNo);
        bool cleared = Enemies.Count > 0 && _frames.Count > 0 && _frames[^1].Alive == 0;
        Summary = cleared ? Loc.Fmt("L.Bots.Sim.SummaryCleared", turns, kills)
                          : Loc.Fmt("L.Bots.Sim.Summary", casts, moves, kills);
        // Repère : meilleure rafale possible sur UNE cible avec ces PA (sans géométrie) -> montre si l'ordre des priorités perd des dégâts.
        var burst = Services.Combat.BurstPlanner.Best(_rotation
            .Where(r => r.Cfg is { Enabled: true, OnMe: false, IsSummon: false } && r.Cfg.TargetKind != SpellTargetKind.Ally)
            .Select(r => new Services.Combat.BurstSpell(r.Name, r.Ap, r.AvgDamage,
                new[] { r.MaxPerTarget, r.MaxPerTurn, r.Cfg.PerTurn }.Where(x => x > 0).DefaultIfEmpty(0).Min()))
            .ToList(), Math.Max(0, SelfAp));
        if (burst.Damage > 0) Summary += $"\nRafale max théorique sur une cible : {burst.Describe()}.";
        Play();
    }

    [RelayCommand]
    private void Play()
    {
        if (_frames.Count == 0) { Run(); return; }
        if (_stepIndex >= _frames.Count - 2) { _stepIndex = -1; ApplyFrame(0); Log.Clear(); }   // au bout -> rejoue du début
        IsPlaying = true; _timer.Start();
    }

    [RelayCommand] private void Pause() { IsPlaying = false; _timer.Stop(); }

    [RelayCommand]
    private void StepForward()
    {
        Pause();
        if (_stepIndex < _frames.Count - 2) { _stepIndex++; ApplyFrame(_stepIndex + 1); AppendLog(_stepIndex + 1); }
    }

    [RelayCommand]
    private void StepBack()
    {
        Pause();
        if (_stepIndex >= 0) { _stepIndex--; ApplyFrame(_stepIndex + 1); RebuildLog(); }
    }

    [RelayCommand] private void Reset() => StopAndReset();

    private void Advance()
    {
        if (_stepIndex >= _frames.Count - 2) { Pause(); return; }
        _stepIndex++;
        ApplyFrame(_stepIndex + 1);
        AppendLog(_stepIndex + 1);
        if (_stepIndex >= _frames.Count - 2) Pause();
    }

    private void StopAndReset()
    {
        Pause();
        _frames.Clear();
        _stepIndex = -1;
        HasResult = false;
        Summary = "";
        Log.Clear();
        RenderInitial();
    }

    // ===== Frames (reconstruction des PV par ActorId — TargetHpAfter = source de vérité, rembobinage exact) =====
    private void BuildFrames(CombatSimulator.SimState state, List<CombatSimulator.SimStep> steps)
    {
        _frames = new List<Frame>(steps.Count + 1);
        var enemySet = new HashSet<int>(state.Enemies.Select(e => e.Cell));
        var live = new Dictionary<long, LiveEnemy>();
        foreach (var e in state.Enemies)
            live[e.ActorId] = new LiveEnemy { Cell = e.Cell, Hp = e.MaxHp > 0 ? e.MaxHp : e.Hp, Max = Math.Max(1, e.MaxHp), Alive = true };

        List<IsoGrid.EnemyToken> Snapshot(long target) => live
            .Select(kv => new IsoGrid.EnemyToken(kv.Value.Cell, Math.Clamp((double)kv.Value.Hp / kv.Value.Max, 0, 1), kv.Value.Alive, kv.Key == target && kv.Value.Alive))
            .ToList();
        int AliveCount() => live.Values.Count(v => v.Alive);

        int myCell = state.SelfCell, ap = state.Ap, mp = state.Mp, turnNo = 1, dmgSeq = 0;
        _frames.Add(new Frame { MyCell = myCell, Enemies = Snapshot(0), Ap = ap, Mp = mp, TurnNo = 1, Alive = AliveCount() });

        foreach (var s in steps)
        {
            IReadOnlyList<int> move = Array.Empty<int>();
            int beam = -1, dmgCell = -1, dmgAmount = 0, dmgElement = -1, thisSeq = 0;
            long target = 0;
            switch (s.Kind)
            {
                case CombatSimulator.SimStepKind.Move:
                    move = PathCells(s.FromCell, s.ToCell, enemySet);
                    myCell = s.ToCell;
                    break;
                case CombatSimulator.SimStepKind.Cast:
                    beam = s.OnSelf ? myCell : s.ToCell;
                    // Applique les PV de TOUS les combattants touchés par la ZONE (AoeHits contient aussi la cible unique) —
                    // c'est ce qui fait « respirer » plusieurs barres de PV d'un seul cast AoE dans la prévisualisation.
                    foreach (var h in s.AoeHits)
                        if (live.TryGetValue(h.ActorId, out var lh)) { lh.Hp = h.HpAfter; lh.Alive = !h.Killed; }
                    // Cible principale : surlignage du token + chiffre de dégâts flottant (sur son cast direct, pas le sort-sur-soi).
                    if (!s.OnSelf && s.TargetActorId != 0)
                    {
                        target = s.TargetActorId;
                        if (s.Damage > 0) { dmgCell = s.ToCell; dmgAmount = s.Damage; dmgElement = s.Element; thisSeq = ++dmgSeq; }
                    }
                    break;
            }
            ap = s.ApAfter; mp = s.MpAfter; turnNo = s.TurnNo;
            _frames.Add(new Frame
            {
                MyCell = myCell, Move = move, Beam = beam, Enemies = Snapshot(target),
                DmgCell = dmgCell, DmgAmount = dmgAmount, DmgElement = dmgElement, DmgSeq = thisSeq,
                Ap = ap, Mp = mp, TurnNo = turnNo, Alive = AliveCount(),
                Log = StepLog(s, AliveCount()),
            });
        }
    }

    private void ApplyFrame(int idx)
    {
        if (idx < 0 || idx >= _frames.Count) return;
        var f = _frames[idx];
        RenderMyCell = f.MyCell;
        RenderEnemies = f.Enemies;
        MoveCells = f.Move;
        BeamTarget = f.Beam;
        DamageCell = f.DmgCell; DamageAmount = f.DmgAmount; DamageElement = f.DmgElement;
        DamageSeq = f.DmgSeq;   // APRÈS DamageAmount : l'IsoGrid teste DamageAmount>0 pour (re)lancer la montée du chiffre
        Hud = Loc.Fmt("L.Bots.Sim.Hud", f.TurnNo, f.Ap, f.Mp, f.Alive);
    }

    private void RenderInitial()
    {
        RenderMyCell = MyCell;
        RenderEnemies = Enemies.Select(e => new IsoGrid.EnemyToken(e.Cell, 1.0, true, false)).ToList();
        MoveCells = Array.Empty<int>();
        BeamTarget = -1; DamageCell = -1; DamageAmount = 0; DamageElement = -1; DamageSeq = 0;
        Hud = Loc.Fmt("L.Bots.Sim.Hud", 1, Math.Max(0, SelfAp), Math.Max(0, SelfMp), Enemies.Count);
    }

    private void AppendLog(int frameIdx)
    {
        if (frameIdx > 0 && frameIdx < _frames.Count && !string.IsNullOrEmpty(_frames[frameIdx].Log))
            Log.Add(_frames[frameIdx].Log);
    }

    private void RebuildLog()
    {
        Log.Clear();
        for (int k = 1; k <= _stepIndex + 1 && k < _frames.Count; k++)
            if (!string.IsNullOrEmpty(_frames[k].Log)) Log.Add(_frames[k].Log);
    }

    // Journal d'un pas (aliveAfter = ennemis vivants APRÈS ce pas, pour « N restant(s) » après un kill).
    private static string StepLog(CombatSimulator.SimStep s, int aliveAfter) => s.Kind switch
    {
        CombatSimulator.SimStepKind.Move => Loc.Fmt("L.Bots.Sim.LogMove", s.ToCell),
        CombatSimulator.SimStepKind.Cast when s.OnSelf => Loc.Fmt("L.Bots.Sim.LogCastSelf", s.SpellName),
        CombatSimulator.SimStepKind.Cast when s.Killed => Loc.Fmt("L.Bots.Sim.LogKill", s.SpellName, aliveAfter),
        CombatSimulator.SimStepKind.Cast when s.Damage > 0 => Loc.Fmt("L.Bots.Sim.LogCastDmg", s.SpellName, s.Damage, s.Distance),
        CombatSimulator.SimStepKind.Cast => Loc.Fmt("L.Bots.Sim.LogCast", s.SpellName, s.Distance),
        CombatSimulator.SimStepKind.Pass => Loc.T(s.NoteKey == "no-enemy" ? "L.Bots.Sim.LogNoEnemy" : "L.Bots.Sim.LogPass"),
        CombatSimulator.SimStepKind.TurnStart => Loc.Fmt("L.Bots.Sim.LogTurnSep", s.TurnNo),
        CombatSimulator.SimStepKind.EndTurn => Loc.T("L.Bots.Sim.LogEnd"),
        _ => "",
    };

    // Chemin marchable (ennemis exclus) pour surligner un déplacement.
    private static IReadOnlyList<int> PathCells(int from, int to, HashSet<int> enemies)
    {
        bool walk(int c) => (uint)c < MapPathfinder.CellCount && !enemies.Contains(c);
        var key = MapPathfinder.KeyMovements(from, to, walk, allowDiag: false, null);
        if (key.Count == 0) return new[] { from, to };
        return MapPathfinder.ExpandPath(key.Select(k => k & 0xFFF).ToList());
    }

    // Trouve une cellule marchable à chacune des distances demandées de `from` (plateau par défaut).
    private static IEnumerable<int> FindCellsAtDistance(int from, int[] distances)
    {
        foreach (var d in distances)
            for (int c = 0; c < MapPathfinder.CellCount; c++)
                if (c != from && MapPathfinder.CellDistance(from, c) == d) { yield return c; break; }
    }
}
