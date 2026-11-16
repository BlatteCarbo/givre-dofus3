using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>
/// Reads only the public Cytrus metadata advertised by Ankama. This is an
/// announcement of a client build, NOT evidence of wire compatibility.
/// Never downloads or changes game files and never executes an external binary.
/// </summary>
public static class OfficialDofusReleaseProbe
{
    private static readonly Uri Metadata = new("https://cytrus.cdn.ankama.com/cytrus.json");
    private const int MaxResponseBytes = 64 * 1024;

    public sealed record Result(string? LatestVersion, string Message)
    {
        public bool VerifiedMetadata => LatestVersion is not null;
        public bool ProtocolCertified => false;
        public bool CanAutomaticallyUpgrade => false;
        public bool IsNewerThan(string? installed)
        {
            if (!ProtocolUpdateProbe.ValidVersion(LatestVersion) ||
                !ProtocolUpdateProbe.ValidVersion(installed) ||
                !Version.TryParse(LatestVersion, out var latest) ||
                !Version.TryParse(installed, out var existing)) return false;
            return latest > existing;
        }
    }

    public static Result Parse(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaxResponseBytes)
            return Unknown();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 9 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("games", out var games) ||
                !games.TryGetProperty("dofus", out var dofus) ||
                !dofus.TryGetProperty("platforms", out var platforms) ||
                !platforms.TryGetProperty("windows", out var windows) ||
                !windows.TryGetProperty("dofus3", out var release) ||
                release.ValueKind != JsonValueKind.String)
                return Unknown();
            string? raw = release.GetString();
            if (raw is null || !raw.StartsWith("6.0_", StringComparison.Ordinal))
                return Unknown();
            string gameVersion = raw[4..];
            return ProtocolUpdateProbe.ValidVersion(gameVersion)
                ? new(gameVersion, "Version publiée sur le CDN officiel (dofus3/windows) : " + gameVersion +
                    ". Compatibilité réseau non déduite.")
                : Unknown();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return Unknown(); }
    }

    public static async Task<Result> CheckAsync(CancellationToken token = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
            using var request = new HttpRequestMessage(HttpMethod.Get, Metadata);
            using var reply = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (reply.StatusCode != HttpStatusCode.OK ||
                reply.Content.Headers.ContentLength is > MaxResponseBytes)
                return Unknown();
            await using var stream = await reply.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > MaxResponseBytes) return Unknown();
                memory.Write(buffer, 0, read);
            }
            return Parse(new UTF8Encoding(false, true).GetString(memory.ToArray()));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
            InvalidOperationException or DecoderFallbackException or TaskCanceledException)
        { return Unknown(); }
    }

    private static Result Unknown() => new(null,
        "Dernière version en ligne non vérifiée (connexion ou format indisponible).");
}
