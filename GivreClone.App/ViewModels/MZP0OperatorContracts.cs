using System;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Diagnostic read-only. No network, account, game, injection or command operations are exposed.
/// A host supplies the live source AND a separate trusted observation authority.
/// The DTOs are metadata only: never carry raw frames, payloads, names, tokens or credentials.
/// </summary>
public enum MZP0SignalKind
{
    Unclassified,
    Session,
    Mapping,
    Decoder,
    Map,
    Combat,
    ServerResponse,
}

public enum MZP0Direction
{
    Unknown,
    ClientToServer,
    ServerToClient,
    Local,
}

/// <summary>Identity must be independently pinned by the host to the owned client process.</summary>
public sealed record MZP0SessionIdentity(
    string SessionId,
    int ProcessId,
    DateTimeOffset ProcessBirthUtc,
    long Generation,
    string ClientBuild,
    string ClientSha256);

/// <summary>A source's claim alone is never proof of process ownership or server acknowledgement.</summary>
public sealed record MZP0CandidateObservation(
    MZP0SessionIdentity Identity,
    string SourceId,
    string ObservationId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    MZP0Direction Direction,
    MZP0SignalKind Kind,
    string? CorrelationId = null);

/// <summary>
/// Produced by an external host-controlled verifier, never by the observation producer itself.
/// The ViewModel additionally checks every field and expiration against the pinned session.
/// </summary>
public sealed record MZP0AuthorityProof(
    MZP0SessionIdentity Identity,
    string SourceId,
    string ObservationId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    MZP0Direction Direction,
    MZP0SignalKind Kind,
    DateTimeOffset ExpiresAtUtc,
    string? CorrelationId,
    bool IsCorrelatedServerAck);

public sealed class MZP0ObservationEventArgs(MZP0CandidateObservation candidate) : EventArgs
{
    public MZP0CandidateObservation Candidate { get; } = candidate;
}

/// <summary>Explicit externally owned, event-driven ingress. No polling or built-in sources.</summary>
public interface IMZP0OperatorObservationSource
{
    event EventHandler<MZP0ObservationEventArgs>? Received;
    event EventHandler? Unavailable;
}

/// <summary>
/// Independently validates origin, direction, identity, frame/source provenance and correlation.
/// Implement in the trusted host; the UI has no mechanism to grant its own authority.
/// </summary>
public interface IMZP0OperatorObservationAuthority
{
    bool IsCurrentOwnedSession(MZP0SessionIdentity pinnedIdentity);
    MZP0AuthorityProof? Verify(MZP0CandidateObservation candidate);
}
