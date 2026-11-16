using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne « libellé / valeur » de la fiche (lecture seule).</summary>
public sealed record InspectorLine(string Label, string Value);

/// <summary>Une recette d'atelier affichable (marque si elle est réalisable avec le sac actuel).</summary>
public sealed record CraftRecipeRow(RecipeInfo Info, bool CanCraftNow)
{
    public string Name => Info.Name;
    public string Caption => $"{Info.Name} · {Info.Level}";
}

/// <summary>Une ligne d'ingrédient : requis / possédé et manque (rouge) ou surplus (vert) — mêmes repères que les outils d'HDV de craft.</summary>
public sealed record CraftLineRow(string Name, int Need, int Have)
{
    public bool Missing => Have < Need;
    public string NeedHave => $"{Have} / {Need}";
    public string Delta => Missing ? $"−{Need - Have}" : (Have > Need ? $"+{Have - Need}" : "0");
}

/// <summary>Un type de ressource présent sur la carte (« Blé · Faucher ») avec ses exemplaires disponibles / total.</summary>
public sealed record ResourceGroupRow(Bitmap? Icon, string Name, string Verb, int Available, int Total, MapEntity First)
{
    public string Count => $"{Available}/{Total}";
    public string Caption => string.IsNullOrEmpty(Verb) ? Name : $"{Name} · {Verb}";
}

/// <summary>Une action de PNJ déclarée par le jeu (id d'action + libellé).</summary>
public sealed record NpcActionOption(int Id, string Label);

/// <summary>Une réponse de dialogue PNJ. Le jeu n'expose ici que l'identifiant : le libellé n'est jamais inventé.</summary>
public sealed record DialogReplyOption(int Id, string Label);

/// <summary>Une ligne de composition d'un groupe de monstres (icône locale du jeu si disponible).</summary>
public sealed record InspectorMember(Bitmap? Icon, string Name, int Count, string Level);

/// <summary>
/// Enveloppe d'affichage de <see cref="MapInspectionModel"/> pour UN bot : filtres, recherche, fiche en lecture seule, sélecteur de
/// superposition, légende, fraîcheur. Le contrôle de carte ne reçoit que les listes FILTRÉES (<see cref="VisibleActors"/>).
/// </summary>
public sealed partial class MapInspectorViewModel : ObservableObject
{
    private readonly MapInspectionModel _m = new();
    private bool _loading = true;

    public MapInspectorViewModel()
    {
        var c = AppConfig.Current;   // filtres, mode et favoris PERSISTÉS : retrouvés tels quels au lancement
        ShowMonsters = c.MapShowMonsters; ShowNpcs = c.MapShowNpcs; ShowPlayers = c.MapShowPlayers; ShowResources = c.MapShowResources;
        ActionMode = string.Equals(c.MapInteraction, "act", StringComparison.Ordinal);
        _loading = false;
    }

    private void SavePrefs()
    {
        if (_loading) return;
        var c = AppConfig.Current;
        c.MapShowMonsters = ShowMonsters; c.MapShowNpcs = ShowNpcs; c.MapShowPlayers = ShowPlayers; c.MapShowResources = ShowResources;
        c.MapInteraction = ActionMode ? "act" : "inspect";
        AppConfig.Save();
    }

    /// <summary>Faux = INSPECTION (un clic ouvre une fiche, ne déplace jamais) ; vrai = ACTION (aperçu du trajet puis confirmation).</summary>
    [ObservableProperty] public partial bool ActionMode { get; set; }
    public MapInteractionMode Mode => ActionMode ? MapInteractionMode.Act : MapInteractionMode.Inspect;
    public bool InspectMode => !ActionMode;
    partial void OnActionModeChanged(bool value) { OnPropertyChanged(nameof(Mode)); OnPropertyChanged(nameof(InspectMode)); OnPropertyChanged(nameof(HasPreview)); SavePrefs(); }

    /// <summary>Nombre de pas du trajet prévisualisé sur la carte (renseigné par la vue) ; 0 = aucun.</summary>
    [ObservableProperty] public partial int PreviewSteps { get; set; }
    public bool HasPreview => PreviewSteps > 0;
    public string PreviewText => Loc.Fmt("L.MapInsp.PreviewSteps", PreviewSteps);
    partial void OnPreviewStepsChanged(int value) { OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewText)); }

    [RelayCommand] public void SetInspectMode() => ActionMode = false;
    [RelayCommand] public void SetActionMode() => ActionMode = true;

    // ---- retour d'action : attente / confirmé / refusé / inconnu ----
    private readonly MapActionTracker _action = new();
    public Func<int?>? SelfCellProvider { get; set; }
    public Func<long>? MapIdProvider { get; set; }
    [ObservableProperty] public partial ActionState FeedbackState { get; set; }
    [ObservableProperty] public partial string FeedbackText { get; set; } = "";
    public bool HasFeedback => FeedbackState != ActionState.None;

    /// <summary>Enregistre une action demandée. <paramref name="accepted"/> faux = refusée tout de suite (position inconnue, pas de relais…) ;
    /// vrai = en attente d'une PREUVE (cellule atteinte ou <paramref name="probe"/>), sinon « inconnu » après le délai.</summary>
    public void NoteAction(string label, int target, bool accepted, Func<bool>? probe = null)
    {
        if (!accepted) _action.Refuse(label); else _action.Begin(label, target, Environment.TickCount64, 8000, probe);
        PublishFeedback();
    }

    public void PollFeedback()
    {
        OpenInteraction = OpenInteractionProbe?.Invoke();
        _action.Poll(SelfCellProvider?.Invoke(), Environment.TickCount64);
        PublishFeedback();
        if (SelectedRecipe is not null) RefreshCraftPlan();
    }

    private void PublishFeedback()
    {
        FeedbackState = _action.State;
        FeedbackText = _action.State == ActionState.None ? "" : Loc.Fmt("L.MapInsp.Fb." + _action.State, _action.Label);
        OnPropertyChanged(nameof(HasFeedback));
    }

    [RelayCommand] public void DismissFeedback() { _action.Clear(); PublishFeedback(); }

    /// <summary>Accès rapide au personnage contrôlé.</summary>
    [RelayCommand]
    public void SelectSelf()
    {
        if (!_m.SelectSelf()) { NoteAction(Loc.T("L.MapInsp.SelfUnknown"), -1, false); return; }
        Publish();
    }

    // ---- favoris (lieux : carte + cellule) ----
    public bool CanFavorite => Selected is { Kind: MapEntityKind.Npc or MapEntityKind.Resource };
    public bool IsFavorite => Selected is { } e && MapFavorites.IsFavorite(AppConfig.Current.MapFavorites, MapIdProvider?.Invoke() ?? 0, e.CellId);
    public IReadOnlyList<MapFavorite> FavoritesHere
    {
        get { long map = MapIdProvider?.Invoke() ?? 0; return AppConfig.Current.MapFavorites.Where(f => f.MapId == map).ToList(); }
    }
    public bool HasFavoritesHere => FavoritesHere.Count > 0;

    [RelayCommand]
    public void ToggleFavorite()
    {
        if (Selected is not { } e || !CanFavorite) return;
        AppConfig.Current.MapFavorites = MapFavorites.Toggle(AppConfig.Current.MapFavorites, MapIdProvider?.Invoke() ?? 0, e.CellId, Title,
            e.Kind == MapEntityKind.Npc ? "npc" : "interactive");
        AppConfig.Save();
        OnPropertyChanged(nameof(IsFavorite)); OnPropertyChanged(nameof(FavoritesHere)); OnPropertyChanged(nameof(HasFavoritesHere));
    }

    [RelayCommand]
    public void GoFavorite(MapFavorite? f)
    {
        if (f is null) return;
        if (!InspectCell(f.CellId)) NoteAction(Loc.Fmt("L.MapInsp.FavGone", f.Label), -1, false);   // l'entité n'est plus (encore) sur la carte
    }

    // ---- craft : un atelier ouvre sa vue de recettes directement dans la carte ----
    public Func<int, IReadOnlyList<RecipeInfo>>? RecipesOfSkill { get; set; }
    public Func<long, RecipeInfo?>? RecipeOf { get; set; }
    public Func<long, int>? InventoryCount { get; set; }
    public Func<MapInteractive, InteractiveSkill, IReadOnlyList<(long Gid, int Qty)>, int, System.Threading.Tasks.Task<(bool Ok, int Qty)>>? CraftHandler { get; set; }
    public Func<int, bool>? AdjacentProvider { get; set; }

    [ObservableProperty] public partial string CraftSearch { get; set; } = "";
    [ObservableProperty] public partial CraftRecipeRow? SelectedRecipe { get; set; }
    [ObservableProperty] public partial int CraftCount { get; set; } = 1;
    [ObservableProperty] public partial bool CraftPending { get; set; }
    [ObservableProperty] public partial bool ShowBaseIngredients { get; set; }
    [ObservableProperty] public partial string CraftNotice { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<CraftRecipeRow> CraftRecipes { get; set; } = Array.Empty<CraftRecipeRow>();
    [ObservableProperty] public partial IReadOnlyList<CraftLineRow> CraftLines { get; set; } = Array.Empty<CraftLineRow>();
    [ObservableProperty] public partial int CraftMax { get; set; }

    private InteractiveSkill? StationSkill(MapInteractive it) =>
        RecipesOfSkill is null ? null : it.Skills.FirstOrDefault(sk => RecipesOfSkill(sk.SkillId).Count > 0);

    public bool IsCraftStation => Selected?.Interactive is { } it && StationSkill(it) is not null;
    public bool CraftAdjacent => Selected is { } e && AdjacentProvider?.Invoke(e.CellId) == true;
    public bool CraftNeedsJoin => IsCraftStation && !CraftAdjacent;
    public bool HasCraftRecipe => SelectedRecipe is not null;
    public string CraftPreview => SelectedRecipe is null ? "" : Loc.Fmt("L.MapInsp.CraftPreview", CraftCount, SelectedRecipe.Name,
        string.Join(", ", CraftLines.Select(l => $"{l.Need * CraftCount}× {l.Name}")));

    /// <summary>« Utiliser » un atelier depuis la carte : ouvre sa vue de recettes (au lieu d'ouvrir l'interface du jeu). Renvoie false si ce n'est pas un atelier.</summary>
    public bool TryOpenStation(MapInteractive it)
    {
        if (StationSkill(it) is null)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[atelier] « {it.Name} » (type {it.ElementTypeId}) : compétences [{string.Join(",", it.Skills.Select(k => k.SkillId))}] sans recette connue -> utilisation directe.");
            return false;
        }
        var e = _m.All.FirstOrDefault(x => x.Key == MapInspection.InteractiveKey(it.ElementId));
        if (e is null) return false;
        _m.Candidates = Array.Empty<MapEntity>();
        _m.Select(e);
        Publish();
        return true;
    }

    partial void OnCraftSearchChanged(string value) => RefreshCraftList();
    partial void OnSelectedRecipeChanged(CraftRecipeRow? value) { CraftCount = 1; CraftPending = false; CraftNotice = ""; RefreshCraftPlan(); OnPropertyChanged(nameof(HasCraftRecipe)); }
    partial void OnCraftCountChanged(int value) { CraftPending = false; OnPropertyChanged(nameof(CraftPreview)); }
    partial void OnShowBaseIngredientsChanged(bool value) => RefreshCraftPlan();

    private void RefreshCraftList()
    {
        if (Selected?.Interactive is not { } it || StationSkill(it) is not { } sk || RecipesOfSkill is null) { CraftRecipes = Array.Empty<CraftRecipeRow>(); return; }
        string q = CraftSearch.Trim();
        var rows = RecipesOfSkill(sk.SkillId)
            .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Select(r => new CraftRecipeRow(r, StationCraftPlan.Compute(r.Ingredients.Select(i => (i.Gid, i.Name, i.Quantity)).ToList(), g => InventoryCount?.Invoke(g) ?? 0).MaxCraftable > 0))
            .OrderByDescending(r => r.CanCraftNow).ThenByDescending(r => r.Info.Level).Take(60).ToList();   // réalisables d'abord ; liste bornée (la recherche affine)
        CraftRecipes = rows;
    }

    private void RefreshCraftPlan()
    {
        if (SelectedRecipe is null) { CraftLines = Array.Empty<CraftLineRow>(); CraftMax = 0; return; }
        var ing = SelectedRecipe.Info.Ingredients.Select(i => (i.Gid, i.Name, i.Quantity)).ToList();
        IReadOnlyList<(long Gid, string Name, int Qty)> shown = ing;
        if (ShowBaseIngredients)
            shown = CraftDecomposition.Flatten(ing, gid => RecipeOf?.Invoke(gid) is { Ingredients.Count: > 0 } r ? r.Ingredients.Select(i => (i.Gid, i.Name, i.Quantity)).ToList() : null);
        var plan = StationCraftPlan.Compute(shown, g => InventoryCount?.Invoke(g) ?? 0);
        CraftLines = plan.Lines.Select(l => new CraftLineRow(l.Name, l.PerCraft, l.Have)).ToList();
        CraftMax = plan.MaxCraftable;
        OnPropertyChanged(nameof(CraftPreview));
    }

    [RelayCommand] public void SelectRecipe(CraftRecipeRow? r) => SelectedRecipe = r;
    [RelayCommand] public void CraftMore() { if (CraftCount < Math.Max(1, CraftMax)) CraftCount++; }
    [RelayCommand] public void CraftLess() { if (CraftCount > 1) CraftCount--; }
    [RelayCommand] public void BeginCraft()
    {
        CraftNotice = "";
        if (SelectedRecipe is null) return;
        if (!CraftAdjacent) { CraftNotice = Loc.T("L.MapInsp.CraftJoinFirst"); return; }
        if (!new StationCraftPlan(CraftLines.Select(l => new CraftIngredientLine(0, l.Name, l.Need, l.Have)).ToList(), CraftMax).CanCraft(CraftCount) || ShowBaseIngredients)
        { CraftNotice = Loc.T(ShowBaseIngredients ? "L.MapInsp.CraftNeedDirect" : "L.MapInsp.CraftMissing"); return; }
        CraftPending = true;
    }
    [RelayCommand] public void CancelCraft() { CraftPending = false; CraftNotice = ""; }

    /// <summary>Confirmation EXPLICITE : consomme des objets. Passe par le chemin de craft existant du moteur (droits, retries, vérifications).</summary>
    [RelayCommand]
    public async System.Threading.Tasks.Task ConfirmCraft()
    {
        if (!CraftPending || SelectedRecipe is null || Selected?.Interactive is not { } it || StationSkill(it) is not { } sk || CraftHandler is null)
        { CraftPending = false; CraftNotice = Loc.T("L.MapInsp.CraftUnavailable"); return; }
        var recipe = SelectedRecipe.Info.Ingredients.Select(i => (i.Gid, i.Quantity)).ToList();
        int count = CraftCount;
        CraftPending = false;
        NoteAction(Loc.Fmt("L.MapInsp.CraftAction", count, SelectedRecipe.Name), -1, true, () => false);
        (bool Ok, int Qty) res;
        try { res = await CraftHandler(it, sk, recipe, count).ConfigureAwait(true); } catch { res = (false, 0); }
        if (res.Ok) { _action.Confirm(); CraftNotice = Loc.Fmt("L.MapInsp.CraftDone", res.Qty); }
        else { _action.Refuse(Loc.T("L.MapInsp.CraftFailed")); CraftNotice = Loc.T("L.MapInsp.CraftFailed"); }
        PublishFeedback(); RefreshCraftPlan(); RefreshCraftList();
    }

    private DateTime? _dataAt;
    private readonly Dictionary<int, IReadOnlyDictionary<int, long>?> _xpCache = new();

    /// <summary>XP de base par niveau de grade d'un monstre (base du jeu locale) ; null = base absente. Renseigné par le bot.</summary>
    public Func<int, IReadOnlyDictionary<int, long>?>? XpLookup { get; set; }
    /// <summary>Envoi RÉEL de l'attaque (garde-fous du moteur : fenêtre, position, chemin) ; renseigné par le bot. Jamais appelé sans confirmation.</summary>
    public Func<MapActor, bool>? AttackHandler { get; set; }

    // ---- PNJ / services / personnages (lecture des événements reçus ; envois via les chemins existants du moteur) ----
    public Func<int, IReadOnlyList<(int Id, string Label)>?>? NpcActionsLookup { get; set; }
    /// <summary>Texte d'une réponse de dialogue (id de PNJ, id de réponse) depuis les données du jeu ; null = inconnu.</summary>
    public Func<int, int, string?>? ReplyTextLookup { get; set; }
    public Func<int, string?>? NpcNameLookup { get; set; }
    public Func<int, string?>? InteractiveTypeNameLookup { get; set; }
    /// <summary>La base de données du jeu locale est-elle chargée ? Sinon l'absence d'actions s'explique par la base manquante.</summary>
    public Func<bool>? GameDataAvailable { get; set; }
    public bool GameDataMissing => GameDataAvailable?.Invoke() == false;
    public Func<int, bool>? JoinHandler { get; set; }
    public Func<MapActor, int, System.Threading.Tasks.Task<bool>>? NpcActionHandler { get; set; }
    public Func<int, bool>? ReplyHandler { get; set; }
    public Func<bool>? LeaveHandler { get; set; }
    /// <summary>« craft » / « hdv » / « dialogue » si une interaction est ouverte côté serveur, sinon null.</summary>
    public Func<string?>? OpenInteractionProbe { get; set; }
    [ObservableProperty] public partial string? OpenInteraction { get; set; }
    public bool HasOpenInteraction => OpenInteraction is not null;
    public string OpenInteractionText => OpenInteraction is null ? "" : Loc.T("L.MapInsp.Open." + OpenInteraction);
    partial void OnOpenInteractionChanged(string? value) { OnPropertyChanged(nameof(HasOpenInteraction)); OnPropertyChanged(nameof(OpenInteractionText)); }

    /// <summary>Ferme l'interaction ouverte (atelier, dialogue, HDV) : sans cela le personnage reste bloqué dedans. Renvoie vrai si un envoi a eu lieu.</summary>
    public bool CloseOpenInteraction()
    {
        if (OpenInteractionProbe?.Invoke() is null) return false;
        bool ok = LeaveHandler?.Invoke() == true;
        NoteAction(Loc.T("L.MapInsp.CloseInteraction"), -1, ok, () => OpenInteractionProbe?.Invoke() is null);
        return ok;
    }
    [RelayCommand] public void LeaveInteraction() => CloseOpenInteraction();
    public Func<(int MessageId, IReadOnlyList<string> Params, IReadOnlyList<int> ReplyIds)?>? DialogProbe { get; set; }
    public Func<int, IReadOnlyList<string>, string?>? DialogText { get; set; }
    /// <summary>Lignes réellement connues du personnage contrôlé (PV, PA/PM, pods, kamas, métiers) ; vides tant qu'inconnues.</summary>
    public Func<IReadOnlyList<InspectorLine>>? SelfInfo { get; set; }
    /// <summary>Le service demandé est-il confirmé ouvert par le jeu ? (HDV, boutique...) ; null = pas de sonde pour ce service.</summary>
    public Func<bool>? ServiceOpenProbe { get; set; }

    private readonly NpcDialogLog _dialogLog = new();
    private string _dialogSig = "";
    private string? _phaseKey;
    private string? _craftKey;

    [ObservableProperty] public partial ServicePhase Phase { get; set; }
    [ObservableProperty] public partial string DialogQuestion { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<DialogReplyOption> DialogReplies { get; set; } = Array.Empty<DialogReplyOption>();
    [ObservableProperty] public partial IReadOnlyList<DialogEntry> DialogHistory { get; set; } = Array.Empty<DialogEntry>();
    [ObservableProperty] public partial int InfoTab { get; set; }   // 0 = Général, 1 = Informations (Équipement : non disponible)

    public bool HasDialog => DialogQuestion.Length > 0 || DialogReplies.Count > 0;
    public bool HasDialogHistory => DialogHistory.Count > 0;
    public bool IsNpcSelected => Selected is { Kind: MapEntityKind.Npc };
    public bool IsCharacterSelected => Selected is { Kind: MapEntityKind.Self or MapEntityKind.Player };
    public bool IsControlled => Selected is { Kind: MapEntityKind.Self };
    public bool IsOtherPlayer => Selected is { Kind: MapEntityKind.Player };
    public bool IsGeneralTab => InfoTab == 0;
    public bool IsInfoTab => InfoTab == 1;
    public string CharacterRole => Loc.T(IsControlled ? "L.MapInsp.RoleSelf" : "L.MapInsp.RoleOther");
    public string NoSocialText => Loc.T(IsOtherPlayer && TradeHandler is not null ? "L.MapInsp.SocialTrade" : "L.MapInsp.NoSocial");

    /// <summary>Propose un échange au joueur sélectionné (id d'acteur) ; la fenêtre d'interaction s'ouvre s'il accepte.</summary>
    public Func<long, bool>? TradeHandler { get; set; }
    public bool CanTrade => IsOtherPlayer && TradeHandler is not null;

    [RelayCommand]
    public void RequestTrade()
    {
        if (Selected is not { Kind: MapEntityKind.Player, Actor: { } a } || TradeHandler is null) return;
        NoteAction(Loc.Fmt("L.MapInsp.TradeAction", Title), -1, TradeHandler(a.ActorId), () => OpenInteractionProbe?.Invoke() == "trade");
    }
    public string PhaseText => Phase == ServicePhase.None ? "" : Loc.T("L.MapInsp.Phase." + Phase);
    public bool HasPhase => Phase != ServicePhase.None;

    public IReadOnlyList<NpcActionOption> NpcActions
    {
        get
        {
            if (Selected is not { Kind: MapEntityKind.Npc, Actor: { } a } || a.NpcId <= 0) return Array.Empty<NpcActionOption>();
            var list = NpcActionsLookup?.Invoke(a.NpcId);
            if (list is null) return Array.Empty<NpcActionOption>();   // actions inconnues : rien n'est proposé
            return list.Select(x => new NpcActionOption(x.Id, string.IsNullOrWhiteSpace(x.Label) ? Loc.Fmt("L.MapInsp.ActionNo", x.Id) : x.Label)).ToList();
        }
    }
    public bool HasNpcActions => NpcActions.Count > 0;
    public bool ShowNoActions => IsNpcSelected && !HasNpcActions && !GameDataMissing;
    public bool ShowGameDataMissing => IsNpcSelected && GameDataMissing;

    /// <summary>Informations du personnage SÉLECTIONNÉ : le contrôlé expose ce que le bot sait réellement ; un autre joueur seulement nom / cellule / id.</summary>
    public IReadOnlyList<InspectorLine> CharacterInfo
    {
        get
        {
            if (Selected is not { } e || !IsCharacterSelected) return Array.Empty<InspectorLine>();
            var l = new List<InspectorLine> { new(Loc.T("L.MapInsp.Cell"), e.CellId.ToString()) };
            if (e.Actor is { } a) l.Add(new(Loc.T("L.MapInsp.Id"), a.ActorId.ToString()));
            if (CharacterInspection.CanShowStats(e) && SelfInfo?.Invoke() is { } known) l.AddRange(known);
            return l;
        }
    }

    [ObservableProperty] public partial bool AttackPending { get; set; }
    [ObservableProperty] public partial string AttackNotice { get; set; } = "";
    public bool IsMonsterSelected => Selected is { Kind: MapEntityKind.Monster };
    public bool CanBeginAttack => IsMonsterSelected && !AttackPending;
    public string AttackPreview => Selected is { Actor: { } a } ? Loc.Fmt("L.MapInsp.AttackPreview", a.Name, Selected.CellId, a.Monsters?.Sum(m => m.Count) ?? 0) : "";

    /// <summary>Récolte / utilisation réelle d'une ressource (droits + marche + envoi du moteur) ; renseigné par le bot.</summary>
    public Func<MapInteractive, bool>? HarvestHandler { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ResourceGroupRow> ResourceGroups { get; set; } = Array.Empty<ResourceGroupRow>();
    public bool HasResourceGroups => ResourceGroups.Count > 0;
    public bool IsResourceSelected => Selected is { Kind: MapEntityKind.Resource };
    public string ResourceVerb => Selected?.Interactive is { } it ? (string.IsNullOrWhiteSpace(it.ActionName) ? Loc.T("L.MapInsp.UseGeneric") : it.ActionName!) : "";
    public bool ResourceHarvestable => Selected?.Interactive?.Harvestable == true;

    [RelayCommand]
    public void UseResource()
    {
        if (Selected?.Interactive is not { } it) return;
        if (TryOpenStation(it)) return;   // atelier : ses recettes, pas l'utilisation brute
        bool ok = HarvestHandler?.Invoke(it) == true;
        NoteAction(Loc.Fmt("L.MapInsp.UseAction", Title), -1, ok);
    }

    // ---- « tout récolter » : enchaîne les exemplaires disponibles d'un type, avec arrêts de sécurité ----
    public Func<bool>? PodsFullProbe { get; set; }
    private System.Threading.CancellationTokenSource? _harvestCts;
    [ObservableProperty] public partial bool HarvestAllRunning { get; set; }
    public int HarvestableLikeSelected => Selected is { Kind: MapEntityKind.Resource, Interactive: { } sel }
        ? _m.Shown.Count(e => e.Kind == MapEntityKind.Resource && e.Title == Selected.Title && (e.Interactive?.ActionName ?? "") == (sel.ActionName ?? "") && e.Interactive!.Harvestable) : 0;
    public string HarvestAllText => HarvestAllRunning ? Loc.T("L.MapInsp.HarvestAllStop") : Loc.Fmt("L.MapInsp.HarvestAll", ResourceVerb, HarvestableLikeSelected);
    public bool CanHarvestAll => IsResourceSelected && (HarvestAllRunning || HarvestableLikeSelected > 1) && StationOnly(Selected) == false;
    private bool StationOnly(MapEntity? e) => e?.Interactive is { } it && StationSkill(it) is not null;
    partial void OnHarvestAllRunningChanged(bool value) { OnPropertyChanged(nameof(HarvestAllText)); OnPropertyChanged(nameof(CanHarvestAll)); }

    [RelayCommand]
    public async System.Threading.Tasks.Task HarvestAll()
    {
        if (HarvestAllRunning) { _harvestCts?.Cancel(); return; }
        if (Selected is not { Kind: MapEntityKind.Resource, Interactive: { } sel } first || HarvestHandler is null) return;
        string title = first.Title, verb = sel.ActionName ?? "";
        int epoch0 = _m.Epoch;
        var cts = _harvestCts = new System.Threading.CancellationTokenSource();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        int total = HarvestableLikeSelected, done = 0;
        HarvestAllRunning = true;
        var stop = HarvestStop.Done;
        try
        {
            while (true)
            {
                stop = HarvestAllPlan.Check(cts.IsCancellationRequested, FightProbe?.Invoke() == true, PodsFullProbe?.Invoke() == true,
                                            _m.Epoch != epoch0, OpenInteractionProbe?.Invoke() is not null);
                if (stop != HarvestStop.None) break;
                var next = HarvestAllPlan.NextTarget(_m.Shown.Where(e => e.Kind == MapEntityKind.Resource && e.Title == title && (e.Interactive?.ActionName ?? "") == verb),
                                                     SelfCellProvider?.Invoke(), tried);
                if (next is null) { stop = HarvestStop.Done; break; }
                tried.Add(next.Key); done++;
                bool ok = HarvestHandler(next.Interactive!);
                NoteAction(Loc.Fmt("L.MapInsp.HarvestAllProgress", verb.Length > 0 ? verb : Loc.T("L.MapInsp.UseGeneric"), title, done, Math.Max(total, done)), -1, ok);
                if (!ok) continue;   // refusé (position inconnue, droit absent…) : exemplaire suivant
                // Preuve de fin : l'exemplaire n'est plus disponible (skills désactivés) ; sinon délai maximal avant de passer au suivant.
                long until = Environment.TickCount64 + 15000;
                while (!cts.IsCancellationRequested && Environment.TickCount64 < until && _m.Epoch == epoch0
                       && _m.Shown.Any(e => e.Key == next.Key && e.Interactive is { Harvestable: true }))
                    await System.Threading.Tasks.Task.Delay(300).ConfigureAwait(true);
            }
        }
        finally
        {
            HarvestAllRunning = false;
            _harvestCts = null;
            cts.Dispose();
        }
        NoteAction(Loc.Fmt("L.MapInsp.HarvestAllEnd", title, done, Loc.T("L.MapInsp.Stop." + stop)), -1, stop == HarvestStop.Done);
        if (stop == HarvestStop.Done) _action.Confirm();
        PublishFeedback();
    }

    [RelayCommand] public void SelectGroup(ResourceGroupRow? g) { if (g is not null) { _m.Candidates = Array.Empty<MapEntity>(); _m.Select(g.First); Publish(); } }

    [ObservableProperty] public partial bool ShowMonsters { get; set; } = true;
    [ObservableProperty] public partial bool ShowNpcs { get; set; } = true;
    [ObservableProperty] public partial bool ShowPlayers { get; set; } = true;
    [ObservableProperty] public partial bool ShowResources { get; set; } = true;
    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty] public partial IReadOnlyList<MapActor>? VisibleActors { get; set; }
    [ObservableProperty] public partial IReadOnlyList<MapInteractive>? VisibleInteractives { get; set; }
    [ObservableProperty] public partial MapEntity? Selected { get; set; }
    [ObservableProperty] public partial IReadOnlyList<MapEntity> Candidates { get; set; } = Array.Empty<MapEntity>();
    [ObservableProperty] public partial IReadOnlyList<MapEntity> Results { get; set; } = Array.Empty<MapEntity>();
    [ObservableProperty] public partial IReadOnlySet<int>? MatchCells { get; set; }
    [ObservableProperty] public partial int? SelectedCell { get; set; }
    [ObservableProperty] public partial bool Searching { get; set; }
    [ObservableProperty] public partial string FreshnessText { get; set; } = "";
    [ObservableProperty] public partial int MonsterCount { get; set; }
    [ObservableProperty] public partial int NpcCount { get; set; }
    [ObservableProperty] public partial int PlayerCount { get; set; }
    [ObservableProperty] public partial int ResourceCount { get; set; }

    public bool HasSelection => Selected is not null;
    public bool HasNoSelection => Selected is null;
    public bool HasCandidates => Candidates.Count > 1;
    public string OverlapText => Loc.Fmt("L.MapInsp.Overlap", Candidates.Count);
    public string ResultsText => Loc.Fmt("L.MapInsp.Results", Results.Count);
    public string Title
    {
        get
        {
            if (Selected is not { } e) return "";
            // Un PNJ sans nom résolu côté carte (« PNJ 4356 ») prend le nom des données du jeu quand il y est.
            if (e.Kind == MapEntityKind.Npc && e.Actor is { NpcId: > 0 } a && (e.Title.StartsWith("PNJ ", StringComparison.Ordinal) || e.Title == "?")
                && NpcNameLookup?.Invoke(a.NpcId) is { Length: > 0 } n) return n;
            if (e.Kind == MapEntityKind.Resource && e.Interactive is { } it && e.Title.StartsWith("Interactif (type", StringComparison.Ordinal)
                && InteractiveTypeNameLookup?.Invoke(it.ElementTypeId) is { Length: > 0 } tn) return tn;
            return e.Title;
        }
    }
    public string KindText => Selected is null ? "" : (Selected.Interactive is { ActionName: { Length: > 0 } verb } ? verb : Loc.T("L.MapInsp.Kind." + Selected.Kind));

    public Bitmap? Icon => Selected switch
    {
        { Actor: { Monsters: { Count: > 0 } ms } } => Stores.MonsterIcons.Get(ms[0].GenericId),
        { Interactive: { } it } => it.Skills.Select(s => Stores.InteractiveIcons.Get(s.SkillId)).FirstOrDefault(b => b is not null),
        _ => null,
    };

    private IReadOnlyDictionary<int, long>? GradeXp(int genericId)
    {
        if (_xpCache.TryGetValue(genericId, out var hit)) return hit;
        IReadOnlyDictionary<int, long>? v = null;
        try { v = XpLookup?.Invoke(genericId); } catch { /* base absente : XP inconnue */ }
        if (v is not null || XpLookup is not null) _xpCache[genericId] = v;
        return v;
    }

    public bool HasMembers => Members.Count > 0;

    public IReadOnlyList<InspectorMember> Members
    {
        get
        {
            if (Selected?.Actor is not { Monsters: { Count: > 0 } ms } a) return Array.Empty<InspectorMember>();
            return ms.Select(m =>
            {
                var lv = a.Members?.Where(x => x.GenericId == m.GenericId && x.Level > 0).Select(x => x.Level).ToList();
                string level = lv is { Count: > 0 } ? (lv.Min() == lv.Max() ? lv.Max().ToString() : $"{lv.Min()}–{lv.Max()}") : "";
                return new InspectorMember(Stores.MonsterIcons.Get(m.GenericId), m.Name, m.Count, level);
            }).ToList();
        }
    }

    public IReadOnlyList<InspectorLine> Lines
    {
        get
        {
            if (Selected is not { } e) return Array.Empty<InspectorLine>();
            var l = new List<InspectorLine> { new(Loc.T("L.MapInsp.Cell"), e.CellId.ToString()) };
            string lv = MapInspection.LevelText(e);
            if (lv.Length > 0) l.Add(new(Loc.T("L.MapInsp.Level"), lv));
            if (e.Actor is { } a)
            {
                if (e.Kind == MapEntityKind.Monster && a.Monsters is { } ms) l.Add(new(Loc.T("L.MapInsp.Group"), ms.Sum(m => m.Count).ToString()));
                if (a.GroupLevel > 0) l.Add(new(Loc.T("L.MapInsp.TotalLevel"), a.GroupLevel.ToString()));
                if (e.Kind == MapEntityKind.Monster && GroupXp.Compute(a, GradeXp) is { Any: true } gx)
                    l.Add(new(Loc.T("L.MapInsp.BaseXp"), gx.Complete ? gx.Known.ToString("N0") : Loc.Fmt("L.MapInsp.BaseXpPartial", gx.Known.ToString("N0"), gx.KnownMembers, gx.TotalMembers)));
                if (a.NpcId != 0) l.Add(new(Loc.T("L.MapInsp.NpcModel"), a.NpcId.ToString()));
                l.Add(new(Loc.T("L.MapInsp.Id"), a.ActorId.ToString()));
            }
            else if (e.Interactive is { } it)
            {
                if (!string.IsNullOrEmpty(it.ActionName)) l.Add(new(Loc.T("L.MapInsp.Action"), it.ActionName!));
                l.Add(new(Loc.T("L.MapInsp.State"), Loc.T(it.Harvestable ? "L.MapInsp.Available" : "L.MapInsp.Depleted")));
                l.Add(new(Loc.T("L.MapInsp.Id"), it.ElementId.ToString()));
            }
            return l;
        }
    }

    /// <summary>Met à jour les sources ; <paramref name="epoch"/> = MapEpoch du bot (un changement de carte invalide la sélection).</summary>
    public void Update(IReadOnlyList<MapActor>? actors, IReadOnlyList<MapInteractive>? interactives, string? selfName, int epoch)
    {
        _m.Update(actors, interactives, selfName, epoch);
        if (actors is not null || interactives is not null) _dataAt = DateTime.UtcNow;
        Publish();
    }

    public void RefreshFreshness() =>
        FreshnessText = _dataAt is null ? Loc.T("L.MapInsp.NoData") : Loc.Fmt("L.MapInsp.Fresh", MapInspection.Freshness(_dataAt, DateTime.UtcNow));

    partial void OnShowMonstersChanged(bool value) { Refilter(); SavePrefs(); }
    partial void OnShowNpcsChanged(bool value) { Refilter(); SavePrefs(); }
    partial void OnShowPlayersChanged(bool value) { Refilter(); SavePrefs(); }
    partial void OnShowResourcesChanged(bool value) { Refilter(); SavePrefs(); }
    partial void OnSearchTextChanged(string value) => Refilter();

    private void Refilter()
    {
        _m.Filter = (ShowMonsters ? MapEntityFilter.Monsters : 0) | (ShowNpcs ? MapEntityFilter.Npcs : 0)
                    | (ShowPlayers ? MapEntityFilter.Players : 0) | (ShowResources ? MapEntityFilter.Resources : 0);
        _m.Search = SearchText;
        _m.Rebuild();
        Publish();
    }

    private void Publish()
    {
        VisibleActors = _m.VisibleActors;
        VisibleInteractives = _m.VisibleInteractives;
        MonsterCount = _m.All.Count(e => e.Kind == MapEntityKind.Monster);
        NpcCount = _m.All.Count(e => e.Kind == MapEntityKind.Npc);
        PlayerCount = _m.All.Count(e => e.Kind is MapEntityKind.Player or MapEntityKind.Self);
        ResourceCount = _m.All.Count(e => e.Kind == MapEntityKind.Resource);
        ResourceGroups = _m.All.Where(e => e.Kind == MapEntityKind.Resource && e.Interactive is not null)
            .GroupBy(e => (e.Title, Verb: e.Interactive!.ActionName ?? ""))
            .Select(g => new ResourceGroupRow(
                g.SelectMany(e => e.Interactive!.Skills).Select(sk => Stores.InteractiveIcons.Get(sk.SkillId)).FirstOrDefault(b => b is not null),
                g.Key.Title, g.Key.Verb, g.Count(e => e.Interactive!.Harvestable), g.Count(), g.OrderByDescending(e => e.Interactive!.Harvestable).First()))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        OnPropertyChanged(nameof(HasResourceGroups));
        Searching = _m.Searching;
        Results = _m.Results;
        MatchCells = _m.Searching ? _m.Results.Select(e => e.CellId).ToHashSet() : null;
        Candidates = _m.Candidates;
        Selected = _m.Selected;
        SelectedCell = _m.Selected?.CellId;
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasNoSelection)); OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(OverlapText)); OnPropertyChanged(nameof(ResultsText)); OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindText)); OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(Members));
        OnPropertyChanged(nameof(HasMembers)); OnPropertyChanged(nameof(Lines));
        OnPropertyChanged(nameof(CanFavorite)); OnPropertyChanged(nameof(IsFavorite)); OnPropertyChanged(nameof(FavoritesHere)); OnPropertyChanged(nameof(HasFavoritesHere));
        OnPropertyChanged(nameof(IsCraftStation)); OnPropertyChanged(nameof(CraftAdjacent)); OnPropertyChanged(nameof(CraftNeedsJoin));
        if (Selected?.Key != _craftKey) { _craftKey = Selected?.Key; SelectedRecipe = null; CraftSearch = ""; RefreshCraftList(); }
        OnPropertyChanged(nameof(IsResourceSelected)); OnPropertyChanged(nameof(ResourceVerb)); OnPropertyChanged(nameof(ResourceHarvestable));
        OnPropertyChanged(nameof(HarvestAllText)); OnPropertyChanged(nameof(CanHarvestAll));
        OnPropertyChanged(nameof(IsNpcSelected)); OnPropertyChanged(nameof(NpcActions)); OnPropertyChanged(nameof(HasNpcActions));
        OnPropertyChanged(nameof(ShowNoActions)); OnPropertyChanged(nameof(ShowGameDataMissing));
        OnPropertyChanged(nameof(IsCharacterSelected)); OnPropertyChanged(nameof(IsControlled)); OnPropertyChanged(nameof(IsOtherPlayer)); OnPropertyChanged(nameof(CanTrade));
        OnPropertyChanged(nameof(CharacterRole)); OnPropertyChanged(nameof(CharacterInfo)); OnPropertyChanged(nameof(NoSocialText));
        if (Selected?.Key != _phaseKey) { _phaseKey = Selected?.Key; Phase = ServicePhase.None; InfoTab = 0; }   // l'état d'une action ne suit pas un autre PNJ
        AttackPending = _m.Attack.Pending;
        if (_m.AttackExpired) { AttackNotice = Loc.T("L.MapInsp.AttackExpired"); _m.AttackExpired = false; }
        OnPropertyChanged(nameof(IsMonsterSelected)); OnPropertyChanged(nameof(CanBeginAttack)); OnPropertyChanged(nameof(AttackPreview));
        RefreshFreshness();
    }

    /// <summary>Clic sur une cellule : true si une entité visible y a été inspectée ; false = cellule vide (le contrôle déplace le perso).</summary>
    public bool InspectCell(int cell) { bool r = _m.InspectCell(cell); Publish(); return r; }

    partial void OnPhaseChanged(ServicePhase value) { OnPropertyChanged(nameof(PhaseText)); OnPropertyChanged(nameof(HasPhase)); }
    partial void OnInfoTabChanged(int value) { OnPropertyChanged(nameof(IsGeneralTab)); OnPropertyChanged(nameof(IsInfoTab)); }
    partial void OnDialogQuestionChanged(string value) => OnPropertyChanged(nameof(HasDialog));
    partial void OnDialogRepliesChanged(IReadOnlyList<DialogReplyOption> value) => OnPropertyChanged(nameof(HasDialog));
    partial void OnDialogHistoryChanged(IReadOnlyList<DialogEntry> value) => OnPropertyChanged(nameof(HasDialogHistory));

    [RelayCommand] public void SelectInfoTab(string? tab) => InfoTab = tab == "1" ? 1 : 0;

    /// <summary>Lit le dialogue PNJ courant (événements reçus) et alimente la fiche + l'historique court. Appelée à chaque cycle de rafraîchissement.</summary>
    public void PollDialog()
    {
        var d = DialogProbe?.Invoke();
        if (d is null)
        {
            if (_dialogSig.Length > 0) { _dialogSig = ""; DialogQuestion = ""; DialogReplies = Array.Empty<DialogReplyOption>(); }
            return;
        }
        string sig = d.Value.MessageId + "|" + string.Join(",", d.Value.Params) + "|" + string.Join(",", d.Value.ReplyIds);
        if (sig == _dialogSig) return;
        _dialogSig = sig;
        string text = DialogText?.Invoke(d.Value.MessageId, d.Value.Params) ?? Loc.Fmt("L.MapInsp.MessageNo", d.Value.MessageId);
        _dialogLog.Add(DialogSpeaker.Npc, text);
        DialogQuestion = text;
        int npcId = Selected?.Actor?.NpcId ?? 0;
        DialogReplies = d.Value.ReplyIds.Select(id => new DialogReplyOption(id, ReplyTextLookup?.Invoke(npcId, id) is { Length: > 0 } t ? t : Loc.Fmt("L.MapInsp.ReplyNo", id))).ToList();
        DialogHistory = _dialogLog.Items.ToList();
    }

    /// <summary>Étape 1 : rejoindre le bâtiment / le PNJ (marche jusqu'à une case adjacente). N'ouvre rien.</summary>
    [RelayCommand]
    public void Join()
    {
        if (Selected is not { } e) return;
        bool ok = JoinHandler?.Invoke(e.CellId) == true;
        Phase = ok ? ServicePhase.Joining : ServicePhase.Refused;
        NoteAction(Loc.Fmt("L.MapInsp.JoinAction", Title), -1, ok, () => AdjacentProvider?.Invoke(e.CellId) == true);
    }

    /// <summary>Étape 2 : demander l'ouverture (action déclarée par le jeu). L'ouverture n'est CONFIRMÉE que par la réponse du jeu.</summary>
    [RelayCommand]
    public async System.Threading.Tasks.Task RunNpcAction(NpcActionOption? option)
    {
        if (option is null || Selected?.Actor is not { Kind: MapActorKind.Npc or MapActorKind.TreasureHint or MapActorKind.Unknown } actor || NpcActionHandler is null)
        { Phase = ServicePhase.Refused; return; }
        Phase = ServicePhase.Requested;
        _dialogLog.Add(DialogSpeaker.You, option.Label);
        DialogHistory = _dialogLog.Items.ToList();
        bool answered;
        try { answered = await NpcActionHandler(actor, option.Id).ConfigureAwait(true); }
        catch { answered = false; }
        PollDialog();
        Phase = !answered ? ServicePhase.NoAnswer
              : (ServiceOpenProbe is { } probe && !probe() && !HasDialog ? ServicePhase.NoAnswer : ServicePhase.Opened);
    }

    [RelayCommand]
    public void Reply(DialogReplyOption? reply)
    {
        if (reply is null || ReplyHandler is null) return;
        _dialogLog.Add(DialogSpeaker.You, reply.Label);
        Phase = ReplyHandler(reply.Id) ? ServicePhase.Requested : ServicePhase.Refused;
        DialogHistory = _dialogLog.Items.ToList();
    }

    [RelayCommand]
    public void LeaveDialog()
    {
        if (LeaveHandler?.Invoke() == true) { _dialogLog.Add(DialogSpeaker.System, Loc.T("L.MapInsp.DialogClosed")); DialogQuestion = ""; DialogReplies = Array.Empty<DialogReplyOption>(); _dialogSig = ""; Phase = ServicePhase.None; }
        else Phase = ServicePhase.Refused;
        DialogHistory = _dialogLog.Items.ToList();
    }

    public Func<bool>? FightProbe { get; set; }

    /// <summary>« Attaquer » : l'unique action contre un groupe de monstres (même chemin que le menu clic droit : droits, marche, AttackMonsterRequest).</summary>
    [RelayCommand]
    public void AttackNow()
    {
        if (Selected is not { Kind: MapEntityKind.Monster, Actor: { } a } e) return;
        bool ok = AttackHandler?.Invoke(a) == true;
        NoteAction(Loc.Fmt("L.MapInsp.AttackAction", Title), -1, ok, FightProbe);   // « confirmé » seulement quand le combat démarre réellement
    }

    /// <summary>1er temps : fige la cible et affiche l'aperçu. N'envoie RIEN.</summary>
    [RelayCommand] public void BeginAttack() { AttackNotice = ""; if (_m.Selected is { } e) _m.Attack.Begin(e); Publish(); }
    [RelayCommand] public void CancelAttack() { _m.Attack.Cancel(); AttackNotice = ""; Publish(); }
    /// <summary>2e temps : confirmation explicite. Refusée si la cible a changé depuis l'aperçu ; l'envoi passe par les garde-fous du moteur.</summary>
    [RelayCommand]
    public void ConfirmAttack()
    {
        var target = _m.Attack.Confirm(_m.Selected);
        if (target is null) AttackNotice = Loc.T("L.MapInsp.AttackExpired");
        else if (AttackHandler is null) AttackNotice = Loc.T("L.MapInsp.AttackUnavailable");
        else AttackNotice = Loc.T(AttackHandler(target) ? "L.MapInsp.AttackSent" : "L.MapInsp.AttackRefused");
        Publish();
    }

    [RelayCommand] public void Select(MapEntity? e) { _m.Select(e); Publish(); }
    // Échap / désélection : ne referme que ce que la fiche a ouvert elle-même (dialogue, atelier, HDV). Banque, échange, boutique et
    // zaap ont leur propre fenêtre (bouton Fermer) et peuvent appartenir à un script : un Échap sur la carte ne doit pas les couper.
    [RelayCommand]
    public void ClearSelection()
    {
        if (OpenInteractionProbe?.Invoke() is "dialogue" or "craft" or "hdv") CloseOpenInteraction();
        _m.Clear(); Publish();
    }
    public void Cycle(int dir) { _m.Cycle(dir); Publish(); }
}
