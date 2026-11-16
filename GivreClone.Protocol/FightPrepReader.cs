using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Décode les événements de PRÉPARATION de combat (placement + challenge), pour l'autonomie pré-combat.
/// Numéros de champ = patch courant (capture) ; noms clairs = protos Ankama (fight_preparation/common). Voir memory
/// protocol-wire-map. Les cellules sont des cellId bruts (0-559) en repeated int32 PACKED ; le challenge_id à renvoyer
/// dans la validation (kwv) = le champ #2 de chaque Challenge proposé.
/// </summary>
public static class FightPrepReader
{
    private const int CellCount = 560;

    // Branchement catalogue (numéros par nom, littéral CORRIGÉ en repli).
    private static System.Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary>FightPlacementPossiblePositionsEvent : starting_positions contient les deux listes nommées du catalogue.
    /// En PvE je suis TOUJOURS challenger -> mes cases de placement = la 1re liste (Challengers). null si illisible.</summary>
    public static (IReadOnlyList<int> Challengers, IReadOnlyList<int> Defenders)? ReadPlacementCells(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("FightPlacementPossiblePositionsEvent", "starting_positions") && f.Message is not null)?.Message;
        if (container is null) return null;
        // Respecter les noms : inverser les listes propose les cases de l'équipe ennemie au pré-combat.
        return (DecodeCells(container, F("FightStartingPositions", "challengers_positions")),
                DecodeCells(container, F("FightStartingPositions", "defenders_positions")));
    }

    private static IReadOnlyList<int> DecodeCells(IReadOnlyList<ProtoField> container, int field)
    {
        var f = container.FirstOrDefault(x => x.Field == field && x.Wire == WireType.Len && x.Bytes is not null);
        var list = new List<int>();
        if (f?.Bytes is { Length: > 0 } b)
            foreach (var v in Varints(b)) { int c = (int)(v & 0xFFF); if (c >= 0 && c < CellCount && !list.Contains(c)) list.Add(c); }
        return list;
    }

    /// <summary>ChallengeProposalEvent : `challenge_proposals` (repeated Challenge) au champ #1, `timer` au #2. Chaque
    /// Challenge porte son `challenge_id` au champ #5 (à RENVOYER dans la validation). ⚠ CORRECTIF : l'ancien code lisait
    /// la liste au #2 (= timer, scalaire) et l'id au #2 (= targets) -> renvoyait TOUJOURS vide (défis jamais vus). Les
    /// deux numéros viennent du mapping (challenge_proposals=1, Challenge.challenge_id=5, présents dans fields.json).</summary>
    public static IReadOnlyList<int> ReadChallengeProposalIds(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<int>();
        int fProposals = F("ChallengeProposalEvent", "challenge_proposals");
        int fId = F("Challenge", "challenge_id");
        foreach (var f in tree)
        {
            if (f.Field != fProposals || f.Message is null) continue;   // repeated challenge_proposals = Challenge proposé
            var idF = f.Message.FirstOrDefault(x => x.Field == fId && x.Wire == WireType.Varint);   // challenge_id
            if (idF is not null) list.Add((int)idF.Varint);
        }
        return list;
    }

    /// <summary>Un défi proposé (ChallengeProposalEvent.challenge_proposals) : id + bonus XP/butin (%), état, cibles.
    /// proto3 : bonus/état omis = 0 ; target_cell omis = 0 (ambigu avec la cellule 0).</summary>
    public sealed record ChallengeProposal(int Id, int XpBonus, int DropBonus, int State, IReadOnlyList<(long Id, int Cell)> Targets);

    /// <summary>ChallengeProposalEvent décodé en entier (Lot C2 : combat:challengeProposals). Entrée sans challenge_id ignorée
    /// (même règle que <see cref="ReadChallengeProposalIds"/>). Numéros de champ résolus par le mapping.</summary>
    public static IReadOnlyList<ChallengeProposal> ReadChallengeProposals(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<ChallengeProposal>();
        int fProposals = F("ChallengeProposalEvent", "challenge_proposals");
        int fId = F("Challenge", "challenge_id"), fDrop = F("Challenge", "drop_bonus"), fXp = F("Challenge", "xp_bonus");
        int fState = F("Challenge", "state"), fTargets = F("Challenge", "targets");
        int fTgtId = F("ChallengeTarget", "target_id"), fTgtCell = F("ChallengeTarget", "target_cell");
        foreach (var f in tree)
        {
            if (f.Field != fProposals || f.Message is not { } ch) continue;
            var idF = ch.FirstOrDefault(x => x.Field == fId && x.Wire == WireType.Varint);
            if (idF is null) continue;
            var targets = new List<(long, int)>();
            foreach (var t in ch)
                if (t.Field == fTargets && t.Message is { } tm)
                    targets.Add((VarintOf(tm, fTgtId), (int)VarintOf(tm, fTgtCell)));
            list.Add(new ChallengeProposal((int)idF.Varint, (int)VarintOf(ch, fXp), (int)VarintOf(ch, fDrop), (int)VarintOf(ch, fState), targets));
        }
        return list;
    }

    /// <summary>ChallengeResultEvent : (challenge_id, success). null si pas d'id. proto3 : success omis = false (échoué).</summary>
    public static (int Id, bool Success)? ReadChallengeResult(IReadOnlyList<ProtoField> tree)
    {
        int id = (int)VarintOf(tree, F("ChallengeResultEvent", "challenge_id"));
        if (id == 0) return null;
        return (id, VarintOf(tree, F("ChallengeResultEvent", "success")) != 0);
    }

    // ===================== Lot K : défis ACTIFS du combat (combat:challenges / fightChallenge:*) =====================

    /// <summary>Cible d'un défi actif : id (int64 SIGNÉ, monstres négatifs) + cellule + alliés qui l'ont déjà attaquée
    /// (Duel / Chacun son monstre ; vide si le serveur n'en dit rien).</summary>
    public sealed record ChallengeTargetInfo(long TargetId, int TargetCell, IReadOnlyList<long>? Attackers = null);

    /// <summary>Défi ACTIF (validé) du combat : id, état brut (enum sans noms connus ; 0 = omis), bonus XP/butin (%), cibles.</summary>
    public sealed record FightChallenge(int Id, int State, int XpBonus, int DropBonus, IReadOnlyList<ChallengeTargetInfo> Targets);

    /// <summary>Décode un sous-message Challenge. null si challenge_id absent (proto3 : id 0 = omis -> non publié).
    /// Tous les numéros (Challenge.* / ChallengeTarget.*) résolus PAR NOM (renumérotation imbriquée possible au patch).</summary>
    public static FightChallenge? ReadChallenge(IReadOnlyList<ProtoField>? m)
    {
        if (m is null) return null;
        int fId = F("Challenge", "challenge_id"), fSt = F("Challenge", "state"), fXp = F("Challenge", "xp_bonus"),
            fDrop = F("Challenge", "drop_bonus"), fTg = F("Challenge", "targets"),
            fTid = F("ChallengeTarget", "target_id"), fTc = F("ChallengeTarget", "target_cell"), fTa = F("ChallengeTarget", "attackers_id");
        var id = m.FirstOrDefault(x => x.Field == fId && x.Wire == WireType.Varint);
        if (id is null || id.Varint == 0) return null;
        var targets = new List<ChallengeTargetInfo>();
        foreach (var t in m)
            if (t.Field == fTg && t.Message is { } tm)
                targets.Add(new ChallengeTargetInfo(VarintOf(tm, fTid), (int)VarintOf(tm, fTc), Int64s(tm, fTa)));
        return new FightChallenge((int)id.Varint, (int)VarintOf(m, fSt), (int)VarintOf(m, fXp), (int)VarintOf(m, fDrop), targets);
    }

    /// <summary>ChallengeAddEvent / ChallengeTargetsEvent : sous-message `challenge` (repli #2 pour les deux). Le champ #1
    /// obfusqué de ces événements est ignoré.</summary>
    public static FightChallenge? ReadChallengeCarrier(string msgName, IReadOnlyList<ProtoField> tree)
    {
        int f = F(msgName, "challenge");
        return ReadChallenge(tree.FirstOrDefault(x => x.Field == f && x.Message is not null)?.Message);
    }

    /// <summary>ChallengeListEvent : `challenges` (repeated Challenge). Entrées sans id ignorées.</summary>
    public static IReadOnlyList<FightChallenge> ReadChallengeList(IReadOnlyList<ProtoField> tree)
    {
        int f = F("ChallengeListEvent", "challenges");
        var list = new List<FightChallenge>();
        foreach (var x in tree)
            if (x.Field == f && ReadChallenge(x.Message) is { } c) list.Add(c);
        return list;
    }

    // repeated int64 : encodage PACKED (un champ Len) ou une entrée Varint par valeur, les deux sont valides en proto3.
    private static IReadOnlyList<long> Int64s(IReadOnlyList<ProtoField> msg, int field)
    {
        var list = new List<long>();
        foreach (var x in msg)
        {
            if (x.Field != field) continue;
            if (x.Wire == WireType.Varint) list.Add(unchecked((long)x.Varint));
            else if (x.Wire == WireType.Len && x.Bytes is { Length: > 0 } b) foreach (var v in Varints(b)) list.Add(unchecked((long)v));
        }
        return list;
    }

    // 1er champ VARINT de ce numéro (int64 signé en complément à deux : un id de monstre négatif reste négatif). 0 si absent.
    private static long VarintOf(IReadOnlyList<ProtoField> msg, int field)
    {
        foreach (var x in msg) if (x.Field == field && x.Wire == WireType.Varint) return unchecked((long)x.Varint);
        return 0;
    }

    private static IEnumerable<ulong> Varints(byte[] b)
    {
        int i = 0;
        while (i < b.Length)
        {
            ulong v = 0; int shift = 0;
            while (i < b.Length)
            {
                byte x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            yield return v;
        }
    }
}
