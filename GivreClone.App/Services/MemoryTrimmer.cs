using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Service d'allègement mémoire dynamique pour GivreClone et les clients Dofus gérés.
/// Force périodiquement la compaction du Large Object Heap (LOH) et libère le Working Set
/// des fenêtres minimisées ou en arrière-plan.
/// </summary>
public static class MemoryTrimmer
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private static int _started = 0;
    private static readonly ConcurrentDictionary<int, long> _lastTrimTicksByPid = new();

    public static readonly long MinTrimIntervalMs = (long)TimeSpan.FromMinutes(10).TotalMilliseconds;

    /// <summary>
    /// Décide si un processus doit subir une purge du Working Set.
    /// Retourne true uniquement si workingSetBytes > thresholdMb * 1024 * 1024 ET
    /// (lastTrimTicks == 0 OU nowTicks - lastTrimTicks >= 10 minutes en ms d'Environment.TickCount64).
    /// </summary>
    public static bool ShouldTrim(long workingSetBytes, long lastTrimTicks, long nowTicks, int thresholdMb)
    {
        if (thresholdMb <= 0) return false;
        long thresholdBytes = (long)thresholdMb * 1024L * 1024L;
        if (workingSetBytes <= thresholdBytes) return false;
        if (lastTrimTicks == 0) return true;
        return (nowTicks - lastTrimTicks) >= MinTrimIntervalMs;
    }

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                TrimMemory();
            }
        });
    }

    internal static bool TrimApplication(long workingSetBytes, long lastTrimTicks, long nowTicks,
        int thresholdMb, Action compact, Action releaseWorkingSet)
    {
        if (!ShouldTrim(workingSetBytes, lastTrimTicks, nowTicks, thresholdMb)) return false;
        compact();
        releaseWorkingSet();
        return true;
    }

    public static void TrimMemory()
    {
        try
        {
            long nowTicks = Environment.TickCount64;
            // La collecte et la purge du processus partagent la même décision de pression.
            using (var currentProc = Process.GetCurrentProcess())
            {
                try
                {
                    currentProc.Refresh();
                    _lastTrimTicksByPid.TryGetValue(currentProc.Id, out long lastAppTrim);
                    if (TrimApplication(currentProc.WorkingSet64, lastAppTrim, nowTicks,
                        AppConfig.Current.MemoryTrimAppMb,
                        () =>
                        {
                            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect(2, GCCollectionMode.Aggressive, blocking: false, compacting: true);
                        },
                        () => { EmptyWorkingSet(currentProc.Handle); }))
                    {
                        _lastTrimTicksByPid[currentProc.Id] = nowTicks;
                    }
                }
                catch { }
            }

            // 2. Réduire le Working Set des processus Dofus enfants gérés (ne déclenchent PAS le GC/LOH global)
            var dofusProcesses = Process.GetProcessesByName("Dofus");
            foreach (var p in dofusProcesses)
            {
                try
                {
                    p.Refresh();
                    _lastTrimTicksByPid.TryGetValue(p.Id, out long lastClientTrim);
                    if (ShouldTrim(p.WorkingSet64, lastClientTrim, nowTicks, AppConfig.Current.MemoryTrimClientMb))
                    {
                        if (EmptyWorkingSet(p.Handle) != 0)
                        {
                            _lastTrimTicksByPid[p.Id] = nowTicks;
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
    }
}
