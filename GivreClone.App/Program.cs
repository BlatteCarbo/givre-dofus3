using Avalonia;
using System;
using System.IO;

namespace GivreClone.App;

sealed class Program
{
    // Nothing Avalonia / SynchronizationContext-reliant before AppMain runs - not initialized yet.
    [STAThread]
    public static void Main(string[] args)
    {
        // OUTIL DE RELEASE (build-release.ps1) : exporte les tables de positions de cartes pour le dev web (validation des
        // archimonstres + carte publique) depuis le jeu installé, avec le MÊME extracteur que l'app. Aucune UI, aucune
        // licence, aucun journal : écrit map-positions.json + map-positions-with-subarea.json puis quitte (0 ok, 2 échec).
        if (args.Length == 2 && args[0] == "--export-map-positions") { Environment.Exit(ExportMapPositions(args[1])); return; }
        // Extraction des cartes dans un processus enfant (GC serveur) : aucune UI, aucune licence, voir Services/MapExtractChild.cs.
        if (args.Length >= 1 && args[0] == "--extract-maps") { Environment.Exit(Services.MapExtractChild.Run()); return; }
        EnableHighResolutionTimer();
        // Le journal de performance montre des pauses du ramasse-miettes de 0,5 à 1,9 s toutes les quelques secondes (gel de l'interface et retards des bots) : on demande des collectes de génération 2
        // EN ARRIÈRE-PLAN (pas de collecte bloquante complète tant que la mémoire n'est pas critique).
        // LatencyMode.Interactive permet au ramasse-miettes de collecter la Gen2 et le LOH (grandes allocations)
        // plutôt que de retenir indéfiniment 3-4 Go de mémoire morte comme avec SustainedLowLatency.
        try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive; } catch (InvalidOperationException) { }
        // Journal de diagnostic (codes opaques, cf docs/diag-codes.md) : version + détection d'une fin anormale précédente.
        Services.Diag.Init(Services.LicenseService.AppVersion, !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
        // Garde-fou : une exception de fond / non-observée ne doit plus fail-fast l'app.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ux) Services.Diag.Log(102, Services.Diag.Ex(ux));
            Log(e.ExceptionObject as Exception, "AppDomain");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception, "Task"); e.SetObserved(); };
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Services.Diag.Log(101);   // sortie PROPRE (absente au prochain démarrage = crash/fail-fast/kill -> 103)
        }
        catch (UnauthorizedAccessException ex)
        {
            Log(ex, "FatalAccess");
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
            // Startup can fail before Avalonia has created its first window. Do not
            // turn a storage permission failure into an opaque CLR crash dialog.
            if (OperatingSystem.IsWindows())
                MessageBoxW(IntPtr.Zero,
                    "Givre ne peut pas accéder à son dossier de données.\n\n" +
                    "Lancez l’application depuis Windows avec votre compte habituel. " +
                    "Si le problème persiste, vérifiez les permissions du dossier :\n\n" +
                    Services.MZP19LocalClone.DataRoot + "\n\n" +
                    "Aucune permission n’a été contournée et aucune donnée n’a été déplacée.",
                    "Givre — accès aux données refusé", 0x10);
        }
        catch (Exception ex)
        {
            Log(ex, "Fatal");
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    // Windows arrondit Task.Delay / Thread.Sleep à la résolution du minuteur du processus (15,6 ms par défaut : Sleep(1) ≈ 15,4 ms).
    // Chaque attente courte du combat (accusés, boucles de 10-25 ms, barrières de séquence) arrivait donc 10-30 ms en retard ;
    // Givre demande 1 ms (EasyBot\Main\Program.vb). Sous Windows 11 on retire aussi la limitation
    // « ignorer les demandes de résolution » (processus en arrière-plan / fenêtre masquée). GIVRE_TIMER_RESOLUTION=0 désactive.
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState { public uint Version, ControlMask, StateMask; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(IntPtr hProcess, int informationClass, ref ProcessPowerThrottlingState information, int informationSize);

    private static void EnableHighResolutionTimer()
    {
        if (Environment.GetEnvironmentVariable("GIVRE_TIMER_RESOLUTION") == "0" || !OperatingSystem.IsWindows()) return;
        try { timeBeginPeriod(1); } catch { /* winmm absent : on garde 15,6 ms */ }
        try
        {
            var state = new ProcessPowerThrottlingState { Version = 1, ControlMask = 4, StateMask = 0 };   // 4 = IGNORE_TIMER_RESOLUTION
            SetProcessInformation(System.Diagnostics.Process.GetCurrentProcess().Handle, 4, ref state, 12);
        }
        catch { /* Windows < 11 */ }
    }

    private static int ExportMapPositions(string dir)
    {
        try
        {
            var full = Services.MapDataExtractor.TryExtractFull();
            if (full is not { Count: > 0 }) return 2;
            Directory.CreateDirectory(dir);
            Services.MapDataExtractor.WritePanelTable(Path.Combine(dir, "map-positions.json"), full, includeSubArea: false);
            Services.MapDataExtractor.WritePanelTable(Path.Combine(dir, "map-positions-with-subarea.json"), full, includeSubArea: true);
            return 0;
        }
        catch { return 2; }
    }

    // Journal local (jamais livré, pas dans le binaire) pour diagnostiquer les crashs.
    internal static void Log(Exception? ex, string origin)
    {
        if (ex is null) return;
        try
        {
            var dir = Services.MZP19LocalClone.DataRoot;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{origin}] {DateTime.Now:O}\n{ex}\n\n");
        }
        catch { /* le log ne doit jamais crasher */ }
    }

    // don't remove - also used by the visual designer
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
