using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Transport;

/// <summary>Bot mesuré par l'échantillonneur : pid + libellé lisible (nom du perso).</summary>
public readonly record struct PerfTarget(uint Pid, string Label);

/// <summary>
/// Mesure RAM/CPU toutes les 60 s dans [socket-perf] : GivreClone.exe, puis chaque bot MITM (son process Dofus.exe).
/// C'est la base de comparaison avec le mode Socket, dont la raison d'être est de réduire cette consommation.
/// Coût propre : un tick par minute sur un thread du pool.
/// </summary>
public static class TransportPerf
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const uint HostKey = 0;   // clé du process GivreClone.exe (aucun bot n'a le pid 0)

    // Dernier (temps CPU, horodatage, démarrage du process) par clé pour calculer le % CPU. Le démarrage distingue un pid
    // réutilisé par Windows entre deux mesures. Touché uniquement par la boucle d'échantillonnage.
    private static readonly Dictionary<uint, (TimeSpan Cpu, long Stamp, DateTime Start)> _last = new();
    private static int _started;

    // Mode SOCKET : aucun process par compte -> sa consommation = (ws de GivreClone.exe − base mesurée SANS aucun bot) / N.
    private static long _baselineWs;                 // ws de GivreClone.exe à la dernière mesure sans aucun bot (0 = inconnue)
    private static long _lastAllocated = -1;         // GC.GetTotalAllocatedBytes à la mesure précédente (débit d'allocation)
    private static readonly Dictionary<uint, (long RxF, long RxB, long TxF, long TxB)> _lastSocket = new();

    public static void Start(Func<IReadOnlyList<PerfTarget>> targets, Func<IReadOnlyList<PerfTarget>>? socketTargets = null)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(() => LoopAsync(targets, socketTargets));
    }

    private static async Task LoopAsync(Func<IReadOnlyList<PerfTarget>> targets, Func<IReadOnlyList<PerfTarget>>? socketTargets)
    {
        using (var self = Process.GetCurrentProcess()) _last[HostKey] = (self.TotalProcessorTime, Stopwatch.GetTimestamp(), self.StartTime);
        SocketLog.Perf(0, $"échantillonnage démarré : une mesure toutes les {Interval.TotalSeconds:0} s ({Environment.ProcessorCount} cœurs logiques)");
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            SampleOnce(targets, socketTargets);
    }

    private static void SampleOnce(Func<IReadOnlyList<PerfTarget>> targets, Func<IReadOnlyList<PerfTarget>>? socketTargets)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            long hostWs;
            using (var self = Process.GetCurrentProcess())
            {
                hostWs = self.WorkingSet64;
                long alloc = GC.GetTotalAllocatedBytes(false);
                string rate = _lastAllocated < 0 ? "n/d" : Mb(Math.Max(0, alloc - _lastAllocated));
                _lastAllocated = alloc;
                SocketLog.Perf(0, $"GivreClone.exe : ws={Mb(self.WorkingSet64)} Mo privé={Mb(self.PrivateMemorySize64)} Mo " +
                                  $"tas-gc={Mb(GC.GetTotalMemory(false))} Mo alloué/min={rate} Mo gc0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} " +
                                  $"cpu={Cpu(HostKey, self.TotalProcessorTime, self.StartTime, now)} threads={self.Threads.Count} handles={self.HandleCount}");
            }
            var sockets = socketTargets?.Invoke() ?? Array.Empty<PerfTarget>();
            SampleSockets(sockets, hostWs, anyMitm: targets().Count > 0);

            var seen = new HashSet<uint> { HostKey };
            long totalWs = 0;
            int measured = 0;
            foreach (var t in targets())
            {
                if (t.Pid == 0 || !seen.Add(t.Pid)) continue;
                try
                {
                    using var p = Process.GetProcessById((int)t.Pid);
                    // Tout lire AVANT de cumuler : un process qui meurt en cours de lecture n'entre pas dans le total.
                    long ws = p.WorkingSet64, priv = p.PrivateMemorySize64;
                    TimeSpan cpu = p.TotalProcessorTime;
                    DateTime start = p.StartTime;
                    totalWs += ws;
                    measured++;
                    SocketLog.Perf(t.Pid, $"{t.Label} (mitm, Dofus.exe pid {t.Pid}) : ws={Mb(ws)} Mo " +
                                          $"privé={Mb(priv)} Mo cpu={Cpu(t.Pid, cpu, start, now)}");
                }
                catch (ArgumentException) { SocketLog.Perf(t.Pid, $"{t.Label} : process {t.Pid} terminé depuis la dernière mesure"); }
                catch (Exception e) { SocketLog.Perf(t.Pid, $"{t.Label} : mesure impossible ({e.GetType().Name}: {e.Message})"); }
            }

            var gone = new List<uint>();
            foreach (var key in _last.Keys) if (!seen.Contains(key)) gone.Add(key);
            foreach (var key in gone) _last.Remove(key);

            if (measured > 0)
                SocketLog.Perf(0, $"total bots mitm : {measured} × Dofus.exe = {Mb(totalWs)} Mo (moyenne {Mb(totalWs / measured)} Mo/bot)");
        }
        catch (Exception e) { SocketLog.Perf(0, $"échantillonnage en échec ({e.GetType().Name}: {e.Message})"); }
    }

    // Comptes SOCKET : trafic de la dernière minute par compte + estimation mémoire par compte (objectif < 50 Mo/compte).
    private static void SampleSockets(IReadOnlyList<PerfTarget> sockets, long hostWs, bool anyMitm)
    {
        if (sockets.Count == 0)
        {
            if (!anyMitm) _baselineWs = hostWs;   // aucun bot : base de GivreClone.exe à vide
            _lastSocket.Clear();
            return;
        }
        var seen = new HashSet<uint>();
        foreach (var t in sockets)
        {
            if (!seen.Add(t.Pid) || !Socket.SocketStats.TryGet(t.Pid, out var c)) continue;
            long rxF = Interlocked.Read(ref c.RxFrames), rxB = Interlocked.Read(ref c.RxBytes);
            long txF = Interlocked.Read(ref c.TxFrames), txB = Interlocked.Read(ref c.TxBytes);
            var prev = _lastSocket.TryGetValue(t.Pid, out var pv) ? pv : (0L, 0L, 0L, 0L);
            _lastSocket[t.Pid] = (rxF, rxB, txF, txB);
            SocketLog.Perf(t.Pid, $"{t.Label} (socket) : dernière minute reçu {rxF - prev.Item1} trames / {Kb(rxB - prev.Item2)} Ko, " +
                                  $"envoyé {txF - prev.Item3} trames / {Kb(txB - prev.Item4)} Ko (sessions {Interlocked.Read(ref c.Sessions)})");
        }
        foreach (var k in new List<uint>(_lastSocket.Keys)) if (!seen.Contains(k)) _lastSocket.Remove(k);
        string perAccount = _baselineWs > 0 && !anyMitm
            ? $"≈ {Mb(Math.Max(0, hostWs - _baselineWs) / sockets.Count)} Mo/compte (ws {Mb(hostWs)} − base à vide {Mb(_baselineWs)} Mo, objectif < 50)"
            : anyMitm ? "estimation par compte indisponible (bots MITM en cours : base mêlée)" : "estimation par compte indisponible (base à vide pas encore mesurée)";
        SocketLog.Perf(0, $"total comptes socket : {sockets.Count} — {perAccount}");
    }

    private static string Kb(long bytes) => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);

    // % CPU sur l'intervalle écoulé depuis la mesure précédente : normalisé sur tous les cœurs (Gestionnaire des tâches) ET
    // rapporté à un seul cœur, sinon sur une machine à 16 threads 30 % d'un cœur s'affiche 1,9 % et passe pour « léger ».
    private static string Cpu(uint key, TimeSpan cpu, DateTime start, long now)
    {
        string text = "n/d (1re mesure)";
        if (_last.TryGetValue(key, out var prev) && prev.Start == start)
        {
            double wallMs = (now - prev.Stamp) * 1000.0 / Stopwatch.Frequency;
            double busyMs = (cpu - prev.Cpu).TotalMilliseconds;
            if (wallMs > 0 && busyMs >= 0)
            {
                double oneCore = 100.0 * busyMs / wallMs;
                text = $"{Pct(oneCore / Environment.ProcessorCount)}% ({Pct(oneCore)}% d'un cœur)";
            }
        }
        _last[key] = (cpu, now, start);
        return text;
    }

    private static string Pct(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
}
