using System;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Transport;

/// <summary>
/// Journal verbeux du mode Socket, par catégorie filtrable : [socket] connexion, [socket-auth] HAAPI/jeton,
/// [socket-schnorr] preuve d'identification, [socket-tx]/[socket-rx] chaque paquet, [socket-perf] consommation.
/// Passe par TransportLog : fichier de session (%APPDATA%\GivreClone\logs), Console globale et Console du bot (pid).
/// Actif en Release exprès : l'app tourne en AOT chez l'utilisateur, un #if DEBUG n'y serait jamais visible.
/// </summary>
public static class SocketLog
{
    public static void Info(uint pid, string message) => TransportLog.Line(pid, "[socket] " + message);
    public static void Auth(uint pid, string message) => TransportLog.Line(pid, "[socket-auth] " + message);
    public static void Schnorr(uint pid, string message) => TransportLog.Line(pid, "[socket-schnorr] " + message);
    // Chaque trame était journalisée (~30 lignes/s, des dizaines de Mo par heure) : coût CPU, disque et liste de la Console. On garde UNE ligne par type de message toutes les 3 s, avec le nombre de
    // lignes masquées ; une capture de découverte (Réglages) rétablit le détail complet.
    private static readonly GivreClone.App.Services.LogThrottle Throttle = new(TimeSpan.FromSeconds(3));
    private static string? Throttled(char dir, uint pid, string message)
    {
        if (GivreClone.App.Services.AppConfig.Current.PacketDump) return message;
        return Throttle.ShouldLog(dir + pid.ToString() + GivreClone.App.Services.LogThrottle.KeyOf(message), Environment.TickCount64, out int skipped)
            ? (skipped > 0 ? message + $"  (+{skipped} masquée(s))" : message) : null;
    }
    public static void Tx(uint pid, string message) { if (Throttled('>', pid, message) is { } m) TransportLog.Line(pid, "[socket-tx] " + m); }
    public static void Rx(uint pid, string message) { if (Throttled('<', pid, message) is { } m) TransportLog.Line(pid, "[socket-rx] " + m); }
    public static void Perf(uint pid, string message) => TransportLog.Line(pid, "[socket-perf] " + message);

    /// <summary>Hex des <paramref name="max"/> premiers octets, suffixé "…" si la frame est plus longue. LICENCE DÉVELOPPEUR
    /// SEULEMENT : une trame protobuf porte « type.ankama.com/&lt;code obfusqué&gt; » en clair (décodable depuis l'hexa) -> hors
    /// licence dev, « (masqué) ». Ne jamais y passer une trame d'identification (jeton / ticket du compte) : la taille suffit.</summary>
    public static string Hex(ReadOnlySpan<byte> data, int max = 32) =>
        !GivreClone.App.Services.LicenseService.IsDeveloper ? "(masqué)"
        : data.Length <= max ? Convert.ToHexString(data) : Convert.ToHexString(data[..max]) + "…";
}
