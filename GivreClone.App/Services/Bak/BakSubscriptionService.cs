using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Bak;

public enum BakProfitability
{
    VeryProfitable,
    Normal,
    Expensive
}

public static class BakSubscriptionService
{
    private static readonly object _gate = new();
    private static readonly string StoragePath = Path.Combine(MZP19LocalClone.DataRoot, "bak-rates-history.json");

    public const int ActionBuyOgrines = 6;
    public const int DefaultRate = 548;
    public const int Ogrines7Days = 1400;
    public const int Ogrines30Days = 4000;

    private static readonly ConcurrentDictionary<int, int> ServerRates = new()
    {
        [351] = 548,
        [354] = 620,
        [353] = 610,
        [300] = 1350
    };

    private static readonly ConcurrentDictionary<string, long> SubscriptionExpirationUtc = new();
    private static readonly List<int> RateHistory = new();
    private static int _currentRate = DefaultRate;

    static BakSubscriptionService()
    {
        LoadHistory();
    }

    public static int GetRateForServer(int serverId) =>
        ServerRates.TryGetValue(serverId, out int r) && r > 0 ? r : CurrentRate;

    public static void SetServerRate(int serverId, int rate)
    {
        if (serverId <= 0 || rate <= 0) return;
        ServerRates[serverId] = rate;
        RecordRate(rate);
    }

    public static int CurrentRate
    {
        get => _currentRate;
        set
        {
            if (value <= 0) return;
            _currentRate = value;
            RecordRate(value);
        }
    }

    public static long Cost7Days => (long)CurrentRate * Ogrines7Days;
    public static long Cost30Days => (long)CurrentRate * Ogrines30Days;

    public static BakProfitability EvaluateProfitability(int rate, int serverId = 0)
    {
        int avg = GetAverageHistoricalRate(serverId);
        if (rate <= avg * 0.92) return BakProfitability.VeryProfitable;
        if (rate >= avg * 1.08) return BakProfitability.Expensive;
        return BakProfitability.Normal;
    }

    public static int GetAverageHistoricalRate(int serverId = 0)
    {
        int baseRate = GetRateForServer(serverId);
        lock (_gate)
        {
            if (RateHistory.Count == 0) return baseRate;
            return (int)RateHistory.Average();
        }
    }

    public static void RecordRate(int rate)
    {
        lock (_gate)
        {
            RateHistory.Add(rate);
            if (RateHistory.Count > 500) RateHistory.RemoveAt(0);
            SaveHistory();
        }
    }

    public static void UpdateExpiration(string characterName, long expirationTimestampMs)
    {
        if (string.IsNullOrEmpty(characterName) || expirationTimestampMs <= 0) return;
        SubscriptionExpirationUtc[characterName] = expirationTimestampMs;
    }

    public static (TimeSpan Remaining, DateTime ExpirationDate, bool IsSubscribed) GetSubscriptionStatus(string characterName)
    {
        if (string.IsNullOrEmpty(characterName) || !SubscriptionExpirationUtc.TryGetValue(characterName, out long expMs))
            return (TimeSpan.Zero, DateTime.MinValue, false);

        var expDate = DateTimeOffset.FromUnixTimeMilliseconds(expMs).UtcDateTime;
        var diff = expDate - DateTime.UtcNow;
        bool active = diff > TimeSpan.Zero;
        return (active ? diff : TimeSpan.Zero, expDate, active);
    }

    public static byte[] BuildBakBufferListFrame(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Array.Empty<byte>();
        byte[] gm = EnvelopeBuilder.BuildClientRequest(code, Array.Empty<ProtoField>(), uid: -1);
        return RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
    }

    public static byte[] BuildBakActionFrame(string code, int rate, int ogrinesAmount, int actionType = ActionBuyOgrines)
    {
        if (string.IsNullOrWhiteSpace(code) || rate <= 0 || ogrinesAmount <= 0) return Array.Empty<byte>();
        long totalKamas = (long)rate * ogrinesAmount;
        var fields = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Varint, Varint = (ulong)(uint)actionType },
            new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)rate },
            new() { Field = 3, Wire = WireType.Varint, Varint = (ulong)ogrinesAmount },
            new() { Field = 4, Wire = WireType.Varint, Varint = (ulong)totalKamas },
        };
        byte[] gm = EnvelopeBuilder.BuildClientRequest(code, fields, uid: -1);
        return RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
    }

    public static byte[] BuildBakValidationFrame(string code, string transactionUuid)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(transactionUuid)) return Array.Empty<byte>();
        var fields = new List<ProtoField>
        {
            new() { Field = 2, Wire = WireType.Len, String = transactionUuid },
        };
        byte[] gm = EnvelopeBuilder.BuildClientRequest(code, fields, uid: -1);
        return RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
    }

    private static void LoadHistory()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(StoragePath))
                {
                    var data = JsonSerializer.Deserialize<List<int>>(File.ReadAllText(StoragePath));
                    if (data is not null)
                    {
                        RateHistory.Clear();
                        RateHistory.AddRange(data);
                    }
                }
            }
            catch { }
        }
    }

    private static void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(RateHistory));
        }
        catch { }
    }
}
