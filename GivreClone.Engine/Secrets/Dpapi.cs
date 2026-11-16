using System.Security.Cryptography;
using System.Text;

namespace GivreClone.Engine.Secrets;

/// <summary>Chiffrement au repos DPAPI (CurrentUser) partage par les stores (comptes, proxys).</summary>
public static class Dpapi
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GivreClone.v1");

    public static string Protect(string plain)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Le chiffrement DPAPI requiert Windows.");
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(blob);
    }

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64) || !OperatingSystem.IsWindows()) return null;
        try
        {
            var blob = Convert.FromBase64String(protectedBase64);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }
}
