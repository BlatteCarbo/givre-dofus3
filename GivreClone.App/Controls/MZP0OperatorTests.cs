#if MZP0_TESTS
using System;
using System.Linq;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.App.Controls;

public sealed class MZP0OperatorTests
{
    private static MZP0SessionIdentity Identity => new(
        "fixture-only", 25147, DateTimeOffset.UtcNow.AddMinutes(-2), 4, "3.7.10.10", new string('a', 64));

    private static MZP0CandidateObservation Candidate(MZP0SessionIdentity identity, long sequence = 1,
        MZP0SignalKind kind = MZP0SignalKind.Decoder,
        MZP0Direction direction = MZP0Direction.ServerToClient,
        string? correlation = null)
        => new(identity, "fixture-source", $"fixture-{sequence}", sequence,
            DateTimeOffset.UtcNow, direction, kind, correlation);

    private static (MZP0OperatorViewModel Vm, FakeSource Source, FakeAuthority Authority, MZP0SessionIdentity Identity) Bound()
    {
        var id = Identity;
        var source = new FakeSource();
        var authority = new FakeAuthority();
        var vm = new MZP0OperatorViewModel();
        vm.Bind(source, authority, id, "fixture-source");
        return (vm, source, authority, id);
    }

    [Fact]
    public void Default_is_unknown_with_no_session_or_ack()
    {
        using var vm = new MZP0OperatorViewModel();
        Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
        Assert.Equal("INCONNU", vm.AckStatus);
        Assert.False(vm.Accept(Candidate(Identity)));
        Assert.Contains(vm.VisibleJournal, line => line.Code == "NO_SOURCE");
    }

    [Fact]
    public void External_proof_attribues_observation_but_never_claims_ack()
    {
        var (vm, _, _, id) = Bound();
        using (vm)
        {
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
            Assert.True(vm.Accept(Candidate(id)));
            Assert.Equal("SOURCE ATTRIBUÉE", vm.SessionStatus);
            Assert.Equal("INCONNU", vm.AckStatus);
            Assert.Equal(1, vm.AttributedCount);
        }
    }

    [Fact]
    public void Genuine_external_ack_requires_inbound_server_response_and_matching_correlation()
    {
        var (vm, _, authority, id) = Bound();
        using (vm)
        {
            authority.Ack = true;
            Assert.False(vm.Accept(Candidate(id, kind: MZP0SignalKind.Map, correlation: "uid-01")));
            Assert.Equal("INCONNU", vm.AckStatus);
            Assert.True(vm.Accept(Candidate(id, 2, MZP0SignalKind.ServerResponse,
                MZP0Direction.ServerToClient, "uid-02")));
            Assert.Equal("ACK CORRÉLÉ", vm.AckStatus);
            vm.Unbind();
            Assert.Equal("INCONNU", vm.AckStatus);
        }
    }

    [Fact]
    public void Client_identity_mismatch_invalidates_previously_attributed_session()
    {
        var (vm, _, _, id) = Bound();
        using (vm)
        {
            Assert.True(vm.Accept(Candidate(id)));
            Assert.False(vm.Accept(Candidate(id with { Generation = 5 }, 2)));
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
            Assert.Contains(vm.VisibleJournal, line => line.Code == "IDENTITY_MISMATCH");
        }
    }

    [Fact]
    public void Source_cannot_self_attest()
    {
        var both = new SelfAttester();
        using var vm = new MZP0OperatorViewModel();
        vm.Bind(both, both, Identity, "fixture-source");
        Assert.False(vm.Accept(Candidate(Identity)));
        Assert.Contains(vm.VisibleJournal, line => line.Code == "SELF_ATTESTATION");
    }

    [Fact]
    public void Stale_observation_is_rejected()
    {
        var (vm, _, _, id) = Bound();
        using (vm)
        {
            var stale = Candidate(id) with { ObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
            Assert.False(vm.Accept(stale));
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
            Assert.Contains(vm.VisibleJournal, line => line.Code == "OBSERVATION_EXPIRED");
        }
    }

    [Fact]
    public void Wrong_proof_identity_blocks_candidate()
    {
        var (vm, _, authority, id) = Bound();
        using (vm)
        {
            authority.Build = c => FakeAuthority.Proof(c) with { Identity = c.Identity with { ProcessId = 12 } };
            Assert.False(vm.Accept(Candidate(id)));
            Assert.Contains(vm.VisibleJournal, line => line.Code == "PROOF_MISMATCH");
        }
    }

    [Fact]
    public void Expired_or_far_future_proof_is_not_accepted()
    {
        foreach (var time in new[] { DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddDays(2) })
        {
            var (vm, _, authority, id) = Bound();
            using (vm)
            {
                authority.Build = c => FakeAuthority.Proof(c) with { ExpiresAtUtc = time };
                Assert.False(vm.Accept(Candidate(id)));
                Assert.Equal("INCONNU", vm.AckStatus);
            }
        }
    }

    [Fact]
    public void Authority_exception_or_session_loss_is_fail_closed()
    {
        var (vm, _, authority, id) = Bound();
        using (vm)
        {
            authority.Throw = true;
            Assert.False(vm.Accept(Candidate(id)));
            Assert.Contains(vm.VisibleJournal, line => line.Code == "AUTHORITY_UNAVAILABLE");
            authority.Throw = false;
            authority.IsCurrent = false;
            Assert.False(vm.Accept(Candidate(id, 2)));
            Assert.Contains(vm.VisibleJournal, line => line.Code == "OWNERSHIP_LOST");
        }
    }

    [Fact]
    public void Duplicate_sequence_stays_rejected_after_invalidation()
    {
        var (vm, _, authority, id) = Bound();
        using (vm)
        {
            Assert.True(vm.Accept(Candidate(id, 1)));
            authority.IsCurrent = false;
            Assert.False(vm.Accept(Candidate(id, 2)));
            authority.IsCurrent = true;
            Assert.False(vm.Accept(Candidate(id, 1)));
            Assert.Contains(vm.VisibleJournal, line => line.Code == "STALE_OR_INVALID");
        }
    }

    [Fact]
    public void Filtered_journal_is_bounded_and_resettable()
    {
        var (vm, _, _, id) = Bound();
        using (vm)
        {
            for (int i = 1; i <= 260; i++)
                Assert.False(vm.Accept(Candidate(id with { ProcessId = 555 }, i)));
            Assert.Equal(200, vm.VisibleJournal.Count);
            vm.JournalFilterIndex = 1;
            Assert.Empty(vm.VisibleJournal);
            Assert.False(vm.HasVisibleJournal);
            vm.JournalFilterIndex = 2;
            vm.SearchText = "IDENTITY_MISMATCH";
            Assert.Equal(200, vm.VisibleJournal.Count);
            vm.SearchText = "unmatched-entry";
            Assert.Empty(vm.VisibleJournal);
            vm.SearchText = string.Empty;
            Assert.Equal(200, vm.VisibleJournal.Count);
        }
    }

    [Fact]
    public void Unknown_direction_does_not_become_attributed()
    {
        var (vm, _, _, id) = Bound();
        using (vm)
        {
            Assert.False(vm.Accept(Candidate(id, direction: MZP0Direction.Unknown)));
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
        }
    }

    [Fact]
    public void Clock_expiry_reverts_session_and_ack_without_retaking_process_control()
    {
        var (vm, _, authority, id) = Bound();
        using (vm)
        {
            authority.Ack = true;
            Assert.True(vm.Accept(Candidate(id, kind: MZP0SignalKind.ServerResponse,
                direction: MZP0Direction.ServerToClient, correlation: "uid-10")));
            Assert.Equal("ACK CORRÉLÉ", vm.AckStatus);
            vm.RecheckExpiryAt(DateTimeOffset.UtcNow.AddMinutes(2));
            Assert.Equal("INCONNU", vm.AckStatus);
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
            Assert.Contains(vm.VisibleJournal, row => row.Code == "PROOF_EXPIRED");
        }
    }

    [Fact]
    public void Invalid_build_label_is_never_promoted()
    {
        var source = new FakeSource();
        var authority = new FakeAuthority();
        using var vm = new MZP0OperatorViewModel();
        vm.Bind(source, authority, Identity with { ClientBuild = "unknown\naccount" }, "fixture-source");
        Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
        Assert.False(vm.Accept(Candidate(Identity)));
        Assert.Contains(vm.VisibleJournal, line => line.Code == "INVALID_PIN");
    }

    [Fact]
    public void Reset_disconnect_unsubscribes_without_toggling_a_game_process()
    {
        var (vm, source, _, id) = Bound();
        using (vm)
        {
            Assert.True(source.SubscriberCount > 0);
            vm.Unbind();
            Assert.Equal(0, source.SubscriberCount);
            Assert.False(vm.Accept(Candidate(id)));
            Assert.Equal("NON ATTESTÉE", vm.SessionStatus);
        }
    }

    [Fact]
    public void Rebinding_same_session_retains_sequence_high_watermark()
    {
        var (vm, source, authority, id) = Bound();
        using (vm)
        {
            Assert.True(vm.Accept(Candidate(id)));
            vm.Unbind();
            vm.Bind(source, authority, id, "fixture-source");
            Assert.False(vm.Accept(Candidate(id, 1)));
            Assert.True(vm.Accept(Candidate(id, 2)));
        }
    }

    private sealed class FakeSource : IMZP0OperatorObservationSource
    {
        public event EventHandler<MZP0ObservationEventArgs>? Received;
        public event EventHandler? Unavailable;
        public int SubscriberCount => Received?.GetInvocationList().Length ?? 0;
        public void Disconnect() => Unavailable?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeAuthority : IMZP0OperatorObservationAuthority
    {
        public bool IsCurrent { get; set; } = true;
        public bool Throw { get; set; }
        public bool Ack { get; set; }
        public Func<MZP0CandidateObservation, MZP0AuthorityProof>? Build { get; set; }

        public bool IsCurrentOwnedSession(MZP0SessionIdentity _) => IsCurrent;

        public MZP0AuthorityProof? Verify(MZP0CandidateObservation candidate)
        {
            if (Throw) throw new InvalidOperationException("fixture authority unavailable");
            return Build?.Invoke(candidate) ?? Proof(candidate) with { IsCorrelatedServerAck = Ack };
        }

        public static MZP0AuthorityProof Proof(MZP0CandidateObservation candidate)
            => new(candidate.Identity, candidate.SourceId, candidate.ObservationId,
                candidate.Sequence, candidate.ObservedAtUtc, candidate.Direction, candidate.Kind,
                DateTimeOffset.UtcNow.AddSeconds(10), candidate.CorrelationId, false);
    }

    private sealed class SelfAttester : IMZP0OperatorObservationSource, IMZP0OperatorObservationAuthority
    {
        public event EventHandler<MZP0ObservationEventArgs>? Received { add { } remove { } }
        public event EventHandler? Unavailable { add { } remove { } }
        public bool IsCurrentOwnedSession(MZP0SessionIdentity _) => true;
        public MZP0AuthorityProof Verify(MZP0CandidateObservation c) => FakeAuthority.Proof(c);
    }
}
#endif
