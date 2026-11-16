// Offline-only comparison of an advised combat action with a confirmed observation.
// Adapted from user-owned GivreActive/tools/JondoCompat/Combat/ShadowDecisionComparer.cs.
// No network, game client, class rules, version-specific mapping or native dependency.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GivreClone.Tools.CombatShadowComparison;

public enum ShadowComparisonKind
{
    InsufficientEvidence,
    VersionMismatch,
    StaleTurn,
    NoAdvice,
    Agree,
    DifferentSpell,
    DifferentTarget,
    DifferentOrigin,
    DifferentAim
}

/// <summary>Every differing field, even when several fields disagree at once.</summary>
public enum ShadowDifference { Spell, Target, Origin, Aim }

/// <summary>
/// Explicit targeting distinction: placing a portal, bomb or glyph on a cell
/// does not require a target actor. Unknown is never treated as NoActor.
/// </summary>
public enum ShadowTargetKind { Unknown, Actor, GroundCell, NoActor }

public readonly record struct ShadowTarget(ShadowTargetKind Kind, long ActorId = 0)
{
    public static ShadowTarget Actor(long actorId) => new(ShadowTargetKind.Actor, actorId);
    public static ShadowTarget GroundCell => new(ShadowTargetKind.GroundCell);
    public static ShadowTarget NoActor => new(ShadowTargetKind.NoActor);

    internal bool IsValid => Kind switch
    {
        ShadowTargetKind.Actor => ActorId > 0,
        ShadowTargetKind.GroundCell or ShadowTargetKind.NoActor => ActorId == 0,
        _ => false
    };
}

public enum ShadowAdviceStatus
{
    Unknown,
    Planned,
    NoLegalAction,
    IncompleteSnapshot,
    InvalidSnapshot,
    SearchLimit,
    TimeLimit
}

public readonly record struct ShadowTurnIdentity(
    long FightGeneration, long TurnGeneration, int Round, long SnapshotRevision)
{
    public bool IsValid => FightGeneration > 0 && TurnGeneration > 0
                           && Round > 0 && SnapshotRevision > 0;
}

/// <summary>
/// CellCount comes from the caller's offline board evidence. It is not an
/// implicit assertion about any particular game/client geometry.
/// </summary>
public sealed record ShadowBoard(
    string ClientVersion, ShadowTurnIdentity Identity, int CellCount)
{
    internal bool ValidCell(int cell) => cell >= 0 && cell < CellCount;
}

/// <summary>
/// Path describes the advised move, including its final casting origin.
/// For casting in place, provide a one-cell path containing FromCell.
/// </summary>
public sealed record ShadowAdvice(
    string ClientVersion, ShadowTurnIdentity Identity, ShadowAdviceStatus Status,
    int SpellId, ShadowTarget Target, int FromCell, int AimCell,
    IReadOnlyList<int>? Path);

/// <summary>
/// Caller-supplied observation; CastConfirmed must mean observed success,
/// not merely a request, animation or local intent.
/// </summary>
public sealed record ObservedCombatCast(
    string ClientVersion, ShadowTurnIdentity Identity,
    int SpellId, ShadowTarget Target, int FromCell, int AimCell,
    bool CastConfirmed, bool CompleteObservation);

public sealed class ShadowComparison
{
    private readonly ReadOnlyCollection<ShadowDifference> _differences;

    internal ShadowComparison(
        ShadowComparisonKind kind, ShadowTurnIdentity identity,
        ShadowDifference[] differences)
    {
        Kind = kind;
        Identity = identity;
        _differences = Array.AsReadOnly(differences);
    }

    public ShadowComparisonKind Kind { get; }
    public ShadowTurnIdentity Identity { get; }
    public IReadOnlyList<ShadowDifference> Differences => _differences;

    public bool CanCompare => Kind is ShadowComparisonKind.Agree
        or ShadowComparisonKind.DifferentSpell
        or ShadowComparisonKind.DifferentTarget
        or ShadowComparisonKind.DifferentOrigin
        or ShadowComparisonKind.DifferentAim;

    // Null means the inputs were not comparable; false means a measured mismatch.
    public bool? SpellAgrees => Agreement(ShadowDifference.Spell);
    public bool? TargetAgrees => Agreement(ShadowDifference.Target);
    public bool? OriginAgrees => Agreement(ShadowDifference.Origin);
    public bool? AimAgrees => Agreement(ShadowDifference.Aim);

    private bool? Agreement(ShadowDifference dimension)
        => CanCompare ? !_differences.Contains(dimension) : null;
}

/// <summary>
/// Compares existing offline facts, never plans or permits a cast. Agreement
/// is not a performance or legality score.
/// </summary>
public static class ShadowDecisionComparer
{
    private static ShadowComparison Reject(ShadowComparisonKind kind, ShadowBoard? board)
        => new(kind, board?.Identity ?? default, Array.Empty<ShadowDifference>());

    public static ShadowComparison Compare(
        ShadowBoard? board,
        ShadowAdvice? advice,
        ObservedCombatCast? observed,
        string? expectedClientVersion)
    {
        if (board is null || advice is null || observed is null
            || string.IsNullOrWhiteSpace(expectedClientVersion)
            || string.IsNullOrWhiteSpace(board.ClientVersion)
            || string.IsNullOrWhiteSpace(advice.ClientVersion)
            || string.IsNullOrWhiteSpace(observed.ClientVersion)
            || !board.Identity.IsValid || board.CellCount <= 0
            || !observed.CastConfirmed || !observed.CompleteObservation
            || observed.SpellId <= 0 || !observed.Target.IsValid
            || !board.ValidCell(observed.FromCell) || !board.ValidCell(observed.AimCell))
            return Reject(ShadowComparisonKind.InsufficientEvidence, board);

        if (!string.Equals(board.ClientVersion, expectedClientVersion, StringComparison.Ordinal)
            || !string.Equals(advice.ClientVersion, expectedClientVersion, StringComparison.Ordinal)
            || !string.Equals(observed.ClientVersion, expectedClientVersion, StringComparison.Ordinal))
            return Reject(ShadowComparisonKind.VersionMismatch, board);

        if (!board.Identity.Equals(observed.Identity) || !board.Identity.Equals(advice.Identity))
            return Reject(ShadowComparisonKind.StaleTurn, board);

        if (advice.Status != ShadowAdviceStatus.Planned)
            return Reject(ShadowComparisonKind.NoAdvice, board);

        if (advice.SpellId <= 0 || !advice.Target.IsValid
            || !board.ValidCell(advice.FromCell) || !board.ValidCell(advice.AimCell)
            || advice.Path is not { Count: > 0 } path
            || path.Count > board.CellCount || path[path.Count - 1] != advice.FromCell)
            return Reject(ShadowComparisonKind.InsufficientEvidence, board);

        foreach (int cell in path)
            if (!board.ValidCell(cell))
                return Reject(ShadowComparisonKind.InsufficientEvidence, board);

        var differences = new List<ShadowDifference>(4);
        if (advice.SpellId != observed.SpellId) differences.Add(ShadowDifference.Spell);
        if (advice.Target != observed.Target) differences.Add(ShadowDifference.Target);
        if (advice.FromCell != observed.FromCell) differences.Add(ShadowDifference.Origin);
        if (advice.AimCell != observed.AimCell) differences.Add(ShadowDifference.Aim);

        ShadowComparisonKind kind = differences.Count == 0 ? ShadowComparisonKind.Agree
            : differences[0] switch
            {
                ShadowDifference.Spell => ShadowComparisonKind.DifferentSpell,
                ShadowDifference.Target => ShadowComparisonKind.DifferentTarget,
                ShadowDifference.Origin => ShadowComparisonKind.DifferentOrigin,
                _ => ShadowComparisonKind.DifferentAim
            };
        return new ShadowComparison(kind, board.Identity, differences.ToArray());
    }
}
