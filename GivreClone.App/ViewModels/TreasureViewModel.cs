using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Treasure;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public sealed record TreasureStepRow(int Index, string Arrow, string Label, string Where, string Source, string State);
public enum AutoHuntReadiness { Ready, NoCharacter, NoMap, Travelling, Unavailable, ScriptRunning }

/// <summary>Reconstructed live treasure panel. Displays decoded evidence; does not infer a route or result-code meanings.</summary>
public partial class TreasureViewModel : ViewModelBase
{
    private readonly LaunchService _launch;
    private readonly TreasureRequestTracker _request = new();
    private uint _pid;
    private long _huntSequence = -1, _receiptFloor, _finishFloor;
    private long _startMap;
    private int _questType, _checkpoint;
    private bool _hasHunt;
    private enum HuntScriptKind { Auto, Fouduglen }
    private readonly Dictionary<uint, HuntScriptKind> _huntScript = new();
    private readonly Dictionary<uint, bool> _huntScriptSeen = new();
    public TreasureViewModel() : this(Stores.Launch) { }
    public TreasureViewModel(LaunchService launch) { _launch = launch; }

    [ObservableProperty] public partial string Status { get; set; } = "Sélectionnez un personnage.";
    [ObservableProperty] public partial bool Hunting { get; set; }
    [ObservableProperty] public partial string CheckPointText { get; set; } = "—";
    [ObservableProperty] public partial string RetriesText { get; set; } = "—";
    [ObservableProperty] public partial string SessionText { get; set; } = "Aucune chasse observée.";
    /// <summary>Dernière étape reçue du serveur (celle que le script résout), pas la ligne choisie dans la liste.</summary>
    [ObservableProperty] public partial string CurrentStepText { get; set; } = "Aucune étape en cours.";
    [ObservableProperty] public partial bool Running { get; set; }
    [ObservableProperty] public partial int SelectedIndex { get; set; }
    public ObservableCollection<TreasureStepRow> Steps { get; } = new();
    public bool HasSteps => Steps.Count > 0;
    public bool CanSend => Hunting && !Running;
    public bool CanFlag => CanSend && SelectedIndex >= 0 && SelectedIndex < Steps.Count;
    partial void OnHuntingChanged(bool value) => NotifyActions();
    partial void OnRunningChanged(bool value) => NotifyActions();
    partial void OnSelectedIndexChanged(int value) => NotifyActions();
    private void NotifyActions()
    {
        RefreshFouduglenActions();
        OnPropertyChanged(nameof(CanGo));
        OnPropertyChanged(nameof(CanSend)); OnPropertyChanged(nameof(CanFlag));
        FlagCommand.NotifyCanExecuteChanged(); RemoveFlagCommand.NotifyCanExecuteChanged();
        DigCommand.NotifyCanExecuteChanged(); GiveUpCommand.NotifyCanExecuteChanged();
        NotifyHuntRun();
    }
    partial void RefreshFouduglenActions();

    public void AttachPid(int pid)
    {
        uint next = pid > 0 ? (uint)pid : 0;
        if (_pid == next) return;
        _pid = next; Clear();
        _receiptFloor = _launch.SnowSeq; _finishFloor = _launch.TreasureFinishedSeq(next);
        Status = next == 0 ? "Sélectionnez un personnage." : "En attente d'une chasse reçue du serveur.";
        Refresh();
    }
    private void Clear()
    {
        StopGo();
        _goCts = null; _stepTripSequence = null; Going = false;
        _request.Clear(); Running = false; Hunting = false; _hasHunt = false;
        _huntSequence = -1; Steps.Clear(); OnPropertyChanged(nameof(HasSteps));
        CheckPointText = "—"; RetriesText = "—"; SessionText = "Aucune chasse observée.";
        CurrentStepText = "Aucune étape en cours.";
        // Le script lancé ici survit à la fin d'une chasse (il en relance une) : son arrêt n'est pas oublié ici.
        SelectedIndex = -1; NotifyActions();
    }

    public void Refresh()
    {
        RefreshFouduglenActions();
        if (_pid == 0) { NotifyHuntRun(); return; }
        if (_stepTripSequence is not null && ScriptRunning?.Invoke(_pid) == true) StopGo();
        var hunt = _launch.Treasure(_pid);
        long finished = _launch.TreasureFinishedSeq(_pid);
        // A finish can race a newly observed hunt; its sequence establishes which is newer.
        if (finished > _finishFloor && (hunt is null || finished > hunt.Seq))
        {
            Clear(); _finishFloor = finished; _receiptFloor = _launch.SnowSeq;
            Status = "Fin de chasse reçue du serveur.";
            _ = TakeHuntScriptExit();
            NotifyHuntRun();
            return;
        }
        if (hunt is null)
        {
            if (_hasHunt) { Clear(); _receiptFloor = _launch.SnowSeq; Status = "Aucune chasse active observée."; }
            if (TakeHuntScriptExit()) Status = "Script de chasse terminé.";
            NotifyHuntRun();
            return;
        }
        if (hunt.Seq != _huntSequence)
        {
            if (_stepTripSequence is { } sequence && sequence != hunt.Seq) StopGo();
            bool boundary = !_hasHunt || hunt.StartMapId != _startMap || hunt.QuestType != _questType || hunt.CurrentCheckPoint != _checkpoint;
            if (boundary)
            {
                _request.Clear(); Running = false; _receiptFloor = _launch.SnowSeq;
                Status = "État de chasse reçu du serveur.";
            }
            _hasHunt = true; _startMap = hunt.StartMapId; _questType = hunt.QuestType;
            _checkpoint = hunt.CurrentCheckPoint; _huntSequence = hunt.Seq; Hunting = true;
            CheckPointText = $"Point de contrôle : {hunt.CurrentCheckPoint} / {hunt.TotalCheckPoint}";
            RetriesText = $"Essais disponibles : {hunt.AvailableRetryCount}";
            SessionText = $"PID {_pid} · type {hunt.QuestType} · départ {hunt.StartMapId} · {hunt.Steps.Count}/{hunt.TotalStepCount} indices reçus";
            int selected = SelectedIndex; Steps.Clear();
            for (int i = 0; i < hunt.Steps.Count; i++)
            {
                var step = hunt.Steps[i];
                string label = DescribeStep(step);
                bool directional = step.Kind is "poi" or "hint" or "direction";
                var flag = i < hunt.Flags.Count ? hunt.Flags[i] : null;
                Steps.Add(new(i, directional ? TreasureRequestTracker.Direction(step.Direction) : "—", label,
                    flag is null ? "Carte non observée" : $"Carte {flag.MapId}", "Serveur",
                    flag is null ? "Sans drapeau observé" : $"État brut {flag.State}"));
            }
            SelectedIndex = selected >= 0 && selected < Steps.Count ? selected : -1;
            CurrentStepText = FormatCurrentStep(hunt);
            OnPropertyChanged(nameof(HasSteps)); NotifyActions();
        }
        var answer = _request.Action switch
        {
            "flag" or "remove" => _launch.TreasureFlagAnswer(_pid),
            "dig" => _launch.TreasureDigAnswer(_pid), _ => null
        };
        if (answer is { } receipt && receipt.Seq > _receiptFloor && _request.Receive(receipt.Seq))
        {
            Running = false; _receiptFloor = receipt.Seq;
            Status = $"Réponse serveur : code {receipt.Result}, détail {receipt.Extra}.";
        }
        if (_request.Expire(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15)))
        {
            Running = false; Status = "Délai dépassé : aucune réponse correspondante observée. Vérifiez l'état avant de renvoyer.";
        }
        if (TakeHuntScriptExit()) Status = "Script de chasse terminé.";
        NotifyHuntRun();
    }

    private string DescribeStep(LaunchService.TreasureStep step) => step.Kind switch
    {
        "poi" => $"Indice : {PoiName?.Invoke(step.PoiLabelId) ?? $"#{step.PoiLabelId}"}",
        "hint" => $"Phorreur : {NpcName?.Invoke(step.NpcId) ?? $"PNJ #{step.NpcId}"}",
        "direction" => $"Parcourir {step.MapCount} carte(s)",
        "fight" => "Combat",
        "dig" => "Creuser",
        _ => "Étape inconnue"
    };

    /// <summary>L'étape courante est la dernière reçue : c'est celle que treasure:stepType(-1) résout.</summary>
    private string FormatCurrentStep(LaunchService.TreasureHunt hunt)
    {
        if (hunt.Steps.Count == 0) return "Étape en cours : indice pas encore reçu.";
        var step = hunt.Steps[^1];
        bool directional = step.Kind is "poi" or "hint" or "direction";
        string body = directional ? $"{TreasureRequestTracker.Direction(step.Direction)} {DescribeStep(step)}" : DescribeStep(step);
        string progress = hunt.TotalStepCount > 0 ? $"{hunt.Steps.Count}/{hunt.TotalStepCount} · " : "";
        return $"Étape en cours : {progress}{body}";
    }

    private void Send(string action, Func<bool> send)
    {
        Refresh();
        if (!CanSend || ((action is "flag" or "remove") && !CanFlag)) return;
        long baseline = _launch.SnowSeq;
        try
        {
            if (!send()) { Status = "Requête non envoyée (relais ou autorisation indisponible)."; return; }
            _request.Begin(action, baseline, DateTimeOffset.UtcNow); Running = true;
            Status = "Requête envoyée ; en attente d'une réponse serveur.";
        }
        catch (Exception ex) { Status = $"Envoi impossible : {ex.Message}"; }
    }
    [RelayCommand(CanExecute = nameof(CanFlag))] private void Flag() => Send("flag", () => _launch.TreasureSendFlag(_pid, SelectedIndex, _questType));
    [RelayCommand(CanExecute = nameof(CanFlag))] private void RemoveFlag() => Send("remove", () => _launch.TreasureSendFlagRemove(_pid, SelectedIndex, _questType));
    [RelayCommand(CanExecute = nameof(CanSend))] private void Dig() => Send("dig", () => _launch.TreasureSendDig(_pid, _questType));
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void GiveUp()
    {
        uint pid = _pid;
        long sequence = _huntSequence;
        void Do()
        {
            Refresh();
            if (_pid != pid || _huntSequence != sequence) return;
            Send("giveup", () => _launch.TreasureSendGiveUp(_pid, _questType));
            // Aucune réponse dédiée : la fin de chasse (ou la disparition de l'état) clôt l'attente au prochain rafraîchissement.
            if (Running) Status = "Abandon envoyé ; en attente de la fin de chasse du serveur.";
        }
        if (Confirm is null) Do(); else Confirm("Abandonner la chasse", "La chasse en cours sera perdue. Abandonner ?", Do);
    }

    // ---- Dépendances d'interface fournies par BotsViewModel (le banc hors ligne s'en passe : repli sur les ids / envoi direct) ----
    public Func<int, string?>? PoiName { get; set; }
    public Func<int, string?>? NpcName { get; set; }
    /// <summary>Confirmation (titre, texte, action si confirmé). Null = action directe.</summary>
    public Action<string, string, Action>? Confirm { get; set; }
    /// <summary>Carte de l'étape en cours (direction calculée, indice cherché dans DofusDB) et carte actuelle, ou la raison de l'échec.</summary>
    public Func<uint, LaunchService.TreasureStep, System.Threading.CancellationToken, System.Threading.Tasks.Task<(long? Map, long Here, string? Error)>>? ResolveStepMap { get; set; }

    // ---- « Y aller » : carte de l'étape en cours (direction calculée, indice cherché dans DofusDB), trajet par l'hôte fourni ----
    /// <summary>Trajet vers une carte pour ce pid (fourni par BotsViewModel : hôte de trajet transitoire). Null = indisponible.</summary>
    public Func<uint, long, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>? Travel { get; set; }
    /// <summary>Vrai si un script tourne sur ce pid (il garde la main sur les déplacements).</summary>
    public Func<uint, bool>? ScriptRunning { get; set; }
    [ObservableProperty] public partial bool Going { get; set; }
    private System.Threading.CancellationTokenSource? _goCts;
    private long? _stepTripSequence;
    public bool CanGo => Hunting && !Going && _launch.Treasure(_pid) is { Steps.Count: > 0 } h && h.Steps[^1].Kind is "poi" or "direction";
    partial void OnGoingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGo));
        NotifyHuntRun();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task GoToStep()
    {
        if (!CanGo || Travel is null || _launch.Treasure(_pid) is not { Steps.Count: > 0 } hunt) return;
        if (ScriptRunning?.Invoke(_pid) == true) { Status = "Un script tourne sur ce perso : il garde la main sur les déplacements."; return; }
        var step = hunt.Steps[^1];
        uint pid = _pid;
        if (ResolveStepMap is null) return;
        var cts = new System.Threading.CancellationTokenSource(); _goCts = cts;
        _stepTripSequence = hunt.Seq; Going = true;
        bool OwnsTrip() => _pid == pid && ReferenceEquals(_goCts, cts)
            && _launch.Treasure(pid)?.Seq == hunt.Seq && ScriptRunning?.Invoke(pid) != true;
        Status = "Recherche de la carte de l'étape…";
        try
        {
            var (target, here, error) = await ResolveStepMap(pid, step, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!OwnsTrip()) return;
            if (error is not null) Status = error;
            else if (target is not { } map) Status = "Carte de l'étape introuvable (indice absent de DofusDB ou carte inconnue).";
            else if (map == here) Status = "Déjà sur la carte de l'étape : pose le drapeau.";
            else
            {
                Status = "En route vers la carte de l'étape…";
                bool arrived = await Travel(pid, map, cts.Token);
                if (OwnsTrip()) Status = arrived ? "Arrivé : pose le drapeau." : "Trajet échoué.";
            }
        }
        catch (OperationCanceledException) { if (OwnsTrip()) Status = "Trajet annulé."; }
        catch (Exception e) { if (OwnsTrip()) Status = "Échec : " + e.GetType().Name; }
        finally { if (ReferenceEquals(_goCts, cts)) { _goCts = null; _stepTripSequence = null; Going = false; } cts.Dispose(); }
    }

    [RelayCommand] private void StopGo() { try { _goCts?.Cancel(); } catch { } }

    // ---- Chasse automatique : rejoint d'abord le coffre (Travel = planificateur zaap), puis lance TreasureScript ----
    [ObservableProperty] public partial int AutoHuntLevel { get; set; }          // 0 = premier choix du coffre
    [ObservableProperty] public partial int AutoHuntMaxFails { get; set; } = 3;  // échecs sur un même indice avant abandon
    /// <summary>Démarre le script de chasse automatique sur ce pid (niveau, échecs tolérés) ; false si refusé. Fourni par BotsViewModel.</summary>
    public Func<uint, int, int, bool>? StartAutoHuntScript { get; set; }

    /// <summary>Fichier passé à l'hôte par StartNamedScript("Chasse au trésor", …).</summary>
    public const string AutoHuntScriptFile = "Chasse au trésor.lua";
    /// <summary>Fichier passé à l'hôte par StartNamedScript("Fouduglen", …).</summary>
    public const string FouduglenScriptFile = "Fouduglen.lua";

    /// <summary>
    /// Arrête le script de ce pid seulement si le fichier est celui qui tourne. False : ne rien arrêter
    /// (autre script, ou déjà fini). Fourni par BotsViewModel ; sans lui le bouton reste inopérant.
    /// </summary>
    public Func<uint, string, bool>? StopHuntScript { get; set; }

    /// <summary>La carte courante expose déjà l'élément 484993, ou c'est une carte de coffre connue. Null : seules les cartes connues.</summary>
    public Func<uint, long, bool>? MapHasHuntChest { get; set; }
    /// <summary>Carte de coffre la plus proche depuis la carte courante. Null : <see cref="HuntChestMaps.Nearest"/> sans coordonnées.</summary>
    public Func<uint, long, long>? NearestHuntChest { get; set; }

    public AutoHuntReadiness AutoHuntReadyState => _pid == 0 ? AutoHuntReadiness.NoCharacter
        : _launch.CurrentMap(_pid) is null ? AutoHuntReadiness.NoMap
        : Going ? AutoHuntReadiness.Travelling
        : StartAutoHuntScript is null ? AutoHuntReadiness.Unavailable
        : ScriptRunning?.Invoke(_pid) == true ? AutoHuntReadiness.ScriptRunning
        : AutoHuntReadiness.Ready;
    public bool CanStartAutoHunt => AutoHuntReadyState == AutoHuntReadiness.Ready;
    public string AutoHuntPrerequisites => AutoHuntReadyState switch
    {
        AutoHuntReadiness.NoCharacter => "Sélectionnez un personnage pour lancer une chasse.",
        AutoHuntReadiness.NoMap => "Attendez une carte confirmée du personnage avant de lancer la chasse.",
        AutoHuntReadiness.Travelling => "Un trajet est en cours. Attendez son arrivée ou arrêtez-le.",
        AutoHuntReadiness.Unavailable => "Le lanceur de chasse est indisponible.",
        AutoHuntReadiness.ScriptRunning => "Un script est déjà en cours sur ce personnage. Arrêtez-le avant de lancer la chasse.",
        AutoHuntReadiness.Ready => "Prêt à demander le lancement ; droits et niveau seront vérifiés au démarrage.",
        _ => throw new InvalidOperationException("État de préparation de chasse inconnu.")
    };

    private bool OnHuntChest(uint pid, long map)
        => map > 0 && (MapHasHuntChest?.Invoke(pid, map) ?? HuntChestMaps.Contains(map));

    public bool CanStopHunt => _pid > 0 && _huntScript.ContainsKey(_pid) && StopHuntScript is not null
        && ScriptRunning?.Invoke(_pid) == true;

    private void NotifyHuntRun()
    {
        RefreshFouduglenActions();
        OnPropertyChanged(nameof(CanStartAutoHunt));
        OnPropertyChanged(nameof(AutoHuntReadyState));
        OnPropertyChanged(nameof(AutoHuntPrerequisites));
        OnPropertyChanged(nameof(CanStopHunt));
        StartAutoHuntCommand.NotifyCanExecuteChanged();
        StopHuntCommand.NotifyCanExecuteChanged();
    }

    private void RememberLaunchedHunt(bool fouduglen)
    {
        if (_pid == 0) return;
        _huntScript[_pid] = fouduglen ? HuntScriptKind.Fouduglen : HuntScriptKind.Auto;
        _huntScriptSeen[_pid] = ScriptRunning?.Invoke(_pid) == true;
    }

    /// <summary>Le script qu'on a lancé vient de s'arrêter tout seul. Ne touche pas un script encore en cours, ni une chasse suivante.</summary>
    private bool TakeHuntScriptExit()
    {
        if (_pid == 0 || !_huntScript.ContainsKey(_pid)) return false;
        if (ScriptRunning?.Invoke(_pid) == true) { _huntScriptSeen[_pid] = true; return false; }
        if (!_huntScriptSeen.GetValueOrDefault(_pid)) return false;
        _huntScript.Remove(_pid);
        _huntScriptSeen[_pid] = false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanStartAutoHunt))]
    private async System.Threading.Tasks.Task StartAutoHunt()
    {
        if (!CanStartAutoHunt || StartAutoHuntScript is not { } start) return;
        uint pid = _pid;
        int level = AutoHuntLevel, fails = AutoHuntMaxFails;
        long here = _launch.CurrentMap(pid) ?? 0;
        if (here <= 0)
        {
            Status = "Chasse automatique refusée (personnage pas en jeu).";
            NotifyHuntRun();
            return;
        }
        if (!OnHuntChest(pid, here))
        {
            long map = NearestHuntChest?.Invoke(pid, here) ?? HuntChestMaps.Nearest(here, null);
            if (map <= 0) map = HuntChestMaps.MalleAuxTresors;
            if (map != here)
            {
                if (Travel is not { } travel)
                {
                    Status = "Trajet vers le coffre de chasse indisponible.";
                    NotifyHuntRun();
                    return;
                }
                if (!await TravelToHuntChest(pid, map, travel)) return;
                if (_pid != pid || ScriptRunning?.Invoke(pid) == true) return;
            }
        }
        if (!OnHuntChest(pid, _launch.CurrentMap(pid) ?? 0))
        {
            Status = "Chasse automatique refusée : aucun coffre de chasse confirmé sur la carte d'arrivée.";
            NotifyHuntRun();
            return;
        }
        if (!start(pid, level, fails))
        {
            Status = "Chasse automatique refusée (droit, niveau ou perso pas en jeu).";
            NotifyHuntRun();
            return;
        }
        RememberLaunchedHunt(fouduglen: false);
        Status = "Chasse automatique lancée. L'étape en cours s'affiche ici.";
        NotifyHuntRun();
    }

    /// <summary>Affiche le trajet vers le coffre, puis laisse le planificateur zaap (Travel) l'exécuter. False : script non lancé.</summary>
    private async System.Threading.Tasks.Task<bool> TravelToHuntChest(uint pid, long map, Func<uint, long, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> travel)
    {
        var cts = new System.Threading.CancellationTokenSource();
        _goCts = cts;
        _stepTripSequence = null;
        Going = true;
        string previous = CurrentStepText;
        long sequence = _huntSequence;
        bool keepTravel = !_hasHunt;
        CurrentStepText = $"Trajet vers le coffre de chasse · carte {map}.";
        Status = "En route vers le coffre de chasse…";
        NotifyHuntRun();
        bool arrived = false, cancelled = false;
        try
        {
            arrived = await travel(pid, map, cts.Token);
            cancelled = cts.IsCancellationRequested;
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception) { arrived = false; }
        finally
        {
            if (ReferenceEquals(_goCts, cts)) { _goCts = null; Going = false; }
            cts.Dispose();
        }
        if (_pid != pid) return false;
        if (cancelled || !arrived)
        {
            if (_huntSequence == sequence) CurrentStepText = previous;
            Status = cancelled ? "Trajet annulé." : "Trajet vers le coffre de chasse échoué.";
            NotifyHuntRun();
            return false;
        }
        if (!keepTravel && _huntSequence == sequence) CurrentStepText = previous;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanStopHunt))]
    private void StopHunt()
    {
        if (!CanStopHunt || StopHuntScript is not { } stop || !_huntScript.TryGetValue(_pid, out var kind)) return;
        string file = kind == HuntScriptKind.Fouduglen ? FouduglenScriptFile : AutoHuntScriptFile;
        if (!stop(_pid, file))
        {
            Status = "Arrêt du script refusé.";
            NotifyHuntRun();
            return;
        }
        _huntScript.Remove(_pid);
        _huntScriptSeen[_pid] = false;
        Status = kind == HuntScriptKind.Fouduglen ? "Fouduglen arrêté." : "Chasse automatique arrêtée.";
        NotifyHuntRun();
    }
}
