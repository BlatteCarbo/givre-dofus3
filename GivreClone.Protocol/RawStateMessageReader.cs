using GivreCore.Protobuf;

namespace GivreClone.Protocol;

public readonly record struct RawStateTurn(int? Round, int? Order);
public readonly record struct RawStateInstance(int BaseId, int Scope);
public sealed record RawSpellCastObservation(long? SourceId, int SpellId, int SpellLevelId);
public sealed record RawTimedDamageObservation(long? ActorId, int? NetworkInstanceId,
    long? SourceId, int? SourceSpellId, int ActionId, int? Value, string? Trigger,
    RawStateTurn? Start, RawStateTurn? End, int? SourceGrade = null);

public sealed record RawTriggeredSubcastObservation(long? ActorId, int? NetworkInstanceId,
    long? SourceId, int? SourceSpellId, int? SubSpellId, int? SubSpellGrade,
    int? TriggerGroupId, string? Trigger, RawStateTurn? Start, RawStateTurn? End, int ActionId = 1160);

// Wire evidence only: neither record establishes eligibility or an active state.
public abstract record RawStateObservation(long? ActorId, int? NetworkInstanceId)
{
    public RawStateInstance? ScopedInstance => NetworkInstanceId is int id
        ? id >= 0 ? new(id, 1) : new(-1, 0)
        : null;
}

public sealed record RawStateAddObservation(long? Actor, int? Instance, int ActionId,
    int? StateId, long? SourceId, RawStateTurn? Start, RawStateTurn? End,
    int? RawGdqa, int? RawGdqc)
    : RawStateObservation(Actor, Instance);

public sealed record RawStateRemoveObservation(long? Actor, int? Instance)
    : RawStateObservation(Actor, Instance);

public sealed record RawStateParameterUpdateObservation(long? Actor, int? Instance, int? Parameter0)
    : RawStateObservation(Actor, Instance);

public enum RawStateRemovalRequestKind { SingleInstance, ActorFilteredBulk }

public sealed record RawStateRemovalRequestObservation(long? Actor, int? Instance,
    RawStateRemovalRequestKind RequestKind) : RawStateObservation(Actor, Instance)
{
    // jrl's absent oneof getter returns zero; retain null in the raw ID above.
    public RawStateInstance EffectiveScopedInstance => ScopedInstance ?? new(0, 1);
}

/// <summary>
/// Isolated juh/juu/jth/jsq31 reader for a caller-supplied 3.7 family schema. Family
/// admission is not origin, catalogue identity, eligibility or lifetime proof.
/// Missing scalars remain null (protobuf effective default is zero); a missing
/// endpoint remains distinct from a present endpoint with omitted coordinates.
/// Removal requests do not prove bulk-filter execution or cleared states.
/// </summary>
public sealed class RawStateMessageReader(string? schemaVersion, ProtocolCatalog catalog)
{
    // SpellCast.spell_level is a catalogue row ID, not a spell grade.
    public RawSpellCastObservation? ReadSpellCast(string rawCode, IReadOnlyList<ProtoField> payload)
    {
        if (rawCode != "jsq" || !MZP0ProtocolAdmission.Supports37Family(schemaVersion)) return null;
        try
        {
            var ability = Message(payload, "GameActionFightEvent", "targeted_ability");
            var spell = ability is null ? null : Message(ability, "TargetedAbility", "spell_cast");
            return spell is not null
                && Int32(spell, "SpellCast", "spell_id") is > 0 and var id
                && Int32(spell, "SpellCast", "spell_level") is > 0 and var level
                ? new(Scalar(payload, "GameActionFightEvent", "source_id"), id, level) : null;
        }
        catch (FormatException) { return null; }
    }

    public RawTriggeredSubcastObservation? ReadTriggeredSubcast(string rawCode, IReadOnlyList<ProtoField> payload)
    {
        if (rawCode != "juh" || !MZP0ProtocolAdmission.Supports37Family(schemaVersion)) return null;
        try
        {
            var wrapper = Message(payload, "juh", "fxet");
            if (wrapper is null || Int32(wrapper, "lng", "gdzm") is not (1160 or 792)) return null;
            var effect = Message(wrapper, "lng", "gdzl");
            if (effect is null) return null;
            var trigger = Field(effect, "llg", "gdqf");
            if (trigger is not null && (trigger.Wire != WireType.Len || trigger.Bytes is null)) return null;
            return new(Scalar(effect, "llg", "gdpz"), Int32(effect, "llg", "gdpw"),
                Scalar(wrapper, "lng", "gdzn"), Int32(effect, "llg", "gdqh"),
                Int32(effect, "llg", "gdqb"), Int32(effect, "llg", "gdqe"),
                Int32(effect, "llg", "gdpx"), trigger?.Bytes is { } bytes
                    ? new System.Text.UTF8Encoding(false, true).GetString(bytes) : null,
                Turn(effect, "gdpt"), Turn(effect, "gdpv"), Int32(wrapper, "lng", "gdzm")!.Value);
        }
        catch (FormatException) { return null; }
        catch (System.Text.DecoderFallbackException) { return null; }
    }

    public RawTimedDamageObservation? ReadTimedDamage(string rawCode, IReadOnlyList<ProtoField> payload)
    {
        if (rawCode != "juh" || !MZP0ProtocolAdmission.Supports37Family(schemaVersion)) return null;
        try
        {
            var wrapper = Message(payload, "juh", "fxet");
            if (wrapper is null || Int32(wrapper, "lng", "gdzm") is not (99 or 96 or 2998)) return null;
            var effect = Message(wrapper, "lng", "gdzl");
            if (effect is null) return null;
            var trigger = Field(effect, "llg", "gdqf");
            if (trigger is not { Wire: WireType.Len, Bytes: { } bytes }) return null;
            return new(Scalar(effect, "llg", "gdpz"), Int32(effect, "llg", "gdpw"),
                Scalar(wrapper, "lng", "gdzn"), Int32(effect, "llg", "gdqh"), Int32(wrapper, "lng", "gdzm")!.Value,
                Int32(effect, "llg", "gdps"), new System.Text.UTF8Encoding(false, true).GetString(bytes),
                Turn(effect, "gdpt"), Turn(effect, "gdpv"), Int32(effect, "llg", "gdqg"));
        }
        catch (FormatException) { return null; }
        catch (System.Text.DecoderFallbackException) { return null; }
    }

    // Official eai.vbp -> eai.vbt -> ht constructor uses llg field16
    // as the SpellData lookup key, independently of the effect action.
    public int? ReadSourceSpellId(string rawCode, IReadOnlyList<ProtoField> payload)
    {
        if (rawCode != "juh" || !MZP0ProtocolAdmission.Supports37Family(schemaVersion)) return null;
        try
        {
            var wrapper = Message(payload, "juh", "fxet");
            var effect = wrapper is null ? null : Message(wrapper, "lng", "gdzl");
            return effect is null ? null : Int32(effect, "llg", "gdqh");
        }
        catch (FormatException) { return null; }
    }

    public RawStateObservation? Read(string rawCode, IReadOnlyList<ProtoField> payload)
    {
        if (!MZP0ProtocolAdmission.Supports37Family(schemaVersion)) return null;
        try
        {
            return rawCode switch
            {
                "juh" => ReadAdd(payload),
                "juu" => new RawStateRemoveObservation(
                    Scalar(payload, "juu", "fxhj"), Int32(payload, "juu", "fxhi")),
                "jth" => new RawStateParameterUpdateObservation(
                    Scalar(payload, "jth", "fwyu"), Int32(payload, "jth", "fwyt"),
                    Int32(payload, "jth", "fwys")),
                "jsq" => ReadRemovalRequest(payload),
                _ => null
            };
        }
        catch (FormatException) { return null; }
    }

    private RawStateRemovalRequestObservation? ReadRemovalRequest(IReadOnlyList<ProtoField> payload)
    {
        int tag = catalog.FieldNumber("jsq", "fwvz") ?? throw new FormatException("Raw field mapping missing.");
        // Other envelope variants/metadata are not established for this reader.
        // Reject mixed envelopes instead of guessing a last-oneof-wins order.
        if (payload.Any(f => f.Field != tag)) return null;
        var request = Message(payload, "jsq", "fwvz");
        if (request is null) return null;
        int idTag = catalog.FieldNumber("jrl", "fwov") ?? throw new FormatException("Raw field mapping missing.");
        int actorTag = catalog.FieldNumber("jrl", "fwot") ?? throw new FormatException("Raw field mapping missing.");
        int flagTag = catalog.FieldNumber("jrl", "fwou") ?? throw new FormatException("Raw field mapping missing.");
        // Case3 (fwox), competing oneof fields and unknown variants are unsupported.
        if (request.Any(f => f.Field != idTag && f.Field != actorTag && f.Field != flagTag)) return null;
        var id = Int32(request, "jrl", "fwov");
        var actor = Scalar(request, "jrl", "fwot");
        var flag = Scalar(request, "jrl", "fwou");
        if (flag is not (null or 0 or 1)) return null;
        // #4 is unused by the grounded branch, not a filter selector.
        return new(actor, id, id is < 0
            ? RawStateRemovalRequestKind.ActorFilteredBulk : RawStateRemovalRequestKind.SingleInstance);
    }

    private RawStateAddObservation? ReadAdd(IReadOnlyList<ProtoField> payload)
    {
        var wrapper = Message(payload, "juh", "fxet");
        if (wrapper is null) return null;
        var effect = Message(wrapper, "lng", "gdzl");
        if (effect is null) return null;
        var action = Int32(wrapper, "lng", "gdzm");
        if (action is not (950 or 951 or 952)) return null;
        return new(Scalar(effect, "llg", "gdpz"), Int32(effect, "llg", "gdpw"),
            action.Value, Int32(effect, "llg", "gdps"), Scalar(wrapper, "lng", "gdzn"),
            Turn(effect, "gdpt"), Turn(effect, "gdpv"),
            Int32(effect, "llg", "gdqa"), Int32(effect, "llg", "gdqc"));
    }

    private RawStateTurn? Turn(IReadOnlyList<ProtoField> effect, string field)
    {
        var turn = Message(effect, "llg", field);
        return turn is null ? null : new(Int32(turn, "lfz", "gcra"), Int32(turn, "lfz", "gcqz"));
    }

    private ProtoField? Field(IReadOnlyList<ProtoField> tree, string message, string field)
    {
        // No semantic aliases, guessed tags, or process-wide mutable catalog.
        int tag = catalog.FieldNumber(message, field) ?? throw new FormatException("Raw field mapping missing.");
        ProtoField? found = null;
        foreach (var candidate in tree)
        {
            if (candidate.Field != tag) continue;
            if (found is not null) throw new FormatException("Ambiguous duplicate field.");
            found = candidate;
        }
        return found;
    }

    private long? Scalar(IReadOnlyList<ProtoField> tree, string message, string field)
    {
        var value = Field(tree, message, field);
        if (value is null) return null;
        if (value.Wire != WireType.Varint) throw new FormatException("Expected signed varint.");
        return value.AsSignedVarint;
    }

    private int? Int32(IReadOnlyList<ProtoField> tree, string message, string field)
    {
        var value = Scalar(tree, message, field);
        if (value is null) return null;
        if (value < int.MinValue || value > int.MaxValue) throw new FormatException("Int32 out of range.");
        return (int)value;
    }

    private IReadOnlyList<ProtoField>? Message(IReadOnlyList<ProtoField> tree, string message, string field)
    {
        var value = Field(tree, message, field);
        if (value is null) return null;
        if (value.Wire != WireType.Len) throw new FormatException("Expected nested message.");
        // RawProtobuf.Message is only a heuristic view, including null for empty
        // messages. Reparse the schema-proven bytes so malformed nesting fails.
        if (value.Bytes is not null) return RawProtobuf.Parse(value.Bytes);
        return value.Message ?? throw new FormatException("Nested bytes missing.");
    }
}
