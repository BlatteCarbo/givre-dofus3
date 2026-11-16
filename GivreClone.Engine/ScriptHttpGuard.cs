using System.Net;
using System.Net.Sockets;

namespace GivreClone.Engine;

/// <summary>
/// Règles PURES (testables) des requêtes HTTP émises par les scripts Lua (http:get / http:post) : https uniquement,
/// adresses publiques uniquement (anti-SSRF vers le réseau local / la machine), en-têtes filtrés, bornes de taille/délai.
/// Aucune E/S ici : la vérification d'adresse est rejouée sur les IP RÉSOLUES au moment de la connexion (anti DNS-rebinding).
/// </summary>
public static class ScriptHttpGuard
{
    public const int MaxUrlLength = 2048, MaxRequestBytes = 256 * 1024, MaxResponseBytes = 1024 * 1024, TimeoutMs = 10000, MaxHeaders = 20;
    public const int MaxHeaderNameLength = 64, MaxHeaderValueLength = 4096;
    /// <summary>User-Agent neutre par défaut (surchargeable par le script).</summary>
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly string[] BlockedSuffixes = { ".localhost", ".local", ".internal", ".lan", ".home.arpa" };
    private static readonly string[] BlockedHeaders = { "host", "content-length", "transfer-encoding", "connection", "upgrade", "te", "expect", "keep-alive" };

    /// <summary>URL acceptée ? (https absolu, sans identifiants, hôte non local ; IP littérale = publique). `error` = code stable.</summary>
    public static bool TryValidateUrl(string? raw, out Uri? uri, out string error)
    {
        uri = null; error = "bad_url";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxUrlLength) return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var u)) return false;
        if (!string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        string host = (u.DnsSafeHost ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return false;
        if (host == "localhost") { error = "blocked_address"; return false; }
        foreach (var s in BlockedSuffixes) if (host.EndsWith(s, StringComparison.Ordinal)) { error = "blocked_address"; return false; }
        if (u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(host, out var ip) || !IsPublicAddress(ip)) { error = "blocked_address"; return false; }
        }
        uri = u; error = "";
        return true;
    }

    /// <summary>Adresse routable sur Internet ? (refuse loopback, privé, lien-local, CGNAT, documentation, multicast, réservé…).
    /// Les IPv6 qui ENCAPSULENT une IPv4 (mappée ::ffff:, NAT64 64:ff9b::/96, 6to4 2002::/16) sont jugées sur l'IPv4.</summary>
    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return IsPublicV4(ip.GetAddressBytes());
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        bool Zero(int from, int to) { for (int i = from; i < to; i++) if (b[i] != 0) return false; return true; }
        if (Zero(0, 12)) return false;                                                     // ::/96 (::, ::1, IPv4-compatible)
        if ((b[0] & 0xFE) == 0xFC) return false;                                           // fc00::/7 (ULA)
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;    // 2001:db8::/32 (documentation)
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return false;    // 2001::/32 (Teredo)
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && Zero(4, 12))   // 64:ff9b::/96 (NAT64)
            return IsPublicV4(new[] { b[12], b[13], b[14], b[15] });
        if (b[0] == 0x20 && b[1] == 0x02)                                                  // 2002::/16 (6to4)
            return IsPublicV4(new[] { b[2], b[3], b[4], b[5] });
        return true;
    }

    private static bool IsPublicV4(byte[] a)
    {
        if (a.Length != 4) return false;
        byte x = a[0], y = a[1], z = a[2];
        if (x == 0 || x == 10 || x == 127) return false;                 // 0/8, 10/8, 127/8
        if (x == 100 && (y & 0xC0) == 64) return false;                  // 100.64/10 (CGNAT)
        if (x == 169 && y == 254) return false;                          // 169.254/16 (lien-local)
        if (x == 172 && (y & 0xF0) == 16) return false;                  // 172.16/12
        if (x == 192 && y == 0 && (z == 0 || z == 2)) return false;      // 192.0.0/24, 192.0.2/24
        if (x == 192 && y == 168) return false;                          // 192.168/16
        if (x == 198 && (y & 0xFE) == 18) return false;                  // 198.18/15 (bancs de test)
        if (x == 198 && y == 51 && z == 100) return false;               // 198.51.100/24
        if (x == 203 && y == 0 && z == 113) return false;                // 203.0.113/24
        if (x >= 224) return false;                                      // multicast + réservé + broadcast
        return true;
    }

    /// <summary>En-tête autorisé ? Nom = jeton RFC 7230 (≤ 64), valeur sans CR/LF (≤ 4096) ; en-têtes de transport/relais refusés.
    /// User-Agent et Content-Type restent surchargeables.</summary>
    public static bool IsAllowedHeader(string name, string value)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxHeaderNameLength) return false;
        foreach (char c in name) if (!IsTokenChar(c)) return false;
        if (value is null || value.Length > MaxHeaderValueLength || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0) return false;
        string n = name.ToLowerInvariant();
        if (n.StartsWith("proxy-", StringComparison.Ordinal)) return false;
        foreach (var b in BlockedHeaders) if (n == b) return false;
        return true;
    }

    private static bool IsTokenChar(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
           or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
