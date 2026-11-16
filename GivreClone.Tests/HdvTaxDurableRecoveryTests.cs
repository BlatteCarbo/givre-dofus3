extern alias F06App;

using HdvTaxReservations = F06App::GivreClone.App.Services.HdvTaxReservations;
using HdvTaxOrderReservation = F06App::GivreClone.App.Services.HdvTaxOrderReservation;
using HdvTaxOrderPhase = F06App::GivreClone.App.Services.HdvTaxOrderPhase;
using HdvTaxProfileRecoveryStatus = F06App::GivreClone.App.Services.HdvTaxProfileRecoveryStatus;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Real file-system crash/reopen scenarios, never game traffic.</summary>
public sealed class HdvTaxDurableRecoveryTests
{
    private sealed class IsolatedJournal : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(),
            "givre-hdv-taxes-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(_folder, "hdv", "reservations.journal");
        public HdvTaxReservations Open() => new(PathName);
        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Crash_reopen_restores_unconfirmed_fee_and_denies_duplicate_order()
    {
        using var test = new IsolatedJournal();
        var beforeCrash = test.Open();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            beforeCrash.TryReserveOrder(17, "server:292:A", "sell:uid73:10:50:100", 100, 0, 40));
        // Crash: no "sent" callback, no receipt, no session in memory.
        var afterCrash = test.Open();
        Assert.Equal(40, afterCrash.PendingDurable("server:292:A"));
        Assert.Equal(HdvTaxOrderReservation.AlreadyReserved,
            afterCrash.TryReserveOrder(9999, "server:292:A", "sell:uid73:10:50:100", 100, 0, 40));
        Assert.Equal(HdvTaxOrderReservation.CapExceeded,
            afterCrash.TryReserveOrder(9999, "server:292:A", "sell:uid73:10:51:100", 100, 30, 31));
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            afterCrash.TryReserveOrder(9999, "server:292:A", "sell:uid73:10:51:90", 100, 30, 30));
        Assert.Equal(70, test.Open().PendingDurable("server:292:A"));
    }

    [Fact]
    public void Reprice_uid_and_scoped_server_are_distinct_but_global_profile_limit_remains()
    {
        using var test = new IsolatedJournal();
        var first = test.Open();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            first.TryReserveOrder(111, "server:292:SameName", "reprice:20:10:999", 25, 0, 20));
        Assert.Equal(HdvTaxOrderReservation.CapExceeded,
            test.Open().TryReserveOrder(222, "server:292:SameName", "reprice:21:10:999", 25, 0, 6));
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(222, "server:353:SameName", "reprice:20:10:999", 25, 0, 20));
        Assert.Equal(20, test.Open().PendingDurable("server:353:SameName"));
    }

    [Fact]
    public void Interrupted_append_is_not_treated_as_a_clean_empty_ledger()
    {
        using var test = new IsolatedJournal();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(1, "server:292:Character", "sell:uid1:1:100:2", 200, 0, 10));
        string bytes = File.ReadAllText(test.PathName);
        File.WriteAllText(test.PathName, bytes + "1|partial");
        Assert.Equal(long.MaxValue, test.Open().PendingDurable("server:292:Character"));
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "sell:uid2:1:100:2", 200, 0, 10));
        Assert.Equal(bytes + "1|partial", File.ReadAllText(test.PathName));
    }

    [Fact]
    public void Corrupt_complete_record_is_preserved_and_blocks_new_sends()
    {
        using var test = new IsolatedJournal();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(1, "server:292:Character", "sell:uid1:1:100:2", 200, 0, 10));
        string valid = File.ReadAllText(test.PathName);
        string corrupt = valid.Replace("|10|", "|90|", StringComparison.Ordinal);
        Assert.NotEqual(valid, corrupt);
        File.WriteAllText(test.PathName, corrupt);
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "new:uid", 200, 0, 10));
        Assert.Equal(corrupt, File.ReadAllText(test.PathName));
    }

    [Fact]
    public void Lost_journal_or_anchor_cannot_restore_an_empty_spending_budget()
    {
        using var test = new IsolatedJournal();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(1, "server:292:Character", "sell:uid1:1:100:2", 200, 0, 10));
        string saved = File.ReadAllText(test.PathName);
        File.Delete(test.PathName);
        Assert.Equal(long.MaxValue, test.Open().PendingDurable("server:292:Character"));
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "another", 200, 0, 10));
        File.WriteAllText(test.PathName, saved);
        File.Delete(test.PathName + ".anchor");
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "another", 200, 0, 10));
        Assert.Equal(saved, File.ReadAllText(test.PathName));
    }

    [Fact]
    public void Crash_after_anchor_before_first_entry_is_fail_closed()
    {
        using var test = new IsolatedJournal();
        Directory.CreateDirectory(Path.GetDirectoryName(test.PathName)!);
        File.WriteAllText(test.PathName + ".anchor", "GIVRE-HDV-TAX-ANCHOR-v1|0|NONE\n");
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "new", 200, 0, 10));
        Assert.Equal(0L, new FileInfo(test.PathName).Length);
    }

    [Fact]
    public void Unwritable_journal_is_not_silently_replaced_with_memory()
    {
        using var test = new IsolatedJournal();
        Directory.CreateDirectory(test.PathName); // file path is a directory
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "uid1", 200, 0, 10));
        Assert.True(Directory.Exists(test.PathName));
    }

    [Fact]
    public void Truncation_at_a_complete_record_boundary_still_fails_closed()
    {
        using var test = new IsolatedJournal();
        var ledger = test.Open();
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            ledger.TryReserveOrder(1, "server:292:Character", "order-a", 100, 0, 10));
        string first = File.ReadAllText(test.PathName);
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            ledger.TryReserveOrder(1, "server:292:Character", "order-b", 100, 0, 10));
        File.WriteAllText(test.PathName, first);
        Assert.Equal(long.MaxValue, test.Open().PendingDurable("server:292:Character"));
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            test.Open().TryReserveOrder(1, "server:292:Character", "order-c", 100, 0, 10));
        Assert.Equal(first, File.ReadAllText(test.PathName));
    }

    [Fact]
    public async Task Concurrent_instances_enforce_cap_by_locking_the_same_journal()
    {
        using var test = new IsolatedJournal();
        var attempts = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            test.Open().TryReserveOrder((uint)(i + 1), "server:292:Char",
                "sell:" + i, 100, 0, 10))).ToArray();
        var outcomes = await Task.WhenAll(attempts);
        Assert.Equal(10, outcomes.Count(x => x == HdvTaxOrderReservation.Reserved));
        Assert.Equal(100, test.Open().PendingDurable("server:292:Char"));
        Assert.DoesNotContain(outcomes, x => x == HdvTaxOrderReservation.Unavailable);
    }

    [Fact]
    public void Unknown_scope_invalid_cap_and_invalid_fee_never_touch_storage()
    {
        using var test = new IsolatedJournal();
        var ledger = test.Open();
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            ledger.TryReserveOrder(1, "?", "sell:uid", 100, 0, 10));
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            ledger.TryReserveOrder(1, "server:292:Char", "sell:uid", 100, 0, 0));
        Assert.Equal(HdvTaxOrderReservation.Unavailable,
            ledger.TryReserveOrder(1, "server:292:Char", "sell:uid", -1, 0, 10));
        Assert.False(File.Exists(test.PathName));
    }

    [Fact]
    public void Recovery_snapshot_reports_uncertain_vs_observed_without_releasing_tax()
    {
        using var test = new IsolatedJournal();
        const string profile = "server:292:Character";
        var ledger = test.Open();
        Assert.Equal(HdvTaxProfileRecoveryStatus.Ready,
            ledger.ReadProfileSnapshot(profile).RecoveryStatus);
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            ledger.TryReserveOrder(13, profile, "sell-a", 100, 0, 10));
        Assert.Equal(HdvTaxProfileRecoveryStatus.Ready,
            test.Open().ReadProfileSnapshot(profile).RecoveryStatus);
        Assert.True(ledger.TryAdvanceOrder(13, profile, "sell-a", HdvTaxOrderPhase.Attempted));
        Assert.Equal(HdvTaxProfileRecoveryStatus.NeedsReconciliation,
            test.Open().ReadProfileSnapshot(profile).RecoveryStatus);
        Assert.True(ledger.TryAdvanceOrder(13, profile, "sell-a", HdvTaxOrderPhase.Observed));
        var observed = test.Open().ReadProfileSnapshot(profile);
        Assert.Equal(HdvTaxProfileRecoveryStatus.Ready, observed.RecoveryStatus);
        Assert.Equal(10, observed.ReservedFees);
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            ledger.TryReserveOrder(13, profile, "reprice-b", 100, 0, 12));
        Assert.True(ledger.TryAdvanceOrder(13, profile, "reprice-b", HdvTaxOrderPhase.Attempted));
        Assert.True(ledger.TryAdvanceOrder(13, profile, "reprice-b", HdvTaxOrderPhase.Uncertain));
        Assert.Equal(HdvTaxProfileRecoveryStatus.NeedsReconciliation,
            test.Open().ReadProfileSnapshot(profile).RecoveryStatus);
        Assert.Equal(22, test.Open().ReadProfileSnapshot(profile).ReservedFees);
        Assert.Equal(HdvTaxProfileRecoveryStatus.Ready,
            test.Open().ReadProfileSnapshot("server:353:Other").RecoveryStatus);
    }

    [Fact]
    public void Corrupt_journal_never_appears_ready_or_zero_in_classic_preview()
    {
        using var test = new IsolatedJournal();
        const string profile = "server:292:Character";
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(17, profile, "reprice-a", 100, 0, 10));
        string bad = File.ReadAllText(test.PathName) + "2|corrupt\n";
        File.WriteAllText(test.PathName, bad);
        var snapshot = test.Open().ReadProfileSnapshot(profile);
        Assert.Equal(HdvTaxProfileRecoveryStatus.Unavailable, snapshot.RecoveryStatus);
        Assert.Equal(long.MaxValue, snapshot.ReservedFees);
        Assert.Equal(bad, File.ReadAllText(test.PathName));
    }

    [Fact]
    public void Stale_uncommitted_anchor_staging_bytes_are_preserved_but_never_trusted()
    {
        using var test = new IsolatedJournal();
        const string profile = "server:292:Character";
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(1, profile, "first", 100, 0, 10));
        string staging = test.PathName + ".anchor.pending-interrupted";
        File.WriteAllText(staging, "interrupted/untrusted/corrupt bytes");
        Assert.Equal(HdvTaxOrderReservation.Reserved,
            test.Open().TryReserveOrder(2, profile, "second", 100, 0, 10));
        Assert.Equal("interrupted/untrusted/corrupt bytes", File.ReadAllText(staging));
        Assert.Equal(20, test.Open().PendingDurable(profile));
        Assert.Equal(HdvTaxProfileRecoveryStatus.Ready,
            test.Open().ReadProfileSnapshot(profile).RecoveryStatus);
    }
}
