using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Extraction des cartes dans un PROCESSUS ENFANT (« GivreClone.exe --extract-maps ») lancé avec le ramasse-miettes serveur.
/// Pourquoi : la désérialisation des 17 688 cartes alloue énormément ; avec le GC poste de travail, les threads s'attendent aux pauses
/// (≈ 10 cartes/s, 1,8 cœur utilisé sur 8) ; avec le GC serveur ≈ 26-38 cartes/s. Le GC serveur garde ~1 Go pour toute la vie du processus : on ne
/// l'impose donc qu'à l'enfant, qui disparaît à la fin. Repli en cas d'échec : extraction dans le processus courant (comportement précédent).
/// </summary>
internal static class MapExtractChild
{
    /// <summary>Point d'entrée de l'enfant : extrait, écrit « fait total » sur la sortie standard à chaque palier. Code de sortie : 0 ok, 2 erreur, 3 client absent.</summary>
    public static int Run()
    {
        try
        {
            var reader = new MapCellReader();
            if (!reader.Available) return 3;
            using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            reader.ExtractAllAsync(new Sync(v => { try { stdout.WriteLine($"{v.done} {v.total}"); } catch { } })).GetAwaiter().GetResult();
            return 0;
        }
        catch { return 2; }
    }

    private sealed class Sync(Action<(int done, int total)> action) : IProgress<(int done, int total)>
    {
        public void Report((int done, int total) value) => action(value);
    }

    /// <summary>Lance l'enfant et relaie sa progression. False si l'enfant n'a pas pu démarrer / a échoué : l'appelant retombe sur l'extraction dans le processus.</summary>
    public static async Task<bool> TryRunAsync(IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("GIVRE_MAP_CHILD") == "0") return false;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
        var psi = new ProcessStartInfo(exe, "--extract-maps")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.Environment["DOTNET_gcServer"] = "1";
        psi.Environment["DOTNET_GCHeapCount"] = Math.Clamp(Environment.ProcessorCount, 2, 8).ToString();
        psi.Environment["DOTNET_gcConcurrent"] = "0";
        try
        {
            using var child = Process.Start(psi);
            if (child is null) return false;
            using var reg = ct.Register(() => { try { child.Kill(true); } catch { } });
            string? line;
            while ((line = await child.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                var parts = line.Split(' ');
                if (parts.Length == 2 && int.TryParse(parts[0], out var d) && int.TryParse(parts[1], out var t)) progress?.Report((d, t));
            }
            await child.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            GivreLauncher.Core.Transport.TransportLog.Line($"[map] extraction par processus enfant (GC serveur) terminée, code {child.ExitCode}");
            return child.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[map] processus enfant d'extraction impossible : {ex.GetType().Name}");
            return false;
        }
    }
}
