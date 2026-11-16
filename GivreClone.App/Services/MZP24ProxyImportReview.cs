using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace GivreClone.App.Services;

/// <summary>
/// A local, read-only review of pasted proxy configuration. Unlike the existing
/// aggregate import summary, it explains each rejected or duplicate line.
/// Preview rows NEVER contain usernames, passwords, or the original text.
/// No store, account, session, DNS or network access is performed.
/// </summary>
public static class MZP24ProxyImportReview
{
    public const int MaxInputChars = 262144;
    public const int MaxInputLines = 4096;
    public const int MaxLineChars = 2048;

    public static MZP24ProxyReviewResult Inspect(string? source, string defaultProtocol = "SOCKS5")
    {
        source ??= "";
        if (source.Length > MaxInputChars)
            throw new ArgumentOutOfRangeException(nameof(source), "Import trop volumineux (262144 caractères max).");

        if (!string.Equals(defaultProtocol, "SOCKS5", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(defaultProtocol, "HTTP", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Protocole par défaut non pris en charge.", nameof(defaultProtocol));

        var lines = source.Split('\n');
        if (lines.Length > MaxInputLines)
            throw new ArgumentOutOfRangeException(nameof(source), "Import trop long (4096 lignes max).");

        var rows = new List<MZP24ProxyReviewRow>();
        var unique = new Dictionary<ProxyKey, int>();
        for (int n = 0; n < lines.Length; n++)
        {
            string raw = lines[n].Trim().TrimEnd('\r').Trim();
            if (raw.Length == 0 || raw.StartsWith('#') || raw.StartsWith("--", StringComparison.Ordinal))
                continue;

            int number = n + 1;
            if (raw.Length > MaxLineChars)
            {
                rows.Add(Reject(number, "Ligne trop longue (2048 caractères maximum)."));
                continue;
            }
            if (raw.Any(char.IsControl))
            {
                rows.Add(Reject(number, "Caractère de contrôle interdit dans une entrée proxy."));
                continue;
            }
            // The historic parser maps https:// onto plain HTTP CONNECT.
            // That could silently turn a TLS proxy configuration into cleartext.
            if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(Reject(number, "Proxy HTTPS non admis : le mode HTTP CONNECT local ne chiffre pas la connexion au proxy."));
                continue;
            }

            var parsed = ProxyImportParser.Parse(raw, defaultProtocol);
            if (parsed.Valid.Count != 1)
            {
                string reason = parsed.Errors.FirstOrDefault()?.Reason ?? "Format de proxy non reconnu.";
                rows.Add(Reject(number, Explain(reason)));
                continue;
            }

            var proxy = parsed.Valid[0];
            if (!TryNormalizeHost(proxy.Host, out var canonicalHost))
            {
                rows.Add(Reject(number, "Hôte invalide : utiliser un nom DNS ou une adresse IPv4, sans chemin ni espaces."));
                continue;
            }
            if (proxy.User is { Length: > 128 } || proxy.Pass is { Length: > 512 })
            {
                rows.Add(Reject(number, "Identifiant ou mot de passe trop long (128/512 caractères)."));
                continue;
            }
            if (proxy.User?.Any(char.IsControl) == true || proxy.Pass?.Any(char.IsControl) == true)
            {
                rows.Add(Reject(number, "Caractère de contrôle interdit dans les identifiants."));
                continue;
            }

            // Match the case-insensitive duplicate policy of the existing import.
            var key = new ProxyKey(proxy.Protocol.ToUpperInvariant(), canonicalHost, proxy.Port,
                (proxy.User ?? "").ToUpperInvariant());
            // Keep the same duplicate identity policy as ProxyImportParser: password
            // changes alone do not create another independent proxy endpoint.
            if (unique.TryGetValue(key, out int original))
            {
                rows.Add(new(number, MZP24ProxyLineStatus.Duplicate, "Doublon de la ligne " + original + ".",
                    MaskedEndpoint(proxy.Protocol, canonicalHost, proxy.Port, proxy.User is not null), original));
                continue;
            }
            unique.Add(key, number);
            rows.Add(new(number, MZP24ProxyLineStatus.Accepted, "Prêt pour import local.",
                MaskedEndpoint(proxy.Protocol, canonicalHost, proxy.Port, proxy.User is not null), null));
        }
        return new MZP24ProxyReviewResult(rows);
    }

    private static MZP24ProxyReviewRow Reject(int line, string reason)
        => new(line, MZP24ProxyLineStatus.Rejected, reason, null, null);

    private static string MaskedEndpoint(string protocol, string host, int port, bool hasUser)
        => protocol.ToUpperInvariant() + " " + host + ":" + port.ToString(CultureInfo.InvariantCulture) +
           (hasUser ? " (identifiants masqués)" : " (sans identifiants)");

    private static string Explain(string reason) => reason switch
    {
        "port invalide" => "Port invalide : entier de 1 à 65535 attendu.",
        "hôte invalide" => "Hôte manquant ou invalide : format hôte:port attendu.",
        "protocole inconnu" => "Protocole non reconnu : utiliser socks5:// ou http://.",
        "identifiants user:pass attendus avant @" =>
            "Identifiants invalides : utiliser utilisateur:motdepasse@hôte:port.",
        "hôte:port attendu après @" => "Après @, renseigner hôte:port.",
        _ => "Format attendu : hôte:port, hôte:port:utilisateur:motdepasse ou utilisateur:motdepasse@hôte:port."
    };

    private static bool TryNormalizeHost(string host, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 ||
            host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c > 127))
            return false;

        if (IPAddress.TryParse(host, out var addr))
        {
            if (addr.AddressFamily != AddressFamily.InterNetwork) return false;
            normalized = addr.ToString();
            return true;
        }

        // A dotted numeric host can look like IPv4 without being valid IPv4.
        if (host.Contains('.') && host.All(c => c is '.' or >= '0' and <= '9'))
            return false;

        var labels = host.Split('.');
        if (labels.Any(label => label.Length is < 1 or > 63 ||
            label[0] == '-' || label[^1] == '-' ||
            label.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))))
            return false;
        normalized = host.ToLowerInvariant();
        return true;
    }

    private readonly record struct ProxyKey(string Protocol, string Host, int Port, string User);
}

public enum MZP24ProxyLineStatus { Accepted, Duplicate, Rejected }

public sealed record MZP24ProxyReviewRow(
    int Line,
    MZP24ProxyLineStatus Status,
    string Reason,
    string? MaskedPreview,
    int? DuplicateOf)
{
    public string DisplayStatus => Status switch
    {
        MZP24ProxyLineStatus.Accepted => "Valide",
        MZP24ProxyLineStatus.Duplicate => "Doublon",
        _ => "À corriger"
    };
}

public sealed class MZP24ProxyReviewResult
{
    public IReadOnlyList<MZP24ProxyReviewRow> Rows { get; }
    public int Accepted => Rows.Count(r => r.Status == MZP24ProxyLineStatus.Accepted);
    public int Duplicates => Rows.Count(r => r.Status == MZP24ProxyLineStatus.Duplicate);
    public int Rejected => Rows.Count(r => r.Status == MZP24ProxyLineStatus.Rejected);
    public string Summary => $"{Accepted} accepté(s), {Duplicates} doublon(s), {Rejected} rejeté(s)";

    public MZP24ProxyReviewResult(IReadOnlyList<MZP24ProxyReviewRow> rows) => Rows = rows;
}
