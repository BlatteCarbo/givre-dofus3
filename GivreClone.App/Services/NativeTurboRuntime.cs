using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Runtime visuel du Turbo MITM : fallback no-animation quand GivreMods manque et speedhack Unity combat-only.
///
/// Le runtime upstream est injecté explicitement dans UN Dofus.exe, sans remplacer version.dll et sans filtrer de paquet.
/// Chaque process reçoit son propre staging sous LocalAppData et son propre NativeMods/control/&lt;PID&gt; : un compte Turbo
/// n'active donc jamais le no-animation des autres comptes et ne touche pas une installation native-host déjà présente.
/// </summary>
internal static class NativeTurboRuntime
{
    internal const string RuntimeVersion = "v0.4.0-host+skipper-1.0.8+speedhack-1.0.3";
    internal const string BootstrapModuleName = "GivreTurboBootstrap.dll";
    internal const string HostModuleName = "DofusNativeHost.dll";
    internal const string SkipperModuleName = "DofusCombatAnimationSkipper.dll";
    internal const string SkipperModId = "bubble.dofus3.combat-animation-skipper";
    internal const string SpeedHackModuleName = "DofusCombatSpeedHack.dll";
    internal const string SpeedHackModId = "mizan.dofus3.combat-speedhack";   // id déclaré par DofusCombatSpeedHack.dll (SHA épinglé) ; "givre.*" n'était jamais dans STATUS -> rechargements en boucle
    internal const string OverlayModuleName = "GivreItemIdOverlay.dll";
    internal const string OverlayModId = "givre.dofus3.item-id-overlay";
    internal const string OverlaySha256 = "a38b0c7432bbd2b2974dd7489e823b2706e23043a02be9bdfb7b469e504f2fe9";
    internal const string SpeedHackLeaseFileName = "combat-speedhack.lease";
    internal const string SpeedHackRestoreRequestFileName = "combat-speedhack.restore-request";
    internal const string SpeedHackRestoreAckFileName = "combat-speedhack.restored";
    internal const string TacticalMapLeaseFileName = "tactical-map.lease";
    internal const string TacticalConstructionFileName = "tactical-map.construction";
    internal const string TacticalMapAckFileName = "tactical-map.restored";

    internal const string BootstrapSha256 = "9b9c5f96dbf762ea7fb9d5af395d4f22622781da0b2b53ffa25c20c6c50ea75e";
    internal const string HostSha256 = "22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b";
    internal const string SkipperSha256 = "7138d7dc21cd394fcf737b0db26bfad148341f147f5664f6290cec563666a79b";   // fork Givre 1.0.8, cache borné et retour hors combat
    internal const string TacticalModuleName = "DofusNativeTactical.dll";
    internal const string TacticalModId = "bubble.dofus3.native-tactical";
    // 2026-11-02: concrete reflection dispatch, existing AOT specializations, and nonblocking Task prefab completion.
    internal const string TacticalSha256 = "9e9a7983360e51b1992e0971ef58a7ca442e5ed38a0a725302bc2a72ba618532";
    internal const string SpeedHackSha256 = "895a195a6551efb4a1085e500b745676ce8bf44a0967426de3e14abd5d1a0a24";

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();
    private readonly record struct DesiredState(
        bool Enabled,
        bool CombatSpeedHack,
        bool ItemIds,
        bool Tactical,
        int VisualMask,
        int CombatAppearanceMode,
        int RoleplayAppearanceMode,
        string CombatMiniatures,
        string RoleplayMiniatures,
        long Generation,
        long ProcessStartTicks,
        bool ForgetAfterApply)
    {
        public bool WantsVisuals => VisualMask != 0 || CombatAppearanceMode != 0 || RoleplayAppearanceMode != 0;
        // Provenance of this generation, not a retained request: only handover
        // teardown or a suppressed profile push may be recomputed on failure.
        public bool EnhancedRecoveryAllowed { get; init; }
    }
    private static readonly ConcurrentDictionary<int, DesiredState> Desired = new();
    // Publication order: Launch combat -> deferred native -> StateGate -> TacticalMapGate.
    // No locking callback into Launch; NativeVisualOptions reads its concurrent snapshot.
    internal static readonly object StateGate = new();
    internal static long CurrentGeneration(int pid) => Desired.TryGetValue(pid, out var state) ? state.Generation : -1;
    internal static bool CanRecoverEnhanced(int pid, long start, long generation) =>
        start > 0 && generation != -1 && CurrentGeneration(pid) == generation
        && Desired.TryGetValue(pid, out var state) && state.EnhancedRecoveryAllowed && state.ProcessStartTicks == start
        && ProcessStartTicks(pid) == start && MZP25NativeModOwnership.MayControl(pid)
        && !EnhancedOwnsHooks(pid);
    // Checking IsCurrent and then removing by PID is not atomic: a newer profile may arrive between them.
    private static bool RemoveDesired(int pid, DesiredState expected) =>
        Desired.TryRemove(new System.Collections.Generic.KeyValuePair<int, DesiredState>(pid, expected));
    private static long _requestSeq;
    private static long _stateGeneration;
    private static readonly string RestoreSessionNonce = Guid.NewGuid().ToString("N");
    private const int LeaseHeartbeatMs = 750;
    private static readonly System.Collections.Generic.Dictionary<int, long> CombatLeaseOwners = new();

    internal static void SetCombatActive(int pid, bool active)
    {
        if (pid <= 0) return;
        lock (TacticalMapGate)
        {
            if (active && MZP25NativeModOwnership.MayControl(pid) && ProcessStartTicks(pid) is > 0 and var start)
            {
                CombatLeaseOwners[pid] = start;
                TransportLog.Line($"[turbo-native] pid {pid} : combat owner accepted utc={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} monotonic={Environment.TickCount64} desired={Desired.ContainsKey(pid)}");
                if (Desired.TryGetValue(pid, out var desired) && IsCurrent(pid, desired))
                    RefreshLease(pid, desired);
            }
            else
            {
                CombatLeaseOwners.Remove(pid);
                if (Desired.TryGetValue(pid, out var desired)) WriteRestoreRequestBestEffort(pid, desired);
                DeleteLease(pid);
            }
        }
    }
    private readonly record struct TacticalMap(long ProcessStartTicks, long MapId, long Epoch);
    private static readonly object TacticalMapGate = new();
    private static readonly System.Collections.Generic.Dictionary<int, TacticalMap> TacticalMaps = new();
    private static readonly System.Collections.Generic.Dictionary<int, TacticalConstructionAdmission> TacticalTransitions = new();
    private static long _tacticalMapEpoch;
    private static readonly long TacticalOwnerStart = CurrentOwnerStart();

    private static long CurrentOwnerStart()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime().ToFileTimeUtc();
    }

    // The lease is separate from speedhack: a combat preference says nothing about
    // map readiness. All publication/revocation shares this lock with the heartbeat.
    internal static void BeginTacticalMapConstruction(int pid, long mapId, bool beforeForward)
    {
        lock (TacticalMapGate)
        {
            RevokeTacticalMapReady(pid);
            if (pid <= 0 || mapId is <= 0 or > int.MaxValue
                || !MZP25NativeModOwnership.MayControl(pid)) return;
            long start = ProcessStartTicks(pid);
            if (start == 0) return;
            long generation = beforeForward && Desired.TryGetValue(pid, out var desired)
                && desired.Tactical && IsCurrent(pid, desired) ? desired.Generation : 0;
            TacticalTransitions[pid] = new TacticalConstructionAdmission(
                new DateTime(start, DateTimeKind.Utc).ToFileTimeUtc(), ++_tacticalMapEpoch,
                mapId, generation, Environment.TickCount64);
            if (generation != 0 && Desired.TryGetValue(pid, out var current))
                RefreshTacticalConstructionLocked(pid, current);
        }
    }

    internal static void PublishTacticalMapReady(int pid, long mapId)
    {
        if (pid <= 0 || mapId is <= 0 or > int.MaxValue || !MZP25NativeModOwnership.MayControl(pid)) return;
        long start = ProcessStartTicks(pid);
        if (start == 0) return;
        lock (TacticalMapGate)
        {
            if (!TacticalTransitions.TryGetValue(pid, out var transition)
                || !transition.Complete(new DateTime(start, DateTimeKind.Utc).ToFileTimeUtc(), mapId)) return;
            TacticalMaps[pid] = new TacticalMap(start, mapId, transition.Epoch);
            DeleteTacticalConstruction(pid);
            if (Desired.TryGetValue(pid, out var desired) && IsCurrent(pid, desired))
            {
                RefreshTacticalLeaseLocked(pid, desired);
                // A disable requested off-map remains loaded but inert until cleanup
                // becomes legal again. Resume that reconciliation on the next map.
                if (!desired.Tactical) _ = Task.Run(() => ApplyDesiredAsync(pid, desired));
            }
        }
    }

    internal static void RevokeTacticalMapReady(int pid)
    {
        lock (TacticalMapGate)
        {
            TacticalTransitions.Remove(pid);
            DeleteTacticalConstruction(pid);
            if (TacticalMaps.Remove(pid) || Desired.ContainsKey(pid))
                DeleteTacticalLease(pid);
        }
    }

    internal static void RequestTacticalMapInformation(int pid)
    {
        lock (TacticalMapGate)
        {
            // MapCurrent -> MapInformation -> MapComplementary is a single
            // transition. A request on an already-ready scene starts a reload,
            // which must wait for a fresh authoritative MapCurrent epoch.
            if (TacticalMaps.ContainsKey(pid)) RevokeTacticalMapReady(pid);
        }
    }

    private static void DeleteTacticalConstruction(int pid)
    {
        try { File.Delete(Path.Combine(StageRoot(pid), TacticalConstructionFileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RefreshTacticalConstructionLocked(int pid, DesiredState desired)
    {
        if (!IsCurrent(pid, desired)) return;
        if (!TacticalTransitions.TryGetValue(pid, out var transition)) return;
        if (!desired.Tactical || !MZP25NativeModOwnership.MayControl(pid)
            || transition.TargetStart != new DateTime(desired.ProcessStartTicks, DateTimeKind.Utc).ToFileTimeUtc())
            transition.Revoke();
        string? record = transition.Record(desired.Generation, Environment.ProcessId,
            TacticalOwnerStart, Environment.TickCount64);
        if (record is null) { DeleteTacticalConstruction(pid); return; }
        try
        {
            string root = StageRoot(pid);
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, TacticalConstructionFileName);
            File.WriteAllText(path + ".tmp", record, new UTF8Encoding(false));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException) { transition.Revoke(); DeleteTacticalConstruction(pid); }
        catch (UnauthorizedAccessException) { transition.Revoke(); DeleteTacticalConstruction(pid); }
    }

    private static void DeleteTacticalLease(int pid)
    {
        try { File.Delete(Path.Combine(StageRoot(pid), TacticalMapLeaseFileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static string FormatTacticalLeaseToken(long targetStart, int ownerPid, long ownerStart,
        long epoch, long mapId, long generation, bool enabled) =>
        FormattableString.Invariant($"1 {targetStart} {ownerPid} {ownerStart} {epoch} {mapId} {generation} {(enabled ? 1 : 0)}");

    private static string TacticalToken(DesiredState desired, TacticalMap map) =>
        FormatTacticalLeaseToken(new DateTime(desired.ProcessStartTicks, DateTimeKind.Utc).ToFileTimeUtc(),
            Environment.ProcessId, TacticalOwnerStart, map.Epoch, map.MapId, desired.Generation, desired.Tactical);

    private static void RefreshTacticalLeaseLocked(int pid, DesiredState desired)
    {
        if (!IsCurrent(pid, desired) || !MZP25NativeModOwnership.MayControl(pid)
            || !TacticalMaps.TryGetValue(pid, out var map) || map.ProcessStartTicks != desired.ProcessStartTicks)
        {
            DeleteTacticalLease(pid);
            return;
        }
        string root = StageRoot(pid);
        if (!desired.Tactical && !Directory.Exists(root)) return;
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, TacticalMapLeaseFileName);
            string temporary = path + ".tmp";
            string record = TacticalToken(desired, map) + " " + (Environment.TickCount64 + 2250).ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(temporary, record, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { /* Native expiry closes the gate without a log per heartbeat. */ }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task TacticalLeaseHeartbeatAsync(int pid, DesiredState desired)
    {
        while (IsCurrent(pid, desired))
        {
            lock (TacticalMapGate)
            {
                // Recheck under the same lock as publication: a superseded heartbeat
                // must neither recreate nor delete its successor's lease.
                if (!IsCurrent(pid, desired)) return;
                RefreshTacticalConstructionLocked(pid, desired);
                RefreshTacticalLeaseLocked(pid, desired);
            }
            await Task.Delay(LeaseHeartbeatMs).ConfigureAwait(false);
        }
    }

    private static bool TacticalCleanupAcknowledged(int pid, DesiredState desired)
    {
        lock (TacticalMapGate)
        {
            if (desired.Tactical || !IsCurrent(pid, desired)
                || !TacticalMaps.TryGetValue(pid, out var map) || map.ProcessStartTicks != desired.ProcessStartTicks)
                return false;
            try
            {
                return File.ReadAllText(Path.Combine(StageRoot(pid), TacticalMapAckFileName)).Trim() == TacticalToken(desired, map);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Le runtime autonome est un repli uniquement : s'il y a un GivreMods historique, on lui laisse la main.</summary>
    internal static bool LegacyModsAvailable()
        => (!AppConfig.Current.DofusEnhancedEnabled || DofusEnhanced.LastProbeIncompatible) && CombatVisualModPolicy.CanUseHistoricalMods(MZP19LocalClone.Enabled,
            File.Exists(Path.Combine(AppContext.BaseDirectory, "GivreMods.dll")));

    internal static bool EnhancedOwnsHooks(int pid) =>
        DofusEnhanced.ActivationInProgress(pid)
        || (DofusEnhanced.IsModuleLoaded(pid) && !DofusEnhanced.StartupFailed(pid) && !DofusEnhanced.IsMarkedIncompatible(pid));

    internal static async Task PrepareForEnhancedAsync(int pid)
    {
        long start = ProcessStartTicks(pid);
        if (!MZP25NativeModOwnership.MayControl(pid) || start <= 0)
            throw new InvalidOperationException("Client graphique non possede par cette instance.");
        AppConfig.Current.DofusEnhancedEnabled = true;
        AppConfig.Save();
        var desired = PublishDesired(pid, false, false, ownedProcessStartTicks: start, scheduleApply: false, enhancedRecoveryAllowed: true);
        await ApplyDesiredAsync(pid, desired).ConfigureAwait(false);
        if (ProcessStartTicks(pid) != start)
            throw new OperationCanceledException("Client remplace pendant le changement de fournisseur.");
        foreach (string name in new[] { "GivreMods.dll", CombatVisualOptions.ModuleName, SkipperModuleName, SpeedHackModuleName, TacticalModuleName })
            if (NativeModules.IsLoaded(pid, name))
                throw new InvalidOperationException("Module natif encore charge : " + name + ". Relancer ce client avant Enhanced.");
    }

    private static long ProcessStartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!string.Equals(process.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase)) return 0;
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch { return 0; }
    }

    private static bool IsCurrent(int pid, DesiredState expected)
    {
        if (expected.ProcessStartTicks == 0) return false;
        if (!Desired.TryGetValue(pid, out var current) || current.Generation != expected.Generation) return false;
        return current.ProcessStartTicks == expected.ProcessStartTicks
            && ProcessStartTicks(pid) == expected.ProcessStartTicks;
    }

    private static void EnsureCurrent(int pid, DesiredState expected)
    {
        if (!IsCurrent(pid, expected))
            throw new OperationCanceledException("demande native obsolète ou identité Dofus changée");
    }

    /// <summary>Attend la fenêtre principale du client (max 60 s) avant d'injecter le bootstrap : il installe son crochet de messages sur le fil Unity
    /// 8 ms après l'injection ; sans fenêtre, SetWindowsHookExW échoue (erreur 126), le DLL reste chargé et plus rien n'est retenté -> aucun mod.</summary>
    private static async Task WaitForMainWindowAsync(int pid, DesiredState desired)
    {
        for (int i = 0; i < 600; i++)
        {
            EnsureCurrent(pid, desired);
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) { await Task.Delay(1000).ConfigureAwait(false); return; }   // 1 s de plus : Unity finit de créer son fil de messages
            }
            catch (ArgumentException) { throw new OperationCanceledException("client Dofus terminé avant injection native"); }
            await Task.Delay(100).ConfigureAwait(false);
        }
        TransportLog.Line($"[turbo-native] pid {pid} : fenêtre Dofus absente après 60 s ; injection tentée quand même");
    }

    /// <summary>DLL du runtime Visual C++ x64 absentes de System32 (liste séparée par des virgules), vide si tout est là.</summary>
    internal static string MissingVcRuntime()
    {
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return string.Join(", ", new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" }
            .Where(f => !System.IO.File.Exists(System.IO.Path.Combine(sys, f))));
    }

    private static void InjectBootstrapOwned(int pid, DesiredState desired, string bootstrapTarget)
    {
        EnsureCurrent(pid, desired);
        using var process = Process.GetProcessById(pid);
        // Force l'ouverture d'un handle kernel et le garde vivant pendant toute l'injection. Tant que ce handle
        // existe, l'objet process possédé ne peut pas être détruit/recyclé sous le même PID entre le contrôle
        // d'identité et DllInjector.Inject(pid, ...).
        _ = process.Handle;
        if (!string.Equals(process.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase)
            || process.StartTime.ToUniversalTime().Ticks != desired.ProcessStartTicks)
            throw new OperationCanceledException("identité Dofus changée avant injection native");

        // L'hôte natif (DofusNativeHost.dll) dépend du runtime Visual C++ (MSVCP140 / VCRUNTIME140 / VCRUNTIME140_1). Absent du PC,
        // son chargement dans Dofus échoue en silence (erreur 126 dans native-bootstrap.log) : plus de Turbo natif, animations réelles.
        if (MissingVcRuntime() is { Length: > 0 } missing)
        {
            TransportLog.Line($"[turbo-native] pid {pid} : runtime Visual C++ x64 absent ({missing}) -> Turbo natif impossible. Installer « Microsoft Visual C++ Redistributable 2015-2022 x64 ».");
            Toasts.Show("Turbo natif indisponible : installe le « Microsoft Visual C++ Redistributable 2015-2022 x64 » puis relance le client Dofus.", success: false);
        }
        if (!MZP25InjectionPermit.TryRegister(pid, bootstrapTarget))
        {
            TransportLog.Line($"[mod] pid {pid} : permis d'injection refusé pour le bootstrap natif ; injection annulée");
            throw new UnauthorizedAccessException("permis d'injection native refusé");
        }
        GivreCore.Injection.DllInjector.Inject(pid, bootstrapTarget);

        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != desired.ProcessStartTicks)
            throw new OperationCanceledException("Dofus possédé terminé pendant injection native");
    }

    private static DesiredState PublishDesired(
        int pid,
        bool enabled,
        bool combatSpeedHack,
        bool itemIds = false,
        bool tactical = false,
        bool forgetAfterApply = false,
        long ownedProcessStartTicks = 0,
        int visualMask = 0,
        int combatAppearanceMode = 0,
        int roleplayAppearanceMode = 0,
        string? combatMiniatures = null,
        string? roleplayMiniatures = null,
        bool scheduleApply = true,
        bool enhancedRecoveryAllowed = false)
    {
        lock (StateGate)
        {
            int combatMode = CombatVisualOptions.NormalizeMode(combatAppearanceMode);
            int roleplayMode = CombatVisualOptions.NormalizeMode(roleplayAppearanceMode);
            bool wantsVisuals = (visualMask & 63) != 0 || combatMode != 0 || roleplayMode != 0;
            long startTicks = ownedProcessStartTicks != 0
                ? ownedProcessStartTicks
                : ((enabled || itemIds || tactical || wantsVisuals) ? ProcessStartTicks(pid) : 0);
            var desired = new DesiredState(
                Enabled: enabled && startTicks != 0,
                CombatSpeedHack: enabled && combatSpeedHack && startTicks != 0,
                ItemIds: itemIds && startTicks != 0,
                Tactical: tactical && startTicks != 0,
                VisualMask: startTicks != 0 ? visualMask & 63 : 0,
                CombatAppearanceMode: startTicks != 0 ? combatMode : 0,
                RoleplayAppearanceMode: startTicks != 0 ? roleplayMode : 0,
                CombatMiniatures: string.IsNullOrEmpty(combatMiniatures) ? CombatVisualOptions.DefaultCombatMiniatures : combatMiniatures,
                RoleplayMiniatures: string.IsNullOrEmpty(roleplayMiniatures) ? CombatVisualOptions.DefaultRoleplayMiniatures : roleplayMiniatures,
                Generation: Interlocked.Increment(ref _stateGeneration),
                ProcessStartTicks: startTicks,
                ForgetAfterApply: forgetAfterApply) { EnhancedRecoveryAllowed = enhancedRecoveryAllowed };
            lock (TacticalMapGate)
            {
                Desired[pid] = desired;
                RefreshTacticalConstructionLocked(pid, desired);
                RefreshTacticalLeaseLocked(pid, desired);
            }
            if (startTicks != 0) _ = Task.Run(() => TacticalLeaseHeartbeatAsync(pid, desired));
            if (desired.CombatSpeedHack)
            {
                DeleteRestoreHandshake(pid);
                RefreshLease(pid, desired);
                _ = Task.Run(() => LeaseHeartbeatAsync(pid, desired));
            }
            else
            {
                WriteRestoreRequestBestEffort(pid, desired);
                DeleteLease(pid);
            }
            if (startTicks == 0 && forgetAfterApply)
            {
                RemoveDesired(pid, desired);
                return desired;
            }
            if (scheduleApply) _ = Task.Run(() => ApplyDesiredAsync(pid, desired));
            return desired;
        }
    }

    private static string LeasePath(int pid) => Path.Combine(LeaseRoot(pid), SpeedHackLeaseFileName);
    private static string RestoreRequestPath(int pid) => Path.Combine(LeaseRoot(pid), SpeedHackRestoreRequestFileName);
    private static string RestoreAckPath(int pid) => Path.Combine(LeaseRoot(pid), SpeedHackRestoreAckFileName);
    /// <summary>Dossier où le DLL speedhack ÉPINGLÉ (Mizan 1.0.3) lit/écrit bail, demande et accusé de restauration : codé en dur
    /// <c>%LOCALAPPDATA%\MizanBot\NativeTurbo\&lt;pid&gt;</c>. Avant, le bail était posé dans le dossier de données du clone : le mod ne le voyait
    /// jamais (« lease inactive ») et n'armait donc jamais le x8. Les DLL eux-mêmes restent dans <see cref="StageRoot"/>.</summary>
    private static string LeaseRoot(int pid)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanBot", "NativeTurbo", pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string RestoreToken(DesiredState desired)
        => $"{desired.ProcessStartTicks}:{desired.Generation}:{RestoreSessionNonce}";

    private static void DeleteLease(int pid)
    {
        try { File.Delete(LeasePath(pid)); } catch { /* expiration native = deuxième filet de sécurité */ }
        try { Directory.Delete(LeaseRoot(pid)); } catch { /* non vide ou absent : sans importance */ }
    }

    private static void DeleteRestoreHandshake(int pid)
    {
        try { File.Delete(RestoreRequestPath(pid)); } catch { /* strict check on unload */ }
        try { File.Delete(RestoreAckPath(pid)); } catch { /* strict check on unload */ }
    }

    private static void WriteRestoreRequestBestEffort(int pid, DesiredState desired)
    {
        if (desired.ProcessStartTicks == 0) return;
        try
        {
            Directory.CreateDirectory(LeaseRoot(pid));
            File.WriteAllText(RestoreRequestPath(pid), RestoreToken(desired), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            TransportLog.Line($"[turbo-native] pid {pid} : requête de restauration speedhack non écrite : {ex.Message}");
        }
    }

    private static void WriteRestoreRequestStrict(int pid, DesiredState desired)
    {
        Directory.CreateDirectory(LeaseRoot(pid));
        string token = RestoreToken(desired);
        File.WriteAllText(RestoreRequestPath(pid), token, new UTF8Encoding(false));
        string written = File.ReadAllText(RestoreRequestPath(pid)).Trim();
        if (!string.Equals(written, token, StringComparison.Ordinal))
            throw new IOException("token de restauration speedhack non vérifiable");
    }

    private static bool RestoreAckMatches(int pid, DesiredState desired)
    {
        try
        {
            if (!File.Exists(RestoreAckPath(pid))) return false;
            return string.Equals(
                File.ReadAllText(RestoreAckPath(pid)).Trim(),
                RestoreToken(desired),
                StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static void RefreshLease(int pid, DesiredState desired)
    {
        lock (TacticalMapGate)
        {
            if (!desired.CombatSpeedHack || !IsCurrent(pid, desired)) return;
            if (!CombatLeaseOwners.TryGetValue(pid, out long owner) || owner != desired.ProcessStartTicks)
            {
                WriteRestoreRequestBestEffort(pid, desired);
                DeleteLease(pid);
                return;
            }
            try
            {
                Directory.CreateDirectory(LeaseRoot(pid));
                string path = LeasePath(pid);
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, $"{desired.ProcessStartTicks}:{desired.Generation}", new UTF8Encoding(false));
                    TransportLog.Line($"[turbo-native] pid {pid} : combat lease published utc={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} monotonic={Environment.TickCount64} generation={desired.Generation}");
                }
                else
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

                // La publication et la fin de combat partagent ce verrou : aucun heartbeat
                // ne peut republier le bail après la révocation de son propriétaire de combat.
                if (!IsCurrent(pid, desired)
                    && (!Desired.TryGetValue(pid, out var current) || !current.CombatSpeedHack))
                    DeleteLease(pid);
            }
            catch (Exception ex)
            {
                TransportLog.Line($"[turbo-native] pid {pid} : lease speedhack non rafraîchi : {ex.Message}");
            }
        }
    }

    private static async Task LeaseHeartbeatAsync(int pid, DesiredState desired)
    {
        while (IsCurrent(pid, desired) && desired.CombatSpeedHack)
        {
            RefreshLease(pid, desired);
            await Task.Delay(LeaseHeartbeatMs).ConfigureAwait(false);
        }
        if (!Desired.TryGetValue(pid, out var current) || !current.CombatSpeedHack)
            DeleteLease(pid);
    }

    private static async Task<bool> WaitForLegacyModsAsync(int pid, DesiredState desired)
    {
        if (!LegacyModsAvailable()) return false;
        // Le launcher injecte GivreMods en best-effort juste après GivreKama. Laisser cette voie prioritaire quelques
        // secondes évite de poser deux skippers en parallèle ; si elle ne se matérialise jamais, le fallback autonome
        // prend le relais au lieu de rester désactivé pour la seule raison que le fichier existe sur disque.
        for (int i = 0; i < 24; i++)
        {
            EnsureCurrent(pid, desired);
            if (NativeModules.IsLoaded(pid, "GivreMods.dll")) return true;
            await Task.Delay(200).ConfigureAwait(false);
        }
        EnsureCurrent(pid, desired);
        return NativeModules.IsLoaded(pid, "GivreMods.dll");
    }

    internal readonly record struct NativeEngagement(bool Host, bool SpeedHack);

    internal static NativeEngagement EngagementOf(int pid)
        => pid > 0 && Desired.TryGetValue(pid, out var engaged) ? new(engaged.Enabled, engaged.CombatSpeedHack) : default;

    internal static void RequestState(int pid, bool enabled, bool combatSpeedHack = false, bool itemIds = false, bool tactical = false, bool profileVisuals = false, [CallerMemberName] string caller = "")
    {
        lock (StateGate)
        {
            if (pid <= 0) return;
            var visuals = profileVisuals || CombatVisualOptions.IsProfilePush(caller) ? Stores.Launch.NativeVisualOptions((uint)pid) : CombatVisualSelection.Teardown;
            bool recoveryAllowed = DofusEnhanced.ActivationInProgress(pid)
                && (profileVisuals || CombatVisualOptions.IsProfilePush(caller));
            if (EnhancedOwnsHooks(pid))
            {
                enabled = combatSpeedHack = itemIds = tactical = false;
                visuals = CombatVisualSelection.Teardown;
            }
            // Do not enqueue injection or per-process native files for someone
            // else's Dofus PID. Leave cleanup/revocation to the already enrolled
            // process generation, not a fresh lookup by an untrusted PID.
            if ((enabled || itemIds || tactical || visuals.WantsModule) && !MZP25NativeModOwnership.MayControl(pid)) { TransportLog.Line($"[turbo-native] pid {pid} : demande ignorée (client non possédé par ce clone)"); return; }
            if (!enabled && !itemIds && !tactical && !visuals.WantsModule && MZP19LocalClone.Enabled && !Desired.ContainsKey(pid))
                return; // no cleanup of a PID that this isolated runtime never enrolled
            long ownedStart = !enabled && !itemIds && !tactical && !visuals.WantsModule && Desired.TryGetValue(pid, out var current)
                ? current.ProcessStartTicks
                : 0;
            PublishDesired(pid, enabled, combatSpeedHack, itemIds, tactical, ownedProcessStartTicks: ownedStart,
                visualMask: visuals.Mask, combatAppearanceMode: visuals.CombatMode, roleplayAppearanceMode: visuals.RoleplayMode,
                combatMiniatures: visuals.CombatMiniatures, roleplayMiniatures: visuals.RoleplayMiniatures,
                enhancedRecoveryAllowed: recoveryAllowed);
        }
    }

    /// <summary>
    /// GivreMods devient propriétaire du no-animation. En Turbo MITM, le host autonome reste chargé uniquement
    /// pour le speedhack combat-only ; hors Turbo, il peut être entièrement retiré.
    /// </summary>
    internal static void LegacyModsTookOwnership(int pid)
    {
        lock (StateGate)
        {
            if (pid <= 0) return;
            if (!Desired.TryGetValue(pid, out var desired)) return;
            bool keepSpeedHack = desired.Enabled && desired.CombatSpeedHack;
            PublishDesired(pid, keepSpeedHack, keepSpeedHack, desired.ItemIds, desired.Tactical, ownedProcessStartTicks: desired.ProcessStartTicks,
                visualMask: desired.VisualMask, combatAppearanceMode: desired.CombatAppearanceMode, roleplayAppearanceMode: desired.RoleplayAppearanceMode,
                combatMiniatures: desired.CombatMiniatures, roleplayMiniatures: desired.RoleplayMiniatures);
        }
    }

    internal static void Forget(int pid)
    {
        lock (StateGate)
        {
            if (pid <= 0) return;
            RevokeTacticalMapReady(pid);
            if (!Desired.TryGetValue(pid, out var desired))
            {
                if (MZP19LocalClone.Enabled) return; // never touch another product's per-PID files
                DeleteLease(pid);
                return;
            }
            PublishDesired(
                pid,
                enabled: false,
                combatSpeedHack: false,
                forgetAfterApply: true,
                ownedProcessStartTicks: desired.ProcessStartTicks);
        }
    }

    private static async Task ApplyDesiredAsync(int pid, DesiredState desired)
    {
        var gate = Gates.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!IsCurrent(pid, desired)) return;
                try
                {
                    if (desired.Enabled || desired.ItemIds || desired.Tactical || desired.WantsVisuals) await EnableAsync(pid, desired).ConfigureAwait(false);
                    else await DisableAsync(pid, desired).ConfigureAwait(false);

                    if (desired.ForgetAfterApply && IsCurrent(pid, desired))
                        RemoveDesired(pid, desired);
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        TransportLog.Line($"[turbo-native] pid {pid} : reconciliation échec après 3 essais : {ex.GetType().Name}: {ex.Message}");
                        return;
                    }
                    await Task.Delay(150 * (attempt + 1)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string RuntimeSourceDir()
        => Path.Combine(AppContext.BaseDirectory, "NativeTurboRuntime");

    /// <summary>GIVRE_NATIVE_STAGE (chemin absolu) : pose les DLL natifs (bootstrap, host, mods) dans CE dossier réel plutôt que dans le dossier de données.
    /// Utile quand le dossier de données est redirigé (sandbox/MSIX) : le crochet du bootstrap (SetWindowsHookExW) échoue alors avec l'erreur 126.</summary>
    private static string StageRoot(int pid)
        => Environment.GetEnvironmentVariable("GIVRE_NATIVE_STAGE") is { Length: > 0 } over && Path.IsPathFullyQualified(over)
            ? Path.Combine(Path.GetFullPath(over), pid.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : StageRootDefault(pid);

    private static string StageRootDefault(int pid)
        => MZP25NativeModPaths.Turbo(pid, MZP19LocalClone.Enabled, MZP19LocalClone.DataRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static bool HashMatches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireSource(string path, string sha, string label)
    {
        if (!HashMatches(path, sha))
            throw new InvalidDataException($"{label} absent ou SHA-256 inattendu ({RuntimeVersion})");
    }

    private static void EnsureOwnedTargetExact(string source, string target, string sha)
    {
        if (HashMatches(target, sha)) return;
        File.Copy(source, target, overwrite: true);
        if (!HashMatches(target, sha))
            throw new IOException($"copie de {Path.GetFileName(target)} non vérifiable");
    }

    private static async Task EnableAsync(int pid, DesiredState desired)
    {
        EnsureCurrent(pid, desired);
        if (EnhancedOwnsHooks(pid))
        {
            await DisableAsync(pid, desired).ConfigureAwait(false);
            return;
        }
        bool combatSpeedHack = desired.CombatSpeedHack;
        // A fingerprint refusal is authoritative for this client process. Do not
        // keep suppressing the independent skipper in favor of a module that
        // cannot load; leave its ERROR status visible to callers.
        bool visualsRefused = CombatVisualOptions.CompatibilityRefused(VisualOptionsStatus(pid));
        bool wantVisuals = desired.WantsVisuals && !visualsRefused;
        bool wantSkipper = desired.Enabled && !wantVisuals;
        if (desired.WantsVisuals && visualsRefused)
            TransportLog.Line($"[turbo-native] pid {pid} : options visuelles incompatibles ; skipper indépendant demandé={wantSkipper}, aucune option avancée certifiée");
        if (wantVisuals && NativeModules.IsLoaded(pid, "GivreMods.dll"))
            throw new InvalidOperationException("Un ancien module visuel est charge ; relancer ce client avant de changer de fournisseur.");
        bool wantOverlay = desired.ItemIds;
        bool wantTactical = desired.Tactical;
        bool legacyModsLoaded = wantSkipper && await WaitForLegacyModsAsync(pid, desired).ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (legacyModsLoaded && !combatSpeedHack && !wantOverlay && !wantTactical && !wantVisuals)
        {
            // Une transition Turbo -> Fast/Normal doit retirer immédiatement le speedhack autonome et restaurer
            // son timeScale, même si GivreMods reste chargé pour le no-animation.
            await DisableAsync(pid, desired).ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : GivreMods déjà chargé -> fallback autonome non activé");
            return;
        }

        string sourceDir = RuntimeSourceDir();
        string bootstrapSource = Path.Combine(sourceDir, "DofusNativeBootstrap.dll");
        string hostSource = Path.Combine(sourceDir, HostModuleName);
        string skipperSource = Path.Combine(sourceDir, SkipperModuleName);
        string speedHackSource = Path.Combine(sourceDir, SpeedHackModuleName);
        RequireSource(bootstrapSource, BootstrapSha256, "bootstrap natif Turbo");
        RequireSource(hostSource, HostSha256, "host natif Turbo");
        string overlaySource = Path.Combine(sourceDir, OverlayModuleName);
        if (wantSkipper && !legacyModsLoaded) RequireSource(skipperSource, SkipperSha256, "skipper natif Turbo");
        if (wantOverlay) RequireSource(overlaySource, OverlaySha256, "mod ID objets");
        string visualsSource = Path.Combine(sourceDir, CombatVisualOptions.ModuleName);
        if (wantVisuals) RequireSource(visualsSource, CombatVisualOptions.Sha256, "options visuelles");
        string tacticalSource = Path.Combine(sourceDir, TacticalModuleName);
        if (wantTactical) RequireSource(tacticalSource, TacticalSha256, "mod tactique natif");
        if (combatSpeedHack) RequireSource(speedHackSource, SpeedHackSha256, "speedhack natif Turbo");

        // Le bootstrap standalone vérifie seulement: process == Dofus.exe + présence d'un dossier Dofus_Data à côté de lui.
        // On exploite ce contrat pour un staging Givre ISOLÉ par PID : aucun fichier posé dans l'installation Dofus, aucun
        // NativeMods partagé entre comptes, et aucun conflit avec un DofusNativeHost éventuellement installé par l'utilisateur.
        string root = StageRoot(pid);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Dofus_Data"));
        string bootstrapTarget = Path.Combine(root, BootstrapModuleName);
        string hostTarget = Path.Combine(root, HostModuleName);
        string skipperTarget = Path.Combine(root, SkipperModuleName);
        string speedHackTarget = Path.Combine(root, SpeedHackModuleName);
        string overlayTarget = Path.Combine(root, OverlayModuleName);
        string visualsTarget = Path.Combine(root, CombatVisualOptions.ModuleName);
        if (wantVisuals) EnsureOwnedTargetExact(visualsSource, visualsTarget, CombatVisualOptions.Sha256);
        WriteVisualOptions(root, wantVisuals
            ? new CombatVisualSelection(desired.VisualMask, desired.CombatAppearanceMode, desired.RoleplayAppearanceMode, desired.CombatMiniatures, desired.RoleplayMiniatures)
            : CombatVisualSelection.Teardown);
        string tacticalTarget = Path.Combine(root, TacticalModuleName);
        EnsureOwnedTargetExact(bootstrapSource, bootstrapTarget, BootstrapSha256);
        EnsureOwnedTargetExact(hostSource, hostTarget, HostSha256);
        if (wantSkipper && !legacyModsLoaded) EnsureOwnedTargetExact(skipperSource, skipperTarget, SkipperSha256);
        if (wantOverlay) EnsureOwnedTargetExact(overlaySource, overlayTarget, OverlaySha256);
        if (wantTactical) EnsureOwnedTargetExact(tacticalSource, tacticalTarget, TacticalSha256);
        if (combatSpeedHack) EnsureOwnedTargetExact(speedHackSource, speedHackTarget, SpeedHackSha256);

        if (!NativeModules.IsLoaded(pid, BootstrapModuleName))
        {
            await WaitForMainWindowAsync(pid, desired).ConfigureAwait(false);
            for (int attempt = 0; attempt < 6 && !NativeModules.IsLoaded(pid, BootstrapModuleName); attempt++)
            {
                EnsureCurrent(pid, desired);
                try { InjectBootstrapOwned(pid, desired, bootstrapTarget); }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException) { throw; } // un refus de permis n'est jamais un problème de timing Unity
                catch { /* process/Unity pas encore prêt */ }
                for (int i = 0; i < 4 && !NativeModules.IsLoaded(pid, BootstrapModuleName); i++)
                {
                    EnsureCurrent(pid, desired);
                    await Task.Delay(150).ConfigureAwait(false);
                }
            }
        }
        EnsureCurrent(pid, desired);
        if (!NativeModules.IsLoaded(pid, BootstrapModuleName))
            throw new InvalidOperationException("bootstrap natif Turbo non confirmé dans Dofus.exe");

        string control = Path.Combine(root, "NativeMods", "control", pid.ToString(CultureInfo.InvariantCulture));
        string inbox = Path.Combine(control, "inbox");
        for (int i = 0; i < 60 && !Directory.Exists(inbox); i++)
        {
            EnsureCurrent(pid, desired);
            await Task.Delay(100).ConfigureAwait(false);
        }
        EnsureCurrent(pid, desired);
        if (!Directory.Exists(inbox))
            throw new TimeoutException("canal de contrôle du host natif Turbo indisponible");

        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        legacyModsLoaded = wantSkipper && NativeModules.IsLoaded(pid, "GivreMods.dll");
        if (!wantVisuals && status.Contains(CombatVisualOptions.ModId, StringComparison.Ordinal))
            await EnsureUnloadedAsync(pid, desired, control, CombatVisualOptions.ModId, "options visuelles").ConfigureAwait(false);
        if (!wantSkipper)
        {
            if (status.Contains(SkipperModId, StringComparison.Ordinal))
                await EnsureUnloadedAsync(pid, desired, control, SkipperModId, "skipper").ConfigureAwait(false);
        }
        else if (legacyModsLoaded && status.Contains(SkipperModId, StringComparison.Ordinal))
        {
            await EnsureUnloadedAsync(pid, desired, control, SkipperModId, "skipper fallback").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : GivreMods reprend le no-animation, skipper fallback retiré");
        }
        else if (!legacyModsLoaded && !status.Contains(SkipperModId, StringComparison.Ordinal))
        {
            await EnsureLoadedAsync(pid, desired, control, skipperTarget, SkipperModId, "skipper").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : skipper Animator2D CHARGÉ ({RuntimeVersion}, STATUS confirmé ; effet en combat à vérifier)");
        }
        else if (legacyModsLoaded)
            TransportLog.Line($"[turbo-native] pid {pid} : GivreMods garde la propriété du no-animation");

        status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (combatSpeedHack && !status.Contains(SpeedHackModId, StringComparison.Ordinal))
        {
            await EnsureLoadedAsync(pid, desired, control, speedHackTarget, SpeedHackModId, "speedhack").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : speedhack combat-only x8 ACTIF ({RuntimeVersion})");
        }
        else if (!combatSpeedHack && status.Contains(SpeedHackModId, StringComparison.Ordinal))
        {
            await EnsureUnloadedAsync(pid, desired, control, SpeedHackModId, "speedhack").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : speedhack combat-only désactivé");
        }
        try
        {
            await EnsureExtraModAsync(pid, desired, control, visualsTarget, CombatVisualOptions.ModId, "options visuelles experimentales", wantVisuals).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (wantVisuals
            && CombatVisualOptions.CompatibilityRefused(VisualOptionsStatus(pid)))
        {
            // The first LOAD can publish the refusal after provider selection.
            // Re-enter under the same PID gate: the exact status disables visuals
            // before the existing ownership checks admit an independent skipper.
            EnsureCurrent(pid, desired);
            await EnableAsync(pid, desired).ConfigureAwait(false);
            return;
        }
        await EnsureOverlayAsync(pid, desired, control, overlayTarget, wantOverlay).ConfigureAwait(false);
        await EnsureExtraModAsync(pid, desired, control, tacticalTarget, TacticalModId, "mod tactique natif (F8 en jeu)", wantTactical).ConfigureAwait(false);
    }

    private static readonly ConcurrentDictionary<int, bool> InactivityPublished = new();

    /// <summary>Écrit <c>inactivity.ini</c> à côté de GivreItemIdOverlay.dll. Absent du fichier, le mod reste actif.
    /// Faux retire le détour. N'écrit rien tant que le dossier de staging de ce pid n'existe pas.</summary>
    internal static void PublishInactivityHook(int pid, bool enabled)
    {
        if (pid <= 0 || !MZP25NativeModOwnership.MayControl(pid)) return;
        try
        {
            string root = StageRoot(pid);
            if (!Directory.Exists(root))
            {
                InactivityPublished.TryRemove(pid, out _);
                return;
            }
            string path = Path.Combine(root, "inactivity.ini");
            if (InactivityPublished.TryGetValue(pid, out bool previous) && previous == enabled && File.Exists(path)) return;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, enabled ? "inactivity=true\n" : "inactivity=false\n", new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            InactivityPublished[pid] = enabled;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Vrai seulement quand le mod a confirmé le détour dans inactivity.status. Un fichier périmé sans DLL chargée ne compte pas : l'appelant vérifie aussi le module.</summary>
    internal static bool InactivityHookEngaged(int pid)
    {
        if (pid <= 0 || !MZP25NativeModOwnership.MayControl(pid)) return false;
        try
        {
            string text = File.ReadAllText(Path.Combine(StageRoot(pid), "inactivity.status")).Trim();
            return string.Equals(text, "hooked", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void WriteVisualOptions(string root, CombatVisualSelection selection)
    {
        string path = Path.Combine(root, "visual-options.ini");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, CombatVisualOptions.FormatIni(selection), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    internal static string VisualOptionsStatus(int pid)
    {
        if (pid <= 0 || !MZP25NativeModOwnership.MayControl(pid)) return "";
        try { return File.ReadAllText(Path.Combine(StageRoot(pid), "visual-options.status")).Trim(); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    private static async Task EnsureExtraModAsync(int pid, DesiredState desired, string control, string target, string modId, string label, bool want)
    {
        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (want && !status.Contains(modId, StringComparison.Ordinal))
        {
            await EnsureLoadedAsync(pid, desired, control, target, modId, label).ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : {label} CHARGÉ (STATUS confirmé ; effet visuel à vérifier)");
        }
        else if (!want && status.Contains(modId, StringComparison.Ordinal))
            await EnsureUnloadedAsync(pid, desired, control, modId, label).ConfigureAwait(false);
    }

    private static async Task EnsureOverlayAsync(int pid, DesiredState desired, string control, string overlayTarget, bool want)
    {
        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (want && !status.Contains(OverlayModId, StringComparison.Ordinal))
        {
            await EnsureLoadedAsync(pid, desired, control, overlayTarget, OverlayModId, "mod ID objets").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : mod ID objets ACTIF (l'ID s'affiche à droite du nom dans les bulles)");
        }
        else if (!want && status.Contains(OverlayModId, StringComparison.Ordinal))
            await EnsureUnloadedAsync(pid, desired, control, OverlayModId, "mod ID objets").ConfigureAwait(false);
    }

    private static async Task DisableAsync(int pid, DesiredState desired)
    {
        EnsureCurrent(pid, desired);
        string visualsRoot = StageRoot(pid);
        if (Directory.Exists(visualsRoot)) WriteVisualOptions(visualsRoot, CombatVisualSelection.Teardown);
        bool bootstrapLoaded = NativeModules.IsLoaded(pid, BootstrapModuleName);
        bool hostLoaded = NativeModules.IsLoaded(pid, HostModuleName);
        if (!bootstrapLoaded && !hostLoaded) return;
        string root = StageRoot(pid);
        string control = Path.Combine(root, "NativeMods", "control", pid.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(Path.Combine(control, "inbox")))
            throw new TimeoutException("host natif chargé mais canal de contrôle indisponible pendant le disable");
        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (status.Contains(SpeedHackModId, StringComparison.Ordinal))
        {
            await EnsureUnloadedAsync(pid, desired, control, SpeedHackModId, "speedhack").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : speedhack combat-only désactivé");
        }
        await EnsureExtraModAsync(pid, desired, control, Path.Combine(root, CombatVisualOptions.ModuleName), CombatVisualOptions.ModId, "options visuelles", want: false).ConfigureAwait(false);
        await EnsureOverlayAsync(pid, desired, control, Path.Combine(root, OverlayModuleName), want: false).ConfigureAwait(false);
        await EnsureExtraModAsync(pid, desired, control, Path.Combine(root, TacticalModuleName), TacticalModId, "mod tactique natif", want: false).ConfigureAwait(false);
        status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        EnsureCurrent(pid, desired);
        if (status.Contains(SkipperModId, StringComparison.Ordinal))
        {
            await EnsureUnloadedAsync(pid, desired, control, SkipperModId, "skipper").ConfigureAwait(false);
            TransportLog.Line($"[turbo-native] pid {pid} : skipper Animator2D désactivé");
        }
    }

    private static async Task EnsureLoadedAsync(
        int pid, DesiredState desired, string control, string target, string modId, string label)
    {
        EnsureCurrent(pid, desired);
        if (EnhancedOwnsHooks(pid))
            throw new OperationCanceledException("Enhanced possede les crochets natifs.");
        string load = await ControlAsync(control, "LOAD\t" + target).ConfigureAwait(false);
        if (!load.StartsWith("OK\t", StringComparison.Ordinal))
            throw new InvalidOperationException($"chargement du {label} refusé : {load}");
        EnsureCurrent(pid, desired);
        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        if (!status.Contains(modId, StringComparison.Ordinal))
            throw new InvalidOperationException($"chargement du {label} non confirmé par STATUS");
    }

    private static async Task EnsureUnloadedAsync(
        int pid, DesiredState desired, string control, string modId, string label)
    {
        EnsureCurrent(pid, desired);
        string status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        if (!status.Contains(modId, StringComparison.Ordinal))
        {
            if (string.Equals(modId, SpeedHackModId, StringComparison.Ordinal)) DeleteRestoreHandshake(pid);
            return;
        }

        if (string.Equals(modId, SpeedHackModId, StringComparison.Ordinal))
        {
            // Jamais d'UNLOAD tant que le module n'a pas lui-même confirmé la restauration de la baseline
            // pour CETTE génération/identité process. Un ACK ancien ne peut donc pas autoriser un unload.
            WriteRestoreRequestStrict(pid, desired);
            DeleteLease(pid);
            bool restored = false;
            for (int i = 0; i < 60; i++)
            {
                EnsureCurrent(pid, desired);
                if (RestoreAckMatches(pid, desired)) { restored = true; break; }
                await Task.Delay(50).ConfigureAwait(false);
            }
            if (!restored)
                throw new TimeoutException("baseline timeScale non confirmée; speedhack laissé chargé mais lease inactif");
        }

        if (string.Equals(modId, TacticalModId, StringComparison.Ordinal))
        {
            lock (TacticalMapGate)
            {
                if (!TacticalMaps.ContainsKey(pid))
                    throw new OperationCanceledException("nettoyage tactique reporte jusqu'a la prochaine carte");
            }
            bool restored = false;
            for (int i = 0; i < 60; i++)
            {
                EnsureCurrent(pid, desired);
                if (TacticalCleanupAcknowledged(pid, desired)) { restored = true; break; }
                await Task.Delay(50).ConfigureAwait(false);
            }
            if (!restored)
                throw new TimeoutException("nettoyage tactique non confirme; module conserve sans autorisation d'activation");
        }

        EnsureCurrent(pid, desired);
        string unload = await ControlAsync(control, "UNLOAD\t" + modId).ConfigureAwait(false);
        if (!unload.StartsWith("OK\t", StringComparison.Ordinal))
            throw new InvalidOperationException($"déchargement du {label} refusé : {unload}");
        EnsureCurrent(pid, desired);
        status = await ControlAsync(control, "STATUS").ConfigureAwait(false);
        if (status.Contains(modId, StringComparison.Ordinal))
            throw new InvalidOperationException($"déchargement du {label} non confirmé par STATUS");
        if (string.Equals(modId, SpeedHackModId, StringComparison.Ordinal)) DeleteRestoreHandshake(pid);
    }

    private static async Task<string> ControlAsync(string controlRoot, string command)
    {
        string id = "givre-" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture)
            + "-" + Interlocked.Increment(ref _requestSeq).ToString("x", CultureInfo.InvariantCulture);
        string inbox = Path.Combine(controlRoot, "inbox");
        string outbox = Path.Combine(controlRoot, "outbox");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(outbox);
        string request = Path.Combine(inbox, id + ".request");
        string response = Path.Combine(outbox, id + ".response");
        if (command.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("commande de contrôle invalide", nameof(command));
        File.WriteAllText(request, command, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        for (int i = 0; i < 80; i++)
        {
            if (File.Exists(response))
            {
                string text = File.ReadAllText(response);
                try { File.Delete(response); } catch { /* diagnostic seulement */ }
                return text;
            }
            await Task.Delay(50).ConfigureAwait(false);
        }
        throw new TimeoutException("réponse du host natif Turbo absente");
    }
}
