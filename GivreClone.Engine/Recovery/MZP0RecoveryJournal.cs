using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.Engine.Recovery;

/// <summary>
/// Offline, single-writer fail-closed journal. The caller MUST persist BeginIntent and
/// OpenDispatchWindow before invoking a transport. Neither the journal nor reopening it
/// ever retransmits an operation; an interrupted send is always classified as uncertain.
/// Acknowledgment is possible only with a current-generation, correlated, explicitly
/// verified inbound response supplied by the transport adapter.
/// </summary>
public sealed class MZP0RecoveryJournal : IDisposable
{
    private const int FormatVersion = 1;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _temporaryPath;
    private readonly string _backupPath;
    private readonly int _maxEntries;
    private readonly int _maxSnapshotBytes;
    private readonly FileStream _singleWriterLease;
    private MZP0Payload _payload;
    private MZP0SessionGeneration? _active;
    private bool _faulted;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = false
    };

    /// <param name="path">An explicit dedicated journal path (no default user/profile directory).</param>
    /// <param name="maxEntries">Maximum retained records; unresolved records cannot be discarded.</param>
    /// <param name="maxSnapshotBytes">Upper limit on both the serialized and loaded snapshot.</param>
    public MZP0RecoveryJournal(string path, int maxEntries = 128, int maxSnapshotBytes = 262144)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A journal path is required.", nameof(path));
        if (maxEntries is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxSnapshotBytes is < 1024 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxSnapshotBytes));

        _path = Path.GetFullPath(path);
        _temporaryPath = _path + ".tmp";
        _backupPath = _path + ".bak";
        _maxEntries = maxEntries;
        _maxSnapshotBytes = maxSnapshotBytes;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // A persistent lease file also marks a path that was initialized before. A lost
        // primary must never be silently mistaken for a brand-new empty journal.
        bool leasePreviouslyExisted = File.Exists(_path + ".lck");
        // Prevent two journal instances/processes from silently overwriting one another.
        _singleWriterLease = new FileStream(_path + ".lck", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(_path))
            {
                _payload = ReadAndValidate();
                // A leftover uncommitted temporary file is never authoritative.
                if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
            }
            else
            {
                // Lost primary + backup/temporary is an ambiguous crash, NOT an empty store.
                if (leasePreviouslyExisted || File.Exists(_temporaryPath) || File.Exists(_backupPath))
                    throw new InvalidDataException("Recovery journal primary is missing; recovery is blocked.");
                _payload = new MZP0Payload();
            }
        }
        catch
        {
            _singleWriterLease.Dispose();
            throw;
        }
    }

    /// <summary>Snapshots are immutable copies. A reopened journal has no active generation.</summary>
    public IReadOnlyList<MZP0JournalRecord> Records
    {
        get { lock (_gate) { EnsureUsable(); return _payload.Records.ToArray(); } }
    }

    public long Revision
    {
        get { lock (_gate) { EnsureUsable(); return _payload.Revision; } }
    }

    public MZP0SessionGeneration? ActiveGeneration
    {
        get { lock (_gate) { EnsureUsable(); return _active; } }
    }

    /// <summary>
    /// Begins a NEW generation, even if the PID and connection ID match a prior session.
    /// Every unresolved record from any preceding generation becomes Uncertain durably.
    /// </summary>
    public MZP0SessionGeneration Activate(MZP0SessionIdentity identity)
    {
        ValidateIdentity(identity);
        lock (_gate)
        {
            EnsureUsable();
            if (_payload.LastGeneration == long.MaxValue || _payload.Revision == long.MaxValue)
                throw new InvalidOperationException("Recovery generation exhausted.");

            var generation = new MZP0SessionGeneration(_payload.LastGeneration + 1, Guid.NewGuid(), identity);
            Mutate(next =>
            {
                next.LastGeneration = generation.Number;
                next.LastCorrelationUid = 0;
                for (int i = 0; i < next.Records.Count; i++)
                {
                    var record = next.Records[i];
                    if (record.State is MZP0DeliveryState.Prepared or MZP0DeliveryState.DispatchWindowOpened)
                        next.Records[i] = record with { State = MZP0DeliveryState.Uncertain, UpdatedRevision = next.Revision };
                }
            });
            _active = generation;
            return generation;
        }
    }

    /// <summary>Persist the intent BEFORE any outbound activity, with no payload bytes.</summary>
    public MZP0JournalRecord BeginIntent(
        MZP0SessionGeneration generation, Guid operationId, int correlationUid,
        MZP0OperationCategory category, MZP0DeliverySemantics semantics)
    {
        lock (_gate)
        {
            RequireActive(generation);
            if (operationId == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(operationId));
            if (correlationUid <= 0) throw new ArgumentOutOfRangeException(nameof(correlationUid));
            if (!Enum.IsDefined(category) || !Enum.IsDefined(semantics))
                throw new ArgumentException("Invalid operation classification.");
            if (_payload.Records.Any(r => r.OperationId == operationId))
                throw new InvalidOperationException("Duplicate operation identity; no duplicate dispatch is authorized.");
            if (_payload.Records.Any(r => r.Generation == generation.Number &&
                r.GenerationNonce == generation.Nonce && r.CorrelationUid == correlationUid))
                throw new InvalidOperationException("Duplicate correlation UID within one session generation.");
            if (correlationUid <= _payload.LastCorrelationUid)
                throw new InvalidOperationException("Correlation UIDs must strictly increase within one generation.");
            if (_payload.Records.Count >= _maxEntries)
                throw new InvalidOperationException("Recovery journal is full; reconcile or prune terminal records first.");
            var record = new MZP0JournalRecord(operationId, generation.Number, generation.Nonce,
                correlationUid, category, semantics, MZP0DeliveryState.Prepared,
                _payload.Revision + 1, _payload.Revision + 1);
            Mutate(next =>
            {
                next.Records.Add(record);
                next.LastCorrelationUid = correlationUid;
            });
            return record;
        }
    }

    /// <summary>
    /// This durable step is mandatory immediately before attempting exactly one send.
    /// After a crash or timeout, it cannot be called again for the same operation.
    /// </summary>
    public MZP0DispatchAuthorization OpenDispatchWindow(MZP0SessionGeneration generation, Guid operationId)
    {
        lock (_gate)
        {
            RequireActive(generation);
            var record = RequireRecord(generation, operationId);
            if (record.State != MZP0DeliveryState.Prepared)
                throw new InvalidOperationException("Dispatch window was already opened or operation is terminal.");
            Mutate(next => Replace(next, operationId, record with
            {
                State = MZP0DeliveryState.DispatchWindowOpened,
                UpdatedRevision = next.Revision
            }));
            return new MZP0DispatchAuthorization(generation, operationId, record.CorrelationUid);
        }
    }

    /// <summary>
    /// A failed Send(), timeout or disconnection is NOT proof that nothing was sent.
    /// Convert the open window into a durable, non-replayable uncertain outcome.
    /// </summary>
    public void MarkUncertain(MZP0SessionGeneration generation, Guid operationId)
    {
        lock (_gate)
        {
            RequireActive(generation);
            var record = RequireRecord(generation, operationId);
            if (record.State != MZP0DeliveryState.DispatchWindowOpened)
                throw new InvalidOperationException("Only an open dispatch window may become uncertain.");
            Mutate(next => Replace(next, operationId, record with
            {
                State = MZP0DeliveryState.Uncertain,
                UpdatedRevision = next.Revision
            }));
        }
    }

    /// <summary>Only a prepared operation can be cancelled as definitely not dispatched.</summary>
    public void CancelBeforeDispatch(MZP0SessionGeneration generation, Guid operationId)
    {
        lock (_gate)
        {
            RequireActive(generation);
            var record = RequireRecord(generation, operationId);
            if (record.State != MZP0DeliveryState.Prepared)
                throw new InvalidOperationException("Cannot claim an opened dispatch window was never sent.");
            Mutate(next => Replace(next, operationId, record with
            {
                State = MZP0DeliveryState.CancelledBeforeDispatch,
                UpdatedRevision = next.Revision
            }));
        }
    }

    /// <summary>
    /// Evidence must be attested by a transport adapter after checking the inbound origin
    /// and genuine server response. This method cannot infer success from a sent frame.
    /// </summary>
    public bool TryAcknowledge(MZP0SessionGeneration generation, Guid operationId, MZP0AcknowledgmentEvidence evidence)
    {
        lock (_gate)
        {
            RequireActive(generation);
            var record = RequireRecord(generation, operationId);
            if (record.State != MZP0DeliveryState.DispatchWindowOpened)
                return false;
            if (!evidence.ExplicitPositiveResponse || evidence.InboundGeneration != generation ||
                evidence.CorrelationUid != record.CorrelationUid)
                return false;
            Mutate(next => Replace(next, operationId, record with
            {
                State = MZP0DeliveryState.Acknowledged,
                UpdatedRevision = next.Revision
            }));
            return true;
        }
    }

    /// <summary>
    /// Durable resolution of an uncertain historical operation, performed from a NEW
    /// live generation only with explicit authoritative evidence about the original
    /// generation and UID. Never reopens its dispatch window, even if not applied.
    /// </summary>
    public bool TryReconcile(MZP0SessionGeneration currentGeneration, Guid operationId,
        MZP0ReconciliationEvidence evidence)
    {
        lock (_gate)
        {
            RequireActive(currentGeneration);
            var record = _payload.Records.SingleOrDefault(r => r.OperationId == operationId);
            if (record is null || record.State != MZP0DeliveryState.Uncertain ||
                !evidence.AuthorityVerified || !Enum.IsDefined(evidence.Conclusion) ||
                record.Generation != evidence.OriginalGeneration ||
                record.GenerationNonce != evidence.OriginalNonce ||
                record.CorrelationUid != evidence.CorrelationUid)
                return false;
            var nextState = evidence.Conclusion == MZP0ReconciliationConclusion.Applied
                ? MZP0DeliveryState.ReconciledApplied : MZP0DeliveryState.ReconciledNotApplied;
            Mutate(next => Replace(next, operationId, record with
            {
                State = nextState,
                UpdatedRevision = next.Revision
            }));
            return true;
        }
    }

    /// <summary>Exposes a disposition only; intentionally offers NO replay/send API.</summary>
    public MZP0RecoveryDisposition Assess(Guid operationId)
    {
        lock (_gate)
        {
            EnsureUsable();
            var record = _payload.Records.FirstOrDefault(x => x.OperationId == operationId);
            if (record is null) return MZP0RecoveryDisposition.Unknown;
            return record.State switch
            {
                MZP0DeliveryState.Prepared when _active?.Number == record.Generation &&
                    _active?.Nonce == record.GenerationNonce => MZP0RecoveryDisposition.AwaitingFirstDispatch,
                MZP0DeliveryState.DispatchWindowOpened when _active?.Number == record.Generation &&
                    _active?.Nonce == record.GenerationNonce => MZP0RecoveryDisposition.AwaitingVerifiedAcknowledgment,
                MZP0DeliveryState.Acknowledged => MZP0RecoveryDisposition.Acknowledged,
                MZP0DeliveryState.CancelledBeforeDispatch => MZP0RecoveryDisposition.CancelledBeforeDispatch,
                MZP0DeliveryState.ReconciledApplied => MZP0RecoveryDisposition.ReconciledApplied,
                MZP0DeliveryState.ReconciledNotApplied => MZP0RecoveryDisposition.ReconciledNotApplied,
                _ => MZP0RecoveryDisposition.RequiresExplicitReconciliation
            };
        }
    }

    /// <summary>
    /// Prune only proved terminal records. Uncertain operations always consume journal
    /// capacity; exhaustion deliberately prevents new operations until reviewed.
    /// </summary>
    public int PruneTerminal(MZP0SessionGeneration generation)
    {
        lock (_gate)
        {
            RequireActive(generation);
            int removed = _payload.Records.Count(r => r.State is
                MZP0DeliveryState.Acknowledged or MZP0DeliveryState.CancelledBeforeDispatch or
                MZP0DeliveryState.ReconciledApplied or MZP0DeliveryState.ReconciledNotApplied);
            if (removed > 0)
                Mutate(next => next.Records.RemoveAll(r => r.State is
                    MZP0DeliveryState.Acknowledged or MZP0DeliveryState.CancelledBeforeDispatch or
                    MZP0DeliveryState.ReconciledApplied or MZP0DeliveryState.ReconciledNotApplied));
            return removed;
        }
    }

    private MZP0JournalRecord RequireRecord(MZP0SessionGeneration generation, Guid operationId)
    {
        var record = _payload.Records.SingleOrDefault(r => r.OperationId == operationId);
        if (record is null || record.Generation != generation.Number || record.GenerationNonce != generation.Nonce)
            throw new InvalidOperationException("Operation does not belong to the active session generation.");
        return record;
    }

    private void RequireActive(MZP0SessionGeneration generation)
    {
        EnsureUsable();
        if (_active is null || generation != _active.Value)
            throw new InvalidOperationException("Stale, inactive or foreign session generation.");
    }

    private static void ValidateIdentity(MZP0SessionIdentity identity)
    {
        if (identity.ProcessId <= 0 || identity.ProcessStartUtcTicks <= 0 || identity.ConnectionId <= 0 ||
            string.IsNullOrWhiteSpace(identity.ProtocolVersion) || identity.ProtocolVersion.Length > 32 ||
            !identity.ProtocolVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new ArgumentException("Incomplete or invalid process birth, connection or protocol identity.", nameof(identity));
    }

    private static void Replace(MZP0Payload payload, Guid operationId, MZP0JournalRecord nextRecord)
    {
        int i = payload.Records.FindIndex(r => r.OperationId == operationId);
        if (i < 0) throw new InvalidOperationException("Missing journal record.");
        payload.Records[i] = nextRecord;
    }

    private void Mutate(Action<MZP0Payload> edit)
    {
        if (_payload.Revision == long.MaxValue) throw new InvalidOperationException("Journal revision exhausted.");
        var next = new MZP0Payload
        {
            Revision = _payload.Revision + 1,
            LastGeneration = _payload.LastGeneration,
            LastCorrelationUid = _payload.LastCorrelationUid,
            Records = new List<MZP0JournalRecord>(_payload.Records)
        };
        edit(next);
        // Size/capacity validation is a rejected request, NOT a possible I/O commit.
        byte[] bytes = Encode(next);
        try
        {
            WriteAtomic(bytes);
            _payload = next;
        }
        catch
        {
            // The rename may have completed before an I/O exception. Never continue with
            // potentially stale in-memory state; reopen and validate before any action.
            _faulted = true;
            _active = null;
            throw;
        }
    }

    private byte[] Encode(MZP0Payload next)
    {
        if (next.Records.Count > _maxEntries) throw new InvalidOperationException("Journal entry limit reached.");
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
        var envelope = new MZP0Envelope
        {
            SchemaVersion = FormatVersion,
            Payload = next,
            Sha256 = Convert.ToHexString(SHA256.HashData(payloadBytes))
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (bytes.Length > _maxSnapshotBytes) throw new InvalidOperationException("Journal byte limit reached.");
        return bytes;
    }

    private void WriteAtomic(byte[] bytes)
    {
        // A writer is never permitted to recreate a lost primary as though it were new.
        if (_payload.Revision > 0 && !File.Exists(_path))
            throw new InvalidDataException("Recovery journal primary disappeared; writing is blocked.");
        using (var stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write,
                   FileShare.None, bufferSize: 4096, options: FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(_path)) File.Replace(_temporaryPath, _path, _backupPath);
        else File.Move(_temporaryPath, _path);
    }

    private MZP0Payload ReadAndValidate()
    {
        if (new FileInfo(_path).Length > _maxSnapshotBytes)
            throw new InvalidDataException("Recovery snapshot exceeds configured byte limit.");
        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length == 0) throw new InvalidDataException("Empty recovery snapshot.");
        MZP0Envelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MZP0Envelope>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Missing recovery envelope.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Recovery snapshot is invalid; refusing to reset its journal.", e);
        }
        if (envelope.SchemaVersion != FormatVersion || envelope.Payload is null ||
            envelope.Sha256 is null || envelope.Sha256.Length != 64)
            throw new InvalidDataException("Unsupported or incomplete recovery snapshot.");
        var contentBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, JsonOptions);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(RequireHexDigest(envelope.Sha256)), SHA256.HashData(contentBytes)))
            throw new InvalidDataException("Recovery snapshot checksum mismatch.");
        var payload = envelope.Payload;
        if (payload.Revision < 1 || payload.LastGeneration < 1 || payload.LastCorrelationUid < 0 ||
            payload.Records is null ||
            payload.Records.Count > _maxEntries)
            throw new InvalidDataException("Recovery snapshot has invalid counters or size.");
        var knownIds = new HashSet<Guid>();
        foreach (var record in payload.Records)
        {
            if (record is null || record.OperationId == Guid.Empty || !knownIds.Add(record.OperationId) ||
                record.Generation < 1 || record.Generation > payload.LastGeneration ||
                record.GenerationNonce == Guid.Empty || record.CorrelationUid <= 0 ||
                record.CreatedRevision < 1 || record.UpdatedRevision < record.CreatedRevision ||
                record.UpdatedRevision > payload.Revision ||
                !Enum.IsDefined(record.Category) || !Enum.IsDefined(record.Semantics) || !Enum.IsDefined(record.State))
                throw new InvalidDataException("Recovery snapshot contains an invalid or duplicate journal record.");
            if (record.Generation == payload.LastGeneration && record.CorrelationUid > payload.LastCorrelationUid)
                throw new InvalidDataException("Recovery snapshot UID watermark is inconsistent.");
        }
        return payload;
    }

    private static string RequireHexDigest(string digest)
    {
        if (!digest.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid recovery snapshot checksum.");
        return digest;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted) throw new InvalidOperationException("Recovery journal I/O failed; close and reopen to validate.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active = null;
            _singleWriterLease.Dispose();
        }
    }

    private sealed class MZP0Envelope
    {
        public int SchemaVersion { get; set; }
        public MZP0Payload? Payload { get; set; }
        public string? Sha256 { get; set; }
    }

    private sealed class MZP0Payload
    {
        public long Revision { get; set; }
        public long LastGeneration { get; set; }
        public int LastCorrelationUid { get; set; }
        public List<MZP0JournalRecord> Records { get; set; } = new();
    }
}
