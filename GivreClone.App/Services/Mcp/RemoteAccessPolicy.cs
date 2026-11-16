using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GivreClone.App.Services.Mcp;

/// <summary>Règles du contrôle à distance, pures : qui a le droit de se connecter (boucle locale, Tailscale, réseau local si autorisé), le jeton
/// (génération, comparaison en temps constant) et le nom d'un outil. Rien n'est ouvert à Internet : une adresse publique est toujours refusée.</summary>
public static class RemoteAccessPolicy
{
    public static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public static bool IsAllowed(IPAddress remote, bool allowLan)
    {
        var ip = Normalize(remote);
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0xfd && b[1] == 0x7a && b[2] == 0x11 && b[3] == 0x5c && b[4] == 0xa1 && b[5] == 0xe0) return true;   // Tailscale fd7a:115c:a1e0::/48
            return allowLan && ((b[0] & 0xfe) == 0xfc || (b[0] == 0xfe && (b[1] & 0xc0) == 0x80));                           // ULA fc00::/7, lien local fe80::/10
        }
        var v = ip.GetAddressBytes();
        if (v[0] == 100 && v[1] >= 64 && v[1] <= 127) return true;                                                          // Tailscale 100.64.0.0/10
        if (!allowLan) return false;
        return v[0] == 10 || (v[0] == 172 && v[1] >= 16 && v[1] <= 31) || (v[0] == 192 && v[1] == 168) || (v[0] == 169 && v[1] == 254);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool TokenMatches(string expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static bool IsToolName(string? name)
    {
        if (name is null || name.Length is < 2 or > 48) return false;
        foreach (char c in name) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
        return true;
    }
}
