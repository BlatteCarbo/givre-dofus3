using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne d'edition de plage : nom + SCRIPT ASSIGNE (précis, choisi dans la bibliothèque) + 7 toggles de jour +
/// heure debut/fin (texte HH:mm). Ecrit directement dans son <see cref="PlanningSlot"/> et notifie le parent (persistance)
/// a chaque changement. <see cref="Conflict"/> = la plage en chevauche une autre (signalé, à corriger). <see cref="Accent"/>
/// = couleur partagée avec le bloc du calendrier.</summary>
public partial class PlanningSlotRow : ObservableObject
{
    public PlanningSlot Model { get; }
    private readonly Action _onChange;
    private bool _loading;
    // Ligne RETIRÉE de l'éditeur (rechargement d'un perso, suppression) : ses contrôles peuvent encore écrire en retour
    // pendant leur démontage (ComboBox dont la liste ScriptNames change -> SelectedItem=null) -> plus JAMAIS d'écriture
    // dans le PlanningSlot VIVANT du store (c'est ainsi que les assignations étaient effacées au départ/retour d'un bot).
    private bool _detached;
    private bool _resyncPosted;
    private int _heals;   // ré-affichages demandés par un null parasite de la ComboBox (bornés : jamais de boucle)

    /// <summary>Noms de scripts disponibles (bibliothèque) — liste PARTAGÉE, tenue à jour par la VM.</summary>
    public ObservableCollection<string> Choices { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    /// <summary>Script SÉLECTIONNÉ dans la ComboBox (null = rien d'affiché). Ce n'est qu'un reflet : l'assignation vraie est
    /// <see cref="PlanningSlot.Script"/>, modifiée seulement par un choix explicite (jamais par un null de la ComboBox).</summary>
    [ObservableProperty] public partial string? Script { get; set; }
    /// <summary>Nom assigné mais ABSENT de la bibliothèque (script supprimé) : « script introuvable », jamais effacé.</summary>
    [ObservableProperty] public partial string MissingName { get; set; } = "";
    [ObservableProperty] public partial bool IsMissing { get; set; }
    /// <summary>La plage a un script assigné (présent ou introuvable) : bouton « Retirer » visible.</summary>
    [ObservableProperty] public partial bool HasAssignment { get; set; }
    [ObservableProperty] public partial bool Mon { get; set; }
    [ObservableProperty] public partial bool Tue { get; set; }
    [ObservableProperty] public partial bool Wed { get; set; }
    [ObservableProperty] public partial bool Thu { get; set; }
    [ObservableProperty] public partial bool Fri { get; set; }
    [ObservableProperty] public partial bool Sat { get; set; }
    [ObservableProperty] public partial bool Sun { get; set; }
    [ObservableProperty] public partial string StartText { get; set; } = "08:00";
    [ObservableProperty] public partial string EndText { get; set; } = "12:00";
    [ObservableProperty] public partial bool Conflict { get; set; }
    [ObservableProperty] public partial IBrush Accent { get; set; } = Brushes.Gray;

    public PlanningSlotRow(PlanningSlot model, ObservableCollection<string> choices, Action onChange)
    {
        Model = model; Choices = choices; _onChange = onChange;
        _loading = true;
        Name = model.Name;
        ReadScript();
        Mon = (model.Days & 1) != 0; Tue = (model.Days & 2) != 0; Wed = (model.Days & 4) != 0; Thu = (model.Days & 8) != 0;
        Fri = (model.Days & 16) != 0; Sat = (model.Days & 32) != 0; Sun = (model.Days & 64) != 0;
        StartText = ToHm(model.StartMin); EndText = ToHm(model.EndMin);
        _loading = false;
        Choices.CollectionChanged += OnChoicesChanged;
    }

    /// <summary>Retire la ligne de l'éditeur : elle ignore désormais toute écriture venue de ses contrôles (et ne suit plus
    /// la liste des choix, partagée avec la VM -> pas de fuite).</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        Choices.CollectionChanged -= OnChoicesChanged;
    }

    // Liste des choix modifiée (script ajouté / retiré / renommé) : ré-affiche l'assignation du modèle une fois la liste
    // stabilisée — l'écran reflète toujours le modèle, même après un vidage transitoire de la ComboBox.
    private void OnChoicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => PostResync();

    // Différé : les ComboBox traitent le même changement de liste APRÈS nous.
    private void PostResync()
    {
        if (_resyncPosted || _detached) return;
        _resyncPosted = true;
        Dispatcher.UIThread.Post(() => { _resyncPosted = false; SyncScriptFromModel(); }, DispatcherPriority.Background);
    }

    /// <summary>Ré-affiche l'assignation depuis le modèle (liste des choix modifiée : ajout / suppression / renommage d'un
    /// script de la bibliothèque), sans re-persister.</summary>
    public void SyncScriptFromModel()
    {
        if (_detached) return;
        _loading = true;
        ReadScript();
        _loading = false;
    }

    // Modèle -> affichage : sélectionné si le script est dans la liste, sinon état « introuvable » (nom conservé).
    private void ReadScript()
    {
        string name = Model.Script ?? "";
        bool assigned = !string.IsNullOrWhiteSpace(name);
        bool known = assigned && Choices.Contains(name);
        Script = known ? name : null;
        MissingName = assigned && !known ? name : "";
        IsMissing = MissingName.Length > 0;
        HasAssignment = assigned;
    }

    /// <summary>« Retirer » : désassigne EXPLICITEMENT la plage (elle retombe sur le script de repli figé). Sans effet sur
    /// une ligne détachée (éditeur rechargé entre-temps). Renvoie true si le modèle a changé (-> la VM sauvegarde).</summary>
    public bool ClearScript()
    {
        if (_detached || !PlanningEdit.ClearScript(Model)) return false;
        SyncScriptFromModel();   // ComboBox sur l'invite « Script de la plage », bandeau « introuvable » masqué
        return true;
    }

    partial void OnNameChanged(string v) => Apply();

    partial void OnScriptChanged(string? v)
    {
        if (_loading || _detached) return;
        if (string.IsNullOrWhiteSpace(v))
        {
            // La ComboBox n'a pas d'entrée « aucun » : un null ici n'est JAMAIS un choix de l'utilisateur (liste des choix
            // reconstruite, élément retiré, conteneur démonté). L'assignation du modèle reste INTACTE ; on ré-affiche
            // (borné : pas de boucle si la ComboBox ne pouvait pas afficher l'assignation).
            if (!string.IsNullOrWhiteSpace(Model.Script) && _heals++ < 3) PostResync();
            return;
        }
        _heals = 0;
        MissingName = ""; IsMissing = false; HasAssignment = true;
        if (PlanningEdit.AssignScript(Model, v)) _onChange();   // choix explicite -> assignation + sauvegarde
    }

    partial void OnMonChanged(bool v) => Apply();
    partial void OnTueChanged(bool v) => Apply();
    partial void OnWedChanged(bool v) => Apply();
    partial void OnThuChanged(bool v) => Apply();
    partial void OnFriChanged(bool v) => Apply();
    partial void OnSatChanged(bool v) => Apply();
    partial void OnSunChanged(bool v) => Apply();
    partial void OnStartTextChanged(string v) => Apply();
    partial void OnEndTextChanged(string v) => Apply();

    private void Apply()
    {
        if (_loading || _detached) return;
        Model.Name = Name ?? "";
        // (le SCRIPT n'est pas réécrit ici : seul OnScriptChanged y touche, sur un choix explicite)
        Model.Days = (Mon ? 1 : 0) | (Tue ? 2 : 0) | (Wed ? 4 : 0) | (Thu ? 8 : 0) | (Fri ? 16 : 0) | (Sat ? 32 : 0) | (Sun ? 64 : 0);
        if (TryHm(StartText, out int st)) Model.StartMin = st;
        if (TryHm(EndText, out int en)) Model.EndMin = en;
        _onChange();
    }

    private static string ToHm(int m) => $"{m / 60:00}:{m % 60:00}";

    private static bool TryHm(string s, out int minutes)
    {
        minutes = 0;
        var parts = (s ?? "").Trim().Split(':');
        // Minutes sur DEUX chiffres exigées : une saisie partielle (« 18:0 ») n'est jamais appliquée en cours de frappe.
        if (parts.Length != 2 || parts[1].Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int mi)) return false;
        if (h < 0 || h > 23 || mi < 0 || mi > 59) return false;
        minutes = h * 60 + mi;
        return true;
    }
}

/// <summary>Une ligne de la BIBLIOTHEQUE de scripts : nom editable + info (nb de lignes). Le corps Lua est fige a l'ajout
/// (on re-capture / re-charge si besoin). La zone de saisie écrit dans <see cref="Draft"/> (frappe en cours) ; le renommage
/// n'est VALIDÉ (<see cref="CommitRename"/>) qu'à Entrée, à la perte du focus, ou au rechargement de l'éditeur — jamais
/// perdu, jamais de nom vide / doublon transitoire. Il est propagé aux plages qui référencent l'ancien nom — c'est la VM qui
/// l'applique au modèle (<c>PlanningEdit.RenameScript</c>).</summary>
public partial class PlanningScriptRow : ObservableObject
{
    public PlanningScript Model { get; }
    // VM : valide et applique le renommage (modèle + plages), renvoie le nom RETENU (vide refusé, doublon suffixé ′).
    private readonly Func<PlanningScriptRow, string, string> _onRename;
    private bool _loading, _detached;

    /// <summary>Nom VALIDÉ (celui du modèle). L'affecter applique le renommage (tests / sonde).</summary>
    [ObservableProperty] public partial string Name { get; set; } = "";
    /// <summary>Texte de la zone de saisie (frappe en cours, non validée).</summary>
    [ObservableProperty] public partial string Draft { get; set; } = "";

    public string Info { get; }

    public PlanningScriptRow(PlanningScript model, Func<PlanningScriptRow, string, string> onRename)
    {
        Model = model; _onRename = onRename;
        _loading = true;
        Name = model.Name;
        Draft = model.Name;
        int lines = string.IsNullOrEmpty(model.Body) ? 0 : model.Body.Split('\n').Length;
        Info = Loc.Fmt("L.Bots.Planning.ScriptInfo", lines);
        _loading = false;
    }

    /// <summary>Retire la ligne de l'éditeur : plus aucun renommage venu de sa zone de saisie (démontage).</summary>
    public void Detach() => _detached = true;

    /// <summary>Valide la saisie en cours (Entrée, perte du focus, rechargement de l'éditeur pendant la frappe). Sans effet si
    /// rien n'a changé ou si la ligne est détachée.</summary>
    public void CommitRename()
    {
        if (_detached) return;
        string d = Draft ?? "";
        if (d == Name) return;
        Name = d;   // -> OnNameChanged : renommage appliqué (ou refusé / ajusté), Draft resynchronisé
    }

    /// <summary>Échap : abandonne la saisie en cours (la zone reprend le nom validé).</summary>
    public void CancelRename()
    {
        if (!_detached) Draft = Name;
    }

    partial void OnNameChanged(string v)
    {
        if (_loading || _detached) return;
        string kept = _onRename(this, v ?? "");
        // Nom refusé (vide) ou ajusté (rogné / doublon ′) : le nom validé ET la zone de saisie reprennent le nom RETENU.
        _loading = true;
        Name = kept;
        Draft = kept;
        _loading = false;
    }
}

/// <summary>Un BLOC du calendrier « semaine type » : une plage rendue à sa position (marge haute) et hauteur (par minute)
/// dans une colonne de jour, avec libellé + horaire + script + couleur. Réécrit à chaque changement de plage.</summary>
public sealed class PlanningBlock
{
    public Thickness Margin { get; init; }      // décalage vertical (position par minute)
    public double Height { get; init; }
    public string Name { get; init; } = "";
    public string TimeText { get; init; } = "";
    public string ScriptText { get; init; } = "";
    public string Tip { get; init; } = "";
    public IBrush Fill { get; init; } = Brushes.Transparent;
    public IBrush Stroke { get; init; } = Brushes.Transparent;
    public bool ShowTime => Height >= 26;
    public bool ShowScript => Height >= 40 && !string.IsNullOrEmpty(ScriptText);
}

/// <summary>Une colonne de jour de la grille "semaine type" : libelle (Lu..Di) + nb de plages + les BLOCS positionnés.</summary>
public sealed class PlanningDayColumn : ObservableObject
{
    public string Label { get; init; } = "";
    public int[] Hours { get; } = new int[24];   // fond « réglé » (24 lignes horaires, valeurs inutilisées)
    public ObservableCollection<PlanningBlock> Blocks { get; } = new();
    private string _countText = "";
    public string CountText { get => _countText; set { _countText = value; OnPropertyChanged(); } }
    public bool HasBlocks => Blocks.Count > 0;
    public void RaiseCount() { CountText = Blocks.Count > 0 ? Blocks.Count.ToString() : ""; OnPropertyChanged(nameof(HasBlocks)); }
}

/// <summary>VM de l'onglet Planning (par perso selectionne) : active/desactive, humanisation (jitter), bibliothèque de
/// scripts, liste de plages editables (chacune avec SON script), calendrier « semaine type » à blocs, horloge live +
/// statut. La persistance (planning.json) est immediate ; l'EXECUTION est faite par <c>BotsViewModel.Tick</c>.</summary>
public partial class PlanningViewModel : ObservableObject
{
    private CharPlanning _cp = new();
    private bool _loading;

    // Hauteur (px) de la colonne calendrier = 24 cases horaires de 11 px (9 + 2 marge) -> position par minute.
    private const double ColHeight = 264.0;
    private const double PerMin = ColHeight / 1440.0;

    private static readonly Color[] PaletteColors =
    {
        Color.Parse("#3E9B6E"), Color.Parse("#4A82C4"), Color.Parse("#C9873E"), Color.Parse("#9B5FB0"),
        Color.Parse("#C74A6B"), Color.Parse("#3EA6A6"), Color.Parse("#B08A3E"), Color.Parse("#6E7BC9"),
    };
    private static readonly IBrush[] PaletteSolid = PaletteColors.Select(c => (IBrush)new ImmutableSolidColorBrush(c)).ToArray();
    private static readonly IBrush[] PaletteFill = PaletteColors.Select(c => (IBrush)new ImmutableSolidColorBrush(Color.FromArgb(0x5E, c.R, c.G, c.B))).ToArray();
    private static IBrush Solid(int i) => PaletteSolid[i % PaletteSolid.Length];
    private static IBrush Fill(int i) => PaletteFill[i % PaletteFill.Length];

    /// <summary>Fourni par BotsViewModel : renvoie le script Lua courant de l'editeur (fige a l'activation).</summary>
    public Func<string>? GetCurrentScript { get; set; }
    /// <summary>Fourni par BotsViewModel : ce texte est-il le MODÈLE d'exemple de l'éditeur Lua (affiché tant que le perso
    /// n'a pas de script) ? Le modèle n'est JAMAIS figé comme script de repli du planning (activation, « Figer »).</summary>
    public Func<string, bool>? IsTemplateScript { get; set; }
    private bool IsTemplate(string? s) => !string.IsNullOrWhiteSpace(s) && IsTemplateScript?.Invoke(s!) == true;
    // Script de repli RÉEL : ni vide, ni le modèle d'exemple (couvre un modèle figé par une version précédente).
    private bool IsRealScript(string? s) => !string.IsNullOrWhiteSpace(s) && !IsTemplate(s);
    /// <summary>Fourni par BotsViewModel : Account.Id du bot affiché (lie le perso à son compte pour la connexion auto).</summary>
    public Func<string?>? GetCurrentAccountId { get; set; }
    /// <summary>Journal des actions de l'éditeur (ajout d'un .lua...) : console du bot, ou notification pour un perso hors ligne.</summary>
    public Action<string, LuaLogLevel>? Log { get; set; }

    /// <summary>Boutons liés à l'éditeur Lua (figer / ajouter le script courant) : masqués dans la modale d'un perso hors
    /// ligne, dont l'éditeur Lua n'est pas celui affiché.</summary>
    [ObservableProperty] public partial bool ShowEditorActions { get; set; } = true;
    /// <summary>« Planning activé » verrouillé (follower d'équipe : c'est le chef qui pilote).</summary>
    [ObservableProperty] public partial bool EnableLocked { get; set; }

    [ObservableProperty] public partial bool Enabled { get; set; }
    /// <summary>« Vrai planning » : connecte le compte au début de chaque plage et le déconnecte à la fin.</summary>
    [ObservableProperty] public partial bool AutoConnect { get; set; }
    [ObservableProperty] public partial int JitterMin { get; set; }
    [ObservableProperty] public partial string ClockText { get; set; } = "—";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool HasScript { get; set; }
    [ObservableProperty] public partial string NewScriptName { get; set; } = "";

    public ObservableCollection<PlanningSlotRow> Slots { get; } = new();
    public bool HasSlots => Slots.Count > 0;

    /// <summary>Bibliotheque de scripts du perso : chaque plage en assigne UN précis.</summary>
    public ObservableCollection<PlanningScriptRow> Scripts { get; } = new();
    /// <summary>Noms des scripts de la bibliothèque — source des ComboBox « script » par plage (tenue à jour en place).</summary>
    public ObservableCollection<string> ScriptNames { get; } = new();

    /// <summary>Grille "semaine type" (7 colonnes de jour) reflétant les plages en blocs libellés.</summary>
    public ObservableCollection<PlanningDayColumn> WeekDays { get; } = new();
    private static readonly string[] DayKeys = { "L.Day.D0", "L.Day.D1", "L.Day.D2", "L.Day.D3", "L.Day.D4", "L.Day.D5", "L.Day.D6" };

    private void RebuildWeek()
    {
        if (WeekDays.Count != 7)
        {
            WeekDays.Clear();
            for (int i = 0; i < 7; i++) WeekDays.Add(new PlanningDayColumn { Label = Loc.T(DayKeys[i]) });
        }
        foreach (var col in WeekDays) col.Blocks.Clear();

        for (int i = 0; i < _cp.Slots.Count; i++)
        {
            var s = _cp.Slots[i];
            if (s.Days == 0) continue;
            string label = string.IsNullOrWhiteSpace(s.Name) ? Loc.Fmt("L.Bots.Planning.SlotDefaultN", i + 1) : s.Name;
            string time = $"{ToHm(s.StartMin)}–{ToHm(s.EndMin)}";
            string script = s.Script ?? "";
            if (PlanningEval.MissingScript(_cp, s) is not null) script = "⚠ " + script;   // script supprimé de la bibliothèque
            string tip = string.IsNullOrEmpty(script) ? $"{label} · {time}" : $"{label} · {time} · {script}";
            IBrush fill = Fill(i), stroke = Solid(i);
            bool overnight = s.EndMin <= s.StartMin;
            for (int d = 0; d < 7; d++)
            {
                if ((s.Days & (1 << d)) == 0) continue;
                if (!overnight) AddBlock(d, s.StartMin, s.EndMin, label, time, script, tip, fill, stroke);
                else { AddBlock(d, s.StartMin, 1440, label, time, script, tip, fill, stroke); AddBlock((d + 1) % 7, 0, s.EndMin, label, time, script, tip, fill, stroke); }
            }
        }
        foreach (var col in WeekDays) col.RaiseCount();
    }

    private void AddBlock(int day, int startMin, int endMin, string name, string time, string script, string tip, IBrush fill, IBrush stroke)
    {
        double top = startMin * PerMin;
        double h = Math.Max(6, (endMin - startMin) * PerMin);
        WeekDays[day].Blocks.Add(new PlanningBlock
        {
            Margin = new Thickness(0, top, 0, 0), Height = h,
            Name = name, TimeText = time, ScriptText = script, Tip = tip, Fill = fill, Stroke = stroke,
        });
    }

    // Couleur d'accent (liée au bloc) + drapeau de CHEVAUCHEMENT sur chaque plage. Recalculé à chaque changement.
    private void SyncRowVisuals()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            Slots[i].Accent = Solid(i);
            bool conflict = false;
            for (int j = 0; j < Slots.Count; j++)
                if (j != i && PlanningEval.Overlaps(Slots[i].Model, Slots[j].Model)) { conflict = true; break; }
            Slots[i].Conflict = conflict;
        }
    }

    // Met la liste ScriptNames au diapason de la bibliothèque (même ORDRE) SANS Clear() : préserve la sélection des
    // ComboBox dont le nom reste. Une ComboBox dont le nom disparaît peut écrire null en retour : sans effet sur le modèle
    // (PlanningSlotRow l'ignore) -> l'appelant ré-affiche ensuite les plages (SyncSlotScripts).
    private void RebuildScriptNames()
    {
        var want = _cp.Scripts.Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        for (int i = ScriptNames.Count - 1; i >= 0; i--) if (!want.Contains(ScriptNames[i])) ScriptNames.RemoveAt(i);
        for (int i = 0; i < want.Count; i++)
        {
            if (i < ScriptNames.Count && ScriptNames[i] == want[i]) continue;
            int j = ScriptNames.IndexOf(want[i]);
            if (j >= 0) ScriptNames.Move(j, i); else ScriptNames.Insert(i, want[i]);
        }
    }

    // Ré-affiche l'assignation de chaque plage (sélection, ou « script introuvable ») après un changement de la liste.
    private void SyncSlotScripts()
    {
        foreach (var r in Slots) r.SyncScriptFromModel();
    }

    /// <summary>Valide les renommages de la bibliothèque encore EN COURS DE FRAPPE (rechargement de l'éditeur, fermeture de
    /// la modale hors ligne) : appliqués au perso affiché, jamais perdus.</summary>
    public void CommitPendingRenames()
    {
        foreach (var r in Scripts.ToList()) r.CommitRename();
    }

    public void Load(string? charName)
    {
        // ⚠ D'ABORD détacher et retirer les lignes du perso PRÉCÉDENT, AVANT de toucher à la liste des choix : leurs ComboBox
        // restaient liées à ScriptNames et écrivaient null en retour quand un nom en disparaissait (Load(null) au départ du
        // dernier bot / fin de plage, autre perso sélectionné, modale hors ligne) -> les assignations plage -> script de
        // l'ancien perso étaient effacées dans le store (objets partagés) puis sauvées au Save suivant. Détachées, elles ne
        // touchent plus jamais leur PlanningSlot.
        foreach (var r in Slots) r.Detach();
        Slots.Clear();
        // Renommage de la bibliothèque EN COURS DE FRAPPE (validé seulement à Entrée / perte du focus) : appliqué au perso
        // PRÉCÉDENT (_cp encore le sien) avant de retirer ses lignes, sinon perdu au rechargement (Reconnecter, re-clé,
        // autre bot). Les plages sont déjà détachées : le changement de ScriptNames qui s'ensuit ne les touche pas.
        CommitPendingRenames();
        var oldScripts = Scripts.ToList();
        Scripts.Clear();
        foreach (var r in oldScripts) r.Detach();
        _loading = true;
        _cp = PlanningStore.Get(charName);
        Enabled = _cp.Enabled;
        AutoConnect = _cp.AutoConnect;
        JitterMin = _cp.JitterMin;
        HasScript = IsRealScript(_cp.Script);
        foreach (var sc in _cp.Scripts) Scripts.Add(new PlanningScriptRow(sc, ScriptRenamed));
        RebuildScriptNames();
        foreach (var s in _cp.Slots) Slots.Add(new PlanningSlotRow(s, ScriptNames, Persist));
        OnPropertyChanged(nameof(HasSlots));
        SyncRowVisuals();
        RebuildWeek();
        _loading = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_loading) return;
        _cp.Enabled = value;
        // Fige le script de l'éditeur comme REPLI s'il n'y en a pas encore (ou si c'est le modèle figé par une version
        // précédente) — jamais le MODÈLE d'exemple affiché à un perso sans script : le planning le lancerait tel quel.
        if (value && !IsRealScript(_cp.Script) && GetCurrentScript?.Invoke() is { } cur && IsRealScript(cur)) _cp.Script = cur;
        HasScript = IsRealScript(_cp.Script);
        PlanningStore.Save();
    }

    partial void OnAutoConnectChanged(bool value)
    {
        if (_loading) return;
        _cp.AutoConnect = value;
        // Lie tout de suite le perso au compte affiché (sinon au prochain passage du scheduler, dès qu'il le voit en jeu).
        if (value && GetCurrentAccountId?.Invoke() is { Length: > 0 } acc) _cp.AccountId = acc;
        PlanningStore.Save();
    }

    partial void OnJitterMinChanged(int value)
    {
        if (_loading) return;
        _cp.JitterMin = value < 0 ? 0 : value;
        PlanningStore.Save();
    }

    [RelayCommand]
    private void AddSlot()
    {
        var s = new PlanningSlot { Days = 0b111_1111, StartMin = 8 * 60, EndMin = 12 * 60 };
        _cp.Slots.Add(s);
        Slots.Add(new PlanningSlotRow(s, ScriptNames, Persist));
        OnPropertyChanged(nameof(HasSlots));
        SyncRowVisuals();
        RebuildWeek();
        PlanningStore.Save();
    }

    [RelayCommand]
    private void RemoveSlot(PlanningSlotRow? row)
    {
        if (row is null) return;
        row.Detach();
        Slots.Remove(row);
        _cp.Slots.Remove(row.Model);
        OnPropertyChanged(nameof(HasSlots));
        SyncRowVisuals();
        RebuildWeek();
        PlanningStore.Save();
    }

    /// <summary>(Re)fige le script courant de l'editeur comme script de REPLI (plages sans script assigné). Jamais le MODÈLE
    /// d'exemple (refusé, signalé) ; un éditeur vide efface le repli (comportement historique).</summary>
    [RelayCommand]
    private void CaptureScript()
    {
        if (GetCurrentScript is not { } g) return;
        string cur = g();
        if (IsTemplate(cur)) { Log?.Invoke(Loc.T("L.Bots.Planning.CaptureTemplate"), LuaLogLevel.Warning); return; }
        _cp.Script = cur;
        HasScript = IsRealScript(_cp.Script);
        PlanningStore.Save();
    }

    /// <summary>« Retirer » (à côté de la liste de la plage, ou dans le bandeau « script introuvable ») : la plage n'a plus de
    /// script assigné -> repli figé (<see cref="CharPlanning.Script"/>), jamais le modèle d'exemple.</summary>
    [RelayCommand]
    private void ClearSlotScript(PlanningSlotRow? row)
    {
        if (row is null || !Slots.Contains(row) || !row.ClearScript()) return;
        SyncRowVisuals();
        RebuildWeek();
        PlanningStore.Save();
    }

    /// <summary>Ajoute le script courant de l'editeur Lua a la BIBLIOTHEQUE sous le nom saisi (à assigner ensuite à une plage).</summary>
    [RelayCommand]
    private void AddScript()
    {
        string body = GetCurrentScript?.Invoke() ?? "";
        if (string.IsNullOrWhiteSpace(body)) return;
        AddLibraryScript(NewScriptName, body);
        NewScriptName = "";
    }

    /// <summary>Ajoute un script CHARGÉ DEPUIS UN FICHIER .lua à la bibliothèque (appelé par le sélecteur de fichier du
    /// code-behind). Nom par défaut = nom du fichier. Évite le détour éditeur Lua → figer. <paramref name="sourcePath"/> :
    /// chemin du fichier, mémorisé pour RECHARGER le script au début de chaque plage (trajet modifié sur disque).</summary>
    public void AddScriptFromFile(string name, string body, string? sourcePath = null) => AddLibraryScript(name, body, sourcePath);

    private void AddLibraryScript(string name, string body, string? sourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        // Nom GÉNÉRÉ (« Script N ») : libre dans la bibliothèque ET inutilisé par les plages -> ne reprend jamais en silence
        // une plage « script introuvable ». Nom SAISI (champ, nom du fichier) : sans doublon (′) ; s'il est celui d'une plage
        // « introuvable », elle est RÉTABLIE — et c'est journalisé.
        string nm = string.IsNullOrWhiteSpace(name)
            ? PlanningEdit.DefaultScriptName(_cp, n => Loc.Fmt("L.Bots.Planning.ScriptDefault", n))
            : PlanningEdit.UniqueScriptName(_cp, name.Trim());
        var relinked = PlanningEdit.OrphanedSlots(_cp, nm);   // AVANT l'ajout : ce nom n'est encore porté par aucun script
        var sc = new PlanningScript { Name = nm, Body = body, SourcePath = sourcePath ?? "" };
        _cp.Scripts.Add(sc);
        Scripts.Add(new PlanningScriptRow(sc, ScriptRenamed));
        RebuildScriptNames();
        SyncSlotScripts();   // une plage « script introuvable » qui portait ce nom le retrouve
        RebuildWeek();
        PlanningStore.Save();
        LogRelinked(relinked, nm);
    }

    // Plages « script introuvable » rétablies par un nom SAISI (ajout / renommage) : une ligne par plage, jamais en silence.
    private void LogRelinked(System.Collections.Generic.List<PlanningSlot> relinked, string name)
    {
        foreach (var s in relinked)
        {
            int i = _cp.Slots.IndexOf(s);
            string label = string.IsNullOrWhiteSpace(s.Name) ? Loc.Fmt("L.Bots.Planning.SlotDefaultN", i + 1) : s.Name;
            Log?.Invoke(Loc.Fmt("L.Bots.Planning.ScriptRelinked", label, name), LuaLogLevel.Info);
        }
    }

    [RelayCommand]
    private void RemoveScript(PlanningScriptRow? row)
    {
        if (row is null) return;
        row.Detach();
        Scripts.Remove(row);
        // Les plages qui l'assignaient GARDENT son nom -> « script introuvable » (jamais vidées en silence, rien lancé).
        PlanningEdit.RemoveScript(_cp, row.Model);
        RebuildScriptNames();
        SyncSlotScripts();
        SyncRowVisuals();
        RebuildWeek();
        PlanningStore.Save();
    }

    // Un script de la bibliothèque est renommé -> MODÈLE d'abord (nom + plages qui l'assignaient), PUIS la liste des choix,
    // PUIS l'affichage des plages : l'assignation suit le script (avant : la liste était reconstruite en premier, la
    // ComboBox se vidait et la plage perdait son script). Renvoie le nom retenu (vide refusé, doublon suffixé ′).
    private string ScriptRenamed(PlanningScriptRow row, string wanted)
    {
        if (_loading) return row.Model.Name;
        string old = row.Model.Name;
        string kept = PlanningEdit.RenameScript(_cp, row.Model, wanted, out var relinked);
        if (kept == old) return kept;
        RebuildScriptNames();
        SyncSlotScripts();
        RebuildWeek();
        PlanningStore.Save();
        LogRelinked(relinked, kept);   // nom saisi = celui d'une plage « introuvable » : rétablie, journalisé
        return kept;
    }

    private void Persist()
    {
        if (_loading) return;
        SyncRowVisuals();       // une plage a changé (jours/heures/nom/script) -> recalcul chevauchements + accents
        RebuildWeek();          // -> maj des blocs du calendrier
        PlanningStore.Save();
    }

    private static string ToHm(int m) => $"{m / 60:00}:{m % 60:00}";

    /// <summary>Mis a jour par le tick de BotsViewModel : horloge live + statut (active / en attente / desactive).</summary>
    public void UpdateClock(DateTime now, bool active)
    {
        ClockText = PlanningText.Clock(now);
        IsActive = active;
        StatusText = !Enabled ? Loc.T("L.Bots.Planning.Off")
                   : active ? Loc.T("L.Bots.Planning.Active")
                            : Loc.T("L.Bots.Planning.Idle");
    }
}

/// <summary>Libellés d'heure du planning, dans la langue de l'app (<see cref="LocDate"/> : le build AOT est en culture
/// invariante). Jours ABRÉGÉS lisibles (« mer. ») — pas les pastilles « Lu/Ma/Me » des plages, qu'on prenait pour du texte coupé.</summary>
public static class PlanningText
{
    /// <summary>Horloge de l'onglet : « mer. 22:41 ».</summary>
    public static string Clock(DateTime now) => LocDate.DayShort(now) + " " + LocDate.Hm(now);

    /// <summary>Échéance ABSOLUE (texte durable, ex. statut d'un compte) : « mer. 08:00 », avec la date (« mer. 30 sept. 08:00 »)
    /// quand l'échéance est à 6 jours ou plus (même jour de la semaine suivante, sinon ambigu).</summary>
    public static string Absolute(DateTime at, DateTime now)
        => (at.Date - now.Date).Days >= 6
            ? LocDate.DayMonth(at) + " " + LocDate.Hm(at)
            : LocDate.DayShort(at) + " " + LocDate.Hm(at);

    /// <summary>Échéance relative à maintenant : « aujourd'hui 22:00 », « demain 08:00 », sinon « jeu. 08:00 ».</summary>
    public static string When(DateTime at, DateTime now)
    {
        if (at.Date == now.Date) return Loc.Fmt("L.Planning.When.Today", LocDate.Hm(at));
        if (at.Date == now.Date.AddDays(1)) return Loc.Fmt("L.Planning.When.Tomorrow", LocDate.Hm(at));
        return Absolute(at, now);
    }
}
