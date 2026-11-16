using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

/// <summary>Un sort vu par le planificateur : coût en PA, dégâts moyens sur la cible, nombre maximal de lancers ce tour (0 = sans limite).</summary>
public sealed record BurstSpell(string Name, int Ap, int AvgDamage, int MaxCasts);

/// <summary>Combinaison retenue : nombre de lancers par sort, dégâts totaux et PA dépensés.</summary>
public sealed record BurstPlan(IReadOnlyList<(string Name, int Casts)> Casts, int Damage, int ApUsed)
{
    public string Describe() => Casts.Count == 0 ? "aucun lancer"
        : string.Join(" + ", Casts.Select(c => c.Casts > 1 ? $"{c.Casts}× {c.Name}" : c.Name)) + $" = {Damage} dégâts pour {ApUsed} PA";
}

/// <summary>Meilleure rafale THÉORIQUE sur une seule cible : sac à dos borné sur les PA (géométrie, ligne de vue, résistances et
/// états ignorés). Sert à comparer avec la rotation par priorité, pas à jouer.</summary>
public static class BurstPlanner
{
    public static BurstPlan Best(IReadOnlyList<BurstSpell> spells, int ap)
    {
        ap = Math.Max(0, Math.Min(ap, 100));
        // best[a] = (dégâts, lancers par sort) avec au plus a PA ; chaque lancer est un objet du sac (borné par MaxCasts).
        var dmg = new int[ap + 1];
        var pick = new int[ap + 1][];
        for (int a = 0; a <= ap; a++) pick[a] = new int[spells.Count];
        for (int s = 0; s < spells.Count; s++)
        {
            var sp = spells[s];
            if (sp.Ap <= 0 || sp.AvgDamage <= 0) continue;
            int copies = sp.MaxCasts > 0 ? sp.MaxCasts : ap / sp.Ap;
            for (int c = 0; c < copies; c++)
                for (int a = ap; a >= sp.Ap; a--)
                {
                    int cand = dmg[a - sp.Ap] + sp.AvgDamage;
                    if (cand > dmg[a] && pick[a - sp.Ap][s] < copies)
                    {
                        dmg[a] = cand;
                        pick[a] = (int[])pick[a - sp.Ap].Clone();
                        pick[a][s]++;
                    }
                }
        }
        var best = pick[ap];
        var casts = spells.Select((sp, i) => (sp.Name, best[i])).Where(x => x.Item2 > 0).ToList();
        return new BurstPlan(casts, dmg[ap], spells.Select((sp, i) => sp.Ap * best[i]).Sum());
    }
}
