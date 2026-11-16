using System;
namespace GivreClone.App.Services;
/// <summary>Action IDs are reused. An ACK from an earlier request or outer sequence cannot acknowledge the current one.</summary>
public static class TurboSequenceAckPolicy
{
    public static bool ShouldInject(long actionId, long sequenceStartedAt, long requestAt, long? acknowledgedAt) =>
        actionId != 0 && (!acknowledgedAt.HasValue || acknowledgedAt.Value < Math.Max(sequenceStartedAt, requestAt));
}
