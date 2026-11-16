using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Extrait le serverId d'un WhoIsEvent (reponse a un /whois - code lqe sur le client actuel). Le NOM du serveur
/// ne transite jamais ; seul l'id circule. Le numero de champ derive selon le patch, donc on retient la valeur
/// varint qui RESOUT en nom de serveur (delegue alimente par serversdataroot). Confirme sur dump reel : le
/// WhoIsEvent de "Persodeux" porte 294 -> "Hell Mina". Meme principe que un bot tiers (ServerHelper) et un bot tiers
/// (D2Oparsing.GetServerName), qui lisent l'id d'un message puis resolvent le nom en local.
/// </summary>
public static class WhoIsReader
{
    public static int ServerId(IReadOnlyList<ProtoField> tree, Func<int, string?> resolves)
    {
        foreach (var f in tree)
        {
            if (f.Wire != WireType.Varint) continue;
            var v = f.Varint;
            if (v == 0 || v > int.MaxValue) continue;      // ids serveur petits ; ecarte character_id, verbose=-1...
            if (resolves((int)v) is not null) return (int)v;
        }
        return 0;
    }
}
