using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GivreRelayAgent;

/// <summary>
/// Ce que le partage laisse passer (handoff §3.4), d'après le <c>welcome</c> du gateway.
///
/// « Dofus uniquement » (scope ankama) :
///  - un NOM est accepté s'il finit par un des suffixes (ankama.com, ankama-games.com) et que le port est autorisé ;
///  - une IP est acceptée si elle sort d'une résolution récente (moins de 10 min) d'un nom de <c>resolve</c>.
/// « Tout » (scope all) : ports 80 et 443, plus ce que « Dofus uniquement » accepte (le jeu passe aussi par le 5555).
///
/// Dans tous les cas, l'agent ne joint JAMAIS une adresse privée ou réservée (box, imprimante, réseau local de l'ami).
/// </summary>
public sealed class HostPolicy
{
    private static readonly string[] DefaultSuffixes = { "ankama.com", "ankama-games.com" };
    private static readonly int[] DefaultPorts = { 443, 5555 };
    private static readonly TimeSpan ResolveValidity = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DateTime> _resolved = new(StringComparer.Ordinal);

    public bool ScopeAll { get; }
    public IReadOnlyList<string> Suffixes { get; }
    public IReadOnlyList<string> Resolve { get; }
    public IReadOnlySet<int> Ports { get; }

    public HostPolicy(bool scopeAll, IEnumerable<string>? suffixes, IEnumerable<string>? resolve, IEnumerable<int>? ports)
    {
        ScopeAll = scopeAll;
        Suffixes = (suffixes ?? DefaultSuffixes).Select(Normalize).Where(s => s.Length > 0).ToArray();
        Resolve = (resolve ?? Array.Empty<string>()).Select(Normalize).Where(s => s.Length > 0).Distinct().ToArray();
        Ports = (ports ?? DefaultPorts).ToHashSet();
    }

    /// <summary>Lit <c>scope</c> et <c>allowed_hosts</c> du message d'accueil (null pour le scope « all » : valeurs par défaut).</summary>
    public static HostPolicy FromWelcome(JsonElement w)
    {
        bool all = w.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String && sc.GetString() == "all";
        if (!w.TryGetProperty("allowed_hosts", out var ah) || ah.ValueKind != JsonValueKind.Object)
            return new HostPolicy(all, null, null, null);
        return new HostPolicy(all, Strings(ah, "suffixes"), Strings(ah, "resolve"),
            ah.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToArray()
                : null);
    }

    private static string[]? Strings(JsonElement o, string k)
        => o.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : null;

    private static string Normalize(string h) => h.Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>La destination demandée est-elle permise par le partage (avant résolution DNS) ?</summary>
    public bool Allows(string destHost, int port)
    {
        if (port is <= 0 or > 65535) return false;
        string h = Normalize(destHost);
        bool ankama = Ports.Contains(port) && (TryParseIp(h, out var ip) ? RecentlyResolved(ip) : MatchesSuffix(h));
        return ankama || ScopeAll && port is 80 or 443;
    }

    /// <summary>IP littérale, crochets IPv6 et zone (<c>fe80::1%eth0</c>) retirés avant l'analyse.</summary>
    public static bool TryParseIp(string host, out IPAddress ip)
    {
        string h = host.Trim();
        if (h.StartsWith('[') && h.EndsWith(']')) h = h[1..^1];
        int zone = h.IndexOf('%');
        if (zone > 0 && h.Contains(':')) h = h[..zone];
        return IPAddress.TryParse(h, out ip!);
    }

    private bool MatchesSuffix(string h) => Suffixes.Any(s => h == s || h.EndsWith("." + s, StringComparison.Ordinal));

    private bool RecentlyResolved(IPAddress ip)
        => _resolved.TryGetValue(Canon(ip), out var until) && until > DateTime.UtcNow;

    private static string Canon(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();

    /// <summary>Retient une IP obtenue en résolvant un nom de <c>resolve</c>.</summary>
    public void Remember(IPAddress ip, DateTime until) => _resolved[Canon(ip)] = until;

    /// <summary>Résout les noms de <c>resolve</c> (au démarrage puis toutes les 5 min). Chaque IP vaut 10 min.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        foreach (var n in Resolve)
        {
            try
            {
                foreach (var a in await Dns.GetHostAddressesAsync(n, ct).ConfigureAwait(false))
                    Remember(a, DateTime.UtcNow + ResolveValidity);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* nom momentanément introuvable : réessayé au prochain cycle */ }
        }
        foreach (var kv in _resolved) if (kv.Value <= DateTime.UtcNow) _resolved.TryRemove(kv.Key, out _);
    }

    /// <summary>Adresse joignable sur Internet (ni locale, ni privée, ni réservée) ? Protège le réseau de l'ami.</summary>
    public static bool IsPublic(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return false;
        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = a.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127
                     || b[0] == 100 && b[1] >= 64 && b[1] <= 127          // CGNAT 100.64/10
                     || b[0] == 169 && b[1] == 254                         // lien local
                     || b[0] == 172 && b[1] >= 16 && b[1] <= 31
                     || b[0] == 192 && b[1] == 168
                     || b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)
                     || b[0] == 198 && (b[1] == 18 || b[1] == 19)          // bancs de test
                     || b[0] == 198 && b[1] == 51 && b[2] == 100
                     || b[0] == 203 && b[1] == 0 && b[2] == 113
                     || b[0] >= 224);                                      // multicast + réservé + broadcast
        }
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = a.GetAddressBytes();
            if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast || a.Equals(IPAddress.IPv6None)) return false;
            if ((b[0] & 0xFE) == 0xFC) return false;                       // ULA fc00::/7
            return (b[0] & 0xE0) == 0x20;                                  // seul l'unicast global 2000::/3 est public
        }
        return false;
    }
}
