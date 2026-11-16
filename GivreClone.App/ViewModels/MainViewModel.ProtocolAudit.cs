using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.ProtocolUpdates;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Contrôle local au démarrage ; la commande d'indexation est séparée, explicite
/// et limitée au programme DofusReverseIndexer du workspace. Aucune promotion
/// automatique d'un codec ni émission de paquet Dofus.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] public partial bool ProtocolAuditVisible { get; set; }
    [ObservableProperty] public partial bool ProtocolAuditBusy { get; set; }
    [ObservableProperty] public partial bool ProtocolIndexerAvailable { get; set; }
    [ObservableProperty] public partial string ProtocolAuditText { get; set; } =
        "Analyse des empreintes locales en attente…";
    [ObservableProperty] public partial string SocketReviewStatus { get; private set; } =
        "Socket · admission du mapping en cours de vérification";
    [ObservableProperty] public partial string SocketSessionSummary { get; private set; } =
        "Socket · sessions non encore observées";
    [ObservableProperty] public partial string SocketControlSummary { get; private set; } =
        "Canal de commandes · état non observé";
    [ObservableProperty] public partial string SocketCapabilitiesSummary { get; private set; } =
        "Connexion ≠ pilotage : mouvement, combat et expéditions nécessitent des preuves serveur distinctes.";
    [ObservableProperty] public partial string ProtocolShortStatus { get; private set; } =
        "Vérification locale en cours…";

    private ProtocolUpdateReport? _latestProtocolReport;

    /// <summary>Diagnostic volontairement masqué au démarrage, disponible depuis Outils.</summary>
    [RelayCommand]
    private void ToggleProtocolDetails()
    {
        ProtocolAuditVisible = !ProtocolAuditVisible;
        AdvancedToolsVisible = false;
        RefreshSocketUiSummary();
    }

    /// <summary>
    /// Observation UX read-only. Counts actual Socket sessions instead of a
    /// saved Socket preference; no authentication or gameplay status inferred.
    /// Called while the tools menu is shown, never starts/stops a session.
    /// </summary>
    public void RefreshSocketUiSummary()
    {
        var sessions = AccountsVM.Accounts.Where(row => row.Running && row.IsSocketSession).ToArray();
        int paused = sessions.Count(row => row.IsDisconnected || row.SocketBackoff);
        int live = sessions.Count(row => row.Activity == BotActivity.InGame
            && !row.IsDisconnected && !row.SocketBackoff);
        int pending = Math.Max(0, sessions.Length - live - paused);
        SocketSessionSummary = sessions.Length == 0
            ? "Socket · aucune session clientless active"
            : $"Socket · {sessions.Length} session(s), en jeu : {live}, en attente : {pending}, déconnectée(s) : {paused}";
        var controls = sessions.Select(row => AccountsVM.ObserveSocketControl(row)).ToArray();
        int registered = controls.Count(control => control.State == "channel-registered");
        int limited = controls.Count(control => control.State == "movement-diagnostic-only");
        int awaiting = controls.Count(control => control.State == "awaiting-map");
        int observationOnly = controls.Count(control => control.State == "observation-only");
        int suspended = controls.Count(control => control.State is "suspended" or "license-blocked" or "botting-paused");
        SocketControlSummary = sessions.Length == 0
            ? "Commandes · aucune session à contrôler"
            : $"Commandes · complet : {registered}/{sessions.Length}, déplacement seul : {limited}, " +
                $"carte attendue : {awaiting}, injecteur absent : {observationOnly}, suspendue(s) : {suspended}";
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshProtocolAuditAsync()
    {
        if (ProtocolAuditBusy) return;
        ProtocolAuditBusy = true;
        ProtocolIndexerAvailable = false;
        SocketReviewStatus = "Socket · vérification locale des versions et SHA en cours…";
        ProtocolAuditText = "Lecture de la version et des empreintes Dofus…";
        try
        {
            var selected = AppConfig.Current.DofusExePath;
            string? bundle = LocalVersionNotice.ReadDeclaredBuild(
                Path.Combine(AppContext.BaseDirectory, "data", "bundle-build.json"));
            var scanned = await Task.Run(() =>
            {
                try
                {
                    string exe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(selected);
                    var report = ProtocolUpdateProbe.Inspect(exe, bundle);
                    var workspace = FindGivreWorkspace();
                    if (workspace is not null && report.InstalledVersion is not null)
                    {
                        string indexerRoot = Path.Combine(workspace, "DofusReverseIndexer");
                        string? reference = FindPreviousProtoVersion(indexerRoot, report.InstalledVersion);
                        if (reference is not null) report = report.WithLocalSchemaEvidence(indexerRoot, reference);
                    }
                    var observationsRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GivreClone", "ProtocolObservations");
                    ProtocolObservedDrift.Result observation = ProtocolObservedDrift.CompareAndRemember(
                        observationsRoot, Path.GetDirectoryName(exe)!, report);
                    // The long-form protocol audit is independent from Socket.
                    // A hash-pinned Socket candidate is not a gameplay verdict.
                    MZP12SocketVersionBinding.Decision? socketAdmission = null;
                    if (report.Status == ProtocolUpdateStatus.VERSION_CHANGED && LocalEdition.Enabled)
                    {
                        var current = MZP11CytrusVersionProbe.InspectInstalledDofus(exe);
                        socketAdmission = MZP12SocketVersionBinding.Evaluate(
                            MappingCatalog.SessionVersionBinding, current);
                    }
                    return (Report: report, Observation: observation.Message,
                        Changed: observation.Changed, SocketAdmission: socketAdmission);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    return (Report: ProtocolUpdateProbe.Inspect(null, bundle),
                        Observation: "Repère local non accessible.", Changed: false,
                        SocketAdmission: (MZP12SocketVersionBinding.Decision?)null);
                }
            }).ConfigureAwait(true);

            var result = scanned.Report;
            _latestProtocolReport = result;
            // La découverte d'une nouvelle version publiée est indépendante du
            // diagnostic de la version réellement installée. Elle ne bloque pas
            // l'application et ne déclenche aucune mise à jour automatique.
            var published = await OfficialDofusReleaseProbe.CheckAsync().ConfigureAwait(true);
            bool newerRelease = published.IsNewerThan(result.InstalledVersion);
            // The local observations are an UNTRUSTED historical comparison.
            // Updating from 3.7.11 to a SHA-pinned *reviewed* 3.7.12 naturally
            // changes that old record. It must not keep a false update banner.
            // An actual trusted-baseline HASH_CHANGED is never suppressed, and
            // any unreviewed local drift continues to require attention.
            bool unresolvedHashDrift = LocalVersionNotice.HasUnresolvedHashDrift(
                scanned.Changed, result.Status == ProtocolUpdateStatus.HASH_CHANGED,
                scanned.SocketAdmission);
            var versionNotice = LocalVersionNotice.Compare(result.InstalledVersion, bundle,
                scanned.SocketAdmission,
                installedHashesChanged: unresolvedHashDrift);
            VersionNoticeText = versionNotice.Message;
            // Published third-party updates are informational; they do not
            // invalidate the installed-client admission or justify an amber
            // banner. Installed version or SHA drift still does.
            VersionNoticeVisible = versionNotice.RequiresAttention;
            bool sameProtocolFamily = MZP10MappingVersionAdmission.IsSameProtocolFamily(result.InstalledVersion, bundle);
            SocketReviewStatus = versionNotice.RequiresAttention
                ? "Socket · catalogue requis : " + versionNotice.Message
                : $"Socket · Dofus {result.InstalledVersion} compatible avec le catalogue {bundle}";
            ProtocolShortStatus = versionNotice.RequiresAttention
                ? "Nouvelle famille de protocole · catalogue dédié nécessaire"
                : "Protocole compatible · correctifs de la même branche";
            SocketCapabilitiesSummary = sameProtocolFamily
                ? "Catalogue partagé au sein de la branche majeure.mineure ; aucune revue protocole requise entre correctifs."
                : "Un catalogue propre à la nouvelle branche est nécessaire.";
            string auditSummary = sameProtocolFamily
                ? versionNotice.Message
                : result.Message;
            string observedText = sameProtocolFamily
                ? "Les changements d'empreintes binaires ne changent pas la famille du protocole ; les modules natifs sont vérifiés séparément."
                : scanned.Changed && !unresolvedHashDrift
                ? "Observation historique différente ; revue SHA du client courant prise en compte."
                : scanned.Observation;
            ProtocolAuditText = auditSummary + " " + observedText + " " +
                (sameProtocolFamily ? "" : result.SchemaEvidence is { } schema ? " " + schema.Message :
                    " Aucune paire d'extractions protobuf locales vérifiées.") +
                " " + published.Message +
                (newerRelease ? " GivreWork reste sur le client installé ; version publiée non admise automatiquement."
                    : "") +
                " Codes wire et ACK de gameplay non certifiés par cette analyse locale.";
            ProtocolIndexerAvailable = result.CanRun;
            RefreshSocketUiSummary();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _latestProtocolReport = null;
            ProtocolAuditText = "Diagnostic local indisponible : " + e.GetType().Name;
            ProtocolShortStatus = "Audit local indisponible";
            SocketReviewStatus = "Socket · admission non vérifiée (diagnostic indisponible)";
            SocketCapabilitiesSummary = "Impossible de conclure sur le mapping, le déplacement ou le combat.";
            VersionNoticeText = "Contrôle de version et d'empreintes indisponible. Vérifiez l'installation avant pilotage.";
            VersionNoticeVisible = true;
        }
        finally { ProtocolAuditBusy = false; }
    }

    /// <summary>
    /// Exécute le PLANNING hors réseau de l'indexeur, sur action de l'utilisateur.
    /// --execute n'est jamais transmis : l'indexeur ne télécharge ni ne promeut.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RunIndexerReviewAsync() => RunIndexerAsync(extract: false);

    /// <summary>
    /// Seul ce bouton secondaire déclenche les acquisitions et l'extraction de
    /// candidats dans les caches isolés de l'indexeur. Aucun mapping n'est activé.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RunIndexerExtractionAsync() => RunIndexerAsync(extract: true);

    private async Task RunIndexerAsync(bool extract)
    {
        if (ProtocolAuditBusy) return;
        // Indexer output belongs to the requested details, never a permanent
        // banner. Close the popup so the operator can see this explicit action.
        AdvancedToolsVisible = false;
        ProtocolAuditVisible = true;
        await RefreshProtocolAuditAsync().ConfigureAwait(true);
        if (_latestProtocolReport?.CanRun != true) return;
        ProtocolAuditBusy = true;
        ProtocolIndexerAvailable = false;
        try
        {
            string? workspace = FindGivreWorkspace();
            string? python = FindPython();
            string? sha = null;
            if (python is not null)
            {
                using var stream = new FileStream(python, FileMode.Open, FileAccess.Read, FileShare.Read);
                sha = Convert.ToHexString(SHA256.HashData(stream));
            }
            var plan = ProtocolIndexerPlanner.BuildReviewOnly(_latestProtocolReport,
                workspace, python, sha, userConfirmed: true,
                destinationAppDirectory: FindOwnedAppDirectory(workspace));
            if (!plan.CanRun || plan.Executable is null || plan.WorkingDirectory is null)
            {
                ProtocolAuditText = "Indexeur indisponible : " + plan.Message;
                return;
            }
            if (extract && !HasExtractionSpace(plan.WorkingDirectory))
            {
                ProtocolAuditText = "Extraction refusée : espace libre inférieur à 3 Gio.";
                return;
            }
            ProtocolAuditText = extract
                ? "Extraction et comparaison DofusReverseIndexer en cours (revue seulement, aucune activation de protocole)…"
                : "Plan hors ligne DofusReverseIndexer en cours…";
            string response = await Task.Run(async () =>
            {
                var info = new ProcessStartInfo(plan.Executable)
                {
                    WorkingDirectory = plan.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string argument in plan.Arguments) info.ArgumentList.Add(argument);
                // Un clic distinct et explicite est nécessaire pour permettre l'acquisition.
                if (extract) info.ArgumentList.Add("--execute");
                foreach (var pair in plan.EnvironmentVariables) info.Environment[pair.Key] = pair.Value;
                info.Environment["PYTHONNOUSERSITE"] = "1";
                info.Environment["PYTHONIOENCODING"] = "utf-8";
                foreach (string key in new[] { "PYTHONHOME", "PYTHONINSPECT", "PYTHONSTARTUP" })
                    info.Environment.Remove(key);
                using var child = Process.Start(info) ??
                    throw new InvalidOperationException("Indexeur non démarré.");
                using var limit = new CancellationTokenSource(extract
                    ? TimeSpan.FromMinutes(25) : TimeSpan.FromMinutes(2));
                Task<string> stdout = ReadBoundedAsync(child.StandardOutput, limit.Token);
                Task<string> stderr = ReadBoundedAsync(child.StandardError, limit.Token);
                try { await child.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    throw;
                }
                string output = await stdout.ConfigureAwait(false);
                string errors = await stderr.ConfigureAwait(false);
                bool receiptValid = child.ExitCode == 0 &&
                    (extract ? IsSafeExtractionReceipt(output)
                        : ProtocolIndexerRunner.IsReviewOnlyReceipt(output));
                return "Code " + child.ExitCode + " · " +
                    (receiptValid ? "Revue de schémas produite (non certifiée)" :
                        "Revue incomplète ou reçu indexeur non vérifiable") + ". " +
                    (string.IsNullOrWhiteSpace(errors) ? output : errors);
            }).ConfigureAwait(true);
            ProtocolAuditText = "DofusReverseIndexer (sans activation de mapping) : " +
                TruncateForUi(response, 950);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException)
        {
            ProtocolAuditText = "Analyse interrompue ou indisponible : " + ex.GetType().Name +
                ". Aucun mapping de runtime n'a été promu.";
        }
        finally
        {
            ProtocolAuditBusy = false;
            ProtocolIndexerAvailable = _latestProtocolReport?.CanRun == true;
        }
    }

    private static bool HasExtractionSpace(string existingDirectory)
    {
        var root = Path.GetPathRoot(existingDirectory);
        return root is not null && new DriveInfo(root).AvailableFreeSpace >= 3L * 1024 * 1024 * 1024;
    }

    private static string? FindGivreWorkspace()
    {
        var environment = Environment.GetEnvironmentVariable("GIVRE_WORKSPACE_ROOT");
        var userVideos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Videos", "GIVRE");
        foreach (string? candidate in new[] { environment, userVideos })
        {
            if (candidate is null) continue;
            var full = Path.GetFullPath(candidate);
            if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(full)), "GIVRE",
                    StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) return full;
        }
        return null;
    }

    private static string? FindPython()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python");
        foreach (int version in new[] { 314, 313, 312, 311, 310 })
        {
            string exe = Path.Combine(baseDir, "Python" + version, "python.exe");
            if (File.Exists(exe) && !File.GetAttributes(exe).HasFlag(FileAttributes.ReparsePoint))
                return exe;
        }
        return null;
    }

    private static string FindOwnedAppDirectory(string? workspace)
    {
        if (workspace is null) return "GivreWork";
        string relative = Path.GetRelativePath(workspace, AppContext.BaseDirectory);
        string folder = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return folder is "GivreWork-Integration-20261020" or "GivreWork"
            ? folder : "GivreWork";
    }

    private static string? FindPreviousProtoVersion(string indexerRoot, string currentVersion)
    {
        string directory = Path.Combine(indexerRoot, "local-output", "historical-extractions-v3");
        if (!Directory.Exists(directory) || !Version.TryParse(currentVersion, out var current))
            return null;
        Version? previous = null;
        string? previousString = null;
        foreach (string path in Directory.EnumerateDirectories(directory).Take(300))
        {
            string name = Path.GetFileName(path);
            if (!ProtocolUpdateProbe.ValidVersion(name) || !Version.TryParse(name, out var candidate)
                || candidate >= current || (previous is not null && candidate <= previous)) continue;
            previous = candidate;
            previousString = name;
        }
        return previousString;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder(4096);
        char[] buffer = new char[2048];
        int size;
        while ((size = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            if (text.Length < 8192) text.Append(buffer, 0, Math.Min(size, 8192 - text.Length));
        return text.ToString();
    }

    private static string TruncateForUi(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "… (rapport dans le dossier de revue)";

    /// <summary>
    /// La comparaison obtenue par extraction reste un artefact de revue uniquement :
    /// le processus ne peut jamais transformer son code de sortie en permission gameplay.
    /// </summary>
    private static bool IsSafeExtractionReceipt(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            bool Check(string property, JsonValueKind expected) =>
                root.TryGetProperty(property, out var value) && value.ValueKind == expected;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("mode", out var mode) && mode.GetString() == "execute" &&
                root.TryGetProperty("status", out var status) &&
                status.GetString() is "review-required" or "planned" &&
                Check("review_only", JsonValueKind.True) &&
                Check("runtime_certified", JsonValueKind.False) &&
                Check("auto_promote", JsonValueKind.False) &&
                Check("givre_runtime_modified", JsonValueKind.False);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return false; }
    }
}
