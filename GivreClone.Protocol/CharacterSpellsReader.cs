using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Decode le SpellsEvent (code obfusque "hms" sur le patch actuel) : la liste des sorts APPRIS du personnage. Forme
/// wire confirmee sur trame reelle : repeated #1 { ... , spell_id #3, spell_level #4 } + #2 (spell_visualisation).
/// On renvoie les spell_id ayant un champ #3 (une entree de fin sans #3 est ignoree). Sert a peupler le picker de
/// l'onglet Combat avec les sorts REELLEMENT appris (au lieu de tout le repertoire de la classe).
/// </summary>
public static class CharacterSpellsReader
{
    private static System.Func<string,string,int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string,string,int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    public static IReadOnlyList<int> ReadLearnedSpells(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<int>();
        foreach (var f in tree)
        {
            if (f.Field != F("SpellsEvent", "human_spells") || f.Message is not { } m) continue;
            // 3.6.11.12 : spell_id #2 (avant #3). Capture : hnl = repeated #1 { #1, spell_id #2, grade #5 }
            // et les trois sorts lances (13667 / 13677 / 13676) figurent tous dans cette liste.
            var idF = m.FirstOrDefault(x => x.Field == F("SpellItem", "spell_id") && x.Wire == WireType.Varint);
            if (idF is null) continue;
            int id = (int)idF.Varint;
            if (id > 0 && !list.Contains(id)) list.Add(id);
        }
        return list;
    }

    /// <summary>spell_id -> grade (niveau appris, champ #4). Sert à lire les dégâts du BON grade (spelllevelsdataroot).</summary>
    public static IReadOnlyDictionary<int, int> ReadLearnedSpellLevels(IReadOnlyList<ProtoField> tree)
    {
        var map = new Dictionary<int, int>();
        foreach (var f in tree)
        {
            if (f.Field != F("SpellsEvent", "human_spells") || f.Message is not { } m) continue;
            // 3.6.11.12 : spell_id #2 (avant #3). Capture : hnl = repeated #1 { #1, spell_id #2, grade #5 }
            // et les trois sorts lances (13667 / 13677 / 13676) figurent tous dans cette liste.
            var idF = m.FirstOrDefault(x => x.Field == F("SpellItem", "spell_id") && x.Wire == WireType.Varint);
            if (idF is null) continue;
            int id = (int)idF.Varint;
            int lvl = (int)(m.FirstOrDefault(x => x.Field == F("SpellItem", "spell_level") && x.Wire == WireType.Varint)?.Varint ?? 1);
            if (id > 0 && !map.ContainsKey(id)) map[id] = lvl <= 0 ? 1 : lvl;
        }
        return map;
    }
}
