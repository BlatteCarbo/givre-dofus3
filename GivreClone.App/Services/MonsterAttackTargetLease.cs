using System;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Identity lease for a user-selected monster group. An actor ID alone is not
/// evidence of a surviving group: server map snapshots can replace or remove it
/// before the movement ACK and the eventual AttackMonsterRequest.
/// </summary>
public sealed class MonsterAttackTargetLease
{
    public const long MaxPendingAttackAgeMs = 45_000;

    /// <summary>A late/unknown movement completion cannot resurrect a prior attack.</summary>
    public static bool IsPendingFresh(long requestedAt, long now)
        => requestedAt >= 0 && now >= requestedAt
            && now - requestedAt <= MaxPendingAttackAgeMs;

    /// <summary>ACK alone is insufficient: require the same tracked movement
    /// to have received its own correlated server movement echo.</summary>
    public static bool MatchesConfirmedMovement(long requestedSequence, long echoedSequence,
        long trackedSequence, long requestedMap, long observedMap, long requestedAt, long now)
        => requestedSequence > 0 && requestedSequence == echoedSequence
            && requestedSequence == trackedSequence && requestedMap > 0
            && requestedMap == observedMap && IsPendingFresh(requestedAt, now);

    private readonly long _mapId;
    private readonly long _mapGeneration;
    private readonly MapActor _actor;

    private MonsterAttackTargetLease(long mapId, long mapGeneration, MapActor actor)
    {
        _mapId = mapId;
        _mapGeneration = mapGeneration;
        _actor = actor;
    }

    public static MonsterAttackTargetLease? Capture(BotWorldState world, long mapId, MapActor selected)
    {
        if (world is null || selected is null || mapId <= 0
            || selected.Kind != MapActorKind.Monster || selected.ActorId >= 0
            || (uint)selected.CellId >= MapPathfinder.CellCount) return null;
        long generation = Interlocked.Read(ref world.MapGeneration);
        if (generation <= 0 || !world.SelfReadyFor(mapId)) return null;
        var lease = new MonsterAttackTargetLease(mapId, generation, selected);
        return lease.IsCurrent(world) ? lease : null;
    }

    public bool IsCurrent(BotWorldState world)
    {
        if (world is null) return false;
        long before = Interlocked.Read(ref world.ActorPublicationSequence);
        if ((before & 1) != 0 || Interlocked.Read(ref world.MapGeneration) != _mapGeneration
            || world.MapId != _mapId) return false;

        // An injected move deliberately marks the self-cell as predicted until
        // the server echo arrives. That must not invalidate the *group* lease
        // before the pending attack can be queued. Self readiness is checked
        // once at capture; arrival synchronization is enforced by the caller.

        // Reference identity is intentional: if the same contextual ID is
        // recycled, or a group moves/is replaced with identical display data,
        // a new server actor record must not reuse an older click's permission.
        var actors = world.Actors;
        bool matches = actors is not null && actors.Count(a => a.ActorId == _actor.ActorId) == 1
            && actors.Any(a => ReferenceEquals(a, _actor)
                && a.Kind == MapActorKind.Monster && a.CellId == _actor.CellId);
        return matches && Interlocked.Read(ref world.ActorPublicationSequence) == before;
    }

    /// <summary>The queued attack can fire only from the exact destination
    /// confirmed by the server, still adjacent to the same observed group.</summary>
    public bool IsAtConfirmedArrival(BotWorldState world, int expectedCell)
    {
        if (world is null) return false;
        long before = Interlocked.Read(ref world.ActorPublicationSequence);
        return (before & 1) == 0 && IsCurrent(world)
            && (uint)expectedCell < MapPathfinder.CellCount
            && world.SelfReadyFor(_mapId) && world.SelfCell == expectedCell
            && MapPathfinder.CellDistance(expectedCell, _actor.CellId) <= 1
            && Interlocked.Read(ref world.ActorPublicationSequence) == before;
    }

    /// <summary>
    /// Keep the final server-actor check and the submission of an attack in the
    /// same publication gate. An actor removal or map transition published on
    /// another thread cannot slip between checking the target and submitting
    /// the attack. The dispatch is still NOT a server ACK of combat startup.
    /// </summary>
    public bool TrySubmitFromConfirmedCell(BotWorldState world, int expectedCell, Func<bool> submit)
    {
        if (world is null || submit is null) return false;
        lock (world.ActorPublicationGate)
        {
            if (!IsAtConfirmedArrival(world, expectedCell)) return false;
            return submit();
        }
    }
}
