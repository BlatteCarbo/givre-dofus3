using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>Durable ownership of an ambiguous route/gather action. Never use a PID as identity.</summary>
public readonly record struct ActivityRecoveryScope(int ServerId, long CharacterId)
{
    public bool IsValid => ServerId > 0 && CharacterId > 0;
}

public enum ActivityRecoveryDurableState
{
    Clear,
    Pending,
    IdentityMissing,
    Corrupt,
    Busy,
    StorageUnavailable
}

/// <summary>
/// Immutable UI/prime snapshot. Any state except Clear is fail-closed.
/// Pending means "an attempt MAY have been sent", not a server-verified ACK.
/// </summary>
public sealed record ActivityRecoveryDurableStatus(
    ActivityRecoveryScope Scope,
    ActivityRecoveryDurableState State,
    ActivityRecoverySegment? Segment,
    long MapId,
    long MapGeneration,
    string Detail)
{
    public bool IsQuarantined => State != ActivityRecoveryDurableState.Clear;
}

public readonly record struct ActivityRecoveryDurableAdmission(
    bool MayDispatch, ActivityRecoveryDurableStatus Status);

/// <summary>
/// Write-ahead one-shot admission. FileMode.CreateNew is the cross-process winner lock;
/// WriteThrough + Flush(flushToDisk:true) make the marker durable before returning
/// MayDispatch=true. Crashes during creation leave even a zero-byte marker quarantined.
///
/// The marker is NEVER automatically deleted, rewritten or cleared. This is intentional:
/// a position, inventory difference, iwi or pid-only MoveOutcome cannot establish a
/// correlated ACK. A future explicit, independently authorized reconciliation must be
/// implemented before any release API can be added.
/// </summary>
public sealed partial class ActivityRecoveryDurableStore
{
    private const string Header = "ARC-DURABLE-1";
    private const int MaxRecordBytes = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;

    private sealed record DurableRecord(
        int Version,
        int ServerId,
        long CharacterId,
        ActivityRecoverySegment Segment,
        ActivityRecoverySnapshot Observation,
        DateTime CreatedUtc);

    public ActivityRecoveryDurableStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Isolated recovery root required", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public static string DefaultRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("LocalApplicationData unavailable; durable recovery disabled");
        return Path.Combine(local, "GivreClone", "ActivityRecoveryDurable", "v1");
    }

    private string MarkerPath(ActivityRecoveryScope scope) =>
        Path.Combine(_root, "server-" + scope.ServerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "character-" + scope.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".pending");

    private static ActivityRecoveryDurableStatus Failure(
        ActivityRecoveryScope scope, ActivityRecoveryDurableState state, string detail) =>
        new(scope, state, null, 0, 0, detail);

    // File.Exists is NOT an authoritative read of a durable admission marker:
    // it also returns false on several I/O and access failures. Those failures
    // must never be presented as Clear after a Stop/Start.
    private ActivityRecoveryDurableStatus MissingMarkerStatus(ActivityRecoveryScope scope, string path)
    {
        string? parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            try
            {
                var attributes = File.GetAttributes(parent);
                if ((attributes & FileAttributes.Directory) == 0)
                    return Failure(scope, ActivityRecoveryDurableState.StorageUnavailable,
                        "recovery parent path is not a directory");
                // Enumerating the nearest existing ancestor checks that we can
                // actually inspect it, rather than trusting Directory.Exists(false).
                using var entries = Directory.EnumerateFileSystemEntries(parent).GetEnumerator();
                _ = entries.MoveNext();
                return Failure(scope, ActivityRecoveryDurableState.Clear, "no durable attempt");
            }
            catch (FileNotFoundException) { parent = Path.GetDirectoryName(parent); }
            catch (DirectoryNotFoundException) { parent = Path.GetDirectoryName(parent); }
            catch (UnauthorizedAccessException)
            {
                return Failure(scope, ActivityRecoveryDurableState.StorageUnavailable,
                    "recovery parent cannot be inspected");
            }
            catch (IOException)
            {
                return Failure(scope, ActivityRecoveryDurableState.StorageUnavailable,
                    "recovery parent I/O unavailable");
            }
        }
        return Failure(scope, ActivityRecoveryDurableState.StorageUnavailable,
            "no inspectable recovery ancestor");
    }

    private static bool ValidSegment(ActivityRecoverySegment? segment)
    {
        if (segment is null || !Enum.IsDefined(segment.Kind)) return false;
        return segment.Kind switch
        {
            ActivityRecoveryKind.Cell =>
                segment.Target >= 0 && segment.Target < GivreClone.Protocol.MapPathfinder.CellCount,
            ActivityRecoveryKind.Gather or ActivityRecoveryKind.Interaction =>
                segment.ElementId > 0 && segment.Target == segment.ElementId &&
                (uint)segment.Cell < GivreClone.Protocol.MapPathfinder.CellCount,
            ActivityRecoveryKind.MapTransition => segment.Target > 0,
            _ => false
        };
    }

    /// <summary>Read-only, process-safe status: reading during the writer's exclusive
    /// window is Busy (quarantined), NEVER Clear.</summary>
    public ActivityRecoveryDurableStatus ReadStatus(ActivityRecoveryScope scope) =>
        ReadMarker(scope).Status;

    // Each reconciliation reads the immutable, checksummed WRITE-AHEAD marker once.
    // Do not reconstruct the original stock/position from UI cache or current PID.
    private (ActivityRecoveryDurableStatus Status, DurableRecord? Record) ReadMarker(ActivityRecoveryScope scope)
    {
        (ActivityRecoveryDurableStatus, DurableRecord?) Deny(ActivityRecoveryDurableState state, string reason) =>
            (Failure(scope, state, reason), null);
        if (!scope.IsValid) return Deny(ActivityRecoveryDurableState.IdentityMissing, "server/character id missing");
        try
        {
            string path = MarkerPath(scope);
            // Direct open distinguishes genuine absence from a locked/denied
            // marker. File.Exists is intentionally not used for admission state.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            if (file.Length is <= 0 or > MaxRecordBytes)
                return Deny(ActivityRecoveryDurableState.Corrupt, "empty/oversized pending marker");
            byte[] data = new byte[(int)file.Length];
            file.ReadExactly(data);
            string text = StrictUtf8.GetString(data);
            int first = text.IndexOf('\n');
            int second = first >= 0 ? text.IndexOf('\n', first + 1) : -1;
            if (first < 0 || second < 0 || text[..first] != Header ||
                second + 1 >= text.Length || !text.EndsWith('\n'))
                return Deny(ActivityRecoveryDurableState.Corrupt, "marker framing invalid");
            string expected = text[(first + 1)..second];
            string json = text[(second + 1)..^1];
            if (expected.Length != 64 || !IsDigestValid(expected, json))
                return Deny(ActivityRecoveryDurableState.Corrupt, "marker digest mismatch");
            DurableRecord? record = JsonSerializer.Deserialize<DurableRecord>(json);
            if (record is null || record.Version != 1 || record.ServerId != scope.ServerId ||
                record.CharacterId != scope.CharacterId || !ValidSegment(record.Segment) ||
                !record.Observation.Map.IsValid || record.Observation.Map.MapId <= 0)
                return Deny(ActivityRecoveryDurableState.Corrupt, "marker identity/contents invalid");
            return (new ActivityRecoveryDurableStatus(scope, ActivityRecoveryDurableState.Pending,
                record.Segment, record.Observation.Map.MapId,
                record.Observation.Map.Generation, "durable attempt; correlated ACK absent"), record);
        }
        catch (FileNotFoundException)
        {
            var status = MissingMarkerStatus(scope, MarkerPath(scope));
            return (status, null);
        }
        catch (DirectoryNotFoundException)
        {
            var status = MissingMarkerStatus(scope, MarkerPath(scope));
            return (status, null);
        }
        catch (IOException) { return Deny(ActivityRecoveryDurableState.Busy, "pending marker locked/unreadable"); }
        catch (UnauthorizedAccessException) { return Deny(ActivityRecoveryDurableState.StorageUnavailable, "recovery directory denied"); }
        catch (Exception ex) when (ex is JsonException or FormatException or DecoderFallbackException)
        {
            return Deny(ActivityRecoveryDurableState.Corrupt, "pending marker invalid");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return Deny(ActivityRecoveryDurableState.StorageUnavailable, "recovery path unavailable");
        }
    }

    private static bool IsDigestValid(string expected, string json)
    {
        try
        {
            byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            byte[] digest = Convert.FromHexString(expected);
            return CryptographicOperations.FixedTimeEquals(actual, digest);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// MayDispatch=true ONLY after an exclusive CreateNew and fsync have completed.
    /// It must be called immediately before invoking the existing gameplay primitive.
    /// No removal on errors: even a short/empty file means the action MAY have escaped.
    /// </summary>
    public ActivityRecoveryDurableAdmission TryReserve(
        ActivityRecoveryScope scope, ActivityRecoverySegment segment,
        ActivityRecoverySnapshot snapshot)
    {
        if (!scope.IsValid)
            return new(false, Failure(scope, ActivityRecoveryDurableState.IdentityMissing, "server/character id missing"));
        if (!snapshot.Map.IsValid || snapshot.Map.MapId <= 0 || !snapshot.PositionConfirmed ||
            snapshot.PositionMap != snapshot.Map.MapId || snapshot.GeometryMap != snapshot.Map.MapId ||
            snapshot.Interrupted || snapshot.Suspended || !ValidSegment(segment))
            return new(false, Failure(scope, ActivityRecoveryDurableState.StorageUnavailable, "invalid pre-send snapshot"));
        try
        {
            string path = MarkerPath(scope);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var record = new DurableRecord(1, scope.ServerId, scope.CharacterId, segment,
                snapshot, DateTime.UtcNow);
            string json = JsonSerializer.Serialize(record);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            byte[] content = Encoding.UTF8.GetBytes(Header + "\n" + digest + "\n" + json + "\n");
            if (content.Length > MaxRecordBytes)
                return new(false, Failure(scope, ActivityRecoveryDurableState.StorageUnavailable, "marker too large"));
            // Atomic ownership across threads/processes, no read-then-create race.
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(content);
                file.Flush(flushToDisk: true);
            }
            return new(true, new ActivityRecoveryDurableStatus(scope,
                ActivityRecoveryDurableState.Pending, segment,
                snapshot.Map.MapId, snapshot.Map.Generation,
                "fsynced before gameplay send; ACK not correlated"));
        }
        catch (IOException)
        {
            // Includes ERROR_FILE_EXISTS and another process holding the marker.
            var status = ReadStatus(scope);
            return new(false, status.State == ActivityRecoveryDurableState.Clear
                ? Failure(scope, ActivityRecoveryDurableState.StorageUnavailable, "marker create/fsync failed")
                : status);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException)
        {
            return new(false, Failure(scope, ActivityRecoveryDurableState.StorageUnavailable, "durable marker cannot be written"));
        }
    }
}
