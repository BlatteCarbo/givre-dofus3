using System;
using System.Security.Cryptography;
using System.Text;

namespace GivreClone.App.Services;

/// <summary>Suppression de personnage, forme relevée sur deux vraies suppressions du client 3.7.12.12 (MITM) :
/// PrepareCharacterDeletionRequest = champ 1 (id perso) ; CharacterDeletionRequest = champ 1 (hash, 32 hex minuscules) + champ 2 (id perso).
/// Le hash est MD5(idPerso + "~" + réponse secrète) — sensible à la casse (« Dofus » ≠ « dofus ») — vérifié sur les deux captures. Pure : n'envoie rien.</summary>
public static class CharacterDeletionEncoder
{
    public static string AnswerHash(long characterId, string answer)
    {
        if (characterId <= 0) throw new ArgumentException("id de personnage invalide");
        var h = MD5.HashData(Encoding.UTF8.GetBytes($"{characterId}~{answer ?? ""}"));
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    public static byte[] PrepareBody(long characterId) => Concat(new byte[] { 0x08 }, Varint((ulong)characterId));

    public static byte[] DeleteBody(long characterId, string answer)
        => Concat(new byte[] { 0x0A, 0x20 }, Encoding.ASCII.GetBytes(AnswerHash(characterId, answer)), new byte[] { 0x10 }, Varint((ulong)characterId));

    private static byte[] Concat(params byte[][] p) { var o = new System.Collections.Generic.List<byte>(); foreach (var x in p) o.AddRange(x); return o.ToArray(); }

    private static byte[] Varint(ulong v)
    {
        var o = new System.Collections.Generic.List<byte>();
        while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
        o.Add((byte)v);
        return o.ToArray();
    }
}
