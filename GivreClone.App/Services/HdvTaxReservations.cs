using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GivreClone.App.Services;

/// <summary>
/// Conservative HDV fee reservations. The durable order path appends a
/// checksummed intent and Flush(true)s it BEFORE the caller may send.
/// No receipt identifies a paid fee by order: entries are never released
/// on a global kamas delta, failed send, timeout or process restart.
/// </summary>
public enum HdvTaxOrderReservation
{
    Reserved,
    AlreadyReserved,
    CapExceeded,
    Unavailable,
    NeedsReconciliation
}

/// <summary>Local intent/dispatch lifecycle, not a server-issued tax receipt.</summary>
public enum HdvTaxOrderPhase { Reserved = 0, Attempted = 1, Observed = 2, Uncertain = 3 }

public enum HdvTaxProfileRecoveryStatus { Ready, NeedsReconciliation, Unavailable }

public readonly record struct HdvTaxProfileSnapshot(long ReservedFees,
    HdvTaxProfileRecoveryStatus RecoveryStatus);

/// <summary>
/// A missing or corrupt journal cannot be repaired automatically by erasing
/// or guessing reservations. Legacy in-memory TryReserve remains for callers
/// without an order identity; real capped sends use TryReserveOrder.
/// </summary>
public sealed class HdvTaxReservations
{
    private readonly object _gate = new();
    private readonly Dictionary<(uint Pid, string Profile), long> _pending = new();
    private readonly HashSet<(string Profile, string Order)> _memoryOrders = new();
    private readonly string? _journalPath;
    // Local bookkeeping only; never used as a server session authority.
    private readonly string _localRunToken = Guid.NewGuid().ToString("N");
    private const string Header = "GIVRE-HDV-TAX-RESERVATIONS-v1\n";
    private const string AnchorPrefix = "GIVRE-HDV-TAX-ANCHOR-v1|";

    public HdvTaxReservations(string? journalPath = null)
        => _journalPath = journalPath is null ? null : Path.GetFullPath(journalPath);

    public long Pending(uint pid, string? profile)
    {
        if (pid == 0 || string.IsNullOrWhiteSpace(profile)) return 0;
        lock (_gate)
            return _pending.TryGetValue((pid, profile), out long amount) ? amount : 0;
    }

    public bool TryReserve(uint pid, string? profile, long cap, long observedSpent, long fee)
    {
        if (cap < 0) return false;
        if (cap == 0) return HdvTaxBudget.CanReserve(0, observedSpent, 0, fee);
        // Never attribute capped costs to an unidentified character/profile.
        if (pid == 0 || string.IsNullOrWhiteSpace(profile) || profile == "?") return false;
        lock (_gate)
        {
            var key = (pid, profile);
            long pending = _pending.TryGetValue(key, out var amount) ? amount : 0;
            if (!HdvTaxBudget.CanReserve(cap, observedSpent, pending, fee)) return false;
            _pending[key] = checked(pending + fee);
            return true;
        }
    }

    /// <summary>
    /// Repeated identical order identity is refused, not sent twice and not
    /// billed twice. Different stock quantity/UID or reprice UID denotes a
    /// new local intent. The profile MUST include a server identity.
    /// </summary>
    public HdvTaxOrderReservation TryReserveOrder(uint pid, string? profile,
        string? orderKey, long cap, long observedSpent, long fee)
    {
        if (cap < 0 || pid == 0 || !ValidScope(profile)
            || string.IsNullOrWhiteSpace(orderKey) || orderKey.Length > 512
            || fee <= 0 || observedSpent < 0)
            return HdvTaxOrderReservation.Unavailable;
        lock (_gate)
        {
            if (_journalPath is null)
            {
                long prior = _pending.Where(x => x.Key.Profile == profile).Aggregate(
                    0L, (sum, entry) => SaturatingAdd(sum, entry.Value));
                if (_memoryOrders.Contains((profile!, orderKey)))
                    return HdvTaxOrderReservation.AlreadyReserved;
                if (!HdvTaxBudget.CanReserve(cap, observedSpent, prior, fee))
                    return HdvTaxOrderReservation.CapExceeded;
                _memoryOrders.Add((profile!, orderKey));
                var key = (pid, profile!);
                _pending[key] = SaturatingAdd(
                    _pending.TryGetValue(key, out long existing) ? existing : 0, fee);
                return HdvTaxOrderReservation.Reserved;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
                // Exclusive for the entire read -> check -> append -> flush.
                // Other app processes on the same DataRoot cannot race a cap.
                using var stream = OpenExclusive(_journalPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite);
                string anchorPath = _journalPath + ".anchor";
                bool hasAnchor = File.Exists(anchorPath);
                // A missing anchor with a populated journal, or a present
                // anchor with an EMPTY journal, indicates missing data/crash.
                if (hasAnchor != (stream.Length > 0)) return HdvTaxOrderReservation.Unavailable;
                var state = ReadJournal(stream);
                if (state is null) return HdvTaxOrderReservation.Unavailable;
                if (hasAnchor && !ValidAnchor(anchorPath, state))
                    return HdvTaxOrderReservation.Unavailable;
                if (state.Orders.Contains((profile!, orderKey)))
                    return HdvTaxOrderReservation.AlreadyReserved;
                // A dispatch that has not been uniquely observed blocks new
                // mutations even in unlimited-tax mode, across all processes.
                // A completed, attributable listing ACK still retains its fee.
                if (state.Phases.Any(x => x.Key.Profile == profile &&
                    x.Value is HdvTaxOrderPhase.Attempted or HdvTaxOrderPhase.Uncertain))
                    return HdvTaxOrderReservation.NeedsReconciliation;
                long reserved = state.Totals.TryGetValue(profile!, out long amount) ? amount : 0;
                if (!HdvTaxBudget.CanReserve(cap, observedSpent, reserved, fee))
                    return HdvTaxOrderReservation.CapExceeded;

                // The persistent anchor is established and fsynced BEFORE
                // the first order entry. If interrupted before that entry
                // is durable, recovery sees anchor+empty journal and blocks.
                if (!hasAnchor)
                {
                    using var anchor = new FileStream(anchorPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                    anchor.Write(Encoding.UTF8.GetBytes(AnchorFor(state)));
                    anchor.Flush(flushToDisk: true);
                }
                string record = EncodeRecord(profile!, orderKey, pid, fee, _localRunToken);
                AppendCommitted(stream, anchorPath, state, record);
                return HdvTaxOrderReservation.Reserved;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                // Any uncertain write must prevent an action, including for
                // this process; never retry with an empty in-memory ledger.
                return HdvTaxOrderReservation.Unavailable;
            }
        }
    }

    /// <summary>
    /// Attempted MUST be durable before invoking the network sender.
    /// Observed is permitted only when an upstream caller already validated a
    /// unique UID/quantity/price server outcome. No phase ever releases tax.
    /// Uncertain is terminal until explicit reconciliation outside this API.
    /// </summary>
    public bool TryAdvanceOrder(uint pid, string? profile, string? orderKey, HdvTaxOrderPhase next)
    {
        if (_journalPath is null || pid == 0 || !ValidScope(profile) ||
            string.IsNullOrWhiteSpace(orderKey) || orderKey.Length > 512 ||
            next is not (HdvTaxOrderPhase.Attempted or HdvTaxOrderPhase.Observed
                or HdvTaxOrderPhase.Uncertain))
            return false;
        lock (_gate)
            try
            {
                using var stream = OpenExclusive(_journalPath, FileMode.Open, FileAccess.ReadWrite);
                string anchorPath = _journalPath + ".anchor";
                var state = ReadJournal(stream);
                if (state is null || !ValidAnchor(anchorPath, state) ||
                    !state.Phases.TryGetValue((profile!, orderKey!), out var prior) ||
                    !state.Pids.TryGetValue((profile!, orderKey!), out uint owner) || owner != pid ||
                    next == HdvTaxOrderPhase.Attempted && prior != HdvTaxOrderPhase.Reserved ||
                    next != HdvTaxOrderPhase.Attempted && prior != HdvTaxOrderPhase.Attempted)
                    return false;
                string record = EncodePhase(profile!, orderKey!, pid, next, _localRunToken);
                AppendCommitted(stream, anchorPath, state, record);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                return false;
            }
    }

    public HdvTaxOrderPhase? ReadOrderPhase(string? profile, string? orderKey)
    {
        if (_journalPath is null || !ValidScope(profile) || string.IsNullOrWhiteSpace(orderKey))
            return null;
        lock (_gate)
            try
            {
                using var stream = OpenExclusive(_journalPath, FileMode.Open, FileAccess.Read);
                var state = ReadJournal(stream);
                return state is not null && ValidAnchor(_journalPath + ".anchor", state)
                    && state.Phases.TryGetValue((profile!, orderKey!), out var phase) ? phase : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public long PendingDurable(string? profile)
        => ReadProfileSnapshot(profile).ReservedFees;

    /// <summary>
    /// One locked/read-only transaction status for the classic HDV preview.
    /// This exposes local reconciliation needs, not a tax receipt. An
    /// unreadable journal is never presented as a zero reservation balance.
    /// </summary>
    public HdvTaxProfileSnapshot ReadProfileSnapshot(string? profile)
    {
        if (!ValidScope(profile))
            return new(long.MaxValue, HdvTaxProfileRecoveryStatus.Unavailable);
        if (_journalPath is null)
            lock (_gate)
                return new(_pending.Where(x => x.Key.Profile == profile)
                    .Aggregate(0L, (sum, entry) => SaturatingAdd(sum, entry.Value)),
                    HdvTaxProfileRecoveryStatus.Ready);
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_journalPath))
                    return File.Exists(_journalPath + ".anchor")
                        ? new(long.MaxValue, HdvTaxProfileRecoveryStatus.Unavailable)
                        : new(0, HdvTaxProfileRecoveryStatus.Ready);
                using var stream = OpenExclusive(_journalPath, FileMode.Open,
                    FileAccess.Read);
                var state = ReadJournal(stream);
                if (state is null || !ValidAnchor(_journalPath + ".anchor", state))
                    return new(long.MaxValue, HdvTaxProfileRecoveryStatus.Unavailable);
                long fees = state.Totals.TryGetValue(profile!, out long amount) ? amount : 0;
                var status = state.Phases.Any(x => x.Key.Profile == profile &&
                    x.Value is HdvTaxOrderPhase.Attempted or HdvTaxOrderPhase.Uncertain)
                    ? HdvTaxProfileRecoveryStatus.NeedsReconciliation
                    : HdvTaxProfileRecoveryStatus.Ready;
                return new(fees, status);
            }
            catch (IOException) { return new(long.MaxValue, HdvTaxProfileRecoveryStatus.Unavailable); }
            catch (UnauthorizedAccessException) { return new(long.MaxValue, HdvTaxProfileRecoveryStatus.Unavailable); }
        }
    }

    private static bool ValidScope(string? profile)
        => !string.IsNullOrWhiteSpace(profile) && profile.StartsWith("server:",
            StringComparison.Ordinal) && profile.Length <= 256 && profile.Split(':').Length >= 3;

    private static long SaturatingAdd(long a, long b) =>
        a >= long.MaxValue - b ? long.MaxValue : a + b;

    private static FileStream OpenExclusive(string path, FileMode mode, FileAccess access)
    {
        for (int retry = 0; ; retry++)
            try { return new FileStream(path, mode, access, FileShare.None); }
            catch (IOException e) when (retry < 30 && (e.HResult & 0xFFFF) is 32 or 33)
            {
                // Contention from another authorized instance; bounded retry.
                // After exhaustion refuse the operation, never bypass the journal.
                Thread.Sleep(10);
            }
    }

    private static string AnchorFor(State state)
        => AnchorPrefix + state.Count.ToString(CultureInfo.InvariantCulture)
           + "|" + state.TailHash + "\n";

    private static bool ValidAnchor(string path, State state)
    {
        try { return File.ReadAllText(path, new UTF8Encoding(false, true)) == AnchorFor(state); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or DecoderFallbackException) { return false; }
    }

    private sealed class State
    {
        public HashSet<(string Profile, string Order)> Orders { get; } = new();
        public Dictionary<string, long> Totals { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string Profile, string Order), HdvTaxOrderPhase> Phases { get; } = new();
        public Dictionary<(string Profile, string Order), uint> Pids { get; } = new();
        public int Count { get; set; }
        public string TailHash { get; set; } = "NONE";
    }

    private static void AppendCommitted(FileStream stream, string anchorPath, State state,
        string record)
    {
        stream.Position = stream.Length;
        byte[] bytes = Encoding.UTF8.GetBytes((stream.Length == 0 ? Header : "") + record);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        state.Count++;
        state.TailHash = record.Split('|')[^1].TrimEnd('\n');
        // AtomicJson.Write is atomic, but its File.WriteAllText(temp) doesn't
        // fsync. This is a transaction anchor, not a UI settings file: sync
        // its replacement bytes before the same-directory atomic rename.
        // Crash between journal fsync and this rename deliberately leaves a
        // mismatch and blocks all following mutations (never replays them).
        string staging = anchorPath + ".pending-" + Guid.NewGuid().ToString("N");
        using (var pending = new FileStream(staging, FileMode.CreateNew,
                   FileAccess.Write, FileShare.None))
        {
            byte[] anchorBytes = new UTF8Encoding(false).GetBytes(AnchorFor(state));
            pending.Write(anchorBytes);
            pending.Flush(flushToDisk: true);
        }
        File.Move(staging, anchorPath, overwrite: true);
    }

    private static State? ReadJournal(FileStream stream)
    {
        if (stream.Length == 0) return new State();
        if (stream.Length > 16 * 1024 * 1024) return null; // avoid unbounded recovery
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string content;
        try { content = reader.ReadToEnd(); }
        catch (DecoderFallbackException) { return null; }
        if (!content.StartsWith(Header, StringComparison.Ordinal)
            || !content.EndsWith('\n')) return null;
        var lines = content[Header.Length..].Split('\n');
        var state = new State();
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string line = lines[i];
            string[] fields = line.Split('|');
            if (fields.Length != 7 || fields[0] is not ("1" or "2")
                || !uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint pid)
                || pid == 0 || fields[5].Length != 32 || !fields[5].All(Uri.IsHexDigit))
                return null;
            var prefix = string.Join("|", fields.Take(6));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix)));
            if (!string.Equals(hash, fields[6], StringComparison.Ordinal)) return null;
            string profile, order;
            try
            {
                profile = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(fields[1]));
                order = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(fields[2]));
            }
            catch (Exception e) when (e is FormatException or DecoderFallbackException) { return null; }
            if (!ValidScope(profile) || string.IsNullOrWhiteSpace(order) || order.Length > 512)
                return null;
            var key = (profile, order);
            if (fields[0] == "1")
            {
                if (!long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture,
                        out long fee) || fee <= 0 || !state.Orders.Add(key))
                    return null;
                long prior = state.Totals.TryGetValue(profile, out long sum) ? sum : 0;
                // Historical MaxTaxSpend=0 is unlimited. Saturate the
                // informational sum rather than creating an accidental
                // monetary ceiling at Int64.MaxValue. A positive cap still
                // refuses new orders via CanReserve when totals saturate.
                state.Totals[profile] = SaturatingAdd(prior, fee);
                state.Phases[key] = HdvTaxOrderPhase.Reserved;
                state.Pids[key] = pid;
            }
            else
            {
                if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int rawPhase) ||
                    rawPhase is < 1 or > 3 ||
                    !state.Phases.TryGetValue(key, out var prior) ||
                    !state.Pids.TryGetValue(key, out uint owner) || owner != pid ||
                    rawPhase == 1 && prior != HdvTaxOrderPhase.Reserved ||
                    rawPhase != 1 && prior != HdvTaxOrderPhase.Attempted)
                    return null;
                state.Phases[key] = (HdvTaxOrderPhase)rawPhase;
            }
            state.Count++;
            state.TailHash = fields[6];
        }
        return state;
    }

    private static string EncodeRecord(string profile, string order, uint pid, long fee,
        string localRunToken)
    {
        string prefix = string.Join("|", "1",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(order)),
            pid.ToString(CultureInfo.InvariantCulture),
            fee.ToString(CultureInfo.InvariantCulture), localRunToken);
        return prefix + "|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix))) + "\n";
    }

    private static string EncodePhase(string profile, string order, uint pid,
        HdvTaxOrderPhase phase, string localRunToken)
    {
        string prefix = string.Join("|", "2",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(order)),
            pid.ToString(CultureInfo.InvariantCulture),
            ((int)phase).ToString(CultureInfo.InvariantCulture), localRunToken);
        return prefix + "|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix))) + "\n";
    }
}
