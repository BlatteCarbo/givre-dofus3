using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>Issue de l'attente d'un fournisseur de mods client (Dofus Enhanced / Better Dofus) après un lancement.</summary>
public enum EnhancedVerdict
{
    /// <summary>Pas encore tranché (ou client fermé) : ne rien injecter.</summary>
    Pending,
    /// <summary>Le module est chargé et actif : il possède les crochets natifs.</summary>
    Owns,
    /// <summary>Le module ne se chargera pas sur ce client : les mods Givre prennent le relais.</summary>
    Incompatible,
}

public static class DofusEnhanced
{
    private static readonly ConcurrentDictionary<int, byte> Activations = new();
    private static readonly ConcurrentDictionary<int, byte> IncompatiblePids = new();
    private static readonly ConcurrentDictionary<string, string> HashCache = new(StringComparer.OrdinalIgnoreCase);
    internal static bool ActivationInProgress(int pid) => Activations.ContainsKey(pid);

    /// <summary>Dernier verdict statique (release.ini vs client réel) : true = aucun fournisseur installé ne convient.</summary>
    internal static volatile bool LastProbeIncompatible;

    internal static void MarkIncompatible(int pid) => IncompatiblePids[pid] = 0;
    internal static bool IsMarkedIncompatible(int pid) => IncompatiblePids.ContainsKey(pid);

    private const string LegacyDll = "DofusMiniatures.dll";
    private const string BetterDofusDll = "BetterDofus.dll";
    private static readonly TimeSpan EngineGrace = TimeSpan.FromSeconds(2);

    private static string ToolsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "Tools", "DofusEnhanced");

    /// <summary>Dossier de la révision r126 (Dofus Enhanced historique, 3.7.12.12).</summary>
    public static string Folder => Path.Combine(ToolsRoot, "r126", "DofusMiniatures-3.7.11.11-r126");

    public static bool Installed => Providers().Count > 0;

    /// <summary>Un fournisseur installé : dossier de version, chargeur, DLL, fichier de réglages partagé.</summary>
    public sealed record Provider(string Name, string Folder, string LoaderName, string DllName, string SettingsFile, bool Legacy)
    {
        public string Loader => Path.Combine(Folder, LoaderName);
        public string Dll => Path.Combine(Folder, DllName);
        public string ReleaseIni => Path.Combine(Folder, "release.ini");
        public string AutoloadLog => Path.Combine(Folder, "autoload.log");
    }

    /// <summary>
    /// Fournisseurs installés, préférés d'abord : Better Dofus (<c>BetterDofus-X.Y.Z</c>, le plus récent d'abord) puis r126.
    /// Better Dofus s'installe en dépliant son zip dans le dossier des outils : aucune copie n'est faite ici.
    /// </summary>
    internal static IReadOnlyList<Provider> Providers()
    {
        var list = new List<Provider>();
        try
        {
            if (Directory.Exists(ToolsRoot))
            {
                foreach (var (dir, _) in Directory.EnumerateDirectories(ToolsRoot, "BetterDofus-*")
                    .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d)["BetterDofus-".Length..], out var v) ? v : null))
                    .Where(x => x.Version is not null)
                    .OrderByDescending(x => x.Version))
                {
                    string loaderName = File.Exists(Path.Combine(dir, "BetterDofusLoader.exe"))
                        ? "BetterDofusLoader.exe"
                        : File.Exists(Path.Combine(dir, "BetterDofusAutoLoader.exe"))
                            ? "BetterDofusAutoLoader.exe" : "";
                    if (!string.IsNullOrEmpty(loaderName) && File.Exists(Path.Combine(dir, BetterDofusDll)))
                        list.Add(new Provider("Better Dofus", dir, loaderName, BetterDofusDll,
                            Path.Combine(ToolsRoot, "BetterDofus-settings.ini"), false));
                }
            }
        }
        catch (IOException) { /* dossier illisible : pas de Better Dofus */ }
        catch (UnauthorizedAccessException) { }
        if (File.Exists(Path.Combine(Folder, "ModuleLoader.exe")) && File.Exists(Path.Combine(Folder, LegacyDll)))
            list.Add(new Provider("Dofus Enhanced r126", Folder, "ModuleLoader.exe", LegacyDll,
                Path.Combine(ToolsRoot, "r126", "DofusMiniatures-3.7.11.11-settings.ini"), true));
        return list;
    }

    internal static bool IsModuleLoaded(int pid)
        => NativeModules.IsLoaded(pid, LegacyDll) || NativeModules.IsLoaded(pid, BetterDofusDll);

    /// <summary>Enhanced a refusé de démarrer dans ce client (startup-pid.status) : sa DLL reste chargée sans aucun crochet actif.</summary>
    public static bool StartupFailed(int pid)
    {
        var folders = Providers().Select(p => p.Folder).Append(Folder).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            try
            {
                if (File.ReadAllText(Path.Combine(folder, $"startup-{pid}.status")).StartsWith("ERROR", StringComparison.Ordinal))
                    return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    // ---- Décision de compatibilité (pure, testée) -------------------------------------------------------------------

    /// <summary>Lit un fichier <c>clé=valeur</c> (release.ini, settings.ini) ; commentaires <c>#</c> et lignes vides ignorés.</summary>
    public static Dictionary<string, string> ParseIni(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    /// <summary>
    /// true si release.ini déclare une version ou une empreinte différente du client réel. Une valeur inconnue d'un côté
    /// ne prouve rien (pas de désaccord) : le journal du chargeur et le délai prennent alors le relais.
    /// </summary>
    public static bool ReleaseMismatch(IReadOnlyDictionary<string, string> release, string? liveVersion, string? liveGameHash, string? liveMetadataHash)
    {
        static bool Differs(string? a, string? b)
            => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        release.TryGetValue("clientVersion", out var version);
        release.TryGetValue("gameHash", out var game);
        release.TryGetValue("metadataHash", out var metadata);
        return Differs(version, liveVersion) || Differs(game, liveGameHash) || Differs(metadata, liveMetadataHash);
    }

    /// <summary>
    /// true si autoload.log dit, pour CE pid, que le chargeur automatique a laissé le jeu intact faute de version compatible.
    /// Le bloc d'un pid va de sa ligne <c>pid=N</c> jusqu'à la ligne horodatée suivante ; <c>LOAD_READY</c> l'emporte.
    /// <c>UPDATE_NO_COMPATIBLE_RELEASE</c> (contrôle de mise à jour) ne compte pas : seul <c>NO_COMPATIBLE_RELEASE:</c> compte.
    /// </summary>
    public static bool AutoloadLogSaysNoCompatible(string logText, int pid)
    {
        var lines = logText.Replace("\r\n", "\n").Split('\n');
        var pidToken = new Regex($@"\bpid={pid}\b");
        int start = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
            if (pidToken.IsMatch(lines[i])) { start = i; break; }
        if (start < 0 || lines[start].Contains("LOAD_READY", StringComparison.Ordinal)) return false;
        for (int i = start; i < lines.Length; i++)
        {
            if (i > start && TimestampLine.IsMatch(lines[i])) break;
            if (Regex.IsMatch(lines[i], @"(?<![A-Z_])NO_COMPATIBLE_RELEASE:")) return true;
        }
        return false;
    }

    private static readonly Regex TimestampLine = new(@"^\d{4}-\d{1,2}-\d{1,2} \d{1,2}:\d{1,2}:\d{1,2}\b", RegexOptions.Compiled);

    /// <summary>Verdict : chargé et sain = possède les crochets ; échec connu (désaccord, journal, démarrage refusé, délai) = repli.</summary>
    public static EnhancedVerdict Judge(bool releaseMismatch, bool autoloadSaysNoCompatible, bool moduleLoaded, bool startupFailed, bool boundElapsed)
    {
        if (moduleLoaded && !startupFailed) return EnhancedVerdict.Owns;
        if (startupFailed || releaseMismatch || autoloadSaysNoCompatible || boundElapsed) return EnhancedVerdict.Incompatible;
        return EnhancedVerdict.Pending;
    }

    // ---- Sondage du client réel ---------------------------------------------------------------------------------------

    private static string Sha256Hex(string path)
    {
        var info = new FileInfo(path);
        string key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (HashCache.TryGetValue(key, out var cached)) return cached;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        HashCache[key] = hash;
        return hash;
    }

    /// <summary>Empreintes GameAssembly.dll / global-metadata.dat du client de ce pid (null = illisible ou absent).</summary>
    private static (string? Game, string? Metadata) LiveClientHashes(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            string? exe = process.MainModule?.FileName;
            string? dir = exe is null ? null : Path.GetDirectoryName(exe);
            if (dir is null) return (null, null);
            string game = Path.Combine(dir, "GameAssembly.dll");
            string meta = Path.Combine(dir, "Dofus_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            return (File.Exists(game) ? Sha256Hex(game) : null, File.Exists(meta) ? Sha256Hex(meta) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }

    /// <summary>Premier fournisseur dont release.ini concorde avec le client de ce pid, sinon null.</summary>
    internal static Provider? Probe(int pid)
    {
        var (game, meta) = LiveClientHashes(pid);
        foreach (var provider in Providers())
        {
            try
            {
                var release = ParseIni(File.ReadAllText(provider.ReleaseIni));
                if (!ReleaseMismatch(release, null, game, meta)) return provider;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static string ReadAutoloadLogs()
    {
        var text = new System.Text.StringBuilder();
        foreach (string folder in Providers().Select(p => p.Folder).Append(Folder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new FileStream(Path.Combine(folder, "autoload.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 1_000_000) stream.Seek(-1_000_000, SeekOrigin.End);
                text.Append(new StreamReader(stream).ReadToEnd()).Append('\n');
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return text.ToString();
    }

    private static bool IsAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Après le lancement d'un client MITM alors qu'Enhanced est activé : écrit les réglages « client léger », fait charger
    /// Better Dofus par son chargeur (seul chemin d'activation, jamais dans un client déjà ouvert), puis attend que le module
    /// soit chargé. Incompatible = le module ne se chargera pas ; l'appelant injecte alors les mods Givre.
    /// </summary>
    internal static async Task<EnhancedVerdict> ResolveForLaunchAsync(int pid, bool lighten, TimeSpan? bound = null)
    {
        var provider = await Task.Run(() => Probe(pid)).ConfigureAwait(false);
        LastProbeIncompatible = provider is null;
        if (provider is null) { MarkIncompatible(pid); return EnhancedVerdict.Incompatible; }
        if (lighten)
        {
            try { WriteBotSettings(provider); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TransportLog.Line($"[mod] pid {pid} : réglages {provider.Name} non écrits : {ex.Message}");
            }
        }
        var limit = bound ?? TimeSpan.FromSeconds(75);
        var clock = Stopwatch.StartNew();
        bool attempted = false;
        TimeSpan? engineSeen = null;
        while (true)
        {
            if (!IsAlive(pid)) return EnhancedVerdict.Pending;
            string autoLog = File.Exists(provider.AutoloadLog) ? File.ReadAllText(provider.AutoloadLog) : "";
            var verdict = Judge(false, AutoloadLogSaysNoCompatible(autoLog, pid),
                IsModuleLoaded(pid), StartupFailed(pid), clock.Elapsed > limit);
            if (verdict == EnhancedVerdict.Incompatible) MarkIncompatible(pid);
            if (verdict != EnhancedVerdict.Pending) return verdict;
            if (!provider.Legacy && !attempted && NativeModules.IsLoaded(pid, "GameAssembly.dll"))
            {
                engineSeen ??= clock.Elapsed;
                if (clock.Elapsed - engineSeen.Value >= EngineGrace)
                {
                    attempted = true;
                    try { await ActivateAsync(pid).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        TransportLog.Line($"[mod] pid {pid} : activation {provider.Name} refusée : {ex.Message}");
                        MarkIncompatible(pid);
                        return EnhancedVerdict.Incompatible;
                    }
                }
            }
            await Task.Delay(1000).ConfigureAwait(false);
        }
    }

    // ---- Réglages « client léger » ------------------------------------------------------------------------------------

    /// <summary>
    /// Réglages imposés aux clients de bot. Allège : sans effets de sort, lancers raccourcis, sans animations d'attaque/mort,
    /// transitions rapides sans préchargement, miniatures en combat, aucun overlay FPS, aucune mise à jour GitHub.
    /// Coupe tout ce qui agit sur le jeu : trajets du mod, passage Roublard, équipement auto de pierre d'âme, synchro Metamob.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BotSettings(bool legacy)
    {
        var s = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["noSpellEffects"] = "1", ["shortSpellCasts"] = "1", ["noAttackAnimations"] = "1", ["noDeathAnimations"] = "1",
            ["fastMapTransitions"] = "1", ["preloadNextMap"] = "0",
            ["preferredMode"] = "2",
            ["miniatureSelf"] = "1", ["miniatureAllies"] = "1", ["miniatureAllySummons"] = "1",
            ["miniatureOwnSummons"] = "1", ["miniatureEnemies"] = "1", ["miniatureEnemySummons"] = "1",
            ["perfOverlay"] = "0", ["perfOverlayShown"] = "0", ["githubUpdates"] = "0",
            ["mapTravelPlanner"] = "0", ["chatTravelLinks"] = "0", ["questTravel"] = "0", ["spouseTeleport"] = "0",
            ["travelRoublardTemple"] = "0", ["soulAutoEquip"] = "0", ["metamobSync"] = "0",
        };
        if (legacy) s["mode"] = "2";
        return s;
    }

    /// <summary>Remplace/ajoute les clés données en gardant l'ordre, les commentaires et les autres clés.</summary>
    public static string MergeIni(string existing, IReadOnlyDictionary<string, string> values)
    {
        string newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = existing.Length == 0
            ? new List<string>()
            : existing.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        var pending = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            if (pending.Remove(key, out var value)) lines[i] = key + "=" + value;
        }
        foreach (var (key, value) in pending) lines.Add(key + "=" + value);
        return string.Join(newline, lines) + newline;
    }

    /// <summary>
    /// Écrit les réglages du fournisseur. Un fichier Better Dofus encore absent est amorcé depuis le fichier Enhanced r126
    /// (le mod fait la même migration) pour ne pas perdre les préférences. Les clients déjà ouverts ne relisent pas ce fichier.
    /// </summary>
    internal static void WriteBotSettings(Provider provider)
    {
        string existing = "";
        if (File.Exists(provider.SettingsFile)) existing = File.ReadAllText(provider.SettingsFile);
        else if (!provider.Legacy)
        {
            string old = Path.Combine(ToolsRoot, "r126", "DofusMiniatures-3.7.11.11-settings.ini");
            if (File.Exists(old)) existing = File.ReadAllText(old);
        }
        string merged = MergeIni(existing, BotSettings(provider.Legacy));
        if (merged == existing) return;
        Directory.CreateDirectory(Path.GetDirectoryName(provider.SettingsFile)!);
        string temp = provider.SettingsFile + ".givre.tmp";
        File.WriteAllText(temp, merged);
        File.Move(temp, provider.SettingsFile, overwrite: true);
    }

    // ---- Activation (bouton Options ou lancement) ---------------------------------------------------------------------

    public static async Task<string> ActivateAsync(int pid)
    {
        lock (NativeTurboRuntime.StateGate)
            if (!Activations.TryAdd(pid, 0))
                throw new InvalidOperationException("Activation Enhanced deja en cours pour ce client.");
        IncompatiblePids.TryRemove(pid, out _);
        long start = 0;
        bool failed = true;
        bool handoverStarted = false;
        try
        {
            if (!MZP25NativeModOwnership.MayControl(pid))
                throw new InvalidOperationException("Selectionne un client MITM appartenant a ce bot.");
            using var client = Process.GetProcessById(pid);
            _ = client.Handle;
            if (!string.Equals(client.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Client Dofus attendu.");
            start = client.StartTime.ToUniversalTime().Ticks;
            var provider = await ValidateActivationAsync(pid).ConfigureAwait(false);
            handoverStarted = true;
            await NativeTurboRuntime.PrepareForEnhancedAsync(pid).ConfigureAwait(false);
            if (client.HasExited) throw new OperationCanceledException("Le client a ete ferme avant le chargement.");
            string result = await LoadEnhancedAsync(pid, provider).ConfigureAwait(false);
            failed = StartupFailed(pid);
            return result;
        }
        finally
        {
            CompleteActivation(pid, start, failed && handoverStarted);
        }
    }

    private static void CompleteActivation(int pid, long start, bool recover)
    {
        long generation;
        lock (NativeTurboRuntime.StateGate)
        {
            Activations.TryRemove(pid, out _);
            generation = NativeTurboRuntime.CurrentGeneration(pid);
        }
        // Release the reservation before recomputing, with no runtime lock held.
        // A later request or activation wins over this single recovery attempt.
        if (recover && start > 0)
        {
            try { Stores.Launch.RecoverNativeAfterEnhanced((uint)pid, start, generation); }
            catch (Exception ex) { GivreLauncher.Core.Transport.TransportLog.Line($"[turbo-native] pid {pid} : reprise apres Enhanced impossible : {ex.Message}"); }
        }
    }

    private static readonly (string Name, string Sha256)[] LegacyPins =
    {
        ("ModuleLoader.exe", "40B1BD9D590CDCC9C67DE0AC117D0C349B1F9E1F3771282B25FAEB6C68B285E3"),
        ("DofusMiniatures.dll", "21E7E4B7D81270C9AEE222A50A6E93A6A94B7B5A73463F7B8FA7B74A47E21212"),
    };

    private static async Task<Provider> ValidateActivationAsync(int pid)
    {
        if (!MZP25NativeModOwnership.MayControl(pid))
            throw new InvalidOperationException("Selectionne un client MITM appartenant a ce bot.");
        using var client = Process.GetProcessById(pid);
        _ = client.Handle;
        if (Stores.Launch.IsInFight((uint)pid))
            throw new InvalidOperationException("Termine le combat avant de changer de fournisseur de mods.");
        var provider = await Task.Run(() => Probe(pid)).ConfigureAwait(false)
            ?? throw new InvalidDataException("Aucun fournisseur Enhanced installe n'est compatible avec ce client.");
        foreach (var (name, expected) in ExpectedHashes(provider))
        {
            string path = Path.Combine(provider.Folder, name);
            if (!File.Exists(path)) throw new InvalidDataException("Fichier Enhanced manquant : " + name);
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Empreinte Enhanced incorrecte : " + name);
        }
        return provider;
    }

    /// <summary>r126 : empreintes épinglées dans le code. Better Dofus : toutes les empreintes .dll/.exe de son release.ini.</summary>
    private static IEnumerable<(string Name, string Sha256)> ExpectedHashes(Provider provider)
    {
        if (provider.Legacy) return LegacyPins;
        var release = ParseIni(File.ReadAllText(provider.ReleaseIni));
        var files = release
            .Where(kv => kv.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        if (!files.Any(f => f.Key.Equals(provider.DllName, StringComparison.OrdinalIgnoreCase))
            || !files.Any(f => f.Key.Equals(provider.LoaderName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("release.ini sans empreinte du chargeur ou de la DLL : " + provider.Name);
        return files;
    }

    private static async Task<string> LoadEnhancedAsync(int pid, Provider provider)
    {
        if (IsModuleLoaded(pid))
            return "Enhanced est deja charge. Options du jeu > Dofus Enhanced.";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(provider.Loader)
            {
                WorkingDirectory = provider.Folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add(provider.Dll);
        if (!process.Start()) throw new IOException("Le chargeur Enhanced n'a pas demarre.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0 || !NativeModules.IsLoaded(pid, provider.DllName))
            throw new InvalidOperationException("Activation Enhanced non confirmee : " + error + output);
        return $"{provider.Name} charge. Ouvre Options du jeu > Dofus Enhanced. " + output.Trim();
    }
}
