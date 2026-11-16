using System.Collections.Concurrent;
using System.Threading;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Compteurs de trafic PAR compte Socket (pid synthétique), lus par TransportPerf toutes les 60 s. Un compte Socket n'a pas
/// de process à mesurer : sa consommation se lit dans GivreClone.exe (ws − base à vide) / nombre de comptes, et son activité
/// dans ces compteurs. Incréments Interlocked sur le fil de lecture / d'écriture : coût négligeable.
/// </summary>
internal static class SocketStats
{
    internal sealed class Counters
    {
        public long RxFrames, RxBytes, TxFrames, TxBytes, Sessions;
    }

    private static readonly ConcurrentDictionary<uint, Counters> _byPid = new();

    public static Counters For(uint pid) => _byPid.GetOrAdd(pid, static _ => new Counters());

    public static void Rx(uint pid, int bytes) { var c = For(pid); Interlocked.Increment(ref c.RxFrames); Interlocked.Add(ref c.RxBytes, bytes); }
    public static void Tx(uint pid, int bytes) { var c = For(pid); Interlocked.Increment(ref c.TxFrames); Interlocked.Add(ref c.TxBytes, bytes); }
    public static void SessionStarted(uint pid) => Interlocked.Increment(ref For(pid).Sessions);

    public static bool TryGet(uint pid, out Counters c) => _byPid.TryGetValue(pid, out c!);

    /// <summary>Compte arrêté : on libère ses compteurs.</summary>
    public static void Forget(uint pid) => _byPid.TryRemove(pid, out _);
}
