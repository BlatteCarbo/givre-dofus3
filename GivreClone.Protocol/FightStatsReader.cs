using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Stats d'un combattant décodées des paquets de combat. Vie/Pa/Pm = valeurs COURANTES (Vie suivie via les
/// pertes de PV #40 ; Pa/Pm via les MAJ de caractéristique jxw) ; MaxLife = lifePoints(base)+vitalité ;
/// MaxPa/MaxPm = PA/PM max (base, régénérés chaque tour → budget du moteur).</summary>
public readonly record struct FighterStats(long FighterId, int Pa, int Pm, int Life, int MaxLife, int MaxPa, int MaxPm)
{
    public ImmutableDictionary<int, int> Characteristics { get; init; } = ImmutableDictionary<int, int>.Empty;
}

/// <summary>Lot F : vitalité HORS combat lue de la fiche perso (CharacterCharacteristicsEvent). MaxLife = base(0)+vitalité(11) ;
/// LifeDelta = caractéristique 97 (PV courants = MaxLife + delta ; null si absente) ; énergie 29/47 ; paliers d'XP #4/#8.</summary>
public readonly record struct SelfVitals(int MaxLife, int? LifeDelta, int? Energy, int? MaxEnergy, long? XpLevelFloor, long? XpNextLevelFloor);

/// <summary>
/// Décode PA/PM/PV/résist des paquets de combat Dofus 3. Mapping DÉRIVÉ EMPIRIQUEMENT d'une capture réelle
/// (voir memory combat-module) et corroboré par un bot tiers: les IDs de caractéristique sont des VALEURS de
/// données (pas des numéros de champ wire) donc stables au patch. Chaque caractéristique = une entrée {charId, valeur}
/// (charId au champ #1, OMIS ⇒ 0/lifePoints ; valeur = somme des varints du sous-message de valeur).
/// - GameFightShowFighter (jxg) : stats complètes d'un combattant (conteneur = body.#2.#2.#2, entrées répétées #5).
/// - CharacterCharacteristicsEvent (kub, hors combat) : mes stats de base (conteneur = #2, entrées répétées #11).
/// - GameActionFightEvent (jwe) : perte de PV au sous-message #40 { #2 cible, #3 montant } ; variation PA/PM au
///   sous-message #20 { #1 delta signé, #2 cible } quand action_id (#14) ∈ {102 = PA, 129 = PM}.
/// </summary>
public static class FightStatsReader
{
    // IDs de caractéristique Dofus. IdRange = Portée (#19, cf CharIdHints).
    public const int IdLife = 0, IdPa = 1, IdVitality = 11, IdPm = 23, IdRange = 19, IdShield = 96;
    // Lot F (survie) : PV courants = base(0) + vitalité(11) + delta(97, négatif une fois blessé) ; énergie 29 / max 47
    //.
    public const int IdCurLifeDelta = 97, IdEnergy = 29, IdMaxEnergy = 47;

    // Branchement catalogue : TOUS les numéros de champ viennent du fields.json streamé (v5), par les noms qui y figurent.
    // Sous-messages de GameActionFightEvent = leur nom court v5 (Slide, Death, LifePointsLost, PointsVariation…), jamais
    // un ancien nom Dofus 2 ni un pin de confirmed.json (non appliqués en prod : régression combat du 04/10).
    // NB : les IdPa/IdPm/IdLife/... ci-dessus sont des VALEURS de donnée (charId), PAS des numéros de champ -> jamais
    // catalogués. La forme du sous-message de valeur (base+equip+ctxModif) reste de la logique métier, pas un numéro.
    private static System.Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    // Le oneof détermine la forme : les numéros se recouvrent entre detailed et usable.
    // Somme des composantes nommées seulement ; used est un compteur, pas un bonus.
    private static long CharacteristicValue(IReadOnlyList<ProtoField> entry)
    {
        static long Sum(IReadOnlyList<ProtoField>? fields, params int[] numbers)
            => fields is null ? 0 : numbers.Sum(n => fields.FirstOrDefault(x => x.Field == n && x.Wire == WireType.Varint)?.AsSignedVarint ?? 0);
        var usable = entry.FirstOrDefault(x => x.Field == F("CharacterCharacteristic", "usable") && x.Wire == WireType.Len);
        if (usable is not null) return Sum(usable.Message,
            F("CharacterCharacteristicDetailedUsable", "base"), F("CharacterCharacteristicDetailedUsable", "objects_and_mount_bonus"),
            F("CharacterCharacteristicDetailedUsable", "additional"), F("CharacterCharacteristicDetailedUsable", "alignment_gift_bonus"),
            F("CharacterCharacteristicDetailedUsable", "context_modification"), F("CharacterCharacteristicDetailedUsable", "temporary"));
        var detailed = entry.FirstOrDefault(x => x.Field == F("CharacterCharacteristic", "detailed") && x.Wire == WireType.Len);
        if (detailed is not null) return Sum(detailed.Message,
            F("CharacterCharacteristicDetailed", "base"), F("CharacterCharacteristicDetailed", "objects_and_mount_bonus"),
            F("CharacterCharacteristicDetailed", "additional"), F("CharacterCharacteristicDetailed", "alignment_gift_bonus"),
            F("CharacterCharacteristicDetailed", "context_modification"), F("CharacterCharacteristicDetailed", "temporary"));
        var value = entry.FirstOrDefault(x => x.Field == F("CharacterCharacteristic", "value") && x.Wire == WireType.Len);
        return Sum(value?.Message, F("CharacterCharacteristicValue", "total"));
    }

    // charId -> valeur, à partir d'un conteneur dont chaque entrée est au champ <entryField>.
    // 3.6.11.12 : l'id de caractéristique est passé de #1 à #4 (entrée = { valeur #1|#2|#3, charId #4 }) ; l'id 0
    // (= PV de base) reste OMIS du wire, piège proto3 habituel.
    private static Dictionary<int, int> ParseEntries(IReadOnlyList<ProtoField> container, int entryField)
    {
        var map = new Dictionary<int, int>();
        int fId = F("CharacterCharacteristic", "characteristic_id");
        foreach (var e in container)
        {
            if (e.Field != entryField || e.Message is null) continue;
            int id = (int)(e.Message.FirstOrDefault(x => x.Field == fId && x.Wire == WireType.Varint)?.Varint ?? 0);
            map[id] = (int)CharacteristicValue(e.Message);
        }
        return map;
    }

    /// <summary>GameFightShowFighter (jxg) : stats d'UN combattant. null si structure non reconnue.</summary>
    public static FighterStats? ReadShowFighter(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : racine #1 = { information #1, fighterId #2, disposition #3 } et les caractéristiques sont en
        // information.#3.#3, entrées répétées #3. Avant : racine #2, id #3, conteneur #2.#2.#2, entrées #5.
        var body = tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message;
        var idF = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_id") && f.Wire == WireType.Varint);
        var container = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_information") && f.Message is not null)?.Message
                            ?.FirstOrDefault(f => f.Field == F("ActorInformation", "fighter") && f.Message is not null)?.Message
                            ?.FirstOrDefault(f => f.Field == F("FightFighterInformation", "stats") && f.Message is not null)?.Message;
        int entryField = F("FightCharacteristics", "characteristics");
        if (container is null || idF is null) return null;
        long id = idF.AsSignedVarint;   // joueur = positif, monstres = -1/-2/-3
        var m = ParseEntries(container, entryField);
        if (m.Count == 0) return null;
        int pa = m.GetValueOrDefault(IdPa), pm = m.GetValueOrDefault(IdPm);
        int maxLife = m.GetValueOrDefault(IdLife) + m.GetValueOrDefault(IdVitality);
        int curLife = maxLife > 0 ? System.Math.Clamp(maxLife + m.GetValueOrDefault(IdCurLifeDelta), 1, maxLife) : 0;
        return new FighterStats(id, pa, pm, curLife, maxLife, pa, pm)
        {
            Characteristics = m.ToImmutableDictionary()
        };   // PV courants = max + delta(97) si blessé à l'entrée en combat
    }

    /// <summary>Niveau d'un MONSTRE depuis GameFightShowFighter : (id de combat, niveau), ou null si ce n'est pas un monstre
    /// (joueur, invocation de joueur…) ou niveau omis. Chemin PAR NOM : information → actor_information → fighter → ai_fighter
    /// → monster_fighter_information → creature_level. Sert aux défis Cruel / Ordonné (achever par niveau croissant/décroissant).</summary>
    public static (long FighterId, int Level)? ReadShowFighterLevel(IReadOnlyList<ProtoField> tree)
    {
        var body = tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message;
        var idF = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_id") && f.Wire == WireType.Varint);
        var monster = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_information") && f.Message is not null)?.Message
                          ?.FirstOrDefault(f => f.Field == F("ActorInformation", "fighter") && f.Message is not null)?.Message
                          ?.FirstOrDefault(f => f.Field == F("FightFighterInformation", "ai_fighter") && f.Message is not null)?.Message
                          ?.FirstOrDefault(f => f.Field == F("AIFighterInformation", "monster_fighter_information") && f.Message is not null)?.Message;
        var lvl = monster?.FirstOrDefault(f => f.Field == F("MonsterFighter", "creature_level") && f.Wire == WireType.Varint);
        if (idF is null || lvl is null || lvl.Varint == 0) return null;
        return (idF.AsSignedVarint, (int)lvl.Varint);
    }

    /// <summary>Nom de JOUEUR d'un combattant depuis GameFightShowFighter, ou null si ce n'est PAS un joueur (monstre/
    /// invocation : pas de sous-message `named_fighter`). Chemin résolu PAR NOM : information(1) → actor_information(1) →
    /// fighter(3) → named_fighter(8) → name(5). Sert à n'expulser QUE de vrais joueurs intrus (jamais nos invocations).</summary>
    public static string? ReadShowFighterName(IReadOnlyList<ProtoField> tree)
    {
        var body = tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message;
        var fighter = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_information") && f.Message is not null)?.Message
                          ?.FirstOrDefault(f => f.Field == F("ActorInformation", "fighter") && f.Message is not null)?.Message;
        var named = fighter?.FirstOrDefault(f => f.Field == F("FightFighterInformation", "named_fighter") && f.Message is not null)?.Message;
        var s = named?.FirstOrDefault(f => f.Field == F("NamedFighterInformation", "name") && f.Wire == WireType.Len)?.String;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static (long FighterId, int Breed)? ReadShowFighterBreed(IReadOnlyList<ProtoField> tree)
    {
        var body = tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message;
        var id = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_id") && f.Wire == WireType.Varint);
        var character = body?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_information") && f.Message is not null)?.Message
            ?.FirstOrDefault(f => f.Field == F("ActorInformation", "fighter") && f.Message is not null)?.Message
            ?.FirstOrDefault(f => f.Field == F("FightFighterInformation", "named_fighter") && f.Message is not null)?.Message
            ?.FirstOrDefault(f => f.Field == F("NamedFighterInformation", "character_information") && f.Message is not null)?.Message;
        var breed = character?.FirstOrDefault(f => f.Field == F("FightCharacterInformation", "breed_id") && f.Wire == WireType.Varint);
        return id is not null && breed is not null ? (id.AsSignedVarint, unchecked((int)breed.Varint)) : null;
    }

    /// <summary>SOURCE (lanceur) d'une action de combat GameActionFightEvent (jwe) = `source_id`, champ #11 en v4
    ///. L'ancien littéral #3 était FAUX pour le protocole actuel: #3 = `reduce_damages` (un sous-message
    /// LEN), donc le filtre Varint ne matchait jamais -> null systématique -> les invocations du joueur n'étaient
    /// jamais classées ALLIÉES. Sert à identifier le PROPRIÉTAIRE d'une invocation : si la source est MON perso,
    /// l'invocation est ALLIÉE (id négatif comme un monstre, mais à NE PAS cibler). Résolu par nom via le catalogue
    /// (repli 11). null si absent. ⚠ À CONFIRMER en jeu sur une vraie trame jwe d'invocation.</summary>
    public static long? ReadActionSource(IReadOnlyList<ProtoField> tree)
    {
        var s = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "source_id") && f.Wire == WireType.Varint);
        return s?.AsSignedVarint;
    }

    /// <summary>Vrai si ce FightTurnEvent (`jwi`) désigne le tour du perso dont l'id perso est `me` : son `character_id`
    /// (grand entier positif, ~1e10) == `me`. C'est le signal FIABLE de « c'est MON tour » (
    /// `IsMyTurn = character_id == mon id`), contrairement au `fighter_id` de `jto` (id de COMBAT) qui peut valoir un
    /// sentinel -1 (0xFFFFFFFFFFFFFFFF) ne matchant jamais le perso, et à `jyj` qui est VIDE.
    /// PATCH-PROOF : Dofus décale les champs et ajoute des champs factices à chaque MAJ -> on ne fige PAS le n° de champ ;
    /// on teste si N'IMPORTE QUEL varint du message vaut `me` (l'id perso est une valeur unique et grande, aucun champ de
    /// durée/factice ne peut entrer en collision).</summary>
    /// <summary>Combattant dont c'est le tour (FightTurnEvent.character_id, signé : monstres négatifs) ; 0 si le champ manque.</summary>
    public static long TurnActor(IReadOnlyList<ProtoField> tree)
        => tree.FirstOrDefault(f => f.Field == F("FightTurnEvent", "character_id") && f.Wire == WireType.Varint) is { } who ? who.AsSignedVarint : 0;

    public static bool IsFightTurnOf(IReadOnlyList<ProtoField> tree, long me)
    {
        if (me == 0) return false;
        // 3.6.11.12 : le combattant dont c'est le tour est en #7 (capture : { round #3, durée #5, rang #6, id #7 }).
        // #7 vient du catalogue (FightTurnEvent.character_id) avec 7 en repli ; le balayage large reste en repli
        // patch-proof (un id perso ~1e10 n'entre en collision avec aucun champ de durée) et n'est PAS retiré : c'est
        // un filet délibéré (cf tour fantôme) que le gate par front jyj rend inoffensif.
        if (tree.FirstOrDefault(f => f.Field == F("FightTurnEvent", "character_id") && f.Wire == WireType.Varint) is { } who)
            return who.AsSignedVarint == me;
        foreach (var f in tree)
            if (f.Wire == WireType.Varint && f.AsSignedVarint == me) return true;
        return false;
    }

    /// <summary>ÉQUIPE (spawn_information.team) d'un combattant + son id de jointure (jxg), pour classer ALLIÉ/ENNEMI par
    /// ÉQUIPE (`ennemi = team != MON équipe`, MON équipe lue DYNAMIQUEMENT — jamais 0 en dur, une
    /// agression inverse les côtés). **PIÈGE proto3 (confirmé 3 sources)** : team=0 (Challenger) est OMISE du wire ->
    /// spawn.#2 PRÉSENT = Defender(1), ABSENT = Challenger(0). (L'ancien code lisait spawn.#3 = `alive` = 1 pour tous.)
    /// Structure live : information = body.#2 { disposition #1, actor_information #2, actor_id #3 } ;
    /// combattant = information.#2.#2 { innerId #1 (= id perso si joueur, 0 si créature), stats #2, spawn #7 { team #2,
    /// alive #3, position #4 } }. On CLÉ par l'id qui matche `_fighters` : le PERSO par son innerId #1 positif, une
    /// CRÉATURE par l'actor_id #3 (son id de combat négatif) — évite la collision du -1 ambigu de la forme détaillée perso.</summary>
    /// <summary>Ids des MEMBRES d'une équipe (FightTeamUpdateEvent `kae`). team = #1 (FightTeamInformation) ; membres
    /// répétés = #1.#5.#1[] ; id de CHAQUE membre = #2 (charId POSITIF pour un joueur, fight id NÉGATIF pour une entité
    /// invoquée/compagnon — ex archiduk). Un membre entité porte aussi son propriétaire à #7.#4. Sert à reconnaître les
    /// ALLIÉS (compagnon/invocation présents dans MON roster) que le jxg per-fighter n'expose PAS toujours (pas de team).</summary>
    public static IReadOnlyList<long> ReadTeamMemberIds(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<long>();
        var team = tree.FirstOrDefault(f => f.Field == F("FightTeamUpdateEvent", "team") && f.Message is not null)?.Message;   // FightTeamInformation
        if (team is null) return list;
        // 3.6.11.12 : membres = #1.#2[] avec l'id en #4 (avant : #1.#5[] avec l'id en #2). Capture 2026-09-08 :
        // jym = { #1 { #2 { #2 { #4 = 41234567890, #5 { nom "Persoun", niveau 132 } } }, #5 = 41234567890, #8 = 1 } }.
        // On balaie TOUS les sous-messages du conteneur : la liste est répétée, mais le numéro d'entrée dépend
        // du camp (roster vide -> le conteneur dégénère en chaîne vide) et le seul invariant est l'id en #4.
        int fMemberId = F("FightTeamMemberInformation", "member_id");
        if (team.FirstOrDefault(f => f.Field == F("FightTeamInformation", "team_members") && f.Message is not null)?.Message is { } members)
            foreach (var m in members)
                if (m.Message is { } mm
                    && mm.FirstOrDefault(x => x.Field == fMemberId && x.Wire == WireType.Varint) is { } idF)
                    list.Add(idF.AsSignedVarint);
        // Repli : le MENEUR (leader_id) est toujours présent, même quand la liste détaillée est vide.
        if (list.Count == 0
            && team.FirstOrDefault(f => f.Field == F("FightTeamInformation", "leader_id") && f.Wire == WireType.Varint) is { } lead)
            list.Add(lead.AsSignedVarint);
        return list;
    }

    public static (long JoinId, int Team)? ReadFighterTeam(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : entrée #1 = { information #1, actorId #2, disposition #3 } ; combattant = information.#3 ;
        // spawn = combattant.#2 { team #2, ... } ; innerId (id perso) = combattant.#7.
        // Le PIÈGE proto3 est intact : team = 0 (Challenger) est OMISE -> #2 présent = Defender(1), absent = 0.
        // Vérifié : j'agresse le groupe -> MOI sans #2 (camp 0), les 7 monstres avec #2 = 1.
        var entry = tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message;
        var fighter = entry?.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_information") && f.Message is not null)?.Message
                          ?.FirstOrDefault(f => f.Field == F("ActorInformation", "fighter") && f.Message is not null)?.Message;
        var spawn = fighter?.FirstOrDefault(f => f.Field == F("FightFighterInformation", "spawn_information") && f.Message is not null)?.Message;
        if (spawn is not null)
        {
            int t2 = spawn.FirstOrDefault(f => f.Field == F("SpawnInformation", "team") && f.Wire == WireType.Varint) is { } t2f ? (int)t2f.Varint : 0;
            long inner2 = fighter!.FirstOrDefault(f => f.Field == F("FightFighterInformation", "character_id") && f.Wire == WireType.Varint)?.AsSignedVarint ?? 0;
            if (inner2 > 0) return (inner2, t2);
            var a2 = entry!.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "actor_id") && f.Wire == WireType.Varint);
            if (a2 is not null) return (a2.AsSignedVarint, t2);
        }
        return null;
    }

    /// <summary>MAJ EN DIRECT d'une caractéristique (GameActionFightEvent jxw) : `#1 acteur, #3 msg { #5 msg { #1 charId,
    /// #5 msg { #1 base, #4 contextModif(négatif), #6 used } } }`. Renvoie la valeur COURANTE = base + contextModif
    ///. Les entrées sans valeur caractéristique sont ignorées.</summary>
    public static IEnumerable<(long Actor, int CharId, int Value)> ReadCharacteristicUpdates(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : jxh = { acteur #1, #2 { entrée #3 { valeur #1|#2|#3, charId #4 }, #4 } }.
        // Avant : { #1, #3 { #5 { charId #1, valeur } } }. Tout a bougé d'un cran.
        var actorF = tree.FirstOrDefault(f => f.Field == F("FightRefreshCharacterStatsEvent", "fighter_id") && f.Wire == WireType.Varint);
        if (actorF is null) yield break;
        var cont = tree.FirstOrDefault(f => f.Field == F("FightRefreshCharacterStatsEvent", "stats") && f.Message is not null)?.Message;
        int idFieldNo = F("CharacterCharacteristic", "characteristic_id");
        if (cont is null) yield break;
        foreach (var field in cont)
        {
            if (field.Field != F("FightCharacteristics", "characteristics") || field.Message is not { } entry) continue;
            var idF = entry.FirstOrDefault(x => x.Field == idFieldNo && x.Wire == WireType.Varint);
            if (!entry.Any(x => x.Message is not null && (x.Field == F("CharacterCharacteristic", "usable") || x.Field == F("CharacterCharacteristic", "detailed") || x.Field == F("CharacterCharacteristic", "value")))) continue;
            int charId = idF is null ? 0 : (int)idF.Varint;   // id 0 (PV de base) OMIS du wire : piège proto3
            yield return (actorF.AsSignedVarint, charId, (int)CharacteristicValue(entry));
        }
    }

    // [legacy retiré] ReadCharacteristicUpdateLegacy (lecteur jxh pré-patch, conteneur #3/entrée #5/charId #1) : aucun
    // appelant (prouvé : 0 référence dans la solution), totalement inatteignable sur le wire v4 -> supprimé. La forme v4
    // est gérée par ReadCharacteristicUpdates ci-dessus (routée catalogue).

    /// <summary>Cellule d'un combattant depuis GameFightShowFighter (jxg) : body.#1.#1. Sert à AJOUTER un combattant
    /// nouvellement apparu (invocation) au suivi de positions. null si absent.</summary>
    public static int? ReadShowFighterCell(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : entrée #1, disposition #3, cellule #4 (avant : #2 / #1 / #1).
        if (tree.FirstOrDefault(f => f.Field == F("FightFighterShowEvent", "information") && f.Message is not null)?.Message is { } e1
            && e1.FirstOrDefault(f => f.Field == F("ActorPositionInformation", "disposition") && f.Message is not null)?.Message is { } d3
            && d3.FirstOrDefault(x => x.Field == F("EntityDisposition", "cell_id") && x.Wire == WireType.Varint) is { } c4)
            return (int)c4.Varint;
        return null;
    }

    /// <summary>Un combattant INVOQUÉ : id, case (-1 si disposition absente), équipe (null si l'info de spawn manque ; proto3 : équipe 0
    /// omise = 0 quand le spawn est présent), invocateur (null si absent).</summary>
    public readonly record struct SummonedFighter(long Id, int Cell, int? Team, long? Summoner,
        int? InvisibilityState = null);

    public static SummonedFighter? ReadShownSummon(IReadOnlyList<ProtoField> tree)
    {
        var actor = Sub(tree, F("FightFighterShowEvent", "information"));
        var fighter = Sub(Sub(actor, F("ActorPositionInformation", "actor_information")), F("ActorInformation", "fighter"));
        var stats = Sub(fighter, F("FightFighterInformation", "stats"));
        var spawn = Sub(fighter, F("FightFighterInformation", "spawn_information"));
        if (stats?.Any(f => f.Field == F("FightCharacteristics", "summoned") && f.Wire == WireType.Varint && f.Varint != 0) != true
            || spawn?.Any(f => f.Field == F("SpawnInformation", "alive") && f.Wire == WireType.Varint && f.Varint != 0) != true
            || SummonerOf(stats) is not { } owner)
            return null;
        return actor is null ? null : FromActorPosition(actor, null, owner);
    }

    /// <summary>
    /// INVOCATIONS d'une action de combat (GameActionFightEvent.summons), QUEL QUE SOIT le type d'invocation (créature, double,
    /// bombe, invocation statique comme l'épée du Sacrieur…). Avant : seul l'action 181 était lue, et seulement la 1re
    /// invocation, sans équipe ni invocateur -> une invocation ALLIÉE (lancée par un coéquipier) tombait dans le repli
    /// « id négatif = monstre » et le bot la frappait. Les invocations ne passent pas par FightFighterShowEvent : sans
    /// elles, leur case restait aussi « libre » (bot qui marche dessus / tire à travers).
    /// Deux formes : « par acteur » (ActorPositionInformation complètes : équipe dans spawn_information, invocateur dans
    /// stats) et « par contexte » (groupe d'invocations identiques : équipe par SpawnInformation, invocateur commun).
    /// </summary>
    public static List<SummonedFighter> ReadSummons(IReadOnlyList<ProtoField> tree)
    {
        var res = new List<SummonedFighter>();
        var summons = Sub(tree, F("GameActionFightEvent", "summons"));
        if (summons is null) return res;
        if (Sub(summons, F("Summons", "summons_by_actor")) is { } byActor)
            foreach (var a in byActor)
                if (a.Field == F("SummonsByActor", "summons") && a.Message is not null && FromActorPosition(a.Message, null, null) is { } s)
                    res.Add(s);
        if (Sub(summons, F("Summons", "summons_by_context_information")) is { } byCtx)
            foreach (var c in byCtx)
            {
                if (c.Field != F("SummonsByContextInformation", "summons") || c.Message is null) continue;
                long? summoner = SummonerOf(Sub(c.Message, F("SummonContextInformation", "characteristics")));
                var contextStats = Sub(c.Message, F("SummonContextInformation", "characteristics"));
                foreach (var sp in c.Message)
                {
                    if (sp.Field != F("SummonContextInformation", "summons") || sp.Message is null) continue;
                    int team = (int)(sp.Message.FirstOrDefault(x => x.Field == F("SpawnInformation", "team") && x.Wire == WireType.Varint)?.Varint ?? 0);
                    if (Sub(sp.Message, F("SpawnInformation", "position")) is { } pos && FromActorPosition(pos, team, summoner) is { } s)
                        res.Add(s with { InvisibilityState = s.InvisibilityState ?? VisibilityOf(contextStats) });
                }
            }
        return res;
    }

    private static IReadOnlyList<ProtoField>? Sub(IReadOnlyList<ProtoField>? msg, int field) =>
        msg?.FirstOrDefault(f => f.Field == field && f.Message is not null)?.Message;

    private static SummonedFighter? FromActorPosition(IReadOnlyList<ProtoField> api, int? team, long? summoner)
    {
        long id = api.FirstOrDefault(x => x.Field == F("ActorPositionInformation", "actor_id") && x.Wire == WireType.Varint)?.AsSignedVarint ?? 0;
        if (id == 0) return null;
        var disposition = api.FirstOrDefault(x =>
            x.Field == F("ActorPositionInformation", "disposition") && x.Wire == WireType.Len);
        var cellF = disposition?.Message
            ?.FirstOrDefault(x => x.Field == F("EntityDisposition", "cell_id") && x.Wire == WireType.Varint);
        var fighter = Sub(Sub(api, F("ActorPositionInformation", "actor_information")), F("ActorInformation", "fighter"));
        if (fighter is not null)
        {
            if (team is null && Sub(fighter, F("FightFighterInformation", "spawn_information")) is { } spawn)
                team = (int)(spawn.FirstOrDefault(x => x.Field == F("SpawnInformation", "team") && x.Wire == WireType.Varint)?.Varint ?? 0);
            summoner ??= SummonerOf(Sub(fighter, F("FightFighterInformation", "stats")));
        }
        return new SummonedFighter(id, disposition is null ? -1 : (int)(cellF?.Varint ?? 0), team, summoner,
            VisibilityOf(Sub(fighter, F("FightFighterInformation", "stats"))));
    }

    private static int? VisibilityOf(IReadOnlyList<ProtoField>? stats) => stats is null ? null
        : (int)(stats.FirstOrDefault(x => x.Field == F("FightCharacteristics", "invisibility_state")
            && x.Wire == WireType.Varint)?.Varint ?? 0);

    private static long? SummonerOf(IReadOnlyList<ProtoField>? stats)
    {
        var s = stats?.FirstOrDefault(x => x.Field == F("FightCharacteristics", "summoner") && x.Wire == WireType.Varint);
        return s is null || s.AsSignedVarint == 0 ? null : s.AsSignedVarint;
    }

    /// <summary>CharacterCharacteristicsEvent (kub, hors combat) : mes PA/PM + PV max de base. null si absent.</summary>
    public static (int Pa, int Pm, int MaxLife, int Range)? ReadSelfCharacteristics(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        if (container is null) return null;
        // Les caractéristiques sont répétées au champ CharacterCharacteristics.characteristics (5 en repli ; 11 = ancien).
        var m = ParseEntries(container, F("CharacterCharacteristics", "characteristics"));
        if (!m.ContainsKey(IdPa) && !m.ContainsKey(IdPm)) return null;
        return (m.GetValueOrDefault(IdPa), m.GetValueOrDefault(IdPm), m.GetValueOrDefault(IdLife) + m.GetValueOrDefault(IdVitality), m.GetValueOrDefault(IdRange));
    }

    /// <summary>CharacterCharacteristicsEvent : TOUTES les caractéristiques du perso (id de caractéristique -> total : force 10, intelligence 15, % dommages 25, résistances 33-37…). null si absent.</summary>
    public static IReadOnlyDictionary<int, int>? ReadSelfCharacteristicMap(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        if (container is null) return null;
        var m = ParseEntries(container, F("CharacterCharacteristics", "characteristics"));
        return m.Count == 0 ? null : m;
    }

    /// <summary>Valeurs de base seules, sans équipement ni bonus : la requête de répartition utilise une cible absolue.</summary>
    public static IReadOnlyDictionary<int, int>? ReadSelfCharacteristicBases(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        if (container is null) return null;
        var result = new Dictionary<int, int>();
        foreach (var field in container.Where(f => f.Field == F("CharacterCharacteristics", "characteristics") && f.Message is not null))
        {
            var entry = field.Message!;
            int id = (int)(entry.FirstOrDefault(f => f.Field == F("CharacterCharacteristic", "characteristic_id") && f.Wire == WireType.Varint)?.Varint ?? 0);
            foreach (string kind in new[] { "detailed", "usable" })
            {
                var fieldValue = entry.FirstOrDefault(f => f.Field == F("CharacterCharacteristic", kind) && f.Wire == WireType.Len);
                if (fieldValue is null) continue;
                var value = fieldValue.Message;
                string type = kind == "detailed" ? "CharacterCharacteristicDetailed" : "CharacterCharacteristicDetailedUsable";
                result[id] = (int)(value?.FirstOrDefault(f => f.Field == F(type, "base") && f.Wire == WireType.Varint)?.AsSignedVarint ?? 0);
                break;
            }
        }
        return result;
    }

    /// <summary>Kamas depuis CharacterCharacteristicsEvent (kub) : conteneur #2 -&gt; champ #10 (int64). Confirmé capture :
    /// 177242. kub refire souvent (après un combat, refresh périodique...) -&gt; garde le total de kamas à jour hors login.</summary>
    public static long? ReadKamas(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : les kamas sont au champ #11 du conteneur (avant #10). Vérifié : 54 156 032, la même valeur
        // que le champ kamas du snapshot d'inventaire reçu 40 ms plus tôt.
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        var k = container?.FirstOrDefault(f => f.Field == F("CharacterCharacteristics", "kamas") && f.Wire == WireType.Varint);
        return k is null ? null : (long)k.Varint;
    }

    /// <summary>Expérience TOTALE depuis CharacterCharacteristicsEvent (kub) : conteneur #2 -&gt; champ experience #6 (int64).
    /// null si absent. Confirmé capture (320 157 998, entre level_floor #4 et next_level_floor #8). Sert au calcul XP/heure.</summary>
    public static long? ReadExperience(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        var x = container?.FirstOrDefault(f => f.Field == F("CharacterCharacteristics", "experience") && f.Wire == WireType.Varint);
        return x is null ? null : (long)x.Varint;
    }

    // ================================================================== Lot F : survie (PV réels, régénération, énergie, XP)

    /// <summary>Vitalité hors combat depuis CharacterCharacteristicsEvent : PV max (0+11), delta de PV courants (97), énergie
    /// (29/47), paliers d'XP (experience_level_floor / experience_next_level_floor). null si ni caractéristique ni palier.</summary>
    public static SelfVitals? ReadSelfVitals(IReadOnlyList<ProtoField> tree)
    {
        var container = tree.FirstOrDefault(f => f.Field == F("CharacterCharacteristicsEvent", "stats") && f.Message is not null)?.Message;
        if (container is null) return null;
        var m = ParseEntries(container, F("CharacterCharacteristics", "characteristics"));
        var lf = container.FirstOrDefault(f => f.Field == F("CharacterCharacteristics", "experience_level_floor") && f.Wire == WireType.Varint);
        var nf = container.FirstOrDefault(f => f.Field == F("CharacterCharacteristics", "experience_next_level_floor") && f.Wire == WireType.Varint);
        if (m.Count == 0 && lf is null && nf is null) return null;
        int? Opt(int id) => m.TryGetValue(id, out var v) ? v : null;
        return new SelfVitals(
            m.GetValueOrDefault(IdLife) + m.GetValueOrDefault(IdVitality),
            Opt(IdCurLifeDelta), Opt(IdEnergy), Opt(IdMaxEnergy),
            lf is null ? null : (long)lf.Varint, nf is null ? null : (long)nf.Varint);
    }

    /// <summary>UpdateLifePointsEvent : (PV, PV max, régénération terminée ?). fzgr/fzgs (#1/#2) sont OBFUSQUÉS, ordre inconnu
    /// -&gt; décodés PAR VALEUR (PV ≤ PV max : min = PV, max = PV max). Une seule valeur = PV à 0 omis (proto3). null si vide.</summary>
    public static (int Life, int Max, bool Gained)? ReadUpdateLifePoints(IReadOnlyList<ProtoField> tree)
    {
        int g = F("UpdateLifePointsEvent", "life_points_gained");
        var vals = tree.Where(f => f.Field != g && f.Wire == WireType.Varint).Select(f => (int)f.AsSignedVarint).ToList();
        if (vals.Count == 0) return null;
        bool gained = tree.Any(f => f.Field == g);
        return vals.Count == 1 ? (0, vals[0], gained) : (vals.Min(), vals.Max(), gained);
    }

    /// <summary>LifePointsRegenBeginEvent.regen_rate (taux de régénération, Dofus 2 : 1 PV toutes les rate×100 ms). null si absent.</summary>
    public static int? ReadRegenRate(IReadOnlyList<ProtoField> tree)
    {
        var r = tree.FirstOrDefault(f => f.Field == F("LifePointsRegenBeginEvent", "regen_rate") && f.Wire == WireType.Varint);
        return r is null ? null : (int)r.Varint;
    }

    /// <summary>EmotePlayEvent : (emote_id, actor_id). Champs absents -&gt; 0 (proto3).</summary>
    public static (int EmoteId, long ActorId) ReadEmotePlay(IReadOnlyList<ProtoField> tree)
    {
        var e = tree.FirstOrDefault(f => f.Field == F("EmotePlayEvent", "emote_id") && f.Wire == WireType.Varint);
        var a = tree.FirstOrDefault(f => f.Field == F("EmotePlayEvent", "actor_id") && f.Wire == WireType.Varint);
        return (e is null ? 0 : (int)e.Varint, a?.AsSignedVarint ?? 0);
    }

    /// <summary>Perte de PV : GameActionFightEvent avec sous-message #40 { #2 cible, #3 montant }. Renvoie (cible, montant).</summary>
    public readonly record struct LifeLoss(long Target, int Amount, int PermanentDamage, int ShieldLoss = 0);

    /// <summary>Soins et vols de vie annoncés par le serveur, pour joueurs comme monstres.</summary>
    public static (long Target, int Amount)? ReadLifeGain(IReadOnlyList<ProtoField> tree)
    {
        var gain = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "life_points_gain") && f.Message is not null)?.Message;
        var target = gain?.FirstOrDefault(f => f.Field == F("LifePointsGain", "target_id") && f.Wire == WireType.Varint);
        if (target is null) return null;
        int amount = (int)(gain!.FirstOrDefault(f => f.Field == F("LifePointsGain", "delta") && f.Wire == WireType.Varint)?.Varint ?? 0);
        return (target.AsSignedVarint, amount);
    }

    public static (long Target, int Amount)? ReadLifeLoss(IReadOnlyList<ProtoField> tree)
        => ReadLifeLossDetails(tree) is { } loss ? (loss.Target, loss.Amount) : null;

    /// <summary>Perte courante et érosion annoncées séparément par le serveur.</summary>
    public static LifeLoss? ReadLifeLossDetails(IReadOnlyList<ProtoField> tree)
    {
        var loss = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "life_points_lost") && f.Message is not null)?.Message;
        if (loss is null) return null;
        var target = loss.FirstOrDefault(f => f.Field == F("LifePointsLost", "target_id") && f.Wire == WireType.Varint);
        if (target is null) return null;
        int amount = (int)(loss.FirstOrDefault(f => f.Field == F("LifePointsLost", "loss") && f.Wire == WireType.Varint)?.Varint ?? 0);
        int permanent = (int)(loss.FirstOrDefault(f => f.Field == F("LifePointsLost", "permanent_damages") && f.Wire == WireType.Varint)?.Varint ?? 0);
        int shield = (int)(loss.FirstOrDefault(f => f.Field == F("LifePointsLost", "shield_loss") && f.Wire == WireType.Varint)?.Varint ?? 0);
        return new LifeLoss(target.AsSignedVarint, amount, permanent, shield);
    }

    /// <summary>Variation PA/PM : action_id (#14) ∈ {102 = PA, 129 = PM}, sous-message #20 { #1 delta signé, #2 cible }.</summary>
    public static (long Target, bool IsPa, int Delta)? ReadPointVariation(IReadOnlyList<ProtoField> tree)
    {
        var act = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "action_id") && f.Wire == WireType.Varint);
        if (act is null || (act.Varint != 102 && act.Varint != 129)) return null;
        // Porteur GameActionFightEvent.points_variation, sous-message PointsVariation { delta, target_id } : noms du fields.json
        // v5 (l'ancien nom « GameActionFightPointsVariation » ne tenait que par _aliases, absents en prod, 04/10).
        if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "points_variation") && f.Message is not null)?.Message is { } s12
            && s12.FirstOrDefault(x => x.Field == F("PointsVariation", "delta") && x.Wire == WireType.Varint) is { } d12
            && s12.FirstOrDefault(x => x.Field == F("PointsVariation", "target_id") && x.Wire == WireType.Varint) is { } c12)
            return (c12.AsSignedVarint, act.Varint == 102, (int)d12.AsSignedVarint);
        return null;
    }
}
