using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Metamob;

/// <summary>HTTP natif du contrat REST Metamob (base https://www.metamob.fr/api/v1). Les chemins suivent la table
/// publique du SDK MIT ix-xs/metamob.api, commit 8be181dc. Aucun code du SDK n'est copié.
/// N'appelle ni PATCH /quests/{slug} (réglages), ni PATCH .../trade, ni POST /conversations/{username}/read.</summary>
public sealed class MetamobClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _http;
    private readonly string _key;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _retries;

    public MetamobClient(string apiKey) : this(apiKey, CreateHandler(), true, null) { }

    public MetamobClient(string apiKey, HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(apiKey, handler, false, delay) { }

    private MetamobClient(string apiKey, HttpMessageHandler handler, bool disposeHandler, Func<TimeSpan, CancellationToken, Task>? delay)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Clé API absente.", nameof(apiKey));
        _key = apiKey.Trim();
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _retries = 3;
        _http = new HttpClient(handler, disposeHandler)
        {
            BaseAddress = new Uri(MetamobLimits.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task ConfirmKeyAsync(CancellationToken cancellationToken)
    {
        var text = await SendAsync(HttpMethod.Get, "me", null, false, cancellationToken).ConfigureAwait(false);
        using var doc = Parse(text, true);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new MetamobContractException("Réponse du compte illisible.", true);
    }

    public async Task<IReadOnlyList<MetamobServer>> ListServersAsync(CancellationToken cancellationToken)
    {
        var text = await SendAsync(HttpMethod.Get, "servers", null, false, cancellationToken).ConfigureAwait(false);
        using var doc = Parse(text, true);
        if (!TryArray(doc.RootElement, out var data)) throw new MetamobContractException("Liste des serveurs illisible.", true);
        var list = new List<MetamobServer>();
        foreach (var item in data.EnumerateArray())
        {
            if (!TryInt(item, "id", out var id) || id <= 0) continue;
            if (!TryString(item, "name", out var name)) continue;
            var version = "";
            if (item.TryGetProperty("game_version", out var game) && game.ValueKind == JsonValueKind.Object && TryString(game, "name", out var gameName))
                version = gameName;
            list.Add(new MetamobServer(id, name, version));
        }
        return list;
    }

    public async Task<IReadOnlyList<MetamobQuest>> ListQuestsAsync(CancellationToken cancellationToken)
    {
        var text = await SendAsync(HttpMethod.Get, "quests", null, false, cancellationToken).ConfigureAwait(false);
        using var doc = Parse(text, true);
        if (!TryArray(doc.RootElement, out var data)) throw new MetamobContractException("Liste des quêtes illisible.", true);
        var list = new List<MetamobQuest>();
        foreach (var item in data.EnumerateArray())
        {
            if (!TryString(item, "slug", out var slug) || !MetamobText.IsSlug(slug)) continue;
            if (!item.TryGetProperty("server", out var server) || server.ValueKind != JsonValueKind.Object) continue;
            if (!TryInt(server, "id", out var serverId) || serverId <= 0) continue;
            TryString(server, "name", out var serverName);
            TryString(item, "character_name", out var character);
            var type = "";
            if (item.TryGetProperty("quest_template", out var template) && template.ValueKind == JsonValueKind.Object
                && template.TryGetProperty("quest_type", out var questType) && questType.ValueKind == JsonValueKind.Object
                && questType.TryGetProperty("name", out var typeName) && typeName.ValueKind == JsonValueKind.Object
                && TryString(typeName, "fr", out var french))
                type = french;
            list.Add(new MetamobQuest(slug, character, serverId, serverName, type));
        }
        return list;
    }

    public async Task<MonsterPage> GetQuestMonstersAsync(string slug, int limit, int offset, CancellationToken cancellationToken)
    {
        if (!MetamobText.IsSlug(slug)) throw new MetamobContractException("Quête invalide.");
        if (limit < 1 || limit > MetamobLimits.MaxPage) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var relative = "quests/" + Uri.EscapeDataString(slug) + "/monsters?limit="
            + limit.ToString(CultureInfo.InvariantCulture) + "&offset=" + offset.ToString(CultureInfo.InvariantCulture);
        var text = await SendAsync(HttpMethod.Get, relative, null, false, cancellationToken).ConfigureAwait(false);
        return ParsePage(text);
    }

    public async Task<IReadOnlyList<MonsterAck>> UpdateQuantitiesAsync(string slug, IReadOnlyList<QuantityChange> batch, CancellationToken cancellationToken)
    {
        if (!MetamobText.IsSlug(slug)) throw new MetamobContractException("Quête invalide.");
        if (batch is null || batch.Count < 1 || batch.Count > MetamobLimits.MaxBatch) throw new ArgumentOutOfRangeException(nameof(batch));
        var body = new BatchBody();
        foreach (var row in batch)
        {
            if (row.MonsterId <= 0 || row.LocalQuantity < 0 || row.LocalQuantity > MetamobLimits.MaxQuantity)
                throw new ArgumentOutOfRangeException(nameof(batch));
            body.Monsters.Add(new MonsterBody { MonsterId = row.MonsterId, Quantity = row.LocalQuantity });
        }
        var json = JsonSerializer.Serialize(body, Json);
        var relative = "quests/" + Uri.EscapeDataString(slug) + "/monsters";
        var text = await SendAsync(HttpMethod.Patch, relative, json, true, cancellationToken).ConfigureAwait(false);
        return ParseAcks(text);
    }

    public void Dispose() => _http.Dispose();

    private async Task<string> SendAsync(HttpMethod method, string relative, string? json, bool write, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var message = new HttpRequestMessage(method, relative);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            message.Headers.UserAgent.Add(new ProductInfoHeaderValue("GivreReplica-Metamob", "1"));
            if (json is not null)
            {
                var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                message.Content = content;
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _retries) throw new MetamobCallException(0, "Délai dépassé.", true);
                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= _retries) throw new MetamobCallException(0, MetamobText.Scrub(ex.Message, _key), true);
                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            string text;
            int status;
            bool success;
            TimeSpan? retryAfter;
            using (response)
            {
                text = response.Content is null ? "" : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                status = (int)response.StatusCode;
                success = response.IsSuccessStatusCode;
                retryAfter = RetryAfter(response);
            }
            if (success) return text;

            var error = new MetamobCallException(status, MetamobText.PublicError(status, text, _key), true);
            var retryable = status == 429 || status == 502 || status == 503 || status == 504 || (status == 500 && !write);
            if (!retryable || attempt >= _retries) throw error;
            var delay = status == 429 && retryAfter is TimeSpan asked ? asked : Backoff(attempt);
            if (delay > TimeSpan.FromSeconds(60)) throw error;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan Backoff(int attempt)
    {
        var ms = 500d * Math.Pow(2, attempt);
        if (ms > 60_000) ms = 60_000;
        return TimeSpan.FromMilliseconds(ms);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is TimeSpan delta) return delta > TimeSpan.Zero ? delta : null;
        if (header?.Date is DateTimeOffset date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }
        return null;
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    };

    private static JsonDocument Parse(string text, bool requestSent)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text); }
        catch (JsonException) { throw new MetamobContractException("Réponse illisible.", requestSent); }
    }

    private static MonsterPage ParsePage(string text)
    {
        using var doc = Parse(text, true);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("monsters", out var monsters) || monsters.ValueKind != JsonValueKind.Array)
            throw new MetamobContractException("Liste des monstres illisible.", true);
        int? total = null;
        if (data.TryGetProperty("pagination", out var page) && page.ValueKind == JsonValueKind.Object && TryInt(page, "total", out var parsed))
            total = parsed;
        var list = new List<RemoteMonster>();
        foreach (var item in monsters.EnumerateArray())
        {
            if (!TryInt(item, "id", out var id) || id <= 0 || !TryInt(item, "quantity", out var quantity) || quantity < 0)
                throw new MetamobContractException("Liste des monstres illisible.", true);
            list.Add(new RemoteMonster(id, ReadName(item), quantity));
        }
        return new MonsterPage(list, total);
    }

    private static IReadOnlyList<MonsterAck> ParseAcks(string text)
    {
        using var doc = Parse(text, true);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("monsters", out var monsters) || monsters.ValueKind != JsonValueKind.Array)
            throw new MetamobContractException("Réponse du lot illisible.", true);
        var list = new List<MonsterAck>();
        foreach (var item in monsters.EnumerateArray())
        {
            if (!TryInt(item, "monster_id", out var id) || !TryInt(item, "quantity", out var quantity))
                throw new MetamobContractException("Réponse du lot illisible.", true);
            list.Add(new MonsterAck(id, quantity));
        }
        return list;
    }

    private static string ReadName(JsonElement item)
    {
        if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.Object) return "";
        foreach (var lang in new[] { "fr", "en", "es" })
            if (TryString(name, lang, out var text)) return text;
        return "";
    }

    private static bool TryArray(JsonElement root, out JsonElement data)
    {
        data = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Array;
    }

    private static bool TryInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var prop) && prop.TryGetInt32(out value);
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;
        value = prop.GetString() ?? "";
        return value.Length > 0;
    }

    private sealed class BatchBody
    {
        [JsonPropertyName("monsters")]
        public List<MonsterBody> Monsters { get; } = new();
    }

    private sealed class MonsterBody
    {
        [JsonPropertyName("monster_id")]
        public int MonsterId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }
}
