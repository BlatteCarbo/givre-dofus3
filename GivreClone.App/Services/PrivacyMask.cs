namespace GivreClone.App.Services;

/// <summary>Masquage d'affichage du « Mode discret » (StreamerPrivacy). Pur et déterministe : ne sert qu'à l'UI, les
/// identifiants réels (moteur, MCP, persistance) ne passent jamais par ici.</summary>
public static class PrivacyMask
{
    public const string MaskedIp = "***.***.***.***";

    /// <summary>« jean.dupont@mail.fr » -> « je***@mail.fr » ; « jeandupont » -> « je*** ».</summary>
    public static string MaskLogin(string? s, bool mask)
    {
        if (!mask || string.IsNullOrEmpty(s)) return s ?? "";
        int at = s.IndexOf('@');
        if (at >= 0)
            return Head(s[..at]) + "***@" + s[(at + 1)..];
        return Head(s) + "***";
    }

    /// <summary>« 1.2.3.4 » -> « ***.***.***.*** » ; « proxy.host:8080 » -> « ***.***.***.***:8080 ».</summary>
    public static string MaskIp(string? s, bool mask)
    {
        if (!mask || string.IsNullOrEmpty(s)) return s ?? "";
        string port = "";
        int colon = s.LastIndexOf(':');
        // Un seul « : » (hôte:port) ou IPv6 entre crochets (« [::1]:443 ») : le port est gardé. Une IPv6 nue n'a pas de port.
        bool hasPort = colon > 0 && (s.IndexOf(':') == colon || (s[0] == '[' && s[colon - 1] == ']'));
        if (hasPort && IsDigits(s.AsSpan(colon + 1))) port = s[colon..];
        return MaskedIp + port;
    }

    private static string Head(string s) => s.Length <= 2 ? s : s[..2];

    private static bool IsDigits(System.ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return false;
        foreach (char c in s) if (c is < '0' or > '9') return false;
        return true;
    }
}
