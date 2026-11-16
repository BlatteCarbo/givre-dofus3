using System;

namespace GivreClone.App.Services;

// Serialized by NativeTurboRuntime.TacticalMapGate. This record admits capture
// only; it is never a substitute for the ready-map lease or managed scene access.
internal sealed class TacticalConstructionAdmission
{
    internal const long HeartbeatTtlMs = 2250;
    internal const long MaximumConstructionMs = 15000;
    internal long TargetStart { get; }
    internal long Epoch { get; }
    internal long MapId { get; }
    private readonly long _generation;
    private readonly long _deadline;
    private bool _pending;

    internal TacticalConstructionAdmission(long targetStart, long epoch, long mapId,
        long generation, long now)
    {
        TargetStart = targetStart;
        Epoch = epoch;
        MapId = mapId;
        _generation = generation;
        _deadline = checked(now + MaximumConstructionMs);
        _pending = generation > 0;
    }

    internal void Revoke() => _pending = false;

    internal bool Complete(long targetStart, long mapId)
    {
        // Readiness and capture are different authorities. A profile change or
        // capture timeout ends capture, not the observed map transition. Full
        // revocation removes this object from the runtime's transition table.
        if (TargetStart != targetStart || MapId != mapId) return false;
        _pending = false;
        return true;
    }

    internal string? Record(long generation, int ownerPid, long ownerStart, long now)
    {
        if (!_pending) return null;
        if (generation != _generation || now >= _deadline)
        {
            _pending = false;
            return null;
        }
        long expires = Math.Min(checked(now + HeartbeatTtlMs), _deadline);
        return FormattableString.Invariant($"1 {TargetStart} {ownerPid} {ownerStart} {Epoch} {MapId} {_generation} {expires}");
    }
}
