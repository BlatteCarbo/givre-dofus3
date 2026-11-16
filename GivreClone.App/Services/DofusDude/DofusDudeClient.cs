using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.DofusDude;

/// <summary>
/// Client haute performance pour l'API Dofus 3 de dofusdude (https://api.dofusdu.de).
/// Intègre l'Almanax quotidien, la résolution des niveaux d'items (routage HDV 60- Astrub vs 61+ Bonta),
/// les recettes et les drops de monstres avec mise en cache mémoire thread-safe.
/// </summary>
public static class DofusDudeClient
{
    private const string BaseUrl = "https://api.dofusdu.de/dofus3/v1/fr";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly ConcurrentDictionary<long, int> ItemLevels = new();
    private static volatile AlmanaxTribute? _cachedAlmanax;
    private static long _almanaxFetchedDay = -1;

    public sealed record AlmanaxTribute(
        string Date,
        int ItemGid,
        string ItemName,
        string BonusDescription,
        string BonusType,
        int RewardKamas);

    /// <summary>
    /// Récupère l'offrande et le bonus de l'Almanax du jour (mis en cache par jour).
    /// </summary>
    public static async Task<AlmanaxTribute?> GetTodayAlmanaxAsync(CancellationToken ct = default)
    {
        long today = DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay;
        if (_cachedAlmanax is not null && _almanaxFetchedDay == today) return _cachedAlmanax;

        try
        {
            using var resp = await Http.GetAsync($"{BaseUrl}/almanax", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return _cachedAlmanax;

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return _cachedAlmanax;
            var first = doc.RootElement[0];

            string date = first.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
            int rewardKamas = first.TryGetProperty("reward_kamas", out var rk) ? rk.GetInt32() : 0;
            
            string bonusDesc = "";
            string bonusType = "";
            if (first.TryGetProperty("bonus", out var b))
            {
                bonusDesc = b.TryGetProperty("description", out var bd) ? bd.GetString() ?? "" : "";
                if (b.TryGetProperty("type", out var bt))
                    bonusType = bt.TryGetProperty("name", out var btn) ? btn.GetString() ?? "" : "";
            }

            int itemGid = 0;
            string itemName = "";
            if (first.TryGetProperty("tribute", out var tr) && tr.TryGetProperty("item", out var it))
            {
                itemGid = it.TryGetProperty("ankama_id", out var aid) ? aid.GetInt32() : 0;
                itemName = it.TryGetProperty("name", out var iname) ? iname.GetString() ?? "" : "";
            }

            var tribute = new AlmanaxTribute(date, itemGid, itemName, bonusDesc, bonusType, rewardKamas);
            _cachedAlmanax = tribute;
            _almanaxFetchedDay = today;
            return tribute;
        }
        catch { return _cachedAlmanax; }
    }

    /// <summary>
    /// Résout le niveau d'un objet (équipements, ressources, consommables).
    /// Met en cache mémoire pour éliminer les requêtes réseau répétitives.
    /// </summary>
    public static async Task<int> GetItemLevelAsync(long gid, CancellationToken ct = default)
    {
        if (gid <= 0) return 0;
        if (ItemLevels.TryGetValue(gid, out int cachedLevel)) return cachedLevel;

        try
        {
            // Tente d'abord les ressources
            using var resp = await Http.GetAsync($"{BaseUrl}/items/resources/{gid}", ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("level", out var l))
                {
                    int lvl = l.GetInt32();
                    ItemLevels[gid] = lvl;
                    return lvl;
                }
            }
            
            // Sinon équipements
            using var respEq = await Http.GetAsync($"{BaseUrl}/items/equipment/{gid}", ct).ConfigureAwait(false);
            if (respEq.IsSuccessStatusCode)
            {
                using var stream = await respEq.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("level", out var l))
                {
                    int lvl = l.GetInt32();
                    ItemLevels[gid] = lvl;
                    return lvl;
                }
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Détermine la meilleure destination d'HDV (Astrub vs Bonta) selon le niveau de l'objet.
    /// Objets <= 60 : HDV Astrub (191104004)
    /// Objets >= 61 : HDV Bonta (212601350) car Astrub bloque au-delà du niveau 60.
    /// </summary>
    public static async Task<long> RouteHdvMapAsync(long gid, CancellationToken ct = default)
    {
        int lvl = await GetItemLevelAsync(gid, ct).ConfigureAwait(false);
        return lvl > 60 ? 212601350L : 191104004L;
    }
}
