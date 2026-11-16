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

// ===================== Équipes : composition, chef, persistance, groupe en jeu, broadcasts / runOnce / bascule de script, démarrer / arrêter le groupe =====================
public partial class BotsViewModel
{
    // ===================== ÉQUIPES (multi-comptes ; plusieurs équipes indépendantes, suivi synthétique au Lot suivant) =====================
    // État CROSS-perso -> il vit ici (l'orchestrateur multi-bots). Un bot ∈ AU PLUS UNE équipe. La vérité de
    // l'appartenance = Team.Members ; BotRow.TeamId en est le miroir. On ne mute que via AssignBot/UnassignBot.
    public ObservableCollection<Team> Teams { get; } = new();
    /// <summary>Bots EN JEU présents dans AUCUN roster d'équipe -> alimente les ComboBox « Ajouter un membre » de chaque carte d'équipe.</summary>
    public ObservableCollection<BotRowViewModel> UnassignedBots { get; } = new();
    /// <summary>Bots EN JEU affichés SEULS dans l'arbre un bot tiers = pas sous une équipe ACTIVE (TeamId null). Inclut un membre d'une
    /// équipe NON formée (non lancée en mode équipe) lancé en solo -> il apparaît comme un bot seul tant que l'équipe n'est pas active.</summary>
    public ObservableCollection<BotRowViewModel> SoloBots { get; } = new();
    /// <summary>Au moins 2 bots en jeu pour que composer une équipe ait un sens.</summary>
    public bool CanFormTeam => Bots.Count >= 2;

    // Palette de démarcation : couleurs INTRINSÈQUES distinctes (pas des accents de thème -> pas de DynamicResource,
    // cf. theme-accent-dynamicresource). Assignées à tour de rôle à la création.
    private static readonly Color[] TeamPalette =
    {
        Color.Parse("#4C8DFF"), Color.Parse("#E6603B"), Color.Parse("#3BD17E"), Color.Parse("#B57BEC"),
        Color.Parse("#E6B24C"), Color.Parse("#38BFB0"), Color.Parse("#EC6FA8"), Color.Parse("#7C89F0"),
    };
    private int _teamColorSeq;

    // AUTO-INVITATION (« Connecter en jeu ») : équipes dont on a lancé les comptes et qui attendent que leurs membres
    // arrivent en jeu pour que le chef les invite AUTOMATIQUEMENT (piloté au Tick par DriveTeamAutoForm). L'armement
    // expire après AutoFormWindowMs (les comptes qui ne se connectent pas sont abandonnés en silence).
    private readonly HashSet<string> _autoFormTeams = new();              // team.Id armés
    private readonly Dictionary<string, long> _autoFormArmedAt = new();   // horodatage d'armement (TickCount64) -> expiration
    private readonly HashSet<int> _teamAutoInvited = new();               // pids de followers déjà invités par l'auto-form (anti-spam)
    private const long AutoFormWindowMs = 150_000;                        // fenêtre d'auto-invitation (2 min 30 : le temps que tout se connecte)

    private static bool SameServer(BotRowViewModel a, BotRowViewModel b)
        => a.Row.Server is { Length: > 0 } s && s != "-" && b.Row.Server == s;   // serveur RÉSOLU et identique (grouping intra-serveur uniquement)

    [RelayCommand]
    private void AddTeam()
    {
        var color = TeamPalette[_teamColorSeq++ % TeamPalette.Length];
        var team = new Team(Loc.Fmt("L.Bots.Team.DefaultName", Teams.Count + 1), color);
        WatchTeam(team);
        Teams.Add(team);
        MarkTeamsDirty();
    }

    [RelayCommand]
    private void RemoveTeam(Team? team)
    {
        if (team is null) return;
        foreach (var m in team.Members.ToList()) ClearBotTeam(m);   // libère les membres EN JEU
        team.Roster.Clear();
        team.PropertyChanged -= OnTeamMetaChanged;
        Teams.Remove(team);
        RefreshBotLists();
        UpdateTeam();
        MarkTeamsDirty();
    }

    [RelayCommand]
    private void CycleTeamColor(Team? team)
    {
        if (team is null) return;
        int i = Array.IndexOf(TeamPalette, team.Color);
        team.Color = TeamPalette[(i + 1 + TeamPalette.Length) % TeamPalette.Length];   // OnTeamMetaChanged re-pousse la couleur + marque dirty
    }

    // Affecte un bot EN JEU à une équipe = l'ajoute au ROSTER persisté (par nom+serveur ; détaché d'abord de toute autre
    // -> « au plus une équipe »). 1er du roster = chef. La réconciliation matérialise ensuite le membre vivant.
    public void AssignBot(Team team, BotRowViewModel bot)
    {
        if (team.Active) return;   // groupe déjà formé -> membres verrouillés
        if (bot.Row.CharacterName is not { Length: > 0 } name) return;
        string server = bot.Row.Server ?? "";
        if (team.Roster.FirstOrDefault() is { } anchor && anchor.Server is { Length: > 0 } asv && asv != "-"
            && server.Length > 0 && server != "-" && !string.Equals(asv, server, StringComparison.OrdinalIgnoreCase))
        { Toasts.Show(Loc.T("L.Bots.Team.ErrOtherServer"), success: false); return; }   // une équipe est intra-serveur
        RemoveFromRoster(bot);                                     // au plus une équipe
        team.Roster.Add(new TeamMemberRecord { Name = name, Server = server, Leader = team.Roster.Count == 0, BreedId = bot.Row.BreedId, AccountId = bot.Row.Model.Id });
        ReconcileTeams();
        MarkTeamsDirty();
    }

    // Retrait UI d'un membre de son équipe (par nom+serveur -> oublié aussi pour les prochaines sessions).
    [RelayCommand]
    private void RemoveMember(BotRowViewModel? bot)
    { if (bot is not null) { RemoveFromRoster(bot); ReconcileTeams(); MarkTeamsDirty(); } }

    [RelayCommand]
    private void SetTeamLeader(BotRowViewModel? bot)
    {
        if (bot is null || bot.Row.CharacterName is not { Length: > 0 } name) return;
        // Trouver l'équipe par APPARTENANCE (Members), pas par bot.TeamId : TeamId est null tant que l'équipe n'est pas
        // formée (dé-groupé pour l'affichage solo), or « Définir chef » se fait justement AVANT de former l'équipe.
        var team = Teams.FirstOrDefault(t => !t.Active && t.Members.Contains(bot));
        if (team is null) return;
        string server = bot.Row.Server ?? "";
        foreach (var e in team.Roster)
            e.Leader = string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && ServerEq(e.Server, server);
        ReconcileTeams();
        MarkTeamsDirty();
    }

    // Variantes HORS LIGNE (paramètre = fiche OfflineMemberViewModel) : mêmes actions par nom+serveur sur le roster.
    [RelayCommand]
    private void RemoveOfflineMember(OfflineMemberViewModel? m)
    {
        if (m is null || Teams.FirstOrDefault(t => t.Id == m.TeamId) is not { Active: false } team) return;
        int idx = team.Roster.FindIndex(e => string.Equals(e.Name, m.Name, StringComparison.OrdinalIgnoreCase) && ServerEq(e.Server, m.Server));
        if (idx < 0) return;
        bool wasLeader = team.Roster[idx].Leader;
        team.Roster.RemoveAt(idx);
        if (wasLeader && team.Roster.Count > 0) team.Roster[0].Leader = true;   // chef retiré -> promotion du 1er restant
        ReconcileTeams(); MarkTeamsDirty();
    }

    [RelayCommand]
    private void SetOfflineLeader(OfflineMemberViewModel? m)
    {
        if (m is null || Teams.FirstOrDefault(t => t.Id == m.TeamId) is not { Active: false } team) return;
        foreach (var e in team.Roster)
            e.Leader = string.Equals(e.Name, m.Name, StringComparison.OrdinalIgnoreCase) && ServerEq(e.Server, m.Server);
        ReconcileTeams(); MarkTeamsDirty();
    }

    // Retire un bot (par nom+serveur) du roster de TOUTE équipe ; promeut un remplaçant chef si le chef partait.
    private void RemoveFromRoster(BotRowViewModel bot)
    {
        if (bot.Row.CharacterName is not { Length: > 0 } name) return;
        string server = bot.Row.Server ?? "";
        foreach (var t in Teams)
        {
            int idx = t.Roster.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && ServerEq(e.Server, server));
            if (idx < 0) continue;
            bool wasLeader = t.Roster[idx].Leader;
            t.Roster.RemoveAt(idx);
            if (wasLeader && t.Roster.Count > 0) t.Roster[0].Leader = true;   // chef retiré -> promotion du 1er restant
        }
    }

    private static bool ServerEq(string? a, string? b)
    {
        a ??= ""; b ??= "";
        if (a is "" or "-" || b is "" or "-") return true;   // serveur pas résolu d'un côté -> on n'oppose pas (match sur le nom)
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RefMatches(TeamMemberRecord e, BotRowViewModel b)
        => b.Row.CharacterName is { Length: > 0 } n && string.Equals(n, e.Name, StringComparison.OrdinalIgnoreCase) && ServerEq(e.Server, b.Row.Server);

    // Verrouille (ou déverrouille) un follower : la propriété UI FollowerLocked ET le signal COMBAT côté LaunchService doivent
    // suivre EXACTEMENT la même condition -> un seul point de vérité. Follower = « juste placer + prêt » (le chef décide des challenges).
    private void ApplyFollowerLock(BotRowViewModel bot, bool locked)
    {
        bot.FollowerLocked = locked;
        if (bot.Pid > 0) Stores.Launch.SetTeamFollower((uint)bot.Pid, locked);
        if (ReferenceEquals(bot, SelectedBot)) Planning.EnableLocked = locked;   // interrupteur « Planning activé » de l'onglet
    }

    private void ClearBotTeam(BotRowViewModel bot)
    {
        CancelFollow(bot.Pid); ClearCombatJoin(bot.Pid);
        bot.TeamId = null; bot.TeamRole = TeamRoleKind.None; bot.TeamColorBrush = null; ApplyFollowerLock(bot, false);
    }

    // Réconcilie les membres VIVANTS (Members) avec le ROSTER persisté + les bots EN JEU. Termine par RefreshBotLists (DIFF :
    // ne mute UnassignedBots/SoloBots que si la séquence change) -> appelable au Tick sans churn. L'appartenance n'est
    // matérialisée (TeamId, groupe) que pour une équipe ACTIVE ; sinon le membre en jeu reste un bot SOLO.
    private void ReconcileTeams()
    {
        foreach (var team in Teams)
        {
            foreach (var m in team.Members.ToList())
                if (!(Bots.Contains(m) && team.Roster.Exists(e => RefMatches(e, m))))
                { team.Members.Remove(m); ClearBotTeam(m); }
            foreach (var e in team.Roster)
            {
                var bot = Bots.FirstOrDefault(b => RefMatches(e, b));
                if (bot is null) continue;   // membre hors ligne -> reste au roster (affiché GRISÉ via DisplayMembers), pas matérialisé
                if (bot.Row.BreedId > 0 && e.BreedId != bot.Row.BreedId) { e.BreedId = bot.Row.BreedId; _teamsDirty = true; }   // mémorise la classe -> icône visible même hors ligne
                if (bot.Row.Model.Id is { Length: > 0 } aid && e.AccountId != aid) { e.AccountId = aid; _teamsDirty = true; }   // mémorise le COMPTE -> « Connecter en jeu » relance le bon compte
                if (!team.Members.Contains(bot)) team.Members.Add(bot);   // Members alimenté même hors mode équipe (FormTeam en a besoin)
                var role = e.Leader ? TeamRoleKind.Leader : TeamRoleKind.Follower;
                // Le RÔLE (chef/follower) est TOUJOURS matérialisé : il alimente l'édition du roster (Team.Leader/CanForm,
                // « Définir chef ») même équipe NON formée. Ce qui reste gaté sur ACTIVE = seulement le GROUPAGE dans l'arbre
                // (TeamId -> sort de SoloBots + couleur + verrou follower). Équipe non formée -> membre affiché SOLO (TeamId null).
                bot.TeamRole = role;
                if (team.Active)
                { if (bot.TeamId != team.Id || bot.TeamColorBrush is null) { bot.TeamId = team.Id; bot.TeamColorBrush = team.ColorBrush; } }
                else if (bot.TeamId is not null)   // équipe NON formée -> dé-groupe (affiché solo) mais garde le rôle
                { bot.TeamId = null; bot.TeamColorBrush = null; }
                ApplyFollowerLock(bot, team.Active && role == TeamRoleKind.Follower);   // follower d'un groupe FORMÉ -> automatisation solo verrouillée + challenges laissés au chef
            }
            RebuildDisplayMembers(team);
            team.NotifyMembers();
        }
        RefreshBotLists();   // inconditionnel : capte AUSSI les bots solo (aucun roster) que la boucle d'appartenance ne touche pas (bug « bot seul absent »)
        UpdateTeam();
    }

    // Construit la liste d'AFFICHAGE de la carte (ordre du roster) : membre EN JEU = son BotRow vivant (présence live) ;
    // sinon fiche HORS LIGNE (nom + icône de classe persistée, grisée). Auto-diff : ne remplace la collection QUE si la
    // séquence a réellement changé -> aucun churn UI au Tick (la réconciliation tourne souvent).
    private void RebuildDisplayMembers(Team team)
    {
        var desired = new List<object>(team.Roster.Count);
        foreach (var e in team.Roster)
        {
            var bot = Bots.FirstOrDefault(b => RefMatches(e, b));
            desired.Add(bot is not null ? bot : new OfflineMemberViewModel(team, e, Stores.ClassIcons.GetById(e.BreedId)));
        }
        if (SameDisplay(team.DisplayMembers, desired)) return;
        team.DisplayMembers.Clear();
        foreach (var d in desired) team.DisplayMembers.Add(d);
    }

    private static bool SameDisplay(IList<object> cur, List<object> next)
    {
        if (cur.Count != next.Count) return false;
        for (int i = 0; i < cur.Count; i++)
        {
            if (ReferenceEquals(cur[i], next[i])) continue;   // même BotRow vivant
            if (cur[i] is OfflineMemberViewModel a && next[i] is OfflineMemberViewModel b
                && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && ServerEq(a.Server, b.Server)
                && a.IsLeader == b.IsLeader && a.CanEdit == b.CanEdit) continue;
            return false;
        }
        return true;
    }

    // Charge les équipes persistées (teams.json) : métadonnées + roster ; Active reste false (on reforme le groupe en jeu).
    // Les membres vivants se matérialisent par réconciliation à mesure que les bots se connectent (match nom+serveur).
    private void LoadTeams()
    {
        foreach (var rec in TeamStore.Load().Teams)
        {
            var color = Color.TryParse(rec.Color, out var c) ? c : TeamPalette[Teams.Count % TeamPalette.Length];
            var team = new Team(string.IsNullOrWhiteSpace(rec.Name) ? Loc.Fmt("L.Bots.Team.DefaultName", Teams.Count + 1) : rec.Name, color)
            { SyncMaxMs = rec.SyncMax, SyncMinMs = rec.SyncMin, FollowEnabled = rec.Follow, CombatJoinEnabled = rec.CombatJoin };
            foreach (var m in rec.Members)
                if (!string.IsNullOrWhiteSpace(m.Name))
                    team.Roster.Add(new TeamMemberRecord { Name = m.Name, Server = m.Server ?? "", Leader = m.Leader, BreedId = m.BreedId, AccountId = m.AccountId ?? "" });
            if (team.Roster.Count > 0 && !team.Roster.Exists(e => e.Leader)) team.Roster[0].Leader = true;   // garantit un chef
            WatchTeam(team);
            Teams.Add(team);
        }
        _teamColorSeq = Teams.Count;
        ReconcileTeams();
    }

    private void WatchTeam(Team team) => team.PropertyChanged += OnTeamMetaChanged;

    // Métadonnées d'équipe éditées (nom/couleur/toggles/délais) -> à re-sérialiser ; la couleur est re-poussée sur les membres.
    private void OnTeamMetaChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Team.Color) && sender is Team t)
            foreach (var m in t.Members) m.TeamColorBrush = t.ColorBrush;
        _teamsDirty = true;
    }

    private void MarkTeamsDirty() => _teamsDirty = true;

    private void SaveTeamsNow()
    {
        _teamsDirty = false;
        var file = new TeamsFile();
        foreach (var t in Teams)
        {
            var rec = new TeamRecord
            {
                Name = t.Name, Color = t.Color.ToString(), Follow = t.FollowEnabled, CombatJoin = t.CombatJoinEnabled,
                SyncMin = t.SyncMinMs, SyncMax = t.SyncMaxMs,
            };
            foreach (var e in t.Roster) rec.Members.Add(new TeamMemberRecord { Name = e.Name, Server = e.Server, Leader = e.Leader, BreedId = e.BreedId, AccountId = e.AccountId });
            file.Teams.Add(rec);
        }
        TeamStore.Save(file);
    }

    [RelayCommand]
    private void FormTeam(Team? team)
    {
        if (team is null) return;
        if (team.Leader is not { } leader) { Toasts.Show(Loc.T("L.Bots.Team.ErrNoLeader"), success: false); return; }
        var followers = team.Members.Where(m => !ReferenceEquals(m, leader) && SameServer(m, leader)).ToList();
        if (followers.Count == 0) { Toasts.Show(Loc.T("L.Bots.Team.ErrNoFollower"), success: false); return; }

        team.Active = true;
        foreach (var f in followers)
            if (f.Pid > 0 && leader.Pid > 0)
                Stores.Launch.ConfigureNativeFollow((uint)f.Pid, (uint)leader.Pid, team.FollowEnabled);
        ReconcileTeams();   // matérialise l'appartenance MAINTENANT (TeamId, groupe visible, sortie de SoloBots) sans attendre le Tick
        foreach (var m in team.Members) ApplyFollowerLock(m, m.TeamRole == TeamRoleKind.Follower);   // verrouille les followers dès la formation (+ challenges au chef)
        foreach (var f in followers)   // le chef prend la main -> stoppe les scripts solo des followers (sinon ils lutteraient contre TeamPilot)
            if (f.Pid > 0 && _luaHosts.TryGetValue(f.Pid, out var fh) && fh.Running) { fh.Stop("team"); _planningPaused.Add(f.Pid); }

        // Invitation RÉELLE en jeu : le chef invite chaque follower par NOM ; le follower auto-accepte (OnPartyInvited).
        int sent = 0; string? firstError = null;
        if (leader.Pid > 0)
            foreach (var f in followers)
                if (f.Row.CharacterName is { Length: > 0 } fn)
                { if (Stores.Launch.PartyInvite((uint)leader.Pid, fn)) sent++; else firstError ??= Stores.Launch.LastPartyInviteError; }

        if (firstError is null)
            Toasts.Show(Loc.Fmt("L.Bots.Team.Formed", team.Name, leader.Row.CharacterName ?? "?", sent), success: true, sound: false);
        else
            Toasts.Show(Loc.Fmt("L.Bots.Team.InviteFailed", firstError), success: false);
    }

    [RelayCommand]
    private void DisbandTeam(Team? team)
    {
        if (team is null) return;
        team.Active = false;
        foreach (var member in team.Members)
            if (member.Pid > 0) Stores.Launch.ConfigureNativeFollow((uint)member.Pid, 0, false);
        DisarmAutoForm(team);   // dissolution -> plus d'auto-invitation en attente
        foreach (var m in team.Members) { CancelFollow(m.Pid); ClearCombatJoin(m.Pid); ApplyFollowerLock(m, false); }   // stoppe suivi + auto-join + déverrouille
        ReconcileTeams();   // dé-matérialise MAINTENANT (TeamId null -> les membres repassent en SoloBots, groupe masqué)
        Toasts.Show(Loc.Fmt("L.Bots.Team.DisbandedName", team.Name), sound: false);
    }

    // « Connecter en jeu » : lance EN JEU (via LEUR proxy par-compte défini dans « Mon compte ») chaque membre du roster
    // pas déjà lancé, puis ARME l'auto-invitation -> dès qu'un membre arrive en jeu et est prêt, le chef l'invite tout
    // seul (DriveTeamAutoForm au Tick). Un membre jamais vu en jeu depuis cette machine n'a pas de compte associé
    // (AccountId vide) -> on le signale (il faut le lancer une fois via l'onglet Comptes pour l'associer).
    [RelayCommand]
    private async Task ConnectTeamInGame(Team? team)
    {
        if (team is null || team.Connecting) return;
        if (team.Roster.Count == 0) { Toasts.Show(Loc.T("L.Bots.Team.ErrEmptyRoster"), success: false); return; }
        if (!LicenseService.IsAuthorized) { Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); return; }

        _autoFormTeams.Add(team.Id);                          // arme l'auto-invitation AVANT de lancer (capte les arrivées)
        _autoFormArmedAt[team.Id] = Environment.TickCount64;
        team.Connecting = true;
        try
        {
            int launched = 0, unknown = 0;
            foreach (var e in team.Roster.ToList())           // copie : LaunchInGameForTeamAsync peut muter les collections (Resync)
            {
                if (Bots.Any(b => RefMatches(e, b))) continue;      // déjà en jeu -> rien à lancer
                var acc = FindAccountFor(e);
                if (acc is null) { unknown++; continue; }           // compte inconnu (jamais vu en jeu) -> à associer d'abord
                if (acc.Running) continue;
                if (await _accounts.LaunchInGameForTeamAsync(acc)) launched++;
            }
            if (unknown > 0) Toasts.Show(Loc.Fmt("L.Bots.Team.ConnectUnknown", unknown), success: false);
            else Toasts.Show(Loc.Fmt("L.Bots.Team.Connecting", team.Name, launched), success: true, sound: false);
        }
        finally { team.Connecting = false; }
    }

    // Retrouve le COMPTE (AccountRow) qui pilote un membre du roster : d'abord par AccountId mémorisé (identité stable,
    // survit au renommage de perso), sinon par nom de perso déjà résolu sur un compte de cette session. Null = inconnu.
    private AccountRow? FindAccountFor(TeamMemberRecord e)
    {
        if (e.AccountId is { Length: > 0 } id && _accounts.Accounts.FirstOrDefault(a => a.Model.Id == id) is { } byId) return byId;
        return _accounts.Accounts.FirstOrDefault(a =>
            a.CharacterName is { Length: > 0 } cn && string.Equals(cn, e.Name, StringComparison.OrdinalIgnoreCase) && ServerEq(a.Server, e.Server));
    }

    // Invitation de groupe REÇUE par un follower : on n'accepte QUE si elle vient du chef de SON équipe (par CharacterId)
    // -> sécurité (jamais le groupe d'un inconnu). Résout l'équipe par TeamId du follower (désambiguïse en multi-équipes).
    private void OnPartyInvited(uint pid, long fromId, string fromName, long partyId)
        => Dispatcher.UIThread.Post(() =>
        {
            if (partyId == 0) return;
            if (!_byPid.TryGetValue((int)pid, out var follower) || follower.TeamRole != TeamRoleKind.Follower) return;
            if (Teams.FirstOrDefault(t => t.Id == follower.TeamId) is not { Active: true } team) return;
            if (team.Leader is not { } leader || leader.Pid <= 0) return;
            if (Stores.Launch.CharacterId((uint)leader.Pid) != fromId) return;   // pas l'invit de NOTRE chef -> on ignore
            Stores.Launch.PartyAccept(pid, partyId);
            Toasts.Show(Loc.Fmt("L.Bots.Team.Accepted", follower.Row.CharacterName ?? fromName), success: true, sound: false);
        });

    // Reconstruit les listes dérivées, en DIFF (ne mute la collection QUE si la séquence change réellement -> aucun churn de
    // sélection des ComboBox même appelé au Tick) :
    //  - UnassignedBots (ComboBox « Ajouter ») = bots EN JEU présents dans AUCUN roster d'équipe (évite le double-roster).
    // - SoloBots = bots EN JEU pas sous une équipe ACTIVE (TeamId null) -> inclut un membre d'équipe NON formée
    //    lancé seul, qui doit s'afficher comme un bot solo tant que l'équipe n'est pas lancée en mode équipe.
    private void RefreshBotLists()
    {
        SyncBotList(UnassignedBots, Bots.Where(b => !Teams.Any(t => t.Roster.Exists(e => RefMatches(e, b)))));
        SyncBotList(SoloBots, Bots.Where(b => b.TeamId is null));
    }
    private static void SyncBotList(ObservableCollection<BotRowViewModel> col, IEnumerable<BotRowViewModel> src)
    {
        var next = src.ToList();
        if (next.Count == col.Count)
        {
            bool same = true;
            for (int i = 0; i < next.Count; i++) if (!ReferenceEquals(next[i], col[i])) { same = false; break; }
            if (same) return;
        }
        col.Clear();
        foreach (var b in next) col.Add(b);
    }

    // Recalcule la présence de chaque membre (comparaison de carte au chef de SON équipe). AUCUN réseau. Appelé au Tick.
    private void UpdateTeam()
    {
        foreach (var b in Bots)
        {
            if (b.TeamId is null || Teams.FirstOrDefault(t => t.Id == b.TeamId) is not { } team || team.Leader is not { } leader)
            { b.TeamPresence = TeamPresenceKind.Connected; continue; }
            // Session de jeu perdue (MITM, Dofus.exe ouvert) : hors ligne pour l'équipe, quel que soit son rôle.
            if (Stores.Launch.IsLinkDown((uint)b.Pid)) { b.TeamPresence = TeamPresenceKind.Offline; continue; }
            bool isLeader = ReferenceEquals(b, leader);
            bool same = SameServer(b, leader) || isLeader;   // le chef est trivialement « même serveur »
            b.TeamPresence = !same ? TeamPresenceKind.OtherServer
                           : isLeader ? TeamPresenceKind.SameMap
                           : b.MapId == 0 ? TeamPresenceKind.Connected
                           : b.MapId == leader.MapId ? TeamPresenceKind.SameMap
                           : TeamPresenceKind.Elsewhere;
        }
    }

    // ---- « Le chef attend l'équipe avant le combat » (appelé au Tick) : pousse à LaunchService, par chef, les pids des
    // followers EN JEU attendus. FightOne (thread script) consulte ce snapshot avant d'engager -> plus de combat en solo.
    // Snapshot COMPLET reconstruit à chaque Tick (aucune rémanence : un ancien chef/une option coupée disparaît aussitôt). ----
    private void PushTeamCombatWait()
    {
        var combatSnap = new Dictionary<uint, uint[]>();
        var mapSnap = new Dictionary<uint, uint[]>();
        // Snapshot de COMPOSITION pour le scripting Lua (team:members/isLeader/leaderName). Indépendant des options d'attente :
        // il reflète l'équipe dès qu'elle a un chef + ≥1 membre EN JEU. Chaque membre pointe sur la MÊME vue (liste partagée).
        var teamSnap = new Dictionary<uint, LaunchService.TeamView>();
        foreach (var team in Teams)
        {
            if (team.Leader is { } lead && lead.Pid > 0)
            {
                var members = team.Members.Where(m => m.Pid > 0)
                    .Select(m => new LaunchService.TeamMemberInfo((uint)m.Pid, m.Row.CharacterName ?? "", m.IsLeader)).ToList();
                if (members.Count > 0)
                {
                    var view = new LaunchService.TeamView((uint)lead.Pid, lead.Row.CharacterName ?? "", members);
                    foreach (var m in members) teamSnap[m.Pid] = view;   // tout membre EN JEU lit la même vue ; isLeader = LeaderPid==pid
                }
            }
            if (!team.Active || team.Leader is not { } leader || leader.Pid <= 0) continue;
            // Followers EN JEU seulement : un follower « Déconnecté » (session perdue, MITM) n'arrivera jamais -> le chef ne l'attend pas.
            var fps = team.Members.Where(m => !ReferenceEquals(m, leader) && m.Pid > 0 && !Stores.Launch.IsLinkDown((uint)m.Pid))
                                  .Select(m => (uint)m.Pid).ToArray();
            if (fps.Length == 0) continue;
            if (team.CombatJoinEnabled) combatSnap[(uint)leader.Pid] = fps;   // attendre les followers avant le combat = IMPLICITE dès qu'on rejoint
            if (team.FollowEnabled) mapSnap[(uint)leader.Pid] = fps;          // attendre les followers avant de quitter la carte = IMPLICITE dès qu'on suit
        }
        Stores.Launch.SetTeamCombatWait(combatSnap);
        Stores.Launch.SetTeamMapChangeWait(mapSnap);
        Stores.Launch.SetTeamSnapshot(teamSnap);
    }

    // ---- Drain des BROADCASTS Lua d'équipe (team:broadcast) : le thread de script a enfilé une demande dans LaunchService ;
    // ici (thread UI, où vivent les hôtes) on LANCE le snippet sur chaque AUTRE membre EN JEU de l'équipe de l'émetteur.
    // Remplace le script courant du membre. Borné par tick (anti-flood) + killswitch licence. ----
    private void DrainTeamBroadcasts()
    {
        int budget = 8;   // au plus 8 broadcasts traités par tick (chaque demande peut lancer plusieurs hôtes)
        while (budget-- > 0 && Stores.Launch.TryDequeueTeamBroadcast(out var req))
        {
            if (!LicenseService.CanBot) return;   // killswitch : pas de session valide -> on ne lance rien
            var team = Teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == (int)req.FromPid));
            if (team is null) continue;
            int started = 0;
            // Membres EN JEU : un membre « Déconnecté » (session perdue, MITM) ne reçoit pas le snippet (injections refusées).
            // Ni un membre en reconnexion (MITM relancé en place, socket sans session en jeu) : le script y tournerait à l'aveugle.
            foreach (var m in team.Members.Where(m => m.Pid > 0 && m.Pid != (int)req.FromPid && !Stores.Launch.IsLinkDown((uint)m.Pid)
                                                      && !ScriptStartBlockedByReconnect(m)).ToList())
            {
                _planningPaused.Remove(m.Pid);
                var host = GetOrCreateHost(m.Pid);
                ApplyMovementSpeed(host, m.Row.CharacterName);
                host.Start(req.Script, "equipe.lua");
                if (SelectedBot?.Pid == m.Pid) LuaRunning = true;
                started++;
            }
            if (started > 0) Stores.Launch.TeamLog($"[équipe] {team.Name} : broadcast Lua lancé sur {started} membre(s).");
        }
    }

    // ---- Lot G : team:runOnce(member, lua) — l'extrait tourne sur l'hôte /cmd TRANSITOIRE du membre (état Lua séparé) : il
    // n'interrompt PAS son script principal. Revérifie que la cible est toujours dans l'équipe de l'émetteur (modèle vivant). ----
    private void DrainTeamRunOnce()
    {
        int budget = 8;
        while (budget-- > 0 && Stores.Launch.TryDequeueTeamRunOnce(out var r))
        {
            if (!LicenseService.CanBot) return;   // killswitch : pas de session valide -> on ne lance rien
            var team = Teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == (int)r.FromPid));
            var target = team?.Members.FirstOrDefault(m => m.Pid == (int)r.TargetPid && m.Pid > 0);
            if (target is null || !_byPid.TryGetValue(target.Pid, out var bot)) continue;   // plus dans la même équipe / hors ligne
            string from = _byPid.TryGetValue((int)r.FromPid, out var fb) ? (fb.Row.CharacterName ?? "?") : "?";
            AppendLuaLine("équipe", Loc.Fmt("L.Lua.TeamRunOnceReceived", from), LuaLogLevel.Info, target.Pid);
            RunConsoleLua(bot, r.Source);
        }
    }

    // ---- Lot G : loadScript(name) / reloadScript() — bascule du script PRINCIPAL. Résolu ici (thread UI) : la bibliothèque
    // du planning n'est pas thread-safe. Mêmes gardes que RunLua (licence, cap de niveau). Le scheduler du planning ne
    // relance un script que si aucun ne tourne : une bascule en pleine plage est conservée ; la fin de plage l'arrête. ----
    private void DrainScriptSwitches()
    {
        int budget = 8;
        while (budget-- > 0 && Stores.Launch.TryDequeueScriptSwitch(out var req))
        {
            // Demande abandonnée (loadScript a déjà rendu false : délai dépassé) ou run appelant arrêté (Stop utilisateur,
            // MODERATOR_STOP, fin de plage, /abortCmd) : on ne bascule PAS, sinon l'arrêt serait annulé.
            if (req.Done.Task.IsCompleted || req.Caller.IsCancellationRequested) { req.Done.TrySetResult(false); continue; }
            bool ok = false;
            try
            {
                int pid = (int)req.Pid;
                if (!LicenseService.CanBot || !_byPid.TryGetValue(pid, out var bot) || Stores.Launch.LevelStartBlocked(req.Pid))
                    AppendLuaLine("script", Loc.Fmt("L.Lua.LoadScriptRefused", req.Name ?? ""), LuaLogLevel.Warning, pid);
                else if (req.Name is null)
                {
                    ok = _luaHosts.TryGetValue(pid, out var h) && h.Restart();
                    if (ok && SelectedBot?.Pid == pid) LuaRunning = true;
                }
                else if (ResolveLibraryScript(bot.Row.CharacterName, req.Name, out var resolved) is not { } body)
                    AppendLuaLine("script", Loc.Fmt("L.Lua.LoadScriptNotFound", req.Name), LuaLogLevel.Warning, pid);
                else
                {
                    var host = GetOrCreateHost(pid);
                    ApplyMovementSpeed(host, bot.Row.CharacterName);
                    AppendLuaLine("script", Loc.Fmt("L.Lua.LoadScriptSwitch", resolved), LuaLogLevel.Success, pid);
                    host.Start(body, resolved);
                    BotScripts.Set(pid, resolved, true);   // télémétrie web : nom du script courant
                    if (SelectedBot?.Pid == pid) LuaRunning = true;
                    ok = true;
                }
            }
            catch { ok = false; }
            req.Done.TrySetResult(ok);
        }
    }

    /// <summary>Script nommé : bibliothèque du planning du perso (nom exact, puis sans casse, puis sans « .lua »), sinon
    /// fichier %APPDATA%/GivreClone/scripts/&lt;name&gt;.lua (nom validé, ≤ 512 Ko, pas de lien). null si introuvable.</summary>
    private static string? ResolveLibraryScript(string? character, string name, out string resolved)
    {
        resolved = name;
        static string Bare(string s) { s = s.Trim(); return s.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s; }
        if (!string.IsNullOrWhiteSpace(character))
        {
            var lib = PlanningStore.Get(character).Scripts;
            var hit = lib.FirstOrDefault(x => x.Name == name)
                   ?? lib.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?? lib.FirstOrDefault(x => string.Equals(Bare(x.Name ?? ""), Bare(name), StringComparison.OrdinalIgnoreCase));
            if (hit is not null && !string.IsNullOrWhiteSpace(hit.Body)) { resolved = hit.Name ?? name; return hit.Body; }
        }
        if (GivreLuaHost.ReadScriptsFolderScript(name) is { Length: > 0 } fileBody && !string.IsNullOrWhiteSpace(fileBody))
        {
            resolved = Bare(name) + ".lua";
            return fileBody;
        }
        return null;
    }

    // team:joinLeader() / joinBoss() Lua (Lot B) : le moteur enfile le pid du CHEF (thread script) ; on exécute ici,
    // sur le thread UI, EXACTEMENT la commande console /joinBoss (re-vérifie le chef sur le modèle Teams vivant).
    private void DrainTeamJoinLeader()
    {
        int budget = 4;
        HashSet<uint>? seen = null;
        while (budget-- > 0 && Stores.Launch.TryDequeueTeamJoinLeader(out var pid))
        {
            if (!LicenseService.CanBot) return;   // killswitch
            if (!(seen ??= new()).Add(pid)) continue;   // doublons d'un même chef dans le même drain : un seul ralliement
            var bot = Bots.FirstOrDefault(b => b.Pid == (int)pid);
            if (bot is not null) HandleJoinBoss(bot, fromLua: true);
        }
    }
    private bool _teamsDirty;                                                // équipes modifiées -> à re-sérialiser (flush throttlé au Tick)

    // ---- Commandes de GROUPE (onglet Équipe): démarrer / arrêter tout le groupe en un clic. Startallpath:
    // on lance le SCRIPT DU CHEF (les followers sont pilotés par le suivi/mirroir) ; l'arrêt, lui, coupe TOUT le monde. ----
    [RelayCommand]
    private void StartTeam(Team? team)
    {
        if (team is null || !LicenseService.IsAuthorized) return;
        if (LicenseService.IsSuspended) { Toasts.Show(Loc.T("L.LicLink.Paused"), success: false); return; }   // reprise de session bloquée
        if (team.Leader is not { Pid: > 0 } leader) { Toasts.Show(Loc.T("L.Bots.Team.ErrNoLeader"), success: false); return; }
        if (ScriptStartBlockedByReconnect(leader)) { Toasts.Show(Loc.T("L.Reconnect.Busy"), success: false, sound: false); return; }   // chef en cours de « Reconnecter » / socket sans session
        foreach (var m in team.Members)   // cap de niveau licence (offre gratuite) : aucun membre au-delà ne peut être piloté
            if (m.Pid > 0 && RefuseLevelCapped(m.Pid, interactive: true)) return;
        string script = PlanningStore.Get(leader.Row.CharacterName).Script;
        if (string.IsNullOrWhiteSpace(script) || IsSampleScript(script)) { Toasts.Show(Loc.T("L.Bots.Team.ErrNoScript"), success: false); return; }
        _planningPaused.Remove(leader.Pid);
        var host = GetOrCreateHost(leader.Pid);
        ApplyMovementSpeed(host, leader.Row.CharacterName);
        host.Start(script, "script.lua");
        if (SelectedBot?.Pid == leader.Pid) LuaRunning = true;
        Stores.Launch.TeamLog($"[équipe] {team.Name} : groupe démarré (script du chef {leader.Row.CharacterName} ; les followers suivent).");
    }

    [RelayCommand]
    private void StopTeam(Team? team)
    {
        if (team is null) return;
        foreach (var m in team.Members)
        {
            if (m.Pid <= 0) continue;
            if (_luaHosts.TryGetValue(m.Pid, out var h)) { try { h.Stop(); } catch { /* best-effort */ } }
            _planningPaused.Add(m.Pid);   // arrêt manuel -> le scheduler ne relance pas
            DropInPlaceScript(m.Row.Model.Id);   // ... ni la relance MITM en place en cours (script relevé avant la relance)
            CancelFollow(m.Pid);
        }
        if (SelectedBot is { } sb && team.Members.Any(m => m.Pid == sb.Pid)) LuaRunning = false;
        Stores.Launch.TeamLog($"[équipe] {team.Name} : groupe arrêté (scripts + suivi coupés).");
    }
}
