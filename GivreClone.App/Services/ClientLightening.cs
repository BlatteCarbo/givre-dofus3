using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Allègement d'un client Dofus MITM SANS rien injecter ni toucher à sa mémoire : QoS d'efficacité Windows (EcoQoS),
/// priorité sous la normale et purge périodique du working set tant que la fenêtre est réduite ou n'a pas le focus.
/// Un client dont la fenêtre est au premier plan n'est jamais bridé (l'utilisateur joue dessus) ; s'il le devient après
/// coup, la priorité et le QoS reviennent à la normale. Le mode Socket n'a pas de Dofus.exe : non concerné.
/// </summary>
public static class ClientLightening
{
    public static readonly TimeSpan TrimInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<int, long> Watched = new();

    // ---- Décisions pures (testées) ----------------------------------------------------------------------------------

    /// <summary>Bridage (EcoQoS + BelowNormal) : jamais sur la fenêtre au premier plan.</summary>
    public static bool ShouldThrottle(bool foreground) => !foreground;

    /// <summary>Purge du working set : fenêtre réduite ou sans focus (= pas au premier plan), au plus une fois par 60 s.</summary>
    public static bool ShouldTrim(bool foreground, TimeSpan sinceLastTrim)
        => !foreground && sinceLastTrim >= TrimInterval;

    public readonly record struct Result(bool Skipped, bool PrioritySet, bool EcoQosSet, bool Watching);

    // ---- API --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Allège ce client maintenant (sauf s'il est au premier plan) et le surveille jusqu'à sa fermeture. Idempotent par pid.
    /// Best-effort : un refus d'accès n'est jamais fatal.
    /// </summary>
    public static Result Apply(Process process)
    {
        bool foreground = IsForeground(process);
        bool priority = false, eco = false;
        if (ShouldThrottle(foreground)) (priority, eco) = Throttle(process, true);
        bool watching = StartWatch(process);
        return new Result(foreground, priority, eco, watching);
    }

    /// <summary>Remet la priorité normale et le QoS géré par le système.</summary>
    public static void Restore(Process process) => Throttle(process, false);

    public static bool IsEcoQosEnabled(Process process)
    {
        var state = new PowerThrottlingState { Version = 1 };
        return GetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>())
            && (state.StateMask & ExecutionSpeed) != 0;
    }

    private static (bool Priority, bool Eco) Throttle(Process process, bool on)
    {
        bool priority = false, eco = false;
        try { process.PriorityClass = on ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; priority = true; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
        try
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = on ? ExecutionSpeed : 0, StateMask = on ? ExecutionSpeed : 0 };
            eco = SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or EntryPointNotFoundException) { }
        return (priority, eco);
    }

    private static bool StartWatch(Process process)
    {
        int pid = process.Id;
        long start;
        try { start = process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return false; }
        if (!Watched.TryAdd(pid, start)) return false;
        _ = Task.Run(() => WatchAsync(pid, start));
        return true;
    }

    private static async Task WatchAsync(int pid, long start)
    {
        DateTime lastTrim = DateTime.UtcNow;
        bool throttled = true;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks != start) return;
            while (!process.HasExited)
            {
                await Task.Delay(Tick).ConfigureAwait(false);
                process.Refresh();
                if (process.HasExited) break;
                bool foreground = IsForeground(process);
                if (foreground == throttled)
                {
                    Throttle(process, !foreground);
                    throttled = !foreground;
                }
                if (ShouldTrim(foreground, DateTime.UtcNow - lastTrim))
                {
                    lastTrim = DateTime.UtcNow;
                    EmptyWorkingSet(process.Handle);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ArgumentException)
        {
            TransportLog.Line($"[mod] pid {pid} : allègement client interrompu : {ex.Message}");
        }
        finally { Watched.TryRemove(pid, out _); }
    }

    private static bool IsForeground(Process process)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint owner);
        return owner == (uint)process.Id;
    }

    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState info, uint size);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
