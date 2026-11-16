namespace GivreClone.Engine.Recovery;

/// <summary>Only identifiers and categorical metadata are journaled. Never supply payloads, accounts or secrets.</summary>
public enum MZP0OperationCategory { Movement, Combat, Inventory, Economy, Interaction, Other }

public enum MZP0DeliverySemantics { Idempotent, NonIdempotent }

public enum MZP0DeliveryState
{
    Prepared,
    DispatchWindowOpened,
    Acknowledged,
    Uncertain,
    CancelledBeforeDispatch,
    ReconciledApplied,
    ReconciledNotApplied
}

public enum MZP0RecoveryDisposition
{
    Unknown,
    AwaitingFirstDispatch,
    AwaitingVerifiedAcknowledgment,
    RequiresExplicitReconciliation,
    Acknowledged,
    CancelledBeforeDispatch,
    ReconciledApplied,
    ReconciledNotApplied
}

public enum MZP0ReconciliationConclusion { Applied, DefinitelyNotApplied }

/// <summary>
/// A process birth timestamp and connection identity protect against PID/connection reuse.
/// No account or character identifiers are accepted.
/// </summary>
public readonly record struct MZP0SessionIdentity(
    int ProcessId, long ProcessStartUtcTicks, int ConnectionId, string ProtocolVersion);

/// <summary>A new generation is minted for every activation, including after a process restart.</summary>
public readonly record struct MZP0SessionGeneration(
    long Number, Guid Nonce, MZP0SessionIdentity Identity);

/// <summary>
/// Obtained only AFTER the dispatch window was persisted. A caller must still verify this
/// authorization belongs to its current transport before sending once.
/// </summary>
public readonly record struct MZP0DispatchAuthorization(
    MZP0SessionGeneration Generation, Guid OperationId, int CorrelationUid);

/// <summary>
/// Adapter-supplied evidence for a correlated inbound response. The adapter is responsible
/// for verifying the inbound source and explicit positive acknowledgment; timeout, Send()
/// success or a matching UID alone do not constitute acknowledgment.
/// </summary>
public readonly record struct MZP0AcknowledgmentEvidence(
    MZP0SessionGeneration InboundGeneration, int CorrelationUid, bool ExplicitPositiveResponse);

/// <summary>
/// External authoritative reconciliation only. A timeout, disconnected socket or lack
/// of a response cannot prove either conclusion. This type is an adapter assertion,
/// not a built-in verification mechanism.
/// </summary>
public readonly record struct MZP0ReconciliationEvidence(
    long OriginalGeneration, Guid OriginalNonce, int CorrelationUid,
    MZP0ReconciliationConclusion Conclusion, bool AuthorityVerified);

/// <summary>Immutable metadata; no outbound frames, input parameters or private fields.</summary>
public sealed record MZP0JournalRecord(
    Guid OperationId,
    long Generation,
    Guid GenerationNonce,
    int CorrelationUid,
    MZP0OperationCategory Category,
    MZP0DeliverySemantics Semantics,
    MZP0DeliveryState State,
    long CreatedRevision,
    long UpdatedRevision);
