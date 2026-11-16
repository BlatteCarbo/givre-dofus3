using System;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Assemble une frame GameMessage DE ZERO (GivreCore ne sait qu'EDITER une frame capturee via WithAnyPayload).
/// Grammaire (les CODES `hea/heb/hee/hdy` ET les numeros de slot de l'enveloppe viennent DESORMAIS DU MAPPING,
/// resolus PAR NOM via <see cref="UseCatalog"/> ; les litteraux ci-dessous ne sont plus que des REPLIS de secours) :
///   GameMessage { request ; event ; response }   (mapping : request=1, event=2, response=3)
///   Request  { content (Any) ; uid }             (mapping : content=1, uid=2)
///   Event    { content (Any) }                    (mapping : content=3 — a TOURNE, cf plus bas)
///   Response { content (Any) ; uid }             (mapping : content=1, uid=2)
///   Any      { string type_url=1 ; bytes value=2 }   type_url = "type.ankama.com/&lt;code&gt;"
/// Retourne les octets du GameMessage (NON prefixes) : TcpProxyServer.Replay ajoute lui-meme le varint(len).
/// </summary>
public static class EnvelopeBuilder
{
    public const string TypeUrlPrefix = "type.ankama.com/";

    // ============================ RESOLUTION PAR NOM (branchee 1x au demarrage) ============================
    // Le ONEOF de l'enveloppe (GameMessage) et les champs de Request/Event/Response SONT dans le mapping streame
    // (matching_current.json pour les codes hea/heb/hee/hdy ; fields.json pour GameMessage/Request/Event/Response).
    // On les resout donc PAR NOM comme tout le reste : un patch qui repermute le oneof (c'est arrive en 3.6.11.12,
    // cf historique ci-dessous) se corrige alors par un simple update du bundle, SANS recompiler. Sans catalogue
    // branche (certains tests unitaires), on retombe sur les REPLIS = les numeros du client actuel (zero regression).
    private static Func<string, string, int?>? _fieldNumber;

    /// <summary>Branche le catalogue (mapping streame). Sans lui, on utilise les replis (numeros du client actuel).</summary>
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;

    /// <summary>Numero de slot/champ d'enveloppe resolu PAR NOM via le mapping, ou le repli (numero courant) si absent.</summary>
    // Champ d'enveloppe absent du mapping : on REFUSE de forger (une trame à numéro inventé partirait dans le vide ou
    // serait mal lue par le serveur) plutôt que de deviner.
    private static int Slot(string message, string field) => _fieldNumber?.Invoke(message, field)
        ?? throw new InvalidOperationException($"Enveloppe : {message}.{field} absent du mapping, trame non forgée.");

    // ================================ SLOTS DE L'ENVELOPPE (replis) ================================
    // GameMessage (code `hea`) est un ONEOF de trois branches, et CE ONEOF A TOURNE au patch 3.6.11.12.
    // Preuve, prise dans les deux .proto derives du client (GivreProxy/.pipeline-out) :
    //
    //   3.6.10.11   hea { hdx=1 {Any, repeated int32}   <- Event
    //                     hdy=2 {Any, int32}            <- REQUEST   (ce que le bot ecrivait)
    //                     hdw=3 {Any, int32} }          <- Response
    //
    //   3.6.11.12   hea { heb=1 {Any, int32}            <- REQUEST   (deplace en #1)
    //                     hee=2 {metadonnees, Any content=3, ...} <- Event (content decale en #3)
    //                     hdy=3 {Any, int32} }          <- Response
    //
    // Consequence si on reste sur un slot perime : la requete part dans la mauvaise branche, le serveur ne peut pas
    // la lire -> il la jette SANS REPONSE NI ERREUR. Blocage TOTAL du chemin emission, invisible dans les journaux.
    // La LECTURE, elle, n'est pas concernee : RawProtobuf cherche le type_url en profondeur, quel que soit le slot.
    //
    // C'est precisement pour ne PLUS dependre d'un slot fige qu'on resout maintenant PAR NOM via le mapping (Slot()).
    // Les valeurs ci-dessous ne servent QUE de repli quand aucun catalogue n'est branche (numeros du client actuel).
    private static int GameMessageRequestField => Slot("GameMessage", "request");   // heb
    private static int GameMessageEventField => Slot("GameMessage", "event");        // hee
    private static int GameMessageResponseField => Slot("GameMessage", "response");  // hdy
    private static int RequestContentField => Slot("Request", "content");
    private static int RequestUidField => Slot("Request", "uid");
    private static int EventContentField => Slot("Event", "content");   // v4 : content decale en #3 (etait #2)
    private static int ResponseUidField => Slot("Response", "uid");

    // Any est le type BIEN CONNU google.protobuf.Any : sa disposition { type_url=1 ; value=2 } est definie par
    // google/protobuf/any.proto, PAS par Ankama -> stable par construction, absente du mapping, jamais permutee.
    private const int AnyTypeUrlField = 1;
    private const int AnyValueField = 2;

    /// <summary>Enveloppe Any { type_url = "type.ankama.com/&lt;code&gt;" ; value = payload }. Encodage CANONIQUE proto3
    /// (celui du vrai client, Google.Protobuf) : un payload VIDE omet le champ value (il n'émet jamais « 12 00 »).</summary>
    public static List<ProtoField> BuildAny(string code, IReadOnlyList<ProtoField> payload)
    {
        var any = new List<ProtoField> { new ProtoField { Field = AnyTypeUrlField, Wire = WireType.Len, String = TypeUrlPrefix + code } };
        if (payload.Count > 0)
            any.Add(new ProtoField { Field = AnyValueField, Wire = WireType.Len, Message = new List<ProtoField>(payload) });
        return any;
    }

    /// <summary>Action client -&gt; serveur : GameMessage { Request { Any content ; uid } }. Canonique comme le vrai client :
    /// uid = 0 est OMIS (valeur par défaut proto3) ; uid = -1 (la convention du client pour ~tous ses messages, confirmé par
    /// les tailles de trames MITM) reste émis sur 10 octets.</summary>
    public static byte[] BuildClientRequest(string code, IReadOnlyList<ProtoField> payload, int uid)
    {
        var any = BuildAny(code, payload);
        // Ordre d'écriture du vrai client (vérifié sur capture 3.7.11.11) : uid (#1) AVANT content (#2), c'est-à-dire par numéro croissant.
        var request = new List<ProtoField>();
        if (uid != 0)
            request.Add(new ProtoField { Field = RequestUidField, Wire = WireType.Varint, Varint = unchecked((ulong)(long)uid) }); // uid int64 (sign-extend)
        request.Add(new ProtoField { Field = RequestContentField, Wire = WireType.Len, Message = any });
        if (request.Count > 1 && request[0].Field > request[1].Field) request.Reverse();   // numérotation inversée par un patch : toujours croissant
        var gameMessage = new List<ProtoField>
        {
            new ProtoField { Field = GameMessageRequestField, Wire = WireType.Len, Message = request },
        };
        return RawProtobuf.Encode(gameMessage);
    }

    /// <summary>Push serveur -&gt; client (spoof d'un Event vers le client). GameMessage { Event { Any content } }.</summary>
    public static byte[] BuildServerEvent(string code, IReadOnlyList<ProtoField> payload)
    {
        var any = BuildAny(code, payload);
        var evt = new List<ProtoField>
        {
            new ProtoField { Field = EventContentField, Wire = WireType.Len, Message = any },
        };
        var gameMessage = new List<ProtoField>
        {
            new ProtoField { Field = GameMessageEventField, Wire = WireType.Len, Message = evt },
        };
        return RawProtobuf.Encode(gameMessage);
    }

    /// <summary>Lit l'uid d'enveloppe (Request.uid ou Response.uid) d'une frame brute, ou null. Slots resolus par le mapping.</summary>
    public static int? ReadEnvelopeUid(byte[] rawFrame)
    {
        List<ProtoField> tree;
        try { tree = RawProtobuf.Parse(rawFrame); } catch { return null; }
        int reqSlot = GameMessageRequestField, respSlot = GameMessageResponseField;
        int reqUid = RequestUidField, respUid = ResponseUidField;
        foreach (var f in tree)
        {
            if (f.Wire != WireType.Len || f.Message is not { } inner) continue;
            if (f.Field == reqSlot)
                foreach (var g in inner) if (g.Field == reqUid && g.Wire == WireType.Varint) return (int)g.Varint;
            if (f.Field == respSlot)
                foreach (var g in inner) if (g.Field == respUid && g.Wire == WireType.Varint) return (int)g.Varint;
        }
        return null;
    }
}
