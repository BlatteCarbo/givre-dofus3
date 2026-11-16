using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.SpellParity;

/// <summary>
/// Présentation des différences des sorts importés. La prévisualisation ne change ni
/// le profil ni les droits du moteur, et n'essaie pas d'émuler les cibles legacy.
/// </summary>
public static class SpellParityDisplay
{
    public static IReadOnlyList<string> ChangedFields(SpellConfig source, SpellConfig other)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(other);

        var changes = new List<string>();
        void Add<T>(string label, T a, T b)
        {
            if (!EqualityComparer<T>.Default.Equals(a, b)) changes.Add(label);
        }

        // L'ordre privilégie le type de cible et les restrictions, plus dangereux
        // à importer sans s'en rendre compte qu'une différence de priorité.
        Add("cible", source.TargetKind, other.TargetKind);
        Add("sur soi", source.OnMe, other.OnMe);
        Add("buff sur soi", source.SelfBuff, other.SelfBuff);
        Add("invocation", source.IsSummon, other.IsSummon);
        Add("invocations max", source.MaxSummons, other.MaxSummons);
        Add("PV cible min", source.TgtHpMin, other.TgtHpMin);
        Add("PV cible max", source.TgtHpMax, other.TgtHpMax);
        Add("PV soi min", source.SelfHpMin, other.SelfHpMin);
        Add("PV soi max", source.SelfHpMax, other.SelfHpMax);
        Add("PA min", source.MinAp, other.MinAp);
        Add("tour min", source.MinTurn, other.MinTurn);
        Add("ennemis min", source.MinEnemies, other.MinEnemies);
        Add("cibles zone min", source.MinAoeHits, other.MinAoeHits);
        Add("portée min", source.CastRangeMin, other.CastRangeMin);
        Add("portée max", source.CastRangeMax, other.CastRangeMax);
        Add("approche", source.AllowApproach, other.AllowApproach);
        Add("lancers/tour", source.PerTurn, other.PerTurn);
        Add("relance", source.Cooldown, other.Cooldown);
        Add("activé", source.Enabled, other.Enabled);
        Add("priorité", source.Priority, other.Priority);
        Add("sort", source.SpellId, other.SpellId);
        return changes;
    }

    public static string Describe(SpellConfig spell, SpellConfig? comparedTo = null)
    {
        ArgumentNullException.ThrowIfNull(spell);
        var focus = comparedTo is null ? "" : string.Join(", ", ChangedFields(spell, comparedTo));
        string baseText = $"{Target(spell)} · prio {spell.Priority} · x{spell.PerTurn}/tour"
            + (spell.Cooldown > 0 ? $" · CD {spell.Cooldown}" : "")
            + (spell.Enabled ? "" : " · désactivé");

        // Chaque champ signalé dans une différence est aussi lisible par la paire
        // de descriptions actuelle/importée (quel est le chiffre qui va changer ?).
        var details = new List<string>();
        if (comparedTo is not null)
        {
            foreach (var label in ChangedFields(spell, comparedTo))
            {
                details.Add(label switch
                {
                    "cible" => "cible " + spell.TargetKind,
                    "sur soi" => "sur soi=" + Yes(spell.OnMe),
                    "buff sur soi" => "buff=" + Yes(spell.SelfBuff),
                    "invocation" => "invocation=" + Yes(spell.IsSummon),
                    "invocations max" => "invocations max=" + spell.MaxSummons,
                    "PV cible min" => "PV cible min=" + spell.TgtHpMin,
                    "PV cible max" => "PV cible max=" + spell.TgtHpMax,
                    "PV soi min" => "PV soi min=" + spell.SelfHpMin,
                    "PV soi max" => "PV soi max=" + spell.SelfHpMax,
                    "PA min" => "PA min=" + spell.MinAp,
                    "tour min" => "tour min=" + spell.MinTurn,
                    "ennemis min" => "ennemis min=" + spell.MinEnemies,
                    "cibles zone min" => "zone min=" + spell.MinAoeHits,
                    "portée min" => "portée min=" + spell.CastRangeMin,
                    "portée max" => "portée max=" + spell.CastRangeMax,
                    "approche" => "approche=" + Yes(spell.AllowApproach),
                    "lancers/tour" => "x" + spell.PerTurn + "/tour",
                    "relance" => "CD " + spell.Cooldown,
                    "activé" => spell.Enabled ? "activé" : "désactivé",
                    "priorité" => "prio " + spell.Priority,
                    "sort" => "sort " + spell.SpellId,
                    _ => label,
                });
            }
        }
        return details.Count == 0 ? baseText : string.Join(" · ", details) + " | " + baseText;
    }

    private static string Target(SpellConfig spell) => spell.OnMe ? "soi" : spell.IsSummon ? "invocation" :
        spell.TargetKind == SpellTargetKind.Ally ? "allié" : "ennemi";
    private static string Yes(bool value) => value ? "oui" : "non";
}
