using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>Un skill d'un element interactif (recolter, couper, utiliser, TP...) : uid d'INSTANCE (a renvoyer dans
/// InteractiveUseRequest), id de skill (catalogue), nameId, et s'il est ACTIVE (utilisable maintenant) ou desactive
/// (ex ressource epuisee : le skill passe en disabled).</summary>
public sealed record InteractiveSkill(int SkillInstanceUid, int SkillId, int NameId, bool Enabled);

/// <summary>
/// Un element interactif pose sur la carte (ressource, zaap, coffre, atelier...). La CELLULE vient du StatedElement
/// ou de la mapData du client ; le TYPE (ElementTypeId -&gt; Name/icone) + les SKILLS viennent de l'InteractiveElement.
/// Recoltable/utilisable = possede au moins un skill ACTIVE (independant de State, qui est ambigu). Name resolu cote App.
/// </summary>
public sealed record MapInteractive(int CellId, int ElementId, int ElementTypeId, int State,
                                    IReadOnlyList<InteractiveSkill> Skills, string? Name = null, string? ActionName = null)
{
    // skillIds GENERIQUES non-recolte. On preferera un vrai
    // skill de recolte a ces actions generiques (ex "Utiliser") quand on choisit lequel envoyer.
    private static readonly HashSet<int> GenericSkills = new() { 339, 360, 361, 362 };

    /// <summary>Disponible MAINTENANT : possede un skill ACTIVE (losange vert / icone pleine opacite). On NE recolte que
    /// dans ce cas -&gt; evite le "Action impossible" (iwq) renvoye par le serveur sur une ressource indisponible.</summary>
    public bool Harvestable => Skills.Any(s => s.Enabled);

    /// <summary>Le skill ACTIF a envoyer pour recolter (1er actif non-generique, sinon 1er actif), ou null si aucun
    /// actif = ressource indisponible/epuisee -&gt; on NE tente PAS.</summary>
    public InteractiveSkill? HarvestSkill =>
        Skills.FirstOrDefault(s => s.Enabled && !GenericSkills.Contains(s.SkillId))
        ?? Skills.FirstOrDefault(s => s.Enabled);

    /// <summary>Skill representatif pour NOMMER l'interactif (verbe) : actif de preference, sinon desactive (ex
    /// ressource epuisee : on veut quand meme afficher "Couper"/"Pecher"). NE PAS utiliser pour envoyer une recolte.</summary>
    public InteractiveSkill? GatherSkill =>
        HarvestSkill
        ?? Skills.FirstOrDefault(s => !GenericSkills.Contains(s.SkillId))
        ?? (Skills.Count > 0 ? Skills[0] : null);

    /// <summary>uid d'instance du skill ACTIF a envoyer dans InteractiveUseRequest, ou null si indisponible.</summary>
    public int? SkillInstanceUid => HarvestSkill?.SkillInstanceUid;

    /// <summary>Choix du skill a envoyer (primitive generique map:useById). sel == 0 : HarvestSkill (meme choix que
    /// goUseInteractive). sel &lt; 0: -N = N-ieme skill ACTIF (1-based, ordre du paquet, convention tiers useById(elem,-1)).
    /// sel &gt; 0 : skill ACTIF dont SkillInstanceUid == sel, sinon dont SkillId == sel. null si aucun.</summary>
    public InteractiveSkill? PickSkill(int sel)
    {
        if (sel == 0) return HarvestSkill;
        var enabled = Skills.Where(s => s.Enabled).ToList();
        if (sel < 0) { long n = -(long)sel; return n <= enabled.Count ? enabled[(int)n - 1] : null; }
        return enabled.FirstOrDefault(s => s.SkillInstanceUid == sel) ?? enabled.FirstOrDefault(s => s.SkillId == sel);
    }
}
