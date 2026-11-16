using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Décode les événements de GROUPE Dofus (PartyJoinEvent / NewMember / MemberUpdate / MemberRemove / Leave / Deleted /
/// KickedBy / LeaderUpdate) pour tenir l'état « dans quel groupe suis-je, avec qui ». Numéros de champ PAR NOM via le
/// mapping (fields.json), littéral courant en repli. Membre = message <c>Character { character_basic_information #2 {
/// name #5 }, id #3 }</c> (disposition confirmée par capture sur CharacterListEvent ; NON capturée sur les events de groupe
/// -> l'appelant garde un repli par identifiant de personnage).
/// </summary>
public static class PartyReader
{
    public readonly record struct PartyMember(long Id, string Name);

    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string m, string f) => _fieldNumber?.Invoke(m, f) ?? FieldMiss.Of(m, f);   // mapping streamé seul

    /// <summary>Un <c>Character</c> (sous-message) -> (id, nom). null si id absent/0.</summary>
    public static PartyMember? ReadCharacter(IReadOnlyList<ProtoField> character)
    {
        long id = PayloadReader.Int(character, F("Character", "id"), 0);
        if (id == 0) return null;
        string name = "";
        var basic = PayloadReader.Field(character, F("Character", "character_basic_information"));
        if (basic is not null && Sub(basic) is { } b)
            name = PayloadReader.Str(b, F("CharacterBasicInformation", "name")) ?? "";
        return new PartyMember(id, name);
    }

    public static (long PartyId, long LeaderId, IReadOnlyList<PartyMember> Members) ReadJoin(IReadOnlyList<ProtoField> p)
    {
        long partyId = PayloadReader.Int(p, F("PartyJoinEvent", "party_id"), 0);
        long leaderId = PayloadReader.Int(p, F("PartyJoinEvent", "leader_id"), 0);
        int fMembers = F("PartyJoinEvent", "members");
        var list = new List<PartyMember>();
        foreach (var f in p)
        {
            if (f.Field != fMembers || f.Wire != WireType.Len || Sub(f) is not { } sub) continue;
            if (ReadCharacter(sub) is { } m) list.Add(m);
        }
        return (partyId, leaderId, list);
    }

    public static (long PartyId, PartyMember? Member) ReadNewMember(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("PartyNewMemberEvent", "party_id"), 0), MemberAt(p, F("PartyNewMemberEvent", "member")));

    public static (long PartyId, PartyMember? Member) ReadMemberUpdate(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("PartyMemberUpdateEvent", "party_id"), 0), MemberAt(p, F("PartyMemberUpdateEvent", "member")));

    public static (long PartyId, long LeavingId) ReadMemberRemove(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("PartyMemberRemoveEvent", "party_id"), 0),
            PayloadReader.Int(p, F("PartyMemberRemoveEvent", "leaving_player_id"), 0));

    public static (long PartyId, long LeaderId) ReadLeaderUpdate(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("PartyLeaderUpdateEvent", "party_id"), 0),
            PayloadReader.Int(p, F("PartyLeaderUpdateEvent", "party_leader_id"), 0));

    /// <summary>party_id d'un event simple (PartyLeaveEvent #2, PartyDeletedEvent #1, PartyKickedByEvent #1).</summary>
    public static long ReadPartyId(string message, IReadOnlyList<ProtoField> p)
        => PayloadReader.Int(p, F(message, "party_id"), 0);

    private static PartyMember? MemberAt(IReadOnlyList<ProtoField> p, int field)
    {
        var f = PayloadReader.Field(p, field);
        return f is not null && f.Wire == WireType.Len && Sub(f) is { } sub ? ReadCharacter(sub) : null;
    }

    private static IReadOnlyList<ProtoField>? Sub(ProtoField f)
    {
        if (f.Message is not null) return f.Message;
        if (f.Bytes is { Length: > 0 } b) { try { return RawProtobuf.Parse(b); } catch { return null; } }
        return null;
    }
}
