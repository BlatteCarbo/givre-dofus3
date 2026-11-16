using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Règle d'un défi de combat. Les ids sont ceux des défis « génériques » (catégorie 1 de la table des défis du jeu,
/// challengesdataroot) ; les variantes de donjon qui portent le MÊME nom (ex. « Hardi » d'un boss) suivent la même règle.</summary>
public enum ChallengeRule
{
    Unknown,
    Zombie, Statue, Premier, Dernier, Econome, Versatile, Nomade, Barbare, Cruel, Intouchable, MainsPropres, Elementaire,
    Ordonne, PetitsDabord, Focus, Elitiste, Survivant, Imprevisible, TueurAGages, Hardi, Collant, Blitzkrieg, Misanthrope,
    Prudent, DeuxPourUn, Partage, Duel, ChacunSonMonstre, Duo, Trio, Quatuor, Chrono, SansCoeur, MemeLignee, Diagonale,
    Miroir, TueurAVue, PasVuPasPris, LigneDeMire, Pins, RaserLesMurs, Conquerant, Doume,
}

/// <summary>Ce que l'IA fait d'un défi : <see cref="Enforced"/> = elle adapte son jeu (déplacement, sorts, cible, case de fin
/// de tour) ; <see cref="Natural"/> = rien de particulier à faire, il passe ou non selon le combat ; <see cref="Unsupported"/>
/// = l'IA ne sait pas le tenir (arme, dégâts indirects, coordination fine…).</summary>
public enum ChallengeSupport { Unsupported, Natural, Enforced }

/// <summary>Contexte du choix : combat en équipe (au moins un allié) et bot de corps-à-corps (sorts offensifs à courte portée).
/// <paramref name="Rules"/> = l'IA respecte les défis actifs (option de l'onglet Combat) ; <paramref name="LevelsKnown"/> =
/// niveaux des ennemis connus (Cruel / Ordonné).</summary>
public readonly record struct ChallengeContext(bool Team, bool Melee, bool Rules, bool LevelsKnown);

/// <summary>Une proposition de défi : id, règle résolue, bonus (%) annoncé par le serveur.</summary>
public readonly record struct ChallengeOffer(int Id, ChallengeRule Rule, int Bonus);

public readonly record struct ChallengeRewardOffer(int Id, ChallengeRule Rule, int XpBonus, int DropBonus);

/// <summary>
/// Catalogue des défis génériques (règle, prise en charge par l'IA, chance de réussite estimée) et CHOIX du défi parmi les
/// propositions du serveur : celui qui rapporte le plus EN MOYENNE (chance de réussite × bonus) parmi ceux acceptés par
/// l'utilisateur. Rater un défi ne coûte rien (pas de bonus, c'est tout) : un défi difficile mais très payé peut l'emporter,
/// à condition que l'utilisateur l'ait accepté.
/// </summary>
public static class ChallengeCatalog
{
    // Ordre d'affichage = ordre des ids du jeu (catégorie 1). Ids et noms vérifiés dans les données 3.6 (challengesdataroot).
    private static readonly (int Id, ChallengeRule Rule)[] Generic =
    {
        (1, ChallengeRule.Zombie), (2, ChallengeRule.Statue), (3, ChallengeRule.Premier), (4, ChallengeRule.Dernier),
        (5, ChallengeRule.Econome), (6, ChallengeRule.Versatile), (8, ChallengeRule.Nomade), (9, ChallengeRule.Barbare),
        (10, ChallengeRule.Cruel), (17, ChallengeRule.Intouchable), (19, ChallengeRule.MainsPropres), (20, ChallengeRule.Elementaire),
        (25, ChallengeRule.Ordonne), (30, ChallengeRule.PetitsDabord), (31, ChallengeRule.Focus), (32, ChallengeRule.Elitiste),
        (33, ChallengeRule.Survivant), (34, ChallengeRule.Imprevisible), (35, ChallengeRule.TueurAGages), (36, ChallengeRule.Hardi),
        (37, ChallengeRule.Collant), (38, ChallengeRule.Blitzkrieg), (39, ChallengeRule.Misanthrope), (40, ChallengeRule.Prudent),
        (42, ChallengeRule.DeuxPourUn), (44, ChallengeRule.Partage), (45, ChallengeRule.Duel), (46, ChallengeRule.ChacunSonMonstre),
        (489, ChallengeRule.Duo), (490, ChallengeRule.Trio), (491, ChallengeRule.Quatuor), (492, ChallengeRule.Chrono),
        (962, ChallengeRule.SansCoeur), (964, ChallengeRule.MemeLignee), (965, ChallengeRule.Diagonale), (966, ChallengeRule.Miroir),
        (967, ChallengeRule.TueurAVue), (968, ChallengeRule.PasVuPasPris), (969, ChallengeRule.LigneDeMire), (970, ChallengeRule.Pins),
        (971, ChallengeRule.RaserLesMurs), (973, ChallengeRule.Conquerant), (974, ChallengeRule.Doume),
    };
    private static readonly Dictionary<int, ChallengeRule> ById = Generic.ToDictionary(g => g.Id, g => g.Rule);

    /// <summary>Ids des défis génériques, dans l'ordre du jeu (liste « Défis acceptés »).</summary>
    public static IReadOnlyList<int> GenericIds { get; } = Generic.Select(g => g.Id).ToArray();

    /// <summary>Règle d'un défi générique (Unknown si l'id n'en est pas un ; les variantes se résolvent par nom, côté moteur).</summary>
    public static ChallengeRule RuleOfGeneric(int id) => ById.TryGetValue(id, out var r) ? r : ChallengeRule.Unknown;

    /// <summary>Id générique d'une règle (clé de la liste « Défis acceptés ») ; 0 pour Unknown.</summary>
    public static int GenericIdOf(ChallengeRule r)
    {
        foreach (var (id, rule) in Generic) if (rule == r) return id;
        return 0;
    }

    public static ChallengeSupport Support(ChallengeRule r) => r switch
    {
        ChallengeRule.Zombie or ChallengeRule.Statue or ChallengeRule.Nomade or ChallengeRule.Versatile or ChallengeRule.Econome
            or ChallengeRule.Elementaire or ChallengeRule.Premier or ChallengeRule.Dernier or ChallengeRule.Elitiste
            or ChallengeRule.Imprevisible or ChallengeRule.TueurAGages or ChallengeRule.Focus or ChallengeRule.TueurAVue
            or ChallengeRule.Cruel or ChallengeRule.Ordonne or ChallengeRule.Duel or ChallengeRule.Hardi or ChallengeRule.Prudent
            or ChallengeRule.Collant or ChallengeRule.Misanthrope or ChallengeRule.MemeLignee or ChallengeRule.Diagonale
            or ChallengeRule.LigneDeMire or ChallengeRule.PasVuPasPris or ChallengeRule.RaserLesMurs or ChallengeRule.Conquerant
            => ChallengeSupport.Enforced,
        ChallengeRule.Survivant or ChallengeRule.SansCoeur or ChallengeRule.Intouchable or ChallengeRule.Chrono or ChallengeRule.Duo
            or ChallengeRule.Trio or ChallengeRule.Quatuor
            => ChallengeSupport.Natural,
        _ => ChallengeSupport.Unsupported,
    };

    /// <summary>Tous les défis sont acceptés par défaut ; seules les exclusions explicites du profil les refusent.</summary>
    public static bool DefaultAccepted(ChallengeRule r) => true;

    /// <summary>Défi accepté compte tenu des surcharges de l'utilisateur (ids GÉNÉRIQUES cochés <paramref name="on"/> / décochés
    /// <paramref name="off"/> par rapport aux valeurs conseillées). Défi inconnu (propre à un donjon) = accepté : le rater ne
    /// coûte rien.</summary>
    public static bool Accepted(ChallengeRule r, ICollection<int> on, ICollection<int> off)
    {
        if (r == ChallengeRule.Unknown) return true;
        int gid = GenericIdOf(r);
        if (off.Contains(gid)) return false;
        if (on.Contains(gid)) return true;
        return DefaultAccepted(r);
    }

    /// <summary>Chance de réussite estimée (0-100) dans ce contexte. Valeurs prudentes, issues du comportement de l'IA :
    /// une règle tenue par l'IA perd beaucoup si l'option « adapter l'IA » est coupée.</summary>
    public static int Likelihood(ChallengeRule r, ChallengeContext ctx)
    {
        int p = r switch
        {
            ChallengeRule.Versatile => 90,
            ChallengeRule.Focus => 85,
            ChallengeRule.Premier or ChallengeRule.Elitiste or ChallengeRule.TueurAGages or ChallengeRule.TueurAVue => 80,
            ChallengeRule.Imprevisible => 75,
            ChallengeRule.Dernier => 70,
            ChallengeRule.Cruel or ChallengeRule.Ordonne => ctx.LevelsKnown ? 70 : 35,
            ChallengeRule.Zombie => ctx.Melee ? 45 : 50,   // mesuré ~9 % de réussite, et le PM imposé gâche le 1er tour
            ChallengeRule.Statue => ctx.Melee ? 55 : 80,
            ChallengeRule.Nomade => ctx.Melee ? 30 : 35,   // idem : tous les PM brûlés loin de l'ennemi -> tour passé sans attaque
            ChallengeRule.Econome => 45,
            ChallengeRule.Elementaire => 80,
            ChallengeRule.Hardi => ctx.Melee ? 85 : 60,
            ChallengeRule.Prudent => ctx.Melee ? 45 : 75,
            ChallengeRule.LigneDeMire => 80,
            ChallengeRule.PasVuPasPris => 40,
            ChallengeRule.RaserLesMurs => 65,
            ChallengeRule.Conquerant => 50,
            ChallengeRule.Collant => ctx.Team ? 60 : 0,
            ChallengeRule.Misanthrope => ctx.Team ? 75 : 95,
            ChallengeRule.MemeLignee => ctx.Team ? 55 : 0,
            ChallengeRule.Diagonale => ctx.Team ? 50 : 0,
            ChallengeRule.Duel => ctx.Team ? 50 : 90,
            ChallengeRule.Survivant => ctx.Team ? 80 : 90,
            ChallengeRule.SansCoeur => ctx.Team ? 70 : 95,
            ChallengeRule.Chrono or ChallengeRule.Duo or ChallengeRule.Trio or ChallengeRule.Quatuor => 50,
            ChallengeRule.Intouchable => 25,
            ChallengeRule.PetitsDabord or ChallengeRule.Partage or ChallengeRule.ChacunSonMonstre => ctx.Team ? 30 : 90,
            ChallengeRule.Blitzkrieg => 20,
            ChallengeRule.DeuxPourUn or ChallengeRule.Doume => 15,
            ChallengeRule.Pins => 10,
            ChallengeRule.Barbare or ChallengeRule.MainsPropres or ChallengeRule.Miroir => 5,
            _ => 30,   // défi inconnu (donjon spécifique) : on ne sait rien
        };
        if (!ctx.Rules && Support(r) == ChallengeSupport.Enforced) p /= 2;   // l'IA joue comme d'habitude : tenu par hasard
        return Math.Clamp(p, 0, 100);
    }

    /// <summary>Select expected preferred reward, then secondary bonus and lowest id. Missing metadata keeps server order.</summary>
    /// <summary>Poids (en combats) de l'estimation a priori face aux résultats observés : 6 combats observés pèsent autant
    /// que l'a priori. Statue estimé 80 % mais raté 13 fois sur 14 la nuit du 24/10 -> 32 %, plus choisi face à 65 %.</summary>
    public const int PriorWeight = 6;

    /// <summary>Chance de réussite corrigée par l'expérience : moyenne bayésienne de l'a priori et des issues observées.</summary>
    public static int Blend(int prior, int successes, int failures)
    {
        successes = Math.Max(0, successes); failures = Math.Max(0, failures);
        int n = successes + failures;
        return n == 0 ? prior : (int)Math.Round((prior * (double)PriorWeight + 100.0 * successes) / (PriorWeight + n));
    }

    public static int? PickPreferred(IReadOnlyList<ChallengeRewardOffer> offers,
        Func<ChallengeRewardOffer, bool> accepted, ChallengeContext ctx, bool loot, Func<ChallengeRule, int, int>? adjust = null)
    {
        int P(ChallengeRule r) { int prior = Likelihood(r, ctx); return adjust is null ? prior : Math.Clamp(adjust(r, prior), 0, 100); }
        var candidates = offers.Where(accepted).ToArray();
        if (candidates.Length == 0) return null;
        // No reward metadata: preserve server order instead of inventing a reward.
        if (candidates.All(o => o.XpBonus <= 0 && o.DropBonus <= 0)) return candidates[0].Id;
        int Primary(ChallengeRewardOffer o) => Math.Max(0, loot ? o.DropBonus : o.XpBonus);
        int Secondary(ChallengeRewardOffer o) => Math.Max(0, loot ? o.XpBonus : o.DropBonus);
        return candidates.OrderByDescending(o => (long)P(o.Rule) * Primary(o))
            .ThenByDescending(Secondary)
            .ThenBy(o => o.Id)
            .First().Id;
    }
    /// <summary>Défi à valider : parmi les propositions ACCEPTÉES, celle de plus grand gain moyen (chance × bonus ; bonus
    /// inconnu = 1 point) ; à égalité la plus sûre, puis l'ordre du serveur. null = aucune proposition acceptée.</summary>
    public static int? Pick(IReadOnlyList<ChallengeOffer> offers, Func<ChallengeOffer, bool> accepted, ChallengeContext ctx)
    {
        ChallengeOffer? best = null; long bestScore = -1; int bestP = -1;
        foreach (var o in offers)
        {
            if (!accepted(o)) continue;
            int p = Likelihood(o.Rule, ctx);
            long score = (long)p * Math.Max(1, o.Bonus);
            if (score > bestScore || (score == bestScore && p > bestP)) { best = o; bestScore = score; bestP = p; }
        }
        return best?.Id;
    }
}
