using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.ProtocolUpdates;

public sealed record ProtocolIndexerRunResult(bool Started, bool ReviewOnlyCompleted, int? ExitCode,
    string Message, string Stdout, string Stderr)
{
    public bool RuntimeCertified => false;
    public bool ProtocolPromoted => false;
}

/// <summary>
/// Optional *explicit-click only* runner. The only allowed CLI is the indexer's
/// pinned offline plan. It has no automatic invocation or --execute support.
/// Tests exercise the planner only; do not run the real indexer or Dofus.
/// </summary>
public static class ProtocolIndexerRunner
{
    private const int MaxCapturedChars = 131072;

    public static async Task<ProtocolIndexerRunResult> RunReviewOnlyAsync(
        ProtocolUpdateReport report, string workspaceRoot,
        string approvedPythonExe, string approvedPythonSha256,
        bool explicitlyConfirmed, string? destinationAppDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var plan = ProtocolIndexerPlanner.BuildReviewOnly(report, workspaceRoot,
            approvedPythonExe, approvedPythonSha256, explicitlyConfirmed, destinationAppDirectory);
        if (!plan.CanRun || plan.Executable is null || plan.WorkingDirectory is null ||
            plan.Arguments.Count != 9 || plan.Arguments[0] != "-m" || plan.Arguments[1] != "dri" ||
            plan.Arguments[2] != "rework-auto-update" || plan.Arguments[3] != "--givre-root" ||
            plan.Arguments[5] != "--output-root" || plan.Arguments[7] != "--version" ||
            plan.Arguments.Contains("--execute", StringComparer.Ordinal) || !plan.ReviewOnly || plan.DownloadsOrUpgrades)
            return new(false, false, null, plan.Message, "", "");

        var start = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in plan.Arguments) start.ArgumentList.Add(arg);
        foreach (var variable in plan.EnvironmentVariables) start.Environment[variable.Key] = variable.Value;
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (string key in new[] { "PYTHONHOME", "PYTHONINSPECT", "PYTHONSTARTUP" })
            start.Environment.Remove(key);

        using var process = new Process { StartInfo = start };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                return new(false, false, null, "Impossible de lancer Python en mode plan.", "", "");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var stdout = CaptureBoundedAsync(process.StandardOutput);
            var stderr = CaptureBoundedAsync(process.StandardError);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Only the child created above, never Dofus or another indexer process.
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                return new(true, false, process.ExitCode,
                    cancellationToken.IsCancellationRequested ? "Plan annulé." : "Plan arrêté après trois minutes.",
                    stdout.Result, stderr.Result);
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            bool safeReceipt = process.ExitCode == 0 && IsReviewOnlyReceipt(stdout.Result);
            return new(true, safeReceipt, process.ExitCode,
                safeReceipt ? "Plan indexeur hors ligne terminé ; comparaison réseau et ACK toujours à certifier."
                    : "Résultat de l'indexeur non validé : sortie inattendue ou échec.",
                stdout.Result, stderr.Result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new(false, false, null, "Lancement du plan local impossible.", "", "");
        }
    }

    /// <summary>Offline-only result gate; exposed so tests can reject forged upgrade receipts.</summary>
    public static bool IsReviewOnlyReceipt(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("status", out var status) && status.GetString() == "planned" &&
                root.TryGetProperty("mode", out var mode) && mode.GetString() == "plan" &&
                root.TryGetProperty("review_only", out var review) && review.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("runtime_certified", out var runtime) && runtime.ValueKind == JsonValueKind.False &&
                root.TryGetProperty("auto_promote", out var auto) && auto.ValueKind == JsonValueKind.False &&
                root.TryGetProperty("givre_runtime_modified", out var modified) && modified.ValueKind == JsonValueKind.False;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; }
    }

    private static async Task<string> CaptureBoundedAsync(StreamReader reader)
    {
        char[] buffer = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            int available = MaxCapturedChars - text.Length;
            if (available > 0) text.Append(buffer, 0, Math.Min(available, count));
            // Continue draining streams even after the display cap, avoiding pipe deadlocks.
        }
        return text.ToString();
    }
}
