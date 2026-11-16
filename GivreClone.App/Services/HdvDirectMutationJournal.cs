using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GivreClone.App.Services;

public enum HdvDirectMutationDecision { Allowed, NeedsReview, Unavailable }
public enum HdvDirectMutationPhase { Started = 1, Confirmed = 2, Uncertain = 3, Reviewed = 4, Attempted = 5 }
public enum HdvDirectMutationReadState { Ready, IdentityMissing, StorageUnavailable }
public sealed record HdvDirectMutationHistoryRow(
    string Order, uint OriginalPid, HdvDirectMutationPhase Phase, Guid LocalRunId)
{
    public bool NeedsReview => Phase is HdvDirectMutationPhase.Started
        or HdvDirectMutationPhase.Attempted or HdvDirectMutationPhase.Uncertain;
    public string Kind => Order.Split(':', 2)[0];
}
public sealed record HdvDirectMutationSnapshot(
    string Profile, HdvDirectMutationReadState State,
    IReadOnlyList<HdvDirectMutationHistoryRow> Rows, string Reason)
{
    public bool HasPending => State != HdvDirectMutationReadState.Ready ||
                              Rows.Any(row => row.NeedsReview);
}
/// <summary>
/// Read-only exact full-history lookup (not limited by the classic UI's last
/// 32/128 visible orders). A Confirmed phase is a LOCAL correlated business
/// observation from the original sender, not a network ACK or production GID.
/// </summary>
public sealed record HdvDirectMutationExactV14(
    string Profile, string Order, HdvDirectMutationReadState State,
    HdvDirectMutationHistoryRow? Row, string Reason,
    bool HasDurableAttempt = false)
{
    public bool HasCorrelatedLocalResult =>
        State == HdvDirectMutationReadState.Ready &&
        HasDurableAttempt && Row is { Phase: HdvDirectMutationPhase.Confirmed };
    public bool IsServerAck => false;
    public bool CanAuthorizeReplay => false;
}

/// <summary>
/// Transactional V15 read-only view of one canonical order and its owner's
/// OTHER outstanding order, from one validated journal+anchor reconstruction.
/// No state here admits an outgoing mutation or grants an ACK/tax receipt.
/// SELL keys deliberately have no GID, decoder epoch or production caller.
/// </summary>
public enum HdvDirectTransactionStateV15
{
    IdentityInvalid, StorageUnavailable, SourceChanged, Unseen, OtherOrderPending,
    Started, Attempted, Uncertain, Reviewed,
    LocallyConfirmedWithAttempt, LegacyConfirmedWithoutAttempt, RunMismatch
}

public sealed record HdvDirectTransactionViewV15(
    string Profile, string Order, HdvDirectTransactionStateV15 State,
    HdvDirectMutationHistoryRow? Row, bool HasDurableAttempt,
    bool HasOtherPendingOrder, string Reason)
{
    public bool OriginalOrderRecorded => Row is not null;
    public bool RequiresOperatorInspection => State is
        HdvDirectTransactionStateV15.Started or HdvDirectTransactionStateV15.Attempted or
        HdvDirectTransactionStateV15.Uncertain;
    public bool HasLocallyConfirmedAttempt =>
        State == HdvDirectTransactionStateV15.LocallyConfirmedWithAttempt &&
        HasDurableAttempt && Row is { Phase: HdvDirectMutationPhase.Confirmed };
    public bool MayAuthorizeSend => false;
    public bool IsServerAck => false;
    public bool MayReleaseTax => false;
    public bool MayReplay => false;
}
public readonly record struct HdvDirectMutationLease(
    Guid RunId, string Profile, string Order, uint Pid);

/// <summary>
/// V17 local caller provenance, recorded inside the fsynced STARTED row before
/// its original sender can inject. It binds the bag GID and source snapshot to
/// the original local run; it is never a packet or server-issued receipt.
/// </summary>
public sealed record HdvProductionOriginV17(
    Guid CaptureId, long Gid, int BagUid, int Quantity, long Price,
    int ObservedStock, long DecoderEpoch, CaptureLinkIdentity? LinkedSource)
{
    public bool IsServerAck => false;
    public bool MayReleaseTax => false;
    public bool MayReplay => false;
}

/// <summary>
/// Read-only durable origin+phase from one journal/anchor reconstruction.
/// Legacy and independent UI/Bulk/Lua rows have no ProductionOrigin.
/// </summary>
public sealed record HdvProductionOriginalAttemptV17(
    string Profile, string Order, HdvDirectMutationReadState State,
    HdvDirectMutationHistoryRow? Row, HdvProductionOriginV17? ProductionOrigin,
    bool HasDurableAttempt, bool OtherOrderPending, string Reason)
{
    public bool RequiresManualInspection =>
        State != HdvDirectMutationReadState.Ready ||
        OtherOrderPending || Row is { NeedsReview: true } ||
        (Row is not null && ProductionOrigin is null);
    public bool HasOriginalProductionOrigin => ProductionOrigin is not null && Row is not null;
    public bool IsServerAck => false;
    public bool MayReleaseTax => false;
    public bool MayAutoReconcile => false;
    public bool MaySendOrReplay => false;
}

/// <summary>
/// A non-fiscal, append-only pre-send admission journal for the *actual*
/// seller dispatch. All callers, including callers already covered by the
/// separate tax ledger, pass this one gate. It never books or releases taxes,
/// and its local keys are NOT server order identifiers or ACKs.
/// </summary>
public sealed class HdvDirectMutationJournal : IDisposable
{
    private const string Header = "GIVRE-HDV-DIRECT-MUTATION-v1\n";
    private const string AnchorHeader = "GIVRE-HDV-DIRECT-ANCHOR-v1|";
    private const string Genesis = "GENESIS";
    private readonly object _gate = new();
    private readonly HashSet<Guid> _live = new();
    private readonly Dictionary<Guid, FileStream> _activeHandles = new();
    // The durable SELL key does not contain a production caller. A guided
    // ListAsync can only claim its own locally admitted attempt if TryBegin
    // ran in that call's ExecutionContext. A different UI/Bulk/Lua callback
    // may create the same exact SELL order, but cannot acquire this witness.
    // This volatile origin witness is NEVER an ACK or replay authority.
    private sealed record ProductionOrigin(Guid CaptureId, uint Pid, string Profile,
        string Order, HdvProductionOriginV17? DurableOrigin,
        Func<bool>? RemainsCurrent);
    private readonly AsyncLocal<ProductionOrigin?> _productionOrigin = new();
    private readonly Dictionary<Guid, (uint Pid, string Profile, string Order, Guid RunId)>
        _productionRuns = new();
    private readonly HashSet<Guid> _activeProductionCaptures = new();
    private readonly string _path;
    private bool _disposed;

    public HdvDirectMutationJournal(string path)
        => _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

    public static string Profile(int server, long character)
        => server > 0 && character > 0
            ? $"server:{server.ToString(CultureInfo.InvariantCulture)}:character:{character.ToString(CultureInfo.InvariantCulture)}"
            : "";

    private static bool ValidProfile(string? profile)
    {
        if (profile is null || profile.Length is < 20 or > 128) return false;
        string[] parts = profile.Split(':');
        return parts.Length == 4 && parts[0] == "server" && parts[2] == "character" &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int server) &&
               long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out long character) &&
               server > 0 && character > 0 && profile == Profile(server, character);
    }

    private static bool ValidOrder(string? order)
    {
        if (string.IsNullOrWhiteSpace(order) || order.Length > 512 ||
            !order.All(c => c is >= ' ' and <= '~' && c != '|')) return false;
        // V7 keys remain byte-for-byte compatible, but malformed or synthetic
        // prices/UIDs/stocks can no longer enter the durable admission gate.
        bool sell = order.StartsWith("sell:", StringComparison.Ordinal);
        bool reprice = order.StartsWith("reprice:", StringComparison.Ordinal);
        if (sell || reprice)
        {
            string[] parts = order.Split(':');
            if (parts.Length != (sell ? 5 : 4) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int listingUid) || listingUid <= 0 ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) || quantity <= 0 ||
                !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out long listingPrice) || listingPrice <= 0)
                return false;
            if (reprice) return order == HdvTaxOrderKeys.Reprice(listingUid, quantity, listingPrice);
            return int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int stock) &&
                   stock >= quantity && order == HdvTaxOrderKeys.Sell(listingUid, quantity, listingPrice, stock);
        }
        bool buy = order.StartsWith("buy:", StringComparison.Ordinal);
        if (!buy && !order.StartsWith("remove:", StringComparison.Ordinal)) return false;
        string[] fields = order.Split(':');
        if (fields.Length != 7 ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int uid) || uid <= 0 ||
            !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long gid) || gid <= 0 ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int qty) || qty <= 0 ||
            !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out long price) || price <= 0 ||
            !fields[6].StartsWith("epoch-", StringComparison.Ordinal) ||
            !long.TryParse(fields[6][6..], NumberStyles.None, CultureInfo.InvariantCulture, out long epoch) || epoch <= 0)
            return false;
        return buy
            ? qty is (1 or 10 or 100 or 1000) && order == BuyOrder(uid, gid, qty, price, epoch)
            : order == RemoveOrder(uid, gid, qty, price, epoch);
    }

    /// <summary>Local pre-send keys; the epoch is a decoder lifetime, never a server receipt.
    /// A buyer offer does not expose stock, so that absence is recorded explicitly.</summary>
    public static string BuyOrder(int offerUid, long gid, int quantity, long price, long decoderEpoch)
        => $"buy:{offerUid}:{gid}:{quantity}:{price}:stock-unknown:epoch-{decoderEpoch}";

    /// <summary>Capture the server-observed listing snapshot before withdrawing it.</summary>
    public static string RemoveOrder(int listingUid, long gid, int quantity, long price, long decoderEpoch)
        => $"remove:{listingUid}:{gid}:{quantity}:{price}:stock-{quantity}:epoch-{decoderEpoch}";

    private const string ProductionOriginTagV17 = "production-v17";

    private static bool ValidProductionOriginV17(HdvProductionOriginV17 origin,
        uint pid, string order)
    {
        if (origin.CaptureId == Guid.Empty || origin.Gid <= 0 ||
            origin.BagUid <= 0 || origin.Quantity <= 0 || origin.Price <= 0 ||
            origin.ObservedStock < origin.Quantity || origin.DecoderEpoch <= 0 ||
            order != HdvTaxOrderKeys.Sell(origin.BagUid, origin.Quantity,
                origin.Price, origin.ObservedStock))
            return false;
        if (origin.LinkedSource is not { } link) return true;
        return link.SourceInstanceId != Guid.Empty && link.Pid == pid &&
               link.RunEpoch > 0 && link.TunnelId > 0;
    }

    private static string FormatProductionOriginV17(HdvProductionOriginV17 origin)
    {
        var link = origin.LinkedSource;
        return string.Join(":", ProductionOriginTagV17, origin.CaptureId.ToString("N"),
            origin.Gid.ToString(CultureInfo.InvariantCulture),
            origin.BagUid.ToString(CultureInfo.InvariantCulture),
            origin.Quantity.ToString(CultureInfo.InvariantCulture),
            origin.Price.ToString(CultureInfo.InvariantCulture),
            origin.ObservedStock.ToString(CultureInfo.InvariantCulture),
            origin.DecoderEpoch.ToString(CultureInfo.InvariantCulture),
            (link?.SourceInstanceId ?? Guid.Empty).ToString("N"),
            (link?.Pid ?? 0).ToString(CultureInfo.InvariantCulture),
            (link?.RunEpoch ?? 0).ToString(CultureInfo.InvariantCulture),
            (link?.TunnelId ?? 0).ToString(CultureInfo.InvariantCulture),
            (link?.ConnectionToken ?? Guid.Empty).ToString("N"));
    }

    private static bool TryParseProductionOriginV17(string raw, uint pid,
        string order, out HdvProductionOriginV17? origin)
    {
        origin = null;
        string[] f = raw.Split(':');
        if (f.Length != 13 || f[0] != ProductionOriginTagV17 ||
            !Guid.TryParseExact(f[1], "N", out Guid captureId) ||
            !long.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out long gid) ||
            !int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out int uid) ||
            !int.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out int qty) ||
            !long.TryParse(f[5], NumberStyles.None, CultureInfo.InvariantCulture, out long price) ||
            !int.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out int stock) ||
            !long.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out long epoch) ||
            !Guid.TryParseExact(f[8], "N", out Guid sourceId) ||
            !uint.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out uint sourcePid) ||
            !long.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out long runEpoch) ||
            !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out long tunnelId) ||
            !Guid.TryParseExact(f[12], "N", out Guid connection))
            return false;
        bool noLink = sourceId == Guid.Empty && sourcePid == 0 &&
                      runEpoch == 0 && tunnelId == 0 && connection == Guid.Empty;
        var link = noLink ? (CaptureLinkIdentity?)null :
            new CaptureLinkIdentity(sourceId, sourcePid, runEpoch, tunnelId, connection);
        var decoded = new HdvProductionOriginV17(
            captureId, gid, uid, qty, price, stock, epoch, link);
        if (!ValidProductionOriginV17(decoded, pid, order) ||
            FormatProductionOriginV17(decoded) != raw)
            return false;
        origin = decoded;
        return true;
    }

    /// <summary>
    /// Pin one advisory production observation to the current asynchronous
    /// caller BEFORE it invokes the existing Lua→HdvSellAsync sender. This does
    /// not reserve a sale or bypass the durable per-profile pre-send gate.
    /// </summary>
    public void ObserveProductionCallV16(Guid captureId, uint pid, string? profile, string? order)
    {
        bool valid = captureId != Guid.Empty && pid != 0 &&
            ValidProfile(profile) && ValidOrder(order) &&
            order!.StartsWith("sell:", StringComparison.Ordinal);
        _productionOrigin.Value = valid
            ? new ProductionOrigin(captureId, pid, profile!, order!, null, null)
            : null;
        if (valid)
            lock (_gate)
            {
                if (_disposed)
                {
                    _productionOrigin.Value = null;
                    return;
                }
                if (_activeProductionCaptures.Count >= 1024)
                    _activeProductionCaptures.Clear();
                _activeProductionCaptures.Add(captureId);
            }
    }

    /// <summary>
    /// V17 origin from the one exact server-model bag stack captured before
    /// ListAsync. The V16 call context still directs the real sender to
    /// TryBegin, but a durable STARTED record now keeps its GID/source after a
    /// crash. A missing or mismatched identity cannot mint this metadata.
    /// </summary>
    public bool ObserveProductionCallV17(Guid captureId, uint pid, string? profile,
        string? order, long gid, int bagUid, int quantity, long price,
        int observedStock, long decoderEpoch, CaptureLinkIdentity? linkedSource,
        Func<bool>? remainsCurrent = null)
    {
        var origin = new HdvProductionOriginV17(captureId, gid, bagUid,
            quantity, price, observedStock, decoderEpoch, linkedSource);
        if (pid == 0 || !ValidProfile(profile) || !ValidOrder(order) ||
            !ValidProductionOriginV17(origin, pid, order!))
        {
            _productionOrigin.Value = null;
            return false;
        }
        lock (_gate)
        {
            if (_disposed) return false;
            if (_activeProductionCaptures.Count >= 1024)
                _activeProductionCaptures.Clear();
            _activeProductionCaptures.Add(captureId);
        }
        _productionOrigin.Value = new ProductionOrigin(captureId, pid, profile!,
            order!, origin, remainsCurrent);
        return true;
    }

    /// <summary>
    /// Consume the ONE local TryBegin run that executed in the original call
    /// context with the exact captured profile/PID/order. Absence is ambiguous:
    /// no other matching journal row can be attributed to that caller.
    /// </summary>
    public Guid ConsumeProductionCallRunV16(Guid captureId, uint pid,
        string? profile, string? order)
    {
        if (_productionOrigin.Value?.CaptureId == captureId)
            _productionOrigin.Value = null;
        lock (_gate)
        {
            _activeProductionCaptures.Remove(captureId);
            if (!_productionRuns.Remove(captureId, out var found)) return Guid.Empty;
            return found.Pid == pid && found.Profile == profile &&
                   found.Order == order ? found.RunId : Guid.Empty;
        }
    }

    /// <summary>
    /// Advisory read of the in-memory original-call attempt identity. The
    /// authoritative order phase must still be read from the durable journal.
    /// Nothing here survives restart or proves a response from the server.
    /// </summary>
    public Guid ReadProductionCallRunV16(Guid captureId, uint pid,
        string? profile, string? order)
    {
        lock (_gate)
        {
            return !_disposed && _productionRuns.TryGetValue(captureId, out var found) &&
                   found.Pid == pid && found.Profile == profile && found.Order == order
                ? found.RunId : Guid.Empty;
        }
    }

    /// <summary>
    /// Inspect the ORIGINAL production provenance after a process crash, or
    /// while another caller is active. The run and metadata are read under one
    /// journal lock and checked against the same durable checksum/anchor.
    /// </summary>
    public HdvProductionOriginalAttemptV17 ReadProductionOriginalAttemptV17(
        string? profile, string? exactOrder, Guid expectedRun = default,
        Guid expectedCapture = default)
    {
        string p = profile ?? "", order = exactOrder ?? "";
        HdvProductionOriginalAttemptV17 Verdict(HdvDirectMutationReadState state,
            string reason, HdvDirectMutationHistoryRow? row = null,
            HdvProductionOriginV17? origin = null, bool attempted = false,
            bool otherPending = false) =>
            new(p, order, state, row, origin, attempted, otherPending, reason);
        if (!ValidProfile(profile) || !ValidOrder(exactOrder))
            return Verdict(HdvDirectMutationReadState.IdentityMissing,
                "invalid exact profile/order");
        lock (_gate)
        {
            if (_disposed)
                return Verdict(HdvDirectMutationReadState.StorageUnavailable,
                    "journal disposed");
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.Read);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    EmptyWithPriorSenderWitness(state))
                    return Verdict(HdvDirectMutationReadState.StorageUnavailable,
                        "invalid journal checksum, phase or anchor");
                bool other = state.Pending.TryGetValue(p, out var pending) &&
                             pending.Order != order;
                var found = state.History.FirstOrDefault(x =>
                    x.Profile == p && x.Entry.Order == order);
                if (found.Entry is null)
                    return Verdict(HdvDirectMutationReadState.Ready,
                        "no locally durable exact order; remote absence unproven",
                        otherPending: other);
                var entry = found.Entry;
                if (expectedRun != Guid.Empty && expectedRun != entry.RunId)
                    return Verdict(HdvDirectMutationReadState.IdentityMissing,
                        "original run mismatch", otherPending: other);
                state.ProductionOrigins.TryGetValue(entry.RunId, out var origin);
                if (expectedCapture != Guid.Empty &&
                    origin?.CaptureId != expectedCapture)
                    return Verdict(HdvDirectMutationReadState.IdentityMissing,
                        "original capture was not the durable sender",
                        otherPending: other);
                return Verdict(HdvDirectMutationReadState.Ready,
                    "durable local origin only; server receipt and tax release unproven",
                    new(entry.Order, entry.Pid, entry.Phase, entry.RunId),
                    origin, state.AttemptedRuns.Contains(entry.RunId), other);
            }
            catch (FileNotFoundException)
            {
                return Verdict(MissingJournalHasDurableWitness()
                        ? HdvDirectMutationReadState.StorageUnavailable
                        : HdvDirectMutationReadState.Ready,
                    "journal absent; local and remote outcome unproven");
            }
            catch (DirectoryNotFoundException)
            { return Verdict(HdvDirectMutationReadState.StorageUnavailable,
                "journal parent unavailable"); }
            catch (Exception e) when (StorageError(e))
            { return Verdict(HdvDirectMutationReadState.StorageUnavailable,
                "journal unreadable (" + e.GetType().Name + ")"); }
        }
    }

    /// <summary>
    /// Bounded, read-only reconstruction of actual durable mutations for ONE
    /// profile. It shares the exact checksum-chain/anchor validator used by
    /// TryBegin, but cannot reserve, finish, review or resend an order.
    /// A failed read is never displayed as an empty, clean ledger.
    /// </summary>
    public HdvDirectMutationSnapshot ReadSnapshot(string? profile, int maxRows = 32)
    {
        if (!ValidProfile(profile))
            return new(profile ?? "", HdvDirectMutationReadState.IdentityMissing,
                Array.Empty<HdvDirectMutationHistoryRow>(),
                "server/character identity invalid; no negative clearance");
        if (maxRows is < 1 or > 128)
            return new(profile!, HdvDirectMutationReadState.StorageUnavailable,
                Array.Empty<HdvDirectMutationHistoryRow>(),
                "unbounded or invalid journal inspection request");
        lock (_gate)
        {
            HdvDirectMutationSnapshot Unavailable(string why)
                => new(profile!, HdvDirectMutationReadState.StorageUnavailable,
                    Array.Empty<HdvDirectMutationHistoryRow>(), why);
            if (_disposed) return Unavailable("journal disposed");
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.Read);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    EmptyWithPriorSenderWitness(state))
                    return Unavailable("checksum, anchor or journal state invalid");
                return new(profile!, HdvDirectMutationReadState.Ready,
                    state.History
                        .Where(x => x.Profile == profile)
                        .TakeLast(maxRows).Reverse()
                        .Select(x => new HdvDirectMutationHistoryRow(
                            x.Entry.Order, x.Entry.Pid,
                            x.Entry.Phase, x.Entry.RunId)).ToArray(),
                    "verified local checksum and anchor; no server ACK claimed");
            }
            catch (FileNotFoundException)
            {
                if (MissingJournalHasDurableWitness())
                    return Unavailable("journal missing but previous durable mutation witness exists");
                return new(profile!, HdvDirectMutationReadState.Ready,
                    Array.Empty<HdvDirectMutationHistoryRow>(),
                    "no local journal file; not proof that remote orders never existed");
            }
            catch (DirectoryNotFoundException)
            {
                // File.Exists masks inaccessible or absent parents as false.
                // A removed/unavailable journal root must never be displayed
                // as a verified empty order history in the classic UI.
                return Unavailable("journal parent directory missing or inaccessible");
            }
            catch (Exception e) when (StorageError(e))
            {
                return Unavailable("journal storage unavailable or unreadable (" +
                    e.GetType().Name + ")");
            }
        }
    }

    /// <summary>
    /// Exact lookup for a production/listing observer across ALL durable
    /// history. Does not mutate, clear, retry or join the separate tax ledger.
    /// An already-selected order can also pin its ORIGINAL local run. A
    /// pre/post observer that does not yet know the run must separately prove
    /// the exact order was ABSENT before dispatch, then validate the new run
    /// and original PID after return; matching UID/price alone is not proof.
    /// </summary>
    public HdvDirectMutationExactV14 ReadExactV14(string? profile,
        string? exactOrder, Guid originalRun = default)
    {
        string p = profile ?? "", order = exactOrder ?? "";
        if (!ValidProfile(profile) || !ValidOrder(exactOrder))
            return new(p, order, HdvDirectMutationReadState.IdentityMissing, null,
                "invalid profile/order, cannot claim a listing");
        lock (_gate)
        {
            HdvDirectMutationExactV14 Unavailable(string why)
                => new(p, order, HdvDirectMutationReadState.StorageUnavailable, null, why);
            if (_disposed) return Unavailable("journal disposed");
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.Read);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    EmptyWithPriorSenderWitness(state))
                    return Unavailable("journal checksum, anchor or phase invalid");
                var hit = state.History.FirstOrDefault(row =>
                    row.Profile == p && row.Entry.Order == order);
                return hit.Entry is null ||
                       originalRun != Guid.Empty && hit.Entry.RunId != originalRun
                    ? new(p, order, HdvDirectMutationReadState.Ready, null,
                        "no exact recorded profile/order/run; never infer a send")
                    : new(p, order, HdvDirectMutationReadState.Ready,
                        new(hit.Entry.Order, hit.Entry.Pid,
                            hit.Entry.Phase, hit.Entry.RunId),
                        "locally fsynced phase only, not a server ACK",
                        state.AttemptedRuns.Contains(hit.Entry.RunId));
            }
            catch (FileNotFoundException)
            {
                return MissingJournalHasDurableWitness()
                    ? Unavailable("journal missing with prior durable witness")
                    : new(p, order, HdvDirectMutationReadState.Ready, null,
                        "no local intent exists; not proof of server non-delivery");
            }
            catch (DirectoryNotFoundException)
            { return Unavailable("journal parent unavailable"); }
            catch (Exception e) when (StorageError(e))
            { return Unavailable("journal unreadable (" + e.GetType().Name + ")"); }
        }
    }

    /// <summary>
    /// Inspect the exact order, its original run and any profile-wide pending
    /// operation under ONE journal read. Unlike separate ReadExact+NeedsReview
    /// calls, the fields cannot describe two different durable generations.
    /// A later sender can still mutate the journal: this is NEVER a send gate.
    /// </summary>
    public HdvDirectTransactionViewV15 ReadTransactionV15(
        string? profile, string? exactOrder, Guid expectedRun = default)
    {
        string p = profile ?? "", order = exactOrder ?? "";
        HdvDirectTransactionViewV15 Verdict(HdvDirectTransactionStateV15 status,
            string reason, HdvDirectMutationHistoryRow? row = null,
            bool attempted = false, bool otherPending = false)
            => new(p, order, status, row, attempted, otherPending, reason);
        if (!ValidProfile(profile) || !ValidOrder(exactOrder))
            return Verdict(HdvDirectTransactionStateV15.IdentityInvalid,
                "invalid canonical profile/order, not a transaction receipt");
        lock (_gate)
        {
            if (_disposed)
                return Verdict(HdvDirectTransactionStateV15.StorageUnavailable,
                    "journal disposed, no negative admission");
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.Read);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    EmptyWithPriorSenderWitness(state))
                    return Verdict(HdvDirectTransactionStateV15.StorageUnavailable,
                        "journal checksum/phase or anchor invalid");
                bool pendingOther = state.Pending.TryGetValue(p, out var pending) &&
                                    pending.Order != order;
                var found = state.History.FirstOrDefault(x =>
                    x.Profile == p && x.Entry.Order == order);
                if (found.Entry is null)
                    return Verdict(pendingOther
                            ? HdvDirectTransactionStateV15.OtherOrderPending
                            : HdvDirectTransactionStateV15.Unseen,
                        pendingOther ? "other order pending; no inference of admission" :
                        "no local intent for exact order; remote absence unproven",
                        otherPending: pendingOther);
                var entry = found.Entry;
                if (expectedRun != Guid.Empty && entry.RunId != expectedRun)
                    return Verdict(HdvDirectTransactionStateV15.RunMismatch,
                        "original run does not match; no order attribution",
                        otherPending: pendingOther);
                var history = new HdvDirectMutationHistoryRow(
                    entry.Order, entry.Pid, entry.Phase, entry.RunId);
                bool attempted = state.AttemptedRuns.Contains(entry.RunId);
                var phase = entry.Phase switch
                {
                    HdvDirectMutationPhase.Started => HdvDirectTransactionStateV15.Started,
                    HdvDirectMutationPhase.Attempted => HdvDirectTransactionStateV15.Attempted,
                    HdvDirectMutationPhase.Uncertain => HdvDirectTransactionStateV15.Uncertain,
                    HdvDirectMutationPhase.Reviewed => HdvDirectTransactionStateV15.Reviewed,
                    HdvDirectMutationPhase.Confirmed when attempted =>
                        HdvDirectTransactionStateV15.LocallyConfirmedWithAttempt,
                    HdvDirectMutationPhase.Confirmed =>
                        HdvDirectTransactionStateV15.LegacyConfirmedWithoutAttempt,
                    _ => HdvDirectTransactionStateV15.StorageUnavailable
                };
                return Verdict(phase,
                    "one hash-chain/anchor-consistent local record; never TCP ACK, tax credit or send grant",
                    history, attempted, pendingOther);
            }
            catch (FileNotFoundException)
            {
                return MissingJournalHasDurableWitness()
                    ? Verdict(HdvDirectTransactionStateV15.StorageUnavailable,
                        "journal missing with prior durable witness")
                    : Verdict(HdvDirectTransactionStateV15.Unseen,
                        "no local journal; remote order absence unproven");
            }
            catch (DirectoryNotFoundException)
            { return Verdict(HdvDirectTransactionStateV15.StorageUnavailable,
                "journal parent missing or inaccessible"); }
            catch (Exception e) when (StorageError(e))
            { return Verdict(HdvDirectTransactionStateV15.StorageUnavailable,
                "journal unreadable (" + e.GetType().Name + ")"); }
        }
    }

    /// <summary>Fail closed on a damaged/unavailable journal or anchor.</summary>
    public bool NeedsReview(string? profile, string? requiredOrderPrefix = null)
    {
        if (!ValidProfile(profile)) return true;
        lock (_gate)
            try
            {
                if (_disposed) return true;
                using var stream = Open(FileMode.Open, FileAccess.Read);
                var state = Read(stream);
                return state is null || !Anchored(stream, state) ||
                       EmptyWithPriorSenderWitness(state) ||
                       (state.Pending.TryGetValue(profile!, out var pending) &&
                        (requiredOrderPrefix is null ||
                         pending.Order.StartsWith(requiredOrderPrefix, StringComparison.Ordinal)));
            }
            catch (FileNotFoundException) { return MissingJournalHasDurableWitness(); }
            catch (DirectoryNotFoundException) { return MissingJournalHasDurableWitness(); }
            catch (Exception e) when (StorageError(e)) { return true; }
    }

    /// <summary>
    /// Write and fsync the intent BEFORE touching the transport. Crash in the
    /// interval between this point and the network call remains quarantined.
    /// </summary>
    public HdvDirectMutationDecision TryBegin(string? profile, uint pid,
        string? order, out HdvDirectMutationLease lease)
    {
        lease = default;
        if (pid == 0 || !ValidProfile(profile) || !ValidOrder(order))
            return HdvDirectMutationDecision.Unavailable;
        lock (_gate)
        {
            FileStream? active = null;
            try
            {
                if (_disposed) return HdvDirectMutationDecision.Unavailable;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                bool previouslyWitnessed = MissingJournalHasDurableWitness();
                // An old active-lock file survives after a completed or
                // crashed order. If someone removed BOTH journal and anchor,
                // it must not be possible to bootstrap a clean new ledger and
                // replay an old UID through a different desktop/Lua entrypoint.
                if (!File.Exists(_path) && previouslyWitnessed)
                    return HdvDirectMutationDecision.Unavailable;
                var origin = _productionOrigin.Value;
                bool ownedCapture = origin is not null &&
                    origin.Pid == pid && origin.Profile == profile &&
                    origin.Order == order &&
                    _activeProductionCaptures.Contains(origin.CaptureId);
                // Deny a stale production capture BEFORE opening the persistent
                // .active witness or creating the first journal file. Creating
                // either for a rejected virgin operation would make an empty
                // ledger look like an interrupted sender and poison recovery.
                if (ownedCapture && origin!.RemainsCurrent is not null)
                {
                    bool current;
                    try { current = origin.RemainsCurrent(); }
                    catch (Exception) { current = false; }
                    if (!current) return HdvDirectMutationDecision.Unavailable;
                }
                // This OS lock lives until the actual sender has settled.
                // Even an operator in another Givre process cannot clear
                // an in-flight order. A crash releases the kernel handle.
                active = OpenActive(profile!);
                using var stream = Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    (state.Count == 0 && previouslyWitnessed))
                    return HdvDirectMutationDecision.Unavailable;
                if (state.Orders.Contains((profile!, order!)) ||
                    state.Pending.ContainsKey(profile!))
                    return HdvDirectMutationDecision.NeedsReview;
                var run = Guid.NewGuid();
                HdvProductionOriginV17? durableOrigin = ownedCapture
                    ? origin!.DurableOrigin : null;
                if (!Append(stream, state, profile!, order!, pid, run,
                        HdvDirectMutationPhase.Started, durableOrigin))
                    return HdvDirectMutationDecision.Unavailable;
                lease = new(run, profile!, order!, pid);
                _live.Add(run);
                _activeHandles.Add(run, active);
                active = null;
                if (ownedCapture)
                {
                    // Old/abandoned captures are intentionally evicted; only
                    // a fresh one-use current ticket may claim a local run.
                    if (_productionRuns.Count >= 1024) _productionRuns.Clear();
                    _productionRuns.TryAdd(origin!.CaptureId,
                        (pid, profile!, order!, run));
                }
                return HdvDirectMutationDecision.Allowed;
            }
            catch (Exception e) when (StorageError(e))
            { return HdvDirectMutationDecision.Unavailable; }
            finally { active?.Dispose(); }
        }
    }

    /// <summary>
    /// Persist the actual transport-attempt boundary before calling Inject.
    /// Started without Attempted after a crash is STILL quarantined: it is
    /// impossible to prove that no bytes reached the server after a crash.
    /// The original V7 Started -> Finish transition remains readable.
    /// </summary>
    public bool TryMarkAttempt(HdvDirectMutationLease lease)
    {
        if (!ValidProfile(lease.Profile) || !ValidOrder(lease.Order) ||
            lease.RunId == Guid.Empty || lease.Pid == 0) return false;
        lock (_gate)
        {
            if (_disposed || !_live.Contains(lease.RunId)) return false;
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.ReadWrite);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    !state.Pending.TryGetValue(lease.Profile, out var pending) ||
                    pending.RunId != lease.RunId || pending.Order != lease.Order ||
                    pending.Pid != lease.Pid || pending.Phase != HdvDirectMutationPhase.Started)
                    return false;
                return Append(stream, state, lease.Profile, lease.Order, lease.Pid,
                    lease.RunId, HdvDirectMutationPhase.Attempted);
            }
            catch (Exception e) when (StorageError(e)) { return false; }
        }
    }

    /// <summary>Confirmed requires the caller's separately correlated exact result.</summary>
    public bool TryFinish(HdvDirectMutationLease lease, bool confirmed)
    {
        if (!ValidProfile(lease.Profile) || !ValidOrder(lease.Order) ||
            lease.RunId == Guid.Empty || lease.Pid == 0) return false;
        lock (_gate)
        {
            if (_disposed || !_live.Contains(lease.RunId)) return false;
            try
            {
                using var stream = Open(FileMode.Open, FileAccess.ReadWrite);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    !state.Pending.TryGetValue(lease.Profile, out var pending) ||
                    pending.RunId != lease.RunId || pending.Order != lease.Order ||
                    pending.Pid != lease.Pid || pending.Phase is not
                        (HdvDirectMutationPhase.Started or HdvDirectMutationPhase.Attempted))
                    return false;
                if (!Append(stream, state, lease.Profile, lease.Order, lease.Pid,
                        lease.RunId, confirmed ? HdvDirectMutationPhase.Confirmed : HdvDirectMutationPhase.Uncertain))
                    return false;
                _live.Remove(lease.RunId);
                _activeHandles.Remove(lease.RunId, out var active);
                active?.Dispose();
                return true;
            }
            catch (Exception e) when (StorageError(e)) { return false; }
        }
    }

    /// <summary>
    /// Only the explicit operator reconciliation can clear a pending original
    /// profile; never a timer, stock delta, KamasUpdate or generic error.
    /// A still-live sender cannot be manually cleared mid-dispatch.
    /// </summary>
    public bool TryReview(string? profile, string? requiredOrderPrefix = null)
    {
        if (!ValidProfile(profile)) return false;
        lock (_gate)
            try
            {
                if (_disposed) return false;
                // An invalid review of an empty, virgin ledger is not an
                // emission and must NOT create an .active-* historical
                // witness which would block its first legitimate TryBegin.
                if (!File.Exists(_path)) return false;
                using var active = OpenActive(profile!);
                using var stream = Open(FileMode.Open, FileAccess.ReadWrite);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    !state.Pending.TryGetValue(profile!, out var pending) ||
                    (requiredOrderPrefix is not null &&
                     !pending.Order.StartsWith(requiredOrderPrefix, StringComparison.Ordinal)) ||
                    _live.Contains(pending.RunId)) return false;
                return Append(stream, state, profile!, pending.Order, pending.Pid,
                    pending.RunId, HdvDirectMutationPhase.Reviewed);
            }
            catch (Exception e) when (StorageError(e)) { return false; }
    }

    /// <summary>
    /// Exact operator-selected durable review, not a generic prefix clear.
    /// The original run/order/profile must still be pending under the
    /// exclusive cross-process lock immediately before fsync. A live sender
    /// or an identity change inside the confirmation callback refuses.
    /// No server receipt, tax release or game dispatch is manufactured.
    /// </summary>
    public bool TryReviewExact(string? profile, string? exactOrder, Guid localRun,
        Func<bool>? remainsCurrent = null)
    {
        if (!ValidProfile(profile) || !ValidOrder(exactOrder) ||
            localRun == Guid.Empty) return false;
        bool Current()
        {
            try { return remainsCurrent?.Invoke() != false; }
            catch (Exception) { return false; }
        }
        if (!Current()) return false;
        lock (_gate)
        {
            if (_disposed || !Current()) return false;
            try
            {
                if (!File.Exists(_path)) return false;
                using var active = OpenActive(profile!);
                using var stream = Open(FileMode.Open, FileAccess.ReadWrite);
                var state = Read(stream);
                if (state is null || !Anchored(stream, state) ||
                    !state.Pending.TryGetValue(profile!, out var pending) ||
                    pending.Order != exactOrder || pending.RunId != localRun ||
                    _live.Contains(localRun) || !Current()) return false;
                return Append(stream, state, profile!, exactOrder!, pending.Pid,
                    localRun, HdvDirectMutationPhase.Reviewed);
            }
            catch (Exception e) when (StorageError(e)) { return false; }
        }
    }

    private sealed record Entry(string Order, uint Pid, Guid RunId, HdvDirectMutationPhase Phase);
    private sealed class State
    {
        public int Count;
        public string Tail = Genesis;
        public HashSet<(string Profile, string Order)> Orders { get; } = new();
        public Dictionary<string, Entry> Pending { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Entry> Last { get; } = new(StringComparer.Ordinal);
        // Stable per-operation history independent of phase transitions:
        // one row per locally started order, updated in O(1) per record.
        public List<(string Profile, Entry Entry)> History { get; } = new();
        public Dictionary<Guid, int> HistorySlots { get; } = new();
        // A legacy Started -> Confirmed record is still readable for V7
        // compatibility, but V14 production cannot treat it as evidence of
        // a durably admitted outgoing attempt. Reconstructed only from the
        // actual hash-chained Attempted phase, never guessed from Confirmed.
        public HashSet<Guid> AttemptedRuns { get; } = new();
        // Optional V17 STARTED metadata is embedded in the SAME hash chain and
        // anchor, so a crash cannot separate it from the original run.
        public Dictionary<Guid, HdvProductionOriginV17> ProductionOrigins { get; } = new();
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Anchor(State state)
        => AnchorHeader + state.Count.ToString(CultureInfo.InvariantCulture) + "|" + state.Tail + "\n";
    private static bool StorageError(Exception e)
        => e is IOException or UnauthorizedAccessException or NotSupportedException
             or ArgumentException or DecoderFallbackException or OverflowException;

    private FileStream Open(FileMode mode, FileAccess access)
    {
        for (int retry = 0; ; retry++)
            try { return new FileStream(_path, mode, access, FileShare.None); }
            catch (IOException e) when (retry < 20 && (e.HResult & 0xFFFF) is 32 or 33)
            { System.Threading.Thread.Sleep(10); }
    }

    private FileStream OpenActive(string profile)
        => new(_path + ".active-" + Hash(profile)[..24],
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private bool MissingJournalHasDurableWitness()
    {
        try
        {
            if (File.Exists(_path + ".anchor")) return true;
            string? parent = Path.GetDirectoryName(_path);
            if (parent is null || !Directory.Exists(parent)) return false;
            // The per-profile OS lock files are deliberately persistent.
            // Check ALL profiles: deletion of the shared ledger for one
            // character must also deny a different character's next order.
            return Directory.EnumerateFiles(parent,
                Path.GetFileName(_path) + ".active-*", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception e) when (StorageError(e)) { return true; }
    }

    // After crash, a zero-record journal (including one truncated back to a
    // valid header/genesis anchor) plus a persistent .active-* witness cannot
    // be treated as a brand-new clean journal. A legitimate first TryBegin
    // records whether that witness existed BEFORE creating its own lockfile.
    private bool EmptyWithPriorSenderWitness(State state)
        => state.Count == 0 && MissingJournalHasDurableWitness();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var stream in _activeHandles.Values) stream.Dispose();
            _activeHandles.Clear();
            _live.Clear();
            _productionRuns.Clear();
            _activeProductionCaptures.Clear();
            _productionOrigin.Value = null;
        }
    }

    private bool Anchored(FileStream stream, State state)
    {
        string anchor = _path + ".anchor";
        if (File.Exists(anchor) != (stream.Length > 0)) return false;
        if (stream.Length == 0) return true;
        try { return File.ReadAllText(anchor, new UTF8Encoding(false, true)) == Anchor(state); }
        catch (Exception e) when (StorageError(e)) { return false; }
    }

    private static State? Read(FileStream stream)
    {
        if (stream.Length == 0) return new State();
        if (stream.Length > 16 * 1024 * 1024) return null;
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string content = reader.ReadToEnd();
        if (!content.StartsWith(Header, StringComparison.Ordinal) || !content.EndsWith('\n')) return null;
        var state = new State();
        string[] lines = content[Header.Length..].Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string[] f = lines[i].Split('|');
            if (f.Length is not (7 or 8) ||
                !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index != state.Count + 1 ||
                !uint.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint pid) ||
                pid == 0 || !Guid.TryParseExact(f[4], "N", out Guid run) || run == Guid.Empty ||
                !int.TryParse(f[5], NumberStyles.None, CultureInfo.InvariantCulture, out int phaseNum) ||
                !Enum.IsDefined(typeof(HdvDirectMutationPhase), phaseNum))
                return null;
            string profile, order;
            try
            {
                var strict = new UTF8Encoding(false, true);
                profile = strict.GetString(Convert.FromBase64String(f[1]));
                order = strict.GetString(Convert.FromBase64String(f[2]));
            }
            catch (Exception e) when (StorageError(e) || e is FormatException) { return null; }
            if (!ValidProfile(profile) || !ValidOrder(order) ||
                f[^1] != Hash(state.Tail + "|" + string.Join("|", f.Take(f.Length - 1)))) return null;
            var phase = (HdvDirectMutationPhase)phaseNum;
            HdvProductionOriginV17? production = null;
            if (f.Length == 8 &&
                (phase != HdvDirectMutationPhase.Started ||
                 !TryParseProductionOriginV17(f[6], pid, order, out production)))
                return null;
            if (phase == HdvDirectMutationPhase.Started)
            {
                if (state.Pending.ContainsKey(profile) || !state.Orders.Add((profile, order)))
                    return null;
                state.Pending.Add(profile, new Entry(order, pid, run, phase));
                if (state.HistorySlots.ContainsKey(run)) return null;
                state.HistorySlots.Add(run, state.History.Count);
                state.History.Add((profile, new Entry(order, pid, run, phase)));
                if (production is not null)
                    state.ProductionOrigins.Add(run, production);
            }
            else
            {
                if (!state.Pending.TryGetValue(profile, out var previous) ||
                    previous.Order != order || previous.RunId != run || previous.Pid != pid ||
                    (phase switch
                    {
                        HdvDirectMutationPhase.Attempted => previous.Phase != HdvDirectMutationPhase.Started,
                        HdvDirectMutationPhase.Reviewed => previous.Phase is not
                            (HdvDirectMutationPhase.Started or HdvDirectMutationPhase.Attempted or
                             HdvDirectMutationPhase.Uncertain),
                        HdvDirectMutationPhase.Confirmed or HdvDirectMutationPhase.Uncertain =>
                            previous.Phase is not (HdvDirectMutationPhase.Started or HdvDirectMutationPhase.Attempted),
                        _ => true
                    }))
                    return null;
                if (phase is HdvDirectMutationPhase.Confirmed or HdvDirectMutationPhase.Reviewed)
                    state.Pending.Remove(profile);
                else
                    state.Pending[profile] = previous with { Phase = phase };
                if (!state.HistorySlots.TryGetValue(run, out int historyIndex) ||
                    state.History[historyIndex].Profile != profile ||
                    state.History[historyIndex].Entry.Order != order)
                    return null;
                state.History[historyIndex] =
                    (profile, previous with { Phase = phase });
                if (phase == HdvDirectMutationPhase.Attempted)
                    state.AttemptedRuns.Add(run);
            }
            state.Last[profile] = new Entry(order, pid, run, phase);
            state.Count++;
            state.Tail = f[^1];
        }
        return state;
    }

    private bool Append(FileStream stream, State state, string profile, string order,
        uint pid, Guid run, HdvDirectMutationPhase phase,
        HdvProductionOriginV17? productionOrigin = null)
    {
        string payload = string.Join("|", (state.Count + 1).ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(order)),
            pid.ToString(CultureInfo.InvariantCulture), run.ToString("N"),
            ((int)phase).ToString(CultureInfo.InvariantCulture));
        if (productionOrigin is not null)
        {
            if (phase != HdvDirectMutationPhase.Started ||
                !ValidProductionOriginV17(productionOrigin, pid, order))
                return false;
            payload += "|" + FormatProductionOriginV17(productionOrigin);
        }
        string hash = Hash(state.Tail + "|" + payload);
        if (stream.Length == 0)
        {
            byte[] header = Encoding.UTF8.GetBytes(Header);
            stream.Write(header);
            stream.Flush(flushToDisk: true);
            // The first journal header without its anchor must be rejected
            // after a crash, not treated as a pristine empty journal.
            using var anchor = new FileStream(_path + ".anchor", FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            byte[] genesis = Encoding.UTF8.GetBytes(Anchor(state));
            anchor.Write(genesis);
            anchor.Flush(flushToDisk: true);
        }
        byte[] record = Encoding.UTF8.GetBytes(payload + "|" + hash + "\n");
        stream.Position = stream.Length;
        stream.Write(record);
        stream.Flush(flushToDisk: true);
        // Anchor updated only AFTER the record's durable flush. An interrupted
        // update makes the next reader reject the pair, never replay it.
        var next = new State { Count = state.Count + 1, Tail = hash };
        string staging = _path + ".anchor.pending-" + Guid.NewGuid().ToString("N");
        using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Anchor(next));
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(staging, _path + ".anchor", overwrite: true);
        return true;
    }
}
