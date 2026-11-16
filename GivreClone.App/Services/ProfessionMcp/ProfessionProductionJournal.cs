using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

public enum ProfessionProductionJournalStatus
{
    Empty, Current, Conflict, Corrupt, Unavailable
}

/// <summary>One proposed manual action. This is a checkpoint, never a server receipt.</summary>
public sealed record ProfessionProductionPendingAction(
    string ActionKey, string SessionKey, long ObservationRevision,
    int StepId, string Kind, long ItemGid, long Quantity, long? RecipeId,
    // Omitted on older advisory checkpoints: preserve their exact digest when
    // deserializing and reserializing a journal created before V15.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EvidenceContract = null);

public sealed record ProfessionProductionJournalEntry(
    long Sequence, ProfessionMcpRecoveryState RecoveryState,
    ProfessionProductionPendingAction? Pending,
    // Keep independently attributed receipts and V9 attempts across every CAS
    // checkpoint, including the terminal state. Older journals omit both fields
    // so their anchored digest is left unchanged on read.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ConsumedReceiptIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Guid>? ConsumedV9RunIds = null);

public sealed record ProfessionProductionJournalRead(
    ProfessionProductionJournalStatus Status,
    long Sequence,
    ProfessionProductionJournalEntry? Entry);

/// <summary>
/// The storage adapter must make CompareExchange atomic across callers and processes.
/// A stale sequence can NEVER overwrite a newer checkpoint.
/// </summary>
public interface IProfessionProductionJournal
{
    ProfessionProductionJournalRead Read();
    ProfessionProductionJournalStatus CompareExchange(
        long expectedSequence, ProfessionProductionJournalEntry next);
}

/// <summary>
/// File-backed CAS journal. A separate lock file coordinates processes; a flushed
/// temporary file is renamed over the journal under the lock. Invalid bytes block
/// updates, preserving the original file for investigation. The caller owns the
/// directory and may choose a per-character/session journal path.
/// </summary>
public sealed class ProfessionProductionFileJournal : IProfessionProductionJournal
{
    private const int MaxJournalBytes = 1_048_576;
    private const int MaxAnchorBytes = 256;
    private const string AnchorHeader = "GIVRE-PROFESSION-GUIDANCE-ANCHOR-v1|";
    private readonly string _path;
    private readonly string _lockPath;
    private readonly string _anchorPath;

    public ProfessionProductionFileJournal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Journal path required.", nameof(path));
        _path = Path.GetFullPath(path);
        _lockPath = _path + ".lock";
        _anchorPath = _path + ".anchor";
    }

    public ProfessionProductionJournalRead Read()
    {
        try { return ReadInternal(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(ProfessionProductionJournalStatus.Unavailable, 0, null); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or
            ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return new(ProfessionProductionJournalStatus.Corrupt, 0, null); }
    }

    public ProfessionProductionJournalStatus CompareExchange(
        long expectedSequence, ProfessionProductionJournalEntry next)
    {
        if (expectedSequence < 0 || expectedSequence == long.MaxValue ||
            next is null || next.Sequence != expectedSequence + 1 ||
            next.RecoveryState is null || !ValidEntry(next))
            return ProfessionProductionJournalStatus.Corrupt;

        string? temp = null;
        string? anchorTemp = null;
        FileStream? gate = null;
        bool locked = false;
        try
        {
            // FileShare.ReadWrite is required so competing processes reach Lock;
            // an inability to acquire the exclusive region fails closed.
            gate = new FileStream(_lockPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.ReadWrite);
            gate.Lock(0, 1);
            locked = true;
            var previous = ReadInternal();
            if (previous.Status is not (ProfessionProductionJournalStatus.Empty or ProfessionProductionJournalStatus.Current))
                return previous.Status;
            if (previous.Sequence != expectedSequence)
                return ProfessionProductionJournalStatus.Conflict;
            // Revalidate under the cross-process CAS lock. The incoming record
            // may contain caller-owned mutable lists that changed after the
            // initial shape check, including consumed receipt identities.
            if (!ValidEntry(next) ||
                previous.Entry is { } old && !ValidTransition(old, next))
                return ProfessionProductionJournalStatus.Corrupt;
            var file = new StoredJournal(next, Digest(next));
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(file);
            if (data.Length > MaxJournalBytes) return ProfessionProductionJournalStatus.Corrupt;
            temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var output = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(data);
                output.Flush(flushToDisk: true);
            }
            anchorTemp = _anchorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var anchor = new FileStream(anchorTemp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] stamp = System.Text.Encoding.UTF8.GetBytes(AnchorFor(next, file.Digest));
                anchor.Write(stamp);
                anchor.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            temp = null;
            // The anchor is updated last. An interruption between the two
            // renames causes a detectable mismatch and blocks subsequent work.
            File.Move(anchorTemp, _anchorPath, overwrite: true);
            anchorTemp = null;
            return ProfessionProductionJournalStatus.Current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return ProfessionProductionJournalStatus.Unavailable; }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (anchorTemp is not null)
            {
                try { File.Delete(anchorTemp); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (locked)
            {
                try { gate!.Unlock(0, 1); } catch (IOException) { }
            }
            gate?.Dispose();
        }
    }

    private ProfessionProductionJournalRead ReadInternal()
    {
        if (!File.Exists(_path))
            return new(File.Exists(_anchorPath) ? ProfessionProductionJournalStatus.Corrupt :
                ProfessionProductionJournalStatus.Empty, 0, null);
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (input.Length is <= 0 or > MaxJournalBytes)
            return new(ProfessionProductionJournalStatus.Corrupt, 0, null);
        byte[] buffer = new byte[checked((int)input.Length)];
        input.ReadExactly(buffer);
        StoredJournal? stored;
        try { stored = JsonSerializer.Deserialize<StoredJournal>(buffer); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { return new(ProfessionProductionJournalStatus.Corrupt, 0, null); }
        if (stored?.Entry is not { } entry || stored.Digest is null ||
            !ValidEntry(entry) ||
            !string.Equals(stored.Digest, Digest(entry), StringComparison.Ordinal) ||
            !File.Exists(_anchorPath) ||
            !AnchorMatches(entry, stored.Digest))
            return new(ProfessionProductionJournalStatus.Corrupt, 0, null);
        return new(ProfessionProductionJournalStatus.Current, entry.Sequence, entry);
    }

    private static bool ValidEntry(ProfessionProductionJournalEntry value)
    {
        if (value.Sequence <= 0 ||
            value.RecoveryState is not { Checkpoint: not null, LastEvidence: not null, Proposal: not null } state ||
            state.LastEvidence.Receipt is null || state.LastEvidence.Observation is null ||
            state.LastEvidence.Observation.Inventory is null ||
            state.LastEvidence.Observation.Sources is null ||
            state.Proposal.Steps is null ||
            state.Proposal.UsedStock is null ||
            state.Proposal.RemainingStock is null ||
            state.Proposal.Warnings is null ||
            state.Checkpoint.Revision != state.LastEvidence.Observation.Revision)
            return false;
        // A journal is a bounded checkpoint, not an unbounded event log. Reject
        // duplicate or malformed receipt identities even when the pending step
        // is absent (completed checkpoint) so replay history survives restarts.
        if (value.ConsumedReceiptIds is { } receipts &&
            (receipts.Count > 2048 || receipts.Any(r => r is null or { Length: 0 or > 128 } ||
                !r.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':')) ||
             receipts.Distinct(StringComparer.Ordinal).Count() != receipts.Count))
            return false;
        if (value.ConsumedV9RunIds is { } runs &&
            (runs.Count > 2048 || runs.Any(id => id == Guid.Empty) ||
             runs.Distinct().Count() != runs.Count))
            return false;
        var p = value.Pending;
        return p is null
            ? state.Proposal.IsComplete && state.Proposal.Steps.All(s =>
                s.Kind == PlanStepKind.UseInventory)
            : p.ActionKey is { Length: 64 } && p.ActionKey.All(Uri.IsHexDigit) &&
              p.SessionKey is { Length: > 0 } &&
              p.EvidenceContract is null or "V9_EXACT" &&
              (p.Kind is "GATHER" or "DROP" or "BUY" or "CRAFT") &&
              p.ObservationRevision == state.Checkpoint.Revision && p.StepId > 0 &&
              p.ItemGid > 0 && p.Quantity > 0 &&
              state.Proposal.Steps.Any(s => s.Id == p.StepId &&
                  string.Equals(s.Kind.ToString(), p.Kind, StringComparison.OrdinalIgnoreCase) &&
                  s.ItemGid == p.ItemGid && s.Quantity == p.Quantity &&
                  s.RecipeId == p.RecipeId) &&
              state.LastEvidence.Receipt.SessionKey == p.SessionKey;
    }

    private static bool ValidTransition(ProfessionProductionJournalEntry old,
        ProfessionProductionJournalEntry next)
    {
        // The file journal independently enforces append-only receipt/run
        // histories; a stale or buggy caller cannot silently drop entries and
        // thereby make a previously consumed receipt eligible again.
        static bool HistoryIsAppendOnly<T>(IReadOnlyList<T>? before,
            IReadOnlyList<T>? after)
        {
            int oldCount = before?.Count ?? 0;
            int newCount = after?.Count ?? 0;
            return newCount >= oldCount && newCount <= oldCount + 1 &&
                (before is null || after is not null &&
                    before.SequenceEqual(after.Take(oldCount)));
        }

        if (!HistoryIsAppendOnly(old.ConsumedReceiptIds, next.ConsumedReceiptIds) ||
            !HistoryIsAppendOnly(old.ConsumedV9RunIds, next.ConsumedV9RunIds))
            return false;
        int receiptsAdded = (next.ConsumedReceiptIds?.Count ?? 0) -
            (old.ConsumedReceiptIds?.Count ?? 0);
        int runsAdded = (next.ConsumedV9RunIds?.Count ?? 0) -
            (old.ConsumedV9RunIds?.Count ?? 0);
        if (runsAdded > receiptsAdded ||
            // Completed is terminal. A second click must never write another
            // checkpoint or reopen an already finished objective.
            old.Pending is null ||
            old.Pending?.EvidenceContract == "V9_EXACT" &&
                next.Pending is { EvidenceContract: not "V9_EXACT" } ||
            old.RecoveryState.Checkpoint.ItemGid != next.RecoveryState.Checkpoint.ItemGid ||
            old.RecoveryState.Checkpoint.ItemQuantity != next.RecoveryState.Checkpoint.ItemQuantity ||
            old.RecoveryState.Checkpoint.Mode != next.RecoveryState.Checkpoint.Mode ||
            old.RecoveryState.Checkpoint.RequestFingerprint !=
                next.RecoveryState.Checkpoint.RequestFingerprint ||
            old.RecoveryState.LastEvidence.Observation.SubjectKey !=
                next.RecoveryState.LastEvidence.Observation.SubjectKey ||
            old.RecoveryState.LastEvidence.Receipt.SessionKey !=
                next.RecoveryState.LastEvidence.Receipt.SessionKey ||
            next.RecoveryState.Checkpoint.Revision < old.RecoveryState.Checkpoint.Revision)
            return false;
        if (old.Pending is { EvidenceContract: "V9_EXACT" } pending &&
            (next.RecoveryState.Checkpoint.Revision <= old.RecoveryState.Checkpoint.Revision ||
             // Every strict step needs exactly one independently attributed
             // receipt. A craft additionally consumes exactly one completed
             // V9 attempt; a gathering/buy/drop step must consume no V9 run.
             receiptsAdded != 1 ||
             runsAdded != (pending.Kind == "CRAFT" ? 1 : 0)))
            return false;
        return true;
    }

    private static string Digest(ProfessionProductionJournalEntry entry) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entry)));

    private static string AnchorFor(ProfessionProductionJournalEntry entry, string digest) =>
        AnchorHeader + entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "|" + digest + "\n";

    private bool AnchorMatches(ProfessionProductionJournalEntry entry, string digest)
    {
        using var anchor = new FileStream(_anchorPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (anchor.Length is <= 0 or > MaxAnchorBytes) return false;
        var bytes = new byte[(int)anchor.Length];
        anchor.ReadExactly(bytes);
        return string.Equals(System.Text.Encoding.UTF8.GetString(bytes),
            AnchorFor(entry, digest), StringComparison.Ordinal);
    }

    public sealed record StoredJournal(ProfessionProductionJournalEntry Entry, string Digest);
}
