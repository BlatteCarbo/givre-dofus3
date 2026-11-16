using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Infos personnage extraites d'un paquet : nom, niveau, classe (nom + index d'enum), sexe (nom + index).
/// BreedIndex/GenderIndex = valeurs BRUTES (pour l'icone de classe) ; champs absents -&gt; null.</summary>
public sealed record CharacterInfo(string? Name, int? Level, string? Breed, string? Gender,
                                   int? BreedIndex = null, int? GenderIndex = null);

/// <summary>
/// Extrait les infos d'un personnage depuis un arbre protobuf decode (message porteur : code kvi = liste de
/// personnages). Le personnage est imbrique : kvi -&gt; lpg -&gt; lpe { name #2, level #3, look #4/5/6 }, et le look
/// contient sexe (lpa.#3 = enum 2 valeurs) et classe (lhg.#2 = index 0-18). Numeros verifies sur le client
/// actuel (gm39) avec verite terrain "Sram (3) femme (1)". Robuste a l'imbrication ; numeros propres au patch.
/// </summary>
public static class CharacterInfoReader
{
    private static System.Func<string,string,int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string,string,int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    // Ordre de l'enum Breed (0-18), noms FR d'affichage.
    private static readonly string[] Breeds =
    {
        "Féca", "Osamodas", "Enutrof", "Sram", "Xélor", "Ecaflip", "Eniripsa", "Iop", "Crâ", "Sadida",
        "Sacrieur", "Pandawa", "Roublard", "Zobal", "Steamer", "Eliotrope", "Huppermage", "Ouginak", "Forgelance",
    };

    /// <summary>Nom FR de la classe par index d'enum Breed (0-18, même ordre que le protocole de connexion) ; null si inconnu.</summary>
    public static string? BreedName(int index) => index >= 0 && index < Breeds.Length ? Breeds[index] : null;

    public static CharacterInfo? Extract(IReadOnlyList<ProtoField> tree)
    {
        var lpe = FindCharacterNode(tree);
        return lpe is null ? null : BuildInfo(lpe);
    }

    private static CharacterInfo? BuildInfo(IReadOnlyList<ProtoField> lpe)
    {
        string? name = lpe.FirstOrDefault(f => f.Wire == WireType.Len && f.String is { } s && LooksLikeName(s))?.String;
        int? level = LargestSmallVarint(lpe);
        var (breed, gender, breedIdx, genderIdx) = ExtractBreedGender(lpe);
        if (name is null && level is null) return null;
        return new CharacterInfo(name, level, breed, gender, breedIdx, genderIdx);
    }

    /// <summary>TOUS les personnages d'un CharacterInformation (kvi) : chaque entrée #1 = { base #1, characterId #2 }. Un
    /// compte a PLUSIEURS persos -> on ne peut pas prendre le premier ; le perso joué est choisi via kvw (charId). Renvoie
    /// (charId, infos) pour chacun.</summary>
    public static IReadOnlyList<(long Id, CharacterInfo Info)> ExtractAll(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<(long, CharacterInfo)>();
        foreach (var entry in tree)
        {
            if (entry.Field != F("CharacterListEvent", "characters") || entry.Message is not { } wrap) continue;
            // 3.6.11.12 : chaque entree porte { infos #2, characterId #3 } ; avant le patch { infos #1, id #2 }.
            // Capture 2026-09-08 : { #2 { #1 { look #3, classe #6 }, niveau #4, nom #5 }, #3 = 41234567890 }.
            // On tente la disposition COURANTE puis l ancienne -- et l id est lu dans la MEME disposition que les
            // infos : les melanger reviendrait a prendre l id sur un champ qui n existe pas dans cette version.
            var baseNode = wrap.FirstOrDefault(f => f.Field == F("Character", "character_basic_information") && f.Message is not null)?.Message;
            int idField = F("Character", "id");
            if (baseNode is null || BuildInfo(baseNode) is not { } info) continue;
            long id = (long)(wrap.FirstOrDefault(f => f.Field == idField && f.Wire == WireType.Varint)?.Varint ?? 0);
            list.Add((id, info));
        }
        return list;
    }

    /// <summary>Perso d'une sélection RÉUSSIE (CharacterSelectionEvent.success). Le type imbriqué Success n'a pas d'entrée
    /// propre dans fields.json (nom court en collision) : on prend le sous-message qui a la forme d'un Character (il porte
    /// Character.character_basic_information), par noms v5 seulement. Avant le 04/10, le filtrage passait par le numéro de
    /// CharacterListEvent.characters, égal par coïncidence.</summary>
    public static CharacterInfo? ExtractSelected(IReadOnlyList<ProtoField> success)
    {
        int baseF = F("Character", "character_basic_information");
        foreach (var f in success)
            if (f.Message is { } ch
                && ch.FirstOrDefault(x => x.Field == baseF && x.Message is not null)?.Message is { } b
                && BuildInfo(b) is { } info)
                return info;
        return null;
    }

    /// <summary>Id du perso SÉLECTIONNÉ (CharacterSelectionEvent.success { character { basic, id } }), 0 si illisible. Couvre la
    /// sélection forcée par le serveur (reconnexion en combat), où la requête de sélection du client n'existe pas.</summary>
    public static long ExtractSelectedId(IReadOnlyList<ProtoField> success)
    {
        int baseF = F("Character", "character_basic_information"), idF = F("Character", "id");
        foreach (var f in success)
            if (f.Message is { } ch && ch.Any(x => x.Field == baseF && x.Message is not null)
                && ch.FirstOrDefault(x => x.Field == idF && x.Wire == WireType.Varint) is { } id && (long)id.Varint > 0)
                return (long)id.Varint;
        return 0;
    }

    /// <summary>Compat : (nom, niveau) seuls.</summary>
    public static (string? Name, int? Level) ExtractNameLevel(IReadOnlyList<ProtoField> tree)
    {
        var ci = Extract(tree);
        return (ci?.Name, ci?.Level);
    }

    /// <summary>characterId depuis CharacterInformation (kvi) : payload #1 (lpg) -&gt; #2 (int64). Ou null.
    /// Confirme capture 2026-08-20 (Persodeux -&gt; 612345678901 = son id de combattant). Sert d'owner au havre-sac.</summary>
    public static long? ExtractCharacterId(IReadOnlyList<ProtoField> tree)
    {
        // Numeros deplaces par le patch : on reutilise ExtractAll plutot que de figer #1/#2 une seconde fois.
        foreach (var (id, _) in ExtractAll(tree))
            if (id != 0) return id;
        return null;
    }

    // Le (sous-)message qui EST le personnage : contient un pseudo (string) + un niveau (varint 1-255).
    private static IReadOnlyList<ProtoField>? FindCharacterNode(IReadOnlyList<ProtoField> tree)
    {
        bool hasName = tree.Any(f => f.Wire == WireType.Len && f.String is { } s && LooksLikeName(s));
        bool hasLevel = tree.Any(f => f.Wire == WireType.Varint && f.Varint is >= 1 and <= 255);
        if (hasName && hasLevel) return tree;

        foreach (var f in tree)
            if (f.Message is { } sub)
            {
                var r = FindCharacterNode(sub);
                if (r is not null) return r;
            }
        return null;
    }

    // Le plus grand varint 1-255 = niveau (evite un bool 0/1 en tete, exclut les gros ids).
    private static int? LargestSmallVarint(IReadOnlyList<ProtoField> node)
    {
        int? level = null;
        foreach (var f in node)
            if (f.Wire == WireType.Varint && f.Varint is >= 1 and <= 255 && (level is null || (int)f.Varint > level))
                level = (int)f.Varint;
        return level;
    }

    // Sexe + classe depuis le "look" (sous-message oneof, champ 4/5/6 de lpe). Renvoie aussi les index BRUTS (icone).
    private static (string? Breed, string? Gender, int? BreedIndex, int? GenderIndex) ExtractBreedGender(IReadOnlyList<ProtoField> lpe)
    {
        // 3.6.11.12 : le look est descendu d un cran (lpe.#1.#3) et la CLASSE n est plus dedans mais a cote,
        // en lpe.#1.#6 (= 17 pour Persoun, 9 pour Persozero ; le meme 17 se retrouve dans GameFightShowFighter).
        // Encodage 1-indexe conserve. Le SEXE vit dans CharacterLook.base_information (#7) -> CharacterBaseInformation.gender
        // (#4, 0=Homme/1=Femme) ; confirme capture 2026-09-19 (Persoun femme -> base_information = {#4:1}). Absent (proto3) => 0.
        // TOUT resolu par NOM via le catalogue streame (aucun numero fige)  ; champ inconnu du mapping = non lu.
        var lpe1 = lpe.FirstOrDefault(f => f.Field == F("CharacterBasicInformation", "character_look") && f.Message is not null)?.Message;
        if (lpe1?.FirstOrDefault(f => f.Field == F("CharacterLook", "breed_id") && f.Wire == WireType.Varint) is { } nb && nb.Varint >= 1)
        {
            int bi = (int)nb.Varint - 1;
            int gi = 0;
            var baseInfo = lpe1.FirstOrDefault(f => f.Field == F("CharacterLook", "base_information") && f.Message is not null)?.Message;
            if (baseInfo?.FirstOrDefault(f => f.Field == F("CharacterBaseInformation", "gender") && f.Wire == WireType.Varint) is { } ng)
                gi = (int)ng.Varint;
            return (bi >= 0 && bi < Breeds.Length ? Breeds[bi] : null, gi == 1 ? "Femme" : "Homme", bi, gi);
        }
        // (03/10 : l'ancienne lecture du look Dofus-2 à des numéros figés — lpe.#4/5/6, classe #7, sexe #2.#3 — est retirée.)
        return (null, null, null, null);
    }

    private static bool LooksLikeName(string s)
    {
        if (s.Length is < 2 or > 40) return false;
        bool hasLetter = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c)) hasLetter = true;
            // Apostrophe AUTORISÉE (pseudos type "K'aos"/"L'ombre") : sinon le nom identité (kvi/kvw) sortait faux/nul alors
            // que MapActorsReader, lui, l'accepte -> mismatch -> self jamais matché par nom (position self inconnue en boucle).
            else if (!(char.IsDigit(c) || c is '-' or '_' or ' ' or '\'')) return false;
        }
        return hasLetter;
    }
}
