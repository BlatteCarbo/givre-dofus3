using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Zaap;

namespace GivreClone.App.Services.LocalLaunch;

/// <summary>
/// Autorités de lancement et de Zaap de l'édition locale, d'après la recette de ZaapEmu (utilisateur) : le client est lancé avec
/// --port/--hash/--configUrl..., se connecte au Zaap Thrift local, y échange <c>connect(hash)</c> puis réclame son jeton de jeu.
/// Le jeton vient de l'appelant (déjà obtenu légitimement par l'utilisateur) ; rien n'est deviné ni simulé côté Ankama.
/// Le Zaap écoute sur un port local libre, jamais 5657/5658/26666 (refusé par ZaapEmulator).
/// </summary>
public sealed class LocalZaapAuthority : IIndependentTestZaapWireAuthority, IIndependentTestGameLaunchAuthority, IDisposable
{
    private readonly int _zaapPort;
    private readonly int _configPort;
    private readonly string _connectionHosts;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expected = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<string?>>> _refreshers = new(StringComparer.Ordinal);
    private HttpListener? _http;
    private CancellationTokenSource? _cts;

    /// <param name="connectionHosts">Valeur de « connectionHosts » du divazaap.json (hôtes de connexion que le client doit utiliser).</param>
    public LocalZaapAuthority(int zaapPort, int configPort, string connectionHosts, Action<string>? log = null)
        => (_zaapPort, _configPort, _connectionHosts, _log) = (zaapPort, configPort, connectionHosts, log ?? (_ => { }));

    public int ZaapPort => _zaapPort;

    /// <summary>Port du relais MITM local, transmis au hook natif par GIVRE_RELAY_PORT (0 = laisser le hook sur son port historique).</summary>
    public int RelayPort { get; set; }
    public string? ItemOverlaySettingsPath { get; set; }

    /// <summary>Ajoute --configUrl (divazaap.json local) à la ligne de commande. ACTIVÉ PAR DÉFAUT : c'est la recette qui a fonctionné en direct dans ZaapEmu et
    /// dans l'émulateur de lanceur de Givre (DivaZaap). Le lanceur officiel installé n'a pas cet argument dans son modèle (rapport docs/codex/01), mais son retrait
    /// a donné l'écran « connexion avec l'Ankama Launcher interrompue » au premier essai réel du clone.</summary>
    public bool UseConfigUrl { get; set; } = true;

    /// <summary>Démarre le serveur de configuration HTTP (divazaap.json) ; idempotent.</summary>
    public void StartConfigServer()
    {
        if (_http is not null) return;
        _cts = new CancellationTokenSource();
        _http = new HttpListener();
        _http.Prefixes.Add($"http://127.0.0.1:{_configPort}/");
        _http.Start();
        _ = ServeConfigAsync(_http, _cts.Token);
    }

    public static string BuildConfigJson(string connectionHost) => JsonSerializer.Serialize(new
    {
        gameAppId = 1,
        connectionHosts = new[] { connectionHost },
        buildType = "release",
        chatAppId = 99,
        chatServerHost = "zaap-chat.ankama.com",
        chatServerPort = 6337,
        versionFileUrl = "",
        haapiAnkamaUrl = "https://haapi.ankama.com/json/Ankama/v5/",
        haapiDofusUrl = "https://haapi.ankama.com/json/Dofus/v3/",
        shopDofusUrl = "https://shop-api.ankama.com/",
        gamesActivityDescriptorUrl = "https://launcher.cdn.ankama.com/configs/useractivities.json",
        avatarUrlFormat = "https://avatar.ankama.com/users/{0}.png",
        dofusWebsiteUrl = "https://www.dofus.com",
    });

    private async Task ServeConfigAsync(HttpListener l, CancellationToken ct)
    {
        while (l.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await l.GetContextAsync().ConfigureAwait(false); } catch { break; }
            try
            {
                if (ctx.Request.Url?.AbsolutePath == "/divazaap.json")
                {
                    var buf = Encoding.UTF8.GetBytes(BuildConfigJson(_connectionHosts));
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = buf.Length;
                    await ctx.Response.OutputStream.WriteAsync(buf, ct).ConfigureAwait(false);
                    _log("[zaap-config] divazaap.json servi");
                }
                else ctx.Response.StatusCode = 404;
            }
            catch { /* client parti */ }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    // ------------------------------------------------------------------ lancement
    /// <summary>La session doit être annoncée avant le lancement (le hash est celui de ZaapEmulator.RegisterAccount).</summary>
    /// <summary>Fournisseur de jetons FRAIS pour cette session : le client redemande un jeton à chaque (re)connexion et un jeton de jeu est à usage unique,
    /// comme avec le vrai lanceur (auth_getGameToken crée un jeton à chaque appel). Sans fournisseur, seul le premier appel reçoit le jeton initial.</summary>
    public void SetTokenRefresher(string sessionHash, Func<CancellationToken, Task<string?>> refresher) => _refreshers[sessionHash] = refresher;

    public void Expect(string sessionHash) => _expected[sessionHash] = DateTimeOffset.UtcNow.AddMinutes(2);

    public ProcessStartInfo? CreateAuthorizedStartInfo(string exePath, string exeSha256, string accountSessionHash, int connectionType, string? hardwareSeed)
    {
        if (!Guid.TryParse(accountSessionHash, out _) || !_expected.ContainsKey(accountSessionHash)) return null;
        if (!string.Equals(Path.GetFileName(exePath), "Dofus.exe", StringComparison.OrdinalIgnoreCase) || exeSha256.Length != 64) return null;
        // Contrat du lanceur officiel (zaap.yml + zaap-start.bat du dossier du jeu, rapport docs/codex/01) : ni --configUrl ni hôte explicite,
        // mais --connectionPort. Le -logFile du batch est omis (journal du jeu non redirigé). --configUrl reste possible (UseConfigUrl) : héritage ZaapEmu, effet NON VÉRIFIÉ.
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        if (ItemOverlaySettingsPath is { Length: > 0 } settingsPath)
            psi.Environment["GIVRE_ITEM_OVERLAY_SETTINGS"] = settingsPath;
        void A(string k, string v) { psi.ArgumentList.Add(k); psi.ArgumentList.Add(v); }
        A("--port", _zaapPort.ToString());
        A("--gameName", "dofus");
        A("--gameRelease", "dofus3");
        A("--instanceId", "1");
        A("--hash", accountSessionHash);
        A("--canLogin", "true");
        A("--langCode", "fr");
        A("--autoConnectType", connectionType.ToString());
        A("--connectionPort", "5555");
        if (UseConfigUrl) A("--configUrl", $"http://127.0.0.1:{_configPort}/divazaap.json");
        psi.EnvironmentVariables["ZAAP_PORT"] = _zaapPort.ToString();
        psi.EnvironmentVariables["ZAAP_GAME"] = "dofus";
        psi.EnvironmentVariables["ZAAP_RELEASE"] = "dofus3";
        psi.EnvironmentVariables["ZAAP_INSTANCE_ID"] = "1";
        psi.EnvironmentVariables["ZAAP_HASH"] = accountSessionHash;
        psi.EnvironmentVariables["ZAAP_CAN_AUTH"] = "true";
        if (RelayPort > 0) psi.EnvironmentVariables["GIVRE_RELAY_PORT"] = RelayPort.ToString();
        psi.EnvironmentVariables["GIVRE_KAMA_LOG"] = "1";   // diagnostic hook (givrekama-<pid>.log) : à retirer une fois le MITM validé
        if (!string.IsNullOrEmpty(hardwareSeed)) psi.EnvironmentVariables["GIVRE_HWSEED"] = hardwareSeed;
        return psi;
    }

    public bool ConfirmStillAuthorized(string exePath, string exeSha256, string accountSessionHash)
        => _expected.TryGetValue(accountSessionHash, out var exp) && DateTimeOffset.UtcNow < exp;

    // ------------------------------------------------------------------ Zaap Thrift
    public async Task<string?> AdmitTestConnectionAsync(TcpClient incoming, CancellationToken ct)
    {
        incoming.ReceiveTimeout = 15_000;
        var s = incoming.GetStream();
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromSeconds(15));
        var m = await ThriftBinary.ReadMessageAsync(s, to.Token).ConfigureAwait(false);
        string? hash = m.Name == "connect" && m.Args.TryGetValue(4, out var h) ? h as string : null;
        bool known = hash is not null && _expected.ContainsKey(hash);
        await ThriftBinary.WriteReplyAsync(s, m.Name, m.SeqId, known ? hash : "", ct).ConfigureAwait(false);
        _log(known ? "[zaap] connect : session connue" : "[zaap] connect : session INCONNUE");
        return known ? hash : null;
    }

    public bool IsStillAuthorized(string testSessionHash)
        => _expected.TryGetValue(testSessionHash, out var exp) && DateTimeOffset.UtcNow < exp;

    /// <summary>Sert le reste de la session Thrift sur le flux admis : jeton de jeu (une seule fois), type de connexion, infos utilisateur.</summary>
    public async Task WriteTestResponseAsync(Stream stream, string testSessionHash, string authenticatedToken, int connectionType, CancellationToken ct)
    {
        bool tokenGiven = false;
        using var life = CancellationTokenSource.CreateLinkedTokenSource(ct);
        life.CancelAfter(TimeSpan.FromMinutes(30));

        string lastTokenNote = "";   // précision du journal : jamais la valeur du jeton

        async Task<string> TokenAsync()
        {
            if (!tokenGiven) { tokenGiven = true; lastTokenNote = " (jeton servi une fois)"; return authenticatedToken; }
            if (_refreshers.TryGetValue(testSessionHash, out var refresh))
            {
                try
                {
                    var fresh = await refresh(life.Token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(fresh)) { lastTokenNote = " (jeton frais servi)"; return fresh; }
                }
                catch (Exception e) when (e is not OperationCanceledException) { _log("[zaap] jeton frais : échec (" + e.GetType().Name + ")"); }
            }
            lastTokenNote = " (jeton déjà consommé)";
            return "";
        }

        try
        {
            while (!life.IsCancellationRequested)
            {
                var m = await ThriftBinary.ReadMessageAsync(stream, life.Token).ConfigureAwait(false);
                object? result = m.Name switch
                {
                    "connect" => testSessionHash,
                    "auth_getGameToken" or "auth_getGameTokenWithWindowId" => await TokenAsync().ConfigureAwait(false),
                    "updater_isUpdateAvailable" => false,
                    "settings_get" => (m.Args.TryGetValue(2, out var k) && k as string == "autoConnectType") ? connectionType.ToString() : "",
                    "userInfo_get" => "{\"id\":\"1\",\"type\":\"ANKAMA\",\"login\":\"local\",\"nickname\":\"local\",\"firstname\":\"local\",\"lastname\":\"local\",\"nicknameWithTag\":\"local#0000\"}",
                    "zaapVersion_get" => "1.0.0-local",
                    "zaapMustUpdate_get" => false,
                    // Autres méthodes du contrat officiel (20) : valeurs prudentes, effet côté client NON VÉRIFIÉ.
                    "hasGameAccess" => true,
                    "hasPremiumAccess" => false,
                    "askForGuestComplete" or "system_addNotification" => "",
                    _ => null,
                };
                _log($"[zaap] appel {m.Name}" + (m.Name.StartsWith("auth_getGameToken", StringComparison.Ordinal) ? lastTokenNote : ""));
                await ThriftBinary.WriteReplyAsync(stream, m.Name, m.SeqId, result, life.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException) { /* session finie */ }
        finally { _expected.TryRemove(testSessionHash, out _); _refreshers.TryRemove(testSessionHash, out _); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _http?.Stop(); _http?.Close(); } catch { }
        _http = null;
    }
}
