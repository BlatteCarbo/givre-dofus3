using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Extrait les ELEMENTS INTERACTIFS d'un MapComplementaryInformationEvent decode.
///
/// ⚠️ TOUS les numeros ci-dessous sont RENUMEROTES a chaque patch Dofus, y compris CEUX DES SOUS-MESSAGES.
/// Ils sont donc resolus PAR NOM via <see cref="UseCatalog"/> (fields.json streame) ; les constantes ne sont
/// qu'un repli de capture. Verite du 3.6.11.12, prise sur trames reelles (log 19:23, element 513989 recolte) :
///
///   MapComplementaryInformationEvent : interactive_elements = #8 (etait #11) ; stated_elements = #15.
///   InteractiveElement  : #1 age_bonus(opt) ; #2 element_type_id ; #3 on_current_map ; #4 element_id ;
///                         #5 enabled_skills[] ; #6 disabled_skills[].
///   StatedElement       : #1 on_current_map ; #2 state ; #3 element_id ; #4 cell_id.
///                         (cell et state ont ECHANGE de place depuis 3.6.10.11 : cellules lues comme "1".)
///   InteractiveElementSkill : #1 name_id(opt) ; #2 skill_instance_uid ; #3 skill_id.
///   StatedElementUpdatedEvent      : le StatedElement est au champ #3 (etait #1).
///   InteractiveElementUpdatedEvent : l'InteractiveElement est au champ #1.
///
/// PREUVE enabled/disabled : element 513989 porte son skill en #5 avant la recolte, en #6 juste apres
/// (meme message ive, state passe a 2) : #5 = enabled, #6 = disabled.
///
/// ⚠️ PIEGE DU PATCH : le filtre on_current_map etait fige sur #1, devenu age_bonus (absent) : TOUS les
/// interactifs etaient rejetes en silence, donc plus aucune ressource sur la carte interactive.
///
/// Jointure element_id (StatedElement -&gt; cellule+etat) x (InteractiveElement -&gt; type+skills). On itere les
/// InteractiveElement et on joint ; cellule de repli = mapData du client. cellId hors [0,559] -&gt; ignore.
/// </summary>
public static class MapInteractivesReader
{
    private const int CellCount = 560;

    private static Func<string, string, int?>? _fieldNumber;

    /// <summary>Branche le catalogue (fields.json streame) : les numeros de champ, IMBRIQUES COMPRIS, sont
    /// alors resolus par nom clair et survivent au prochain patch sans recompilation.</summary>
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;

    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <param name="cellForElement">element_id -&gt; cellId depuis la mapData du client (positions des interactifs).
    /// Necessaire pour les interactifs SANS StatedElement (zaaps, etc.) dont la cellule n'est pas dans le protocole.</param>
    public static IReadOnlyList<MapInteractive> Extract(IReadOnlyList<ProtoField> tree, Func<int, int?>? cellForElement = null,
                                                        int? statedField = null, int? interactiveField = null,
                                                        Action<int, int, IReadOnlyList<InteractiveSkill>>? droppedNoCell = null)
    {
        int fStated = statedField ?? F("MapComplementaryInformationEvent", "stated_elements");
        int fInter = interactiveField ?? F("MapComplementaryInformationEvent", "interactive_elements");

        int seState = F("StatedElement", "state"), seElem = F("StatedElement", "element_id"), seCell = F("StatedElement", "cell_id");
        int ieOnMap = F("InteractiveElement", "on_current_map"), ieType = F("InteractiveElement", "element_type_id");
        int ieElem = F("InteractiveElement", "element_id");

        // 1) StatedElement : element_id -> (cell_id, state ; state omis = 0 = disponible, defaut proto3).
        var stated = new Dictionary<int, (int Cell, int State)>();
        foreach (var f in tree)
        {
            if (f.Field != fStated || f.Message is not { } se) continue;
            if (Var(se, seElem) is not long elementId) continue;
            stated[(int)elementId] = ((int)(Var(se, seCell) ?? -1), (int)(Var(se, seState) ?? 0));
        }

        // 2) InteractiveElement : itere TOUS les interactifs. Cellule = StatedElement sinon mapData (zaaps sans etat).
        var list = new List<MapInteractive>();
        foreach (var f in tree)
        {
            if (f.Field != fInter || f.Message is not { } ie) continue;
            if ((Var(ie, ieOnMap) ?? 0) != 1) continue;             // on_current_map=false: ressource d'une carte VOISINE
            if (Var(ie, ieElem) is not long elementIdL) continue;
            int elementId = (int)elementIdL;
            int typeId = (int)(Var(ie, ieType) ?? 0);

            int cell, state;
            if (stated.TryGetValue(elementId, out var st) && st.Cell >= 0) { cell = st.Cell; state = st.State; }
            else if (cellForElement?.Invoke(elementId) is int mc) { cell = mc; state = stated.TryGetValue(elementId, out var s2) ? s2.State : 0; }
            else { droppedNoCell?.Invoke(elementId, typeId, ReadSkills(ie)); continue; }   // pas de cellule connue : non affichable (signalé pour diagnostic)
            if (cell is < 0 or >= CellCount) continue;

            list.Add(new MapInteractive(cell, elementId, typeId, state, ReadSkills(ie)));
        }
        return list;
    }

    /// <summary>Lit un InteractiveElementUpdatedEvent : retrouve le sous-message InteractiveElement, dans le champ
    /// porteur OU au top-level, et renvoie (element_id, skills). Sert a rafraichir en direct l'etat RECOLTABLE
    /// (disponible -&gt; epuise). Null si absent.</summary>
    public static (int ElementId, IReadOnlyList<InteractiveSkill> Skills)? ReadElementSkillsUpdate(IReadOnlyList<ProtoField> tree)
    {
        int wrap = F("InteractiveElementUpdatedEvent", "interactive_element");
        if (tree.FirstOrDefault(x => x.Field == wrap && x.Message is not null)?.Message is { } w && TryElement(w) is { } hit) return hit;
        if (TryElement(tree) is { } top) return top;
        foreach (var f in tree)
            if (f.Message is { } sub && TryElement(sub) is { } e) return e;
        return null;
    }

    private static (int, IReadOnlyList<InteractiveSkill>)? TryElement(IReadOnlyList<ProtoField> ie)
    {
        if (Var(ie, F("InteractiveElement", "element_id")) is not long elementIdL) return null;
        var skills = ReadSkills(ie);
        return skills.Count == 0 ? null : ((int)elementIdL, skills);
    }

    // enabled_skills / disabled_skills : le MEME skill bascule de la premiere liste vers la seconde quand la
    // ressource est epuisee. Le drapeau Enabled porte donc tout l'etat "recoltable".
    private static IReadOnlyList<InteractiveSkill> ReadSkills(IReadOnlyList<ProtoField> ie)
    {
        int on = F("InteractiveElement", "enabled_skills"), off = F("InteractiveElement", "disabled_skills");
        int sUid = F("InteractiveElementSkill", "skill_instance_uid"), sId = F("InteractiveElementSkill", "skill_id");
        int sName = F("InteractiveElementSkill", "name_id");
        var skills = new List<InteractiveSkill>();
        foreach (var s in ie)
            if ((s.Field == on || s.Field == off) && s.Message is { } sk && Var(sk, sUid) is long uid && Var(sk, sId) is long skId)
                skills.Add(new InteractiveSkill((int)uid, (int)skId, (int)(Var(sk, sName) ?? 0), s.Field == on));
        return skills;
    }

    /// <summary>Lit le StatedElement d'un StatedElementUpdatedEvent (porteur #3 sur 3.6.11.12, premier sous-message
    /// en repli). Retourne (cell_id, element_id, state) ou null.</summary>
    public static (int Cell, int ElementId, int State)? ReadStatedUpdate(IReadOnlyList<ProtoField> tree)
    {
        int wrap = F("StatedElementUpdatedEvent", "stated_element");
        var se = tree.FirstOrDefault(x => x.Field == wrap && x.Message is not null)?.Message
              ?? tree.FirstOrDefault(x => x.Message is not null)?.Message;
        if (se is null) return null;
        if (Var(se, F("StatedElement", "element_id")) is not long elementId) return null;
        if (Var(se, F("StatedElement", "cell_id")) is not long cell) return null;
        return ((int)cell, (int)elementId, (int)(Var(se, F("StatedElement", "state")) ?? 0));
    }

    private static long? Var(IReadOnlyList<ProtoField> node, int field)
    {
        var pf = node.FirstOrDefault(x => x.Field == field && x.Wire == WireType.Varint);
        return pf is null ? null : (long)pf.Varint;
    }
}
