using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Lecteurs des événements du Lot E (petites forges) : demande d'échange / de défi reçue, options de combat, variante de
/// sort, point de respawn, popup de modération. Numéros de champ résolus PAR NOM via <see cref="UseCatalog"/> (fields.json
/// v4) ; le repli est le numéro de fields.json au moment de l'écriture — ⚠ plusieurs messages sont numérotés dans un AUTRE
/// ordre que clear_game_2025.proto (antérieur au patch) : ne jamais relire le .proto.
///
/// proto3 : un champ à 0 / false / "" est OMIS du fil -> absent = 0 / false / "" (jamais « inconnu »).
/// </summary>
public static class LotEEventsReader
{
    private static Func<string, string, int?>? _fieldNumber;

    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;

    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary>ExchangeRequestedTradeEvent (jyv) : source_id #4, target_id #2, exchange_type #1.</summary>
    public static (long Source, long Target, int Type) ReadTradeRequest(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("ExchangeRequestedTradeEvent", "source_id")),
            PayloadReader.Int(p, F("ExchangeRequestedTradeEvent", "target_id")),
            (int)PayloadReader.Int(p, F("ExchangeRequestedTradeEvent", "exchange_type")));

    /// <summary>PlayerFightFriendlyRequestedEvent (hpu) : fight_id #2, source_id #3, target_id #4.</summary>
    public static (long FightId, long Source, long Target) ReadDuelRequest(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Int(p, F("PlayerFightFriendlyRequestedEvent", "fight_id")),
            PayloadReader.Int(p, F("PlayerFightFriendlyRequestedEvent", "source_id")),
            PayloadReader.Int(p, F("PlayerFightFriendlyRequestedEvent", "target_id")));

    /// <summary>fight_id d'un message (FightRequestCanceledEvent #1, PlayerFightFriendlyAnsweredEvent #5). 0 si absent.</summary>
    public static long ReadFightId(IReadOnlyList<ProtoField> p, string msg)
        => PayloadReader.Int(p, F(msg, "fight_id"));

    /// <summary>FightOptionUpdateEvent (jyq) : option #1 (0 = SECRET, omis), state #2 (false omis), fight_id #3,
    /// team #4 (0 = Challenger, omis ; même codage que SpawnInformation.team).</summary>
    public static (int Option, bool State, long FightId, int Team) ReadFightOptionUpdate(IReadOnlyList<ProtoField> p)
        => ((int)PayloadReader.Int(p, F("FightOptionUpdateEvent", "option")),
            PayloadReader.Bool(p, F("FightOptionUpdateEvent", "state")),
            PayloadReader.Int(p, F("FightOptionUpdateEvent", "fight_id")),
            (int)PayloadReader.Int(p, F("FightOptionUpdateEvent", "team")));

    /// <summary>SpellVariantActivationEvent (hmq) : spell_id #2, effective #1.</summary>
    public static (int SpellId, bool Effective) ReadSpellVariantResult(IReadOnlyList<ProtoField> p)
        => ((int)PayloadReader.Int(p, F("SpellVariantActivationEvent", "spell_id")),
            PayloadReader.Bool(p, F("SpellVariantActivationEvent", "effective")));

    /// <summary>ZaapRespawnUpdatedEvent (hij) : map_id #1.</summary>
    public static long ReadRespawnMap(IReadOnlyList<ProtoField> p)
        => PayloadReader.Int(p, F("ZaapRespawnUpdatedEvent", "map_id"));

    /// <summary>PopupWarningEvent (ioe) : content #1, author #3, lock_duration #2 (secondes).</summary>
    public static (string Content, string Author, int LockSeconds) ReadPopup(IReadOnlyList<ProtoField> p)
        => (PayloadReader.Str(p, F("PopupWarningEvent", "content")) ?? "",
            PayloadReader.Str(p, F("PopupWarningEvent", "author")) ?? "",
            (int)PayloadReader.Int(p, F("PopupWarningEvent", "lock_duration")));
}
