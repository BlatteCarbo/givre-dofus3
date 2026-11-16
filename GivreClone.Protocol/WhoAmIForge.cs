using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Forge la frame WhoAmIRequest (code lqb sur le client actuel : { bool verbose = 1 }), prefixee varint(len),
/// prete a etre injectee CLIENT-&gt;SERVEUR sur la connexion de JEU. Le serveur repond par un WhoIsEvent (lqe)
/// qui porte le serverId -&gt; nom du serveur (resolu en local). C'est la methode d'un bot tiers (ForgeAndSend
/// WhoIsRequest sur son propre perso). Le NOM ne transite jamais ; on ne fait que declencher l'evenement.
/// </summary>
public static class WhoAmIForge
{
    /// <summary>
    /// GameMessage forge (NON prefixe) pour WhoAmIRequest, reconstruit a l'IDENTIQUE de la vraie trame /whoami
    /// capturee sur le patch actuel (les numeros d'enveloppe ont derive vs EnvelopeBuilder/proto 2025) :
    ///   GameMessage { #2 { #1 = Any{ #1 type_url ; #2 value = lqb{#1 verbose=1} } ; #2 = uid = -1 (int64) } }
    /// Le uid = -1 encode en int64 (10 octets) est la convention des requetes du client (reponse = Event, pas
    /// liee a l'uid). Si les numeros redrivent, la trame REELLE capturee (rejeu) prend le relais automatiquement.
    /// </summary>
    /// <param name="code">code du message, lu dans le mapping streamé (jamais figé : il tourne à chaque patch).</param>
    /// <param name="verboseField">numéro du champ « verbose », lu dans le mapping streamé.</param>
    public static byte[] BuildGameMessage(string code, int verboseField)
    {
        var lqb = new List<ProtoField> { new() { Field = verboseField, Wire = WireType.Varint, Varint = 1 } }; // verbose = true
        return EnvelopeBuilder.BuildClientRequest(code, lqb, uid: -1);   // enveloppe validee sur trame reelle
    }

    /// <summary>Prefixe une trame GameMessage (forgee OU capturee) du varint(len) -&gt; prete a injecter.</summary>
    public static byte[] Frame(byte[] gameMessage) =>
        RawProtobuf.EncodeVarint((ulong)gameMessage.Length).Concat(gameMessage).ToArray();
}
