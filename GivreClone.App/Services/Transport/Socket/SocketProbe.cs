using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ProtocolUpdates;
using GivreClone.App.Services.License;
using GivreLauncher.Core.Accounts;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>Issue d'une connexion Socket, pour la boucle de reconnexion : seul un échec PASSAGER se retente. Un refus
/// DÉFINITIF (banni, identifiants, Bouclier exigé, keydata absent, aucun perso…) retenté chaque minute = spam HAAPI /
/// e-mails de code Bouclier + signal de détection.</summary>
internal enum SocketOutcomeKind
{
    /// <summary>Stop / Déconnexion : fin voulue.</summary>
    Cancelled,
    /// <summary>La session de jeu a tourné (login accepté) puis s'est terminée (coupure, kick, inactivité…).</summary>
    SessionEnded,
    /// <summary>Échec passager AVANT le jeu (réseau, proxy, serveur plein, déjà connecté…) : on peut retenter.</summary>
    Transient,
    /// <summary>Refus DÉFINITIF : ne jamais retenter sans l'utilisateur.</summary>
    Permanent,
}

/// <summary>ServerKick : la session s'est terminée sur une déconnexion IMPOSÉE par le serveur (hang_up) — la boucle de session
/// la PARQUE (pid conservé, aucune reconnexion automatique) au lieu de l'abandonner.</summary>
internal readonly record struct SocketOutcome(SocketOutcomeKind Kind, string Reason, bool ServerKick = false, bool Banned = false, string BanEnd = "");

/// <summary>
/// Owned clientless attempts sharing a synthetic PID. Only the latest attempt
/// may register, feed or remove that PID's injector. The gate serializes actual
/// registration/removal so a previous finally cannot erase its successor.
/// This is local lifecycle ownership, not server authentication or an ACK.
/// </summary>
internal sealed class SocketSessionOwnership
{
    internal sealed class Lease(uint pid, Action revoke,
        Func<SocketLinkProvenance, bool> validate)
    {
        private int _active = 1;
        internal uint Pid { get; } = pid;
        internal Action Revoke { get; } = revoke;
        internal Func<SocketLinkProvenance, bool> Validate { get; } = validate;
        internal bool IsActive => Volatile.Read(ref _active) == 1;
        internal void Retire() => Volatile.Write(ref _active, 0);
        internal bool Registered;
    }

    private readonly object _gate = new();
    private readonly Dictionary<uint, Lease> _current = new();
    // An external unregister callback can synchronously reenter Claim/Release.
    // Never allow registration while an older owner is being unregistered:
    // otherwise its delayed remove can erase the successor's injector.
    private readonly HashSet<uint> _unregistering = new();

    internal Lease Claim(uint pid, Action revokePrevious,
        Func<SocketLinkProvenance, bool> validate, Action<uint> unregister)
    {
        ArgumentNullException.ThrowIfNull(revokePrevious);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(unregister);
        Lease? previous = null;
        var lease = new Lease(pid, revokePrevious, validate);
        try
        {
            lock (_gate)
            {
                if (_current.TryGetValue(pid, out previous))
                {
                    // Retire synchronously at the ownership switch. The old
                    // SocketGameClient checks this volatile lease before queue
                    // admission and each attempted TCP write; invoking its Stop
                    // callback later cannot leave an old PID owner temporarily live.
                    previous.Retire();
                }
                // Publish the replacement BEFORE invoking any external callback.
                // Reentrant Claim may supersede it; this method must not overwrite
                // that newer lease after returning from unregister.
                _current[pid] = lease;
                if (previous is not null && previous.Registered)
                {
                    previous.Registered = false;
                    _unregistering.Add(pid);
                    try { unregister(pid); }
                    catch
                    {
                        lease.Retire();
                        if (_current.TryGetValue(pid, out var current) &&
                            ReferenceEquals(current, lease))
                            _current.Remove(pid);
                        throw;
                    }
                    finally { _unregistering.Remove(pid); }
                }
            }
        }
        finally
        {
            // A broken/unavailable unregister callback must not leave the
            // previous socket alive. Cancellation can reenter; do it outside
            // the registry lock even on the exception path.
            try { previous?.Revoke(); } catch { /* Superseded lease stays revoked. */ }
        }
        return lease;
    }

    internal bool IsCurrent(Lease lease)
    {
        lock (_gate)
            return _current.TryGetValue(lease.Pid, out var current) &&
                ReferenceEquals(current, lease) && lease.IsActive;
    }

    internal bool IsCurrentSocketLink(SocketLinkProvenance source)
    {
        lock (_gate)
        {
            if (!_current.TryGetValue(source.SyntheticPid, out var lease) ||
                !lease.IsActive) return false;
            try { return lease.Validate(source); }
            catch { return false; }
        }
    }

    internal bool TryRegister(Lease lease, Action register)
    {
        ArgumentNullException.ThrowIfNull(register);
        lock (_gate)
        {
            if (!_current.TryGetValue(lease.Pid, out var current) ||
                !ReferenceEquals(current, lease) || !lease.IsActive ||
                _unregistering.Contains(lease.Pid)) return false;
            // An external registration may synchronously reenter Claim/Release
            // on this thread (Monitor is reentrant). A retired lease cannot
            // become registered merely because its callback returned.
            register();
            if (!_current.TryGetValue(lease.Pid, out current) ||
                !ReferenceEquals(current, lease) || !lease.IsActive ||
                _unregistering.Contains(lease.Pid)) return false;
            lease.Registered = true;
            return true;
        }
    }

    internal bool Release(Lease lease, Action<uint> unregister)
    {
        ArgumentNullException.ThrowIfNull(unregister);
        lock (_gate)
        {
            if (!_current.TryGetValue(lease.Pid, out var current) ||
                !ReferenceEquals(current, lease)) return false;
            lease.Retire();
            // Remove the exact old owner BEFORE the potentially reentrant
            // unregister. Never remove a successor installed by its callback.
            _current.Remove(lease.Pid);
            if (lease.Registered)
            {
                lease.Registered = false;
                _unregistering.Add(lease.Pid);
                try { unregister(lease.Pid); }
                finally { _unregistering.Remove(lease.Pid); }
            }
            return true;
        }
    }

    /// <summary>
    /// The real SocketProbe OutboundWritten consumer boundary. Validate BOTH
    /// the exact lease (not just reused synthetic PID) and the current socket
    /// source before each callback. No callbacks run while _gate is held; the
    /// first subscriber may synchronously replace or close this connection.
    /// This path never feeds the legacy PID-only decoder or grants an ACK.
    /// </summary>
    internal void ForwardWritten(Lease lease, SocketTransportWriteObservation observation,
        Action<SocketTransportWriteObservation>? diagnostic,
        Action<SocketTransportWriteObservation>? additional)
    {
        // A constructed "written" record with no actual queue/attempt chain
        // is not evidence from the real SocketGameClient drain. Legacy V8
        // construction is retained, but cannot be promoted at this boundary.
        if (observation.AdmissionOrdinal <= 0 ||
            observation.AttemptOrdinal <= 0 ||
            observation.Ordinal <= 0 ||
            !observation.LocalWriteCompleted || observation.ServerAcknowledged)
            return;
        bool Current() => IsCurrent(lease) && IsCurrentSocketLink(observation.Source);
        if (!Current()) return;
        if (diagnostic is not null)
        {
            try { diagnostic(observation); }
            catch { /* The observer cannot interrupt the owning TCP stream. */ }
        }
        if (additional is null) return;
        foreach (Action<SocketTransportWriteObservation> listener in additional.GetInvocationList())
        {
            if (!Current()) return;
            try { listener(observation); }
            catch { /* A stale/throwing subscriber cannot interrupt TCP. */ }
        }
    }
}

/// <summary>
/// One exact source observed on either of the two real SocketGameClient paths:
/// framed input/protocol traffic OR completed engine writes. EOF/replacement
/// must revoke that exact source even if only post-flush traffic was observed.
/// No PID-only fallback, ACK assumption, or persistent storage is involved.
/// </summary>
internal sealed class SocketSourceCycle
{
    private sealed record Observed(SocketLinkProvenance Source);
    private Observed? _source;
    private int _retired;
    // 0=pending, 1=callback in progress, 2=exact revoke completed.
    // If the optional external callback throws, the enclosing finally may
    // retry this SAME exact source; no stale bytes can re-enter meantime.
    private int _revocationState;

    internal bool Note(SocketLinkProvenance source)
    {
        if (Volatile.Read(ref _retired) != 0) return false;
        if (source.ClientInstance == Guid.Empty || source.ConnectionToken == Guid.Empty ||
            source.RunEpoch <= 0 || source.SyntheticPid == 0) return false;
        var current = Volatile.Read(ref _source);
        if (current is not null)
            return current.Source == source && Volatile.Read(ref _retired) == 0;
        var first = new Observed(source);
        var prior = Interlocked.CompareExchange(ref _source, first, null);
        return (prior is null || prior.Source == source) &&
            Volatile.Read(ref _retired) == 0;
    }

    internal bool TryGet(out SocketLinkProvenance source)
    {
        if (Volatile.Read(ref _retired) == 0 &&
            Volatile.Read(ref _source) is { } current &&
            Volatile.Read(ref _retired) == 0)
        {
            source = current.Source;
            return true;
        }
        source = default;
        return false;
    }

    internal void RevokeExact(Action<SocketLinkProvenance> revoke)
    {
        ArgumentNullException.ThrowIfNull(revoke);
        // Retire first: callbacks racing with EOF/Stop must not re-admit the
        // same link or deliver a late post-write frame. An exact failure may
        // be retried by the finalizer, never promoted to another PID/source.
        Volatile.Write(ref _retired, 1);
        if (Volatile.Read(ref _source) is not { } current ||
            Interlocked.CompareExchange(ref _revocationState, 1, 0) != 0) return;
        try
        {
            revoke(current.Source);
            Volatile.Write(ref _revocationState, 2);
        }
        catch
        {
            Volatile.Write(ref _revocationState, 0);
            throw;
        }
    }
}

/// <summary>
/// Connexion Socket bout-en-bout pour UN compte : auth HAAPI (jeton de jeu) → serveur de connexion (liste serveurs,
/// sélection, host + jeton du serveur de jeu) → serveur de jeu (SocketGameClient, session persistante). Tout en [socket*].
/// N'a besoin d'AUCUN Dofus.exe ni injection : c'est le principe du mode clientless.
/// </summary>
internal static class SocketProbe
{
    private static readonly SocketSessionOwnership _sessionOwnership = new();
    /// <summary>
    /// True for the exact stream owner of this synthetic PID at the instant
    /// checked. A previously delivered frame alone never grants admission.
    /// </summary>
    internal static bool IsCurrentSocketLink(SocketLinkProvenance source) =>
        _sessionOwnership.IsCurrentSocketLink(source);
    // Un seul appel Cytrus toutes les 10 min pour tous les comptes (démarrage de plusieurs bots : pas 7 s de timeout chacun).
    private static readonly SemaphoreSlim _latestGate = new(1, 1);
    private static OfficialDofusReleaseProbe.Result? _latest;
    private static long _latestAt;
    private static async Task<OfficialDofusReleaseProbe.Result> LatestPublishedCachedAsync(CancellationToken ct)
    {
        await _latestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_latest is { VerifiedMetadata: true } r && Environment.TickCount64 - _latestAt < 600_000) return r;
            _latest = await OfficialDofusReleaseProbe.CheckAsync(ct).ConfigureAwait(false);
            _latestAt = Environment.TickCount64;
            return _latest;
        }
        finally { _latestGate.Release(); }
    }

    /// <summary>Empreinte matérielle envoyée en device_identifier (et HWID de la preuve Schnorr Fiat-Shamir) : SHA-256(HWID
    /// canonique + « | » + graine PAR COMPTE) en hex = 64 caractères. Le vrai client envoie un identifiant de 64 caractères :
    /// ses trames de login font TOUJOURS 127 o (138 o avec la sélection serveur, 22 tunnels MITM) = exactement 64 caractères +
    /// Shield vide; le SHA-512 donnait 199 o sur 28 logins socket — une empreinte visible dès le
    /// login. La graine (Account.DeviceSeed) rend l'identifiant UNIQUE et STABLE par compte : sur un serveur mono-compte, un
    /// device_identifier identique pour tous les comptes de la machine fait refuser le 2e compte (même « machine »). Le HWID
    /// de licence (Hwid.Compute) reste inchangé et machine-constant — seul cet identifiant Socket est dérivé par compte.</summary>
    private static string DeviceId(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Hwid.Compute() + "|" + seed)));

    // Délai avant la nouvelle tentative quand le serveur de jeu ferme AVANT d'accepter le login (session précédente encore
    // tenue, qu'il vient d'éjecter). Mesuré : le serveur de connexion refuse encore le compte à +1,3/+2,2/+3,0/+7,9 s après
    // l'éjection (retries du vrai client), nos reprises réussies sont toutes venues à +7,5 s ou plus -> 10-14 s aléatoires.
    private const int GhostRetryMinMs = 10_000, GhostRetryMaxMs = 14_000;

    // Refus d'identification PASSAGERS (enum IdentificationResponse.Error) : 0 inconnu, 1 déjà connecté (session encore
    // tenue), 2 délai OTP. Les autres (banni, Bouclier, verrouillé, identifiants, e-mail, IP anonyme interdite, pseudo,
    // non autorisé) sont définitifs ; 12/13 (version) sont corrigés sur place avec required_version.
    private static bool IdErrorTransient(int reason) => reason is 0 or 1 or 2;
    // Refus de sélection serveur PASSAGER : 0 refusé (serveur plein/fermé), 3 maintenance. 1 abonnés, 2 compte vérifié = définitifs.
    private static bool SelectErrorTransient(int err) => err is 0 or 3;

    /// <summary>Connexion complète. Si la session précédente de ce compte est encore tenue par le serveur (fermeture qui ne
    /// l'a pas atteint, ou combat en cours), la connexion suivante l'ÉJECTE et se fait fermer juste après
    /// l'IdentificationRequest ; la d'après passe. On fait donc UNE nouvelle tentative automatique dans ce cas précis —
    /// jamais sur un Stop, jamais en boucle. <paramref name="onCertMinted"/> : certificat Bouclier forgé pendant l'auth ->
    /// à PERSISTER (sinon ALREADYSECURED définitif au login suivant, cf lancement MITM). <paramref name="preferredCharacter"/> :
    /// perso à sélectionner (planning), sinon le premier de la liste. <paramref name="onGhostRetry"/> : appelé (secondes) juste
    /// avant l'attente de cette nouvelle tentative -> statut de la ligne.</summary>
    public static async Task<SocketOutcome> RunAsync(uint logPid, string login, string deviceSeed, ProxySettings? proxy, bool keyData,
                                                     string? apiKey, string? refreshToken, string? certId, string? certEncoded,
                                                     int preferredServerId, string? preferredCharacter, IShieldCodePrompt shieldPrompt,
                                                     Action<string, string>? onCertMinted, CancellationToken ct = default,
                                                     Action<int>? onGhostRetry = null,
                                                     Func<IReadOnlyList<ConnServer>, CancellationToken, Task<(int ServerId, string? Character)?>>? chooseServerChar = null,
                                                     ProxySettings? gameProxy = null,
                                                     Action<SocketTransportFrameObservation>? onObservedFrame = null,
                                                     Action<SocketTransportWriteObservation>? onOutgoingWritten = null)
    {
        // Certificat Bouclier forgé pendant une tentative : la tentative SUIVANTE doit l'utiliser (sinon, compte devenu SECURED
        // sans cert -> ALREADYSECURED alors qu'on vient de le sauvegarder).
        string? cid = certId, cenc = certEncoded;
        Action<string, string> minted = (i, e) => { cid = i; cenc = e; onCertMinted?.Invoke(i, e); };
        // Choix interactif serveur/perso : CAPTURÉ à la 1re tentative pour être REJOUÉ à la 2e (« session précédente encore
        // tenue ») sans re-demander à l'utilisateur — le 2e essai part alors droit sur le serveur/perso déjà choisi.
        int effServer = preferredServerId;
        string? effChar = preferredCharacter;
        Func<IReadOnlyList<ConnServer>, CancellationToken, Task<(int ServerId, string? Character)?>>? capturingChoose =
            chooseServerChar is null ? null : async (servers, cct) =>
            {
                var pick = await chooseServerChar(servers, cct).ConfigureAwait(false);
                if (pick is { } pk && pk.ServerId > 0) { effServer = pk.ServerId; effChar = pk.Character; }
                return pick;
            };
        for (int attempt = 1; ; attempt++)
        {
            var (outcome, ghostKick) = await RunOnceAsync(logPid, login, deviceSeed, proxy, keyData, apiKey, refreshToken, cid, cenc,
                                                          effServer, effChar, shieldPrompt, minted, ct,
                                                          // Sélecteur interactif : proposé UNIQUEMENT à la 1re tentative (au 2e
                                                          // essai, effServer/effChar portent déjà le choix -> aucune nouvelle demande).
                                                          attempt == 1 ? capturingChoose : null, gameProxy, onObservedFrame,
                                                          onOutgoingWritten).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return new(SocketOutcomeKind.Cancelled, "arrêt demandé");
            if (!ghostKick || attempt >= 2) return outcome;
            int delay = Random.Shared.Next(GhostRetryMinMs, GhostRetryMaxMs + 1);
            SocketLog.Info(logPid, "⚠ le serveur de jeu a fermé AVANT d'accepter le login : la session précédente du compte était encore " +
                                   $"tenue côté serveur (perso resté en jeu) et vient d'être éjectée — nouvelle tentative dans {delay / 1000} s…");
            try { onGhostRetry?.Invoke(delay / 1000); } catch { /* statut d'affichage seulement */ }
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return new(SocketOutcomeKind.Cancelled, "arrêt demandé"); }
        }
    }

    // Une passe. GhostKick = serveur de jeu fermé AVANT d'accepter le login (-> une nouvelle tentative).
    private static async Task<(SocketOutcome Outcome, bool GhostKick)> RunOnceAsync(
        uint logPid, string login, string deviceSeed, ProxySettings? proxy, bool keyData, string? apiKey, string? refreshToken,
        string? certId, string? certEncoded, int preferredServerId, string? preferredCharacter, IShieldCodePrompt shieldPrompt,
        Action<string, string>? onCertMinted, CancellationToken ct,
        Func<IReadOnlyList<ConnServer>, CancellationToken, Task<(int ServerId, string? Character)?>>? chooseServerChar = null,
        ProxySettings? gameProxy = null,
        Action<SocketTransportFrameObservation>? onObservedFrame = null,
        Action<SocketTransportWriteObservation>? onOutgoingWritten = null)
    {
        static (SocketOutcome, bool) Done(SocketOutcomeKind k, string why) => (new SocketOutcome(k, why), false);

        SocketLog.Info(logPid, $"=== CONNEXION SOCKET — compte « {login} » ({(keyData ? "Ankama Launcher" : "navigateur")}) ===");
        // Sans mapping, rien ne peut être forgé côté jeu : inutile de consommer un jeton HAAPI + un login de connexion.
        if (MappingCatalog.GetEffective() is null)
        {
            SocketLog.Info(logPid, "mapping ABSENT (pas encore reçu du serveur de licence) — abandon.");
            return Done(SocketOutcomeKind.Permanent, "mapping du protocole absent");
        }
        // The former socket path accepted any local hardcoded version and then
        // auto-retried an arbitrary server required_version against the SAME
        // mapping. Prevent mismatched protobuf field names/tags before auth.
        var ownedExe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(
            AppConfig.Current.DofusExePath);
        var versionBinding = MappingCatalog.SessionVersionBinding;
        var currentManifest = MZP11CytrusVersionProbe.InspectInstalledDofus(ownedExe);
        var initialAdmission = MZP12SocketVersionBinding.Evaluate(versionBinding, currentManifest);
        if (!initialAdmission.StructurallyConsistent)
        {
            SocketLog.Info(logPid, "version/mapping incompatibles : " + initialAdmission.SafeMessage);
            return Done(SocketOutcomeKind.Permanent, initialAdmission.SafeMessage);
        }
        bool compatibilityReview = SocketPilotPolicy.IsCrossPatchSession(initialAdmission.IsUnverifiedCrossPatchReview,
            initialAdmission.StructurallyConsistent, versionBinding?.MappingBuild, currentManifest.ProtocolVersion);
        // Opérateur : les codes réseau du build revu sont identiques à ceux du mapping. Le reçu SHA est épinglé à
        // l'installation (tout patch le casse) ; en plus, UNE vérification Cytrus au lancement : la dernière version
        // publiée doit être exactement celle installée. Sinon (nouveau patch, Cytrus muet) on reste en revue restreinte.
        bool aliasesVerified = false;
        if (compatibilityReview)
        {
            var latest = await LatestPublishedCachedAsync(ct).ConfigureAwait(false);
            aliasesVerified = SocketPilotPolicy.AliasesVerified(latest.LatestVersion, currentManifest.ProtocolVersion);
            SocketLog.Info(logPid, aliasesVerified
                ? $"✔ Cytrus : {latest.LatestVersion} = version installée, reçu SHA valide → pilotage complet (codes supposés identiques, essai opérateur)."
                : $"⚠ Cytrus : dernière version {latest.LatestVersion ?? "inconnue"} ≠ installée {currentManifest.ProtocolVersion} (ou injoignable) → revue restreinte.");
        }
        bool reviewRestricted = compatibilityReview && !aliasesVerified;
        if (reviewRestricted)
            SocketLog.Info(logPid, "⚠ Revue de compatibilité inter-patch : reçu SHA vérifié. " +
                "Déplacements de diagnostic autorisés après sélection et contexte serveur. " +
                "Combat, expédition, récolte et HDV automatisés indisponibles tant que le protocole d'action et ses ACK ne sont pas validés.");
        if (!keyData && string.IsNullOrEmpty(apiKey))
        {
            SocketLog.Info(logPid, "aucun apiKey pour ce compte navigateur — abandon.");
            return Done(SocketOutcomeKind.Permanent, "compte navigateur sans jeton (reconnecte-le via le navigateur)");
        }

        try
        {
            // 1) jeton de jeu via HAAPI (le Shield est réglé À LA CRÉATION du jeton, pas dans la trame de login). Deux
            //    sources, comme le lancement MITM (LaunchService.LaunchAsync) : keydata Zaap (Ankama Launcher) ou tokens navigateur.
            SocketLog.Auth(logPid, "obtention du jeton de jeu (HAAPI CreateToken game=1)…");
            ConnectionJournal.ForBot(logPid, Loc.T("L.Journal.Haapi"));
            string token;
            using (var auth = new AnkamaAuth(proxy, shieldPrompt) { CertificateHasher = GivreClone.App.Services.LocalLaunch.AnkamaOAuth.HashForStoredCertificate })
            {
                if (keyData)
                {
                    // Compte AJOUTÉ VIA ANKAMA LAUNCHER : jeton dérivé du keydata Zaap (apiKey + cert liés à la machine),
                    // exactement comme LaunchService.AuthenticateFromKeyDataAsync — aucun compte navigateur requis.
                    SocketLog.Auth(logPid, "compte Ankama Launcher : lecture du keydata Zaap…");
                    var all = await Task.Run(ZaapKeydataReader.ReadInstalledLauncherKeydataAsync).ConfigureAwait(false);
                    var kd = all.FirstOrDefault(k => k.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                    if (kd is null)
                    {
                        SocketLog.Info(logPid, "compte absent du keydata Zaap (ouvre le launcher Ankama une fois, puis relance) — abandon.");
                        return Done(SocketOutcomeKind.Permanent, "compte absent du keydata du launcher Ankama");
                    }
                    token = await auth.AuthenticateFromKeyDataAsync(kd, ct).ConfigureAwait(false);
                }
                else
                {
                    var acct = new Account
                    {
                        Login = login, Password = "", Proxy = proxy, ApiKey = apiKey, RefreshToken = refreshToken,
                        Certificate = (certId is { Length: > 0 } && certEncoded is { Length: > 0 })
                            ? new ShieldCertificate { Id = certId, EncodedCertificate = certEncoded } : null,
                    };
                    try { token = await auth.AuthenticateAsync(acct, ct).ConfigureAwait(false); }
                    finally
                    {
                        // Même règle que le lancement MITM (LaunchService.LaunchAsync) : un certificat Bouclier forgé ici est
                        // PERSISTÉ dès sa création, même si le CreateToken qui suit échoue (proxy 4G instable) — sinon le compte
                        // devient SECURED sans cert = ALREADYSECURED définitif à la reconnexion suivante.
                        if (acct.Certificate is { } minted && minted.Id != certId)
                        {
                            SocketLog.Auth(logPid, "certificat Bouclier forgé → mémorisé pour les prochaines connexions.");
                            onCertMinted?.Invoke(minted.Id, minted.EncodedCertificate);
                        }
                    }
                }
            }
            SocketLog.Auth(logPid, "jeton de jeu obtenu.");

            // 2) version client (celle que le serveur accepte ; corrigée ci-dessous s'il en exige une autre)
            var version = currentManifest.ProtocolVersion!;
            SocketLog.Info(logPid, $"version du Dofus installé = {version} (marqueur local et binaires ; version serveur encore à confirmer)");
            var beforeConnect = MZP12SocketVersionBinding.Evaluate(versionBinding,
                MZP11CytrusVersionProbe.InspectInstalledDofus(ownedExe));
            if (!beforeConnect.StructurallyConsistent)
                return Done(SocketOutcomeKind.Permanent, beforeConnect.SafeMessage);

            // 3) connexion au serveur de connexion. Shield VIDE comme le vrai client : le jeton porte déjà la preuve du Shield.
            var p = new SocketLoginParams
            {
                DeviceId = DeviceId(deviceSeed),
                ClientVersion = version,
                Token = token,
                CertificateId = 0,
                CertificateHash = "",
                Proxy = proxy,
                PreferredServerId = preferredServerId,
                PreferredCharacter = preferredCharacter,
                ChooseServerChar = chooseServerChar,
                CreationPending = () => SocketCreationRegistry.Has(login),
            };
            var client = new SocketConnectionClient(logPid);
            var result = await client.RunAsync(p, ct).ConfigureAwait(false);

            // Auto-correction de version : le serveur a renvoyé required_version -> on retente avec CETTE version exacte.
            if (result is null && client.RequiredVersion is { Length: > 0 } rv && rv != version)
            {
                var requiredAdmission = MZP12SocketVersionBinding.CheckServerRequiredBuild(versionBinding, rv);
                if (!requiredAdmission.StructurallyConsistent)
                {
                    SocketLog.Info(logPid, "required_version refusée : " + requiredAdmission.SafeMessage);
                    return Done(SocketOutcomeKind.Permanent, requiredAdmission.SafeMessage);
                }
                SocketLog.Info(logPid, $"le serveur exige la version {rv} — nouvelle tentative avec cette version.");
                version = rv;
                var p2 = new SocketLoginParams
                {
                    DeviceId = p.DeviceId, ClientVersion = rv, Token = p.Token,
                    CertificateId = p.CertificateId, CertificateHash = p.CertificateHash,
                    Proxy = p.Proxy, PreferredServerId = p.PreferredServerId, PreferredCharacter = p.PreferredCharacter, ChooseServerChar = chooseServerChar, CreationPending = p.CreationPending,
                };
                client = new SocketConnectionClient(logPid);
                result = await client.RunAsync(p2, ct).ConfigureAwait(false);
            }
            if (result is null)
            {
                SocketLog.Info(logPid, "=== serveur de connexion : NON abouti. ===");
                if (ct.IsCancellationRequested) return Done(SocketOutcomeKind.Cancelled, "arrêt demandé");
                // Sélection serveur/perso ANNULÉE par l'utilisateur : abandon PROPRE (traité comme un arrêt volontaire, jamais retenté).
                if (client.UserCancelled) return Done(SocketOutcomeKind.Cancelled, "sélection serveur/personnage annulée");
                if (client.SelectionRejected) return Done(SocketOutcomeKind.Permanent, "serveur/personnage explicite indisponible");
                if (client.NoCharacter) return Done(SocketOutcomeKind.Permanent, "aucun personnage sur ce compte");
                if (client.IdErrorReason == 3)   // BANNED : remonté au compte (plus de lancement automatique)
                    return (new SocketOutcome(SocketOutcomeKind.Permanent, "identification refusée : " + ConnectionMessages.IdErrorName(3)
                        + (client.BanEndDate.Length > 0 ? $" (jusqu'au {client.BanEndDate})" : ""), Banned: true, BanEnd: client.BanEndDate), false);
                if (client.IdErrorReason >= 0 && !IdErrorTransient(client.IdErrorReason))
                    return Done(SocketOutcomeKind.Permanent, "identification refusée : " + ConnectionMessages.IdErrorName(client.IdErrorReason));
                if (client.SelectErrorReason >= 0 && !SelectErrorTransient(client.SelectErrorReason))
                    return Done(SocketOutcomeKind.Permanent, "sélection du serveur refusée : " + ConnectionMessages.SelectErrorName(client.SelectErrorReason));
                return Done(SocketOutcomeKind.Transient, client.IdErrorReason >= 0
                    ? "identification refusée (passager) : " + ConnectionMessages.IdErrorName(client.IdErrorReason)
                    : "serveur de connexion injoignable ou muet");
            }

            // Serveur DÉTERMINISTE (choisi à l'étape A) -> renseigne row.Server tout de suite, sans WhoAmI.
            if (result.SelectedServerId != 0) Stores.Launch.SetSocketServerId(logPid, result.SelectedServerId);

            // 4) serveur de JEU. Port = le PREMIER annoncé par le serveur (5555/443 -> 5555), exactement ce qu'utilise le vrai
            // client (relais MITM: « …:5555 ») et un bot tiers. Le 443 forcé était à la fois une empreinte et
            //    le port qui passe mal derrière un partage 4G/5G (cf HandshakeRelay) : tous les « fantômes » constatés y étaient.
            SocketLog.Info(logPid, "=== serveur de connexion OK → serveur de jeu ===");
            var gamePort = result.GamePorts.Count > 0 ? result.GamePorts[0] : 5555;
            // Proxy du SERVEUR DE JEU (paramètres du compte) : la connexion est passée par le proxy du compte (ex. rotatif),
            // la session de jeu bascule sur celui-ci (ex. statique, stable).
            ConnectionJournal.ForBot(logPid, Loc.Fmt("L.Journal.GameServer", $"{result.GameHost}:{gamePort}"));
            if (gameProxy is not null)
            {
                SocketLog.Info(logPid, $"serveur de jeu : bascule sur le proxy de jeu {gameProxy.Kind} {gameProxy.Host}:{gameProxy.Port}");
                ConnectionJournal.ForBot(logPid, Loc.Fmt("L.Journal.GameProxySwitch", $"{gameProxy.Kind} {gameProxy.Host}:{gameProxy.Port}"), LuaLogLevel.Warning);
            }
            // Scope réel de CETTE session. Une revue inter-patch laisse le runtime nul : réécrire la version
            // filaire ne suffit pas à armer le miroir. Le catalogue et ce scope doivent être la même chaîne non vide.
            var sourceCycle = new SocketSourceCycle();
            var game = new SocketGameClient(logPid);
            var lease = _sessionOwnership.Claim(logPid, () =>
                {
                    // Revocation belongs to the source that actually produced
                    // these bytes, not to the synthetic PID of a replacement.
                    try { sourceCycle.RevokeExact(Stores.Launch.RevokeSocketObservedLink); }
                    finally { game.RevokeForReplacement(); }
                },
                game.IsCurrentSocketLink,
                Stores.Launch.UnregisterSocketInjector);
            game.BindSessionOwnership(() => lease.IsActive);
            try
            {
                if (_sessionOwnership.IsCurrent(lease))
                    Stores.Launch.NoteSocketProtocolScope(logPid,
                        versionBinding?.MappingBuild, compatibilityReview ? null : version);
                await game.RunAsync(new SocketGameParams
            {
                Host = result.GameHost,
                Port = gamePort,
                Token = result.GameToken,
                ClientVersion = version,
                DeviceId = p.DeviceId,
                Proxy = gameProxy ?? proxy,
                // Perso choisi au sélecteur interactif (mode « choix du personnage ») prioritaire ; sinon le perso demandé
                // (planning) ou, à défaut, le premier / plus récent (étape B).
                PreferredCharacter = client.ChosenCharacter ?? preferredCharacter,
                // Admission nominative et SHA-épinglée du candidat inter-patch.
                // L'option de pilotage revue NE vaut PAS autorisation générale :
                // SocketGameClient valide les événements runtime et limite
                // strictement les demandes non encore corroborées par le serveur.
                ReviewedCrossPatchPilot = compatibilityReview,
                AliasesVerified = aliasesVerified,
                Creation = SocketCreationRegistry.Take(login),
                AccountLogin = login,
                Deletion = SocketDeletionRegistry.Take(login),
                // Toutes les trames (recues + envoyees) partent dans le MEME decodeur que le MITM -> l'UI par-bot se peuple
                // (carte/niveau/serveur/pods/interactifs/activite) comme un client MITM, sans Dofus.exe.
                FeedFrame = (pid, clientToServer, wire) =>
                {
                    // The legacy decoder cannot carry a source token. At
                    // minimum refuse frames from an older overlapping attempt
                    // before they can mutate the successor's PID-scoped state.
                    if (_sessionOwnership.IsCurrent(lease))
                        Stores.Launch.FeedSocketFrame(pid, clientToServer, wire);
                },
                FeedObserved = observation =>
                {
                    // The source-issued post-dial token and framed bytes now
                    // reach the SAME app decoder as a SEPARATE linked channel.
                    // The existing FeedFrame/PacketDecoded reducer remains
                    // PID-only and cannot borrow this observation's authority.
                    if (!_sessionOwnership.IsCurrent(lease) ||
                        !IsCurrentSocketLink(observation.Source)) return;
                    if (!sourceCycle.Note(observation.Source)) return;
                    try
                    {
                        Stores.Launch.FeedSocketObservedFrame(observation,
                            IsCurrentSocketLink);
                    }
                    catch { /* Optional provenance observer cannot break TCP. */ }
                    if (onObservedFrame is null) return;
                    foreach (Action<SocketTransportFrameObservation> listener in
                             onObservedFrame.GetInvocationList())
                    {
                        if (!_sessionOwnership.IsCurrent(lease) ||
                            !IsCurrentSocketLink(observation.Source)) return;
                        try { listener(observation); }
                        catch { /* A consumer cannot break the game socket. */ }
                    }
                },
                OutboundWritten = observation =>
                {
                    // Engine writes are a separate, post-flush observation
                    // stream: never echo them to FeedFrame/PacketDecoded or
                    // borrow any server ACK from a PID-only decoder.
                    if (!_sessionOwnership.IsCurrent(lease) ||
                        !IsCurrentSocketLink(observation.Source) ||
                        !sourceCycle.Note(observation.Source)) return;
                    _sessionOwnership.ForwardWritten(lease, observation,
                        observed => Stores.Launch.FeedSocketWrittenObservation(
                            observed, IsCurrentSocketLink),
                        onOutgoingWritten);
                },
                // PILOTAGE : l'ancienne revue désactivait totalement l'injecteur, même APRÈS un login et un contexte
                // de jeu authentiques : comptes "En jeu" impossibles à déplacer/combattre, scripts et expéditions muets.
                // Le reçu local SHA + admission du build sont validés AVANT cette étape ; c'est un essai opérateur,
                // pas une certification de tous les messages. SocketGameClient.Inject refuse avant sa readiness live,
                // et le moteur exige toujours ses droits/caps ; pas de repli MITM pour un pid Socket.
                RegisterInjectorWithAdmission = (pid, inject) =>
                    pid == logPid && _sessionOwnership.TryRegister(lease,
                        () => Stores.Launch.RegisterSocketInjector(
                            pid,
                            wire => _sessionOwnership.IsCurrent(lease) &&
                                game.TryGetCurrentSocketLink(out var source) &&
                                IsCurrentSocketLink(source) && inject(wire),
                            reviewedMovementOnly: reviewRestricted)),
                UnregisterInjector = pid =>
                {
                    if (pid == logPid)
                        _sessionOwnership.Release(lease, Stores.Launch.UnregisterSocketInjector);
                },
                ChallengeLoot = Stores.Launch.PrefersChallengeLoot,   // bonus des défis du profil de combat (XP / butin)
                TurboAck = reviewRestricted ? null : () => Stores.Launch.IsTurboAck((uint)logPid),
                TurboAckAll = reviewRestricted ? null : () => Stores.Launch.IsTurboAckAll((uint)logPid),
                TurboEagerReady = reviewRestricted ? null : () => Stores.Launch.IsTurboEagerReady((uint)logPid),
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                try { sourceCycle.RevokeExact(Stores.Launch.RevokeSocketObservedLink); }
                finally
                {
                    // Also covers exceptions thrown before SocketGameClient's
                    // own finalizer can unregister. An older lease never
                    // removes a newer PID's injector.
                    _sessionOwnership.Release(lease, Stores.Launch.UnregisterSocketInjector);
                }
            }
            SocketLog.Info(logPid, "=== session de jeu terminée. ===");
            if (ct.IsCancellationRequested) return Done(SocketOutcomeKind.Cancelled, "arrêt demandé");
            // Déconnexion IMPOSÉE par le serveur (modération, maintenance) : le vrai client ne revient pas tout seul.
            // Typé (ServerKick) : la boucle PARQUE la session même si le hang_up n'a pas été vu par LaunchService.
            if (game.ServerHangUp is { } hu)
                return (new SocketOutcome(SocketOutcomeKind.Permanent, "déconnexion imposée par le serveur : " + hu, ServerKick: true), false);
            if (game.LoginAccepted) return Done(SocketOutcomeKind.SessionEnded, "session de jeu terminée");
            // Nouvelle tentative IMMÉDIATE seulement si le serveur a fermé APRÈS notre login et AVANT de l'accepter (session
            // précédente éjectée). DNS / ouverture TCP / proxy / login sans réponse = échec ordinaire -> boucle de reconnexion
            // (son délai et son backoff), jamais un cycle HAAPI + connexion de plus hors budget.
            return game.ClosedBeforeAccept
                ? (new SocketOutcome(SocketOutcomeKind.Transient, "login de jeu refusé (session précédente encore tenue)"), true)
                : Done(SocketOutcomeKind.Transient, "serveur de jeu injoignable ou muet avant le login");
        }
        // Auth HAAPI refusée (jeton invalide, compte sécurisé sans code, code Bouclier refusé/non saisi…) : définitif.
        catch (AnkamaAuthException e) { SocketLog.Auth(logPid, $"auth refusée : {e.Message}"); return Done(SocketOutcomeKind.Permanent, "auth refusée : " + e.Message); }
        // Annulation DEMANDÉE (Arrêter, Déconnexion, « Reconnecter ») : seule celle-ci vaut « arrêt demandé ».
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { SocketLog.Info(logPid, "connexion annulée."); return Done(SocketOutcomeKind.Cancelled, "arrêt demandé"); }
        // Annulation NON demandée = délai dépassé (HttpClient de l'auth HAAPI : TaskCanceledException après 100 s, proxy muet…) :
        // passager — sinon la boucle de session s'arrêtait en silence, ligne « en ligne » sans session.
        catch (OperationCanceledException e)
        {
            SocketLog.Info(logPid, $"ERREUR connexion : délai HAAPI dépassé (proxy muet ?) — {e.GetType().Name}");
            return Done(SocketOutcomeKind.Transient, "délai HAAPI dépassé (proxy muet ?)");
        }
        // Réseau, proxy, DNS… : passager.
        catch (Exception e) { SocketLog.Info(logPid, $"ERREUR connexion {e.GetType().Name} : {e.Message}"); return Done(SocketOutcomeKind.Transient, $"{e.GetType().Name} : {e.Message}"); }
    }
}
