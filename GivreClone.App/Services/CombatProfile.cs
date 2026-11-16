using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>
/// Style d'IA de combat: pilote le MOUVEMENT et le gating offensif. (dont l'IA "modes" est du code mort),
/// mais expose en dur les 3 comportements demandes. Aggressive = comportement actuel (approche a portee puis tape).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CombatStyle>))]
public enum CombatStyle
{
    Aggressive, // s'approche jusqu'a une case de tir puis lance ses sorts ; en fin de tour se rapproche encore
    Fleeing,    // kite : lance de loin si possible, puis en fin de tour s'ELOIGNE au maximum des ennemis
    Passive,    // ne se deplace PAS vers l'ennemi : lance seulement si deja a portee, sinon passe (buff/soutien)
}

/// <summary>Case de placement en début de combat, indépendante du style. <see cref="LegacyByStyle"/> garde l'ancien
/// choix (Agressif = proche, tout autre style = loin).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlacementMode>))]
public enum PlacementMode
{
    LegacyByStyle,
    Keep, // rester sur la case actuelle si elle est offerte ; sinon aucun envoi
    Near, // case offerte la plus proche d'un ennemi
    Far,  // case offerte la plus loin d'un ennemi
}

/// <summary>Déplacement vers l'ennemi. <see cref="Standard"/> = style + « se déplacer pour lancer » de chaque sort.
/// <see cref="NeverApproach"/> bloque le repositionnement pour porter un sort ET le rapprochement de fin de tour.
/// S'éloigner (kite) reste celui du style.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CombatMovementPolicy>))]
public enum CombatMovementPolicy
{
    Standard,
    NeverApproach,
}

/// <summary>Choix de la cible prioritaire. Closest = defaut.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetMode>))]
public enum TargetMode
{
    Closest,  // ennemi le plus proche (defaut)
    LowestHp, // ennemi avec le moins de PV (focus-kill : PV suivis en live via FighterHpPercent ; a PV egal, le plus proche)
    Farthest, // ennemi le plus loin
}

/// <summary>Relation de la CIBLE d'un sort. Enemy = défaut (offensif). Ally = coéquipier/invoc
/// (soin/buff : cible l'allié le plus bas en PV, hors soi). Le sur-soi reste géré par <see cref="SpellConfig.OnMe"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpellTargetKind>))]
public enum SpellTargetKind
{
    Enemy, // cible un ennemi (défaut)
    Ally,  // cible un allié (soin/buff) : l'allié le plus bas en PV, castable d'ici
    Adjacent, // cible une case ADJACENTE à moi : celle qui touche le plus d'ennemis dans la zone du sort (ou l'ennemi collé)
}

/// <summary>Caractéristique cible de l'auto level-up : la stat dans laquelle dépenser automatiquement les points gagnés à
/// chaque montée de niveau. Un « full stat » par classe (Force pour Iop, Agilité pour Cra, Intelligence/Sagesse pour Eni…).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LevelStatTarget>))]
public enum LevelStatTarget
{
    Strength,     // Force
    Intelligence, // Intelligence
    Chance,       // Chance
    Agility,      // Agilité
    Vitality,     // Vitalité
    Wisdom,       // Sagesse
}

/// <summary>Vitesse de jeu : delai entre chaque action. Instant = quasi immediat (juste ce qu'il faut au serveur), Fast
/// et Slow = delais ALEATOIRES (moins detectable). Instant garde un petit delai sur les DEPLACEMENTS (confirmation reseau).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CombatSpeed>))]
public enum CombatSpeed
{
    Instant = 0, // ~immediat (cast ~150ms, deplacement ~400ms pour la confirmation serveur)
    Fast = 1,    // aleatoire ~0,5-1 s par action
    Slow = 2,    // aleatoire ~1-3 s par action
    Turbo = 3,   // MITM : barrière SequenceEnd+self ACK au lieu des sleeps fixes ; active aussi les mods visuels de combat
}

/// <summary>Un sort de la rotation: id, priorite, nb de lancers/tour, sur-soi.</summary>
public sealed class SpellConfig
{
    // Recaste uniquement un sort offensif visant effectivement un ennemi.
    // Une invocation se lance sur une cellule LIBRE : le filet de dernier recours
    // cible une cellule OCCUPÉE par un ennemi et ne doit jamais la reclassifier.
    public bool CanUseOffensiveFallback()
        => CombatParity.OffensiveFallbackPolicy.IsEligible(
            Enabled, SpellId, PerTurn, OnMe, IsSummon, TargetKind);

    public int SpellId { get; set; }
    public int Priority { get; set; }        // plus petit = lance en premier
    public int PerTurn { get; set; } = 1;     // nb MAX de lancers de CE sort par tour
    public int Cooldown { get; set; }         // temps de relance : N tours mini entre 2 lancers de CE sort (0 = aucun)
    public int PerTarget { get; set; }        // lancers max de CE sort sur une même cible par tour (0 = limite du jeu seule)
    public bool Enabled { get; set; } = true; // desactiver sans supprimer la ligne
    public bool OnMe { get; set; }            // lancer sur MA cellule (buff/soin perso) : aucun ciblage ni portee
    public SpellTargetKind TargetKind { get; set; }   // relation de la cible : Enemy (défaut) | Ally (soin/buff d'un coéquipier)
    // Sort sur soi de type BUFF : le lancer meme si AUCUN ennemi n'est a portee. Par defaut (false) un sort sur soi
    // OFFENSIF (degats en zone autour de moi) n'est lance QUE si un ennemi est dans la zone -> plus de gaspillage a vide.
    // (Un sort sans donnees de degats est auto-considere comme buff -> lance toujours, meme flag a false.)
    public bool SelfBuff { get; set; }
    // INVOCATION : ce sort place une créature sur une CELLULE LIBRE (pas de cible ennemie). Le moteur cherche une cellule
    // marchable inoccupée à portée et lance dessus. MaxSummons plafonne le nombre d'invocations VIVANTES à moi (0 = 1).
    public bool IsSummon { get; set; }
    public int MaxSummons { get; set; } = 1;

    // --- Conditions de lancement (onglet Combat) : 0 = désactivé (défaut) -> AUCUNE restriction (profils existants
    //     inchangés). Le sort n'est lancé que si TOUTES ses conditions sont remplies. PV en % (0-100). PV inconnu ->
    //     condition ignorée (fail-open : on ne bloque pas un cast faute d'info). ---
    public int MinAp { get; set; }              // PA COURANTS mini pour tenter ce sort (0 = aucun ; s'ajoute au coût du sort)
    public int MinTurn { get; set; }            // n° de tour mini (1-based) avant d'autoriser ce sort (0 = dès le tour 1)
    public int MinEnemies { get; set; }         // nb d'ennemis vivants mini pour lancer (0 = aucune exigence)
    public int MinAoeHits { get; set; }         // ZONE : ne lancer que si la MEILLEURE case-cible attrape >= N ennemis (0 = aucune ; sorts mono/sans zone ignorent)
    public int SelfHpMin { get; set; }          // PV% du LANCEUR : lancer seulement si mon PV% >= SelfHpMin ...
    public int SelfHpMax { get; set; } = 100;   // ... ET <= SelfHpMax (fenêtre ; 0..100 = toujours)
    public int TgtHpMin { get; set; }           // PV% de la CIBLE : lancer seulement si PV cible% >= TgtHpMin ...
    public int TgtHpMax { get; set; } = 100;    // ... ET <= TgtHpMax (fenêtre ; 0..100 = toute cible)

    // --- Placement & zones (onglet Placement) : surcharges de la fenêtre de PORTÉE de tir + autorisation de déplacement.
    //     0 = auto (portée native du sort, boosts de PO inclus) -> comportement inchangé. ---
    public int CastRangeMin { get; set; }        // distance MINI de tir imposée (kite par sort) : ne pas lancer plus près (0 = auto)
    public int CastRangeMax { get; set; }        // PLAFOND de portée de tir : se rapprocher au lieu de sniper de loin (0 = auto = portée max native)
    public bool AllowApproach { get; set; } = true; // autoriser le bot à SE DÉPLACER pour amener ce sort à portée (false = tir sur place seulement)

    public SpellConfig Clone() => (SpellConfig)MemberwiseClone();
}

/// <summary>Config de combat d'UN personnage (onglet Combat). Persistee par nom de perso via CombatProfileStore.</summary>
public sealed class CombatProfile
{
    public bool AutoFight { get; set; }                            // jouer automatiquement mes tours de combat
    public CombatStyle Style { get; set; } = CombatStyle.Aggressive;
    public PlacementMode PlacementMode { get; set; } = PlacementMode.LegacyByStyle;
    public CombatMovementPolicy MovementPolicy { get; set; } = CombatMovementPolicy.Standard;
    /// <summary>Faux pour <see cref="CombatMovementPolicy.NeverApproach"/> : ni case de tir, ni rapprochement de fin de tour.</summary>
    [JsonIgnore] public bool AllowsApproach => MovementPolicy != CombatMovementPolicy.NeverApproach;
    public TargetMode Target { get; set; } = TargetMode.Closest;
    // Ciblage « finish/execute » : par-dessus le mode de ciblage, faire PASSER EN PREMIER les ennemis TUABLES ce tour
    // (PV absolus <= dégâts que la rotation abordable peut poser sur une cible). Défaut ON (comportement d'un bon joueur :
    // achever une cible pour lui retirer son tour). Estimation conservatrice (moyenne, PA courants) ; réordonne seulement.
    public bool FinishKill { get; set; } = true;
    public CombatSpeed Speed { get; set; } = CombatSpeed.Fast;     // vitesse de jeu (delais entre actions)
    public int MaxCastsPerTurn { get; set; } = 99;                 // plafond GLOBAL de lancers par tour (tous sorts confondus)
    public int KiteMinDistance { get; set; } = 4;                  // distance mini a garder des ennemis en style Fleeing (borne BASSE de la bande de kite)
    public int KiteMaxDistance { get; set; }                       // borne HAUTE de la bande de kite (0 = illimitée -> ancien kite « fuir au max » ; >0 -> reste dans [min..max])
    // Auto level-up des caractéristiques : à chaque montée de niveau, dépenser les points de carac dans LevelStatTarget.
    // On ne connaît pas le pool (champ non résolu dans le mapping) -> le moteur dépense 1 point puis lit les points restants
    // renvoyés par CharacterCharacteristicUpgradeResultEvent (kqx) et reboucle jusqu'à 0. Gaté au point d'action sous Perm.Fight.
    public bool AutoLevelStats { get; set; }
    public LevelStatTarget LevelStatTarget { get; set; } = LevelStatTarget.Strength;
    public List<SpellConfig> Spells { get; set; } = new();
    // Mode AUTOMATIQUE : la rotation est complétée par tous les sorts APPRIS qui font des dégâts (AutoSpellRotation) ; un sort de
    // la liste garde ses réglages, décoché il reste écarté. Défaut OFF (profils existants inchangés).
    public bool AutoSpells { get; set; }
    // Persistent, explicit opt-in for the read-only tactical advisor. This is
    // not the visual Tactical mod and NEVER authorizes a combat action.
    public bool TacticalAdvisorDiagnostic { get; set; }
    public Combat.AutoCombatElements AutoElements { get; set; }
    // Autonomie PRÉ-COMBAT (le prêt auto du CLIENT Dofus doit être DÉSACTIVÉ pour laisser le bot piloter).
    public bool AutoPreFight { get; set; }                         // placement (selon le style) + prêt automatiques
    public bool AutoChallenge { get; set; }                       // choisir + valider un défi au début du combat
    // CHALLENGES : l'IA tient les challenges actifs (Statue, Versatile, Focus, Hardi…) ; liste « Challenges acceptés » =
    // surcharges des valeurs par défaut du catalogue (ids GÉNÉRIQUES du jeu) ; bonus préféré, annoncé par GivreClone à chaque
    // combat (socket : seul annonceur ; MITM : ré-annoncé juste après celui du client, cf ReannounceChallengeBonusAsync).
    public bool ChallengeRules { get; set; } = true;
    public bool ChallengeLoot { get; set; }
    public List<int> ChallengeOn { get; set; } = new();
    public List<int> ChallengeOff { get; set; } = new();
    // Lot C2 : choix du défi laissé au SCRIPT (combat:setChallengeMode("script")). EN MÉMOIRE seulement (jamais dans combat.json) :
    // posé par la couche d'overrides script (ScriptCombatOverrides), le moteur demande les propositions puis attend le choix
    // du script (borné) au lieu de valider le 1er.
    [JsonIgnore] public bool ChallengeByScript { get; set; }
    // Résurrection AUTOMATIQUE au Phénix: à la mort, libère l'âme (FreeSoulRequest) puis
    // voyage jusqu'à la carte du phénix (donnée par le serveur) et l'utilise. Défaut ON : un bot mort ne fait plus rien,
    // le remettre debout est toujours souhaitable. N'est PAS gaté par le droit "fight" (ce n'est pas une action de combat).
    public bool AutoRevive { get; set; } = true;
    // Mods natifs (GivreMods.dll) appliques a ce client, pilotes via ModControl.
    public bool NoSpellEffects { get; set; } = true;
    public bool ShortSpellCasts { get; set; } = true;
    public bool NoDeathAnimations { get; set; }
    public bool NoAnim { get; set; }                               // Combat Animation Skipper (animations de combat instantanees)
    public bool Tactical { get; set; }                            // mode tactique natif (masque le decor de la carte)
    public bool Collapse { get; set; }                            // collapse des attentes de la sequence de combat (temporisations)
    public bool InstantMove { get; set; }                         // deplacements de combat instantanes (teleport a la cellule d'arrivee)
    // Apparence du client (CombatVisuals). 0 normale, 1 créatures, 2 miniatures.
    // Le défaut Enhanced est 2 : un profil sans clé reste à 0, sinon tous les combattants rétrécissent.
    public int CombatAppearanceMode { get; set; }
    public int RoleplayAppearanceMode { get; set; }
    public bool MiniatureSelf { get; set; } = true;
    public bool MiniatureAllies { get; set; } = true;
    public bool MiniatureAllySummons { get; set; } = true;
    public bool MiniatureOwnSummons { get; set; } = true;
    public bool MiniatureEnemies { get; set; } = true;
    public bool MiniatureEnemySummons { get; set; } = true;
    public bool RoleplayMiniatureSelf { get; set; } = true;
    public bool RoleplayMiniatureParty { get; set; } = true;
    public bool RoleplayMiniaturePlayers { get; set; } = true;
    public bool RoleplayMiniatureMonsters { get; set; } = true;
    public bool RoleplayMiniatureNpcs { get; set; }

    // MZP23 : verrous de début de combat posés par le PROFIL (le script peut aussi les poser, cf. FightLocks.Merge) et réglages Turbo.
    public bool LockSpectators { get; set; }                      // verrouiller le combat aux spectateurs (option « secret »)
    public bool LockJoin { get; set; }                            // fermer le combat (plus personne ne peut le rejoindre)
    public bool LockParty { get; set; }                           // combat réservé au groupe
    public bool TurboAckAll { get; set; }                         // Turbo : accuser les séquences de TOUS les combattants dès leur fin (Socket + MITM, cf TurboFightPolicy)
    public bool TurboEagerReady { get; set; }                     // Turbo : « tour prêt » anticipé + réponse immédiate au marqueur + relances 20 × 100 ms (Socket + MITM)
    public int FleeBelowHpPercent { get; set; }                   // sous ce PV % (0 = jamais) : le planificateur privilégie la survie et s'éloigne
    public int ChallengeAbandonHp { get; set; } = 0;             // abandon sous ce PV % strict (0 = désactivé), conservé jusqu'à la fin du combat

    /// <summary>One-line summary of the policy the engine will apply, logged at fight start.</summary>
    public string PolicySummary() =>
        $"auto={(AutoFight ? "oui" : "non")} style={Style} placement={PlacementMode} déplacement={MovementPolicy} cible={Target}"
        + $" achever={(FinishKill ? "oui" : "non")} vitesse={Speed} sorts={(AutoSpells ? "auto+liste" : "liste")} ({Spells.Count(s => s.Enabled)}/{Spells.Count} actifs)"
        + $" kite={KiteMinDistance}-{(KiteMaxDistance > 0 ? KiteMaxDistance.ToString() : "∞")} fuite<{(FleeBelowHpPercent > 0 ? FleeBelowHpPercent + "%" : "jamais")}"
        + $" lancers/tour≤{MaxCastsPerTurn}";

    /// <summary>Copie profonde (snapshot fige pour le moteur, a l'abri des editions UI en cours de tour).</summary>
    public CombatProfile Clone() => new()
    {
        AutoFight = AutoFight, AutoSpells = AutoSpells, TacticalAdvisorDiagnostic = TacticalAdvisorDiagnostic, AutoElements = AutoElements, Style = Style, PlacementMode = PlacementMode, MovementPolicy = MovementPolicy, Target = Target, FinishKill = FinishKill, Speed = Speed,
        MaxCastsPerTurn = MaxCastsPerTurn, KiteMinDistance = KiteMinDistance, KiteMaxDistance = KiteMaxDistance, FleeBelowHpPercent = FleeBelowHpPercent,
        AutoLevelStats = AutoLevelStats, LevelStatTarget = LevelStatTarget,
        AutoPreFight = AutoPreFight, AutoChallenge = AutoChallenge, ChallengeByScript = ChallengeByScript, AutoRevive = AutoRevive,
        ChallengeRules = ChallengeRules, ChallengeLoot = ChallengeLoot, ChallengeOn = new List<int>(ChallengeOn), ChallengeOff = new List<int>(ChallengeOff),
        NoSpellEffects = NoSpellEffects, ShortSpellCasts = ShortSpellCasts, NoDeathAnimations = NoDeathAnimations,
        NoAnim = NoAnim, Tactical = Tactical, Collapse = Collapse, InstantMove = InstantMove,
        CombatAppearanceMode = CombatAppearanceMode, RoleplayAppearanceMode = RoleplayAppearanceMode,
        MiniatureSelf = MiniatureSelf, MiniatureAllies = MiniatureAllies, MiniatureAllySummons = MiniatureAllySummons,
        MiniatureOwnSummons = MiniatureOwnSummons, MiniatureEnemies = MiniatureEnemies, MiniatureEnemySummons = MiniatureEnemySummons,
        RoleplayMiniatureSelf = RoleplayMiniatureSelf, RoleplayMiniatureParty = RoleplayMiniatureParty,
        RoleplayMiniaturePlayers = RoleplayMiniaturePlayers, RoleplayMiniatureMonsters = RoleplayMiniatureMonsters,
        RoleplayMiniatureNpcs = RoleplayMiniatureNpcs,
        LockSpectators = LockSpectators, LockJoin = LockJoin, LockParty = LockParty, TurboAckAll = TurboAckAll, TurboEagerReady = TurboEagerReady, ChallengeAbandonHp = ChallengeAbandonHp,
        Spells = Spells.ConvertAll(s => new SpellConfig
        {
            SpellId = s.SpellId, Priority = s.Priority, PerTurn = s.PerTurn, Cooldown = s.Cooldown, PerTarget = s.PerTarget, Enabled = s.Enabled,
            OnMe = s.OnMe, TargetKind = s.TargetKind, SelfBuff = s.SelfBuff, IsSummon = s.IsSummon, MaxSummons = s.MaxSummons,
            MinAp = s.MinAp, MinTurn = s.MinTurn, MinEnemies = s.MinEnemies, MinAoeHits = s.MinAoeHits,
            SelfHpMin = s.SelfHpMin, SelfHpMax = s.SelfHpMax, TgtHpMin = s.TgtHpMin, TgtHpMax = s.TgtHpMax,
            CastRangeMin = s.CastRangeMin, CastRangeMax = s.CastRangeMax, AllowApproach = s.AllowApproach,
        }),
    };
}

/// <summary>Fichier persiste (%APPDATA%/GivreClone/combat.json) : map nom de perso -> profil de combat.</summary>
public sealed class CombatProfilesFile
{
    public Dictionary<string, CombatProfile> Profiles { get; set; } = new();
}
