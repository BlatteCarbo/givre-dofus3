using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Browser-only, user-initiated TEST authentication. It intentionally cannot
/// register a game account, launch Dofus, or reuse the original Givre browser
/// profile. Success means only a legitimate WebAuth response was returned:
/// no Launcher 2 child, server session or gameplay ACK is inferred.
/// </summary>
public static class MZP18IsolatedWebAuthRunner
{
    public enum State { Succeeded, Cancelled, Failed, Unavailable }
    public sealed record Result(State State, string Reason);

    private sealed record Witness(string Name, long Size, string Sha);
    private static readonly Witness[] Files =
    {
        new("MZP18IsolatedWebAuth.exe", 162304, "CACE60C4E4525BAEBE2C5D6E466C50B1C0215A4A6E5D806BEA69731F4FA66A44"),
        new("MZP18IsolatedWebAuth.dll", 56320, "384431D2C004297487343B7B940A3BEB42155BCA182998A65EA3364FB4510DE5"),
        new("MZP18IsolatedWebAuth.deps.json", 1869, "5A6621159F971854A0E344FE6C99291476640003E0D05B0BB3351E5EB72A0081"),
        new("MZP18IsolatedWebAuth.runtimeconfig.json", 519, "96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3"),
        new("Microsoft.Web.WebView2.Core.dll", 567896, "616157863B1CD1F1330B4914DD013ADEF10A4CE8CD819598FD6BD4037C2C1646"),
        new("Microsoft.Web.WebView2.WinForms.dll", 38360, "A2DDFDE024F6A248576A69CE9A54B5407AD915EE8FEC7DFF3A089A60948AC0AE"),
        new("Microsoft.Web.WebView2.Wpf.dll", 51160, "2FE5FBCE16F19324A41AE40EB81FEF4E48A853969185F24AB7902350FEABF536"),
        new("WebView2Loader.dll", 165336, "1CBCA1698175936A93D0C4D4148ED355E1B91415C6A4DB6D762F017DE3B6EB9A")
    };

    /// <summary>Purely static verification of the helper shipped with the clone.</summary>
    public static bool IsHelperVerified => TryAcquireHelper(out var lease, out _)
        ? DisposeSuccessfulLease(lease)
        : false;

    private static bool DisposeSuccessfulLease(FileStream? lease)
    {
        lease?.Dispose();
        return true;
    }

    private static bool SafeDirectory(string path)
    {
        var attr = File.GetAttributes(path);
        return attr.HasFlag(FileAttributes.Directory) && !attr.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool IsPrivateRunDirectory(string run)
    {
        var current = WindowsIdentity.GetCurrent().User;
        if (current is null) return false;
        var acl = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(run));
        if (!acl.AreAccessRulesProtected ||
            !current.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) return false;
        bool ownerAllowed = false;
        foreach (FileSystemAccessRule rule in
                 acl.GetAccessRules(includeExplicit: true, includeInherited: true,
                     targetType: typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!current.Equals(rule.IdentityReference)) return false;
            ownerAllowed = true;
        }
        return ownerAllowed;
    }

    private static bool TryAcquireHelper(out FileStream? executable, out string path)
    {
        executable = null;
        path = string.Empty;
        bool verified = false;
        try
        {
            var app = Path.GetFullPath(AppContext.BaseDirectory);
            var folder = Path.Combine(app, "WebAuthTest");
            if (!SafeDirectory(app) || !SafeDirectory(folder)) return false;
            foreach (var witness in Files)
            {
                var file = Path.Combine(folder, witness.Name);
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) return false;
                var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                bool keep = false;
                try
                {
                    if (stream.Length != witness.Size ||
                        !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), witness.Sha,
                            StringComparison.Ordinal))
                        return false;
                    if (witness.Name == "MZP18IsolatedWebAuth.exe")
                    {
                        executable = stream;
                        path = file;
                        keep = true;
                    }
                }
                finally { if (!keep) stream.Dispose(); }
            }
            verified = executable is not null;
            return verified;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                     or System.Security.SecurityException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            // If a later witness failed, do not leave the earlier executable pinned.
            if (!verified)
            {
                executable?.Dispose();
                executable = null;
            }
        }
    }

    /// <summary>
    /// Only creates a new random folder below the explicit MZP3 TEST root.
    /// Browser cookies, if saved by WebView2, never share an original profile.
    /// No automatic recursive cleanup of unknown browser-created files.
    /// </summary>
    private static string? CreateTestProfileRoot()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var replica = Path.Combine(local, "GivreReplica");
            var runs = Path.Combine(replica, "TestRuns");
            var run = Path.Combine(runs, "givre-app-offline");
            foreach (var p in new[] { local, replica, runs, run })
                if (!SafeDirectory(p)) return null;
            if (!IsPrivateRunDirectory(run)) return null;
            var browser = Path.Combine(run, "webauth");
            if (!Directory.Exists(browser)) Directory.CreateDirectory(browser);
            if (!SafeDirectory(browser)) return null;
            var unique = Path.Combine(browser, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unique);
            return SafeDirectory(unique) ? unique : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                     or System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>No credentials from stored accounts or launcher keydata are accessed.</summary>
    public static async Task<Result> RunInteractiveAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(State.Unavailable, "Navigateur TEST disponible uniquement sur Windows.");
        if (!TryAcquireHelper(out var lease, out var exe))
            return new(State.Unavailable, "Compagnon TEST absent ou altéré (SHA).");
        using (lease)
        {
            if (cancellationToken.IsCancellationRequested)
                return new(State.Cancelled, "Opération annulée.");
            var profile = CreateTestProfileRoot();
            if (profile is null)
                return new(State.Unavailable, "Dossier de navigateur TEST privé indisponible.");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = false
            };
            psi.Environment["MZ_TEST_PROFILE_ROOT"] = profile;
            psi.Environment["MZ_WEBAUTH_HS"] = WebAuthRunner.MZP18TestCompanionHandshake();
            psi.Environment.Remove("MZ_WEBAUTH_FAILOVER");
            psi.ArgumentList.Add("--lang");
            psi.ArgumentList.Add("fr");
            Process? child = null;
            try
            {
                child = Process.Start(psi);
                if (child is null) return new(State.Unavailable, "Compagnon TEST non créé.");
                    // Blank interactive fields: the user types credentials only
                    // on the official Ankama login page within a NEW WebView2.
                    await child.StandardInput.WriteLineAsync("{}").ConfigureAwait(false);
                    child.StandardInput.Close();
                    var output = await child.StandardOutput.ReadToEndAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    if (output.Length > 16_384)
                        return new(State.Failed, "Réponse navigateur hors limites.");
                    using var response = JsonDocument.Parse(output);
                    var root = response.RootElement;
                    if (child.ExitCode == 0 &&
                        root.TryGetProperty("access_token", out var token) &&
                        token.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(token.GetString()))
                        return new(State.Succeeded, "Authentification WebAuth reçue (jetons non conservés). Aucun client de jeu lancé.");
                    var state = root.TryGetProperty("status", out var s) &&
                                s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    return state == "cancelled"
                        ? new(State.Cancelled, "Connexion navigateur annulée.")
                        : new(State.Failed, "Authentification navigateur non confirmée.");
            }
            catch (OperationCanceledException)
            {
                if (child is { HasExited: false })
                    try { child.Kill(entireProcessTree: false); } catch { }
                return new(State.Cancelled, "Fenêtre de test fermée ou navigation interrompue.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                         or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
            {
                return new(State.Failed, "Compagnon TEST échoué (" + e.GetType().Name + ").");
            }
            finally { child?.Dispose(); }
        }
    }
}
