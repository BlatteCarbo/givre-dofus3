using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.License;

namespace GivreClone.App.Services;

/// <summary>État du lien avec le serveur de licence, pour l'UI (pastille de la barre de titre).</summary>
public enum LicenseLinkState
{
    /// <summary>Session vivante (ou reprise silencieuse terminée).</summary>
    Online,
    /// <summary>Reprise silencieuse en cours (nouveau /auth après une coupure qui a dépassé la grâce serveur).</summary>
    Resuming,
    /// <summary>Reprise en attente du réseau.</summary>
    WaitingNetwork,
    /// <summary>Reprise en échec répété (~5) : botting suspendu, nouvel essai automatique en fond + bouton « Reconnecter ».</summary>
    Stalled,
    /// <summary>Garde-fou déclenché (reprises trop fréquentes) : botting suspendu, on attend un clic sur « Reconnecter ».</summary>
    NeedsUser,
    /// <summary>Serveur injoignable depuis plusieurs minutes (repli HTTP en échec) : botting suspendu, licence gardée, nouvel
    /// essai automatique ; au retour du réseau, la session continue ou est reprise (lapsed).</summary>
    Offline,
}

// Reprise SILENCIEUSE de session (contrat serveur « lapsed », docs/web-reponse-lapsed-1.1.7.md) : quand une coupure réseau / une
// mise en veille dépasse la grâce du serveur, la réponse à NOTRE requête (hello/beat WS, POST /heartbeat, /mapping, /scripts)
// est SESSION_EXPIRED + lapsed:true. Sur CE seul marqueur (jamais sur une trame poussée, jamais sur un autre code), on refait un
// /auth avec la même clé / le même HWID / le même client.name au lieu de renvoyer l'utilisateur à l'écran de clé.
//  - Ancien jeton lâché TOUT DE SUITE (LicenseClient.Abandon : plus aucune requête ni trame avec lui, réponses ignorées).
//  - Nouveau client authentifié dans une variable LOCALE puis échangé atomiquement (jamais de _client non authentifié : les
//    ~160 gardes HasPermission resteraient fausses pendant le /auth).
//  - JAMAIS IsAuthorized=false, PermissionsChanged ni purge du mapping pendant la reprise (cascades : scripts stoppés, combat
//    désactivé ET enregistré, comptes proxy coupés, décodage MITM mort jusqu'au redémarrage). En échec prolongé, le botting est
//    SUSPENDU par un drapeau séparé (IsSuspended -> CanBot) : injection bloquée + scripts arrêtés, licence intacte.
//  - Garde-fous : une seule reprise à la fois (WS + HTTP), attente du réseau, 0-5 s aléatoires avant le 1er essai, backoff 2/5/15/60 s
//    jitteré, ≤ 6 reprises silencieuses réussies par heure, nouveau marqueur < 60 s après une reprise -> on demande à l'utilisateur.
//  - Jamais de reprise après kill switch / control_kill / fin de session voulue / déconnexion utilisateur (ForbidResume).
public static partial class LicenseService
{
    /// <summary>État courant du lien licence (pastille UI).</summary>
    public static LicenseLinkState LinkState { get; private set; } = LicenseLinkState.Online;
    /// <summary>Changement d'état du lien licence (thread de fond -> marshaller vers l'UI).</summary>
    public static event Action<LicenseLinkState>? LinkStateChanged;

    /// <summary>Botting SUSPENDU sans déconnecter la licence (pas d'IsAuthorized=false : cascades) : reprise de session bloquée
    /// (Stalled / NeedsUser) OU lien perdu (repli HTTP en échec, Offline).</summary>
    public static bool IsSuspended => _resumeSuspended || _linkDown || _controlUnverified || _signingBlocked;
    private static volatile bool _resumeSuspended;
    private static volatile bool _linkDown;
    private static volatile bool _controlUnverified;   // contrôle de version injoignable pendant une coupure totale (récupérable)
    /// <summary>Le bot peut agir : licence autorisée ET botting non suspendu. Porte des injections et du planning.</summary>
    public static bool CanBot => IsAuthorized && !IsSuspended;

    /// <summary>TESTS uniquement : fabrique de LicenseClient (sinon le vrai serveur).</summary>
    internal static Func<LicenseClient>? ClientFactoryOverride { get; set; }
    private static LicenseClient NewClient() =>
        ClientFactoryOverride?.Invoke() ?? new LicenseClient(AppSecrets.BaseUrl, AppSecrets.ServerPublicKeyHex, ClientName, AppVersion);

    private static readonly object _resumeLock = new();
    private static int _resumeState;                     // 0 aucune, 1 en cours, 2 attend l'utilisateur (Interlocked)
    private static volatile bool _resumeForbidden;       // collant : kill / fin voulue / déconnexion ; levé par un login manuel réussi
    private static CancellationTokenSource? _resumeCts;
    private static int _sessionGen;                      // ++ à chaque login manuel / déconnexion -> une reprise périmée n'est pas validée
    private static readonly Queue<long> _resumeOkTicks = new();   // reprises silencieuses réussies (fenêtre glissante 1 h)
    private static long _lastResumeOkTick;
    private static TaskCompletionSource<bool>? _retryNow;         // « Reconnecter » pendant l'attente du backoff -> essai immédiat
    // Après ~5 échecs (Stalled), un essai toutes les 5 min : le budget /auth du serveur est ~30/h PAR IP (CGNAT partagé).
    private static readonly int[] ResumeBackoffS = { 2, 5, 15, 60, 300 };
    private const int ResumeStallAfter = 5, ResumeMaxPerHour = 6, ResumeMaxAttemptsPerHour = 20;
    private const int ResumeRateLimitedMinS = 300;                 // RATE_LIMITED : jamais de nouvel essai avant 5 min
    private const long ResumeLoopGuardMs = 60_000;
    private static readonly Queue<long> _resumeAttemptTicks = new(); // /auth de reprise AUTOMATIQUES tentés (fenêtre glissante 1 h)

    // Codes du /auth qui mettent fin à la reprise (licence réellement inutilisable) : déconnexion normale avec le message du
    // serveur. Décidé sur le CODE signé, jamais sur le statut HTTP (KEY_LOCKED est aussi un 429 et il est terminal).
    private static bool IsAuthTerminal(string code) => code is "INVALID_KEY" or "USER_BANNED" or "KEY_REVOKED" or "KEY_PAUSED"
        or "KEY_EXPIRED" or "KEY_LOCKED" or "HWID_MISMATCH";

    // force : ré-émet un état identique (Stalled après chaque nouvel échec -> le bouton « Reconnecter » réapparaît après un clic).
    private static void SetLinkState(LicenseLinkState s, bool force = false)
    {
        // « En ligne » alors que le serveur reste injoignable (lien perdu / contrôle non vérifiable) : la pastille le dit.
        // (Jamais après une fin de session : ForbidResume est posé avant CancelResume -> la pastille disparaît avec la session.)
        if (s == LicenseLinkState.Online && (_linkDown || _controlUnverified) && IsAuthorized && !_resumeForbidden) s = LicenseLinkState.Offline;
        if (LinkState == s && !force) return;
        LinkState = s;
        try { LinkStateChanged?.Invoke(s); } catch { /* abonné UI : ne casse jamais la reprise */ }
    }

    /// <summary>Marqueur lapsed reçu (thread de fond). LicenseClient ne le signale que pour une réponse SIGNÉE, FRAÎCHE, à
    /// NOTRE requête (nonce vérifié), avec code SESSION_EXPIRED et lapsed == true (booléen strict).</summary>
    private static void OnLapsed(LicenseClient client, int source)
    {
        Diag.Log(309, source);
        if (_resumeForbidden) { Diag.Log(414, 1); return; }
        if (client.TerminalError is not null) { Diag.Log(414, 4); return; }   // kick déjà reçu (attente terminate_in) : il prime
        if (!ReferenceEquals(client, _client)) { Diag.Log(414, 3); return; }
        if (Interlocked.CompareExchange(ref _resumeState, 1, 0) != 0) { Diag.Log(414, 2); return; }   // une seule reprise (WS + HTTP)
        client.Abandon();   // ancien jeton lâché tout de suite (idempotent si le WS l'a déjà fait)
        Logger.Info("Session de licence expirée après coupure (lapsed) — reprise silencieuse");

        long now = Environment.TickCount64;
        int okLastHour;
        lock (_resumeOkTicks)
        {
            while (_resumeOkTicks.Count > 0 && now - _resumeOkTicks.Peek() > 3_600_000) _resumeOkTicks.Dequeue();
            okLastHour = _resumeOkTicks.Count;
        }
        long lastOk = Interlocked.Read(ref _lastResumeOkTick);
        if (lastOk != 0 && now - lastOk < ResumeLoopGuardMs) { EnterNeedsUser(3); return; }   // re-expire juste après une reprise
        if (okLastHour >= ResumeMaxPerHour) { EnterNeedsUser(2); return; }                   // budget /auth (30/h/IP, CGNAT)
        StartResumeLoop(manual: false);
    }

    private static void StartResumeLoop(bool manual)
    {
        CancellationTokenSource cts;
        lock (_resumeLock)
        {
            try { _resumeCts?.Cancel(); } catch { }
            _resumeCts = cts = new CancellationTokenSource();
        }
        int gen = Volatile.Read(ref _sessionGen);
        Diag.Log(410, manual ? 1 : 0);
        SetLinkState(LicenseLinkState.Resuming);
        _ = Task.Run(() => ResumeLoopAsync(gen, manual, cts.Token));
    }

    private static async Task ResumeLoopAsync(int gen, bool manual, CancellationToken ct)
    {
        StopHeartbeat();   // ancien WS / repli HTTP, télémétrie et minuteur d'échéance : tous liés à l'ancien jeton
        var key = _lastKey;
        if (string.IsNullOrEmpty(key)) { EndSessionFromResume(Loc.T("L.Auth.SessionEnded"), "SESSION_EXPIRED"); return; }
        int failures = 0;
        bool done = false, userConsent = manual;   // userConsent : le 1er essai d'une reprise manuelle ne consomme pas le budget
        try
        {
            await WaitForNetworkAsync(ct).ConfigureAwait(false);
            // Après une panne serveur > 180 s, TOUS les clients expirent en même temps : étaler les /auth.
            if (!manual) await Task.Delay(Random.Shared.Next(0, 5001), ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested && !_resumeForbidden && gen == Volatile.Read(ref _sessionGen))
            {
                await WaitForNetworkAsync(ct).ConfigureAwait(false);
                if (!userConsent && !TakeAttemptBudget()) { done = true; EnterNeedsUser(4); return; }   // budget /auth horaire épuisé
                userConsent = false;
                var fresh = NewClient();
                bool rateLimited = false;
                try
                {
                    // Même clé, même HWID (mis en cache), même client.name. Requête signée refusée pour dérive d'horloge : le nouvel
                    // essai (après recalage) consomme le budget horaire des /auth automatiques.
                    await fresh.AuthenticateAsync(key!, ct, retryGate: TakeAttemptBudget).ConfigureAwait(false);
                    NoteKeyBinding(fresh);
                    Diag.Log(411, 1, 0, failures + 1);
                    done = true;
                    CommitResume(fresh, gen, manual);
                    return;
                }
                catch (LicenseServerException le) when (IsAuthTerminal(le.Code))
                {
                    try { fresh.Dispose(); } catch { }
                    Diag.Log(411, 0, Diag.Err(le.Code), failures + 1);
                    done = true;
                    EndSessionFromResume(Localize(le), le.Code);   // l'utilisateur apprend POURQUOI (message + raison du /auth)
                    return;
                }
                catch (LicenseServerException le) when (le.Code == "SIGNATURE_REJECTED")
                {
                    // Requête signée refusée : jamais « clé invalide », jamais de boucle. Dérive recalée mais nouvel essai refusé
                    // par le budget horaire -> on attend un clic ; tout autre motif (ou 2e dérive) -> fin de la reprise avec le
                    // message de signature / d'horloge.
                    try { fresh.Dispose(); } catch { }
                    Diag.Log(411, 0, Diag.Err(le.Code), failures + 1);
                    done = true;
                    if (le.Reason == "drift" && !le.ClockWrong) { EnterNeedsUser(4); return; }
                    EndSessionFromResume(Localize(le), le.Code);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { try { fresh.Dispose(); } catch { } return; }
                catch (Exception ex)
                {
                    // RATE_LIMITED, INTERNAL, code inconnu, réseau, signature, horloge : jamais de déconnexion, on réessaie.
                    try { fresh.Dispose(); } catch { }
                    failures++;
                    rateLimited = ex is LicenseServerException { Code: "RATE_LIMITED" };
                    Diag.Log(411, 0, ex is LicenseServerException l2 ? Diag.Err(l2.Code) : 0, failures);
                    if (failures >= ResumeStallAfter) EnterStalled(first: failures == ResumeStallAfter);
                }
                var s = ResumeBackoffS[Math.Min(failures - 1, ResumeBackoffS.Length - 1)];
                if (rateLimited) s = Math.Max(s, ResumeRateLimitedMinS);
                var wait = TimeSpan.FromMilliseconds(s * 1000 * (0.8 + 0.4 * Random.Shared.NextDouble()));
                var retryNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _retryNow, retryNow);
                // « Reconnecter » pendant l'attente = consentement explicite : cet essai-là ne consomme pas le budget.
                var first = await Task.WhenAny(Task.Delay(wait, ct), retryNow.Task).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (first == retryNow.Task && retryNow.Task.Result) userConsent = true;
            }
        }
        catch (OperationCanceledException) { /* reprise annulée (login manuel, déconnexion, kill) */ }
        catch (Exception e)
        {
            Logger.Warn($"Reprise de session interrompue : {e.GetType().Name}");
            // Jamais d'état bloqué « reprise en cours » sans boucle derrière (CanBot resterait vrai, sans battement) : pause +
            // bouton. Pas après un commit (session déjà reprise) ni si la reprise a été annulée / interdite entre-temps.
            if (!done && !ct.IsCancellationRequested && !_resumeForbidden && gen == Volatile.Read(ref _sessionGen)) EnterNeedsUser(5);
        }
    }

    // Budget des /auth de reprise AUTOMATIQUES (réussis ou non) sur l'heure glissante. false = épuisé (on attend un clic).
    private static bool TakeAttemptBudget()
    {
        long now = Environment.TickCount64;
        lock (_resumeAttemptTicks)
        {
            while (_resumeAttemptTicks.Count > 0 && now - _resumeAttemptTicks.Peek() > 3_600_000) _resumeAttemptTicks.Dequeue();
            if (_resumeAttemptTicks.Count >= ResumeMaxAttemptsPerHour) return false;
            _resumeAttemptTicks.Enqueue(now);
            return true;
        }
    }

    // Échange ATOMIQUE vers le client fraîchement authentifié, puis redémarrage du temps réel avec le NOUVEAU jeton.
    private static void CommitResume(LicenseClient fresh, int gen, bool manual)
    {
        LicenseClient? old;
        LicenseServerException? killed = null;
        bool stale = false;
        // Section ATOMIQUE vis-à-vis de ForbidResume (qui pose son drapeau sous le même verrou) : soit la reprise est validée ET
        // son battement démarré avant un kill / une déconnexion (qui nettoient alors le NOUVEAU client), soit elle est refusée.
        lock (_resumeLock)
        {
            old = _client;
            if (_resumeForbidden || gen != Volatile.Read(ref _sessionGen)) stale = true;
            // Kick reçu sur l'ancien client juste avant son abandon (attente terminate_in) : il prime sur la reprise.
            else if (old?.TerminalError is { } te) killed = te;
            else
            {
                if (old is not null) fresh.SeedSeenCommandsFrom(old);   // une commande re-livrée après la reprise n'est pas rejouée
                Volatile.Write(ref _client, fresh);
                // Accusés jamais partis de l'ancienne session : le serveur ne les a jamais vus -> à renvoyer sur la nouvelle.
                // (Accusés en file ou en vol : plus liés à un client ; renvoyés sur le nouveau socket à sa connexion.)
                _resumeSuspended = false;
                Interlocked.Exchange(ref _resumeState, 0);
                StartHeartbeat();   // nouveau socket + hello avec le NOUVEAU jeton ; télémétrie ; échéances
                if (!MappingCatalog.IsReady) _ = MappingCatalog.EnsureFetchedAsync();   // la clé du mapping dérive du nouveau jeton
            }
        }
        if (stale || killed is not null)
        {
            // Déconnexion / login manuel / kill arrivé pendant le /auth : pas de session fantôme côté panel.
            _ = Task.Run(async () => { try { await fresh.LogoutAsync().ConfigureAwait(false); } catch { } finally { fresh.Dispose(); } });
            if (killed is not null) EndSessionFromResume(Localize(killed), killed.Code);
            return;
        }
        // Libéré plus tard : une requête encore en vol sur l'ancien client ne doit pas tomber sur un HttpClient disposé.
        if (old is not null) _ = Task.Delay(30_000).ContinueWith(_ => { try { old.Dispose(); } catch { } }, TaskScheduler.Default);

        long now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastResumeOkTick, now);
        int okLastHour;
        lock (_resumeOkTicks) { if (!manual) _resumeOkTicks.Enqueue(now); okLastHour = _resumeOkTicks.Count; }
        Diag.Log(412, okLastHour, manual ? 1 : 0);
        Logger.Info("Session de licence reprise");
        SetLinkState(LicenseLinkState.Online);
        // Abonnés UI / scripts : une exception chez eux ne doit pas laisser croire que la reprise a échoué (elle est faite).
        try { RaisePermissionsChanged(notifyDelta: true); } catch (Exception e) { Logger.Warn($"Reprise : PermissionsChanged {e.GetType().Name}"); }   // licence fraîche du /auth
        _ = FlushPendingScriptsAsync();
    }

    // ~5 échecs transitoires : botting SUSPENDU, bouton « Reconnecter » ; la reprise continue en fond au plafond du backoff.
    // Ré-émis après CHAQUE échec suivant (un clic masque le bouton le temps de l'essai).
    private static void EnterStalled(bool first)
    {
        if (first) Diag.Log(413, 1);
        _resumeSuspended = true;
        SetLinkState(LicenseLinkState.Stalled, force: true);
    }

    // Garde-fou (re-expiration < 60 s après une reprise, ou quota horaire) : aucune tentative automatique, on attend un clic.
    private static void EnterNeedsUser(int reason)
    {
        Diag.Log(413, reason);
        Interlocked.Exchange(ref _resumeState, 2);
        _resumeSuspended = true;   // AVANT StopHeartbeat (qui lève le lien perdu) : jamais d'instant « botting permis »
        StopHeartbeat();
        SetLinkState(LicenseLinkState.NeedsUser);
    }

    /// <summary>Bouton « Reconnecter » de la pastille : essai immédiat avec la clé en mémoire (reprise en échec, ou garde-fou).
    /// Ne passe PAS par l'écran de clé ni par SaveLoginPreference.</summary>
    public static void RetryResumeNow()
    {
        if (_resumeForbidden) return;
        if (Volatile.Read(ref _resumeState) == 1) { Volatile.Read(ref _retryNow)?.TrySetResult(true); return; }   // coupe l'attente
        if (Interlocked.CompareExchange(ref _resumeState, 1, 2) == 2) StartResumeLoop(manual: true);             // consentement explicite
    }

    // /auth de reprise refusé pour de bon : déconnexion normale (écran de clé avec le message du serveur).
    private static void EndSessionFromResume(string msg, string code)
    {
        ForbidResume();
        IsAuthorized = false;
        MappingCatalog.Clear();
        RaisePermissionsChanged();
        var c = _client;
        _client = null;
        _lastKey = null;
        try { c?.Dispose(); } catch { }
        Diag.Log(400, Diag.Err(code), 3);
        Logger.Warn($"Reprise de session refusée : {code}");
        SessionEnded?.Invoke(msg);
    }

    /// <summary>Plus JAMAIS de reprise pour cette session (kill switch, control_kill, fin voulue, déconnexion) ; annule celle en cours.</summary>
    private static void ForbidResume()
    {
        lock (_resumeLock) _resumeForbidden = true;   // sérialisé avec la section critique de CommitResume
        CancelResume();
    }

    private static void CancelResume()
    {
        CancellationTokenSource? c;
        lock (_resumeLock) { c = _resumeCts; _resumeCts = null; }
        try { c?.Cancel(); } catch { }
        Volatile.Read(ref _retryNow)?.TrySetResult(false);
        Interlocked.Exchange(ref _resumeState, 0);
        _resumeSuspended = false;
        SetLinkState(LicenseLinkState.Online);
    }

    // Lien perdu / revenu (repli HTTP du client COURANT). Ne remplace jamais un état de reprise (Resuming, Stalled, NeedsUser).
    private static void SetLinkDown(bool down)
    {
        if (_linkDown == down) return;
        _linkDown = down;
        Diag.Log(415, down ? 1 : 0);
        if (down) Logger.Warn("Serveur de licence injoignable (coupure prolongée) — botting suspendu, licence gardée");
        else Logger.Info("Serveur de licence de nouveau joignable");
        RefreshOfflineState();
    }

    // Contrôle de version injoignable alors que TOUT le serveur l'est (coupure) : pause récupérable, levée par la preuve suivante.
    private static void SetControlUnverified(bool on)
    {
        if (_controlUnverified == on) return;
        _controlUnverified = on;
        Diag.Log(417, on ? 1 : 0);
        if (on) Logger.Warn("Contrôle de version injoignable (coupure) — botting suspendu jusqu'à la prochaine vérification");
        RefreshOfflineState();
    }

    // Pastille « serveur injoignable » : jamais par-dessus un état de reprise (Resuming / Stalled / NeedsUser), ni hors session.
    private static void RefreshOfflineState()
    {
        bool off = (_linkDown || _controlUnverified) && IsAuthorized && !_resumeForbidden;
        if (off && LinkState == LicenseLinkState.Online) SetLinkState(LicenseLinkState.Offline);
        else if (!off && LinkState == LicenseLinkState.Offline) SetLinkState(LicenseLinkState.Online);
    }

    // ---------------- requêtes signées : lien de clé, key_mismatch, refus sans nouvel essai ----------------

    private static volatile bool _signingBlocked;
    private static int _keyMismatchUsed;

    /// <summary>Requête signée refusée sans nouvel essai possible (client à mettre à jour / horloge fausse) alors qu'aucun mapping
    /// n'est en mémoire : automatisation suspendue (la licence reste ouverte). Levé par un login manuel réussi.</summary>
    public static bool SigningBlocked => _signingBlocked;
    public static string SigningBlockedMessage { get; private set; } = "";

    // /mapping refusé (signature) : le bundle déjà en mémoire est GARDÉ ; sans bundle, on suspend avec le message (une fois).
    private static void BlockSigning(LicenseServerException le)
    {
        if (MappingCatalog.IsReady) { Diag.Log(420, 0); return; }
        SigningBlockedMessage = Localize(le);
        if (_signingBlocked) return;
        _signingBlocked = true;
        Diag.Log(420, 1);
        Logger.Warn("Protocole non reçu : requête refusée (voir message) — automatisation suspendue");
        try { Toasts.Post(SigningBlockedMessage, success: false); } catch { }   // thread de fond : marshalé vers l'UI
    }

    // Session liée à une autre clé : la PREMIÈRE fois, procédure de reprise (ancien jeton + socket lâchés, un /auth signé qui
    // lie la session à NOTRE clé, nouveau socket, mapping si absent) ; si ça se reproduit, on s'arrête (pas de boucle).
    private static void OnKeyMismatch(LicenseClient client, int source)
    {
        if (!ReferenceEquals(client, _client)) return;
        bool again = Interlocked.Exchange(ref _keyMismatchUsed, 1) == 1;
        Diag.Log(419, source, again ? 2 : 1);
        if (again)
        {
            // /mapping : arrêt avec le message ; envoi WS : le coupe-circuit des envois existant prend le relais.
            if (source != 10) BlockSigning(new LicenseServerException("SIGNATURE_REJECTED", "", "key_mismatch"));
            return;
        }
        OnLapsed(client, source);
    }

    // Résultat du /auth (numéros seulement) : signé ?, session liée ?, empreinte serveur == la nôtre ? (support : fiche licence).
    private static void NoteKeyBinding(LicenseClient c)
    {
        int bound = c.KeyBound is bool b ? (b ? 1 : 0) : 2;
        int fp = c.KeyFingerprint is not { Length: > 0 } f ? 2 : string.Equals(f, ClientIdentity.Fingerprint, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        Diag.Log(418, c.SignedAuth ? 1 : 0, bound, fp);
    }

    // Login manuel réussi : repart d'un état neuf (quota et garde-fous remis à zéro).
    private static void ResetResumeGuards()
    {
        _resumeForbidden = false;
        _signingBlocked = false;
        Interlocked.Exchange(ref _keyMismatchUsed, 0);
        Interlocked.Exchange(ref _lastResumeOkTick, 0);
        lock (_resumeOkTicks) _resumeOkTicks.Clear();
        lock (_resumeAttemptTicks) _resumeAttemptTicks.Clear();
    }

    // Attend que le réseau soit revenu (sans compter d'échec). AOT-safe (pas de NetworkWatcher : son ctor crée un DispatcherTimer).
    private static async Task WaitForNetworkAsync(CancellationToken ct)
    {
        bool waited = false;
        while (!NetworkInterface.GetIsNetworkAvailable())
        {
            if (!waited) { waited = true; SetLinkState(LicenseLinkState.WaitingNetwork); }
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }
        // Réseau revenu : retour à l'état d'avant (Stalled garde son bouton, les bots restent en pause jusqu'à la reprise).
        if (waited && LinkState == LicenseLinkState.WaitingNetwork) SetLinkState(_resumeSuspended ? LicenseLinkState.Stalled : LicenseLinkState.Resuming);
    }
}
