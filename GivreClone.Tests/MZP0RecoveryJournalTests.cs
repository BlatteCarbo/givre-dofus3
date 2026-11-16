using System.Text.Json;
using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Pure offline fault/restart tests: no socket, game process, private fixture or credentials.
/// </summary>
public sealed class MZP0RecoveryJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mzp0-tests-" + Guid.NewGuid().ToString("N"));
    private string PathToJournal => Path.Combine(_directory, "journal.json");
    private static readonly MZP0SessionIdentity Origin = new(7654, 638960000000000000L, 22, "3.7.10.10");

    public MZP0RecoveryJournalTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* only a failed test should leave locked fixtures */ }
    }

    private static MZP0JournalRecord Prepare(MZP0RecoveryJournal store, MZP0SessionGeneration session,
        Guid? operationId = null, MZP0DeliverySemantics semantics = MZP0DeliverySemantics.NonIdempotent,
        int uid = 41) => store.BeginIntent(session, operationId ?? Guid.NewGuid(), uid,
            MZP0OperationCategory.Economy, semantics);

    [Fact]
    public void Persisted_intent_is_loaded_but_no_generation_is_live_after_reopen()
    {
        Guid id;
        MZP0SessionGeneration old;
        using (var store = new MZP0RecoveryJournal(PathToJournal))
        {
            old = store.Activate(Origin);
            id = Prepare(store, old).OperationId;
            Assert.Equal(MZP0RecoveryDisposition.AwaitingFirstDispatch, store.Assess(id));
            Assert.Equal(2, store.Revision);
            Assert.True(File.Exists(PathToJournal));
            Assert.False(File.Exists(PathToJournal + ".tmp"));
        }

        using var opened = new MZP0RecoveryJournal(PathToJournal);
        Assert.Null(opened.ActiveGeneration);
        Assert.Equal(MZP0RecoveryDisposition.RequiresExplicitReconciliation, opened.Assess(id));
        Assert.Throws<InvalidOperationException>(() => opened.OpenDispatchWindow(old, id));
        var current = opened.Activate(Origin);
        Assert.True(current.Number > old.Number);
        Assert.NotEqual(current.Nonce, old.Nonce);
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(opened.Records).State);
        Assert.Throws<InvalidOperationException>(() => opened.OpenDispatchWindow(current, id));
    }

    [Theory]
    [InlineData(MZP0DeliverySemantics.NonIdempotent)]
    [InlineData(MZP0DeliverySemantics.Idempotent)]
    public void Crash_during_send_always_becomes_uncertain_without_automatic_replay(MZP0DeliverySemantics semantics)
    {
        Guid id;
        using (var journal = new MZP0RecoveryJournal(PathToJournal))
        {
            var session = journal.Activate(Origin);
            id = Prepare(journal, session, semantics: semantics).OperationId;
            var permit = journal.OpenDispatchWindow(session, id);
            Assert.Equal(id, permit.OperationId);
            Assert.Equal(41, permit.CorrelationUid);
            Assert.Equal(MZP0RecoveryDisposition.AwaitingVerifiedAcknowledgment, journal.Assess(id));
            Assert.Throws<InvalidOperationException>(() => journal.OpenDispatchWindow(session, id));
        }

        using var recovered = new MZP0RecoveryJournal(PathToJournal);
        Assert.Equal(MZP0RecoveryDisposition.RequiresExplicitReconciliation, recovered.Assess(id));
        var fresh = recovered.Activate(Origin);
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(recovered.Records).State);
        Assert.Throws<InvalidOperationException>(() => recovered.OpenDispatchWindow(fresh, id));
        Assert.Throws<InvalidOperationException>(() => recovered.TryAcknowledge(fresh, id,
            new MZP0AcknowledgmentEvidence(fresh, 41, true)));
    }

    [Fact]
    public void Source_generation_uid_and_positive_ack_all_must_match()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var session = journal.Activate(Origin);
        var record = Prepare(journal, session);
        journal.OpenDispatchWindow(session, record.OperationId);
        var foreign = session with { Identity = Origin with { ConnectionId = 23 } };
        var otherNonce = session with { Nonce = Guid.NewGuid() };

        Assert.False(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(session, 41, false)));
        Assert.False(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(session, 42, true)));
        Assert.False(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(foreign, 41, true)));
        Assert.False(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(otherNonce, 41, true)));
        Assert.Throws<InvalidOperationException>(() => journal.TryAcknowledge(foreign, record.OperationId,
            new MZP0AcknowledgmentEvidence(session, 41, true)));
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);

        Assert.True(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(session, 41, true)));
        Assert.Equal(MZP0RecoveryDisposition.Acknowledged, journal.Assess(record.OperationId));
        Assert.False(journal.TryAcknowledge(session, record.OperationId,
            new MZP0AcknowledgmentEvidence(session, 41, true)));
    }

    [Fact]
    public void Prior_generation_cannot_accept_ack_or_change_current_session_even_if_pid_reused()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var first = journal.Activate(Origin);
        var oldRecord = Prepare(journal, first);
        journal.OpenDispatchWindow(first, oldRecord.OperationId);
        var second = journal.Activate(Origin);
        Assert.NotEqual(first, second);

        Assert.Throws<InvalidOperationException>(() => journal.MarkUncertain(first, oldRecord.OperationId));
        Assert.Throws<InvalidOperationException>(() => journal.TryAcknowledge(first, oldRecord.OperationId,
            new MZP0AcknowledgmentEvidence(first, 41, true)));
        Assert.Throws<InvalidOperationException>(() => Prepare(journal, first));
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(journal.Records).State);
        var newRecord = Prepare(journal, second);
        Assert.Equal(second.Number, newRecord.Generation);
    }

    [Fact]
    public void Cancel_allowed_only_before_dispatch_send_failure_is_uncertain()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var session = journal.Activate(Origin);
        var before = Prepare(journal, session);
        journal.CancelBeforeDispatch(session, before.OperationId);
        Assert.Equal(MZP0RecoveryDisposition.CancelledBeforeDispatch, journal.Assess(before.OperationId));
        Assert.Throws<InvalidOperationException>(() => journal.OpenDispatchWindow(session, before.OperationId));

        var during = Prepare(journal, session, uid: 42);
        Assert.Throws<InvalidOperationException>(() => journal.MarkUncertain(session, during.OperationId));
        journal.OpenDispatchWindow(session, during.OperationId);
        Assert.Throws<InvalidOperationException>(() => journal.CancelBeforeDispatch(session, during.OperationId));
        journal.MarkUncertain(session, during.OperationId);
        Assert.Equal(MZP0RecoveryDisposition.RequiresExplicitReconciliation, journal.Assess(during.OperationId));
        Assert.Throws<InvalidOperationException>(() => journal.MarkUncertain(session, during.OperationId));
    }

    [Fact]
    public void Capacity_is_bounded_and_uncertain_entries_are_never_pruned()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal, maxEntries: 2);
        var session = journal.Activate(Origin);
        var uncertain = Prepare(journal, session, uid: 5);
        journal.OpenDispatchWindow(session, uncertain.OperationId);
        journal.MarkUncertain(session, uncertain.OperationId);
        var cancel = Prepare(journal, session, uid: 6);
        journal.CancelBeforeDispatch(session, cancel.OperationId);

        Assert.Throws<InvalidOperationException>(() => Prepare(journal, session, uid: 7));
        Assert.Equal(1, journal.PruneTerminal(session));
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(journal.Records).State);
        Assert.Equal(0, journal.PruneTerminal(session));
        var latest = Prepare(journal, session, uid: 7);
        Assert.NotEqual(uncertain.OperationId, latest.OperationId);
        Assert.Equal(2, journal.Records.Count);
    }

    [Fact]
    public void Duplicate_operation_and_invalid_identity_or_classification_fail_closed()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        Assert.Throws<ArgumentException>(() => journal.Activate(Origin with { ProcessStartUtcTicks = 0 }));
        Assert.Throws<ArgumentException>(() => journal.Activate(Origin with { ProtocolVersion = "secret / path" }));
        var session = journal.Activate(Origin);
        var id = Guid.NewGuid();
        Prepare(journal, session, id);
        Assert.Throws<InvalidOperationException>(() => Prepare(journal, session, id));
        Assert.Throws<ArgumentException>(() => journal.BeginIntent(session, Guid.NewGuid(), 1,
            (MZP0OperationCategory)200, MZP0DeliverySemantics.Idempotent));
        Assert.Throws<ArgumentOutOfRangeException>(() => Prepare(journal, session, uid: 0));
        Assert.Single(journal.Records);
    }

    [Fact]
    public void Correlation_uid_remains_unique_after_terminal_records_have_been_pruned()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var generation = journal.Activate(Origin);
        var record = Prepare(journal, generation, uid: 73);
        journal.CancelBeforeDispatch(generation, record.OperationId);
        Assert.Equal(1, journal.PruneTerminal(generation));
        Assert.Empty(journal.Records);
        Assert.Throws<InvalidOperationException>(() => Prepare(journal, generation, uid: 73));
        Assert.Throws<InvalidOperationException>(() => Prepare(journal, generation, uid: 72));
        Assert.Equal(74, Prepare(journal, generation, uid: 74).CorrelationUid);
    }

    [Theory]
    [InlineData(MZP0ReconciliationConclusion.Applied, MZP0DeliveryState.ReconciledApplied)]
    [InlineData(MZP0ReconciliationConclusion.DefinitelyNotApplied, MZP0DeliveryState.ReconciledNotApplied)]
    public void Explicit_reconciliation_is_bound_to_original_uncertain_generation(
        MZP0ReconciliationConclusion conclusion, MZP0DeliveryState expectedState)
    {
        Guid id;
        MZP0SessionGeneration old;
        using (var beforeCrash = new MZP0RecoveryJournal(PathToJournal))
        {
            old = beforeCrash.Activate(Origin);
            id = Prepare(beforeCrash, old, uid: 17).OperationId;
            beforeCrash.OpenDispatchWindow(old, id);
        }

        using var resumed = new MZP0RecoveryJournal(PathToJournal);
        var current = resumed.Activate(Origin);
        var proof = new MZP0ReconciliationEvidence(old.Number, old.Nonce, 17, conclusion, true);
        Assert.False(resumed.TryReconcile(current, id, proof with { AuthorityVerified = false }));
        Assert.False(resumed.TryReconcile(current, id, proof with { CorrelationUid = 18 }));
        Assert.False(resumed.TryReconcile(current, id, proof with { OriginalNonce = Guid.NewGuid() }));
        Assert.False(resumed.TryReconcile(current, id, proof with { Conclusion = (MZP0ReconciliationConclusion)50 }));
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(resumed.Records).State);
        Assert.True(resumed.TryReconcile(current, id, proof));
        Assert.Equal(expectedState, Assert.Single(resumed.Records).State);
        Assert.False(resumed.TryReconcile(current, id, proof));
        Assert.Throws<InvalidOperationException>(() => resumed.OpenDispatchWindow(current, id));
        Assert.Equal(1, resumed.PruneTerminal(current));
        Assert.Empty(resumed.Records);
    }

    [Fact]
    public void Single_writer_file_lease_prevents_competing_process_instances()
    {
        using var first = new MZP0RecoveryJournal(PathToJournal);
        first.Activate(Origin);
        Assert.Throws<IOException>(() => new MZP0RecoveryJournal(PathToJournal));
        Assert.NotNull(first.ActiveGeneration);
    }

    [Fact]
    public void Corrupted_primary_is_not_restored_from_older_backup_or_overwritten()
    {
        using (var first = new MZP0RecoveryJournal(PathToJournal))
        {
            var session = first.Activate(Origin);
            Prepare(first, session);
            Assert.True(File.Exists(PathToJournal + ".bak"));
        }
        const string corrupt = "{ truncated JSON";
        File.WriteAllText(PathToJournal, corrupt);
        Assert.Throws<InvalidDataException>(() => new MZP0RecoveryJournal(PathToJournal));
        Assert.Equal(corrupt, File.ReadAllText(PathToJournal));
        Assert.True(File.Exists(PathToJournal + ".bak"));
    }

    [Fact]
    public void Snapshot_checksum_rejects_valid_json_with_mutated_decision()
    {
        using (var journal = new MZP0RecoveryJournal(PathToJournal))
        {
            var generation = journal.Activate(Origin);
            Prepare(journal, generation);
        }

        // Mutate one valid enum value without refreshing the envelope checksum.
        string json = File.ReadAllText(PathToJournal);
        Assert.Contains("\"State\":0", json);
        string modified = json.Replace("\"State\":0", "\"State\":2", StringComparison.Ordinal);
        File.WriteAllText(PathToJournal, modified);
        Assert.Throws<InvalidDataException>(() => new MZP0RecoveryJournal(PathToJournal));
        Assert.Equal(modified, File.ReadAllText(PathToJournal));
    }

    [Fact]
    public void Stale_temporary_file_is_ignored_when_primary_committed_and_valid()
    {
        using (var journal = new MZP0RecoveryJournal(PathToJournal))
            journal.Activate(Origin);
        File.WriteAllText(PathToJournal + ".tmp", "possibly incomplete future intent");

        using var reopened = new MZP0RecoveryJournal(PathToJournal);
        Assert.Equal(1, reopened.Revision);
        Assert.False(File.Exists(PathToJournal + ".tmp"));
        Assert.Equal(2, reopened.Activate(Origin).Number);
    }

    [Theory]
    [InlineData(".tmp")]
    [InlineData(".bak")]
    [InlineData(".lck")]
    public void Lost_primary_with_any_recovery_artifact_never_starts_blank(string suffix)
    {
        File.WriteAllText(PathToJournal + suffix, "orphan marker");
        Assert.ThrowsAny<Exception>(() => new MZP0RecoveryJournal(PathToJournal));
        Assert.False(File.Exists(PathToJournal));
    }

    [Fact]
    public void Missing_primary_after_commit_blocks_next_write_and_poisoned_journal()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var session = journal.Activate(Origin);
        File.Delete(PathToJournal);
        Assert.Throws<InvalidDataException>(() => Prepare(journal, session));
        Assert.Throws<InvalidOperationException>(() => journal.Activate(Origin));
        Assert.False(File.Exists(PathToJournal));
    }

    [Fact]
    public void Snapshots_are_bounded_and_rejected_mutation_does_not_poison_journal()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal, maxEntries: 128, maxSnapshotBytes: 1024);
        var session = journal.Activate(Origin);
        int successful = 0;
        for (int i = 0; i < 16; i++)
        {
            try { Prepare(journal, session, uid: i + 1); successful++; }
            catch (InvalidOperationException) { break; }
        }
        Assert.InRange(successful, 1, 15);
        Assert.True(new FileInfo(PathToJournal).Length <= 1024);
        // A validation failure did not alter the last successful persisted state.
        Assert.Equal(successful, journal.Records.Count);
        Assert.NotNull(journal.ActiveGeneration);
        var first = journal.Records[0];
        journal.CancelBeforeDispatch(session, first.OperationId);
        Assert.Equal(MZP0DeliveryState.CancelledBeforeDispatch,
            journal.Records.First(r => r.OperationId == first.OperationId).State);
    }

    [Fact]
    public void Journal_contains_only_categorical_metadata_without_payload_or_secrets()
    {
        using var journal = new MZP0RecoveryJournal(PathToJournal);
        var session = journal.Activate(Origin);
        Prepare(journal, session);
        string persisted = File.ReadAllText(PathToJournal);
        using var doc = JsonDocument.Parse(persisted);
        Assert.NotNull(doc.RootElement.GetProperty("Sha256").GetString());
        Assert.DoesNotContain("PayloadBytes", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountId", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Character", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Origin.ProtocolVersion, persisted);
    }
}
