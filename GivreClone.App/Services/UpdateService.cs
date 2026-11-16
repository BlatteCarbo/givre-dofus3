using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Mise à jour côté APP (flux vérifié Ed25519). Le pointeur (download_url + sha256) provient de l'enveloppe
/// /version SIGNÉE, déjà vérifiée par <see cref="LicenseService"/> (Ed25519 + server_time) -> le sha256 est
/// AUTHENTIFIÉ. On télécharge le ZIP, on vérifie son SHA-256 contre le hash SIGNÉ, puis on passe le ZIP au
/// swappeur NATIF (GivreUpdater.exe) qui remplace les fichiers à froid et relance. L'app ne touche pas au disque
/// applicatif elle-même (elle est verrouillée tant qu'elle tourne).
/// </summary>
public static class UpdateService
{
    public static string InstallRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Givre");
    public static string UpdaterPath => Path.Combine(InstallRoot, "GivreUpdater.exe");

    /// <summary>Compare deux versions "x.y.z" : true si <paramref name="server"/> &gt; <paramref name="current"/>.</summary>
    public static bool IsNewer(string? server, string current)
    {
        if (string.IsNullOrWhiteSpace(server)) return false;
        var a = server.Split('.'); var b = current.Split('.');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length && int.TryParse(a[i], out var xa) ? xa : 0;
            int y = i < b.Length && int.TryParse(b[i], out var yb) ? yb : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>Une MAJ est disponible ET installable (pointeur présent + version serveur plus récente / imposée).</summary>
    public static bool UpdateAvailable(VersionVerdict v) =>
        !string.IsNullOrWhiteSpace(v.DownloadUrl) && !string.IsNullOrWhiteSpace(v.Sha256)
        && (v.Outdated || v.Mandatory || IsNewer(v.CurrentVersion, LicenseService.AppVersion));

    /// <summary>true si le swappeur natif est installé (sinon : réinstallation via l'amorceur requise).</summary>
    public static bool UpdaterPresent => File.Exists(UpdaterPath);

    /// <summary>Télécharge le ZIP, vérifie le SHA-256 SIGNÉ, lance le swappeur natif. Renvoie null si le hand-off a
    /// démarré (l'appelant DOIT alors fermer l'app pour libérer les locks), sinon un message d'erreur.</summary>
    public static async Task<string?> DownloadAndHandOffAsync(VersionVerdict v, IProgress<double>? progress,
                                                              Action<string>? status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(v.DownloadUrl) || string.IsNullOrWhiteSpace(v.Sha256))
            return "Aucun paquet à télécharger.";
        if (!UpdaterPresent)
            return "Composant de mise à jour introuvable (réinstallation requise).";

        var staging = Path.Combine(InstallRoot, "update");
        Directory.CreateDirectory(staging);
        var zip = Path.Combine(staging, "pkg.zip");

        try
        {
            status?.Invoke("Téléchargement…");
            // Cloudflare (plan gratuit) : coupure edge / 524 (~100 s) / 429 = transitoire -> RETRY (backoff+jitter).
            // Pas de reprise partielle : le SHA-256 est revérifié après, donc un retry from-scratch reste sûr.
            const int maxDl = 4;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("GivreClient/" + LicenseService.AppVersion);   // allowlist WAF
                    using var resp = await http.GetAsync(v.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    int code = (int)resp.StatusCode;
                    if (code is >= 400 and < 500 && code != 429) return $"Téléchargement refusé (HTTP {code}).";   // 4xx = fatal
                    resp.EnsureSuccessStatusCode();   // 5xx/524/autre -> exception -> retry
                    long total = resp.Content.Headers.ContentLength ?? (v.Size > 0 ? v.Size : -1);
                    using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var dst = File.Create(zip);
                    var buf = new byte[81920]; long read = 0; int n;
                    while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        read += n;
                        if (total > 0) progress?.Report(Math.Clamp((double)read / total, 0, 1));
                    }
                    break;   // téléchargement complet
                }
                catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxDl)
                {
                    Logger.Warn($"Download MAJ échoué (essai {attempt}/{maxDl}) : {ex.Message}");
                    progress?.Report(0);
                    try { await Task.Delay(TimeSpan.FromSeconds(2 * attempt) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }

            status?.Invoke("Vérification de l'intégrité…");
            if (!await VerifySha256Async(zip, v.Sha256!, ct).ConfigureAwait(false))
                return "Intégrité invalide (SHA-256) — mise à jour annulée.";

            status?.Invoke("Installation…");
            var target = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var args = $"update --pid {Environment.ProcessId} --zip \"{zip}\" --sha256 {v.Sha256} --target \"{target}\" --relaunch GivreClone.exe";
            Process.Start(new ProcessStartInfo(UpdaterPath, args) { UseShellExecute = false, WorkingDirectory = InstallRoot });
            return null;   // succès : l'appelant ferme l'app -> le swappeur attend notre sortie puis remplace/relance
        }
        catch (Exception e)
        {
            Logger.Warn($"Auto-update échouée : {e.Message}");
            return e.Message;
        }
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        if (expectedHex.Length != 64) return false;
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()));
    }
}
