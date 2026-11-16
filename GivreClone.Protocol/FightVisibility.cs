using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

public enum FighterVisibility { Unknown, Visible, Invisible, Detected }
public enum FighterPositionCertainty { Unknown, LastKnown, Confirmed }

public readonly record struct FighterVisibilityObservation(long ActorId, int RawState);

public readonly record struct FighterVisibilityState(int? RawState, FighterVisibility Visibility,
    FighterPositionCertainty PositionCertainty)
{
    public bool CanTarget => Visibility == FighterVisibility.Visible
        && PositionCertainty == FighterPositionCertainty.Confirmed;

    // common.proto FightInvisibilityState: INVISIBLE=0, DETECTED=1, VISIBLE=2.
    // Field numbers still come from the installed protocol catalog.
    public static FighterVisibilityState Observe(FighterVisibilityObservation observation) =>
        new(observation.RawState, observation.RawState switch
        {
            0 => FighterVisibility.Invisible,
            1 => FighterVisibility.Detected,
            2 => FighterVisibility.Visible,
            _ => FighterVisibility.Unknown
        }, FighterPositionCertainty.LastKnown);

    public FighterVisibilityState ConfirmPosition() => Visibility == FighterVisibility.Visible
        ? this with { PositionCertainty = FighterPositionCertainty.Confirmed } : this;

    // A detection locates an actor; it is not a transition to visible or a confirmed attack target.
    public FighterVisibilityState Detect() => this with { PositionCertainty = FighterPositionCertainty.LastKnown };
}

public sealed class FightVisibilityReducer
{
    private ImmutableDictionary<long, FighterVisibilityState> _actors = ImmutableDictionary<long, FighterVisibilityState>.Empty;
    public FighterVisibilityState Get(long actor) => _actors.GetValueOrDefault(actor);
    public void Observe(FighterVisibilityObservation observation, bool positionConfirmed = false) =>
        ImmutableInterlocked.AddOrUpdate(ref _actors, observation.ActorId,
            State(observation, positionConfirmed), (_, _) => State(observation, positionConfirmed));
    // A visibility change alone does not refresh a previously hidden position.
    private static FighterVisibilityState State(FighterVisibilityObservation observation, bool positionConfirmed)
    {
        var state = FighterVisibilityState.Observe(observation);
        return positionConfirmed ? state.ConfirmPosition() : state;
    }
    public void ConfirmPosition(long actor) => ImmutableInterlocked.AddOrUpdate(ref _actors, actor,
        default(FighterVisibilityState), (_, state) => state.ConfirmPosition());
    public void Detect(long actor) => ImmutableInterlocked.AddOrUpdate(ref _actors, actor,
        default(FighterVisibilityState).Detect(), (_, state) => state.Detect());
    // Server relocation (InvisibleDetected) carries an exact cell: it confirms a Visible fighter, but never makes a hidden one targetable.
    public void Locate(long actor) => ImmutableInterlocked.AddOrUpdate(ref _actors, actor,
        default(FighterVisibilityState).Detect(),
        (_, state) => state.Visibility == FighterVisibility.Visible ? state.ConfirmPosition() : state.Detect());
    public void Remove(long actor) => ImmutableInterlocked.TryRemove(ref _actors, actor, out _);
    public void Reset() => System.Threading.Interlocked.Exchange(ref _actors, ImmutableDictionary<long, FighterVisibilityState>.Empty);
}

public static class FightVisibilityReader
{
    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);
    private static ProtoField? Field(IReadOnlyList<ProtoField>? tree, string message, string field) =>
        tree?.FirstOrDefault(f => f.Field == F(message, field));

    public static FighterVisibilityObservation? ReadInitial(IReadOnlyList<ProtoField> tree)
    {
        var body = Field(tree, "FightFighterShowEvent", "information")?.Message;
        var actor = Field(body, "ActorPositionInformation", "actor_id");
        var information = Field(body, "ActorPositionInformation", "actor_information")?.Message;
        var fighter = Field(information, "ActorInformation", "fighter")?.Message;
        var stats = Field(fighter, "FightFighterInformation", "stats");
        if (actor?.Wire != WireType.Varint || stats?.Wire != WireType.Len) return null;
        return ReadState(actor.AsSignedVarint, stats.Message, "FightCharacteristics");
    }

    public static FighterVisibilityObservation? ReadChange(IReadOnlyList<ProtoField> tree)
    {
        var body = Field(tree, "GameActionFightEvent", "invisibility");
        if (body?.Wire != WireType.Len) return null;
        var actor = Field(body.Message, "Invisibility", "target_id");
        if (actor?.Wire != WireType.Varint) return null;
        return ReadState(actor.AsSignedVarint, body.Message, "Invisibility");
    }

    private static FighterVisibilityObservation? ReadState(long actor, IReadOnlyList<ProtoField>? tree, string message)
    {
        var state = Field(tree, message, "invisibility_state");
        if (state is not null && state.Wire != WireType.Varint) return null;
        // Present message plus omitted scalar means protobuf default zero, not missing observation.
        return new(actor, unchecked((int)(state?.Varint ?? 0)));
    }
}
