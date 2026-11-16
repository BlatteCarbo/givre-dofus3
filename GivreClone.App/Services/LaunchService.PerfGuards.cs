using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private const long DiscoveryDumpMaxMs = 15 * 60_000;
    private long _discoveryDumpSince;

    /// <summary>La capture de découverte écrit des dizaines de Mo par heure : oubliée activée, elle use le CPU et le disque. Elle se coupe donc d'elle-même après 15 minutes
    /// (le réglage repasse à Off ; on la réactive d'un clic pour une nouvelle capture).</summary>
    private bool DiscoveryDumpStillAllowed()
    {
        long now = System.Environment.TickCount64;
        if (_discoveryDumpSince == 0) _discoveryDumpSince = now;
        if (now - _discoveryDumpSince <= DiscoveryDumpMaxMs) return true;
        _discoveryDumpSince = 0;
        AppConfig.Current.PacketDump = false;
        try { AppConfig.Save(); } catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { /* réglage non enregistré : tant pis */ }
        TransportLog.Line("[perf] capture de découverte coupée automatiquement après 15 min (réglage repassé à Off) : réactive-la dans les Réglages pour une nouvelle capture");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Show("Capture de découverte coupée après 15 min : réactive-la dans les Réglages pour une nouvelle capture.", success: false));
        return false;
    }
}
