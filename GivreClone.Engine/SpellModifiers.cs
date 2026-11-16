using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Engine;

/// <summary>
/// Effets de « modification de sort » du jeu (identifiants d'effet vérifiés sur les textes du client, fr.bin) : un buff comme
/// la posture de tir du Crâ (32465) donne +3 portée minimale / +4 portée maximale à ses sorts pendant un tour. Le serveur REFUSE
/// alors, sans réponse, un lancer sous la nouvelle portée minimale : sans ce modèle le bot visait à 1-3 cases et perdait son
/// attaque (35 sorts ignorés sur 102 après ce buff, journaux du 24/10).
/// </summary>
public static class SpellModifierActions
{
    public const int MinRangeUp = 280;       // « +N Portée minimale »
    public const int MaxRangeUp = 281;       // « +N Portée maximale »
    public const int RangeModifiable = 282;  // « Portée modifiable »
    public const int ApCostDown = 285;       // « -N PA »
    public const int CastInLineOff = 288;    // « lancer en ligne désactivé »
    public const int LineOfSightOff = 289;   // « ligne de vue désactivée »
    public const int CastsPerTurnUp = 290;   // « +N lancer(s) par tour »
    public const int CastsPerTargetUp = 291; // « +N lancer(s) par cible »
    public const int MaxRangeDown = 294;     // « -N Portée maximale »
    public const int MinRangeDown = 295;     // « -N Portée minimale »
    public const int ApCostUp = 296;         // « +N PA »

    public static bool IsModifier(int actionId) => actionId is MinRangeUp or MaxRangeUp or RangeModifiable or ApCostDown
        or CastInLineOff or LineOfSightOff or CastsPerTurnUp or CastsPerTargetUp or MaxRangeDown or MinRangeDown or ApCostUp;
}

/// <summary>Un effet de modification porté par un sort : <paramref name="TargetSpellId"/> reçoit le modificateur
/// <paramref name="ActionId"/> de valeur <paramref name="Value"/> pendant <paramref name="Duration"/> tours du lanceur.</summary>
public readonly record struct SpellModifierEffect(int ActionId, int TargetSpellId, int Value, int Duration);

/// <summary>Caractéristiques de lancer d'un sort (PA, portées, ligne de vue) avant/après modificateurs.</summary>
public readonly record struct SpellCastStats(int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable);

/// <summary>
/// Modificateurs de sort ACTIFS d'un combattant. Un modificateur posé au tour T avec une durée D vaut tant que le tour courant est
/// strictement inférieur à T + D (durée 1 = jusqu'au début du prochain tour du lanceur, donc le reste de ce tour-ci). Durée négative :
/// jusqu'à la fin du combat. Non thread-safe : l'appelant sérialise (un tour à la fois par combattant).
/// </summary>
public sealed class SpellModifierSet
{
    private readonly record struct Active(int SpellId, int ActionId, int Value, int ExpiresAtTurn);
    private readonly List<Active> _active = new();
    private readonly object _gate = new();   // le réseau pose les modificateurs pendant que le tour les lit
    private int _version;

    public bool Any { get { lock (_gate) return _active.Count > 0; } }
    public int Count { get { lock (_gate) return _active.Count; } }
    /// <summary>Change à chaque pose / expiration / remise à zéro : permet de savoir qu'il faut recalculer les fenêtres de lancer.</summary>
    public int Version { get { lock (_gate) return _version; } }

    /// <summary>Pose les effets d'un sort lancé au tour <paramref name="turn"/>. Les effets sans sort cible (id 0) sont ignorés.</summary>
    public void Apply(IEnumerable<SpellModifierEffect> effects, int turn)
    {
        lock (_gate)
            foreach (var e in effects)
            {
                if (e.TargetSpellId <= 0 || !SpellModifierActions.IsModifier(e.ActionId)) continue;
                int expires = e.Duration < 0 ? int.MaxValue : turn + Math.Max(1, e.Duration);
                // Relancer le même buff renouvelle sa durée au lieu de cumuler (maxStack 1 pour ces effets de portée).
                _active.RemoveAll(a => a.SpellId == e.TargetSpellId && a.ActionId == e.ActionId && a.Value == e.Value);
                _active.Add(new Active(e.TargetSpellId, e.ActionId, e.Value, expires));
                _version++;
            }
    }

    /// <summary>Début du tour <paramref name="turn"/> : retire ce qui est expiré.</summary>
    public void ExpireAt(int turn) { lock (_gate) if (_active.RemoveAll(a => a.ExpiresAtTurn <= turn) > 0) _version++; }

    public void Clear() { lock (_gate) { if (_active.Count > 0) _version++; _active.Clear(); } }

    // Appelés sous _gate uniquement.
    private int Sum(int spellId, int actionId) => _active.Where(a => a.SpellId == spellId && a.ActionId == actionId).Sum(a => a.Value);
    private bool Has(int spellId, int actionId) => _active.Any(a => a.SpellId == spellId && a.ActionId == actionId);

    /// <summary>Caractéristiques effectives du sort, modificateurs actifs appliqués (jamais de portée minimale supérieure à la maximale côté
    /// serveur : on garde la maximale, comme le jeu ; PA jamais négatifs).</summary>
    public SpellCastStats Adjust(int spellId, SpellCastStats b)
    {
        lock (_gate) return AdjustLocked(spellId, b);
    }

    private SpellCastStats AdjustLocked(int spellId, SpellCastStats b)
    {
        if (_active.Count == 0) return b;
        int min = b.MinRange + Sum(spellId, SpellModifierActions.MinRangeUp) - Sum(spellId, SpellModifierActions.MinRangeDown);
        int max = b.Range + Sum(spellId, SpellModifierActions.MaxRangeUp) - Sum(spellId, SpellModifierActions.MaxRangeDown);
        int ap = b.Ap - Sum(spellId, SpellModifierActions.ApCostDown) + Sum(spellId, SpellModifierActions.ApCostUp);
        return b with
        {
            Ap = Math.Max(0, ap),
            MinRange = Math.Max(0, min),
            Range = Math.Max(0, max),
            Rangeable = b.Rangeable || Has(spellId, SpellModifierActions.RangeModifiable),
            CastInLine = b.CastInLine && !Has(spellId, SpellModifierActions.CastInLineOff),
            CastTestLos = b.CastTestLos && !Has(spellId, SpellModifierActions.LineOfSightOff),
        };
    }

    public int ExtraCastsPerTurn(int spellId) { lock (_gate) return Sum(spellId, SpellModifierActions.CastsPerTurnUp); }
    public int ExtraCastsPerTarget(int spellId) { lock (_gate) return Sum(spellId, SpellModifierActions.CastsPerTargetUp); }
}
