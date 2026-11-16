using System.Linq;
using Google.Protobuf;
using GivreClone.Protocol.Generated;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Codec GameMessage TYPE-SAFE: forge une requete client a partir d'un message GENERE
/// (Google.Protobuf) + son code obfusque (type_url), et prefixe la longueur (varint) -> trame prete a injecter.
/// Porte le pattern d'un bot tiers (MessageHandler.SendGameRequest: Request{Content=Any, Uid=-1} -> GameMessage).
/// Destine a REMPLACER les forges manuelles (EnvelopeBuilder/MoveForge/WhoAmIForge) une fois valide byte-identique.
/// ⚠️ GELE SUR 3.6.10.11 -- NE PAS BRANCHER EN L'ETAT (aucun appelant en production au 2026-09-08).
/// Les classes protobuf generees (Generated/Givre.cs) figent les numeros d'AVANT le patch 3.6.11.12 :
///   GameMessage.request = #2   -> c'est desormais le slot EVENEMENT (#1 = requete). Cf EnvelopeBuilder.
///   MapMovementRequest { map_id = 1, key_cells = 2 }  -> c'est desormais { key_cells = 1, map_id = 3 }.
/// Une trame produite ici part donc dans la mauvaise branche de l'enveloppe avec les mauvais champs, et le
/// serveur la jette SANS erreur ni reponse. Regenerer les classes depuis le .proto du patch courant AVANT
/// de remplacer EnvelopeBuilder/MoveForge par ce chemin, et re-activer les deux tests desactives de SmokeTests.
/// </summary>
public static class GameCodec
{
    public const string TypePrefix = "type.ankama.com/";

    /// <summary>Enveloppe un message client en GameMessage{Request{Any{type_url,value}, uid}} (SANS prefixe de
    /// longueur). code = code obfusque du message sur le patch courant (ex "jrw", "lqb"). uid=-1 par defaut.</summary>
    public static byte[] BuildRequest(string code, IMessage payload, long uid = -1)
    {
        var gm = new GameMessage
        {
            Request = new ClientRequest
            {
                Content = new Generated.Any { TypeUrl = TypePrefix + code, Value = payload.ToByteString() },
                Uid = uid,
            },
        };
        return gm.ToByteArray();
    }

    /// <summary>Comme BuildRequest mais PREFIXE la longueur (varint) -> trame exacte a envoyer/injecter sur le fil.</summary>
    public static byte[] BuildFramedRequest(string code, IMessage payload, long uid = -1)
    {
        byte[] gm = BuildRequest(code, payload, uid);
        return RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
    }
}
