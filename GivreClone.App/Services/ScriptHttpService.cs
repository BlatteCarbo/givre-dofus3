using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine;

namespace GivreClone.App.Services;

/// <summary>Résultat d'une requête HTTP de script. Error = null (succès transport) ou code stable : no_permission, bad_url,
/// blocked_address, bad_header, too_large, timeout, network. Location renseigné pour un 3xx seulement (pas de redirection suivie).</summary>
public sealed record ScriptHttpResult(bool Ok, int Status, string Body, string? Error, string? Location, string? ContentType)
{
    public static ScriptHttpResult Fail(string error) => new(false, 0, "", error, null, null);
}

/// <summary>
/// HTTP générique pour les scripts Lua (Lot K : http:get / http:post, developer:getRequest / postRequest). Gate Perm.PmRelay AU
/// POINT D'ACTION (sortie réseau = même famille que le relais de messagerie). Connexion DIRECTE (pas le proxy du compte), https
/// seulement, adresses PUBLIQUES seulement — vérifiées sur les IP résolues à la connexion (anti DNS-rebinding), aucune
/// redirection, aucun cookie, 10 s, 256 Kio envoyés / 1 Mio reçus. Synchrone (appelé depuis le thread du script).
/// </summary>
public static class ScriptHttpService
{
    private sealed class BlockedAddressException : Exception
    {
        public BlockedAddressException() : base("blocked_address") { }
    }

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxResponseHeadersLength = 64,   // Kio
        ConnectCallback = async (ctx, tok) =>
        {
            // Anti DNS-rebinding : on résout, on vérifie TOUTES les adresses, puis on se connecte à CES adresses vérifiées.
            var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, tok).ConfigureAwait(false);
            if (addrs.Length == 0 || addrs.Any(a => !ScriptHttpGuard.IsPublicAddress(a))) throw new BlockedAddressException();
            var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await sock.ConnectAsync(addrs, ctx.DnsEndPoint.Port, tok).ConfigureAwait(false);
                return new NetworkStream(sock, ownsSocket: true);
            }
            catch { sock.Dispose(); throw; }
        },
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Envoie `method` (GET/POST) vers `url`. `headers` = paires (nom, valeur) déjà extraites de la table Lua.
    /// Annulation du script (`ct`) -> OperationCanceledException propagée (le script s'arrête proprement).</summary>
    public static ScriptHttpResult Send(string method, string? url, string? body, IReadOnlyList<KeyValuePair<string, string>>? headers, CancellationToken ct)
        => SendAsync(method, url, body, headers, ct).GetAwaiter().GetResult();

    private static async Task<ScriptHttpResult> SendAsync(string method, string? url, string? body, IReadOnlyList<KeyValuePair<string, string>>? headers, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return ScriptHttpResult.Fail("no_permission");
        if (!ScriptHttpGuard.TryValidateUrl(url, out var uri, out var urlError) || uri is null) return ScriptHttpResult.Fail(urlError.Length > 0 ? urlError : "bad_url");
        headers ??= Array.Empty<KeyValuePair<string, string>>();
        if (headers.Count > ScriptHttpGuard.MaxHeaders || headers.Any(h => !ScriptHttpGuard.IsAllowedHeader(h.Key, h.Value))) return ScriptHttpResult.Fail("bad_header");
        bool post = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
        body ??= "";
        if (post && Encoding.UTF8.GetByteCount(body) > ScriptHttpGuard.MaxRequestBytes) return ScriptHttpResult.Fail("too_large");

        using var req = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, uri);
        if (post)
        {
            string trimmed = body.TrimStart();
            string mediaType = trimmed.StartsWith('{') || trimmed.StartsWith('[') ? "application/json" : "text/plain";
            req.Content = new StringContent(body, Encoding.UTF8, mediaType);   // Content-Type : <mediaType>; charset=utf-8
        }
        bool uaSet = false;
        foreach (var h in headers)
        {
            if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (req.Content is null) continue;   // GET : pas de corps -> Content-Type sans objet
                req.Content.Headers.Remove("Content-Type");
                if (!req.Content.Headers.TryAddWithoutValidation("Content-Type", h.Value)) return ScriptHttpResult.Fail("bad_header");
                continue;
            }
            if (string.Equals(h.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)) uaSet = true;
            if (!req.Headers.TryAddWithoutValidation(h.Key, h.Value)
                && (req.Content is null || !req.Content.Headers.TryAddWithoutValidation(h.Key, h.Value)))
                return ScriptHttpResult.Fail("bad_header");
        }
        if (!uaSet) req.Headers.TryAddWithoutValidation("User-Agent", ScriptHttpGuard.UserAgent);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ScriptHttpGuard.TimeoutMs);
        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (resp.Content.Headers.ContentLength is long len && len > ScriptHttpGuard.MaxResponseBytes) return ScriptHttpResult.Fail("too_large");
            // Lecture bornée à 1 Mio + 1 octet (borne AUSSI la décompression : une bombe gzip s'arrête au plafond).
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var buf = new byte[ScriptHttpGuard.MaxResponseBytes + 1];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), cts.Token).ConfigureAwait(false);
                if (n <= 0) break;
                total += n;
            }
            if (total > ScriptHttpGuard.MaxResponseBytes) return ScriptHttpResult.Fail("too_large");
            int status = (int)resp.StatusCode;
            string text = Encoding.UTF8.GetString(buf, 0, total);
            string? location = status is >= 300 and < 400 ? resp.Headers.Location?.ToString() : null;
            string? contentType = resp.Content.Headers.ContentType?.ToString();
            return new ScriptHttpResult(status is >= 200 and < 300, status, text, null, location, contentType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return ScriptHttpResult.Fail("timeout"); }
        catch (Exception ex) when (IsBlocked(ex)) { return ScriptHttpResult.Fail("blocked_address"); }
        catch (Exception) { return ScriptHttpResult.Fail("network"); }
    }

    private static bool IsBlocked(Exception? ex)
    {
        for (int i = 0; ex is not null && i < 8; i++, ex = ex.InnerException)
            if (ex is BlockedAddressException) return true;
        return false;
    }
}
