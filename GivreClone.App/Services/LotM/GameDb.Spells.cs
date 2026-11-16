using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.LotM;

/// <summary>Règles d'un grade de sort (SpellLevelData) : niveau du perso requis, lancers max par tour / par cible, relance (tours entre
/// deux lancers) et relance initiale (tours avant le premier lancer). 0 = pas de limite.</summary>
public sealed record SpellLevelRule(int Grade, int MinPlayerLevel, int MaxCastPerTurn, int MaxCastPerTarget, int MinCastInterval, int InitialCooldown);

public sealed record SpellStateDamageRule(bool Invulnerable, bool InvulnerableMelee, bool InvulnerableRange, bool CantDealDamage);

public sealed partial class GameDb
{
    /// <summary>Resolve an observed catalogue row only when it belongs to the observed spell.</summary>
    public int? SpellGradeForLevel(int spellId, int levelId)
        => !IsKnownTable("SpellLevelData") ? null
         : Query("select grade from SpellLevelData where id = $0 and spellId = $1", levelId, spellId)
            .Select(r => (int?)I(r[0])).FirstOrDefault();

    /// <summary>Propriétés du catalogue sélectionné, pas une observation des états d'un combattant. Null si inconnu.</summary>
    public SpellStateDamageRule? SpellStateDamage(int stateId)
        => stateId <= 0 || !IsKnownTable("SpellStateData") ? null
         : Query("select invulnerable, invulnerableMelee, invulnerableRange, cantDealDamage from SpellStateData where id = $0", stateId)
            .Select(r => new SpellStateDamageRule(I(r[0]) != 0, I(r[1]) != 0, I(r[2]) != 0, I(r[3]) != 0))
            .FirstOrDefault();

    /// <summary>Grades d'un sort, du plus bas au plus haut (vide si la table est absente ou le sort inconnu).</summary>
    public IReadOnlyList<SpellLevelRule> SpellLevelRules(int spellId)
        => spellId <= 0 || !IsKnownTable("SpellLevelData") ? System.Array.Empty<SpellLevelRule>()
         : Query("select grade, minPlayerLevel, maxCastPerTurn, maxCastPerTarget, minCastInterval, initialCooldown from SpellLevelData where spellId = $0 order by grade", spellId)
            .Select(r => new SpellLevelRule(I(r[0]), I(r[1]), I(r[2]), I(r[3]), I(r[4]), I(r[5]))).ToList();

    /// <summary>Niveau du perso à partir duquel le sort est disponible (grade 1), 0 si inconnu.</summary>
    public int SpellUnlockLevel(int spellId) => SpellLevelRules(spellId).FirstOrDefault()?.MinPlayerLevel ?? 0;

    /// <summary>Grade atteint au niveau <paramref name="level"/> (le plus haut dont le niveau requis est atteint), ou null s'il n'est pas débloqué.</summary>
    public SpellLevelRule? SpellRuleAt(int spellId, int level)
        => SpellLevelRules(spellId).LastOrDefault(r => r.MinPlayerLevel <= level);
}
