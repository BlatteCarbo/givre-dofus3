using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

/// <summary>Pierre d'âme : identifiant générique de l'objet et puissance (niveau maximal du monstre le plus fort du groupe qu'elle peut contenir).</summary>
public sealed record SoulStone(int GenericId, int Power, string Name);

/// <summary>Une pile de pierres dans le sac (uid = identifiant d'objet à équiper).</summary>
public sealed record SoulStoneStack(int GenericId, int Uid, int Quantity);

public enum SoulAction { None, Equip, Wait, Cast }

public sealed record SoulAdvice(SoulAction Action, string Reason, int StoneGid = 0, int StoneUid = 0);

/// <summary>État du combat vu par la décision de capture. Les valeurs inconnues sont à 0 : la décision est alors PRUDENTE (jamais de lancer sur une inconnue).</summary>
public sealed record SoulSituation(
    int HighestLevel,                         // niveau du monstre le plus fort du groupe ennemi (0 = inconnu)
    bool HasCapturable,                       // le groupe contient un archimonstre / boss capturable
    int EquippedStoneGid,                     // pierre actuellement équipée (0 = aucune)
    IReadOnlyList<SoulStoneStack> Bag,        // pierres du sac
    bool SpellReady,                          // « Capture d'âmes » lançable (relance écoulée, PA suffisants)
    long EnemyHp,                             // PV restants de TOUS les ennemis vivants (0 = inconnu / combat fini)
    double DamagePerTurn,                     // dégâts par tour de l'équipe, mesurés pendant ce combat (0 = pas encore mesurés)
    int ApPerTurn,                            // PA disponibles par tour pour le lanceur
    int SpellAp,                              // coût en PA du sort de capture
    int WindowTurns = SoulCapture.StateTurns, // tours pendant lesquels l'état « capture » dure, le tour du lancer compris
    double Margin = 1.25);                    // marge de sécurité sur les dégâts attendus

/// <summary>Capture d'âmes (archimonstres). Règles : une pierre d'âme doit être ÉQUIPÉE (sinon le sort est grisé) et assez puissante pour le monstre le plus fort du groupe ;
/// le sort a une relance de 3 tours ; l'état dure 2 tours et le combat doit être terminé dans ce délai. On ne lance donc que si les dégâts attendus pendant l'état couvrent les PV
/// restants avec une marge. Pur : testable sans jeu. Puissances relevées dans les données du client (type 83, action 705) ; durée / relance : sources publiques, à confirmer en jeu.</summary>
public static class SoulCapture
{
    public const int SpellId = 413;       // « Capture d'âmes »
    public const int StateTurns = 2;      // durée de l'état « chercheur d'âme »
    public const int CooldownTurns = 3;   // relance du sort

    public static readonly IReadOnlyList<SoulStone> Stones = new SoulStone[]
    {
        new(9686, 50, "Petite pierre d'âme"),
        new(9687, 100, "Moyenne pierre d'âme"),
        new(9688, 150, "Grande pierre d'âme"),
        new(9689, 190, "Énorme pierre d'âme"),
        new(9690, 1000, "Gigantesque pierre d'âme"),
    };

    public static SoulStone? StoneById(int genericId) => Stones.FirstOrDefault(s => s.GenericId == genericId);

    /// <summary>La plus petite pierre qui peut contenir un groupe dont le monstre le plus fort a ce niveau ; null si le niveau est inconnu ou hors de portée.</summary>
    public static SoulStone? RequiredStone(int highestLevel) => highestLevel <= 0 ? null : Stones.FirstOrDefault(s => highestLevel <= s.Power);

    /// <summary>Le sort est-il relancé ? (dernier lancer au tour <paramref name="lastCastTurn"/> ; 0 = jamais lancé.)</summary>
    public static bool CooldownElapsed(int currentTurn, int lastCastTurn) => lastCastTurn <= 0 || currentTurn - lastCastTurn >= CooldownTurns;

    public static SoulAdvice Advise(SoulSituation s)
    {
        if (!s.HasCapturable) return new(SoulAction.None, "aucun archimonstre capturable dans ce groupe");
        if (s.HighestLevel <= 0) return new(SoulAction.Wait, "niveau des ennemis inconnu");
        var required = RequiredStone(s.HighestLevel);
        if (required is null) return new(SoulAction.None, $"niveau {s.HighestLevel} : aucune pierre d'âme assez puissante");

        // 1) pierre : équipée et suffisante ? sinon la plus petite pierre suffisante du sac
        var equipped = StoneById(s.EquippedStoneGid);
        if (equipped is null || equipped.Power < required.Power)
        {
            var pick = s.Bag.Where(b => b.Quantity > 0 && StoneById(b.GenericId) is { } st && st.Power >= required.Power)
                            .OrderBy(b => StoneById(b.GenericId)!.Power).ThenBy(b => b.Uid).FirstOrDefault();
            return pick is null
                ? new(SoulAction.None, $"il faut une {required.Name} (ou plus) : aucune dans le sac")
                : new(SoulAction.Equip, $"équiper une {StoneById(pick.GenericId)!.Name} (niveau {s.HighestLevel})", pick.GenericId, pick.Uid);
        }

        // 2) le sort doit être lançable
        if (!s.SpellReady) return new(SoulAction.Wait, "sort de capture en relance ou PA insuffisants", equipped.GenericId);

        // 3) on ne lance que si on est sûr de finir le combat pendant la durée de l'état
        if (s.EnemyHp <= 0) return new(SoulAction.None, "plus d'ennemi à capturer", equipped.GenericId);
        if (s.DamagePerTurn <= 0) return new(SoulAction.Wait, "dégâts par tour pas encore mesurés : un tour d'observation", equipped.GenericId);
        int window = Math.Max(1, s.WindowTurns);
        // le tour du lancer ne compte que pour la part de PA qui reste après le sort
        double castTurnShare = s.ApPerTurn > 0 ? Math.Clamp((s.ApPerTurn - s.SpellAp) / (double)s.ApPerTurn, 0, 1) : 0.5;
        double expected = s.DamagePerTurn * (castTurnShare + (window - 1));
        double needed = s.EnemyHp * Math.Max(1.0, s.Margin);
        return expected >= needed
            ? new(SoulAction.Cast, $"fin de combat attendue dans l'état : {expected:N0} dégâts prévus pour {s.EnemyHp:N0} PV", equipped.GenericId)
            : new(SoulAction.Wait, $"trop tôt : {expected:N0} dégâts prévus pour {s.EnemyHp:N0} PV (×{s.Margin:0.##} de marge)", equipped.GenericId);
    }

    /// <summary>Dégâts par tour de l'équipe, mesurés sur les PV totaux des ennemis relevés au début de chacun de VOS tours : on retient le plus PRUDENT des deux derniers écarts.</summary>
    public static double MeasureDamagePerTurn(IReadOnlyList<long> enemyHpAtTurnStart)
    {
        if (enemyHpAtTurnStart.Count < 2) return 0;
        var drops = new List<long>();
        for (int i = Math.Max(1, enemyHpAtTurnStart.Count - 2); i < enemyHpAtTurnStart.Count; i++) drops.Add(Math.Max(0, enemyHpAtTurnStart[i - 1] - enemyHpAtTurnStart[i]));
        return drops.Min();
    }
}
