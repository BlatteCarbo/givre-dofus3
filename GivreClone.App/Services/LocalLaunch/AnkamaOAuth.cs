using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.LocalLaunch;

/// <summary>
/// Connexion Ankama par OAuth (PKCE) puis validation du code Bouclier (OTP), d'après la chaîne de ZaapEmu (réplique du lanceur Ankama
/// officiel : app.asar, client_id 102). Les identifiants ne passent JAMAIS par ce code : l'utilisateur se connecte lui-même dans son
/// navigateur et saisit lui-même son code OTP. Le jeton d'accès, le jeton de rafraîchissement, la clé API sécurisée et le certificat sont
/// rendus à l'appelant, qui les range dans le coffre chiffré des comptes ; rien n'est journalisé.
/// </summary>
public static class AnkamaOAuth
{
    private const string AuthUrl = "https://auth.ankama.com/login/ankama";
    private const string TokenUrl = "https://auth.ankama.com/token";
    private const string HaapiBase = "https://haapi.ankama.com/json/Ankama/v5/";
    private const string ClientId = "102";
    private const string UserAgent = "Zaap 3.13.18";   // le WAF refuse les requêtes sans agent plausible
    public const int RedirectPort = 9001;
    public const string RedirectUri = "http://127.0.0.1:9001/authorized";

    // ------------------------------------------------------------------ PKCE
    public static string NewCodeVerifier()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(96);
        var sb = new StringBuilder(96);
        foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    public static string CodeChallenge(string verifier)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))).Replace("+", "-").Replace("/", "_").Replace("=", "");

    public static string LoginUrl(string verifier)
        => $"{AuthUrl}?code_challenge={CodeChallenge(verifier)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
           $"&client_id={ClientId}&direct=true&origin_tracker=https://www.ankama-launcher.com/launcher";

    /// <summary>Écoute 127.0.0.1:9001/authorized et rend le paramètre « code » de la redirection (null si délai dépassé / annulé).</summary>
    public static async Task<string?> WaitForCodeAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var l = new HttpListener();
        l.Prefixes.Add($"http://127.0.0.1:{RedirectPort}/");
        l.Start();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var ctx = await l.GetContextAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                var code = ctx.Request.QueryString["code"];
                var body = Encoding.UTF8.GetBytes(code is null
                    ? "<html><body>Aucun code recu. Vous pouvez fermer cet onglet.</body></html>"
                    : "<html><body>Connexion recue. Vous pouvez fermer cet onglet et revenir dans GivreClone.</body></html>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body, cts.Token).ConfigureAwait(false);
                ctx.Response.Close();
                if (!string.IsNullOrEmpty(code)) return code;
            }
        }
        catch (OperationCanceledException) { }
        return null;
    }

    // ------------------------------------------------------------------ HAAPI
    public static HttpClient CreateClient(ProxySettings? proxy)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        if (proxy is not null)
        {
            handler.Proxy = new WebProxy($"{(proxy.Kind == ProxyKind.Socks5 ? "socks5" : "http")}://{proxy.Host}:{proxy.Port}")
            { Credentials = string.IsNullOrEmpty(proxy.Username) ? null : new NetworkCredential(proxy.Username, proxy.Password) };
            handler.UseProxy = true;
        }
        else handler.UseProxy = false;
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string? apiKey = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (apiKey is not null) r.Headers.TryAddWithoutValidation("apikey", apiKey);
        return r;
    }

    private static async Task<JsonDocument> JsonOrThrowAsync(HttpResponseMessage resp, string what, CancellationToken ct)
    {
        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // Le corps d'une erreur peut contenir des éléments sensibles : seul le code HTTP est relayé.
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"{what} refusé (HTTP {(int)resp.StatusCode}).");
        return JsonDocument.Parse(json);
    }

    public static async Task<(string AccessToken, string RefreshToken)> RedeemCodeAsync(HttpClient http, string authCode, string verifier, CancellationToken ct)
    {
        using var req = Req(HttpMethod.Post, TokenUrl);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = authCode, ["code_verifier"] = verifier,
            ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri,
        });
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        using var doc = await JsonOrThrowAsync(resp, "Échange du code", ct).ConfigureAwait(false);
        return (doc.RootElement.GetProperty("access_token").GetString()!, doc.RootElement.GetProperty("refresh_token").GetString()!);
    }

    /// <summary>Compte connecté : identifiant (login) et niveaux de sécurité (ex. SHIELD).</summary>
    public sealed record AccountInfo(string? Login, bool ShieldActive);

    public static async Task<AccountInfo> GetAccountAsync(HttpClient http, string accessToken, CancellationToken ct)
    {
        using var req = Req(HttpMethod.Get, HaapiBase + "Account/Account", accessToken);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        using var doc = await JsonOrThrowAsync(resp, "Lecture du compte", ct).ConfigureAwait(false);
        return ParseAccount(doc.RootElement);
    }

    public static AccountInfo ParseAccount(JsonElement root)
    {
        string? login = root.TryGetProperty("login", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()
                      : root.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        bool shield = root.TryGetProperty("security", out var s) && s.ValueKind == JsonValueKind.Array
                      && s.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), "SHIELD", StringComparison.OrdinalIgnoreCase));
        return new AccountInfo(login, shield);
    }

    public sealed record Certificate(string Id, string Encoded);

    /// <summary>Issue de la demande de code de sécurité (lanceur officiel : askForDoubleAuth).</summary>
    public enum SecurityCodeMode
    {
        /// <summary>Un code vient d'être envoyé par e-mail : à valider avec ValidateCode.</summary>
        EmailSent,
        /// <summary>Le compte est protégé par une application d'authentification : code à 6 chiffres, ValidateOtp.</summary>
        OtpRequired,
        /// <summary>Le compte n'a pas besoin d'être sécurisé (NONEEDTOBESECURED).</summary>
        NotNeeded,
    }

    /// <summary>Message d'erreur HAAPI (champ « message » du corps JSON), ou null.</summary>
    public static string? ErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var d = JsonDocument.Parse(body);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>GET Shield/SecurityCode?transportType=EMAIL|SMS : déclenche l'envoi du code. SECUREDBYOTP = code d'application ; NONEEDTOBESECURED = rien à faire.</summary>
    public static async Task<SecurityCodeMode> RequestSecurityCodeAsync(HttpClient http, string accessToken, bool sms, CancellationToken ct)
    {
        using var req = Req(HttpMethod.Get, HaapiBase + $"Shield/SecurityCode?transportType={(sms ? "SMS" : "EMAIL")}", accessToken);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode) return SecurityCodeMode.EmailSent;
        return ErrorMessage(body) switch
        {
            "SECUREDBYOTP" => SecurityCodeMode.OtpRequired,
            "NONEEDTOBESECURED" => SecurityCodeMode.NotNeeded,
            // Seul le code HTTP est relayé : le corps peut contenir des éléments sensibles.
            _ => throw new InvalidOperationException($"Demande du code de sécurité refusée (HTTP {(int)resp.StatusCode})."),
        };
    }

    /// <summary>Valide le code et rend le certificat : code d'application (ValidateOtp) ou code reçu par e-mail (ValidateCode) ; même requête, autre point d'entrée.</summary>
    public static async Task<Certificate> ValidateSecurityCodeAsync(HttpClient http, string accessToken, string code, bool otp, CancellationToken ct)
    {
        var (hm1, hm2) = Device.Fingerprint();
        string name = "launcher-" + Environment.UserName;
        string url = HaapiBase + $"Shield/{(otp ? "ValidateOtp" : "ValidateCode")}?game_id={ClientId}&code={Uri.EscapeDataString(code)}&hm1={hm1}&hm2={hm2}&name={Uri.EscapeDataString(name)}";
        using var req = Req(HttpMethod.Get, url, accessToken);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        using var doc = await JsonOrThrowAsync(resp, otp ? "Validation du code OTP" : "Validation du code reçu par e-mail", ct).ConfigureAwait(false);
        return ParseCertificate(doc.RootElement);
    }

    public static Task<Certificate> ValidateOtpAsync(HttpClient http, string accessToken, string otp, CancellationToken ct)
        => ValidateSecurityCodeAsync(http, accessToken, otp, otp: true, ct);

    public static Certificate ParseCertificate(JsonElement root)
    {
        var idEl = root.TryGetProperty("id", out var i) ? i : root.GetProperty("certificate").GetProperty("id");
        string id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : idEl.GetString()!;
        var certRoot = root.TryGetProperty("encodedCertificate", out _) ? root : root.GetProperty("certificate");
        string enc = certRoot.TryGetProperty("encodedCertificate", out var ec) ? ec.GetString()! : certRoot.GetProperty("encoded_certificate").GetString()!;
        return new Certificate(id, enc);
    }

    public static async Task<string> RefreshApiKeyAsync(HttpClient http, string accessToken, string refreshToken, CancellationToken ct)
    {
        using var req = Req(HttpMethod.Post, HaapiBase + "Api/RefreshApiKey", accessToken);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken, ["long_life_token"] = "true", ["shop_key"] = "ZAAP", ["payment_mode"] = "OK", ["lang"] = "fr",
        });
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        using var doc = await JsonOrThrowAsync(resp, "Clé API sécurisée", ct).ConfigureAwait(false);
        return doc.RootElement.GetProperty("key").GetString()!;
    }

    // ------------------------------------------------------------------ certificat
    /// <summary>cryptoHelper.generateHashFromCertif du lanceur : AES-256-ECB (PKCS7) du certificat avec hm2 pour clé, puis sha256(hm1 + clair).</summary>
    public static string ComputeCertificateHash(string encodedCertificate, string hm1, string hm2)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.PKCS7; aes.Key = Encoding.UTF8.GetBytes(hm2);
        var cipher = Convert.FromBase64String(encodedCertificate);
        using var dec = aes.CreateDecryptor();
        string plain = Encoding.UTF8.GetString(dec.TransformFinalBlock(cipher, 0, cipher.Length));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hm1 + plain))).ToLowerInvariant();
    }

    /// <summary>Hash du certificat avec l'empreinte d'appareil de CE clone (la même que lors de la validation OTP).</summary>
    public static string? HashForStoredCertificate(string encodedCertificate)
    {
        try { var (hm1, hm2) = Device.Fingerprint(); return ComputeCertificateHash(encodedCertificate, hm1, hm2); }
        catch { return null; }
    }

    /// <summary>Empreinte d'appareil : valeur stable propre à ce clone, stockée dans son dossier de données. Elle n'a pas à ressembler à celle
    /// d'Ankama, seulement à rester la même entre la validation OTP et chaque dérivation du hash : la supprimer rend le certificat inutilisable.</summary>
    public static class Device
    {
        public static string? PathOverride { get; set; }
        /// <summary>Dossier de données de l'application (posé au démarrage) ; sans lui ni <see cref="PathOverride"/>, la lecture échoue.</summary>
        public static Func<string>? DataDirProvider { get; set; }
        private static string FilePath => PathOverride
            ?? System.IO.Path.Combine((DataDirProvider ?? throw new InvalidOperationException("Dossier de données non défini."))(), "ankama-device.id");

        public static (string Hm1, string Hm2) Fingerprint()
        {
            string id;
            var p = FilePath;
            if (File.Exists(p) && File.ReadAllText(p).Trim() is { Length: > 0 } existing) id = existing;
            else
            {
                id = Guid.NewGuid().ToString();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
                File.WriteAllText(p, id);
            }
            return FromId(id);
        }

        public static (string Hm1, string Hm2) FromId(string deviceId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName + "|givrebot-local|" + deviceId));
            string hm1 = Convert.ToHexString(hash).ToLowerInvariant()[..32];
            return (hm1, new string(hm1.Reverse().ToArray()));
        }
    }
}
