namespace GivreClone.Protocol;

/// <summary>
/// Alloue les uid de Request (C-&gt;S) par session et permet, plus tard, de matcher la Response par uid.
/// GivreCore rejoue l'uid capture tel quel et n'offre aucune correlation : GivreClone gere donc ses propres uid.
/// En Phase 0 l'attente de confirmation se fait surtout par Event confirmant (cf. PacketBus) ; la correlation
/// stricte par uid (via EnvelopeBuilder.ReadEnvelopeUid sur la RawFrame de la Response) arrive en Phase 1.
/// </summary>
public sealed class RequestCorrelator
{
    private int _uid;

    /// <summary>Prochain uid (monotone, demarre a 1).</summary>
    public int NextUid() => Interlocked.Increment(ref _uid);
}
