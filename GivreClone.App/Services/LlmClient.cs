using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Client LLM minimal, compatible OpenAI (POST /chat/completions), pour générer une réponse auto à un MP. AOT-safe :
/// DTOs source-gen (<see cref="LlmJsonContext"/>), aucune réflexion. Appel éphémère (timeout + annulation) ; toute
/// erreur -> null (fail-soft : on reste muet plutôt que de crasher/spammer). La clé n'est JAMAIS loggée.
/// </summary>
public static class LlmClient
{
    /// <summary>Génère une complétion. `baseUrl` vide = https://api.openai.com/v1. Renvoie le texte, ou null si échec.</summary>
    public static async Task<string?> CompleteAsync(string apiKey, string model, string? baseUrl,
        string systemPrompt, string userText, int maxChars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(userText)) return null;
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl!.TrimEnd('/');
        // AUDIT L1 : la clé API (Bearer) part vers `root`. On EXIGE https pour un hôte DISTANT (pas de clé en clair sur le
        // réseau) ; http n'est toléré que pour un LLM LOCAL (loopback / IP privée : LM Studio, Ollama...). Sinon on refuse.
        if (!IsAllowedEndpoint(root)) return null;
        var req = new LlmChatRequest
        {
            model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model,
            max_tokens = Math.Clamp(maxChars / 2 + 40, 32, 400),   // ~2 chars/token, marge
            temperature = 0.7,
            messages = new List<LlmChatMessage>
            {
                new() { role = "system", content = systemPrompt ?? "" },
                new() { role = "user", content = userText },
            },
        };
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"GivreClient/{LicenseService.AppVersion}");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            string body = JsonSerializer.Serialize(req, typeof(LlmChatRequest), LlmJsonContext.Default);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(root + "/chat/completions", content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = (LlmChatResponse?)JsonSerializer.Deserialize(raw, typeof(LlmChatResponse), LlmJsonContext.Default);
            string? text = parsed?.choices is { Count: > 0 } c ? c[0].message?.content : null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text!.Trim();
            if (text.Length > maxChars) text = text.Substring(0, maxChars);
            return text;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }   // fail-soft : réseau/clé invalide/quotas -> pas de réponse
    }

    // https autorisé partout ; http SEULEMENT vers loopback / IP privée (LLM local). Tout le reste = refusé (pas de clé en clair distante).
    private static bool IsAllowedEndpoint(string root)
    {
        if (!Uri.TryCreate(root, UriKind.Absolute, out var u)) return false;
        if (u.Scheme == Uri.UriSchemeHttps) return true;
        if (u.Scheme != Uri.UriSchemeHttp) return false;
        string h = u.Host;
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!System.Net.IPAddress.TryParse(h, out var ip)) return false;   // http vers un nom distant -> refusé
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }
}

// ---- DTOs OpenAI (noms JSON exacts, pas de politique de nommage) ----
internal sealed class LlmChatRequest
{
    public string model { get; set; } = "";
    public List<LlmChatMessage> messages { get; set; } = new();
    public int max_tokens { get; set; }
    public double temperature { get; set; }
}
internal sealed class LlmChatMessage { public string role { get; set; } = ""; public string content { get; set; } = ""; }
internal sealed class LlmChatResponse { public List<LlmChatChoice>? choices { get; set; } }
internal sealed class LlmChatChoice { public LlmChatMsg? message { get; set; } }
internal sealed class LlmChatMsg { public string? content { get; set; } }

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LlmChatRequest))]
[JsonSerializable(typeof(LlmChatResponse))]
internal sealed partial class LlmJsonContext : JsonSerializerContext { }
