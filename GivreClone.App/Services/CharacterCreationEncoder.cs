using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Demande de création de personnage. Gender : 0 = masculin, 1 = féminin. HeadId = HeadData.id, BodyId = BodyData.id (jeu), Colors : 6 RVB 24 bits.</summary>
public sealed record CharacterCreationSpec(string Name, int Gender, int BreedId, int HeadId, int BodyId, IReadOnlyList<int> Colors, bool DoTutorial);

/// <summary>Numéros de champ de CharacterRemodelingInformation, lus PAR NOM dans le mapping (jamais codés en dur).</summary>
public sealed record CreationFieldNumbers(int Name, int Gender, int Breed, int Cosmetic, int Body, int Colors);

/// <summary>Sous-message <c>modeling_information</c> de CharacterCreationRequest. Forme relevée sur une vraie création du client 3.7.12.12
/// (nom #2, sexe #3, classe #4, tête #5, corps #7 = BodyData.id, couleurs #8 en varints packés, valeur = RVB + 1). Les valeurs par défaut
/// (sexe masculin = 0) sont omises, comme le fait le client. Pure : n'envoie rien.</summary>
public static class CharacterCreationEncoder
{
    public const int ColorCount = 6;
    public const int MaxNameLength = 20;

    public static byte[] ModelingInfo(CharacterCreationSpec s, CreationFieldNumbers f)
    {
        if (s.BreedId <= 0 || s.HeadId <= 0 || s.BodyId <= 0) throw new ArgumentException("classe, tête et corps requis");
        if (s.Gender is not (0 or 1)) throw new ArgumentException("sexe 0 ou 1");
        if (s.Colors.Count != ColorCount || s.Colors.Any(c => c is < 0 or > 0xFFFFFF)) throw new ArgumentException($"{ColorCount} couleurs RVB 24 bits requises");
        if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > MaxNameLength) throw new ArgumentException("nom invalide");
        if (new[] { f.Name, f.Gender, f.Breed, f.Cosmetic, f.Body, f.Colors }.Any(n => n <= 0)) throw new ArgumentException("champ du mapping introuvable");

        var fields = new List<(int Field, byte[] Bytes)>
        {
            (f.Name, Len(f.Name, System.Text.Encoding.UTF8.GetBytes(s.Name))),
            (f.Breed, Var(f.Breed, (ulong)s.BreedId)),
            (f.Cosmetic, Var(f.Cosmetic, (ulong)s.HeadId)),
            (f.Body, Var(f.Body, (ulong)s.BodyId)),
            (f.Colors, Len(f.Colors, s.Colors.SelectMany(c => Varint((ulong)c + 1)).ToArray())),
        };
        if (s.Gender != 0) fields.Add((f.Gender, Var(f.Gender, (ulong)s.Gender)));
        return fields.OrderBy(x => x.Field).SelectMany(x => x.Bytes).ToArray();
    }

    private static byte[] Var(int field, ulong v) => Varint((ulong)(field << 3)).Concat(Varint(v)).ToArray();
    private static byte[] Len(int field, byte[] body) => Varint((ulong)((field << 3) | 2)).Concat(Varint((ulong)body.Length)).Concat(body).ToArray();

    private static byte[] Varint(ulong v)
    {
        var o = new List<byte>();
        while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
        o.Add((byte)v);
        return o.ToArray();
    }
}
