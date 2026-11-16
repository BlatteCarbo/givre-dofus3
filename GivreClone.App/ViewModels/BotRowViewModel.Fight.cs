using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Controls;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public sealed partial class FightSpellItem : ObservableObject
{
    public FightSpellItem(int id, string name, Bitmap? icon, int ap, int minRange, int range)
    { SpellId = id; Name = name; Icon = icon; ApText = ap.ToString(); Tip = $"{name} — {ap} PA, portée {(minRange > 0 ? minRange + "–" : "")}{range}"; }
    public int SpellId { get; }
    public string Name { get; }
    public Bitmap? Icon { get; }
    public string ApText { get; }
    public string Tip { get; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed record FightChallengeItem(int Id, string Name, string Bonus);

/// <summary>
/// État purement UI du choix de défi. Le moteur conserve ses propositions après validation :
/// vider la liste visuelle ne suffit donc pas à éviter qu'elles réapparaissent au prochain refresh.
/// Le jeton de génération empêche aussi une sélection asynchrone d'un ancien combat de masquer
/// les propositions d'un nouveau combat sur le même PID.
/// </summary>
internal sealed class FightChallengeUiState
{
    private bool _inFight;
    private long _fightId;
    private bool _selecting;
    private bool _chosen;
    private int _generation;

    public int Generation => _generation;

    /// <summary>Retourne true à chaque nouvelle session de combat observée.</summary>
    public bool ObserveFight(bool inFight, long fightId)
    {
        if (!inFight)
        {
            if (!_inFight) return false;
            Reset(false, 0);
            return true;
        }
        if (!_inFight || (_fightId != 0 && fightId != 0 && _fightId != fightId))
        {
            Reset(true, fightId);
            return true;
        }
        // L'identifiant peut être inconnu pendant les premières trames du MÊME combat.
        if (_fightId == 0 && fightId != 0) _fightId = fightId;
        return false;
    }

    private void Reset(bool inFight, long fightId)
    {
        _inFight = inFight;
        _fightId = fightId;
        _selecting = false;
        _chosen = false;
        unchecked { _generation++; }
    }

    /// <summary>Un seul clic en vol, et uniquement sur une ligne encore affichée.</summary>
    public bool TryBeginPick(FightChallengeItem? item, IEnumerable<FightChallengeItem> displayed, out int generation)
    {
        generation = _generation;
        if (!_inFight || _selecting || _chosen || item is null || !displayed.Any(row => ReferenceEquals(row, item)))
            return false;
        _selecting = true;
        return true;
    }

    /// <summary>Ok et Already ferment le choix UI ; un envoi refusé peut être retenté.
    /// Already peut désigner un autre choix encore en cours : il ne prouve pas un ACK serveur.</summary>
    public bool CompletePick(int generation, bool terminal)
    {
        if (generation != _generation || !_selecting) return false;
        _selecting = false;
        _chosen = terminal;
        return true;
    }

    /// <summary>Réconcilie identité, ordre, nom et bonus, pas seulement le nombre d'offres.</summary>
    public void Synchronize(ObservableCollection<FightChallengeItem> displayed, IReadOnlyList<FightChallengeItem>? offered)
    {
        if (!_inFight || _selecting || _chosen || offered is not { Count: > 0 })
        {
            if (displayed.Count > 0) displayed.Clear();
            return;
        }
        if (displayed.SequenceEqual(offered)) return;
        displayed.Clear();
        foreach (var offer in offered) displayed.Add(offer);
    }
}

/// <summary>Une case de la frise d'ordre de jeu : qui, PV %, camp, en train de jouer, mort.</summary>
public sealed record TurnChip(string Label, string Hp, string Tip, bool IsCurrent, bool IsSelf, bool IsEnemy, bool Alive)
{
    public double Opacity => Alive ? 1 : 0.35;
    private static readonly Avalonia.Media.IBrush SelfBrush = Avalonia.Media.Brush.Parse("#E6A63A"), EnemyBrush = Avalonia.Media.Brush.Parse("#E5534B"), AllyBrush = Avalonia.Media.Brush.Parse("#3EBA4A");
    public Avalonia.Media.IBrush Color => IsSelf ? SelfBrush : IsEnemy ? EnemyBrush : AllyBrush;
}

/// <summary>Carte de combat jouable : placement, choix du défi, sorts, déplacement et fin de tour depuis l'interface du bot.</summary>
public partial class BotRowViewModel
{
    private string _fightSig = "";
    private int _fightSpellsFor = -1;
    private volatile bool _challengesAsked;
    private long _challengesTry;
    private readonly FightChallengeUiState _challengeUi = new();
    private readonly Dictionary<long, int> _fightLife = new();   // actorId -> dernière vie vue (pour le journal des dégâts / soins)

    [ObservableProperty] public partial bool FightActive { get; set; }
    [ObservableProperty] public partial bool CombatDefeat { get; set; }
    private long _lastFightSeqSeen = -1;
    [ObservableProperty] public partial bool ManualFight { get; set; }
    [ObservableProperty] public partial bool FightIsPlacement { get; set; }
    [ObservableProperty] public partial bool FightIsMyTurn { get; set; }
    [ObservableProperty] public partial string FightPhaseText { get; set; } = "";
    [ObservableProperty] public partial string FightApText { get; set; } = "";
    [ObservableProperty] public partial string FightMpText { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<FightMark>? FightMarks { get; set; }
    [ObservableProperty] public partial IReadOnlyList<LaunchService.ScriptFighter>? FightFighters { get; set; }
    public ObservableCollection<FightSpellItem> FightSpells { get; } = new();
    public ObservableCollection<FightChallengeItem> FightChallenges { get; } = new();
    public ObservableCollection<string> FightLog { get; } = new();
    /// <summary>Frise de l'ordre de jeu (déduite des débuts de tour ; se complète pendant le premier tour de table).</summary>
    public ObservableCollection<TurnChip> TurnChips { get; } = new();
    [ObservableProperty] public partial string TurnOrderHint { get; set; } = "";

    private void RefreshTurnOrder(IReadOnlyList<LaunchService.ScriptFighter> fighters)
    {
        var (order, current, complete) = Stores.Launch.FightTurnOrder((uint)Pid);
        var byId = fighters.GroupBy(f => f.ActorId).ToDictionary(g => g.Key, g => g.First());
        var chips = new List<TurnChip>();
        int enemyNo = 0;
        foreach (long id in order)
        {
            byId.TryGetValue(id, out var f);
            bool known = f.ActorId == id;
            string label = !known ? "?" : f.IsSelf ? "Moi" : f.IsSummon ? "Invoc." : f.IsEnemy ? $"Ennemi {++enemyNo}" : "Allié";
            string hp = known && f.MaxLife > 0 ? $"{f.HpPercent} %" : "";
            string tip = known ? $"{label} · case {f.Cell}" + (f.MaxLife > 0 ? $" · {f.Life}/{f.MaxLife} PV" : "") : "Combattant parti";
            chips.Add(new TurnChip(label, hp, tip, id == current, known && f.IsSelf, known && f.IsEnemy, known && f.Alive));
        }
        TurnOrderHint = order.Count == 0 ? "" : complete ? "" : "ordre en cours de relevé (premier tour)";
        if (!TurnChips.SequenceEqual(chips))
        {
            TurnChips.Clear();
            foreach (var c in chips) TurnChips.Add(c);
        }
    }
    /// <summary>Libellé du bouton du bandeau (toujours visible : le mode manuel doit être actif AVANT le combat pour couper le placement automatique).</summary>
    public string ManualFightLabel => ManualFight ? "Je joue mes combats : OUI" : "Je joue mes combats";
    public bool FightAuto => FightActive && !ManualFight;
    public bool FightManualActive => FightActive && ManualFight;
    partial void OnFightActiveChanged(bool value) { OnPropertyChanged(nameof(FightAuto)); OnPropertyChanged(nameof(FightManualActive)); }
    partial void OnManualFightChanged(bool value) { OnPropertyChanged(nameof(ManualFightLabel)); OnPropertyChanged(nameof(FightAuto)); OnPropertyChanged(nameof(FightManualActive)); }

    private FightSpellItem? SelectedFightSpell => FightSpells.FirstOrDefault(s => s.IsSelected);

    private void FightNote(string line)
    {
        FightLog.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (FightLog.Count > 3) FightLog.RemoveAt(0);
    }

    /// <summary>Appelé ~3 fois/s par le ViewModel parent pour le bot affiché : relit l'état du combat et recalcule cases colorées, sorts et défis.</summary>
    public void RefreshFight()
    {
        if (Pid <= 0) return;
        var l = Stores.Launch; uint pid = (uint)Pid;
        ManualFight = l.ManualCombatOn(pid);
        var s = l.ManualSnapshot(pid);
        if (!s.InFight)
        {
            _challengeUi.ObserveFight(false, 0);
            if (FightActive) { FightActive = false; FightMarks = null; FightFighters = null; _fightLife.Clear(); TurnChips.Clear(); TurnOrderHint = ""; FightSpells.Clear(); FightChallenges.Clear(); _fightSpellsFor = -1; _challengesAsked = false; _fightSig = ""; }
            return;
        }
        if (_challengeUi.ObserveFight(true, l.CurrentFightId(pid)))
        {
            FightChallenges.Clear();
            _challengesAsked = false;
            _challengesTry = 0;
        }
        FightActive = true;
        var lastFight = l.LastFightResult(pid);
        if (lastFight is not null && lastFight.Seq != _lastFightSeqSeen)
        {
            _lastFightSeqSeen = lastFight.Seq;
            if (lastFight.Outcome == 0)
                CombatDefeat = true;
        }
        if (!FightActive)
        {
            CombatDefeat = false;
        }
        FightIsPlacement = s.Phase == "placement";
        FightIsMyTurn = s.Phase == "myturn";
        FightPhaseText = s.Phase switch { "placement" => "Placement", "myturn" => $"Votre tour · Tour {s.TurnNo}", _ => $"Tour adverse · Tour {s.TurnNo}" };
        FightApText = $"{s.Ap} PA"; FightMpText = $"{s.Mp} PM";
        FightFighters = s.Fighters;
        RefreshTurnOrder(s.Fighters);
        foreach (var f in s.Fighters)
        {
            if (f.MaxLife <= 0) continue;
            if (_fightLife.TryGetValue(f.ActorId, out int before) && before != f.Life)
            {
                int d = f.Life - before;
                string who = f.IsSelf ? "Moi" : f.IsEnemy ? $"Ennemi (case {f.Cell})" : $"Allié (case {f.Cell})";
                FightNote(d < 0 ? $"{who} : {d} PV" + (f.Life <= 0 ? " — mort" : "") : $"{who} : +{d} PV");
            }
            _fightLife[f.ActorId] = f.Life;
        }

        if (_fightSpellsFor != (int)pid)
        {
            FightSpells.Clear();
            foreach (var sp in l.ManualSpells(pid))
                FightSpells.Add(new FightSpellItem(sp.SpellId, SpellNameOf(sp.SpellId), Stores.SpellIcons.Get(l.GameNames.SpellIconId(sp.SpellId)), sp.Ap, sp.MinRange, sp.Range));
            _fightSpellsFor = (int)pid;
        }
        if (FightIsPlacement && ManualFight)
        {
            // La demande peut être refusée tant que le combat n'est pas tout à fait installé : on réessaie (au plus 1 fois/s) jusqu'à ce qu'elle passe.
            if (!_challengesAsked && Environment.TickCount64 - _challengesTry > 1000)
            {
                _challengesTry = Environment.TickCount64;
                int generation = _challengeUi.Generation;
                _ = Task.Run(async () =>
                {
                    if (await l.RequestChallengeProposalsAsync(pid))
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (_challengeUi.Generation == generation) _challengesAsked = true;
                        });
                });
            }
            var props = l.ChallengeProposals(pid);
            _challengeUi.Synchronize(FightChallenges, props?.Select(p =>
                new FightChallengeItem(p.Id,
                    l.GameNames.ChallengeName(p.Id) is { Length: > 0 } n ? n : $"Défi {p.Id}",
                    $"+{p.XpBonus}% XP · +{p.DropBonus}% butin")).ToArray());
        }
        else _challengeUi.Synchronize(FightChallenges, null);

        var sel = SelectedFightSpell;
        string sig = $"{s.Phase}|{s.SelfCell}|{s.Mp}|{s.Ap}|{sel?.SpellId}|{s.Fighters.Count}|{string.Join(',', s.Fighters.Select(f => f.Cell))}";
        if (sig == _fightSig) return;
        _fightSig = sig;
        var marks = new List<FightMark>();
        if (FightIsPlacement) foreach (int c in s.Placement) marks.Add(new FightMark(c, 0));
        else if (FightIsMyTurn)
        {
            if (sel is not null) foreach (int c in l.ManualCastCells(pid, sel.SpellId)) marks.Add(new FightMark(c, 2));
            else foreach (int c in l.ManualReachCells(pid)) marks.Add(new FightMark(c, 1));
        }
        FightMarks = marks;
    }

    private static string SpellNameOf(int id) => Stores.Launch.GameNames.SpellName(id) ?? $"Sort {id}";

    [RelayCommand]
    private void ToggleManualFight()
    {
        Stores.Launch.SetManualCombat((uint)Pid, !Stores.Launch.ManualCombatOn((uint)Pid));
        ManualFight = Stores.Launch.ManualCombatOn((uint)Pid);
        FightNote(ManualFight ? "Mode manuel : tu joues toi-même (placement, défi et tours automatiques coupés)" : "Mode automatique rétabli");
    }

    [RelayCommand] private void FightReady() { FightNote(Stores.Launch.ScriptReady((uint)Pid, true) ? "Prêt" : "Prêt refusé"); }
    [ObservableProperty] public partial bool GiveUpArmed { get; set; }
    public string GiveUpLabel => GiveUpArmed ? "Confirmer l'abandon ?" : "Abandonner";
    partial void OnGiveUpArmedChanged(bool value) => OnPropertyChanged(nameof(GiveUpLabel));

    /// <summary>Abandonner le combat : seulement en combat, et en deux clics (le 2e dans les 4 s) pour éviter un clic malheureux.</summary>
    [RelayCommand]
    private void GiveUpFight()
    {
        if (!Stores.Launch.IsInFight((uint)Pid)) { GiveUpArmed = false; return; }
        if (!GiveUpArmed)
        {
            GiveUpArmed = true;
            Avalonia.Threading.DispatcherTimer.RunOnce(() => GiveUpArmed = false, TimeSpan.FromSeconds(4));
            return;
        }
        GiveUpArmed = false;
        FightNote(Stores.Launch.FightGiveUp((uint)Pid) ? "Combat abandonné" : "Abandon refusé");
    }

    /// <summary>Entre dans le havre-sac (hors combat seulement).</summary>
    [RelayCommand]
    private void OpenHavenBag()
    {
        if (Stores.Launch.IsInFight((uint)Pid)) return;
        FightNote(Stores.Launch.EnterHavenBag((uint)Pid) ? "Entrée dans le havre-sac demandée" : "Havre-sac impossible (perso inconnu ou pas de relais)");
    }

    [RelayCommand] private void FightEndTurn() { FightNote(Stores.Launch.EndTurn((uint)Pid) ? "Tour passé" : "Passer : pas ton tour"); }

    [RelayCommand]
    private void PickFightSpell(FightSpellItem? item)
    {
        bool was = item?.IsSelected == true;
        foreach (var s in FightSpells) s.IsSelected = false;
        if (item is not null && !was) item.IsSelected = true;
        _fightSig = "";   // recalcule la portée
    }

    [RelayCommand]
    private async Task PickFightChallenge(FightChallengeItem? c)
    {
        if (!FightIsPlacement || !ManualFight || !_challengeUi.TryBeginPick(c, FightChallenges, out int generation)) return;
        FightChallenges.Clear(); // ne pas laisser un second bouton cliquable pendant l'attente réseau / équipe
        try
        {
            var r = await Stores.Launch.SelectChallengeAsync((uint)Pid, c!.Id);
            bool terminal = r is LaunchService.ChallengePick.Ok or LaunchService.ChallengePick.Already;
            if (!_challengeUi.CompletePick(generation, terminal)) return; // ancien combat : ne pas altérer le suivant
            FightNote(r switch
            {
                LaunchService.ChallengePick.Ok => $"Défi demandé : {c.Name} (envoi réussi)",
                LaunchService.ChallengePick.Already => "Choix de défi déjà engagé pour ce combat",
                _ => $"Défi refusé ({r})"
            });
        }
        catch (Exception)
        {
            if (_challengeUi.CompletePick(generation, false))
                FightNote("Défi refusé (erreur lors de la sélection)");
        }
    }

    /// <summary>Clic sur la carte en combat : placement, sort sélectionné (lancer), sinon déplacement. Clic droit : désélectionne le sort.</summary>
    public void OnFightCell(int cell, bool right)
    {
        var l = Stores.Launch; uint pid = (uint)Pid;
        if (right) { PickFightSpell(null); return; }
        if (FightIsPlacement)
        {
            if (!ManualFight) return;
            FightNote(l.ScriptPlace(pid, cell) ? $"Placé en {cell}" : $"Placement refusé ({cell})");
        }
        else if (FightIsMyTurn)
        {
            var sel = SelectedFightSpell;
            if (sel is not null)
            {
                var ok = l.ScriptCanCast(pid, sel.SpellId, cell, -1);
                if (!ok.Ok) { FightNote($"{sel.Name} : impossible sur {cell} ({(GivreClone.Protocol.SpellCastVerdict)ok.Reason})"); return; }
                _ = Task.Run(() => l.CastSpell(pid, cell, sel.SpellId));
                FightNote($"{sel.Name} → {cell}");
            }
            else _ = Task.Run(async () => { var (moved, end) = await l.ManualMoveAsync(pid, cell); Avalonia.Threading.Dispatcher.UIThread.Post(() => FightNote(moved ? $"Déplacé en {end}" : "Déplacement impossible")); });
        }
        _fightSig = "";
    }
}
