using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>Relais HTTPS Discord. La version historique Post reste sans attente pour les appels Lua ;
/// PostAsync permet aux notifications de connaître le résultat HTTP réel. Aucune erreur ne journalise le token du webhook.</summary>
public static class WebhookService
{
    // Un redirect HTTP 307/308 pourrait retransmettre le message à un hôte différent.
    // Les webhooks ne doivent suivre aucune redirection, y compris vers une origine HTTPS.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };

    public static void Post(string? url, string? message) => _ = PostAsync(url, message);

    /// <summary>True uniquement après un code HTTP 2xx de la destination, jamais à la simple mise en file.</summary>
    public static Task<bool> PostAsync(string? url, string? message)
        => PostWithClientAsync(url, message, Http, Logger.Warn);

    // Le client injectable permet des tests hors réseau, avec HttpMessageHandler simulé.
    internal static async Task<bool> PostWithClientAsync(string? url, string? message, HttpClient http, Action<string>? warn,
        Func<TimeSpan, Task>? delay = null)
    {
        if (string.IsNullOrWhiteSpace(message) || !Uri.TryCreate(url, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(target.Host)
            || !string.IsNullOrEmpty(target.UserInfo) || target.Fragment.Length > 0
            || message.Length > 2000)
        {
            warn?.Invoke("[webhook] envoi refusé : message ou adresse HTTPS invalide.");
            return false;
        }

        delay ??= static d => Task.Delay(d);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string body = "{\"content\":\"" + Escape(message) + "\"}";
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(target, content).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return true;
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    TimeSpan retryAfter = RetryAfter(response);
                    // Ne jamais ignorer un Retry-After trop long : retourner un échec observable.
                    if (retryAfter <= TimeSpan.FromSeconds(2))
                    {
                        await delay(retryAfter).ConfigureAwait(false);
                        continue;
                    }
                }
                // 3xx, 4xx, 5xx : aucun ACK. On ne réessaie pas les 5xx, dont la livraison est ambiguë.
                warn?.Invoke($"[webhook] échec HTTP {(int)response.StatusCode} (destination masquée).");
                return false;
            }
            catch (Exception ex)
            {
                // Une exception réseau n'exclut pas que le serveur ait accepté le message.
                // Pas de nouvelle tentative automatique ni de faux ACK ; jamais ex.Message (URL privée).
                warn?.Invoke($"[webhook] résultat HTTP non confirmé ({ex.GetType().Name}; destination masquée).");
                return false;
            }
        }
        return false;
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            TimeSpan wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        // Discord peut renvoyer un Retry-After fractionnaire (ex. 1.25 s).
        if (response.Headers.TryGetValues("Retry-After", out var retryValues)
            && double.TryParse(retryValues.FirstOrDefault(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double retrySeconds)
            && double.IsFinite(retrySeconds) && retrySeconds >= 0 && retrySeconds <= 86400)
            return TimeSpan.FromSeconds(retrySeconds);
        if (response.Headers.TryGetValues("X-RateLimit-Reset-After", out var values)
            && double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds)
            && double.IsFinite(seconds) && seconds >= 0 && seconds <= 86400)
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.FromSeconds(1);
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char ch in s)
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch < ' ' ? string.Empty : ch.ToString(),
            });
        return sb.ToString();
    }
}

/// <summary>Identifie une anomalie par destination (SHA-256 sans URL en mémoire longue), serveur, sous-zone et sens.</summary>
internal readonly record struct AnomalyAlertKey(string DestinationHash, string ServerIdentity, int SubareaId, bool Opened)
{
    public static AnomalyAlertKey Create(string url, string? server, int pid, int subareaId, bool opened)
    {
        // Un serveur non encore identifié ne doit jamais fusionner les événements de deux PID distincts.
        string identity = string.IsNullOrWhiteSpace(server) || server.Trim() == "-"
            ? $"pid:{pid}" : "server:" + server.Trim().ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim())));
        return new AnomalyAlertKey(hash, identity, subareaId, opened);
    }
}

internal enum AnomalyAlertBegin { Reserved, Duplicate, Saturated }

/// <summary>Déduplication bornée. Une tentative en cours bloque les doublons ; seul un HTTP 2xx démarre les 15 min.</summary>
internal sealed class AnomalyAlertDeduplicator
{
    private readonly record struct Entry(long At, bool Pending);
    private readonly object _gate = new();
    private readonly Dictionary<AnomalyAlertKey, Entry> _entries = new();
    private readonly int _capacity;
    private readonly long _dedupMs;

    public AnomalyAlertDeduplicator(int capacity = 512, long dedupMs = 15 * 60_000)
    {
        if (capacity < 1 || dedupMs < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _dedupMs = dedupMs;
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public AnomalyAlertBegin TryBegin(AnomalyAlertKey key, long now)
    {
        lock (_gate)
        {
            foreach (var expired in _entries.Where(kv => !kv.Value.Pending && now - kv.Value.At >= _dedupMs)
                                            .Select(kv => kv.Key).ToArray()) _entries.Remove(expired);
            if (_entries.ContainsKey(key)) return AnomalyAlertBegin.Duplicate;
            if (_entries.Count >= _capacity)
            {
                // Les succès non expirés ne peuvent être évincés : cela annulerait la garantie 15 min.
                return AnomalyAlertBegin.Saturated;
            }
            _entries.Add(key, new Entry(now, true));
            return AnomalyAlertBegin.Reserved;
        }
    }

    public void Complete(AnomalyAlertKey key, bool httpSucceeded, long now)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || !entry.Pending) return;
            if (httpSucceeded) _entries[key] = new Entry(now, false);
            else _entries.Remove(key); // une erreur 4xx/5xx, timeout ou exception reste réessayable
        }
    }
}
