using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.Services.UnifiedStock;

/// <summary>
/// Read-only, fail-closed stock witness. PacketDecoded fires BEFORE LaunchService
/// applies inventories. A packet alone therefore must NEVER publish stock:
/// the first admitted snapshot is promoted only when the corresponding
/// post-store HdvUpdated / BankUpdated signal has arrived and the store matches.
/// No client, bank interaction, API call, or synthetic receipt is produced.
/// </summary>
public sealed class LiveStockEvidence
{
    private sealed record Seed(long CharacterId, DateTimeOffset At, string Version);
    private sealed record SnapshotItem(int Uid, long Gid, int Qty, int Position);
    private sealed record Pending(Seed Seed, IReadOnlyList<SnapshotItem> Expected);

    private readonly ConcurrentDictionary<uint, Seed> _bags = new();
    private readonly ConcurrentDictionary<uint, Seed> _banks = new();
    private readonly ConcurrentDictionary<uint, Pending> _waitingBag = new();
    private readonly ConcurrentDictionary<uint, Pending> _waitingBank = new();
    private readonly object _attachGate = new();
    // Publication / invalidation linearizables across packet, waiter and UI threads.
    private readonly object _seedGate = new();
    private LaunchService? _launch;

    public void Attach(LaunchService launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        lock (_attachGate)
        {
            if (_launch is not null)
            {
                if (!ReferenceEquals(_launch, launch))
                    throw new InvalidOperationException("Stock observer cannot change launch owner.");
                return;
            }
            _launch = launch;
            // Post-store signals: InventoryContentEvent -> Inventory.Seed -> HdvUpdated;
            // StorageInventoryContentEvent -> ApplyStorageContent -> BankUpdated.
            launch.HdvUpdated += OnBagStoreUpdated;
            launch.BankUpdated += OnBankStoreUpdated;
            launch.GameLinkLosing += OnLinkLosing;
            launch.GameLinkLost += OnLinkLost;
            launch.GameLinkRestored += Clear;
            launch.PacketDecoded += OnPacket;
        }
    }

    private void Clear(uint pid)
    {
        lock (_seedGate)
        {
            _bags.TryRemove(pid, out _);
            _banks.TryRemove(pid, out _);
            _waitingBag.TryRemove(pid, out _);
            _waitingBank.TryRemove(pid, out _);
        }
    }
    private void OnLinkLosing(uint pid, string _) => Clear(pid);
    private void OnLinkLost(uint pid, string _, bool __) => Clear(pid);

    private static IReadOnlyList<SnapshotItem>? DecodeBag(IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        // Uses the *same* data-driven InventoryReader mapping as Inventory.Seed.
        var items = InventoryReader.ReadContent(payload);
        // If nested messages exist but NONE decode as inventory items, a schema
        // drift is indistinguishable from an empty snapshot. Fail closed.
        if (payload.Count(f => f.Message is not null) != items.Count) return null;
        return Validate(items);
    }

    private static IReadOnlyList<SnapshotItem>? DecodeBank(IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        // LaunchService's StorageInventoryContentEvent.objects field is private
        // and versioned. InventoryReader.ReadContentAt uses the mapped nested
        // ObjectItemInventory schema. Find the one recognized repeated field;
        // any ambiguity or undecodable nested content is UNKNOWN, not zero stock.
        var candidates = payload.Where(f => f.Message is not null)
            .Select(f => f.Field).Distinct().ToArray();
        var readable = new List<(int Field, IReadOnlyList<InventoryReader.Item> Items)>();
        foreach (int field in candidates)
        {
            var entries = InventoryReader.ReadContentAt(payload, field);
            if (entries.Count > 0) readable.Add((field, entries));
        }
        if (readable.Count > 1 || (readable.Count == 0 && candidates.Length != 0))
            return null;
        if (readable.Count == 1 &&
            payload.Count(f => f.Field == readable[0].Field && f.Message is not null) !=
            readable[0].Items.Count) return null;
        return Validate(readable.Count == 0 ? Array.Empty<InventoryReader.Item>() : readable[0].Items);
    }

    private static IReadOnlyList<SnapshotItem>? Validate(IReadOnlyList<InventoryReader.Item> source)
    {
        if (source.Count > 10000) return null;
        var uids = new HashSet<int>();
        var result = new List<SnapshotItem>(source.Count);
        foreach (var item in source)
        {
            if (item.Uid <= 0 || item.Gid <= 0 || item.Qty < 0 ||
                !uids.Add(item.Uid)) return null;
            result.Add(new(item.Uid, item.Gid, item.Qty, item.Position));
        }
        return result.OrderBy(x => x.Uid).ToArray();
    }

    private static bool Matches(IReadOnlyList<SnapshotItem> expected,
        IReadOnlyList<InventoryStore.Item> observed)
    {
        if (expected.Count != observed.Count || observed.Count > 10000) return false;
        var ordered = observed.OrderBy(i => i.Uid).ToArray();
        for (int i = 0; i < ordered.Length; i++)
            if (expected[i].Uid != ordered[i].Uid || expected[i].Gid != ordered[i].Gid ||
                expected[i].Qty != ordered[i].Qty || expected[i].Position != ordered[i].Position)
                return false;
        return true;
    }

    private bool Current(LaunchService launch, uint pid, Seed seed)
        => seed.CharacterId > 0 && launch.CharacterId(pid) == seed.CharacterId &&
           !launch.IsLinkSuspended(pid) &&
           !string.IsNullOrWhiteSpace(launch.LotMGameVersion) &&
           string.Equals(launch.LotMGameVersion, seed.Version, StringComparison.Ordinal);

    private void OnBagStoreUpdated(uint pid)
    {
        var launch = _launch;
        if (launch is null) return;
        lock (_seedGate)
        {
            if (!_waitingBag.TryRemove(pid, out var pending) ||
                !Current(launch, pid, pending.Seed)) return;
            // Later incremental events cannot validate a failed full snapshot:
            // the next decoded server packet always clears the pending marker.
            if (Matches(pending.Expected, launch.Inventory.Items(pid)))
                _bags[pid] = pending.Seed;
        }
    }

    private void OnBankStoreUpdated(uint pid)
    {
        var launch = _launch;
        if (launch is null) return;
        lock (_seedGate)
        {
            if (!_waitingBank.TryRemove(pid, out var pending) ||
                !Current(launch, pid, pending.Seed) || launch.ChestSessionActive(pid) ||
                launch.InHavenBag(pid) ||
                !launch.StorageOpen(pid)) return;
            // The BankOpenAsync waiter may seed the bank on another thread.
            // Store equality admits either legitimate post-apply ordering.
            if (Matches(pending.Expected, launch.Bank.Items(pid)))
                _banks[pid] = pending.Seed;
        }
    }

    private void OnPacket(DecodedPacket packet)
    {
        var launch = _launch;
        if (launch is null) return;
        lock (_seedGate)
        {
        // Character selection is a session boundary even before new CharacterId
        // is published. A disconnected/recycled PID cannot reuse old seeds.
        if (packet.Name is "AuthenticationTicketAcceptedEvent" or
            "CharacterSelectionRequest" or "CharacterSelectionEvent")
        {
            Clear(packet.Pid);
            return;
        }
        // Cancel stale pending observations on the next packet. This prevents a
        // later, unrelated BankUpdated/HdvUpdated from promoting an old packet.
        if (packet.ClientToServer) return;
        _waitingBag.TryRemove(packet.Pid, out _);
        _waitingBank.TryRemove(packet.Pid, out _);
        if (launch.IsLinkSuspended(packet.Pid)) return;
        long character = launch.CharacterId(packet.Pid);
        if (character <= 0 || string.IsNullOrWhiteSpace(launch.LotMGameVersion)) return;
        var seed = new Seed(character, DateTimeOffset.UtcNow, launch.LotMGameVersion);
        if (packet.Name == "InventoryContentEvent")
        {
            _bags.TryRemove(packet.Pid, out _);
            _banks.TryRemove(packet.Pid, out _);
            if (DecodeBag(packet.Payload) is { } expected)
                _waitingBag[packet.Pid] = new Pending(seed, expected);
        }
        else if (packet.Name == "StorageInventoryContentEvent")
        {
            // Locked house chests emit Storage* too, but LaunchService intentionally
            // refuses to apply them to Bank. Never upgrade bank evidence on one.
            _banks.TryRemove(packet.Pid, out _);
            if (!launch.ChestSessionActive(packet.Pid) && !launch.InHavenBag(packet.Pid) &&
                DecodeBank(packet.Payload) is { } expected)
                _waitingBank[packet.Pid] = new Pending(seed, expected);
        }
        }
    }

    public IReadOnlyList<StockObservation> Read(
        uint pid, string accountKey, string exactGameVersion, DateTimeOffset now,
        TimeSpan? maxAge = null)
    {
        lock (_seedGate)
            return ReadCore(pid, accountKey, exactGameVersion, now, maxAge);
    }

    private IReadOnlyList<StockObservation> ReadCore(
        uint pid, string accountKey, string exactGameVersion, DateTimeOffset now,
        TimeSpan? maxAge)
    {
        var launch = _launch;
        if (pid == 0 || accountKey is not { Length: > 0 and <= 100 } ||
            string.IsNullOrWhiteSpace(accountKey) || string.IsNullOrWhiteSpace(exactGameVersion) ||
            launch is null || launch.IsLinkSuspended(pid)) return Array.Empty<StockObservation>();
        // Never stamp the caller-provided game version onto evidence observed
        // under a DIFFERENT runtime version.
        if (!string.Equals(launch.LotMGameVersion, exactGameVersion, StringComparison.Ordinal))
            return Array.Empty<StockObservation>();
        long owner = launch.CharacterId(pid);
        if (owner <= 0) return Array.Empty<StockObservation>();
        var age = maxAge ?? TimeSpan.FromMinutes(5);
        if (age <= TimeSpan.Zero || age > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        var records = new List<StockObservation>(2);
        if (_bags.TryGetValue(pid, out var bag) && bag.CharacterId == owner &&
            bag.Version == exactGameVersion && now >= bag.At && now - bag.At <= age)
        {
            var items = launch.Inventory.Items(pid);
            // Invalid UIDs, negative counts or duplicate stack instances must
            // invalidate the WHOLE observation, never quietly filter entries.
            if (items.Count <= 10000 && items.All(i => i.Uid > 0 && i.Gid > 0 && i.Qty >= 0) &&
                items.Select(i => i.Uid).Distinct().Count() == items.Count)
                records.Add(new StockObservation(accountKey, owner, StockLocation.Inventory,
                    $"bag:{owner}", $"bag-seed-{pid}", bag.At, bag.Version,
                    StockConfidence.Fresh, true,
                    items.Select(i => new StockStack(i.Gid, $"uid:{i.Uid}", i.Qty,
                        Reserved: i.Position != 63)).ToArray()));
        }
        if (_banks.TryGetValue(pid, out var bank) && bank.CharacterId == owner &&
            bank.Version == exactGameVersion && now >= bank.At && now - bank.At <= age &&
            launch.StorageOpen(pid) && !launch.ChestSessionActive(pid) &&
            !launch.InHavenBag(pid))
        {
            var items = launch.Bank.Items(pid);
            if (items.Count <= 10000 && items.All(i => i.Uid > 0 && i.Gid > 0 && i.Qty >= 0) &&
                items.Select(i => i.Uid).Distinct().Count() == items.Count)
                records.Add(new StockObservation(accountKey, owner, StockLocation.Bank,
                    $"bank:{accountKey}", $"bank-seed-{pid}", bank.At, bank.Version,
                    StockConfidence.Fresh, true,
                    items.Select(i => new StockStack(i.Gid, $"uid:{i.Uid}", i.Qty)).ToArray()));
        }
        // No independent Havre-Sac storage snapshot exists.
        return records;
    }
}
