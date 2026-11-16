using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Une ligne d'import de proxy reconnue. Protocol vaut "SOCKS5" ou "HTTP".</summary>
public sealed record ParsedProxy(string Protocol, string Host, int Port, string? User, string? Pass, int Line);

/// <summary>Une ligne refusée, avec le motif (jamais le contenu : il peut porter un mot de passe).</summary>
public sealed record ProxyImportError(int Line, string Reason);

public sealed record ProxyImportResult(IReadOnlyList<ParsedProxy> Valid, IReadOnlyList<ProxyImportError> Errors, int Duplicates)
{
    /// <summary>« 12 proxy(s) valide(s), 1 doublon(s), 2 ligne(s) ignorée(s) » : sans aucun identifiant.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Valid.Count} proxy(s) valide(s)" };
            if (Duplicates > 0) parts.Add($"{Duplicates} doublon(s)");
            if (Errors.Count > 0) parts.Add($"{Errors.Count} ligne(s) ignorée(s)");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Import en masse de proxys collés en texte, une entrée par ligne. Formats reconnus (MZP23, déduits de l'usage courant des listes
/// de proxys ; l'original n'est pas lisible à ce niveau) :
/// <c>host:port</c>, <c>host:port:user:pass</c>, <c>user:pass@host:port</c>, <c>socks5://[user:pass@]host:port</c>, <c>http://…</c>.
/// Les lignes vides et celles commençant par <c>#</c> ou <c>--</c> sont ignorées. Un préfixe de schéma l'emporte sur le protocole par défaut.
/// </summary>
public static class ProxyImportParser
{
    public static ProxyImportResult Parse(string? text, string defaultProtocol = "SOCKS5")
    {
        var valid = new List<ParsedProxy>();
        var errors = new List<ProxyImportError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int dups = 0, n = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            n++;
            var line = raw.Trim().TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!TryParseLine(line, defaultProtocol, n, out var p, out var reason)) { errors.Add(new(n, reason)); continue; }
            if (!seen.Add($"{p!.Protocol}|{p.Host}|{p.Port}|{p.User}")) { dups++; continue; }
            valid.Add(p);
        }
        return new ProxyImportResult(valid, errors, dups);
    }

    private static bool TryParseLine(string line, string defaultProtocol, int lineNo, out ParsedProxy? p, out string reason)
    {
        p = null; reason = "";
        string proto = Normalize(defaultProtocol);
        int scheme = line.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var s = line[..scheme].ToLowerInvariant();
            if (s is "socks5" or "socks") proto = "SOCKS5";
            else if (s is "http" or "https") proto = "HTTP";
            else { reason = "protocole inconnu"; return false; }
            line = line[(scheme + 3)..];
        }

        string host, port; string? user = null, pass = null;
        int at = line.LastIndexOf('@');
        if (at >= 0)
        {
            var cred = line[..at];
            var hp = line[(at + 1)..].Split(':');
            if (hp.Length != 2) { reason = "hôte:port attendu après @"; return false; }
            (host, port) = (hp[0], hp[1]);
            int c = cred.IndexOf(':');
            if (c <= 0) { reason = "identifiants user:pass attendus avant @"; return false; }
            (user, pass) = (cred[..c], cred[(c + 1)..]);
        }
        else
        {
            var f = line.Split(':');
            if (f.Length == 2) (host, port) = (f[0], f[1]);
            else if (f.Length >= 4) { (host, port, user) = (f[0], f[1], f[2]); pass = string.Join(":", f.Skip(3)); }
            else { reason = "format non reconnu"; return false; }
        }

        host = host.Trim();
        if (host.Length == 0 || host.Any(char.IsWhiteSpace)) { reason = "hôte invalide"; return false; }
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var portN) || portN is <= 0 or > 65535)
        { reason = "port invalide"; return false; }
        if (string.IsNullOrEmpty(user)) (user, pass) = (null, null);
        p = new ParsedProxy(proto, host, portN, user, pass, lineNo);
        return true;
    }

    private static string Normalize(string protocol) => protocol.Trim().Equals("HTTP", StringComparison.OrdinalIgnoreCase) ? "HTTP" : "SOCKS5";

    /// <summary>Nom attribué à la n-ième entrée importée : « Base 1 », « Base 2 »… ; base vide → « Proxy ».</summary>
    public static string NameFor(string? nameBase, int index) => $"{(string.IsNullOrWhiteSpace(nameBase) ? "Proxy" : nameBase.Trim())} {index}";
}
