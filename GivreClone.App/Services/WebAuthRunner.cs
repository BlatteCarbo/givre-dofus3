using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services;

/// <summary>
/// Lance l'exe compagnon WebView2 (login Ankama dans un vrai navigateur, passe le WAF) et récupère
/// {access_token, refresh_token} depuis sa sortie standard. Le compagnon est copié dans WebAuth\ à côté de l'app.
/// </summary>
public static class WebAuthRunner
{
    public sealed record Tokens(string AccessToken, string RefreshToken);

    /// <summary>Issue de l'authentification navigateur, pour que le modal explique précisément le résultat.</summary>
    public enum Outcome { Success, Cancelled, Failed, Unavailable }

    public sealed record WebAuthResult(Outcome Outcome, Tokens? Tokens = null, string? Error = null);

    // Secret PARTAGÉ avec GivreLauncher.WebAuth/Program.cs (garde "pas lançable seul"). Doit être IDENTIQUE des deux
    // côtés. Stocké XOR-encodé (0x5A) et assemblé au RUNTIME -> aucun littéral en clair 'grep-able'/dnSpy-able dans le
    // binaire (WebAuth.dll est managé non-VMProtect -> un const string y fuiterait ; ici même WebAuth ne l'expose plus).
    private static readonly byte[] _hsEnc =
    {
        0x17,0x33,0x20,0x3B,0x34,0x74,0x0D,0x3F,0x38,0x1B,0x2F,0x2E,0x32,0x74,0x32,0x3B,0x34,0x3E,0x29,0x32,0x3B,0x31,0x3F,0x74,0x2C,
        0x6B,0x74,0x63,0x3C,0x69,0x39,0x6B,0x3B,0x6D,0x3F,0x77,0x3E,0x35,0x77,0x34,0x35,0x2E,0x77,0x39,0x32,0x3B,0x34,0x3D,0x3F,
    };
    private static string HandshakeSecret()
    {
        var b = new byte[_hsEnc.Length];
        for (int i = 0; i < b.Length; i++) b[i] = (byte)(_hsEnc[i] ^ 0x5A);
        return System.Text.Encoding.UTF8.GetString(b);
    }
    private static string ComputeHandshake(long window)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(HandshakeSecret()));
        return System.Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(window.ToString())));
    }

    // MZP18: legitimate child handshake shared with the isolated, rebuilt
    // browser helper. Never persist or log the result.
    internal static string MZP18TestCompanionHandshake()
        => ComputeHandshake(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

    public static async Task<WebAuthResult> RunAsync(ProxySettings? proxy, string? login, string? password)
    {
        using var companion = MZP17WebAuthIntegrity.TryAcquire(AppContext.BaseDirectory);
        if (companion is null)
            return new WebAuthResult(Outcome.Unavailable, null,
                "Compagnon navigateur TEST absent ou empreinte de distribution invalide.");
        string exe = companion.ExecutablePath;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,   // les SECRETS (login/mot de passe/creds proxy) passent par STDIN, jamais env/args
            CreateNoWindow = false,
        };
        // Jeton "lancé par GivreClone" (HMAC, fenêtre 30 s) : sans lui WebAuth.exe sort immédiatement (pas lançable seul).
        // C'est un HMAC ROTATIF (pas le secret) -> le voir en env est sans conséquence.
        psi.Environment["MZ_WEBAUTH_HS"] = ComputeHandshake(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        // The genuine managed module has an optional diagnostic network beacon
        // behind this inherited environment flag. Never pass it to our child.
        psi.Environment.Remove("MZ_WEBAUTH_FAILOVER");

        // Seuls les NON-secrets restent en arguments : l'HÔTE proxy (routage) + la langue de l'UI.
        if (proxy is not null)
        {
            psi.ArgumentList.Add("--proxy");
            psi.ArgumentList.Add($"{(proxy.Kind == ProxyKind.Socks5 ? "socks5" : "http")}://{proxy.Host}:{proxy.Port}");
        }
        psi.ArgumentList.Add("--lang");
        psi.ArgumentList.Add(Loc.CurrentCode);   // localise l'UI du compagnon (statut IP)

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new WebAuthResult(Outcome.Unavailable);

            // SECRETS (login/mot de passe/creds proxy) via STDIN, une ligne JSON, puis fermeture : un canal PRIVÉ au
            // process (contrairement à env/args, lisibles par tout process du même user + capturés en crash dump).
            using (var sm = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(sm))
                {
                    w.WriteStartObject();
                    w.WriteString("login", login ?? "");
                    w.WriteString("password", password ?? "");
                    w.WriteString("proxy_user", proxy is { HasAuth: true } ? (proxy.Username ?? "") : "");
                    w.WriteString("proxy_pass", proxy is { HasAuth: true } ? (proxy.Password ?? "") : "");
                    w.WriteEndObject();
                }
                await p.StandardInput.WriteLineAsync(System.Text.Encoding.UTF8.GetString(sm.ToArray())).ConfigureAwait(false);
            }
            p.StandardInput.Close();

            var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);

            // Succès : sortie 0 + JSON {access_token, refresh_token}.
            if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                try
                {
                    using var doc = JsonDocument.Parse(stdout);
                    var r = doc.RootElement;
                    var at = r.TryGetProperty("access_token", out var a) ? a.GetString() ?? "" : "";
                    var rt = r.TryGetProperty("refresh_token", out var rf) ? rf.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(at)) return new WebAuthResult(Outcome.Success, new Tokens(at, rt));
                }
                catch { /* stdout inattendu -> traité comme un échec ci-dessous */ }
            }

            // Échec : le compagnon écrit {status: "cancelled"|"failed", message} pour distinguer
            // une fenêtre fermée (abandon) d'une vraie erreur d'échange de jeton.
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                try
                {
                    using var doc = JsonDocument.Parse(stdout);
                    var r = doc.RootElement;
                    var status = r.TryGetProperty("status", out var s) ? s.GetString() : null;
                    var message = r.TryGetProperty("message", out var m) ? m.GetString() : null;
                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                        return new WebAuthResult(Outcome.Failed, null, message);
                    if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        return new WebAuthResult(Outcome.Cancelled);
                }
                catch { /* stdout inattendu */ }
            }

            // Sortie non-zéro sans statut exploitable (crash / process tué) -> échec, pas un abandon.
            return p.ExitCode != 0
                ? new WebAuthResult(Outcome.Failed)
                : new WebAuthResult(Outcome.Cancelled);   // fermeture propre sans jeton -> abandon
        }
        catch (Exception ex)
        {
            // Compagnon présent mais impossible à lancer/lire (corrompu, bloqué par l'AV, supprimé entre-temps).
            return new WebAuthResult(Outcome.Failed, null, ex.Message);
        }
    }

}
