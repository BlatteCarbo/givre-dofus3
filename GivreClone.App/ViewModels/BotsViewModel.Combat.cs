using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Onglet Combat : profil du bot sélectionné, rotation de sorts, options, simulateur de tour =====================
public partial class BotsViewModel
{
    /// <summary>Relais du clic-carte (IsoMapControl.MoveRequested) vers le bot selectionne. Le pathfinding part
    /// TOUJOURS de la cellule REELLE du perso (lue dans les acteurs au moment du clic) ; la position se recale via
    /// le jsj du serveur apres chaque deplacement confirme (pas de prediction cote client -> pas de glitch).</summary>
    // ======================= ONGLET COMBAT (profil du bot selectionne) =======================
    // Profil de combat PAR PERSONNAGE (CombatProfileStore). On edite celui du bot selectionne ; chaque edition sauvegarde
    // le fichier ET repousse le profil au moteur (LaunchService) pour le pid du bot -> l'auto-combat suit en direct.
    private CombatProfile _profile = new();
    private BotRowViewModel? _profileBot;
    // Nom du perso dont _profile est le profil enregistré ; null = perso pas encore identifié -> profil TRANSITOIRE (jamais
    // enregistré, éditeur grisé). Avant : clé partagée « ? », dont les réglages disparaissaient à l'identification.
    private string? _profileKey;
    /// <summary>Profil de combat du perso affiché chargé (perso identifié) -> onglet Combat éditable.</summary>
    public bool CombatProfileReady => _profileKey is not null;
    /// <summary>Bot affiché mais perso pas encore identifié -> bandeau « en cours d'identification », éditeur grisé.</summary>
    public bool CombatProfilePending => _profileBot is not null && _profileKey is null;
    // Profil ENREGISTRÉ d'un perso identifié ; perso inconnu -> profil par défaut transitoire (pas d'entrée « ? » créée).
    private static CombatProfile ProfileFor(string? name) => string.IsNullOrWhiteSpace(name) ? new CombatProfile() : CombatProfileStore.Get(name);
    private readonly Dictionary<int, string?> _pushedName = new();   // pid -> nom pousse (re-push quand le nom se resout)

    public ObservableCollection<SpellRowViewModel> CombatSpells { get; } = new();
    public bool HasCombatSpells => CombatSpells.Count > 0;   // -> état vide « ajoute des sorts » quand la rotation est vide
    public Array CombatStyles { get; } = Enum.GetValues<CombatStyle>();
    public Array TargetModes { get; } = Enum.GetValues<TargetMode>();
    public Array CombatSpeeds { get; } = Enum.GetValues<CombatSpeed>();
    public Array LevelStatTargets { get; } = Enum.GetValues<LevelStatTarget>();

    /// <summary>Un sort de la CLASSE du perso, proposé dans le picker (auto-détecté depuis les bundles du jeu).</summary>
    public sealed record SpellCatalogItem(int Id, string Name, Avalonia.Media.Imaging.Bitmap? Icon) { public override string ToString() => Name; }

    /// <summary>Sorts de la classe NON encore dans la rotation (source du menu « + Ajouter un sort »).</summary>
    public ObservableCollection<SpellCatalogItem> AvailableSpells { get; } = new();

    // Sélection du picker : choisir un sort l'AJOUTE à la rotation puis remet la sélection à zéro.
    private SpellCatalogItem? _spellToAdd;
    public SpellCatalogItem? SpellToAdd
    {
        get => _spellToAdd;
        set
        {
            if (ReferenceEquals(_spellToAdd, value)) return;
            _spellToAdd = value;
            OnPropertyChanged();
            if (value is not null)
            {
                // NE PAS muter AvailableSpells (= l'ItemsSource du ComboBox) DANS son propre changement de sélection :
                // AddSpellFromCatalog -> RefreshAvailableSpells -> Clear() retire l'item qu'on vient de sélectionner
                // PENDANT la notification -> ré-entrée de la sélection -> freeze puis crash. On diffère APRÈS le cycle.
                var toAdd = value;
                Dispatcher.UIThread.Post(() =>
                {
                    AddSpellFromCatalog(toAdd);
                    _spellToAdd = null;
                    OnPropertyChanged(nameof(SpellToAdd));
                });
            }
        }
    }

    // --- Droits serveur (grisage UI) : réévalués via LicenseService.PermissionsChanged (ctor -> RefreshPermissionGates).
    //     Les CheckBox/boutons bindent leur IsEnabled sur ces CanX ; l'exécution reste gatée AU POINT D'ACTION (Lua host,
    //     SetCombatProfile) — le grisage seul ne protège pas d'un client patché (audit P1-2). ---
    public bool CanGather   => LicenseService.HasPermission(LicenseService.Perm.Gather);
    public bool CanFight    => LicenseService.HasPermission(LicenseService.Perm.Fight);
    public bool CanTactical => LicenseService.HasPermission(LicenseService.Perm.Tactical);
    public bool CanNoAnim   => LicenseService.HasPermission(LicenseService.Perm.NoAnim);
    public bool CanAutoReply => LicenseService.HasPermission(LicenseService.Perm.PmRelay);   // messagerie (onglet Paramètres)
    /// <summary>Au moins un mod natif (tactique OU no-anim) autorisé -> la carte Mods est utilisable ;
    /// sinon on affiche le bandeau "pas de licence mods".</summary>
    public bool CanAnyMod   => CanTactical || CanNoAnim;
    /// <summary>Les deux droits de mods présents ; sinon bandeau d'explication (une case cochée mais grisée garde la préférence
    /// enregistrée, inactive tant que le droit manque).</summary>
    public bool CanAllMods  => CanTactical && CanNoAnim;

    /// <summary>true si le perso SÉLECTIONNÉ dépasse le cap de niveau de la licence (Free > 30) -> bandeau + « Lancer » grisé.
    /// Cap PAR PERSONNAGE : réévalué au changement de bot et à chaque montée de niveau.</summary>
    public bool LevelCapBlocked => SelectedBot is { Pid: > 0 } b && Stores.Launch.LevelBlocked((uint)b.Pid);
    /// <summary>Niveau plafond de la licence courante (0 si illimité) — pour libeller le bandeau.</summary>
    public int LicenseLevelCap => LicenseService.LevelCap is int c && c != int.MaxValue ? c : 0;
    /// <summary>« Lancer » possible : droit récolte + perso sous le cap de niveau (grisage UI ; l'exécution reste gatée au point d'action).</summary>
    public bool CanRunLua => !LevelCapBlocked;

    /// <summary>Jouer automatiquement mes tours de combat pour ce bot.</summary>
    public bool CombatAutoFight
    {
        get => _profile.AutoFight;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.Fight)) { OnPropertyChanged(); return; }   // droit "fight" requis
            if (value && _profileBot is { Pid: > 0 } bot)
                Stores.Launch.SetManualCombat((uint)bot.Pid, false);
            if (_profile.AutoFight != value) { _profile.AutoFight = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Style d'IA : Agressif (approche+tape) / Fuyard (kite) / Passif (tire sur place).</summary>
    public CombatStyle CombatStyle
    {
        get => _profile.Style;
        set { if (_profile.Style != value) { _profile.Style = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Cible prioritaire (Le plus proche par defaut).</summary>
    public TargetMode CombatTarget
    {
        get => _profile.Target;
        set { if (_profile.Target != value) { _profile.Target = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Ciblage « finish » : par-dessus le mode de ciblage, achever en priorité les ennemis tuables ce tour. Défaut ON.</summary>
    public bool CombatFinishKill
    {
        get => _profile.FinishKill;
        set { if (_profile.FinishKill != value) { _profile.FinishKill = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Vitesse de jeu (delais entre actions) : Instantané / Rapide / Lent.</summary>
    public CombatSpeed CombatSpeed
    {
        get => _profile.Speed;
        set { if (_profile.Speed != value) { _profile.Speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(CombatIsTurbo)); SaveAndPush(); } }
    }

    /// <summary>Plafond global de lancers par tour (tous sorts confondus).</summary>
    public int CombatMaxCasts
    {
        get => _profile.MaxCastsPerTurn;
        set { if (_profile.MaxCastsPerTurn != value) { _profile.MaxCastsPerTurn = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Distance MINI a garder des ennemis en style Fuyard (borne basse de la bande de kite).</summary>
    public int CombatKiteDistance
    {
        get => _profile.KiteMinDistance;
        set { if (_profile.KiteMinDistance != value) { _profile.KiteMinDistance = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Sous ce PV % (0 = jamais), le tour planifié privilégie la survie : il s'éloigne et limite les dégâts reçus.</summary>
    public int CombatFleeBelowHp
    {
        get => _profile.FleeBelowHpPercent;
        set { if (_profile.FleeBelowHpPercent != value) { _profile.FleeBelowHpPercent = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Distance MAXI de la bande de kite (0 = illimitée -> fuite au maximum ; >0 -> le bot reste dans [min..max]).</summary>
    public int CombatKiteMaxDistance
    {
        get => _profile.KiteMaxDistance;
        set { if (_profile.KiteMaxDistance != value) { _profile.KiteMaxDistance = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Dépenser automatiquement les points de carac à chaque montée de niveau (dans CombatLevelTarget). Droit « fight » requis.</summary>
    public bool CombatAutoLevel
    {
        get => _profile.AutoLevelStats;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.Fight)) { OnPropertyChanged(); return; }   // même droit que l'auto-combat
            if (_profile.AutoLevelStats != value) { _profile.AutoLevelStats = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Caractéristique cible de l'auto level-up (Force / Intelligence / Chance / Agilité / Vitalité / Sagesse).</summary>
    public LevelStatTarget CombatLevelTarget
    {
        get => _profile.LevelStatTarget;
        set { if (_profile.LevelStatTarget != value) { _profile.LevelStatTarget = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>No-anim : termine instantanement les animations de combat (mod natif GivreMods).</summary>
    public bool CombatNoAnim
    {
        get => _profile.NoAnim;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.NoAnim)) { OnPropertyChanged(); return; }   // droit "no-anim" requis
            if (_profile.NoAnim != value) { _profile.NoAnim = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Collapse : termine instantanement les temporisations d'ATTENTE de la sequence de combat (mod natif GivreMods).
    /// Meme famille de pouvoir "vitesse de combat" que no-anim -> gate sous le MEME droit NoAnim.</summary>
    public bool CombatCollapse
    {
        get => _profile.Collapse;
        set
        {
            if (value && !CanCombatCollapse) { OnPropertyChanged(); RefreshCombatVisualStatus(); return; }
            if (_profile.Collapse != value) { _profile.Collapse = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Déplacements de combat instantanes (teleport a la cellule d'arrivee, mod natif GivreMods). Gate sous le droit NoAnim.</summary>
    public bool CombatInstantMove
    {
        get => _profile.InstantMove;
        set
        {
            if (value && !CanCombatInstantMove) { OnPropertyChanged(); RefreshCombatVisualStatus(); return; }
            if (_profile.InstantMove != value) { _profile.InstantMove = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Mode tactique natif : masque le decor de la carte (mod natif GivreMods).</summary>
    public bool CombatTactical
    {
        get => _profile.Tactical;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.Tactical)) { OnPropertyChanged(); return; }   // droit "tactical" requis
            if (_profile.Tactical != value) { _profile.Tactical = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Pré-combat auto : le bot se place (selon le style) et se met PRÊT tout seul. NÉCESSITE que le « prêt
    /// automatique » du client Dofus soit DÉSACTIVÉ (sinon le client lance le combat avant le placement).</summary>
    public bool CombatAutoPreFight
    {
        get => _profile.AutoPreFight;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.Fight)) { OnPropertyChanged(); return; }   // sous-fonction de "fight"
            if (_profile.AutoPreFight != value) { _profile.AutoPreFight = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Choisir + valider automatiquement un challenge au placement (le challenge accepté au meilleur gain moyen) ; le
    /// bonus XP/Butin du profil est annoncé au serveur à chaque combat (socket et MITM).</summary>
    public bool CombatAutoChallenge
    {
        get => _profile.AutoChallenge;
        set
        {
            if (value && !LicenseService.HasPermission(LicenseService.Perm.Fight)) { OnPropertyChanged(); return; }   // sous-fonction de "fight"
            if (_profile.AutoChallenge != value) { _profile.AutoChallenge = value; OnPropertyChanged(); SaveAndPush(); }
        }
    }

    /// <summary>Résurrection AUTOMATIQUE au Phénix à la mort. Indépendante du combat: à la mort le bot
    /// libère l'âme (FreeSoulRequest) puis rejoint le Phénix (carte donnée par le serveur) et l'utilise. Pas de gate "fight".</summary>
    /// <summary>Mode automatique : jouer avec tous les sorts appris qui font des dégâts, en plus de la liste (décocher un sort de la liste l'écarte).</summary>
    public bool CombatAutoSpells
    {
        get => _profile.AutoSpells;
        set { if (_profile.AutoSpells != value) { _profile.AutoSpells = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    public bool CombatAutoRevive
    {
        get => _profile.AutoRevive;
        set { if (_profile.AutoRevive != value) { _profile.AutoRevive = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>« Tout déposer » : dépose tout le sac dans la banque (la banque doit être ouverte).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void DepositAll()
    {
        if (SelectedBot is not { Pid: > 0 } bot) return;
        bool ok = Stores.Launch.BankDepositAll((uint)bot.Pid);
        AppendLuaLine("banque", ok ? "Dépôt de tout le sac demandé" : "Dépôt refusé (banque fermée ou droit manquant)", ok ? LuaLogLevel.Success : LuaLogLevel.Warning);
    }

    /// <summary>Bascule sur l'autre variante du sort (Stalagmite / Onde céleste…) : hors combat seulement.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task SwitchVariant(SpellRowViewModel? row)
    {
        if (row is null || SelectedBot is not { Pid: > 0 } bot || !ReferenceEquals(bot, _profileBot)
            || !CombatSpells.Contains(row)) return;
        var profile = _profile;
        var profileKey = _profileKey;
        int pid = bot.Pid, originalSpell = row.SpellId;
        int alt = row.AltSpellId; string altName = row.AltName;
        if (alt <= 0) return;
        bool ok = await Stores.Launch.SpellVariantSwitchAsync((uint)bot.Pid, alt);
        // Un ACK tardif ne doit pas modifier le profil d'un autre personnage ni une ligne supprimée/éditée.
        if (!ReferenceEquals(bot, _profileBot) || !ReferenceEquals(bot, SelectedBot) || bot.Pid != pid
            || !ReferenceEquals(profile, _profile) || profileKey != _profileKey
            || !CombatSpells.Contains(row) || row.SpellId != originalSpell) return;
        AppendLuaLine("combat", ok ? $"Variante activée : {altName}" : $"Variante refusée : {altName} (hors combat seulement)", ok ? LuaLogLevel.Success : LuaLogLevel.Warning);
        if (!ok) return;
        // La ligne passe sur la variante (mêmes réglages) ; une ligne déjà présente pour la variante est fusionnée (deux variantes
        // d'une même paire ne peuvent pas être équipées ensemble : la ligne restante serait refusée par le serveur à chaque tour).
        foreach (var dup in CombatSpells.Where(r => !ReferenceEquals(r, row) && r.SpellId == alt).ToList())
        { CombatSpells.Remove(dup); _profile.Spells.Remove(dup.Config); }
        row.SwitchTo(alt, SpellDisplayName(alt), SpellIcon(alt));
        row.SetPerTarget(SpellPerTarget(alt));
        RefreshAvailableSpells();
    }

    private string SpellDisplayName(int spellId) => Stores.Launch.GameNames.SpellName(spellId) ?? Loc.Fmt("L.Bots.SpellFallback", spellId);
    private Avalonia.Media.Imaging.Bitmap? SpellIcon(int spellId) => Stores.SpellIcons.Get(Stores.Launch.GameNames.SpellIconId(spellId));

    // Charge le profil du bot selectionne dans les proprietes liees + la liste de sorts + le picker de la classe.
    private void LoadCombatProfile(BotRowViewModel? bot)
    {
        _profileBot = bot;
        var name = bot?.Row.CharacterName;
        _profileKey = string.IsNullOrWhiteSpace(name) ? null : name;
        _profile = ProfileFor(_profileKey);
        OnPropertyChanged(nameof(CombatProfileReady));
        OnPropertyChanged(nameof(CombatProfilePending));
        CombatSpells.Clear();
        SpellRowViewModel.CharacterLevel = bot?.Row.Level ?? 0;
        foreach (var sp in _profile.Spells)
        {
            var row = new SpellRowViewModel(sp, SpellDisplayName(sp.SpellId), SpellIcon(sp.SpellId), SaveAndPush);
            row.SetPerTarget(SpellPerTarget(sp.SpellId));
            CombatSpells.Add(row);
        }
        OnPropertyChanged(nameof(HasCombatSpells));
        RefreshAvailableSpells();
        // Les tables de noms/icones du jeu chargent en tache de fond : si elles ne sont pas encore pretes ici, les
        // sorts s'affichent en repli "Sort <id>" sans icone. On force le chargement (idempotent) hors thread UI puis
        // on re-resout les noms/icones -> "Sort 12904" devient "Sournoiserie" avec son icone des que c'est dispo.
        ScheduleSpellVisualRefresh();
        OnPropertyChanged(nameof(CombatAutoFight));
        OnPropertyChanged(nameof(CombatStyle));
        OnPropertyChanged(nameof(CombatTarget));
        OnPropertyChanged(nameof(CombatFinishKill));
        OnPropertyChanged(nameof(CombatSpeed));
        OnPropertyChanged(nameof(CombatMaxCasts));
        OnPropertyChanged(nameof(CombatKiteDistance));
        OnPropertyChanged(nameof(CombatKiteMaxDistance));
        OnPropertyChanged(nameof(CombatFleeBelowHp));
        OnPropertyChanged(nameof(CombatAutoLevel));
        OnPropertyChanged(nameof(CombatLevelTarget));
        OnPropertyChanged(nameof(CombatNoAnim));
        OnPropertyChanged(nameof(CombatTactical));
        OnPropertyChanged(nameof(CombatCollapse));
        OnPropertyChanged(nameof(CombatInstantMove));
        OnPropertyChanged(nameof(CombatAutoPreFight));
        OnPropertyChanged(nameof(CombatAutoChallenge));
        OnPropertyChanged(nameof(CombatAutoRevive));
        OnPropertyChanged(nameof(CombatAutoSpells));
        RaiseCombatExtras();
        RefreshChallengeCard();
        RestoreCombatTacticalAdvisorForLoadedProfile();
    }

    // Reconstruit la liste du picker = sorts APPRIS du perso (SpellsEvent hms) non encore dans la rotation. Repli sur le
    // repertoire de la CLASSE tant que le paquet des sorts appris n'est pas reçu (perso pas encore identifie / connexion).
    private void RefreshAvailableSpells()
    {
        AvailableSpells.Clear();
        if (_profileBot is not { Pid: > 0 } bot) return;
        var learned = Stores.Launch.LearnedSpells((uint)bot.Pid);
        List<(int Id, string Name)> source = learned.Count > 0
            ? learned.Select(id => (Id: id, Name: SpellDisplayName(id))).OrderBy(x => x.Name).ToList()
            : Stores.Launch.GameNames.SpellsForBreed(bot.Row.BreedId).ToList();   // repli : sorts de la classe
        foreach (var (id, name) in source)
            AvailableSpells.Add(new SpellCatalogItem(id, name, SpellIcon(id)));
    }

    // Stats du perso (PV/PA/PM) mises a jour (sync de combat, degats, hors combat) -> reflet dans l'onglet Infos.
    private void OnFighterStatsUpdated(uint pid) => Dispatcher.UIThread.Post(() =>
    {
        if (!_byPid.TryGetValue((int)pid, out var bot)) return;
        if (Stores.Launch.SelfStats(pid) is { } s)
        {
            bot.CharLife = s.Life; bot.CharMaxLife = s.MaxLife;
            bot.LifeIsEstimated = !Stores.Launch.IsInFight(pid)
                && (Stores.Launch.AuthoritativeLifePercent(pid) is null || Stores.Launch.SelfLifeAuthoritative(pid) != s.Life);
            bot.CharPa = s.Pa; bot.CharMaxPa = s.MaxPa; bot.CharPm = s.Pm; bot.CharMaxPm = s.MaxPm;
            bot.CharRange = Stores.Launch.SelfRange(pid);   // Portée (PO) -> onglet Infos
        }
    });

    // Sorts appris reçus (hms) : si c'est le bot affiché, on rafraîchit le picker avec ses VRAIS sorts + les noms de
    // la rotation (les bundles de noms sont très probablement chargés à ce stade).
    private void OnCharacterSpellsUpdated(uint pid, IReadOnlyList<int> spells) =>
        Dispatcher.UIThread.Post(() => { if (_profileBot is { } b && b.Pid == (int)pid) { RefreshAvailableSpells(); RefreshSpellVisuals(); } });

    // Charge les tables de noms/icones du jeu HORS thread UI (EnsureLoaded fait des I/O sur les bundles ; idempotent
    // et quasi-instantané une fois fait), puis re-résout les visuels des sorts sur le thread UI.
    private void ScheduleSpellVisualRefresh()
    {
        var bot = _profileBot;
        _ = Task.Run(() =>
        {
            Stores.Launch.GameNames.EnsureLoaded();
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_profileBot, bot)) { RefreshSpellVisuals(); RefreshAvailableSpells(); } });
        });
    }

    /// <summary>Limite du jeu « lancers par cible et par tour » d'un sort, au niveau appris par le perso affiché (0 = aucune / inconnue).</summary>
    private int SpellPerTarget(int spellId)
    {
        uint pid = _profileBot is { Pid: > 0 } b ? (uint)b.Pid : 0u;
        try { return spellId > 0 && Stores.Launch.ResolveSimSpell(pid, spellId, false) is { } d ? d.MaxPerTarget : 0; }
        catch { return 0; }
    }

    // Re-résout nom + icône de chaque ligne de la rotation (repli "Sort <id>" -> vrai nom une fois les bundles prêts).
    private void RefreshSpellVisuals()
    {
        SpellRowViewModel.CharacterLevel = _profileBot?.Row.Level ?? 0;
        foreach (var row in CombatSpells) { row.Refresh(SpellDisplayName(row.SpellId), SpellIcon(row.SpellId)); row.SetPerTarget(SpellPerTarget(row.SpellId)); }
    }

    // Sauve le fichier ET repousse le profil au moteur pour le bot courant (auto-combat en direct).
    private void SaveAndPush()
    {
        if (_profileKey is null) return;   // perso pas encore identifié : profil transitoire, rien à enregistrer (éditeur grisé)
        CombatProfileStore.Save();
        // _pushedName = clé du profil RÉELLEMENT poussé (pas le nom courant de la ligne) : si le nom a changé depuis le
        // chargement, le prochain Tick voit la différence et recharge le bon profil au lieu de rester sur l'ancien.
        if (_profileBot is { Pid: > 0 } b) { Stores.Launch.SetCombatProfile((uint)b.Pid, _profile); _pushedName[b.Pid] = _profileKey; }
    }

    // Ajoute un sort CHOISI dans le picker (déjà nommé) à la rotation.
    private void AddSpellFromCatalog(SpellCatalogItem item)
    {
        // Le même sort peut être ajouté plusieurs fois (ex. une ligne « sur moi », une ligne sur l'ennemi, une sur un allié) : chaque ligne a ses propres réglages.
        var sp = new SpellConfig { SpellId = item.Id, Priority = _profile.Spells.Count, PerTurn = 1, Enabled = true };
        // Valeurs du JEU au niveau du perso (SpellLevelData) : lancers par tour / par cible et relance (ex. Berserk : relance 2).
        if (Stores.Launch.GameData?.SpellRuleAt(item.Id, _profileBot?.Row.Level ?? 200) is { } rule)
        {
            sp.PerTurn = rule.MaxCastPerTurn > 0 ? rule.MaxCastPerTurn : 20;   // sans limite du jeu : ce sont les PA qui limitent
            sp.Cooldown = rule.MinCastInterval;
            sp.PerTarget = rule.MaxCastPerTarget;
        }
        _profile.Spells.Add(sp);
        var added = new SpellRowViewModel(sp, item.Name, item.Icon, SaveAndPush);
        added.SetPerTarget(SpellPerTarget(sp.SpellId));
        CombatSpells.Add(added);
        OnPropertyChanged(nameof(HasCombatSpells));
        RefreshAvailableSpells();
        SaveAndPush();
    }

    [RelayCommand]
    private void RemoveSpell(SpellRowViewModel? row)
    {
        if (row is null) return;
        _profile.Spells.Remove(row.Config);
        CombatSpells.Remove(row);
        OnPropertyChanged(nameof(HasCombatSpells));
        RefreshAvailableSpells();
        SaveAndPush();
    }

    // --- Modal de configuration d'UN sort (onglet Sorts -> Configurer) : Essentiel / Placement & zones / Conditions avancées.
    //     EditingSpell = la ligne en cours d'édition (mêmes bindings que la ligne -> édition partagée, persistée via SaveAndPush). ---
    [ObservableProperty] public partial SpellRowViewModel? EditingSpell { get; set; }
    [ObservableProperty] public partial bool SpellConfigVisible { get; set; }

    [RelayCommand]
    private void OpenSpellConfig(SpellRowViewModel? row)
    {
        if (row is null) return;
        EditingSpell = row;
        SpellConfigVisible = true;
    }

    [RelayCommand]
    private void CloseSpellConfig() => SpellConfigVisible = false;

    // --- Modal Simulateur de tour (onglet Comportement -> Simuler). Rejoue la rotation du profil ACTIF (données de combat
    //     résolues via les mêmes tables que le moteur) sur une grille isométrique, animé pas-à-pas. ---
    public CombatSimViewModel CombatSim { get; } = new();
    [ObservableProperty] public partial bool CombatSimVisible { get; set; }

    [RelayCommand]
    private void OpenCombatSim()
    {
        uint pid = _profileBot is { Pid: > 0 } b ? (uint)b.Pid : 0u;
        var rotation = new List<CombatSimulator.SimSpell>();
        foreach (var sp in _profile.Spells)
        {
            if (!sp.Enabled || sp.SpellId <= 0) continue;
            if (Stores.Launch.ResolveSimSpell(pid, sp.SpellId, sp.SelfBuff) is not { } d) continue;
            rotation.Add(new CombatSimulator.SimSpell
            {
                Cfg = sp, Name = SpellDisplayName(sp.SpellId),
                Ap = d.Ap, MinRange = d.MinRange, Range = d.Range,
                CastInLine = d.CastInLine, CastInDiagonal = d.CastInDiagonal, CastTestLos = d.CastTestLos, Rangeable = d.Rangeable, IsBuff = d.IsBuff,
                AvgDamage = d.AvgDamage, Element = d.Element, MaxPerTarget = d.MaxPerTarget, MaxPerTurn = d.MaxPerTurn,
                ZoneShape = d.ZoneShape, ZoneSize = d.ZoneSize, ZoneMin = d.ZoneMin,
            });
        }
        CombatSim.Setup(pid, _profile, rotation, pid > 0 ? Stores.Launch.SelfRange(pid) : 0);
        CombatSimVisible = true;
    }

    [RelayCommand]
    private void CloseCombatSim() => CombatSimVisible = false;

    // Réordonner la rotation : ↑ (delta -1) / ↓ (delta +1). La rotation trie par Priority -> après déplacement, on
    // réaligne _profile.Spells sur l'ordre affiché et on renumérote les priorités (position = priorité).
    [RelayCommand]
    private void MoveSpellUp(SpellRowViewModel? row) => MoveSpell(row, -1);

    [RelayCommand]
    private void MoveSpellDown(SpellRowViewModel? row) => MoveSpell(row, +1);

    private void MoveSpell(SpellRowViewModel? row, int delta)
    {
        if (row is null) return;
        int i = CombatSpells.IndexOf(row);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= CombatSpells.Count) return;   // déjà tout en haut / tout en bas
        CombatSpells.Move(i, j);                                  // ObservableCollection.Move -> l'UI suit l'ordre
        RenumberSpells();
    }

    /// <summary>Réordonne la rotation par glisser-déposer : place <paramref name="dragged"/> à l'emplacement de
    /// <paramref name="target"/> (puis renumérote les priorités = position). Appelé depuis le code-behind (drop).</summary>
    public void MoveSpellTo(SpellRowViewModel dragged, SpellRowViewModel target)
    {
        if (ReferenceEquals(dragged, target)) return;
        int from = CombatSpells.IndexOf(dragged), to = CombatSpells.IndexOf(target);
        if (from < 0 || to < 0) return;
        CombatSpells.Move(from, to);
        RenumberSpells();
    }

    // Après un réordonnancement : priorité = position (0 = plus prioritaire), réaligne _profile.Spells, sauvegarde + pousse.
    private void RenumberSpells()
    {
        _profile.Spells.Clear();
        for (int k = 0; k < CombatSpells.Count; k++) { CombatSpells[k].SetPriority(k); _profile.Spells.Add(CombatSpells[k].Config); }   // SetPriority = notifie l'UI
        SaveAndPush();
    }
}
