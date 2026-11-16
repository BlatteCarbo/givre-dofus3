using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Déconnexion planifiée EN ATTENTE (modale à compte à rebours) : l'utilisateur peut l'annuler (le compte reste
/// en ligne jusqu'à sa prochaine plage) ou la déclencher tout de suite. Objet stable, mis à jour en place à chaque tick.</summary>
public sealed partial class PendingPlanDisconnect : ObservableObject
{
    internal PlanningSession Session { get; init; }
    public string AccountId { get; init; } = "";
    public string Character { get; init; } = "";
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";      // « hors plage » / « fin de plage » (localisé)
    public string NextText { get; init; } = "";    // « prochaine plage : Me 08:00 » / « aucune plage prévue »
    public string Status { get; init; } = "";      // statut posé sur la ligne du compte après la déconnexion
    public bool EndOfSlot { get; init; }           // fin de plage (sinon : lancé hors plage) -> libellé du toast
    public long StartedAt { get; init; }
    public long Deadline { get; set; }
    internal bool Done;                             // traité (retiré après le fondu de la modale)
    [ObservableProperty] public partial int SecondsLeft { get; set; }
    [ObservableProperty] public partial double Progress { get; set; } = 1;
}

/// <summary>Perso en « connexion auto » dont le compte n'est pas en jeu : ligne grisée « Hors-ligne · géré par le
/// planning ». Jamais dans <see cref="BotsViewModel.Bots"/> (réservé aux bots en ligne).
/// ObservableObject (pas ViewModelBase : le ViewLocator ne doit pas lui chercher de vue).</summary>
public sealed partial class PlannedOfflineRow : ObservableObject
{
    public string Character { get; }
    public PlannedOfflineRow(string character) { Character = character; }
    [ObservableProperty] public partial string AccountId { get; set; } = "";
    [ObservableProperty] public partial string Login { get; set; } = "";
    [ObservableProperty] public partial Bitmap? ClassIcon { get; set; }
    [ObservableProperty] public partial string LevelText { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string NextText { get; set; } = "";
    /// <summary>Échéance seule (« mer. 30 sept. 22:10 ») pour la carte étroite : l'icône horloge dit déjà « prochaine
    /// connexion », la phrase complète reste dans l'infobulle et la modale.</summary>
    [ObservableProperty] public partial string NextShort { get; set; } = "";
    [ObservableProperty] public partial string TipText { get; set; } = "";   // SANS donnée perso (les infobulles ne sont pas floutées en mode stream)
    [ObservableProperty] public partial bool IsConnecting { get; set; }
    [ObservableProperty] public partial bool NeedsAttention { get; set; }
    [ObservableProperty] public partial bool CanConnect { get; set; }
    internal long NextMinute = -1;                  // NextStart coûte cher : recalculé au plus une fois par minute...
    internal int NextSig;                           // ...ou quand les plages changent
    internal DateTime? NextAt;
}

// « Vrai planning » : CONNEXION/DÉCONNEXION automatique des comptes selon les plages (interrupteur « Connexion auto »).
//  - début de plage : le compte qui porte le perso est lancé DIRECT EN JEU (AutoConnectType 2 = dernier perso joué), sans
//    modal ; on vérifie le perso (sinon déconnexion + plage ignorée). Un compte déjà en jeu à la main sur ce perso est PRIS
//    EN CHARGE. Le script est lancé par EvaluatePlanning (attend la 1re carte pour un compte géré).
//  - HORS plage : tout compte en jeu sur un perso en connexion auto est déconnecté (après le combat en cours), qu'il ait été
//    lancé par le planning ou À LA MAIN — via une MODALE À COMPTE À REBOURS que l'utilisateur peut annuler (le compte
//    reste alors en ligne jusqu'à sa prochaine plage). Garde-fous anti-verrouillage : planning sans plage utilisable ->
//    jamais de déconnexion ; « Connecter maintenant » (ligne hors-ligne) et Annuler exemptent jusqu'à la prochaine plage ;
//    pas de déconnexion si une plage reprend dans quelques minutes.
//  - sûreté : 3 tentatives max par OCCURRENCE de plage, un lancement planifié à la fois (jamais deux sur un compte), arrêt
//    MANUEL pendant une plage non relancé avant sa fin, crash d'un compte géré relancé par le planning (pas par la relance
//    auto), compte qui exige l'utilisateur (code du Bouclier, échecs répétés) plus retenté avant un lancement manuel.
// Tout tourne sur le thread UI : DrivePlanningConnect au tick throttlé (~1,5 s) AVANT EvaluatePlanning, le compte à
// rebours à CHAQUE tick (300 ms).
public partial class BotsViewModel
{
    private const int PlanMaxAttempts = 3;                  // lancements (crashs compris) par occurrence de plage et par compte
    private const long PlanRetryDelayMs = 60_000;           // attente entre deux tentatives d'un même compte
    private const long PlanInGameTimeoutMs = 300_000;       // lancement -> perso identifié en jeu (auth + chargement du client)
    private const long PlanInFlightMaxMs = 900_000;         // garde-fou : un LaunchAsync qui ne rend jamais la main ne bloque pas à vie
    private const long PlanLaunchSpacingMs = 20_000;        // écart minimal entre deux lancements planifiés (tous comptes)
    private const long PlanDrainMaxMs = 600_000;            // hors plage : attente max de la fin du combat avant le compte à rebours
    private const long PlanStableMs = 900_000;              // session planifiée stable 15 min -> budget de tentatives ré-armé
    private const long PlanOutGraceMs = 30_000;             // plage inactive depuis < 30 s : on ne conclut rien (édition d'une heure)
    private const long PlanCountdownMs = 10_000;            // compte à rebours de la modale de déconnexion (annulable)
    private const long PlanConnectNowHoldMs = 180_000;      // « Connecter maintenant » : exemption tenue pendant le lancement
    private const int PlanKeepOnlineMin = 10;               // une plage reprend dans <= N min (+ 2x jitter) -> on reste connecté
    private const int PlanGiveUpStreakMax = 2;              // plages consécutives abandonnées sans connexion -> attend l'utilisateur

    // État de connexion planifiée PAR COMPTE (Account.Id). Absent = le planning ne gère rien pour ce compte.
    private sealed class PlanConn
    {
        public string OccKey = "";      // occurrence(s) de plage en cours (perso:id@jour) : son changement ré-arme le budget
        public int Attempts;            // lancements tentés pendant l'occurrence courante
        public long NextTryAt;          // TickCount64 : pas de nouvel essai avant
        public long LaunchStartedAt;    // > 0 : lancement planifié en cours (attente du perso en jeu)
        public int LaunchGen;           // AccountRow.LaunchGen de CE lancement : un autre lancement n'est jamais pris pour le nôtre
        public bool InFlight;           // LaunchForPlanningAsync pas encore revenu -> jamais de 2e lancement du même compte
        public long InFlightSince;
        public bool Managed;            // pris en charge pendant la plage (lancé par le planning ou en jeu à la main)
        public long ManagedSince;
        public bool Suppressed;         // arrêt manuel / mauvais perso / abandon : plus de lancement planifié jusqu'à la fin de l'occurrence
        public bool NeedsUser;          // code du Bouclier exigé / échecs répétés : plus de lancement planifié avant un lancement manuel
        public int GiveUpStreak;        // occurrences consécutives abandonnées sans jamais être entré en jeu
        public long OutSince;           // > 0 : hors plage depuis (grâce avant de conclure « fin de plage »)
        public long DrainSince;         // > 0 : hors plage, attente fin de combat / arrêt du script avant le compte à rebours
        public bool Pending;            // compte à rebours de déconnexion affiché
        public bool Override;           // l'utilisateur garde le compte en ligne (Annuler / Connecter maintenant) jusqu'à la prochaine plage
        public string OverrideOcc = ""; // occurrence (OccKey) où l'exemption a été accordée : levée seulement à une AUTRE occurrence
        public bool DisconnectAfterFight;   // déconnexion décidée (décompte fini / « maintenant ») pendant un combat : à la fin du combat, sans re-demander
        public string AfterFightStatus = "";
        public PlanningSession AfterFightSession;
        public bool AfterFightEnd;      // la déconnexion différée vient d'une FIN de plage (libellé du toast)
        public string PlanSig = "";     // empreinte des plages du compte : une édition hors plage ré-arme la grâce
        public long OverrideKeepUntil;  // « Connecter maintenant » : exemption tenue même hors ligne pendant le lancement
        public string OutNote = "";     // dernière explication « reste connecté » journalisée (une fois par état, hors plage)
        public bool Notified;           // une attente (protocole / multi) déjà signalée pendant cette occurrence
    }
    private readonly Dictionary<string, PlanConn> _planConn = new(StringComparer.Ordinal);
    private bool _planLaunchBusy;
    private long _planLastLaunchAt;
    private readonly HashSet<int> _planNoScriptLogged = new();   // bots déjà signalés « aucun script pour cette plage » (anti-spam)

    // Plage SERVIE par le scheduler, par bot (pid) : occurrence de plage déjà traitée (PlanningEval.ActiveOccurrence, évaluée
    // hors blocage) + corps que le planning y a lancé (null = rien lancé par lui : script lancé à la main, plage sans script).
    // Sert à BASCULER de script quand une plage en suit une autre SANS TROU (09:00 fin d'« Otomai » = début de « Fishing ») :
    // avant, le scheduler ne lançait qu'un bot ARRÊTÉ et n'arrêtait qu'HORS plage -> le script de la 1re plage tournait
    // jusqu'à la fin de la dernière. Levé hors plage / planning désactivé ; migré à la re-clé ; gardé par compte quand le bot
    // quitte la liste (_planRunKept) pour la reprise de « Reconnecter » hors ligne.
    private sealed class PlanRun { public string Occ = ""; public string? Body; }
    private readonly Dictionary<int, PlanRun> _planRun = new();
    // Script REPRIS après une reconnexion (socket, relance en place, relance hors ligne, reconnexion de script) : il peut dater
    // d'une plage précédente -> confronté à la plage active au prochain passage du scheduler (PlanningEval.DecideRun).
    private readonly HashSet<int> _planRecheck = new();
    private readonly Dictionary<string, PlanRun> _planRunKept = new(StringComparer.Ordinal);   // Account.Id -> plage servie au départ du bot

    // Un script vient d'être REPRIS sur ce pid (thread UI) : restaure la plage servie gardée pour ce compte (bot sorti de la
    // liste puis relancé) et demande la vérification contre la plage active.
    private void PlanNoteResumed(int pid, string? accountId)
    {
        if (accountId is not null && _planRunKept.Remove(accountId, out var kept) && !_planRun.ContainsKey(pid)) _planRun[pid] = kept;
        _planRecheck.Add(pid);
    }
    private bool _classIconsKicked;                               // chargement du bundle des icônes de classe déjà demandé (lignes hors ligne)

    private PlanConn PlanState(string accountId)
    {
        if (!_planConn.TryGetValue(accountId, out var st)) _planConn[accountId] = st = new PlanConn();
        return st;
    }

    // ------------------------------------------------------------------ état exposé à l'UI

    /// <summary>Déconnexions planifiées en attente (modale à compte à rebours, niveau fenêtre).</summary>
    public ObservableCollection<PendingPlanDisconnect> PendingDisconnects { get; } = new();
    [ObservableProperty] public partial bool PendingDisconnectVisible { get; set; }
    public bool HasManyPending => PendingDisconnects.Count(p => !p.Done) > 1;

    /// <summary>Persos en connexion auto dont le compte n'est pas en jeu.</summary>
    public ObservableCollection<PlannedOfflineRow> PlannedOffline { get; } = new();
    public bool HasPlannedOffline => PlannedOffline.Count > 0;
    /// <summary>État vide de l'arbre un bot tiers: ni bot en jeu, ni perso planifié hors ligne.</summary>
    public bool ShowNoBotsHint => !HasBots && !HasPlannedOffline;
    public string PlannedOfflineCountText => Loc.Fmt("L.Planning.Offline.Count", PlannedOffline.Count);

    /// <summary>Planning d'un perso HORS LIGNE (modale) : instance DISTINCTE de <see cref="Planning"/> (onglet du bot
    /// sélectionné), liée au compte du perso — jamais à l'éditeur Lua / au compte du bot sélectionné.</summary>
    public PlanningViewModel OfflinePlanning { get; } = new() { ShowEditorActions = false };
    [ObservableProperty] public partial bool OfflinePlanningVisible { get; set; }
    // Éditeur de la modale actif : coupé ~250 ms APRÈS la fermeture (pas de grisé pendant le fondu) ; tant qu'il est coupé,
    // aucune frappe n'atteint l'éditeur caché (la modale reste dans l'arbre, invisible).
    [ObservableProperty] public partial bool OfflineEditorEnabled { get; set; }
    [ObservableProperty] public partial PlannedOfflineRow? OfflinePlanningTarget { get; set; }

    // ------------------------------------------------------------------ crochets AccountsViewModel

    // Crash inattendu d'un client : si le planning l'a connecté / pris en charge (ou le connecte), il PREND le crash en
    // charge — état remis à zéro tout de suite et relance dans la plage (budget par occurrence).
    private bool PlanningClaimsCrash(AccountRow row)
    {
        if (!_planConn.TryGetValue(row.Model.Id, out var s)) return false;
        DropPending(row.Model.Id);
        s.DisconnectAfterFight = false;   // la session qui devait partir n'existe plus : jamais de déconnexion sans décompte sur la suivante
        bool owned = s.Managed || s.LaunchStartedAt > 0;
        bool inSlot = s.OccKey.Length > 0;
        // Chemin GÉNÉRIQUE (relance auto si activée) : plage ignorée, session manuelle, ou compte gardé en ligne HORS plage
        // (« Garder en ligne » = choix de l'utilisateur, le planning ne relance que DANS ses plages). L'état de possession est
        // remis à zéro dans tous les cas : la session relancée sera réévaluée (adoptée en plage) au lieu de rester « gérée ».
        if (s.Suppressed || !owned || (s.Override && !inSlot))
        {
            s.Managed = false; s.LaunchStartedAt = 0; s.DrainSince = 0;
            return false;
        }
        // En plage (même exemptée) : c'est le planning qui relance, direct en jeu sur le bon perso, avec son budget.
        s.Managed = false; s.LaunchStartedAt = 0; s.DrainSince = 0;
        s.NextTryAt = Math.Max(s.NextTryAt, Environment.TickCount64 + PlanRetryDelayMs);
        return true;
    }

    // Compte connecté par le planning -> EvaluatePlanning attend la 1re carte avant de lancer le script.
    private bool PlanManaged(AccountRow row)
        => _planConn.TryGetValue(row.Model.Id, out var s) && (s.Managed || s.LaunchStartedAt > 0);

    // Arrêt MANUEL (Arrêter / Arrêter tout / déconnexion web / révocation) : ne pas relancer avant la fin de la plage.
    private void OnAccountUserStopped(AccountRow row)
    {
        DropPending(row.Model.Id);
        DropScriptReconnect(row.Model.Id);   // compat tiers: un arrêt utilisateur annule la relance programmée par le script
        AbortReconnectPreflight(row.Model.Id);   // « Reconnecter » dont les pré-requis sont encore en cours : annulé aussi
        SyncReconnectFlags();                    // bouton « Reconnecter » / « Arrêter » de la carte à jour tout de suite
        if (!_planConn.TryGetValue(row.Model.Id, out var st)) return;
        st.Suppressed = true; st.Managed = false; st.LaunchStartedAt = 0; st.DrainSince = 0;
        st.Override = false; st.DisconnectAfterFight = false;   // exemption = une session : un nouveau lancement repose la question
    }

    // Déconnexion SÈCHE demandée par un script (global:disconnect / givreController:unloadAnAccount): comme un arrêt
    // manuel, pas de relance planifiée avant la fin de la plage — sinon PlanInSlot la prenait pour un crash et reconnectait
    // ~60 s après. (Une reconnexion programmée — reconnect(h) / PLANNING — est tenue à part : ScriptReconnectHeld.)
    private void PlanHoldForScriptStop(AccountRow row)
    {
        DropPending(row.Model.Id);
        if (!_planConn.TryGetValue(row.Model.Id, out var st)) return;
        st.Suppressed = true; st.Managed = false; st.LaunchStartedAt = 0; st.DrainSince = 0; st.DisconnectAfterFight = false;
    }

    // ------------------------------------------------------------------ scheduler (tick throttlé)

    private void DrivePlanningConnect()
    {
        if (!LicenseService.IsAuthorized) return;   // killswitch : ni connexion ni déconnexion planifiée
        var now = DateTime.Now;
        long t = Environment.TickCount64;

        // 1) Lie chaque perso (planning « connexion auto ») au compte sur lequel il est VU en jeu : c'est ce compte que le
        //    planning lancera. Une liaison déjà connue reste sur ce compte (homonymes entre serveurs). Un nom vu sur PLUSIEURS bots (même nom sur
        //    deux serveurs) est ambigu : on ne relie pas (sinon la liaison basculerait à chaque tick). Mémorise aussi
        //    classe/niveau pour la ligne « hors-ligne » du perso.
        foreach (var grp in Bots.Where(b => b.Pid > 0 && b.Row.Running && b.Row.Pid == b.Pid
            && !string.IsNullOrEmpty(b.Row.CharacterName) && Stores.Launch.CurrentMap((uint)b.Pid) is > 0
            && Stores.Launch.SelfCharacterInfo((uint)b.Pid)?.Name == b.Row.CharacterName).GroupBy(b => b.Row.CharacterName!))
        {
            if (grp.Count() != 1 || !PlanningStore.TryGet(grp.Key, out var cpb) || !cpb.AutoConnect) continue;
            var br = grp.First().Row;
            if (!PlanningEval.CanUseAccount(cpb, br.Model.Id)) continue;
            bool dirty = false;
            if (cpb.AccountId != br.Model.Id) { cpb.AccountId = br.Model.Id; dirty = true; }
            if (br.BreedIndex is int bi && (cpb.BreedIndex != bi || cpb.GenderIndex != br.GenderIndex)) { cpb.BreedIndex = bi; cpb.GenderIndex = br.GenderIndex; dirty = true; }
            if (br.Level is int lv && cpb.Level != lv) { cpb.Level = lv; dirty = true; }
            if (dirty) PlanningStore.Save();
        }

        // 2) Plannings « connexion auto » PAR COMPTE, et persos VOULUS en jeu maintenant (+ occurrence de leur plage).
        var bound = new Dictionary<string, List<(string Name, CharPlanning Cp)>>(StringComparer.Ordinal);
        var wanted = new Dictionary<string, List<(string Name, string Occ)>>(StringComparer.Ordinal);
        foreach (var (name, cp) in PlanningStore.All())
        {
            if (!cp.Enabled || !cp.AutoConnect || string.IsNullOrEmpty(cp.AccountId)) continue;
            if (!bound.TryGetValue(cp.AccountId, out var bl)) bound[cp.AccountId] = bl = new List<(string, CharPlanning)>();
            bl.Add((name, cp));
            if (PlanningEval.ActiveOccurrence(cp, now, name) is not { } occ) continue;
            if (!wanted.TryGetValue(cp.AccountId, out var l)) wanted[cp.AccountId] = l = new List<(string, string)>();
            l.Add((name, occ));
        }

        foreach (var accId in bound.Keys.Union(_planConn.Keys).ToList())
        {
            var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == accId);
            // Compte supprimé, ou connexion auto désactivée : on cesse de gérer (sans déconnecter ce qui est en ligne).
            if (row is null || !bound.TryGetValue(accId, out var plans)) { DropPending(accId); _planConn.Remove(accId); continue; }
            var st = PlanState(accId);

            // Un lancement MANUEL réussi (compte en ligne hors lancement planifié) lève l'attente de l'utilisateur : le
            // certificat du Bouclier est alors mémorisé / les identifiants revalidés.
            if (row.Running && row.Pid is not null && !st.InFlight && st.LaunchStartedAt == 0 && !st.Managed)
            { st.NeedsUser = false; st.GiveUpStreak = 0; }

            if (!wanted.TryGetValue(accId, out var chars) || chars.Count == 0) { PlanOutOfSlot(row, st, plans, now, t); continue; }
            string occKey = string.Join("|", chars.Select(c => c.Name + ":" + c.Occ).OrderBy(k => k, StringComparer.Ordinal));
            st.OutSince = 0; st.OutNote = ""; st.DisconnectAfterFight = false;
            // L'exemption « rester en ligne » vaut jusqu'à la PROCHAINE plage : levée seulement quand une occurrence ABSENTE au
            // moment de l'exemption apparaît (une plage qui NAÎT, pas une qui se termine — deux persos sur un compte).
            if (st.Override && PlanHasNewOccurrence(occKey, st.OverrideOcc)) st.Override = false;
            DropPending(accId);                                      // plage (re)devenue active : plus de déconnexion à l'ordre du jour

            // Nouvelle occurrence de plage (début, plage suivante même contiguë, nouveau jour d'une plage continue) :
            // budget de tentatives, suppression manuelle et avis d'attente ré-armés.
            if (occKey != st.OccKey) { PlanCloseOccurrence(row, st); st.OccKey = occKey; }
            PlanInSlot(row, st, chars.Select(c => c.Name).ToList(), plans, now, t);
        }
    }

    // Fin d'une occurrence de plage (hors plage, ou plage suivante) : bilan puis ré-armement du budget / de l'arrêt manuel.
    // Plusieurs occurrences de suite abandonnées sans jamais entrer en jeu (identifiants expirés, compte bloqué…) -> on
    // cesse d'enchaîner des connexions Ankama vouées à l'échec tant que l'utilisateur n'a pas relancé le compte.
    private static void PlanCloseOccurrence(AccountRow row, PlanConn st)
    {
        if (st.OccKey.Length > 0 && st.Attempts >= PlanMaxAttempts && !st.Managed
            && ++st.GiveUpStreak >= PlanGiveUpStreakMax && !st.NeedsUser)
        {
            st.NeedsUser = true;
            Toasts.Show(Loc.Fmt("L.Planning.Auto.Suspended", row.Login, PlanGiveUpStreakMax), success: false);
        }
        st.Attempts = 0; st.Suppressed = false; st.Notified = false;
    }

    // `current` contient-il une occurrence (perso:plage@jour) qui n'était pas dans `granted` ? (clés jointes par « | »)
    private static bool PlanHasNewOccurrence(string current, string granted)
    {
        if (current.Length == 0) return false;
        if (granted.Length == 0) return true;
        var had = new HashSet<string>(granted.Split('|'), StringComparer.Ordinal);
        foreach (var o in current.Split('|')) if (!had.Contains(o)) return true;
        return false;
    }

    // Une plage d'un des persos planifiés de ce compte démarre-t-elle bientôt ? (jitter : l'écart entre deux plages collées
    // peut atteindre 2x jitter). Évalué sur TOUS les persos du compte : le nom du perso en ligne peut être encore inconnu.
    private static bool PlanStartsSoon(List<(string Name, CharPlanning Cp)> plans, DateTime now, int baseMin)
        => plans.Any(p => PlanningEval.StartsWithin(p.Cp, now, p.Name, baseMin + 2 * Math.Max(0, p.Cp.JitterMin)));

    private static DateTime? PlanNextStart(List<(string Name, CharPlanning Cp)> plans, DateTime now)
    {
        DateTime? next = null;
        foreach (var p in plans)
            if (PlanningEval.NextStart(p.Cp, now, p.Name) is DateTime n && (next is null || n < next)) next = n;
        return next;
    }

    // Journalise UNE fois par état (hors plage) pourquoi le compte reste connecté : jamais de silence inexpliqué.
    private void PlanNote(PlanConn st, AccountRow row, string key, string text)
    {
        if (st.OutNote == key) return;
        st.OutNote = key;
        if (row.Pid is int p) AppendLuaLine("planning", text, LuaLogLevel.Info, p);
    }

    // HORS plage pour ce compte : ré-arme les compteurs ; un compte en jeu sur un perso en connexion auto est déconnecté
    // (après le combat) via la modale à compte à rebours — sauf exemption / planning sans plage / plage imminente.
    private void PlanOutOfSlot(AccountRow row, PlanConn st, List<(string Name, CharPlanning Cp)> plans, DateTime now, long t)
    {
        // Grâce : une plage qui redevient active sous 30 s ne doit ni lever un arrêt manuel, ni ré-armer le budget, ni
        // déclencher une déconnexion.
        // Planning édité HORS plage (plage ajoutée, jour coché, heures changées) : pas de conclusion avant 30 s non plus —
        // « Ajouter une plage » (tous les jours 08-12 par défaut) ne doit pas lever l'anti-verrouillage en pleine saisie.
        string sig = string.Join(";", plans.Select(p => p.Cp.JitterMin + ":" + string.Join(",", p.Cp.Slots.Select(s => s.Id + "/" + s.Days + "/" + s.StartMin + "/" + s.EndMin))));
        if (sig != st.PlanSig)
        {
            bool first = st.PlanSig.Length == 0 && st.OutSince == 0;
            st.PlanSig = sig;
            if (!first) st.OutSince = t;   // grâce ré-armée ; le plafond d'attente de fin de combat (DrainSince) n'est PAS remis à zéro
        }
        if (st.OutSince == 0) st.OutSince = t;
        if (t - st.OutSince < PlanOutGraceMs) return;

        PlanCloseOccurrence(row, st);
        st.OccKey = "";
        if (!row.Running)
        {
            st.Managed = false; st.LaunchStartedAt = 0; st.DrainSince = 0; st.OutNote = ""; st.DisconnectAfterFight = false;
            if (st.Override && t > st.OverrideKeepUntil) st.Override = false;   // exemption = une session ; « Connecter maintenant » tient pendant le lancement
            DropPending(row.Model.Id);
            return;
        }
        if (st.LaunchStartedAt > 0 && row.LaunchGen != st.LaunchGen) st.LaunchStartedAt = 0;   // pas NOTRE lancement
        if (st.Pending) return;                        // compte à rebours déjà affiché (DrivePendingDisconnects décide)
        if (st.Override) { st.DrainSince = 0; return; }   // l'utilisateur a choisi de le garder en ligne
        // « Reconnecter » en place en cours (relance MITM, session socket coupée / réveillée) : pas de décompte ni de déconnexion
        // « après le combat » (la relance fait oublier le combat) — la question se repose une fois de retour en jeu.
        if (row.ReconnectInPlace || _accounts.IsRelaunching(row)) { st.DrainSince = 0; return; }

        string? nm = row.CharacterName;
        bool ours = st.Managed || st.LaunchStartedAt > 0;
        if (!ours)
        {
            // Lancé à la main : n'agit que sur un perso EN CONNEXION AUTO de ce compte, une fois identifié (chargement).
            if (string.IsNullOrEmpty(nm)) return;
            if (!plans.Any(p => p.Name == nm)) { PlanNote(st, row, "other", Loc.Fmt("L.Planning.Auto.OtherChar", nm!)); return; }
        }
        // Anti-verrouillage : un planning sans plage utilisable ne déconnecte jamais (sinon activer la connexion auto sur un
        // planning vide couperait tout lancement manuel).
        var own = string.IsNullOrEmpty(nm) ? plans : plans.Where(p => p.Name == nm).DefaultIfEmpty(plans[0]).ToList();
        if (!own.Any(p => PlanningEval.HasUsableSlot(p.Cp))) { PlanNote(st, row, "noslot", Loc.T("L.Planning.Auto.NoSlotKeep")); return; }

        // Une plage reprend bientôt -> on reste connecté (EvaluatePlanning a arrêté le script, il le relancera).
        if (PlanStartsSoon(plans, now, PlanKeepOnlineMin))
        {
            st.DrainSince = 0;
            if (PlanNextStart(plans, now) is DateTime ns)
                PlanNote(st, row, "soon", Loc.Fmt("L.Planning.Auto.KeepOnline", PlanningText.When(ns, now)));
            return;
        }

        int pid = row.Pid ?? 0;
        bool inFight = pid > 0 && Stores.Launch.IsInFight((uint)pid);
        bool scriptUp = pid > 0 && _luaHosts.TryGetValue(pid, out var h) && h.Running;
        if (st.DrainSince == 0) st.DrainSince = t;
        bool waitCapped = t - st.DrainSince >= PlanDrainMaxMs;   // combat trop long / bloqué : on n'attend plus
        // Déconnexion DÉJÀ décidée pendant un combat (décompte arrivé à zéro / « Déconnecter maintenant ») : exécutée dès la
        // fin du combat — ou au plafond d'attente — sans reposer la question.
        if (st.DisconnectAfterFight)
        {
            if (!PlanningEval.CanDisconnectSession(st.AfterFightSession, PlanSessionOf(row), PlanningStore.All(), now))
            { st.DisconnectAfterFight = false; st.DrainSince = 0; return; }
            if (inFight && !waitCapped) return;
            DoPlanDisconnect(row, st, st.AfterFightStatus, string.IsNullOrEmpty(nm) ? row.Login : nm!, st.AfterFightEnd);
            return;
        }
        if ((inFight || scriptUp) && !waitCapped)
        {
            if (inFight) PlanNote(st, row, "fight", Loc.T("L.Planning.Auto.Draining"));
            return;   // combat en cours / script qui s'arrête : on patiente (borné)
        }
        EnqueuePlanDisconnect(row, st, string.IsNullOrEmpty(nm) ? row.Login : nm!, ours, plans, now, t);
    }

    // Plage active pour ce compte (persos voulus = `chars`) : prise en charge d'un compte en jeu, vérification d'un
    // lancement en cours, sinon lancement si hors ligne.
    private void PlanInSlot(AccountRow row, PlanConn st, List<string> chars, List<(string Name, CharPlanning Cp)> plans, DateTime now, long t)
    {
        st.DrainSince = 0;

        // Compte en jeu (hors lancement planifié en attente de vérification) sur un perso voulu : PRIS EN CHARGE -> son
        // crash est relancé par le planning et EvaluatePlanning attend la 1re carte. Même quand la plage est « suppressed »
        // (arrêt manuel puis relance à la main, mauvais perso puis correction) : Suppressed ne bloque QUE les lancements.
        if (row.Running && st.LaunchStartedAt <= 0 && !st.Managed
            && row.Pid is int ap && row.CharacterName is { Length: > 0 } an && chars.Contains(an))
        {
            st.Managed = true; st.ManagedSince = t;
            AppendLuaLine("planning", Loc.Fmt("L.Planning.Auto.Adopted", an), LuaLogLevel.Info, ap);
        }
        if (st.Suppressed) return;

        if (row.Running)
        {
            if (st.Managed && st.Attempts > 0 && t - st.ManagedSince > PlanStableMs) st.Attempts = 0;   // session stable : budget ré-armé
            if (st.LaunchStartedAt <= 0) return;   // en jeu (pris en charge ci-dessus) ou autre perso : EvaluatePlanning s'occupe du script
            // Le client en ligne n'est pas celui de NOTRE lancement (lancement manuel entre-temps) : pas de vérification
            // « mauvais perso » ni de délai sur lui ; il sera pris en charge comme un lancement manuel (ci-dessus).
            if (row.LaunchGen != st.LaunchGen) { st.LaunchStartedAt = 0; return; }
            string? nm = row.CharacterName;
            if (!string.IsNullOrEmpty(nm))
            {
                // Perso voulu — ou autre perso planifié de CE compte dont la plage démarre dans quelques minutes (jitter).
                if (chars.Contains(nm!) || plans.Any(p => p.Name == nm && PlanningEval.StartsWithin(p.Cp, now, p.Name, 2 * Math.Max(0, p.Cp.JitterMin) + 2)))
                {
                    st.LaunchStartedAt = 0; st.Managed = true; st.ManagedSince = t; st.GiveUpStreak = 0;
                    if (row.Pid is int p) AppendLuaLine("planning", Loc.Fmt("L.Planning.Auto.Connected", nm!), LuaLogLevel.Success, p);
                    return;
                }
                // Le client est entré sur un AUTRE perso (dernier perso joué du compte) : pas de script sur le mauvais perso.
                _accounts.StopForPlanning(row, Loc.T("L.Planning.Auto.StatusWrongChar"));
                st.LaunchStartedAt = 0; st.Managed = false; st.Suppressed = true;
                AudioService.PlayError();
                Toasts.Show(Loc.Fmt("L.Planning.Auto.WrongChar", row.Login, nm!, chars[0]), success: false);
                return;
            }
            if (t - st.LaunchStartedAt > PlanInGameTimeoutMs)
            {
                // Coupe le lancement (pid publié ou pas : la suite de LaunchCoreAsync tuera un client qui arriverait après).
                _accounts.StopForPlanning(row, Loc.T("L.Planning.Auto.StatusTimeout"));
                st.LaunchStartedAt = 0; st.NextTryAt = t + PlanRetryDelayMs;
                if (st.Attempts < PlanMaxAttempts) Toasts.Show(Loc.Fmt("L.Planning.Auto.Timeout", row.Login), success: false);
            }
            return;
        }

        // Hors ligne pendant la plage : lancement raté/crashé -> délai avant le prochain essai.
        if (st.LaunchStartedAt > 0 || st.Managed)
        {
            st.LaunchStartedAt = 0; st.Managed = false;
            st.NextTryAt = Math.Max(st.NextTryAt, t + PlanRetryDelayMs);
        }
        // Compat un bot tiers: pause demandée par le script (reconnect(h) / PLANNING) pas encore échue -> c'est sa relance
        // programmée (DriveScriptReconnects) qui reconnecte à l'échéance ; le planning ne la défait pas.
        if (ScriptReconnectHeld(row.Model.Id)) return;
        if (st.NeedsUser) return;   // déjà signalé : on attend un lancement manuel
        if (st.Attempts >= PlanMaxAttempts)
        {
            st.Suppressed = true;
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Planning.Auto.GaveUp", row.Login, PlanMaxAttempts), success: false);
            return;
        }
        // Jamais deux lancements du même compte : le précédent (même coupé par le délai « en jeu ») doit être revenu.
        if (st.InFlight && t - st.InFlightSince < PlanInFlightMaxMs) return;
        // Un lancement planifié à la fois (tous comptes), borné pour qu'un lancement bloqué ne fige pas tout le planning.
        bool busy = _planLaunchBusy && t - _planLastLaunchAt < PlanInGameTimeoutMs;
        if (t < st.NextTryAt || busy || t - _planLastLaunchAt < PlanLaunchSpacingMs) return;

        // Pré-requis d'un lancement SANS surveillance (aucun modal possible) : sinon on attend / on abandonne proprement.
        // Un compte en mode Socket n'a pas besoin de Dofus.exe (connexion clientless).
        if (!AccountsViewModel.UsesSocket(row) && !AccountsViewModel.DofusExeKnown())
        {
            st.Suppressed = true;
            Toasts.Show(Loc.T("L.Planning.Auto.NoDofus"), success: false);
            return;
        }
        if (MappingCatalog.MissingEssentials().Count > 0)
        {
            if (!st.Notified) { st.Notified = true; Toasts.Show(Loc.Fmt("L.Planning.Auto.WaitMapping", chars[0]), success: false, sound: false); }
            return;
        }
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi) && _accounts.Accounts.Any(a => a.Running && a != row))
        {
            if (!st.Notified) { st.Notified = true; Toasts.Show(Loc.Fmt("L.Planning.Auto.WaitMulti", chars[0]), success: false, sound: false); }
            return;
        }

        st.Attempts++;
        st.LaunchStartedAt = t;
        st.InFlight = true; st.InFlightSince = t;
        _planLaunchBusy = true;
        _planLastLaunchAt = t;
        Toasts.Show(Loc.Fmt("L.Planning.Auto.Connecting", row.Login, chars[0]), sound: false);
        _ = PlanLaunchAsync(row, st, t, chars[0]);
        // LaunchCoreAsync a incrémenté row.LaunchGen AVANT son premier await (exécution synchrone jusque-là) : c'est la
        // génération de NOTRE lancement (inchangée s'il a été refusé d'emblée -> compte hors ligne, rien à adopter).
        st.LaunchGen = row.LaunchGen;
    }

    private async Task PlanLaunchAsync(AccountRow row, PlanConn st, long startedAt, string character)
    {
        (bool Ok, bool NeedsShieldCode) r = (false, false);
        try { r = await _accounts.LaunchForPlanningAsync(row, character).ConfigureAwait(true); }
        catch { r = (false, false); }
        finally
        {
            // Ne libère que SES verrous : un lancement bloqué qui revient tard ne doit pas lever ceux d'un lancement plus récent.
            if (st.InFlightSince == startedAt) st.InFlight = false;
            if (_planLastLaunchAt == startedAt) _planLaunchBusy = false;
        }
        if (r.Ok) return;
        // Code du Bouclier exigé : chaque essai ferait envoyer un e-mail de code par Ankama -> on n'insiste pas sans
        // l'utilisateur. Indépendant de l'état du lancement (un tick a pu le reprendre entre l'échec et cette suite).
        if (r.NeedsShieldCode && !st.NeedsUser)
        {
            st.NeedsUser = true;
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Planning.Auto.NeedsShield", row.Login), success: false);
        }
        if (st.LaunchStartedAt != startedAt) return;   // état déjà repris (délai, arrêt, crash, tick)
        // Échec (auth, proxy...) : le toast d'erreur est déjà affiché par le pipeline de lancement.
        st.LaunchStartedAt = 0;
        st.NextTryAt = Environment.TickCount64 + PlanRetryDelayMs;
    }

    // ------------------------------------------------------------------ modale de déconnexion à compte à rebours

    private void EnqueuePlanDisconnect(AccountRow row, PlanConn st, string character, bool endOfSlot,
                                       List<(string Name, CharPlanning Cp)> plans, DateTime now, long t)
    {
        st.Pending = true;
        DateTime? next = PlanNextStart(plans, now);
        var p = new PendingPlanDisconnect
        {
            Session = PlanSessionOf(row),
            AccountId = row.Model.Id, Character = character, Login = row.Login, EndOfSlot = endOfSlot,
            Reason = Loc.T(endOfSlot ? "L.Planning.Pending.ReasonEnd" : "L.Planning.Pending.ReasonOut"),
            NextText = next is DateTime n ? Loc.Fmt("L.Planning.Pending.Next", PlanningText.When(n, now)) : Loc.T("L.Planning.Pending.NoNext"),
            // Statut DURABLE de la ligne compte : échéance absolue (« Me 08:00 »), un « demain » deviendrait faux après minuit.
            Status = next is DateTime n2 ? Loc.Fmt("L.Planning.Auto.StatusNext", PlanningText.Absolute(n2, now))
                                         : Loc.T(endOfSlot ? "L.Planning.Auto.StatusOff" : "L.Planning.Auto.StatusOffOut"),
            StartedAt = t, Deadline = t + PlanCountdownMs,
            SecondsLeft = (int)(PlanCountdownMs / 1000), Progress = 1,
        };
        PendingDisconnects.Add(p);
        OnPropertyChanged(nameof(HasManyPending));
        if (!PendingDisconnectVisible) { PendingDisconnectVisible = true; AudioService.PlayUi(AudioService.ModalIn); }
        if (row.Pid is int pid) AppendLuaLine("planning", Loc.Fmt("L.Planning.Pending.Log", PlanCountdownMs / 1000), LuaLogLevel.Warning, pid);
    }

    // À CHAQUE tick (300 ms) : décompte en place, déconnexion à l'échéance (après re-vérification complète).
    private void DrivePendingDisconnects()
    {
        if (PendingDisconnects.Count == 0) return;
        if (!LicenseService.IsAuthorized)   // killswitch : la modale serait sous l'écran de licence -> ne rien déclencher
        {
            foreach (var p in PendingDisconnects.Where(x => !x.Done).ToList()) DropPending(p.AccountId);
            return;
        }
        long t = Environment.TickCount64;
        foreach (var p in PendingDisconnects.Where(x => !x.Done).ToList())
        {
            long left = p.Deadline - t;
            p.SecondsLeft = (int)Math.Max(0, Math.Ceiling(left / 1000.0));
            p.Progress = Math.Clamp(left / (double)Math.Max(1, p.Deadline - p.StartedAt), 0, 1);
            if (left <= 0) ExecutePlanDisconnect(p);
        }
    }

    private void ExecutePlanDisconnect(PendingPlanDisconnect p)
    {
        FinishPending(p);
        if (!_planConn.TryGetValue(p.AccountId, out var st)) return;
        st.Pending = false;
        var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == p.AccountId);
        // Plus d'actualité : compte déjà parti, exemption, plage revenue entre-temps.
        if (row is null || !row.Running || st.Override || st.OccKey.Length > 0
            || row.ReconnectInPlace || _accounts.IsRelaunching(row)
            || !PlanningEval.CanDisconnectSession(p.Session, PlanSessionOf(row), PlanningStore.All(), DateTime.Now)) return;
        long t = Environment.TickCount64;
        if (row.Pid is int pid && Stores.Launch.IsInFight((uint)pid))
        {
            // Agressé pendant le décompte : la décision est prise, on déconnecte À LA FIN DU COMBAT, sans reposer la question
            // (plafond d'attente compté depuis le PREMIER début d'attente, jamais remis à zéro -> un combat bloqué n'empêche
            // pas indéfiniment la déconnexion).
            if (st.DrainSince == 0) st.DrainSince = t;
            if (t - st.DrainSince < PlanDrainMaxMs)
            {
                st.DisconnectAfterFight = true; st.AfterFightStatus = p.Status; st.AfterFightEnd = p.EndOfSlot;
                st.AfterFightSession = p.Session;
                Toasts.Show(Loc.Fmt("L.Planning.Auto.AfterFight", p.Character), sound: false);
                AppendLuaLine("planning", Loc.Fmt("L.Planning.Auto.AfterFight", p.Character), LuaLogLevel.Warning, pid);
                return;
            }
        }
        DoPlanDisconnect(row, st, p.Status, p.Character, p.EndOfSlot);
    }

    private static PlanningSession PlanSessionOf(AccountRow row)
        => new(row.Model.Id, row.Pid ?? 0, row.LaunchGen, Volatile.Read(ref row.SocketSessionSeq), row.CharacterName);

    private void DoPlanDisconnect(AccountRow row, PlanConn st, string status, string character, bool endOfSlot)
    {
        _accounts.StopForPlanning(row, status);
        st.Managed = false; st.LaunchStartedAt = 0; st.DrainSince = 0; st.OutNote = ""; st.DisconnectAfterFight = false;
        Toasts.Show(Loc.Fmt(endOfSlot ? "L.Planning.Auto.Disconnected" : "L.Planning.Auto.DisconnectedOut", character), sound: false);
    }

    // Retire (sans suite) la déconnexion en attente d'un compte : plage revenue, compte parti, arrêt manuel...
    private void DropPending(string accountId)
    {
        if (_planConn.TryGetValue(accountId, out var st)) st.Pending = false;
        foreach (var p in PendingDisconnects.Where(x => !x.Done && x.AccountId == accountId).ToList()) FinishPending(p);
    }

    // Marque traité ; le DERNIER élément reste affiché le temps du fondu de la carte (pas de saut de hauteur visible).
    private void FinishPending(PendingPlanDisconnect p)
    {
        p.Done = true;
        if (PendingDisconnects.Any(x => !x.Done))
        {
            PendingDisconnects.Remove(p);
            OnPropertyChanged(nameof(HasManyPending));
            return;
        }
        PendingDisconnectVisible = false;
        OnPropertyChanged(nameof(HasManyPending));
        DispatcherTimer.RunOnce(() =>
        {
            foreach (var x in PendingDisconnects.Where(x => x.Done).ToList()) PendingDisconnects.Remove(x);
            OnPropertyChanged(nameof(HasManyPending));
        }, TimeSpan.FromMilliseconds(240));
    }

    [RelayCommand]
    private void CancelPlanDisconnect(PendingPlanDisconnect? p)
    {
        if (p is null || p.Done) return;
        FinishPending(p);
        if (_planConn.TryGetValue(p.AccountId, out var st))
        {
            st.Pending = false; st.Override = true; st.OverrideOcc = st.OccKey; st.OverrideKeepUntil = 0;
            st.DrainSince = 0; st.DisconnectAfterFight = false;
        }
        Toasts.Show(Loc.Fmt("L.Planning.Pending.Cancelled", p.Character), sound: false);
    }

    [RelayCommand]
    private void CancelAllPlanDisconnects()
    {
        foreach (var p in PendingDisconnects.Where(x => !x.Done).ToList()) CancelPlanDisconnect(p);
    }

    [RelayCommand]
    private void PlanDisconnectNow(PendingPlanDisconnect? p)
    {
        if (p is null || p.Done) return;
        p.Deadline = Environment.TickCount64;
        ExecutePlanDisconnect(p);
    }

    // ------------------------------------------------------------------ lignes « hors-ligne · géré par le planning »

    // Tick throttlé (après DrivePlanningConnect / EvaluatePlanning) : lignes des persos en connexion auto hors jeu, badge
    // « planning » des comptes, horloge de la modale. Affichage seul (ne dépend pas de la licence).
    private void RefreshPlannedOffline()
    {
        var now = DateTime.Now;
        var auto = PlanningStore.All().Where(kv => kv.Value.Enabled && kv.Value.AutoConnect).ToList();
        var online = new HashSet<string>(
            Bots.Where(b => b.Pid > 0 && b.Row.Running && !string.IsNullOrEmpty(b.Row.CharacterName)).Select(b => b.Row.CharacterName!),
            StringComparer.Ordinal);

        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, cp) in auto)
        {
            if (online.Contains(name)) continue;   // en jeu : c'est sa ligne de bot qui s'affiche
            keep.Add(name);
            var r = PlannedOffline.FirstOrDefault(x => x.Character == name);
            if (r is null)
            {
                // Le perso revient dans la liste alors que sa modale est ouverte : on réutilise l'objet affiché par la modale
                // (sinon son en-tête et « Connecter maintenant » resteraient figés sur l'ancien état).
                r = OfflinePlanningVisible && OfflinePlanningTarget?.Character == name ? OfflinePlanningTarget! : new PlannedOfflineRow(name);
                PlannedOffline.Add(r);
            }
            FillPlannedRow(r, name, cp, now);
        }
        bool changed = false;
        for (int i = PlannedOffline.Count - 1; i >= 0; i--)
            if (!keep.Contains(PlannedOffline[i].Character)) { PlannedOffline.RemoveAt(i); changed = true; }
        if (changed || keep.Count > 0)
        {
            OnPropertyChanged(nameof(HasPlannedOffline)); OnPropertyChanged(nameof(PlannedOfflineCountText)); OnPropertyChanged(nameof(ShowNoBotsHint));
        }
        // Modale ouverte sur un perso qui vient d'entrer en jeu (ou retiré de la connexion auto) : en-tête à jour.
        if (OfflinePlanningVisible && OfflinePlanningTarget is { } tgo && !keep.Contains(tgo.Character))
        {
            bool isOnline = online.Contains(tgo.Character);
            tgo.StatusText = Loc.T(isOnline ? "L.Planning.Offline.Online" : "L.Planning.Offline.NotAuto");
            tgo.NextText = ""; tgo.NextShort = ""; tgo.CanConnect = false; tgo.IsConnecting = false; tgo.NeedsAttention = false;
        }

        // Badge « Planning » sur la page Comptes (sans nom de perso : les infobulles ne sont pas floutées en mode stream).
        foreach (var acc in _accounts.Accounts)
        {
            var mine = auto.Where(kv => kv.Value.AccountId == acc.Model.Id).ToList();
            acc.PlanningManaged = mine.Count > 0;
            if (mine.Count == 0) { acc.PlanningTip = ""; continue; }
            DateTime? next = null;
            foreach (var (n, c) in mine)
                if (PlannedOffline.FirstOrDefault(x => x.Character == n)?.NextAt is DateTime d && (next is null || d < next)) next = d;
            acc.PlanningTip = next is DateTime nx ? Loc.Fmt("L.Planning.Offline.AccountTipNext", PlanningText.When(nx, now)) : Loc.T("L.Planning.Offline.AccountTip");
        }

        if (OfflinePlanningVisible && OfflinePlanningTarget is { } tg && PlanningStore.TryGet(tg.Character, out var tcp))
            OfflinePlanning.UpdateClock(now, tcp.Enabled && PlanningEval.IsActiveNow(tcp, now, tg.Character));
    }

    // Empreinte des entrées de NextStart (humanisation + identités et bornes des plages). AOT-safe.
    private static int PlanSigHash(CharPlanning cp)
    {
        var h = new HashCode();
        h.Add(cp.JitterMin);
        h.Add(cp.Slots.Count);
        foreach (var s in cp.Slots) { h.Add(s.Id); h.Add(s.Days); h.Add(s.StartMin); h.Add(s.EndMin); }
        return h.ToHashCode();
    }

    private void FillPlannedRow(PlannedOfflineRow r, string name, CharPlanning cp, DateTime now)
    {
        var row = string.IsNullOrEmpty(cp.AccountId) ? null : _accounts.Accounts.FirstOrDefault(a => a.Model.Id == cp.AccountId);
        _planConn.TryGetValue(cp.AccountId ?? "", out var st);
        r.AccountId = cp.AccountId ?? "";
        r.Login = row?.Login ?? "";
        if (r.ClassIcon is null && cp.BreedIndex is int bi)
        {
            // Le bundle des symboles de classe n'est chargé qu'au 1er lancement de compte de la session : après un redémarrage
            // sans aucun compte lancé, les lignes hors ligne n'auraient QUE l'avatar générique. On le charge nous-mêmes (une
            // fois, en tâche de fond) ; l'icône apparaît au tick suivant.
            if (!Stores.ClassIcons.Loaded)
            {
                if (!_classIconsKicked) { _classIconsKicked = true; _ = Task.Run(Stores.ClassIcons.EnsureLoaded); }
            }
            else r.ClassIcon = Stores.ClassIcons.Get(bi, cp.GenderIndex ?? 0);
        }
        r.LevelText = cp.Level is int lv ? Loc.Fmt("L.Planning.Offline.Level", lv) : "";

        bool inSlot = PlanningEval.IsActiveNow(cp, now, name);
        // NextStart est coûteux : recalculé une fois par minute, ou tout de suite si les plages / l'humanisation ont changé
        // (édition dans la modale -> en-tête, ligne et badge à jour en ~1,5 s).
        long minute = now.Ticks / TimeSpan.TicksPerMinute;
        int sig = PlanSigHash(cp);
        if (r.NextMinute != minute || r.NextSig != sig) { r.NextMinute = minute; r.NextSig = sig; r.NextAt = PlanningEval.NextStart(cp, now, name); }

        string status;
        bool attention = false, connecting = false;
        if (row is null) { status = Loc.T("L.Planning.Offline.Unbound"); attention = true; }
        // Compte lancé mais perso pas encore identifié (auth, chargement) : « connexion en cours », pas « autre perso ».
        else if (row.Running && string.IsNullOrEmpty(row.CharacterName)) { status = Loc.T("L.Planning.Offline.Connecting"); connecting = true; }
        else if (st?.NeedsUser == true) { status = Loc.T("L.Planning.Offline.NeedsUser"); attention = true; }
        else if (row.Running) status = Loc.T("L.Planning.Offline.OtherChar");   // nom connu et différent (sinon il serait « en jeu »)
        else if (inSlot && st?.Suppressed == true) status = Loc.T("L.Planning.Offline.Skipped");
        else if (inSlot) { status = Loc.T("L.Planning.Offline.SlotNow"); connecting = true; }
        else if (!PlanningEval.HasUsableSlot(cp)) { status = Loc.T("L.Planning.Offline.NoSlot"); attention = true; }
        else status = Loc.T("L.Planning.Offline.Idle");

        r.StatusText = status;
        r.NeedsAttention = attention;
        r.IsConnecting = connecting;
        r.CanConnect = row is not null && !row.Running;
        r.NextShort = !inSlot && r.NextAt is DateTime nx ? PlanningText.When(nx, now) : "";
        r.NextText = r.NextShort.Length > 0 ? Loc.Fmt("L.Planning.Offline.Next", r.NextShort) : "";
        r.TipText = string.IsNullOrEmpty(r.NextText) ? status + "\n" + Loc.T("L.Planning.Offline.ClickHint")
                                                      : status + "\n" + r.NextText + "\n" + Loc.T("L.Planning.Offline.ClickHint");
    }

    [RelayCommand]
    private void OpenPlanned(PlannedOfflineRow? r)
    {
        if (r is null || !PlanningStore.TryGet(r.Character, out var cp)) return;
        OfflinePlanningTarget = r;
        OfflinePlanning.Load(r.Character);
        var now = DateTime.Now;
        OfflinePlanning.UpdateClock(now, cp.Enabled && PlanningEval.IsActiveNow(cp, now, r.Character));
        OfflineEditorEnabled = true;
        OfflinePlanningVisible = true;
        AudioService.PlayUi(AudioService.ModalIn);
    }

    [RelayCommand]
    private void CloseOfflinePlanning()
    {
        if (!OfflinePlanningVisible) return;
        OfflinePlanning.CommitPendingRenames();   // nom de script en cours de frappe dans la modale : validé, pas perdu
        OfflinePlanningVisible = false;
        DispatcherTimer.RunOnce(() => { if (!OfflinePlanningVisible) OfflineEditorEnabled = false; }, TimeSpan.FromMilliseconds(250));
        AudioService.PlayUi(AudioService.ModalOut);
        // Même CharPlanning partagé : si le bot sélectionné est ce perso (connecté entre-temps), l'onglet est resynchronisé.
        if (OfflinePlanningTarget?.Character is { } name && SelectedBot?.Row.CharacterName == name) Planning.Load(name);
        RefreshPlannedOffline();
    }

    /// <summary>« Connecter maintenant » (ligne hors-ligne ou modale) : lancement MANUEL direct en jeu, exempté de la
    /// déconnexion hors plage jusqu'à la prochaine plage (c'est une demande explicite de l'utilisateur).</summary>
    [RelayCommand]
    private async Task ConnectPlannedNow(PlannedOfflineRow? r)
    {
        r ??= OfflinePlanningTarget;
        if (r is null) return;
        var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == r.AccountId);
        if (row is null || row.Running) return;
        // Ferme la modale AVANT : les modals du lancement (Dofus.exe, protocole, progression) sont dessinés dessous.
        if (OfflinePlanningVisible) CloseOfflinePlanning();
        var st = PlanState(row.Model.Id);
        st.Override = true; st.OverrideOcc = st.OccKey; st.DisconnectAfterFight = false;
        st.OverrideKeepUntil = Environment.TickCount64 + PlanConnectNowHoldMs;
        DropPending(row.Model.Id);
        DropScriptReconnect(row.Model.Id);   // lancement manuel : la relance programmée par un script est caduque
        await _accounts.LaunchInGameForTeamAsync(row, r.Character).ConfigureAwait(true);   // Socket : entre sur CE perso
    }

    // ------------------------------------------------------------------ rechargement du script depuis son fichier

    // Début de plage : si le script assigné vient d'un FICHIER, on recharge son contenu (trajet modifié sur disque pris en
    // compte). Garde-fous : .lua, <= 512 Ko, fichier réellement présent en local (un fichier cloud « à la demande » serait
    // téléchargé en bloquant l'UI) ; en cas de souci on garde le corps figé.
    private void ReloadSlotScriptFromFile(CharPlanning cp, int slot, int pid)
    {
        if (slot < 0 || slot >= cp.Slots.Count) return;
        string name = cp.Slots[slot].Script;
        if (string.IsNullOrWhiteSpace(name)) return;
        var sc = cp.Scripts.FirstOrDefault(x => x.Name == name);
        if (sc is null || string.IsNullOrWhiteSpace(sc.SourcePath)) return;
        try
        {
            var fi = new FileInfo(sc.SourcePath);
            const int RecallOnOpen = 0x40000, RecallOnDataAccess = 0x400000;   // attributs Windows des fichiers cloud non hydratés
            if (!fi.Exists || fi.Length > 512 * 1024
                || ((int)fi.Attributes & (RecallOnOpen | RecallOnDataAccess | (int)FileAttributes.Offline)) != 0
                || !string.Equals(fi.Extension, ".lua", StringComparison.OrdinalIgnoreCase)) return;
            string body = File.ReadAllText(fi.FullName);
            if (string.IsNullOrWhiteSpace(body) || body == sc.Body) return;
            sc.Body = body;
            PlanningStore.Save();
            AppendLuaLine("planning", Loc.Fmt("L.Planning.Auto.Reloaded", sc.Name, fi.Name), LuaLogLevel.Info, pid);
        }
        catch { /* fichier verrouillé / illisible : on garde le corps figé */ }
    }
}
