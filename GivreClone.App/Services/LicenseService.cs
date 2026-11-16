using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.License;
using GivreClone.Engine.Secrets;

namespace GivreClone.App.Services;

/// <summary>Verdict du "launcher control" Ikuware (GET /version, PUBLIC mais SIGNÉ Ed25519 + server_time). Kill = tuer
/// tous les clients ; Outdated = version client &lt; min_version serveur. Ok=false => on BLOQUE, en affichant Message.
/// <see cref="Verified"/> = le verdict provient d'une réponse SIGNÉE valide et fraîche (autorité) ; false = défaut
/// FAIL-CLOSED (injoignable / signature invalide / horloge hors tolérance) -> on bloque, mais avec tolérance TTL au runtime.</summary>
public readonly record struct VersionVerdict(bool Ok, bool Kill, bool Outdated, string? MinVersion, string? CurrentVersion, string? Message, bool Verified,
    string? DownloadUrl = null, string? Sha256 = null, long Size = 0, bool Mandatory = false, string? Notes = null)
{
    /// <summary>DEV seulement (pas de clé publique) : on laisse passer, marqué "vérifié" (le bail runtime le tolère).</summary>
    public static VersionVerdict Permissive => new(true, false, false, null, null, null, true);

    /// <summary>FAIL-CLOSED : pas de preuve signée valide (injoignable / signature / horloge / illisible) -> bloque
    /// comme un kill, mais NON vérifié (le runtime tolère jusqu'au TTL avant de couper ; le boot coupe tout de suite).</summary>
    public static VersionVerdict FailClosed(string? message) => new(false, true, false, null, null, message, false);
}

/// <summary>Télémetrie d'UN bot en jeu, poussée au serveur (canal "bots" du panel Sessions). pid = clé de jointure +
/// cible de la déconnexion à distance ; kamas = -1 si inconnu. Mode = mode de la SESSION en cours : "mitm" (client Dofus
/// relayé) ou "socket" (sans Dofus.exe) — contrat web : docs/web-bot-connection-mode.md. UptimeS = secondes depuis la mise en
/// route du bot (-1 = inconnu) : envoyé dans la trame mais JAMAIS dans la signature de déclenchement (il avance tout seul).</summary>
public sealed record BotTelemetry(int Pid, string Server, string Character, int Level, long MapId, int X, int Y, int PodsCur, int PodsMax, long Kamas, int Breed = 0,
    string? Activity = null, string? Script = null, bool? ScriptRunning = null, string? Mode = null, long UptimeS = -1,
    HdvStatus? Hdv = null);

/// <summary>
/// Authentification GivreClone par cle de licence via l'API Ikuware (client <see cref="LicenseClient"/>,
/// conforme a PROTOCOL.md). Point d'entree unique : <see cref="ValidateAsync"/> (login manuel + auto-login).
///
/// Sur succes, un heartbeat HTTP (60 s, intervalle donne par /auth) tourne en tache de fond ; deux echecs
/// consecutifs — ou un code terminal (KEY_PAUSED, HWID_MISMATCH, USER_BANNED…) — declenchent
/// <see cref="SessionEnded"/> : l'UI doit alors reverrouiller (retour au LoginOverlay).
///
/// La cle memorisee (case "rester connecte") est chiffree DPAPI (CurrentUser) dans settings.json —
/// jamais en clair, illisible sur une autre machine/session.
/// </summary>
public static partial class LicenseService
{
    private const string ClientName = "GivreClone";
    // Target release for the independent reconstructed clone, NOT an assertion of
    // 1.3.1 binary parity. Local mode never calls the old Givre auth service.
    public const string AppVersion = "1.3.3.11";

    /// <param name="KeyRejected">La CLÉ elle-même est refusée pour de bon (invalide, expirée, révoquée, compte banni) : la
    /// connexion auto l'oublie. Jamais sur une coupure réseau, une erreur de signature ou d'horloge (la clé reste mémorisée).</param>
    public readonly record struct AuthResult(bool Success, string Message, bool KeyRejected = false);

    private static LicenseClient? _client;

    /// <summary>GivreRelay : (jeton de session, adresse wss du relais), ou (null, null) hors session autorisée.</summary>
    public static (string? Token, Uri? Uri) RelayEndpoint()
    {
        var c = _client;
        return IsAuthorized && c is { SessionToken: { } t } ? (t, c.RelayClientUri) : (null, null);
    }
    private static CancellationTokenSource? _heartbeatCts;
    private static string? _lastKey;

    /// <summary>La session s'est terminee cote serveur (pause/ban/expiration/HWID/echecs heartbeat).
    /// L'argument est le message deja localise a afficher. Leve depuis un thread de fond → marshaller vers l'UI.</summary>
    public static event Action<string>? SessionEnded;

    /// <summary>Session de licence active + valide = le botting est AUTORISÉ. Passe à false dès qu'une session est
    /// tuée (killswitch panel/ban/expiration) ou à la déconnexion : le scheduler + les scripts Lua doivent s'arrêter.</summary>
    private static bool _isAuthorized;
    public static bool IsAuthorized { get => _isAuthorized || LocalEdition.Enabled; private set => _isAuthorized = value; }

    /// <summary>Fermeture de l'application en cours (déconnexion de sortie <see cref="LogoutBlocking"/>). Cette déconnexion lève
    /// <see cref="PermissionsChanged"/> (le moteur coupe l'auto-combat et les mods de chaque client : la session est finie),
    /// mais ce N'EST PAS une révocation de droits : les abonnés qui COUPENT des choses (comptes routés proxy/adaptateur,
    /// proxys d'interface, cap de niveau…) l'ignorent. Avalonia exécute encore les handlers postés pendant la fermeture.</summary>
    public static bool IsExiting => Volatile.Read(ref _exiting);
    private static bool _exiting;

    public static LicenseInfo? License => _client?.License;

    /// <summary>Empreinte matérielle de CETTE machine (identique à celle envoyée au serveur) — pour "Mon compte".</summary>
    public static string HardwareId => Hwid.Compute();

    /// <summary>Temps restant avant expiration (recalcule a la volee), ou null pour une licence a vie/inconnue.</summary>
    public static TimeSpan? RemainingTime =>
        _client?.License is { ExpiresAtMs: > 0 } li
            ? DateTimeOffset.FromUnixTimeMilliseconds(li.ExpiresAtMs) - DateTimeOffset.UtcNow
            : null;

    // ---------------- droits serveur (permissions par-fonctionnalité) ----------------

    /// <summary>Codes de droits serveur (page "Packages" du panel Ikuware). Chaque droit a SA propre expiration
    /// (peut expirer AVANT la licence). Gater une action = <see cref="HasPermission"/>(Perm.Xxx).</summary>
    public static class Perm
    {
        public const string Gather   = "gather";    // récolte auto
        public const string Fight    = "fight";     // combat auto
        public const string Adapter  = "adapter";   // Givre Adapter
        public const string Multi    = "multi";     // GivreLauncher / multi-comptes
        public const string Tactical = "tactical";  // mode tactique (GivreMods)
        public const string NoAnim   = "no-anim";   // mode no-anim (GivreMods)
        public const string Proxy    = "proxy";     // proxy réseau externe SOCKS5/HTTP (onglet Proxys)
        public const string PmRelay  = "pm-relay";   // messagerie : réponse auto aux MP + relais Discord (backend : canal "pm" + SEND_PM)
        public const string Hdv      = "hdv";        // bot Hôtel de Vente (achat/vente auto) — dépendance backend à accorder (comme "proxy")
        public const string Socket   = "socket";     // mode Socket (clientless, sans Dofus.exe) — le MITM reste inclus de base
        // Licence DÉVELOPPEUR / staff (serveur : maintenance_bypass). SEULE licence autorisée à voir la correspondance
        // code obfusqué -> nom clair (console, logs, API Lua). Un utilisateur normal ne doit JAMAIS pouvoir énumérer le mapping.
        public const string MaintenanceBypass = "maintenance_bypass";

        /// <summary>Tous les codes connus (ordre d'affichage "Mon compte").</summary>
        public static readonly string[] All = { Gather, Fight, Adapter, Multi, Tactical, NoAnim, Proxy, PmRelay, Hdv, Socket };
    }

    /// <summary>Les droits de la licence ont peut-être changé (login, logout, session tuée/expirée). L'UI doit
    /// réévaluer <see cref="HasPermission"/> (griser boutons/checkbox) + rafraîchir "Mon compte". Levé possiblement
    /// depuis un thread de fond → le souscripteur DOIT marshaller vers l'UI (Dispatcher.UIThread).</summary>
    public static event Action? PermissionsChanged;

    /// <summary>Un droit ACTIF vient d'être ajouté / retiré EN DIRECT (édition côté panel). Args : (codes ajoutés,
    /// codes retirés). Sert à notifier l'utilisateur (toast). NON levé au login/logout (transitions normales).</summary>
    public static event Action<IReadOnlyList<string>, IReadOnlyList<string>>? PermissionsDelta;

    private static HashSet<string> _lastActivePerms = new();

    private static void RaisePermissionsChanged() => RaisePermissionsChanged(notifyDelta: false);

    // notifyDelta=true (mise à jour serveur en direct) : compare les droits ACTIFS et lève PermissionsDelta sur un
    // ajout/retrait -> toast. false (login/logout) : synchronise juste l'instantané, sans notifier.
    private static void RaisePermissionsChanged(bool notifyDelta)
    {
        var now = new HashSet<string>(Perm.All.Where(HasPermission));
        if (notifyDelta)
        {
            var added = now.Except(_lastActivePerms).ToList();
            var removed = _lastActivePerms.Except(now).ToList();
            if (added.Count > 0 || removed.Count > 0) PermissionsDelta?.Invoke(added, removed);
        }
        _lastActivePerms = now;
        PermissionsChanged?.Invoke();
    }

    /// <summary>Porte AUTORITAIRE côté client d'une action gatée : session valide ET droit présent + non expiré.
    /// Défense en profondeur — à combiner avec le grisage UI ET le refus au POINT D'ACTION (le grisage seul ne
    /// protège pas d'un client patché ; cf audit sécurité P1-2).</summary>
    public static bool HasPermission(string code)
    {
        if (!IsAuthorized) return false;
        if (LocalEdition.Enabled && _client is null) return true;   // édition locale : droits de l'exploitant, sans serveur
#if DEBUG
        if (_client is null) return true;   // bypass DEV (GIVRE-DEV) : tous les droits, pour tester l'UI sans serveur
#endif
        return _client?.HasPermission(code) ?? false;
    }

    /// <summary>Niveau max. d'un personnage avec l'offre gratuite.</summary>
    public const int FreeLevelCap = 30;

    // Nom du pack (signé) qui désigne l'offre gratuite / l'essai. Repli tant que le serveur ne déclare pas « free » : avant, le
    // cap était déduit des droits (« récolte seule ») et le pack « Givre Free » (gather + fight) passait pour payant, sans cap.
    private static bool IsFreePackageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains("free", StringComparison.OrdinalIgnoreCase) || name.Contains("gratuit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("essai", StringComparison.OrdinalIgnoreCase) || name.Contains("trial", StringComparison.OrdinalIgnoreCase));

    /// <summary>Cap de niveau de PERSONNAGE imposé par la licence (<see cref="int.MaxValue"/> = illimité), comparé au niveau
    /// du perso piloté. Ordre : <c>max_level</c> réglé par l'admin sur le pack (signé ; 0 = aucune limite) ; sinon « free » déclaré
    /// par le serveur (signé : gratuit = 30, payant = aucune limite) ; sinon (ancien serveur) nom du pack.</summary>
    public static int LevelCap
    {
        get
        {
            if (!IsAuthorized) return int.MaxValue;                  // non autorisé -> géré par le gate de login, pas ici
            if (LocalEdition.Enabled && _client is null) return int.MaxValue;   // édition locale : pas de cap
#if DEBUG
            if (_client is null) return int.MaxValue;                // bypass DEV (GIVRE-DEV) : pas de cap
#endif
            if (_client?.MaxLevel is int server) return server <= 0 ? int.MaxValue : server;   // réglé par l'admin (0 = aucune limite)
            if (_client?.License?.Free is bool free) return free ? FreeLevelCap : int.MaxValue;   // déclaré par le serveur : fait foi
            return IsFreePackageName(_client?.License?.PackageName) ? FreeLevelCap : int.MaxValue;
        }
    }

    /// <summary>Un cap de niveau s'applique à cette licence (offre gratuite).</summary>
    public static bool HasLevelCap => LevelCap != int.MaxValue;

    /// <summary>Licence DÉVELOPPEUR / staff (permission serveur <c>maintenance_bypass</c>) : SEULE autorisée à révéler la
    /// correspondance code obfusqué -> nom clair (console, logs, API Lua). Pour tout le reste, seul le nom clair est visible.</summary>
    public static bool IsDeveloper => HasPermission(Perm.MaintenanceBypass);

    /// <summary>Niveau autorisé au cap, niveau INCONNU (≤ 0) toléré : pour le point d'action (injection), où un niveau pas
    /// encore capté ne doit pas casser un joueur gratuit légitime. Les DÉMARRAGES utilisent <see cref="LevelAllowedToStart"/>.</summary>
    public static bool LevelAllowed(int level) => level <= 0 || level <= LevelCap;

    /// <summary>Démarrage d'une automatisation : sous cap, le niveau doit être CONNU et ≤ cap (fail-closed). Sans cap : toujours.</summary>
    public static bool LevelAllowedToStart(int level) => !HasLevelCap || (level > 0 && level <= LevelCap);

    /// <summary>Droits actifs (non expirés) de la licence courante — pour l'onglet "Mon compte".</summary>
    public static IReadOnlyList<PermissionInfo> ActivePermissions() =>
        _client?.ActivePermissions() ?? Array.Empty<PermissionInfo>();

    // ---------------- télémetrie des bots (canal "bots" du panel Sessions) ----------------

    private static Func<IReadOnlyList<BotTelemetry>>? _botProvider;
    private static string _lastBotsSig = "";
    private static long _lastBotsSentTick;   // Environment.TickCount64 du dernier envoi "bots" réussi
    // Filet de fraîcheur de uptime_s (hors signature) : sans aucun autre changement, au plus UN envoi par minute
    // (le web l'affiche à la minute près ; demande serveur 27/09 : jamais d'envoi continu).
    private static readonly long _botsRefreshMs = 60_000;

    /// <summary>L'app fournit l'état COURANT des bots en jeu (thread-safe : appelé depuis un thread de fond). Ex :
    /// AccountsViewModel maintient un instantané. null = pas de télémetrie.</summary>
    public static void SetBotProvider(Func<IReadOnlyList<BotTelemetry>>? provider) => _botProvider = provider;

    // Clé Ed25519 CLIENTE (voir ClientIdentity) : signe les uploads WS et, si le serveur l'annonce, /auth et /mapping.
    private static (byte[] Sk, byte[] Pk) ClientKey() => ClientIdentity.Get();

    /// <summary>Empreinte de la clé client (support) : 16 premiers hexa de SHA-256(clé publique en hexa minuscule, ASCII) —
    /// EXACTEMENT celle que l'admin affiche, pour comparer. Ne révèle rien (dérivée d'une clé PUBLIQUE).</summary>
    public static string ClientKeyFingerprint()
    {
        try { return ClientIdentity.Fingerprint; }
        catch { return "—"; }
    }

    // Réveil immédiat de la boucle télémétrie (start/stop d'un bot, changement notable) -> position quasi live.
    private static readonly SemaphoreSlim _botPulse = new(0, 1);
    // Cadence de fond : un "battement" toutes les ~1,5 s tant qu'un bot bouge (le dev web recommande 1–2 s ;
    // le serveur throttle au-delà de ~2/s). On reste sous ce plafond : 1,5 s de poll + réveil ponctuel sur pulse.
    private static readonly TimeSpan _botTick = TimeSpan.FromMilliseconds(1500);

    /// <summary>Force un envoi télémétrie bots au prochain instant (au lieu d'attendre le tick) : à appeler sur
    /// lancement/arrêt d'un bot ou tout changement notable, pour que le panel se rafraîchisse immédiatement.</summary>
    public static void PulseBotTelemetry()
    {
        try { _botPulse.Release(); } catch (SemaphoreFullException) { /* déjà en attente d'un pulse : ok */ }
        PulseHdvEvents();   // start/stop d'un bot : vide aussi la file HDV (envoi « au moment où un bot s'arrête »)
    }

    // Boucle d'émission tant que la session tourne : pousse le canal "bots" SIGNÉ côté client, best-effort,
    // uniquement quand l'ensemble a CHANGÉ (anti-spam ; la signature inclut x,y -> on émet PENDANT le déplacement,
    // pas seulement au changement de map). Tick ~1,5 s OU réveil immédiat via PulseBotTelemetry (start/stop/notable).
    // Chaque frame porte un nonce + server_time FRAIS (BuildBotsPayloadBytes). PushUploadAsync = no-op si WS down.
    private static void StartBotTelemetry(LicenseClient client, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var (sk, pk) = ClientKey();
                var pkHex = Convert.ToHexString(pk).ToLowerInvariant();
                while (!ct.IsCancellationRequested)
                {
                    // Attend le tick OU un pulse (start/stop/notable) -> émission ~immédiate sur événement.
                    try { await _botPulse.WaitAsync(_botTick, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    // Accusés en file (jamais partis, ou partis sur un socket mort avant la preuve de réception) : rejoués dès qu'un
                    // envoi peut partir, jamais sur le socket qui les a déjà portés.
                    if (HasPendingAcks() && client.CanUpload) await FlushPendingAcksAsync(client, max: 2).ConfigureAwait(false);
                    var bots = _botProvider?.Invoke() ?? Array.Empty<BotTelemetry>();
                    var sig = BotsSignature(bots);
                    // Rien de neuf -> pas d'envoi. uptime_s n'est PAS dans la signature (il changerait à chaque tick = envoi
                    // continu) ; il part avec le prochain vrai changement, ou au plus une fois par minute si des bots tournent.
                    bool stale = bots.Count > 0 && Environment.TickCount64 - _lastBotsSentTick >= _botsRefreshMs;
                    if (sig == _lastBotsSig && !stale) continue;
                    var bytes = BuildBotsPayloadBytes(bots, pkHex);
                    var signature = Cryptographic.Ed25519.Signature(bytes, sk, pk);
                    // Marqué envoyé SEULEMENT si l'envoi est réellement parti (WS down / hello pas encore acquitté / envois
                    // suspendus -> on retentera au prochain tick).
                    if (!await client.PushUploadAsync("bots", bytes, signature).ConfigureAwait(false)) continue;
                    _lastBotsSig = sig;
                    _lastBotsSentTick = Environment.TickCount64;
                }
            }
            catch (Exception e) { Logger.Warn($"Télémetrie bots arrêtée : {e.Message}"); }
        });
    }

    // Signature de CONTENU (hors nonce/server_time) pour détecter un changement réel. NE JAMAIS y mettre un compteur qui avance
    // tout seul (uptime_s…) : la signature changerait à chaque tick -> une trame toutes les 1,5 s par session (demande serveur 27/09).
    private static string BotsSignature(IReadOnlyList<BotTelemetry> bots) =>
        string.Join(";", bots.Where(b => b.Pid > 0)
            .Select(b => $"{b.Pid},{b.Server},{b.Character},{b.Level},{b.MapId},{b.X},{b.Y},{b.PodsCur},{b.PodsMax},{b.Kamas},{b.Breed},{b.Activity},{b.Script},{b.ScriptRunning},{b.Mode}"
                         // HDV : seulement ce qui change « par évènement ». Valeur en vente, gains et âge de la dernière action partent
                         // avec le prochain vrai changement ou le rafraîchissement de 60 s (un reprix toutes les 1-2 s sinon = envoi continu).
                         + (b.Hdv is { } h ? $",{h.State},{h.Auto},{h.Listings},{h.SlotsMax},{h.SoldCount},{h.LastAction}" : "")));

    // JSON signé : { bots:[{pid,server,character,level,map_id,x,y,pods_cur,pods_max,kamas[,breed,activity,script,script_running,mode]}],
    // pubkey, nonce, server_time }. Champs entre crochets : optionnels, écrits seulement s'ils sont connus.
    private static byte[] BuildBotsPayloadBytes(IReadOnlyList<BotTelemetry> bots, string pubKeyHex)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("bots");
            foreach (var b in bots)
            {
                if (b.Pid <= 0) continue;   // le serveur rejette un pid absent/négatif -> on ne l'envoie pas
                w.WriteStartObject();
                w.WriteNumber("pid", b.Pid);
                w.WriteString("server", b.Server ?? "");
                w.WriteString("character", b.Character ?? "");
                w.WriteNumber("level", b.Level);
                w.WriteNumber("map_id", b.MapId);
                w.WriteNumber("x", b.X);
                w.WriteNumber("y", b.Y);
                w.WriteNumber("pods_cur", b.PodsCur);
                w.WriteNumber("pods_max", b.PodsMax);
                w.WriteNumber("kamas", b.Kamas);
                if (b.Breed > 0) w.WriteNumber("breed", b.Breed);   // classe Dofus (1-20) -> icône de classe côté portail/admin
                if (b.Activity is { Length: > 0 } act) w.WriteString("activity", act.Length > 24 ? act.Substring(0, 24) : act);   // état courant (idle/moving/harvesting/fighting/…)
                if (b.Script is { Length: > 0 } scn) w.WriteString("script", scn.Length > 80 ? scn.Substring(0, 80) : scn);       // nom du script Lua chargé
                if (b.ScriptRunning is bool sr) w.WriteBoolean("script_running", sr);                                             // true=tourne, false=chargé mais arrêté
                if (b.Mode is "mitm" or "socket") w.WriteString("mode", b.Mode);   // mode de la session : "mitm" (client Dofus relayé) / "socket" (sans Dofus.exe) ; énum stricte côté serveur
                if (b.UptimeS >= 0) w.WriteNumber("uptime_s", b.UptimeS);   // durée d'activité (« 3h 12m » côté web) — hors signature
                if (b.Hdv is { } h)   // résumé HDV (docs/web-bot-hdv-status.md) ; un serveur qui ne le connaît pas l'ignore sans refus
                {
                    w.WriteStartObject("hdv");
                    w.WriteString("state", h.State);
                    w.WriteBoolean("auto", h.Auto);
                    w.WriteNumber("listings", h.Listings);
                    if (h.SlotsMax > 0) w.WriteNumber("slots_max", h.SlotsMax);
                    w.WriteNumber("value", h.Value);
                    w.WriteNumber("sold_n", h.SoldCount);
                    w.WriteNumber("sold_kamas", h.SoldKamas);
                    if (h.LastAction is { } la) { w.WriteString("last_action", la); w.WriteNumber("last_action_s", h.LastActionAgoS); }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("pubkey", pubKeyHex);
            w.WriteString("nonce", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            w.WriteNumber("server_time", ServerClock.NowMs());
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>Transmet un MP REÇU au backend sur le canal signé "pm" (même mécanique que "bots" : enveloppe Ed25519 +
    /// nonce + server_time). Renvoie true si RÉELLEMENT poussé (WS ouvert ET permission pm-relay). Le backend DM
    /// l'utilisateur sur Discord + portail ; la réponse redescend en SEND_PM. false -> l'app bascule en réponse auto.</summary>
    public static async Task<bool> ForwardPrivateMessageAsync(int pid, string? server, string? character, string sender, long senderId, string content, int autoReplyIn = 0, int breed = 0)
    {
        var client = _client;
        if (client is null) return false;                       // bypass DEV / pas de session -> pas de relais backend
        if (!HasPermission(Perm.PmRelay)) return false;         // gate licence au POINT D'ACTION (pas seulement l'UI)
        try
        {
            var (sk, pk) = ClientKey();
            var pkHex = Convert.ToHexString(pk).ToLowerInvariant();
            byte[] bytes = BuildPmPayloadBytes(pid, server, character, sender, senderId, content, autoReplyIn, breed, pkHex);
            var signature = Cryptographic.Ed25519.Signature(bytes, sk, pk);
            return await client.PushUploadAsync("pm", bytes, signature).ConfigureAwait(false);
        }
        catch (Exception e) { Logger.Warn($"Forward MP échoué : {e.Message}"); return false; }
    }

    /// <summary>Best-effort : signale un ARCHIMONSTRE repéré au backend sur le canal signé "arch" (même enveloppe Ed25519 +
    /// nonce + server_time que "pm"/"bots"). Le backend attribue par pubkey (qui l'a trouvé), DM Discord le finder, poste
    /// dans le salon associé, et alimente la page publique + la vue admin globale. No-op si pas de session (bypass DEV).
    /// N'échoue jamais l'appelant : toute erreur est loggée. Renvoie true si RÉELLEMENT poussé (WS ouvert).</summary>
    public static async Task<bool> ReportArchMonsterAsync(int pid, string? server, string? character, int breed, int archGid, string archName, long mapId, int? x, int? y, int cell, int gfxId = 0, bool noDm = false)
    {
        var client = _client;
        if (client is null) return false;                       // bypass DEV / pas de session -> pas de relais backend
        try
        {
            var (sk, pk) = ClientKey();
            var pkHex = Convert.ToHexString(pk).ToLowerInvariant();
            byte[] bytes = BuildArchPayloadBytes(pid, server, character, breed, archGid, archName, mapId, x, y, cell, gfxId, noDm, pkHex);
            var signature = Cryptographic.Ed25519.Signature(bytes, sk, pk);
            return await client.PushUploadAsync("arch", bytes, signature).ConfigureAwait(false);
        }
        catch (Exception e) { Logger.Warn($"Signalement archimonstre échoué : {e.Message}"); return false; }
    }

    // JSON signé : { pid, server, character, breed, arch_gid, arch_name, gfx_id, map_id, x, y, cell, no_dm, pubkey, nonce, server_time }.
    private static byte[] BuildArchPayloadBytes(int pid, string? server, string? character, int breed, int archGid, string archName, long mapId, int? x, int? y, int cell, int gfxId, bool noDm, string pubKeyHex)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("pid", pid);
            w.WriteString("server", server ?? "");
            w.WriteString("character", character ?? "");
            if (breed > 0) w.WriteNumber("breed", breed);
            w.WriteNumber("arch_gid", archGid);
            w.WriteString("arch_name", archName ?? "");
            if (gfxId > 0) w.WriteNumber("gfx_id", gfxId);   // id d'ICÔNE du monstre (≠ genericId) -> vignette de l'embed
            w.WriteNumber("map_id", mapId);
            if (x is int xx) w.WriteNumber("x", xx);
            if (y is int yy) w.WriteNumber("y", yy);
            w.WriteNumber("cell", cell);
            if (noDm) w.WriteBoolean("no_dm", true);   // l'user a coupé le MP Discord perso -> le backend ne DM pas le finder (salon public + admin inchangés)
            w.WriteString("pubkey", pubKeyHex);
            w.WriteString("nonce", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            w.WriteNumber("server_time", ServerClock.NowMs());
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    // ---------------- push des scripts Lua chargés vers le panel (fiabilisé) ----------------
    // Le push reste best-effort MAIS ne doit plus être perdu en silence. Si la session n'est pas encore prête (course au
    // démarrage : on charge un .lua avant que /auth ait posé le jeton), on met le script EN ATTENTE et on le rejoue dès
    // que la session est établie (StartHeartbeat) ou re-établie (transition WS Connected). La file est clé par NAME
    // (= identité serveur (licence, name)) : dernier contenu gagne. Dédup par name pour ne pas spammer révisions ni
    // console sur un rechargement identique. Tout accès aux dictionnaires ci-dessous se fait SOUS _pushLock.
    private static readonly object _pushLock = new();
    private static readonly Dictionary<string, string> _pendingScripts = new(StringComparer.Ordinal);    // name -> dernier contenu à pousser
    private static readonly Dictionary<string, string> _lastPushedByName = new(StringComparer.Ordinal);   // name -> contenu déjà poussé avec succès (dédup)
    private static readonly Dictionary<string, string> _softFailNotified = new(StringComparer.Ordinal);   // name -> contenu pour lequel UNE ligne d'échec réseau a déjà été affichée (anti-spam)
    private static readonly SemaphoreSlim _flushGate = new(1, 1);                                          // un seul flush concurrent
    private const int MaxPendingScripts = 64;   // borne la file : une session jamais authentifiée ne peut pas la faire enfler
    private static readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);   // name en cours d'envoi : sérialise (pas de double envoi)
    private static int _pushEpoch;   // ++ à chaque Logout : cloisonne la file par licence (une ré-empile tardive d'une licence morte est ignorée)

    /// <summary>Verdict d'un push de script à afficher à l'utilisateur (console Lua du bot) : (message prêt, succès).
    /// Levé pour un push IMMÉDIAT comme pour un push DIFFÉRÉ (rejoué après établissement de session) — dans ce dernier
    /// cas seul un événement peut ramener l'issue à la console, le site d'appel d'origine ayant disparu. Le souscripteur
    /// (BotsViewModel) marshalle vers l'UI. Les chaînes ne portent QUE le nom du script (opsec : aucun détail d'impl).</summary>
    public static event Action<string, bool>? ScriptPushOutcome;

    /// <summary>Best-effort : pousse un script Lua CHARGÉ (fichier .lua) au backend — il rejoint la librairie du portail et
    /// l'inventaire admin, révisions gérées côté serveur (même name + contenu différent = nouvelle révision ; contenu
    /// identique = aucune révision). Si la session n'est pas encore prête (course au démarrage / reco), le script est MIS
    /// EN ATTENTE puis rejoué automatiquement — plus jamais perdu en silence. No-op réel sous bypass DEV (GIVRE-DEV).
    /// N'échoue JAMAIS le chargement local : toute erreur est loggée + surfacée en console, jamais propagée.</summary>
    public static async Task PushScriptAsync(string name, string content)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(content)) return;
        // Garde-fou local aux limites serveur (≤ 30 000 caractères ET ≤ 30 000 octets UTF-8) : refus PERMANENT -> on ne
        // met PAS en file (un retry serait voué au même refus), on le dit une fois.
        if (content.Length > 30_000 || System.Text.Encoding.UTF8.GetByteCount(content) > 30_000)
        {
            Logger.Warn($"Script « {name} » non poussé : dépasse 30 000 caractères/octets.");
            ScriptPushOutcome?.Invoke($"Script « {name} » non envoyé : dépasse 30 000 caractères.", false);
            return;
        }

        // Dédup client : contenu identique déjà poussé avec succès -> rien (ni round-trip, ni ligne console). Le serveur
        // dédoublonne aussi (même contenu = aucune révision), mais on évite ainsi l'aller-retour ET une ligne en double.
        int epoch;
        lock (_pushLock)
        {
            if (_lastPushedByName.TryGetValue(name, out var already) && already == content) return;
            epoch = _pushEpoch;
        }

        var client = _client;
        if (client is null || !client.IsSessionReady)
        {
            if (LocalEdition.Enabled && client is null) return;   // édition locale : rien à envoyer à un serveur
#if DEBUG
            // Bypass DEV (GIVRE-DEV) : session "autorisée" mais _client reste null tout du long -> vrai no-op (pas de
            // file, pas de message). En Release ce chemin est mort : _client null = "pas encore authentifié / déconnecté".
            if (client is null && IsAuthorized) return;
#endif
            // Session pas prête (jeton pas encore posé par AuthenticateAsync, ou déconnecté) -> on met en attente et on
            // rejouera au 1er établissement de session. Ferme la fenêtre de course « je me logge et charge trop vite ».
            Enqueue(name, content, epoch);
            return;
        }

        await TryPushWithRetryAsync(client, name, content, epoch).ConfigureAwait(false);
    }

    // Met (ou remplace) un script en attente : dernier contenu gagne, clé = name. Borne la file (drop-oldest best-effort)
    // pour qu'une session jamais authentifiée ne fasse pas enfler la RAM. Sous _pushLock.
    private static void Enqueue(string name, string content, int epoch)
    {
        lock (_pushLock)
        {
            if (epoch != _pushEpoch) return;   // licence changée depuis la capture -> ne pas empiler pour une AUTRE licence
            if (!_pendingScripts.ContainsKey(name) && _pendingScripts.Count >= MaxPendingScripts)
            {
                string? oldest = null;
                foreach (var k in _pendingScripts.Keys) { oldest = k; break; }   // 1re clé énumérée : abandon best-effort du plus ancien
                if (oldest is not null) _pendingScripts.Remove(oldest);
            }
            _pendingScripts[name] = content;
        }
    }

    // Push d'UN script avec retry borné + verdict console. Succès : mémorise (dédup), retire de la file, « envoyé
    // (révision N) ». Transitoire (réseau/edge Cloudflare via EnvelopeException, RATE_LIMITED, jeton tombé) : backoff +
    // jitter puis, après le dernier essai, re-met en file (rejoué à la prochaine transition WS Connected) + UNE ligne
    // d'échec douce par contenu. Terminal signé : VALIDATION_ERROR -> refus définitif (drop) ; SESSION_/HWID_/KEY_/ban
    // -> gardé en file pour que le prochain login rejoue. Aucune exception n'est propagée (best-effort). Opsec : les
    // lignes visibles ne portent que le nom + ✓/raison générique ; le détail brut reste dans license.log (Logger).
    private static async Task TryPushWithRetryAsync(LicenseClient client, string name, string content, int epoch)
    {
        // Dédup + sérialisation par name : un contenu déjà poussé ne repart pas (couvre AUSSI le chemin flush) ; un name
        // déjà en vol n'est pas envoyé deux fois (l'envoi en cours gère succès/ré-empilage).
        lock (_pushLock)
        {
            if (_lastPushedByName.TryGetValue(name, out var done) && done == content) { _pendingScripts.Remove(name); return; }
            if (!_inFlight.Add(name)) return;
        }
        try
        {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var r = await client.PushScriptAsync(name, content).ConfigureAwait(false);
                lock (_pushLock)
                {
                    _lastPushedByName[name] = content;
                    _pendingScripts.Remove(name);
                    _softFailNotified.Remove(name);
                }
                Logger.Info($"Script « {r.Name} » enregistré côté serveur (révision {r.Revision}).");
                ScriptPushOutcome?.Invoke($"Script « {name} » envoyé au panel (révision {r.Revision}).", true);
                return;
            }
            catch (LicenseServerException e) when (e.Code == "VALIDATION_ERROR")
            {
                // Refus MÉTIER définitif (contenu/nom rejeté) : réessayer ne changerait rien -> on abandonne et on le dit.
                lock (_pushLock) { _pendingScripts.Remove(name); _softFailNotified.Remove(name); }
                Logger.Warn($"Script « {name} » refusé par le serveur : {e.Code}.");
                ScriptPushOutcome?.Invoke($"Script « {name} » non envoyé : refusé par le panel.", false);
                return;
            }
            catch (LicenseServerException e) when (LicenseClient.IsTerminal(e.Code))
            {
                // Session inutilisable (SESSION_EXPIRED / HWID_MISMATCH / KEY_* / ban…) : on GARDE le script en file pour
                // que le prochain login le rejoue ; pas de retry ici (la session ne reviendra pas dans cette boucle).
                Enqueue(name, content, epoch);
                Logger.Warn($"Script « {name} » : session inutilisable ({e.Code}) -> gardé pour le prochain login.");
                return;
            }
            catch (Exception e) when (e is EnvelopeException
                                      || (e is LicenseServerException le && le.Code == "RATE_LIMITED")
                                      || e is InvalidOperationException)
            {
                // Transitoire : réseau / page d'erreur edge Cloudflare (EnvelopeException), débit dépassé (RATE_LIMITED),
                // ou jeton tombé entre le test et l'appel (InvalidOperationException). Backoff + jitter (style du code),
                // sauf au dernier essai. RATE_LIMITED = backoff plus long (le serveur throttle).
                // Jeton abandonné (reprise de session en cours) : réessayer sur CE client est inutile -> file tout de suite, rejouée
                // par la reprise sur le nouveau client (sinon le ré-empilage tardif raterait ce rejeu et resterait en file).
                if (client.IsAbandoned) { Enqueue(name, content, epoch); return; }
                bool rateLimited = e is LicenseServerException { Code: "RATE_LIMITED" };
                if (attempt < maxAttempts)
                {
                    int baseMs = rateLimited ? 1500 : 700;
                    await Task.Delay(baseMs * attempt + Random.Shared.Next(0, 400)).ConfigureAwait(false);
                    continue;
                }
                // Essais épuisés : re-file (rejoué à la reconnexion WS) + UNE ligne d'échec par CONTENU jusqu'au succès.
                Enqueue(name, content, epoch);
                bool notify;
                lock (_pushLock)
                {
                    notify = !_softFailNotified.TryGetValue(name, out var last) || last != content;
                    if (notify) _softFailNotified[name] = content;
                }
                Logger.Warn($"Push du script « {name} » échoué (transitoire) : {e.Message}");
                if (notify)
                    ScriptPushOutcome?.Invoke($"Script « {name} » pas encore envoyé au panel (réseau) — nouvel essai à la reconnexion ou au prochain lancement.", false);
                return;
            }
            catch (Exception e)
            {
                // Inattendu / code métier non catégorisé : on abandonne prudemment (pas de spin), trace en log, ligne générique.
                lock (_pushLock) { _pendingScripts.Remove(name); }
                Logger.Warn($"Push du script « {name} » échoué : {e.Message}");
                ScriptPushOutcome?.Invoke($"Script « {name} » non envoyé.", false);
                return;
            }
        }
        }
        finally { lock (_pushLock) _inFlight.Remove(name); }
    }

    /// <summary>Rejoue les scripts en attente une fois la session établie/re-établie. Ré-entrance protégée (_flushGate) :
    /// un seul flush à la fois. Prend un instantané ATOMIQUE de la file puis la vide, et pousse chaque script avec retry
    /// borné (TryPushWithRetryAsync re-met en file en cas d'échec transitoire). Le flush lui-même NE BOUCLE PAS : la
    /// reprise vient de la prochaine transition WS Connected (évite un spin re-file/re-flush serré).</summary>
    internal static async Task FlushPendingScriptsAsync()
    {
        if (!await _flushGate.WaitAsync(0).ConfigureAwait(false)) return;   // un flush déjà en cours -> on le laisse faire
        try
        {
            var client = _client;
            if (client is null || !client.IsSessionReady) return;

            List<KeyValuePair<string, string>> batch;
            int epoch;
            lock (_pushLock)
            {
                if (_pendingScripts.Count == 0) return;
                epoch = _pushEpoch;
                batch = new List<KeyValuePair<string, string>>(_pendingScripts);
                _pendingScripts.Clear();
            }
            foreach (var kv in batch)
            {
                if (client.IsSessionReady)
                    await TryPushWithRetryAsync(client, kv.Key, kv.Value, epoch).ConfigureAwait(false);
                else
                    Enqueue(kv.Key, kv.Value, epoch);   // session tombée en cours de drain -> ré-empile le reste, reprise à la reco
            }
        }
        finally { _flushGate.Release(); }
    }

    // JSON signé : { pid, server, character, sender_name, sender_character_id, breed, content, auto_reply_in, pubkey, nonce, server_time }.
    private static byte[] BuildPmPayloadBytes(int pid, string? server, string? character, string sender, long senderId, string content, int autoReplyIn, int breed, string pubKeyHex)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("pid", pid);
            w.WriteString("server", server ?? "");
            w.WriteString("character", character ?? "");
            w.WriteString("sender_name", sender);
            w.WriteNumber("sender_character_id", senderId);
            if (breed > 0) w.WriteNumber("breed", breed);   // classe du perso QUI A REÇU (le bot) : sa classe est connue, pas celle de l'expéditeur
            w.WriteString("content", content);
            if (autoReplyIn > 0) w.WriteNumber("auto_reply_in", autoReplyIn);   // fenêtre (s) -> compte à rebours + retrait boutons côté Discord
            w.WriteString("pubkey", pubKeyHex);
            w.WriteString("nonce", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            w.WriteNumber("server_time", ServerClock.NowMs());
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Interroge le "launcher control" Ikuware : GET /api/v1/client/version?v=&lt;version&gt; — endpoint PUBLIC pré-login,
    /// mais RÉPONSE SIGNÉE (enveloppe Ed25519 {payload,signature} + server_time, MÊME clé que /auth). On vérifie la
    /// signature + la fraîcheur (±5 min) AVANT de faire confiance. L'opérateur active un KILL SWITCH global ou un
    /// plancher de version + un message. **FAIL-CLOSED** : injoignable / signature invalide / horloge hors tolérance /
    /// illisible => verdict de BLOCAGE (comme un kill), NON vérifié (Verified=false ; le runtime tolère jusqu'au TTL,
    /// le boot bloque tout de suite). Le principe d'un kill switch : activé OU injoignable => tout se coupe.
    /// </summary>
    public static async Task<VersionVerdict> CheckVersionAsync(CancellationToken ct = default)
    {
        var pubHex = AppSecrets.ServerPublicKeyHex;
        if (pubHex.Length != 64)
        {
#if DEBUG
            return VersionVerdict.Permissive;   // DEV sans clé publique : rien à vérifier -> on laisse (DEBUG uniquement)
#else
            return VersionVerdict.FailClosed(Loc.T("L.Control.Unreachable"));   // prod sans clé = aucune preuve possible
#endif
        }
        var baseUrl = AppSecrets.BaseUrl.TrimEnd('/');
        var pubKey = Convert.FromHexString(pubHex);
        // Cloudflare (plan gratuit) : une réponse edge NON signée (5xx/429/page HTML de challenge) revient !Verified.
        // On la RÉ-ESSAIE (backoff+jitter) au lieu de la confondre avec un kill. Un verdict SIGNÉ (Verified=true : ok
        // OU kill/outdated authentique) est renvoyé IMMÉDIATEMENT -> fail-closed intact (après N essais non signés -> bloque).
        VersionVerdict last = VersionVerdict.FailClosed(Loc.T("L.Control.Unreachable"));
        for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
        {
            if (attempt > 0)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt + Random.Shared.Next(0, 400)), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GivreClient/" + AppVersion);   // allowlist WAF Cloudflare
                // n = nonce NEUF renvoyé en écho dans le payload signé (serveur ≥ volet D) : une réponse qui le porte ne peut être ni
                // forgée ni rejouée -> elle peut recaler l'horloge dès le démarrage (PC à l'heure fausse). Serveur plus ancien : pas
                // d'écho (nonce null), règle d'avant.
                var n = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                using var resp = await http.GetAsync($"{baseUrl}/version?v={Uri.EscapeDataString(AppVersion)}&n={n}", ct).ConfigureAwait(false);
                var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                // Enveloppe SIGNÉE {payload,signature} : MÊME vérif que /auth (Ed25519 sur les octets bruts du payload) + server_time.
                var payload = VerifyVersionEnvelope(raw, pubKey, n);
                RequestSigning.Note(payload);   // client_signing : /auth et /mapping signés dès qu'il est annoncé
                bool kill = payload.TryGetProperty("kill", out var k) && k.ValueKind == JsonValueKind.True;
                // Plancher de version vérifié AUSSI ici : le serveur calcule `outdated` d'après ?v=, qu'un proxy local peut réécrire.
                bool outdated = (payload.TryGetProperty("outdated", out var o) && o.ValueKind == JsonValueKind.True)
                                || BelowMinVersion(Str(payload, "min_version"));
                // Pointeur d'auto-update (build courant) — enveloppe SIGNÉE : le sha256 est authentifié par Ed25519.
                bool mandatory = payload.TryGetProperty("mandatory", out var md) && md.ValueKind == JsonValueKind.True;
                long size = payload.TryGetProperty("size", out var szEl) && szEl.ValueKind == JsonValueKind.Number && szEl.TryGetInt64(out var szv) ? szv : 0;
                var verdict = new VersionVerdict(!(kill || outdated), kill, outdated, Str(payload, "min_version"), Str(payload, "current_version"), Str(payload, "message"), true,
                    Str(payload, "download_url"), Str(payload, "sha256"), size, mandatory, Str(payload, "notes"));
                if (!verdict.Ok) Logger.Warn($"Launcher control : kill={kill} outdated={outdated} (min={verdict.MinVersion})");
                return verdict;   // preuve SIGNÉE fraîche (ok ou kill/outdated) -> AUTORITÉ, immédiat
            }
            catch (Exception e)
            {
                // Non signé / injoignable / illisible -> transport, PAS un verdict. On ré-essaie (borné).
                Logger.Warn($"Launcher control non vérifiable ({e.Message}) -> essai {attempt + 1}/3.");
                // Réponse AUTHENTIQUE mais hors ±5 min : c'est l'horloge du PC qui est fausse, pas le serveur injoignable.
                last = VersionVerdict.FailClosed(Loc.T(e is ClockSkewException ? "L.Auth.DateTime" : "L.Control.Unreachable"));
            }
        }
        return last;   // que du non-signé après N essais -> FAIL-CLOSED (l'appelant bloque)
    }

    // Vérifie l'enveloppe signée de /version : Ed25519 sur les OCTETS BRUTS du payload + fraîcheur server_time (±5 min).
    // Lève sur enveloppe/signature/horloge invalide -> l'appelant retombe en FAIL-CLOSED.
    private static JsonElement VerifyVersionEnvelope(string raw, byte[] pubKey, string? sentNonce = null)
    {
        using var envDoc = JsonDocument.Parse(raw);
        var env = envDoc.RootElement;
        if (!env.TryGetProperty("payload", out var p) || !env.TryGetProperty("signature", out var s)
            || p.ValueKind != JsonValueKind.String || s.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("enveloppe invalide");
        var payloadBytes = Convert.FromBase64String(p.GetString()!);
        var signature = Convert.FromBase64String(s.GetString()!);
        if (signature.Length != 64 || !Cryptographic.Ed25519.CheckValid(signature, payloadBytes, pubKey))
            throw new InvalidOperationException("signature invalide");
        JsonElement payload;
        using (var payDoc = JsonDocument.Parse(payloadBytes)) payload = payDoc.RootElement.Clone();
        // La MÊME clé serveur signe /auth, /mapping, les erreurs et les trames WS : sans ce contrôle de FORME, un proxy local
        // (autorité racine maison) pouvait servir n'importe quelle réponse signée fraîche (erreur de /auth, trame WS) comme un
        // « OK » de /version, et neutraliser pour de bon le kill switch et la version minimale. Refus = non vérifiable (fail-closed).
        if (!IsVersionPayload(payload, sentNonce)) throw new InvalidOperationException("payload inattendu");
        if (!payload.TryGetProperty("server_time", out var st) || !st.TryGetInt64(out var serverMs))
            throw new InvalidOperationException("server_time manquant");
        if (Math.Abs(serverMs - ServerClock.NowMs()) >= 300_000)
        {
            // Hors ±5 min : accepté SEULEMENT si le payload renvoie EXACTEMENT notre n neuf (réponse vivante, ni forgée ni
            // rejouée) -> horloge serveur recalée ; sinon l'horloge du PC est fausse (message dédié), comme avant.
            bool echoed = sentNonce is { Length: > 0 } && payload.TryGetProperty("nonce", out var ne) && ne.ValueKind == JsonValueKind.String
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ne.GetString()!), Encoding.ASCII.GetBytes(sentNonce));
            if (!echoed) throw new ClockSkewException();
            ServerClock.Calibrate(serverMs);
            Diag.Log(206, ServerClock.OffsetMs / 1000);
        }
        return payload;
    }

    private sealed class ClockSkewException() : InvalidOperationException("server_time hors tolérance");

    // Forme EXACTE d'une réponse /version (serveur depuis le canal d'auto-update, 2026-08-23) : status « ok », kill / outdated /
    // mandatory booléens, min_version / current_version présents (chaîne ou null), aucun champ d'un autre point (erreur, trame
    // WS, session, mapping), nonce null (serveur sans ?n=) ou EXACTEMENT notre n.
    private static bool IsVersionPayload(JsonElement p, string? sentNonce)
    {
        static bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False;
        static bool StrOrNull(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Null;
        if (p.ValueKind != JsonValueKind.Object || Str(p, "status") != "ok") return false;
        if (!Bool(p, "kill") || !Bool(p, "outdated") || !Bool(p, "mandatory")) return false;
        if (!StrOrNull(p, "min_version") || !StrOrNull(p, "current_version")) return false;
        foreach (var k in new[] { "code", "type", "command", "session", "license", "artefacts", "valid_until" })
            if (p.TryGetProperty(k, out _)) return false;
        if (p.TryGetProperty("nonce", out var n) && n.ValueKind != JsonValueKind.Null)
            return sentNonce is { Length: > 0 } && n.ValueKind == JsonValueKind.String
                   && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(n.GetString()!), Encoding.ASCII.GetBytes(sentNonce));
        return true;
    }

    // Version courante < plancher signé ? Comparaison sur 4 composantes (1.1.8 == 1.1.8.0) ; illisible -> jamais bloquant.
    private static bool BelowMinVersion(string? min)
    {
        static (int, int, int, int)? Parse(string? s) => Version.TryParse(s, out var v)
            ? (v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0)) : null;
        return Parse(min) is { } m && Parse(AppVersion) is { } a && a.CompareTo(m) < 0;
    }

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Blocage RUNTIME du launcher control : l'opérateur a activé le kill switch (ou imposé une MAJ) PENDANT
    /// que l'app tourne. L'UI DOIT afficher l'écran de blocage (ControlOverlay). Levé depuis un thread de fond.</summary>
    public static event Action<VersionVerdict>? ControlBlocked;

    /// <summary>Nouvelle version exigée alors que l'app est DÉJÀ en cours d'utilisation (connexion réussie dans ce lancement,
    /// manuelle ou auto) : proposée (maintenant / au prochain lancement) au lieu de couper les bots. Une fois par version
    /// cible. Au prochain lancement, le contrôle du démarrage la rend obligatoire comme avant. Levé depuis un thread de fond.</summary>
    public static event Action<VersionVerdict>? UpdateOffered;

    // Connexion réussie dans CE lancement de l'app : une MAJ arrivée ensuite ne coupe plus les bots (elle est proposée).
    private static volatile bool _usedThisLaunch;
    private static string? _offeredTarget;

    /// <summary>Version périmée pendant l'utilisation : proposée au lieu de bloquer (true). Kill switch, ou app pas encore
    /// utilisée (écran de connexion, démarrage) : blocage comme avant (false).</summary>
    private static bool OfferInsteadOfBlock(VersionVerdict v)
    {
        if (v.Kill || !v.Outdated || !_usedThisLaunch) return false;
        string target = v.MinVersion ?? v.CurrentVersion ?? "";
        if (Interlocked.Exchange(ref _offeredTarget, target) != target)
        {
            Diag.Log(503, v.Verified ? 1 : 0);
            Logger.Warn($"Launcher control : version {target} exigée pendant l'utilisation -> proposée (bots maintenus)");
            UpdateOffered?.Invoke(v);
        }
        return true;
    }

    /// <summary>Le serveur demande de DÉCONNECTER un bot (commande DISCONNECT_BOT, panel "Sessions") : arg = pid du
    /// process à arrêter (= chemin du bouton "Arrêter"). Levé depuis un thread de fond -> marshaller vers l'UI.</summary>
    /// <summary>DISCONNECT_BOT reçu : (pid, nonce de la commande, nom de la commande) — le consommateur accuse le résultat
    /// via <see cref="AckScriptControlAsync"/> (le portail affiche « appliqué / refusé »).</summary>
    public static event Action<int, string?, string>? DisconnectBotRequested;

    /// <summary>Commande serveur SEND_PM (réponse manuelle Discord/portail) : (pid, target, content, via) -> l'app forge
    /// et injecte un MP privé en jeu. `via` (optionnel : "discord"/"panel"/...) sert au libellé de la notification.</summary>
    public static event Action<int, string, string, string?>? SendPmRequested;

    /// <summary>Commande serveur PM_ACTION (boutons Discord « Réponse auto » / « Ignorer ») : (pid, target, action) où
    /// action = "auto" (déclenche la réponse auto tout de suite) ou "ignore" (annule le repli, n'envoie rien).</summary>
    public static event Action<int, string, string>? PmActionRequested;

    /// <summary>Commande serveur SHOW_NOTIFICATION { message } : l'app doit AFFICHER `message` à l'utilisateur (toast +
    /// centre de notifs). Levé depuis un thread de fond -> le souscripteur marshalle vers l'UI.</summary>
    public static event Action<string>? ShowNotificationRequested;

    /// <summary>Commande serveur START_SCRIPT/STOP_SCRIPT : (pid, start, script, nonce, commandName). start=true -> (re)lance
    /// (script fourni = charge+lance, sinon le script chargé) ; false -> arrête. nonce/commandName servent à l'accusé
    /// (AckScriptControlAsync). Levé depuis un thread de fond -> le souscripteur marshalle vers l'UI + repush télémétrie.</summary>
    public static event Action<int, bool, string?, string?, string?>? ScriptControlRequested;

    /// <summary>Accusé d'une commande de pilotage de script (après traitement côté UI) sur le canal `command_ack`.</summary>
    public static Task AckScriptControlAsync(string? nonce, string commandName, bool ok, string? error)
        => AckCommandAsync(new ServerCommand(commandName, null, Nonce: nonce), ok, error);

    // UN accusé par commande (nonce) côté serveur : il garde le 1er reçu. Un 2e du même nonce sur le MÊME socket est un REJEU
    // (refus + socket fermé) ; sur un AUTRE socket ou une session reprise, il est ignoré en silence. Le serveur n'accuse jamais
    // réception d'un envoi : un accusé « parti » (remis au système) peut se perdre dans un socket mort pas encore détecté
    // (coupure réseau). Il reste donc EN VOL jusqu'à la preuve de réception : la réponse à un hello/beat envoyé APRÈS lui sur
    // le même socket (ordre du fil + traitement séquentiel serveur). Socket mort avant cette preuve -> remis en file et renvoyé
    // sur le socket suivant (même client, ou nouvelle session après une reprise : accepté par le serveur depuis 5a0bd80a),
    // JAMAIS sur celui qui l'a porté. Fenêtre : 9 min depuis la réception de la commande (serveur : 10 min depuis l'envoi).
    // Charge utile reconstruite à chaque envoi (server_time frais : contrôle de dérive de 5 min).
    private static readonly ConcurrentDictionary<string, long> _acked = new();
    private static readonly ConcurrentDictionary<string, long> _cmdReceived = new();   // nonce -> tick de RÉCEPTION
    // SentGen/SentSeq : socket et n° d'ordre du dernier envoi (0 = jamais parti). Plus lié à un LicenseClient : une file survit
    // à la reprise de session et à un nouveau login (le serveur ignore en silence un accusé d'une autre licence).
    private sealed record PendingAck(string Nonce, string Command, bool Ok, string? Error, long Tick, int SentGen = 0, long SentSeq = 0, int Sends = 0);
    private static readonly List<PendingAck> _pendingAcks = new();    // à (r)envoyer
    private static readonly List<PendingAck> _inflightAcks = new();   // partis, réception pas encore prouvée
    private static int _lastConfirmGen;                               // dernière preuve de réception (sous _ackLock)
    private static long _lastConfirmSeq;
    private static readonly object _ackLock = new();
    private const long AckWindowMs = 9 * 60_000;
    private const int MaxQueuedAcks = 64;
    private const int MaxAckSends = 3;   // un accusé jamais prouvé reçu après 3 sockets est abandonné (607) : jamais de boucle

    // Pousse un accusé { nonce (= celui de la commande, tel quel), command, ok, error? } sur le canal signé "command_ack".
    private static async Task AckCommandAsync(ServerCommand cmd, bool ok, string? error)
    {
        var client = _client;
        var nonce = cmd.Nonce;
        if (client is null || nonce is null || nonce.Length is < 32 or > 64 || !nonce.All(Uri.IsHexDigit)) return;
        long now = Environment.TickCount64;
        foreach (var kv in _acked)
            if (now - kv.Value > 10 * 60_000) _acked.TryRemove(kv.Key, out _);
        if (!_acked.TryAdd(nonce, now)) return;   // déjà accusé (ou en attente) : jamais deux fois
        long received = _cmdReceived.TryRemove(nonce, out var rt) ? rt : now;   // fenêtre comptée depuis la réception
        var ack = new PendingAck(nonce, cmd.Name, ok, error, received);
        var sent = await TrySendAckAsync(client, ack).ConfigureAwait(false);
        Diag.Log(sent is not null ? 601 : 602, Diag.Cmd(cmd.Name), ok ? 1 : 0);
        lock (_ackLock)
        {
            if (sent is { } s) AddInflight(ack with { SentGen = s.Gen, SentSeq = s.Seq, Sends = 1 });
            else AddCapped(_pendingAcks, ack);
        }
        if (sent is not null) client.RequestEarlyBeat();   // preuve de réception en ~1 s plutôt qu'au beat suivant (60 s)
    }

    // Sous _ackLock. Déjà prouvé reçu (la réponse au beat suivant est arrivée avant l'enregistrement) -> rien à suivre.
    private static void AddInflight(PendingAck a)
    {
        if (a.SentGen == _lastConfirmGen && a.SentSeq < _lastConfirmSeq) return;
        AddCapped(_inflightAcks, a);
    }

    private static void AddCapped(List<PendingAck> list, PendingAck a)
    {
        list.Add(a);
        if (list.Count > MaxQueuedAcks) list.RemoveAt(0);
    }

    // Preuve de réception (réponse au hello/beat n° seq du socket gen) : tout accusé parti avant sur ce socket est arrivé.
    private static void OnBeatConfirmed(int gen, long seq)
    {
        lock (_ackLock)
        {
            _lastConfirmGen = gen;
            _lastConfirmSeq = seq;
            _inflightAcks.RemoveAll(a => a.SentGen == gen && a.SentSeq < seq);
        }
    }

    // Nouveau socket prêt : les accusés partis sur un AUTRE socket (forcément mort : un seul socket à la fois) sans preuve de
    // réception sont remis en file pour celui-ci. Doublon possible si la preuve s'est seulement perdue : ignoré par le serveur.
    private static void RequeueUncertainAcks(LicenseClient client)
    {
        int gen = client.SocketGen, n = 0, dropped = 0;
        if (gen == 0) return;
        long now = Environment.TickCount64;
        lock (_ackLock)
        {
            for (int i = _inflightAcks.Count - 1; i >= 0; i--)
            {
                var a = _inflightAcks[i];
                if (a.SentGen == gen) continue;
                _inflightAcks.RemoveAt(i);
                if (now - a.Tick > AckWindowMs) continue;
                if (a.Sends >= MaxAckSends) { dropped++; continue; }
                AddCapped(_pendingAcks, a);
                n++;
            }
        }
        if (n > 0) Diag.Log(606, n);
        if (dropped > 0) Diag.Log(607, dropped);
    }

    private static async Task<(int Gen, long Seq)?> TrySendAckAsync(LicenseClient client, PendingAck a)
    {
        try
        {
            var (sk, pk) = ClientKey();
            using var ms = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("nonce", a.Nonce);
                w.WriteString("command", a.Command);
                w.WriteBoolean("ok", a.Ok);
                if (!string.IsNullOrEmpty(a.Error)) w.WriteString("error", a.Error!.Length > 120 ? a.Error!.Substring(0, 120) : a.Error);
                w.WriteString("pubkey", Convert.ToHexString(pk).ToLowerInvariant());
                w.WriteString("nonce_c", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                w.WriteNumber("server_time", ServerClock.NowMs());
                w.WriteEndObject();
            }
            var bytes = ms.ToArray();
            return await client.PushUploadTrackedAsync("command_ack", bytes, Cryptographic.Ed25519.Signature(bytes, sk, pk)).ConfigureAwait(false);
        }
        catch (Exception e) { Logger.Warn($"command_ack échoué : {e.Message}"); return null; }
    }

    private static bool HasPendingAcks()
    {
        lock (_ackLock) return _pendingAcks.Count > 0;
    }

    // Reconnexion WS (ou tick de télémétrie) : rejoue les accusés en file, encore dans la fenêtre, sur le client COURANT et
    // jamais sur le socket qui les a déjà portés (rejeu pour le serveur). AU COMPTE-GOUTTES (max par appel ; le reste part aux
    // ticks suivants, 2 par 1,5 s ; et le client bride tous ses envois sous le débit du serveur) : le serveur coupe sans
    // prévenir un socket qui dépasse 10 envois d'avance + 2/s ; tout vider d'un coup tuait le nouveau socket avant toute preuve
    // de réception -> reconnexion, re-file, re-vidage… pendant 9 min.
    private static async Task FlushPendingAcksAsync(LicenseClient client, int max)
    {
        if (!ReferenceEquals(client, _client)) return;
        List<PendingAck> batch;
        long now = Environment.TickCount64;
        int gen = client.SocketGen;
        lock (_ackLock)
        {
            _pendingAcks.RemoveAll(a => now - a.Tick > AckWindowMs);
            batch = _pendingAcks.Where(a => a.SentGen != gen).Take(max).ToList();
            foreach (var a in batch) _pendingAcks.Remove(a);
        }
        int replayed = 0;
        foreach (var a in batch)
        {
            var s = await TrySendAckAsync(client, a).ConfigureAwait(false);
            lock (_ackLock)
            {
                if (s is { } sent) { AddInflight(a with { SentGen = sent.Gen, SentSeq = sent.Seq, Sends = a.Sends + 1 }); replayed++; }
                else AddCapped(_pendingAcks, a);   // toujours pas parti : prochain socket / prochain tick
            }
        }
        if (replayed > 0) { client.RequestEarlyBeat(); Diag.Log(603, replayed, batch.Count); }
    }

    // Réévalue les droits à la PROCHAINE échéance (licence ou droit). Sans ça, un droit expirant en cours de session (ex.
    // « multi » 30 j dans une licence 90 j) restait actif pour l'UI et les gardes (bots en trop jamais arrêtés) jusqu'à ce
    // que le serveur tue TOUTE la session (plafond de bots dépassé).
    private static Timer? _expiryTimer;
    private static void ArmExpiryTimer()
    {
        try { _expiryTimer?.Dispose(); } catch { }
        _expiryTimer = null;
        if (_client?.License is not { } lic) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long next = long.MaxValue;
        if (lic.ExpiresAtMs > now) next = lic.ExpiresAtMs;
        foreach (var p in lic.Permissions)
            if (p.ExpiresAtMs is long e && e > now && e < next) next = e;
        if (next == long.MaxValue) return;
        // Plafond du Timer (~49 j) : borné à 20 j ; à l'échéance intermédiaire on réévalue (sans effet) puis on ré-arme.
        long dueMs = Math.Min(next - now + 1000, 20L * 24 * 3600 * 1000);
        _expiryTimer = new Timer(_ => { Diag.Log(403); RaisePermissionsChanged(notifyDelta: true); ArmExpiryTimer(); }, null,
                                 TimeSpan.FromMilliseconds(dueMs), Timeout.InfiniteTimeSpan);
    }

    private static CancellationTokenSource? _versionWatchCts;

    /// <summary>Surveillance PÉRIODIQUE du launcher control (GET /version SIGNÉ) tant que l'app tourne. Sans ça, un
    /// client DÉJÀ lancé AVANT l'activation du kill switch passait au travers (faille V6 boot-only). Modèle "bail de
    /// feu vert" : autorisé tant qu'une preuve SIGNÉE fraîche (kill:false) a été vue depuis &lt; TTL.
    ///  - kill / outdated SIGNÉ  -> blocage IMMÉDIAT ;
    ///  - injoignable / non vérifiable -> blocage APRÈS le TTL (tolère un blip réseau, mais FAIL-CLOSED si réellement
    ///    injoignable : « activé OU injoignable => tout se coupe »).
    /// Le push WS control_kill (cf StartHeartbeat) court-circuite ce poll pour un kill instantané. Idempotent.</summary>
    public static void StartVersionWatch(int intervalSeconds = 45)
    {
        if (_versionWatchCts is not null) return;
        _versionWatchCts = new CancellationTokenSource();
        var ct = _versionWatchCts.Token;
        _ = Task.Run(async () =>
        {
            // Fail-closed après ~2,5 min d'échecs CONTINUS (≥ 3 polls non vérifiables d'affilée). Mesuré depuis le 1er échec et non
            // depuis la dernière preuve : au réveil d'une veille (1 h), le 1er poll part souvent avant le retour du réseau -> avant,
            // blocage IMMÉDIAT (« dernière preuve » vieille d'1 h) alors que la reprise de session allait réussir.
            long firstFailMs = 0;
            int consecutiveFails = 0;
            // Série « ciblée » : échecs de /version comptés SEULEMENT pendant que le reste du serveur répond. Le blocage dur l'exige
            // entière (≥ 3 polls, > 150 s) : juste après une longue coupure, un poll raté pendant le retour du réseau ne suffit plus.
            long selFirstFailMs = 0;
            int selFails = 0;
            int lastVerdict = 0;
            const long ttlMs = 150_000;
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 300)), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                var v = await CheckVersionAsync(ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                int vk = !v.Verified ? 3 : v.Ok ? 0 : v.Kill ? 1 : 2;   // 0 ok, 1 kill, 2 version périmée, 3 non vérifiable
                if (vk != lastVerdict) { Diag.Log(500, vk); lastVerdict = vk; }
                if (v.Verified)
                {
                    // kill confirmé signé -> coupe tout de suite ; version périmée pendant l'utilisation -> proposée, bots maintenus.
                    if (!v.Ok && !OfferInsteadOfBlock(v)) { BlockControl(v); break; }
                    firstFailMs = 0; consecutiveFails = 0;   // preuve fraîche valide -> bail renouvelé
                    selFirstFailMs = 0; selFails = 0;
                    SetControlUnverified(false);
                }
                else
                {
                    if (firstFailMs == 0) firstFailMs = now;
                    consecutiveFails++;
                    if (ShouldHardBlockUnverified(_client)) { if (selFirstFailMs == 0) selFirstFailMs = now; selFails++; }
                    else { selFirstFailMs = 0; selFails = 0; }
                    if (consecutiveFails >= 3 && now - firstFailMs > ttlMs)   // injoignable/non vérifiable durablement -> fail-closed
                    {
                        // Le reste du serveur répond (battement signé récent) alors que /version se tait : blocage CIBLÉ (MITM
                        // qui masquerait un kill switch) -> blocage dur, comme avant. Tout le serveur muet (coupure réseau,
                        // panne) : botting SUSPENDU mais récupérable, on continue d'interroger ; la preuve signée suivante lève
                        // la pause (avant : écran de blocage définitif jusqu'au redémarrage, reprise de session interdite).
                        if (selFails >= 3 && now - selFirstFailMs > ttlMs) { BlockControl(v); break; }
                        SetControlUnverified(true);
                    }
                }
            }
        });
    }

    /// <summary>Contrôle de version injoignable au-delà du délai : blocage DUR (true) si le reste du serveur répond encore (réponse
    /// signée depuis moins de 150 s : /version seule est masquée, MITM possible) ou s'il n'y a pas de session ; sinon (tout le
    /// serveur muet : coupure) pause récupérable.</summary>
    internal static bool ShouldHardBlockUnverified(LicenseClient? lc) =>
        lc is null || (!lc.IsAbandoned && Environment.TickCount64 - lc.LastServerOkTick < 150_000);

    /// <summary>Blocage launcher control : coupe le botting (IsAuthorized=false) + notifie l'UI (ControlBlocked -> écran
    /// de blocage) + les VM (grisage). Appelé par le poll runtime ET le push WS control_kill instantané.</summary>
    internal static void BlockControl(VersionVerdict v)
    {
        ForbidResume();   // kill switch / control_kill / version périmée : jamais de reprise silencieuse ensuite
        Diag.Log(501, v.Kill ? 1 : 0, v.Outdated ? 1 : 0, v.Verified ? 1 : 0);
        IsAuthorized = false;
        MappingCatalog.Clear();   // PURGE le joyau : le décodeur devient aveugle -> le bot ne peut plus agir (pas qu'un bool).
        RaisePermissionsChanged();
        Logger.Warn($"Launcher control : BLOCAGE (kill={v.Kill} outdated={v.Outdated} verified={v.Verified})");
        ControlBlocked?.Invoke(v);
    }

    public static void StopVersionWatch()
    {
        _versionWatchCts?.Cancel();
        _versionWatchCts?.Dispose();
        _versionWatchCts = null;
    }

    /// <summary>Valide une cle de licence aupres du serveur. Non bloquant pour l'UI. Demarre le heartbeat sur succes.</summary>
    public static async Task<AuthResult> ValidateAsync(string licenseKey)
    {
        var key = (licenseKey ?? "").Trim();
        if (string.IsNullOrEmpty(key))
            return new AuthResult(false, Loc.T("L.Login.ErrInvalid"));

#if DEBUG
        // Bypass DEV (build Debug uniquement) : teste l'UI sans serveur. ABSENT des builds Release/prod.
        if (key == "GIVRE-DEV")
        {
            Logger.Info("DEV login bypass (build DEBUG) — verification licence ignoree");
            await Task.Delay(500);
            _lastKey = key;
            IsAuthorized = true;
            RaisePermissionsChanged();
            return new AuthResult(true, "");
        }
#endif

        if (!AppSecrets.IsConfigured)
        {
            Logger.Warn("Serveur de licence non configure (cle publique absente)");
            return new AuthResult(false, Loc.T("L.Auth.Unavailable"));
        }

        return await Task.Run(async () =>
        {
            try
            {
                CancelResume();                               // un login manuel remplace toute reprise en cours
                Interlocked.Increment(ref _sessionGen);       // une reprise qui aboutirait après ce login est invalidée
                StopHeartbeat();
                _client?.Dispose();
                _client = NewClient();
                await _client.AuthenticateAsync(key).ConfigureAwait(false);
                NoteKeyBinding(_client);

                ResetResumeGuards();
                _lastKey = key;
                IsAuthorized = true;
                _usedThisLaunch = true;   // app en cours d'utilisation : une MAJ arrivée ensuite sera proposée, pas imposée
                RaisePermissionsChanged();
                StartHeartbeat();
                _ = FlushPendingScriptsAsync();   // jeton posé : rejoue les scripts chargés avant que la session soit prête (course au démarrage)
                // Récupère + déchiffre le mapping EN MÉMOIRE avant de rendre la main. On ATTEND qu'il soit PRÊT (retries
                // bornés) car le stream peut être lent/incomplet la 1re fois -> sinon un lancement juste après login tombe
                // sur « mapping manquant » (bug rapporté « je me logge et lance trop vite »). Le modal de connexion reste
                // affiché pendant ce temps. Best-effort : si le stream échoue durablement (aléa réseau), on rend la main
                // quand même — le LANCEMENT re-tente derrière son propre modal (EnsureMappingReadyAsync) : on ne bloque
                // jamais le login sur un aléa réseau, mais on maximise les chances que le protocole soit déjà là.
                Logger.Info("Licence validee — recuperation du mapping…");
                for (int attempt = 0; attempt < 4 && !MappingCatalog.IsReady && !SigningBlocked; attempt++)
                {
                    await MappingCatalog.EnsureFetchedAsync().ConfigureAwait(false);
                    if (MappingCatalog.IsReady) break;
                    await Task.Delay(700).ConfigureAwait(false);
                }
                Logger.Info(MappingCatalog.IsReady ? "Acces accorde (mapping prêt)" : "Acces accorde (mapping PAS encore prêt -> retenté au lancement)");
                return new AuthResult(true, "");
            }
            catch (LicenseServerException e)
            {
                Logger.Warn($"Licence refusee — {e.Code}");
                return new AuthResult(false, Localize(e), e.Code is "INVALID_KEY" or "KEY_EXPIRED" or "KEY_REVOKED" or "USER_BANNED");
            }
            catch (EnvelopeException e)
            {
                Logger.Error($"Reponse serveur illisible : {e.Message}");
                return new AuthResult(false, LocalizeEnvelope(e.Message));
            }
            catch (Exception e)
            {
                Logger.Error($"Erreur serveur de licence : {e.Message}");
                return new AuthResult(false, Loc.T("L.Auth.Unavailable"));
            }
        });
    }

    private static void StartHeartbeat()
    {
        StopHeartbeat();
        var client = _client;
        if (client is null) return;
        _heartbeatCts = new CancellationTokenSource();
        var ct = _heartbeatCts.Token;
        var terminated = false;

        // Droits mis à jour EN DIRECT : push WS "license_update" (édition des droits côté panel) ou lecture des
        // permissions du heartbeat (repli ≤60 s) -> propage à l'UI (grisage) + moteur (clamp au prochain push), SANS re-login.
        // Les handlers d'un client REMPLACÉ (reprise de session) ne doivent plus rien faire : garde par instance.
        bool Current() => ReferenceEquals(client, _client);
        client.PermissionsChanged += () => { if (!Current()) return; Logger.Info("Droits de licence mis à jour (temps-réel)"); RaisePermissionsChanged(notifyDelta: true); ArmExpiryTimer(); };
        ArmExpiryTimer();
        // Coupure qui a dépassé la grâce serveur (SESSION_EXPIRED + lapsed, réponse à NOTRE requête) -> reprise silencieuse.
        client.Lapsed += src => OnLapsed(client, src);
        // Session liée à une autre clé cliente (/mapping ou envoi WS) : un seul nouvel /auth, arrêt si ça se reproduit.
        client.KeyMismatch += src => OnKeyMismatch(client, src);
        // Preuve de réception des accusés (générations de socket uniques : aucun risque de confondre deux clients).
        client.BeatConfirmed += OnBeatConfirmed;
        // Lien perdu en repli HTTP (coupure longue) : botting suspendu, licence gardée ; levé au retour du lien.
        client.LinkDownChanged += down => { if (Current()) SetLinkDown(down); };

        // Kill switch INSTANTANÉ : push WS "control_kill" (launcher control activé côté panel) -> blocage global immédiat.
        // La frame est déjà vérifiée (signature Ed25519 + server_time) par le pipeline WS. Une frame "clear" (kill levé) est ignorée.
        client.ControlKill += info =>
        {
            if (!info.Kill && !info.Outdated) return;
            var pushed = new VersionVerdict(false, info.Kill, info.Outdated, info.MinVersion, info.CurrentVersion, info.Message, Verified: true);
            if (!OfferInsteadOfBlock(pushed)) BlockControl(pushed);
        };

        // Commande serveur (panel "Sessions") : DISCONNECT_BOT { pid } -> déconnecte ce bot (= bouton "Arrêter").
        client.Command += cmd =>
        {
            if (!Current()) return;
            // Fenêtre d'accusé du serveur = 10 min depuis l'ENVOI de la commande : on compte nos 9 min depuis sa RÉCEPTION.
            if (cmd.Nonce is { Length: > 0 } rn)
            {
                long rnow = Environment.TickCount64;
                foreach (var kv in _cmdReceived)
                    if (rnow - kv.Value > 10 * 60_000) _cmdReceived.TryRemove(kv.Key, out _);
                _cmdReceived[rn] = rnow;
            }
            if (cmd.Name == "DISCONNECT_BOT" && cmd.Pid is int p && p > 0)
            {
                Logger.Info($"Commande serveur : DISCONNECT_BOT pid={p}");
                DisconnectBotRequested?.Invoke(p, cmd.Nonce, cmd.Name);
            }
            // SEND_PM { pid, target, content, via? } : réponse manuelle (Discord/portail) -> injectée en jeu. pid peut être 0/null.
            else if (cmd.Name == "SEND_PM" && !string.IsNullOrWhiteSpace(cmd.Target) && !string.IsNullOrWhiteSpace(cmd.Content))
            {
                Logger.Info($"Commande serveur : SEND_PM pid={cmd.Pid} target={cmd.Target} via={cmd.Via}");
                SendPmRequested?.Invoke(cmd.Pid ?? 0, cmd.Target!, cmd.Content!, cmd.Via);
            }
            // PM_ACTION { pid, target, action } : boutons Discord « Réponse auto » (auto) / « Ignorer » (ignore).
            else if (cmd.Name == "PM_ACTION" && !string.IsNullOrWhiteSpace(cmd.Target) && !string.IsNullOrWhiteSpace(cmd.Action))
            {
                Logger.Info($"Commande serveur : PM_ACTION pid={cmd.Pid} target={cmd.Target} action={cmd.Action}");
                PmActionRequested?.Invoke(cmd.Pid ?? 0, cmd.Target!, cmd.Action!);
            }
            // SHOW_NOTIFICATION { message } : l'admin/portail pousse une notif à afficher à l'utilisateur (toast + centre de notifs).
            else if (cmd.Name == "SHOW_NOTIFICATION" && !string.IsNullOrWhiteSpace(cmd.Message))
            {
                var msg = cmd.Message!.Length > 500 ? cmd.Message!.Substring(0, 500) : cmd.Message!;   // tronque au-delà de ~500
                Logger.Info("Commande serveur : SHOW_NOTIFICATION");
                ShowNotificationRequested?.Invoke(msg);
                _ = Task.Run(() => AckCommandAsync(cmd, true, null));   // signature hors du fil de réception WS
            }
            // START_SCRIPT { pid, script? } : (re)lance le script du bot pid (script fourni = charge+lance ; sinon le script chargé).
            else if (cmd.Name == "START_SCRIPT" && cmd.Pid is int sp && sp > 0)
            {
                Logger.Info($"Commande serveur : START_SCRIPT pid={sp} script={cmd.Script ?? "(courant)"}");
                ScriptControlRequested?.Invoke(sp, true, cmd.Script, cmd.Nonce, cmd.Name);
            }
            // STOP_SCRIPT { pid } : arrête proprement le script du bot pid (le bot reste connecté).
            else if (cmd.Name == "STOP_SCRIPT" && cmd.Pid is int tp && tp > 0)
            {
                Logger.Info($"Commande serveur : STOP_SCRIPT pid={tp}");
                ScriptControlRequested?.Invoke(tp, false, null, cmd.Nonce, cmd.Name);
            }
            // PAUSE_SCRIPT / RESUME_SCRIPT { pid } : dans le moteur actuel l'hôte Lua ne peut pas se figer en cours de ligne
            // -> PAUSE == arrêt propre (comme STOP), RESUME == relance du script du perso (comme START). On les honore pour
            // que le portail puisse exposer des boutons distincts ; l'accusé part avec le vrai nom de commande.
            else if (cmd.Name == "PAUSE_SCRIPT" && cmd.Pid is int pp && pp > 0)
            {
                Logger.Info($"Commande serveur : PAUSE_SCRIPT pid={pp}");
                ScriptControlRequested?.Invoke(pp, false, null, cmd.Nonce, cmd.Name);
            }
            else if (cmd.Name == "RESUME_SCRIPT" && cmd.Pid is int rp && rp > 0)
            {
                Logger.Info($"Commande serveur : RESUME_SCRIPT pid={rp}");
                ScriptControlRequested?.Invoke(rp, true, null, cmd.Nonce, cmd.Name);
            }
            // SENTINEL_CONFIG { action="scan_interval_s", pid=N } : règle à chaud la cadence du scan Givre Sentinel. N est
            // porté par pid (int, borné 1-60 côté service, 0/absent = ignoré). Le serveur peut ainsi passer la cadence à
            // 1-5 s pour un poste suspect, ou la relâcher. L'accusé part comme pour les autres commandes.
            else if (cmd.Name == "SENTINEL_CONFIG" && cmd.Action is "scan_interval_s" && cmd.Pid is int si)
            {
                Logger.Info($"Commande serveur : SENTINEL_CONFIG scan_interval_s={si}");
                SetSentinelScanInterval(si);
                _ = Task.Run(() => AckCommandAsync(cmd, true, null));
            }
            // Toute autre commande (inconnue, non prise en charge, ou paramètres invalides) : IGNORÉE (forward-compat, jamais
            // terminale), mais ACCUSÉE « refusé » -> le panel ne reste pas sur « envoyé » à vie (un accusé par commande).
            else
            {
                Logger.Info($"Commande serveur ignorée : {cmd.Name}");
                _ = Task.Run(() => AckCommandAsync(cmd, false, "commande invalide ou non prise en charge"));
            }
        };

        // Télémetrie des bots (panel Sessions) : émission périodique signée + re-envoi forcé à chaque (re)connexion WS.
        // À la (re)connexion WS on rejoue aussi les scripts en attente (récup après aléa réseau / WS retombé).
        client.ConnectionChanged += st =>
        {
            if (!Current() || st != WsConnectionState.Connected) return;
            _lastBotsSig = "";
            _ = FlushPendingScriptsAsync();
            RequeueUncertainAcks(client);   // accusés partis sur un socket mort sans preuve de réception
            _ = FlushPendingAcksAsync(client, max: 4);
            RequeueRecentHdvEvents();       // canal hdv : événements partis < 60 s rejoués (dédup serveur par id)
        };
        // Envois vers le panel refusés à répétition par le serveur : suspendus pour cette session (la licence reste active).
        client.UploadsSuspended += code =>
        {
            if (!Current()) return;
            Logger.Warn($"Envois panel suspendus (refus serveur répétés : {code})");
            GivreLauncher.Core.Transport.TransportLog.Line($"[panel] envois vers le panel refusés par le serveur ({code}) — suspendus pour cette session ; la licence reste active.");
        };
        StartBotTelemetry(client, ct);
        StartHdvEvents(client, ct);     // canal "hdv" : événements HDV objet par objet (HDV Tracker)
        StartSentinel(client, ct);      // canal "sentinel" : signaux anti-rétro-ingénierie (signalement seul, revue humaine côté serveur)
        Relay.GivreRelay.Start();       // GivreRelay : partages d'IP d'amis (WebSocket sur le jeton de cette session)

        // Kill INSTANTANE : frame terminale poussee par le serveur (panel "fermer la session", revoke,
        // pause, ban, expiration) -> l'app se reverrouille TOUT DE SUITE, sans attendre un tick de 60 s.
        client.Terminated += ex =>
        {
            terminated = true;
            if (!Current() || ct.IsCancellationRequested) return;   // client remplacé / session arrêtée entre-temps
            ForbidResume();   // fin VOULUE (kick, pause, révocation, ban, expiration) : jamais de reprise
            IsAuthorized = false;   // killswitch -> le scheduler + les scripts Lua doivent s'arreter
            MappingCatalog.Clear();   // ban/revoke/expiration/pause -> purge le mapping : bot aveugle tout de suite.
            RaisePermissionsChanged();
            Logger.Warn($"Session terminee (push serveur temps-reel) : {ex.Code}");
            Diag.Log(400, Diag.Err(ex.Code), 1);
            SessionEnded?.Invoke(Localize(ex));
        };

        _ = Task.Run(async () =>
        {
            try
            {
                // Temps-reel d'abord (WebSocket) : kill instantane. Si le socket ne tient pas (indispo, reconnexions epuisees)
                // ET que la session n'est pas deja terminee -> repli polling HTTP, qui rend la main pour RETENTER le temps réel
                // (une tentative) quand le lien revient ou au plus tard toutes les 2 min. Avant, le repli était définitif.
                bool probe = false;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        while (true)
                        {
                            await client.RunWebSocketAsync(ct, singleAttempt: probe).ConfigureAwait(false);
                            // abandonné = reprise en cours ; TerminalError = kick reçu (Terminated suit ou a suivi)
                            if (ct.IsCancellationRequested || terminated || client.IsAbandoned || client.TerminalError is not null) return;
                            if (!probe) Logger.Info("Heartbeat temps-reel indisponible -> repli HTTP.");
                            var exit = await client.HeartbeatLoopAsync((hbEx, raw) =>
                            {
                                if (!Current() || ct.IsCancellationRequested) return;
                                ForbidResume();
                                IsAuthorized = false;   // killswitch/echecs -> le scheduler + les scripts Lua doivent s'arreter
                                // Purge SEULEMENT sur un code terminal signé (parité avec la trame WS terminale). Après des échecs transitoires
                                // (réseau, 5xx, RATE_LIMITED), purger laisserait le décodage MITM aveugle jusqu'au redémarrage de l'app.
                                if (hbEx is not null && LicenseClient.IsTerminal(hbEx.Code)) MappingCatalog.Clear();
                                RaisePermissionsChanged();
                                var msg = hbEx is not null ? Localize(hbEx) : LocalizeEnvelope(raw);
                                Logger.Warn($"Session terminee (heartbeat HTTP) : {hbEx?.Code ?? "HEARTBEAT"}");
                                Diag.Log(400, hbEx is null ? 98 : Diag.Err(hbEx.Code), 2);   // 98 = échecs d'intégrité répétés (signature/horloge)
                                SessionEnded?.Invoke(msg);
                            }, ct).ConfigureAwait(false);
                            if (exit != LicenseClient.HttpHeartbeatExit.RetryWebSocket) return;
                            probe = true;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception e)
                    {
                        // Imprévu : avant, la tâche s'arrêtait là, licence et botting intacts sans plus aucun contact serveur (plus de
                        // kick reçu). Désormais : botting suspendu (levé au prochain contact réussi) et boucle relancée.
                        Logger.Warn($"Heartbeat : erreur imprévue ({e.GetType().Name}) — botting suspendu, nouvel essai");
                        Diag.Log(416, Diag.Ex(e));
                        if (!Current() || client.IsAbandoned || terminated) return;
                        client.MarkLinkDown();
                        try { await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                        probe = true;
                    }
                }
            }
            catch (Exception e) { Logger.Warn($"Heartbeat arrete : {e.Message}"); }
        });
    }

    private static void StopHeartbeat()
    {
        SetLinkDown(false);   // l'état « lien perdu » appartient au battement arrêté (reprise, login, déconnexion)
        try { _expiryTimer?.Dispose(); } catch { }
        _expiryTimer = null;
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    /// <summary>Deconnexion : arrete le heartbeat, notifie le serveur (best-effort), oublie l'etat de session.</summary>
    public static void Logout()
    {
        ForbidResume();
        Interlocked.Increment(ref _sessionGen);
        IsAuthorized = false;
        MappingCatalog.Clear();   // logout : le mapping ne survit pas à la session.
        RaisePermissionsChanged();
        StopHeartbeat();
        Relay.GivreRelay.Stop();
        // Purge la file de push : la prochaine session (licence potentiellement différente) ne doit pas hériter de scripts en attente.
        lock (_pushLock) { _pushEpoch++; _pendingScripts.Clear(); _lastPushedByName.Clear(); _softFailNotified.Clear(); _inFlight.Clear(); }
        lock (_ackLock) { _pendingAcks.Clear(); _inflightAcks.Clear(); }
        var client = _client;
        _client = null;
        _lastKey = null;
        if (client is not null)
            _ = Task.Run(async () => { try { await client.LogoutAsync(); } catch { } finally { client.Dispose(); } });
    }

    /// <summary>Déconnexion SYNCHRONE à la FERMETURE de l'app : POST /logout avec un court timeout pour que le panel
    /// ne montre PAS une session fantôme jusqu'au TTL (180 s). Comme le launcher C++ (Logout bloquant à l'exit).</summary>
    public static void LogoutBlocking(int timeoutMs = 2000)
    {
        Volatile.Write(ref _exiting, true);   // AVANT PermissionsChanged : les handlers postés doivent voir la sortie
        ForbidResume();
        Interlocked.Increment(ref _sessionGen);
        IsAuthorized = false;
        MappingCatalog.Clear();
        RaisePermissionsChanged();
        StopHeartbeat();
        lock (_pushLock) { _pushEpoch++; _pendingScripts.Clear(); _lastPushedByName.Clear(); _softFailNotified.Clear(); _inFlight.Clear(); }   // purge la file de push (cf Logout)
        lock (_ackLock) { _pendingAcks.Clear(); _inflightAcks.Clear(); }
        var client = _client;
        _client = null;
        _lastKey = null;
        if (client is null) return;
        try { client.LogoutAsync().Wait(timeoutMs); } catch { /* best-effort */ }
        finally { client.Dispose(); }
    }

    // ---------------- persistance de la cle (DPAPI) ----------------

    /// <summary>Enregistre (ou efface) la cle selon le choix "rester connecte". Chiffree DPAPI.</summary>
    public static void SaveLoginPreference(string key, bool remember)
    {
        AppConfig.Current.AutoLogin = remember;
        AppConfig.Current.LicenseProtected = remember ? Dpapi.Protect(key) : "";
        AppConfig.Save();
    }

    /// <summary>Cle memorisee en clair (DPAPI decrypt), ou null si absente/illisible.</summary>
    public static string? LoadSavedKey()
    {
        var blob = AppConfig.Current.LicenseProtected;
        return string.IsNullOrEmpty(blob) ? null : Dpapi.Unprotect(blob);
    }

    public static void ClearSavedKey()
    {
        AppConfig.Current.AutoLogin = false;
        AppConfig.Current.LicenseProtected = "";
        AppConfig.Save();
    }

    /// <summary>Récupère le mapping (déchiffré en mémoire) via la session courante, ou null si pas de session.</summary>
    public static async Task<MappingBundle?> FetchMappingAsync(string build, string? knownBuild = null, int? knownVersion = null, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null) return null;
        try { return await client.FetchMappingAsync(build, knownBuild, knownVersion, ct); }
        catch (LicenseServerException le) when (le.Code == "SIGNATURE_REJECTED" && le.Reason != "key_mismatch")
        {
            BlockSigning(le);   // aucun nouvel essai : bundle en mémoire gardé, sinon automatisation suspendue avec le message
            throw;
        }
    }

    // ---------------- localisation des messages serveur ----------------

    // Traduit un code d'erreur metier signe vers la langue courante (message non revelateur).
    private static string Localize(LicenseServerException e) => e.Code switch
    {
        "INVALID_KEY"                        => Loc.T("L.Login.ErrInvalid"),
        "VALIDATION_ERROR"                   => Loc.T("L.Auth.Generic"),   // champ refusé = bug client, jamais « clé invalide »
        // Requête signée refusée : jamais « clé invalide ». Motif montré depuis une liste FIXE (jamais un texte serveur brut).
        "SIGNATURE_REJECTED"                 => e.ClockWrong ? Loc.T("L.Auth.ClockWrong") : Loc.Fmt("L.Auth.SignatureError", SignatureReasonText(e.Reason)),
        "KEY_EXPIRED"                        => Loc.T("L.Auth.Expired"),
        "KEY_PAUSED"                         => WithReason(e, "L.Auth.Paused"),
        "KEY_REVOKED"                        => WithReason(e, "L.Auth.Banned"),
        "USER_BANNED"                        => WithReason(e, "L.Auth.Banned"),
        "KEY_LOCKED"                         => Loc.T("L.Auth.RateLimited"),
        "RATE_LIMITED"                       => Loc.T("L.Auth.RateLimited"),
        "HWID_MISMATCH"                      => Loc.T("L.Auth.Hwid"),
        "SESSION_EXPIRED"                    => Loc.T("L.Auth.SessionEnded"),
        _                                    => Loc.T("L.Auth.Generic"),
    };

    private static string SignatureReasonText(string? reason) => reason switch
    {
        "unsigned" or "malformed" or "pubkey" or "signature" or "key_mismatch" or "drift" or "replay" => reason!,
        _ => "malformed",
    };

    // Ajoute la raison admin (ban/pause/revoke) si presente, sinon message generique traduit.
    private static string WithReason(LicenseServerException e, string baseKey) =>
        string.IsNullOrWhiteSpace(e.Reason)
            ? Loc.T(baseKey)
            : Loc.Fmt("L.Auth.BannedReasonFmt", e.Reason);

    // Traduit une erreur d'enveloppe (reseau/signature/horloge) d'apres son message technique.
    private static string LocalizeEnvelope(string message)
    {
        var m = (message ?? "").ToLowerInvariant();
        if (m.Contains("reseau") || m.Contains("network") || m.Contains("timeout") || m.Contains("connection"))
            return Loc.T("L.Auth.Connection");
        if (m.Contains("server_time") || m.Contains("horloge") || m.Contains("clock"))
            return Loc.T("L.Auth.DateTime");
        if (m.Contains("signature") || m.Contains("nonce") || m.Contains("rejeu") || m.Contains("replay") || m.Contains("base64"))
            return Loc.T("L.Auth.Verify");
        return Loc.T("L.Auth.Unavailable");
    }
}
