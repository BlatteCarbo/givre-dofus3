using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Forge les requêtes CLIENT (C→S) à injecter. Tout — code 3 lettres ET numéro de champ — vient du MAPPING
/// (matching_current.json + fields.json, streamés en prod), résolu PAR NOM CLAIR. AUCUN numéro ni code n'est
/// écrit en dur ici : ils tournent à chaque patch Dofus, et un littéral périmé fait partir un paquet que le
/// serveur jette EN SILENCE (le mode d'échec le plus coûteux). Si le mapping ne résout pas un code ou un champ,
/// la forge REFUSE (trame vide) et l'appelant journalise — jamais de valeur inventée.
///
/// Le seul « en dur » restant est SÉMANTIQUE, pas protocolaire : la VALEUR d'un champ (is_ready = 1,
/// bonus = 1 = Butin, sentinelles négatives) et le NOM CLAIR du champ (clé stable dans fields.json). Les noms
/// obfusqués, eux, tournent comme les numéros : on ne s'y réfère jamais.
/// </summary>
public static class MoveForge
{
    // ============================ RÉSOLUTION PAR NOM (branchée 1× au démarrage) ============================
    private static Func<string, string?>? _codeForName;
    private static Func<string, string, int?>? _fieldNumber;

    /// <summary>Branche le catalogue (mapping streamé). Sans lui, toute forge REFUSE (rien n'est deviné).</summary>
    public static void UseCatalog(Func<string, string?> codeForName, Func<string, string, int?> fieldNumber)
    {
        _codeForName = codeForName;
        _fieldNumber = fieldNumber;
    }

    /// <summary>Code 3 lettres courant d'un nom clair, ou null si le mapping l'ignore.</summary>
    private static string? Code(string clearName) => _codeForName?.Invoke(clearName);

    /// <summary>Numéro de champ wire courant pour (message, champ clair), ou null si absent de fields.json.</summary>
    private static int? Field(string clearName, string field) => _fieldNumber?.Invoke(clearName, field);

    /// <summary>Trame vide = « je n'ai pas pu forger » (code ou champ non résolu). L'appelant teste Length == 0.</summary>
    private static byte[] None => Array.Empty<byte>();

    /// <summary>
    /// Assemble un payload dont CHAQUE champ est résolu par nom via fields.json. Si un seul champ manque, le
    /// message entier est marqué non forgeable (<see cref="Ok"/> = false) : on préfère ne rien envoyer plutôt
    /// qu'une trame partielle que le serveur jetterait. La valeur reste passée en clair par la forge.
    /// </summary>
    private sealed class Msg
    {
        public string Name { get; }
        private readonly List<ProtoField> _fields = new();
        public bool Ok { get; private set; } = true;
        /// <summary>Premier champ (ou sous-message) non résolu par le mapping (diagnostic). Propagé par <see cref="Sub"/>.</summary>
        public string? Error { get; private set; }
        public List<ProtoField> Fields => _fields;
        public Msg(string name) => Name = name;

        private int? Num(string field)
        {
            var n = Field(Name, field);
            if (n is null) { Ok = false; Error ??= $"champ {Name}.{field} absent du mapping"; }
            return n;
        }

        /// <summary>Champ varint INT64 (sign-extend) : map_id, price, owner, ids négatifs (sentinelles).</summary>
        public Msg Vi(string field, long value)
        {
            if (Num(field) is int n) _fields.Add(new ProtoField { Field = n, Wire = WireType.Varint, Varint = (ulong)value });
            return this;
        }

        /// <summary>Champ varint 32 bits non signé : cell, spell_id, uid, quantité, canal, reply_id…</summary>
        public Msg Vu(string field, long value)
        {
            if (Num(field) is int n) _fields.Add(new ProtoField { Field = n, Wire = WireType.Varint, Varint = (ulong)(uint)value });
            return this;
        }

        public Msg Str(string field, string value)
        {
            if (Num(field) is int n) _fields.Add(new ProtoField { Field = n, Wire = WireType.Len, String = value });
            return this;
        }

        public Msg Bytes(string field, byte[] value)
        {
            if (Num(field) is int n) _fields.Add(new ProtoField { Field = n, Wire = WireType.Len, Bytes = value });
            return this;
        }

        /// <summary>Exige que `field` soit résolu par le mapping SANS l'émettre (valeur 0/false = omise en proto3). Un patch qui
        /// renomme/retire le champ fait alors REFUSER la forge au lieu d'envoyer une trame vide de sens différent.</summary>
        public Msg Require(string field) { Num(field); return this; }

        /// <summary>Champ = sous-message (dont les champs internes sont AUSSI résolus par nom). Propage l'échec + son motif.</summary>
        public Msg Sub(string field, Msg inner)
        {
            if (!inner.Ok) { Ok = false; Error ??= inner.Error; return this; }
            if (Num(field) is int n) _fields.Add(new ProtoField { Field = n, Wire = WireType.Len, Message = inner._fields });
            return this;
        }
    }

    /// <summary>Motif d'échec de la DERNIÈRE forge (champ/code non résolu par le mapping), ou null si elle a réussi.
    /// Diagnostic : l'appelant le lit après une trame vide pour savoir QUEL champ/code manque du catalogue streamé.</summary>
    public static string? LastForgeError { get; private set; }

    /// <summary>Encadre un message CLIENT : résout le code par nom, refuse si code ou un champ manque.</summary>
    private static byte[] Emit(Msg m) => Frame(Code(m.Name), m);

    /// <summary>Variante quand le code est déjà résolu par l'appelant (lui aussi via le mapping).</summary>
    private static byte[] EmitWithCode(string? code, Msg m) => Frame(code, m);

    private static byte[] Frame(string? code, Msg m)
    {
        if (code is null) { LastForgeError = $"code de {m.Name} absent du mapping"; return None; }
        if (!m.Ok) { LastForgeError = m.Error ?? $"un champ de {m.Name} absent du mapping"; return None; }
        LastForgeError = null;
        byte[] gm = EnvelopeBuilder.BuildClientRequest(code, m.Fields, uid: -1);
        return RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
    }

    // ================================ DÉPLACEMENT ================================

    /// <summary>MapMovementRequest { key_cells (repeated int32 PACKÉ), map_id }. key_cells = keyMovements
    /// (cellId | orientation&lt;&lt;12) du MapPathfinder ; le client envoie UN champ packé, l'éclater fait refuser.</summary>
    public static byte[] BuildFramed(long mapId, IReadOnlyList<int> keyCells, bool cautious = false)
    {
        var packed = new List<byte>();
        foreach (var km in keyCells) packed.AddRange(RawProtobuf.EncodeVarint((ulong)(uint)km));
        _ = cautious;   // non transporté sur ce patch (aucun champ observé) ; par nom si un jour nécessaire
        return Emit(new Msg("MapMovementRequest").Bytes("key_cells", packed.ToArray()).Vi("map_id", mapId));
    }

    /// <summary>MapMovementConfirmRequest, corps VIDE. Sans lui, le serveur croit le perso encore en mouvement
    /// et REFUSE le déplacement suivant. À envoyer après l'arrivée (délai ~ durée du trajet).</summary>
    public static byte[] BuildConfirmFramed() => Emit(new Msg("MapMovementConfirmRequest"));

    /// <summary>ReloginRequest, corps VIDE. Capture 2026-10-31 21:43:17 et 21:47:03 : 0 champ. Le client
    /// ferme ensuite la connexion et se reconnecte lui-même. Aucun champ n'est inventé.</summary>
    public static byte[] BuildReloginFramed() => Emit(new Msg("ReloginRequest"));

    /// <summary>MapChangeRequest { map_id } : map_id = carte de DESTINATION (= celle du MapComplementaryInformationEvent
    /// qui suit).</summary>
    public static byte[] BuildMapChangeFramed(long targetMapId)
        => Emit(new Msg("MapChangeRequest").Vi("map_id", targetMapId));

    /// <summary>InteractiveUseRequest { element_id, skill_instance_uid } : clic sur un interactif (ressource, zaap,
    /// coffre…). element_id = m_interactionId ; skill_instance_uid = uid du skill ACTIVÉ (enabled_skills).</summary>
    public static byte[] BuildInteractiveUseFramed(int skillInstanceUid, int elementId)
        => Emit(new Msg("InteractiveUseRequest").Vu("element_id", elementId).Vu("skill_instance_uid", skillInstanceUid));

    /// <summary>InteractiveUseRequest { element_id, [specific_instance_id], skill_instance_uid } — specific_instance_id =
    /// instance de maison (choix par propriétaire, Lot J). 0 -&gt; OMIS (défaut proto3) -&gt; octets IDENTIQUES à la surcharge
    /// à 2 arguments (laissée intacte pour les appelants existants).</summary>
    public static byte[] BuildInteractiveUseFramed(int skillInstanceUid, int elementId, int specificInstanceId)
    {
        var m = new Msg("InteractiveUseRequest").Vu("element_id", elementId);
        if (specificInstanceId != 0) m.Vu("specific_instance_id", specificInstanceId);
        return Emit(m.Vu("skill_instance_uid", skillInstanceUid));
    }

    /// <summary>LockableUseCodeRequest { code } : envoie le code du verrou (coffre / porte de maison) dont le dialogue de
    /// code est ouvert. Le 1er champ est
    /// OMIS = défaut proto3 — À VALIDER EN JEU sur une trame du client manuel.</summary>
    public static byte[] BuildLockableUseCodeFramed(string code)
        => Emit(new Msg("LockableUseCodeRequest").Str("code", code));

    /// <summary>AttackMonsterRequest { monster_group_id } : monster_group_id = l'actor id NÉGATIF du groupe (int64
    /// signé, ex -20004), encodé pleine largeur comme le client. Le serveur approche puis lance le combat.</summary>
    public static byte[] BuildAttackMonsterFramed(long monsterGroupId)
        => Emit(new Msg("AttackMonsterRequest").Vi("monster_group_id", monsterGroupId));

    /// <summary>Répartition complète : budgets investis absolus dans les six stats. Toute stat omise serait remise à zéro.</summary>
    public static byte[] BuildCharacteristicUpgradeFramed(IReadOnlyDictionary<string, int> allocation)
    {
        if (CharacteristicUpgradePolicy.Stats.Any(s => !allocation.TryGetValue(s.Name,out int n) || n < 0)) return None;
        return Emit(new Msg("CharacterCharacteristicUpgradeRequest")
            .Vu("strength",allocation["strength"]).Vu("vitality",allocation["vitality"])
            .Vu("wisdom",allocation["wisdom"]).Vu("chance",allocation["chance"])
            .Vu("agility",allocation["agility"]).Vu("intelligence",allocation["intelligence"]));
    }

    /// <summary>CharacterSelectionRequest { character_id } : choisit le personnage à jouer sur l'écran de sélection (MITM : à la place du clic).</summary>
    public static byte[] BuildCharacterSelectFramed(long characterId)
        => Emit(new Msg("CharacterSelectionRequest").Vi("character_id", characterId));

    /// <summary>CharacterNameSuggestionRequest, corps vide : le serveur propose un nom. Refus si le code n'est pas dans le catalogue.</summary>
    public static byte[] BuildCharacterNameSuggestionFramed()
        => Emit(new Msg("CharacterNameSuggestionRequest"));

    /// <summary>CharacterCreationRequest { modeling_information }. <paramref name="modeling"/> est le sous-message déjà encodé ; un corps vide est refusé.</summary>
    public static byte[] BuildCharacterCreationFramed(byte[] modeling)
    {
        if (modeling is not { Length: > 0 }) { LastForgeError = "modeling_information vide"; return None; }
        return Emit(new Msg("CharacterCreationRequest").Bytes("modeling_information", modeling));
    }
    /// <summary>GameActionFightCastRequest { spell_id, cell } : lance un sort sur une cellule en combat.</summary>
    public static byte[] BuildCastFramed(int cell, int spellId)
        => Emit(new Msg("GameActionFightCastRequest").Vu("spell_id", spellId).Vu("cell", cell));

    /// <summary>FightTurnFinishRequest, corps VIDE : finit/passe mon tour de combat.</summary>
    public static byte[] BuildTurnFinishFramed() => Emit(new Msg("FightTurnFinishRequest"));

    /// <summary>FightTurnReadyRequest { is_ready = 1 } : « tour prêt » (Turbo agressif : envoyé par le bot sans attendre le client).</summary>
    public static byte[] BuildTurnReadyFramed() => Emit(new Msg("FightTurnReadyRequest").Vu("is_ready", 1));

    /// <summary>GameActionAcknowledgementRequest { valid = 1, action_id } : accuse une séquence d'action (Turbo : accusé PAR LE BOT dès la fin de séquence).</summary>
    public static byte[] BuildActionAckFramed(long actionId)
        => Emit(new Msg("GameActionAcknowledgementRequest").Vu("valid", 1).Vu("action_id", actionId));

    /// <summary>FightPlacementPositionRequest { cell_id, actor_id } : se placer avant le combat (actor_id = mon id
    /// de combattant, cell_id = case bleue choisie).</summary>
    public static byte[] BuildPlacementFramed(long entityId, int cell)
        => Emit(new Msg("FightPlacementPositionRequest").Vu("cell_id", cell).Vi("entity_id", entityId));

    /// <summary>FightReadyRequest { is_ready = 1 } : je suis PRÊT → lance le combat. <paramref name="ready"/>=false = « plus
    /// prêt » : corps VIDE (proto3 omet is_ready=0, même motif que FightTurnFinishRequest).</summary>
    public static byte[] BuildReadyFramed(bool ready = true)
        => ready ? Emit(new Msg("FightReadyRequest").Vu("is_ready", 1)) : Emit(new Msg("FightReadyRequest"));

    /// <summary>FightJoinRequest { fight_id, fighter_id } : rejoint un combat EN COURS d'un membre du groupe (auto-join
    /// coordonné d'équipe). fight_id = combat du chef ; fighter_id = id du combattant dont on rejoint le camp (le CHEF).
    /// Calque un bot tiers (GameFightJoinRequestMessage(fightId, teamIdBoss)). Résolu par nom via le mapping.</summary>
    public static byte[] BuildFightJoinFramed(long fightId, long fighterId)
        => Emit(new Msg("FightJoinRequest").Vi("fight_id", fightId).Vi("fighter_id", fighterId));

    // ============================== DÉFIS DE COMBAT (mapping AUTORITATIF) ==============================
    // Séquence du VRAI client (captures 17:40:35 et 19:24:17), noms confirmés par le mapping extrait du client :
    //   C→S ChallengeReadyRequest (VIDE)  → S→C ChallengeProposalEvent → C→S ChallengeSelectionRequest {id}
    //     → C→S ChallengeTargetsRequest {id} (survol) → C→S ChallengeValidateRequest {id} (APPLIQUE) → S→C ChallengeAddEvent
    // Piège levé par le mapping : en 3.6.10.11 la sélection menait par ChallengeModSelectRequest ; en 3.6.11.12 ce
    // message part VIDE à l'entrée du combat — la sélection-avec-id est ChallengeSelectionRequest, la validation
    // ChallengeValidateRequest. Tous portent l'id de challenge au même champ (résolu par nom).

    /// <summary>ChallengeBonusChoiceRequest { challenge_bonus } : oriente le bonus (0 = XP → champ OMIS comme le
    /// client ; 1 = Butin).</summary>
    public static byte[] BuildChallengeBonusFramed(bool loot)
    {
        var m = new Msg("ChallengeBonusChoiceRequest");
        if (loot) m.Vu("challenge_bonus", 1);   // XP = 0 = défaut proto3, omis
        return Emit(m);
    }

    /// <summary>ChallengeSelectionRequest { challenge_id } : pointe une proposition avant de la valider.</summary>
    public static byte[] BuildChallengeSelectFramed(int challengeId)
        => Emit(new Msg("ChallengeSelectionRequest").Vu("challenge_id", challengeId));

    /// <summary>ChallengeValidateRequest { challenge_id } : LE paquet qui applique réellement le challenge.</summary>
    public static byte[] BuildChallengeValidateFramed(int challengeId)
        => Emit(new Msg("ChallengeValidateRequest").Vu("challenge_id", challengeId));

    /// <summary>ChallengeReadyRequest, VIDE : arme le sélecteur → DÉCLENCHE l'envoi des propositions par le serveur
    /// (sinon, en headless, aucune proposition).</summary>
    public static byte[] BuildChallengeRequestFramed() => Emit(new Msg("ChallengeReadyRequest"));

    /// <summary>ChallengeTargetsRequest { challenge_id } : second survol, facultatif.</summary>
    public static byte[] BuildChallengeSelectionFramed(int challengeId)
        => Emit(new Msg("ChallengeTargetsRequest").Vu("challenge_id", challengeId));

    // ================================ BANQUE (dépôt) ================================
    // Ouverture banquier → réponse au dialogue → dépôt. Codes ET champs résolus par nom (le NPC bank-open portait
    // des numéros figés #1/#2 INVERSÉS vs le client : capture réelle imp { npc_map_id #1, npc_action_id #2, npc_id #3 }).

    /// <summary>NpcGenericActionRequest { npc_map_id, npc_action_id, npc_id } : OUVRE le dialogue banquier.
    /// action = 3 (dialogue banque) ; npc_id = sentinelle négative (int64).</summary>
    public static byte[] BuildNpcBankOpenFramed(long npcMapId, long npcId = -20000, int action = 3)
        => Emit(new Msg("NpcGenericActionRequest").Vi("npc_map_id", npcMapId).Vu("npc_action_id", action).Vi("npc_id", npcId));

    /// <summary>NpcDialogReplyRequest { reply_id } : choisit une réponse du dialogue PNJ (1re réponse = accès banque).</summary>
    public static byte[] BuildNpcDialogReplyFramed(int replyId)
        => Emit(new Msg("NpcDialogReplyRequest").Vu("reply_id", replyId));

    /// <summary>ExchangeObjectTransferAllFromInventoryRequest, VIDE : DÉPOSE TOUT le sac (l'équipement porté n'y est
    /// pas ; les kamas ne sont pas touchés).</summary>
    public static byte[] BuildBankDepositAllFramed() => Emit(new Msg("ExchangeObjectTransferAllFromInventoryRequest"));

    /// <summary>ExchangeObjectTransferExistingFromInventoryRequest, VIDE : dépose seulement les objets déjà présents
    /// dans le coffre (stackables).</summary>
    public static byte[] BuildBankDepositExistingFramed() => Emit(new Msg("ExchangeObjectTransferExistingFromInventoryRequest"));

    /// <summary>ExchangeObjectTransferListFromInventoryRequest { objects_uid (repeated int32 PACKÉ) } : dépose la
    /// LISTE d'objets par leurs UIDs.</summary>
    public static byte[] BuildBankDepositListFramed(IReadOnlyList<int> uids)
    {
        var packed = new List<byte>();
        foreach (var u in uids) packed.AddRange(RawProtobuf.EncodeVarint((ulong)(uint)u));
        return Emit(new Msg("ExchangeObjectTransferListFromInventoryRequest").Bytes("objects_uid", packed.ToArray()));
    }

    /// <summary>ExchangeObjectTransferAllToInventoryRequest, VIDE : RETIRE tout le coffre vers le sac (inverse du dépôt tout).</summary>
    public static byte[] BuildBankWithdrawAllFramed() => Emit(new Msg("ExchangeObjectTransferAllToInventoryRequest"));

    /// <summary>ExchangeObjectTransferExistingToInventoryRequest, VIDE : retire du coffre les objets déjà présents dans le sac.</summary>
    public static byte[] BuildBankWithdrawExistingFramed() => Emit(new Msg("ExchangeObjectTransferExistingToInventoryRequest"));

    /// <summary>DialogLeaveRequest, VIDE : FERME le dialogue/coffre banque.</summary>
    public static byte[] BuildDialogLeaveFramed() => Emit(new Msg("DialogLeaveRequest"));

    /// <summary>FreeSoulRequest, VIDE : LIBÈRE l'âme (tombe -&gt; fantôme) après la mort, pour pouvoir rejoindre le Phénix.</summary>
    public static byte[] BuildFreeSoulFramed() => Emit(new Msg("FreeSoulRequest"));

    /// <summary>NpcGenericActionRequest { npc_map_id, npc_action_id, npc_id } : PARLE à un PNJ (action = dialogue). Alias
    /// nommé de la forge banquier (même message) pour clarifier l'intention côté verbes Lua talkNpc.</summary>
    public static byte[] BuildNpcTalkFramed(long npcMapId, long npcId, int action = 3)
        => BuildNpcBankOpenFramed(npcMapId, npcId, action);

    // ================================ SOCIAL (emotes) ================================

    /// <summary>EmotePlayRequest { emote_id } : joue une emote (1 = s'asseoir -&gt; régénération x2). fxra (#1, obfusqué) omis.</summary>
    public static byte[] BuildEmotePlayFramed(int emoteId) => Emit(new Msg("EmotePlayRequest").Vu("emote_id", emoteId));

    // ================================ INVENTAIRE (usage / drop) ================================
    // ⚠️ Le wire ne transporte JAMAIS le GID (id catalogue), UNIQUEMENT l'UID (id d'INSTANCE dans MON sac). La
    // conversion GID→UID se fait côté appelant (InventoryStore) AVANT d'appeler ces forges.

    /// <summary>ObjectUseRequest { object_uid } : UTILISE/ouvre un objet par son UID d'instance.</summary>
    public static byte[] BuildObjectUseFramed(int objectUid)
        => Emit(new Msg("ObjectUseRequest").Vu("object_uid", objectUid));

    /// <summary>ObjectUseMultipleRequest { quantity, object_uid } : utilise `quantity` exemplaires d'un objet (par UID) en UN
    /// paquet. Le 1er champ (nom obfusqué, non résolu) est OMIS -> à valider en jeu ; l'appelant se replie sur la boucle
    /// ObjectUseRequest si l'usage n'a aucun effet.</summary>
    public static byte[] BuildObjectUseMultipleFramed(int objectUid, int quantity)
        => Emit(new Msg("ObjectUseMultipleRequest").Vu("quantity", quantity).Vu("object_uid", objectUid));

    /// <summary>ObjectDropRequest { ObjectUidWithQuantity object } : JETTE `quantity` d'un objet par son UID. Le
    /// sous-message porte { quantity, object_uid } (⚠️ ordre inversé vs naturel — résolu par nom, aucun risque).</summary>
    public static byte[] BuildObjectDropFramed(int objectUid, int quantity)
        => Emit(new Msg("ObjectDropRequest").Sub("object",
            new Msg("ObjectUidWithQuantity").Vu("quantity", quantity).Vu("object_uid", objectUid)));

    /// <summary>ObjectDeleteRequest { object = ObjectUidWithQuantity{ quantity, object_uid } } : DÉTRUIT `quantity` d'un objet
    /// par son UID (≠ drop : l'objet est SUPPRIMÉ, pas posé au sol). Même sous-message que le drop, `object` résolu par nom.</summary>
    public static byte[] BuildObjectDeleteFramed(int objectUid, int quantity)
        => Emit(new Msg("ObjectDeleteRequest").Sub("object",
            new Msg("ObjectUidWithQuantity").Vu("quantity", quantity).Vu("object_uid", objectUid)));

    /// <summary>ObjectSetPositionRequest { position, object_uid, quantity } : équipe (0..15) / déséquipe (63) un objet par UID.
    /// Position 0 (amulette) est écrite en varint explicite 0 : valide, lue comme un champ omis par le serveur.</summary>
    public static byte[] BuildObjectSetPositionFramed(int objectUid, int position, int quantity = 1)
        => Emit(new Msg("ObjectSetPositionRequest").Vu("position", position).Vu("object_uid", objectUid).Vu("quantity", quantity));

    // ================================ QUÊTES ================================

    /// <summary>QuestObjectiveValidationRequest { objective_id, quest_id } (ordre INVERSÉ vs le .proto de réf. — résolu par nom).</summary>
    public static byte[] BuildQuestObjectiveValidationFramed(int questId, int objectiveId)
        => Emit(new Msg("QuestObjectiveValidationRequest").Vu("quest_id", questId).Vu("objective_id", objectiveId));

    /// <summary>QuestStepInformationRequest { quest_id } — le champ #1 (obfusqué, non résolu) est OMIS (défaut proto3).</summary>
    public static byte[] BuildQuestStepInformationFramed(int questId)
        => Emit(new Msg("QuestStepInformationRequest").Vu("quest_id", questId));

    // ================================ CHASSE AU TRÉSOR ================================
    // Champs résolus par NOM (fields.json) ; valeur 0 OMISE comme le vrai client proto3 (le champ doit quand même exister au
    // mapping : Require). Les champs obfusqués non résolus (TreasureHuntDigRequest.flyn, FlagRemoveRequest.flxy) ne sont pas émis.

    private static Msg VuOrOmit(Msg m, string field, long value) => value == 0 ? m.Require(field) : m.Vu(field, value);

    /// <summary>TreasureHuntFlagRequest { index, quest_type } : pose le drapeau n° <paramref name="index"/> (0-based) sur la carte.</summary>
    public static byte[] BuildTreasureFlagFramed(int index, int questType)
        => Emit(VuOrOmit(VuOrOmit(new Msg("TreasureHuntFlagRequest"), "index", index), "quest_type", questType));

    /// <summary>TreasureHuntFlagRemoveRequest { index, quest_type } : retire le drapeau n° <paramref name="index"/>.</summary>
    public static byte[] BuildTreasureFlagRemoveFramed(int index, int questType)
        => Emit(VuOrOmit(VuOrOmit(new Msg("TreasureHuntFlagRemoveRequest"), "index", index), "quest_type", questType));

    /// <summary>TreasureHuntDigRequest { quest_type } : creuse (valide l'étape).</summary>
    public static byte[] BuildTreasureDigFramed(int questType)
        => Emit(VuOrOmit(new Msg("TreasureHuntDigRequest"), "quest_type", questType));

    /// <summary>TreasureHuntGiveUpRequest { quest_type } : abandonne la chasse.</summary>
    public static byte[] BuildTreasureGiveUpFramed(int questType)
        => Emit(VuOrOmit(new Msg("TreasureHuntGiveUpRequest"), "quest_type", questType));

    public static byte[] BuildTreasureLegendaryFramed(int legendaryId)
        => Emit(new Msg("TreasureHuntLegendaryRequest").Vu("legendary_id", legendaryId));

    // ================================ ÉCHANGE (établi de craft / dépôt banque par-uid) ================================
    // Un ÉCHANGE ouvert (coffre banque OU établi de craft) accepte le MÊME message pour poser un objet dedans :
    // ExchangeObjectMoveRequest { quantity #1, object_uid #2 } (numéros INVERSÉS vs le .proto de réf. périmé — fields.json
    // fait foi, capture 2026-09-21). quantity>0 = poser dans l'échange (déposer/mettre en ingrédient) ; on borne l'appelant.

    /// <summary>ExchangeObjectMoveRequest { quantity, object_uid } : pose `quantity` d'un objet (par UID) dans l'échange
    /// ouvert — DÉPÔT banque sélectif ou ingrédient d'établi. GID→UID résolu par l'appelant (InventoryStore).</summary>
    public static byte[] BuildExchangeObjectMoveFramed(int objectUid, int quantity)
        => Emit(new Msg("ExchangeObjectMoveRequest").Vu("quantity", quantity).Vu("object_uid", objectUid));

    /// <summary>ExchangeObjectMoveAndSetReadyRequest { #1 uid, #2 quantité, #4 = 1 (prêt) } : pose UNE rune dans l'atelier de
    /// forgemagie ET valide en un message, exactement comme le client officiel (capture 23/10 : 08 B49A8E02 10 01 20 01). Les noms
    /// de champ du mapping sont obfusqués (#1 « fwsh », #4 « gid ») : seuls leurs NUMÉROS sont prouvés par la capture.</summary>
    public static byte[] BuildExchangeObjectMoveAndSetReadyFramed(int objectUid, int quantity)
        => Emit(new Msg("ExchangeObjectMoveAndSetReadyRequest").Vu("fwsh", objectUid).Vu("quantity", quantity).Vu("gid", 1));

    /// <summary>ExchangeCraftCountRequest { count } : règle le NOMBRE de crafts à réaliser d'un coup (bouton quantité de l'établi).</summary>
    public static byte[] BuildCraftCountFramed(int count)
        => Emit(new Msg("ExchangeCraftCountRequest").Vu("count", count));

    /// <summary>ExchangeReadyRequest { ready = 1, step } : VALIDE le craft (lance la fabrication). step = étape courante
    /// (capture = nb d'ingrédients posés ; passé par l'appelant).</summary>
    public static byte[] BuildCraftReadyFramed(int step)
        => Emit(new Msg("ExchangeReadyRequest").Vu("ready", 1).Vu("step", step));

    // ================================ HÔTEL DE VENTE (ACHAT) ================================
    // Ouverture = InteractiveUseRequest sur l'HDV (S→C ExchangeStartedBidBuyerEvent). Puis, mode achat :
    //   [ExchangeBidHouseTypeRequest {type_id}] → ExchangeBidHouseSearchRequest {object_gid}
    //     → S→C ExchangeTypesItemsExchangerDescriptionForUserEvent {offres : bid_uid + prix par lot}
    //     → ExchangeBidHouseBuyRequest {price, quantity, object_uid=bid_uid}. Champs résolus PAR NOM (fields.json).

    /// <summary>ExchangeBidHouseTypeRequest { type_id, follow=1 } : sélectionne la CATÉGORIE d'objet à parcourir en HDV
    /// (préalable à la recherche d'un GID côté ACHAT). Les forges search/buy existent déjà (variantes à `code`).</summary>
    public static byte[] BuildBidHouseTypeFramed(int typeId, bool follow = true)
    {
        var m = new Msg("ExchangeBidHouseTypeRequest").Vu("type_id", typeId);
        if (follow) m.Vu("follow", 1);
        return Emit(m);
    }

    // ================================ HAVRE-SAC / TÉLÉPORT ================================

    /// <summary>HavenBagEnterRequest { owner } : ENTRE dans le havre-sac (owner = id du perso = SOI). Le code est
    /// résolu par l'appelant (via le mapping) et passé ici.</summary>
    public static byte[] BuildHavenBagEnterFramed(string code, long owner)
        => EmitWithCode(code, new Msg("HavenBagEnterRequest").Vi("owner", owner));

    /// <summary>TeleportRequest { map_id, source_type si ≠0, destination_type si ≠0 }. source_type 0 omis = zaap au sol
    /// (dialogue ouvert) ; 3 = havre-sac ; 5 = carte du monde, sans zaap ouvert. destination_type n'est émis que si ≠0
    /// (zaapi) : les appels zaap au sol et havre-sac gardent les mêmes octets.</summary>
    public static byte[] BuildTeleportFramed(string code, long mapId, int sourceType = 0, int destinationType = 0)
    {
        var m = new Msg("TeleportRequest");
        if (sourceType != 0) m.Vu("source_type", sourceType);
        if (destinationType != 0) m.Vu("destination_type", destinationType);
        m.Vi("map_id", mapId);
        return EmitWithCode(code, m);
    }

    // ================================ MESSAGERIE (MP / canaux) ================================

    /// <summary>ChatPrivateMessageRequest { content, name } : MESSAGE PRIVÉ à `receiverName`. Schéma autoritatif
    /// (content = #2, name = #3) ; l'ancienne forge envoyait content#1/receiver#5, jamais confirmé et contredit
    /// par le schéma. Code résolu par l'appelant.</summary>
    public static byte[] BuildChatPrivateMessageFramed(string code, string receiverName, string content)
        => EmitWithCode(code, new Msg("ChatPrivateMessageRequest").Str("content", content).Str("name", receiverName));

    /// <summary>ChatChannelMessageRequest { channel (si ≠0), content } : message sur un CANAL (0 général → omis).</summary>
    public static byte[] BuildChatChannelMessageFramed(string code, int channel, string content)
    {
        var m = new Msg("ChatChannelMessageRequest");
        if (channel != 0) m.Vu("channel", channel);
        m.Str("content", content);
        return EmitWithCode(code, m);
    }

    // ================================ ÉQUIPE / GROUPE ================================
    // Tout est résolu par NOM via le mapping streamé (code + champs, y compris les sous-messages imbriqués). Si un
    // champ manque du mapping courant, la forge REFUSE (trame vide) plutôt que d'envoyer une trame que le serveur
    // jetterait -> pas de numéro en dur, une MAJ du mapping serveur se propage automatiquement.

    /// <summary>PartyInvitationRequest { target = PlayerSearch{ search_by_character_name = SearchByCharacterName{ name } }, party_type }.
    /// Invite un joueur PAR NOM DE PERSO. party_type = 1 (groupe classique) — CONFIRMÉ par capture (client envoie #2 int 1).</summary>
    public static byte[] BuildPartyInviteFramed(string characterName)
        => Emit(new Msg("PartyInvitationRequest")
            .Sub("target", new Msg("PlayerSearch")
                .Sub("search_by_character_name", new Msg("SearchByCharacterName").Str("name", characterName)))
            .Vu("party_type", 1));

    /// <summary>PartyInvitationAcceptRequest { party_id } : accepte l'invitation reçue.</summary>
    public static byte[] BuildPartyAcceptFramed(long partyId)
        => Emit(new Msg("PartyInvitationAcceptRequest").Vi("party_id", partyId));

    // fqam is undocumented: omit it rather than invent a value or wire type.
    public static byte[] BuildPartyFollowFramed(long playerId, long partyId)
        => Emit(new Msg("PartyFollowMemberRequest").Vi("player_id", playerId).Vu("enabled", 1).Vi("party_id", partyId));

    public static byte[] BuildPartyStopFollowFramed(long playerId, long partyId)
        => Emit(new Msg("PartyStopFollowMemberRequest").Vi("player_id", playerId).Vi("party_id", partyId));

    // The current mapping defines no payload fields for these activation requests.
    public static byte[] BuildAutoFollowActivationFramed() => Emit(new Msg("AutoFollowActivationRequest"));
    public static byte[] BuildFightAutoJoinActivationFramed() => Emit(new Msg("FightAutoJoinActivationRequest"));

    /// <summary>PartyLeaveRequest { party_id } : quitte le groupe.</summary>
    public static byte[] BuildPartyLeaveFramed(long partyId)
        => Emit(new Msg("PartyLeaveRequest").Vi("party_id", partyId));

    /// <summary>ContextKickRequest { target_id } : expulse un combattant du CONTEXTE de combat courant (le chef vire un
    /// intrus non-membre pendant le placement KickCharacter). target_id = actor/character id du combattant.</summary>
    public static byte[] BuildContextKickFramed(long targetId)
        => Emit(new Msg("ContextKickRequest").Vi("target_id", targetId));

    // ================================ ÉCHANGE JOUEUR (trade P2P) ================================
    // Ouverture par ID d'acteur ROLEPLAY du joueur cible sur la carte (résolu par l'appelant via le store d'acteurs).
    // On réutilise le MÊME ExchangeObjectMoveRequest que banque/craft pour poser/retirer un objet, mais en quantité
    // SIGNÉE (+pose, −retire) : la forge banque .Vu ne gère que le dépôt positif. Tout par nom via le mapping streamé
    // (si un champ manque, la forge REFUSE plutôt que d'émettre une trame que le serveur jetterait).

    /// <summary>ExchangePlayerRequest { target_id } : DEMANDE un échange au joueur `targetId` (id d'acteur roleplay sur la carte).</summary>
    public static byte[] BuildExchangePlayerRequestFramed(long targetId)
        => Emit(new Msg("ExchangePlayerRequest").Vi("target_id", targetId));

    /// <summary>ExchangeObjectMoveRequest { quantity SIGNÉE, object_uid } : pose (+) ou retire (−) `quantity` d'un objet (par UID)
    /// dans l'échange ouvert. Variante SIGNÉE de <see cref="BuildExchangeObjectMoveFramed"/> (retrait = quantité négative, encodage proto int32).</summary>
    public static byte[] BuildExchangeObjectMoveSignedFramed(int objectUid, int quantity)
        => Emit(new Msg("ExchangeObjectMoveRequest").Vi("quantity", quantity).Vu("object_uid", objectUid));

    /// <summary>ExchangeMoveKamaRequest { quantity SIGNÉE } : ajoute (+) ou retire (−) des kamas de l'échange. ⚠️ NOM = celui du
    /// mapping AUTORITATIF (matching), PAS l'ancien alias confirmed.json « ExchangeObjectMoveKamaRequest », sinon Code() renvoie null et la forge refuse.</summary>
    public static byte[] BuildExchangeMoveKamaFramed(long qty)
        => Emit(new Msg("ExchangeMoveKamaRequest").Vi("quantity", qty));

    /// <summary>ExchangeReadyRequest { ready, step } : bascule PRÊT (ready=1) / pas prêt (0). step=0 pour un échange joueur
    /// (le compteur d'étape ne sert qu'au craft). Quand les DEUX sont prêts, l'échange se valide.</summary>
    public static byte[] BuildExchangeReadyToggleFramed(bool ready, int step = 0)
        => Emit(new Msg("ExchangeReadyRequest").Vu("ready", ready ? 1 : 0).Vu("step", step));

    /// <summary>ExchangeAcceptRequest, VIDE : ACCEPTE une demande d'échange reçue.</summary>
    public static byte[] BuildExchangeAcceptFramed()
        => Emit(new Msg("ExchangeAcceptRequest"));

    // ================================ HÔTEL DE VENTE (HDV) ================================
    // Tous les numéros sont résolus par nom via fields.json (le schéma autoritatif a corrigé les numéros figés
    // capturés AVANT le patch : la vente/achat/reprice/recherche portaient des #1/#2/#3 périmés). ⚠️ UID : la mise
    // en vente prend l'object_uid du SAC ; reprice/retrait prennent l'uid EXCHANGE (renvoyé par le serveur, change
    // à chaque reprice). Le code est résolu par l'appelant (via le mapping) et passé ici.

    /// <summary>ExchangeObjectMovePricedRequest { quantity, price, object_uid } : MET EN VENTE un objet du sac
    /// (object_uid = uid du SAC ; lot 1/10/100/1000).</summary>
    public static byte[] BuildBidHouseSellFramed(string code, int objectUid, int quantity, long price)
        => EmitWithCode(code, new Msg("ExchangeObjectMovePricedRequest")
            .Vu("quantity", quantity).Vi("price", price).Vu("object_uid", objectUid));

    /// <summary>ExchangeObjectModifyPricedRequest { price, object_uid, quantity } : REPRICE un lot déjà en vente
    /// (object_uid = uid EXCHANGE, dernier renvoyé par le serveur).</summary>
    public static byte[] BuildBidHouseModifyFramed(string code, int exchangeUid, int quantity, long price)
        => EmitWithCode(code, new Msg("ExchangeObjectModifyPricedRequest")
            .Vi("price", price).Vu("object_uid", exchangeUid).Vu("quantity", quantity));

    /// <summary>ExchangeBidHouseBuyRequest { price, quantity, object_uid } : ACHÈTE un lot (object_uid = uid du lot listé).</summary>
    public static byte[] BuildBidHouseBuyFramed(string code, int uid, int quantity, long price)
        => EmitWithCode(code, new Msg("ExchangeBidHouseBuyRequest")
            .Vi("price", price).Vu("quantity", quantity).Vu("object_uid", uid));

    /// <summary>ExchangeObjectMoveRequest { quantity (NÉGATIF), object_uid } : RETIRE un lot de la vente (retour au
    /// sac). `quantity` passé POSITIF, négativé ici (sign-extend comme le client).</summary>
    public static byte[] BuildBidHouseRemoveFramed(string code, int exchangeUid, int quantity)
        => EmitWithCode(code, new Msg("ExchangeObjectMoveRequest")
            .Vi("quantity", -Math.Abs(quantity)).Vu("object_uid", exchangeUid));

    /// <summary>ExchangeBidHouseSearchRequest { follow, object_gid } : sélectionne/cherche un objet par gid (déclenche
    /// listings + prix). ⚠️ ACHAT : le client réel envoie follow=1 (capture 2026-09-21) ; SANS ce champ le serveur
    /// renvoie un jzn VIDE (aucune offre) → l'achat par gid échoue. La lecture prix VENDEUR (kcq) marche sans follow,
    /// d'où le défaut false : follow n'est activé QUE pour l'achat (HdvBuyByGidAsync).</summary>
    public static byte[] BuildBidHouseSearchFramed(string code, int genericId, bool follow = false)
    {
        var m = new Msg("ExchangeBidHouseSearchRequest");
        if (follow) m.Vu("follow", 1);
        return EmitWithCode(code, m.Vu("object_gid", genericId));
    }

    /// <summary>ExchangeBidHousePriceRequest { object_gid } : demande le prix moyen/min d'un objet par gid.</summary>
    public static byte[] BuildMergedInventoryFramed(int context, IReadOnlyList<int> sources)
    {
        var m = new Msg("InventoryUpdateMergedInventoryTypeRequest").Vu("context", context);
        if (sources.Count > 0)
        {
            var packed = new List<byte>();
            foreach (int value in sources)
            {
                ulong u = (ulong)value;
                while (u >= 0x80) { packed.Add((byte)(u | 0x80)); u >>= 7; }
                packed.Add((byte)u);
            }
            m.Bytes("sources", packed.ToArray());
        }
        return Emit(m);
    }

    public static byte[] BuildBidHousePriceFramed(string code, int genericId)
        => EmitWithCode(code, new Msg("ExchangeBidHousePriceRequest").Vu("object_gid", genericId));

    // ================================ LOT E : PETITES FORGES ================================
    // Champs obfusqués (fwmg de jyb, fmbi de hin, fmuq/fmur de hnk) VOLONTAIREMENT omis : sens inconnu, à ne renseigner
    // qu'après capture du vrai client si le serveur ignore la forge.

    /// <summary>FightOptionToggleRequest { option } : BASCULE une option du combat (0 SECRET = pas de spectateurs, 1
    /// PARTY_ONLY, 2 CLOSED, 3 ASK_FOR_HELP). ⚠ bascule : l'envoyer deux fois déverrouille (l'appelant rend idempotent).
    /// option 0 = omise en proto3, mais le champ doit rester résolu (Require).</summary>
    public static byte[] BuildFightOptionToggleFramed(int option)
    {
        var m = new Msg("FightOptionToggleRequest");
        if (option != 0) m.Vu("option", option); else m.Require("option");
        return Emit(m);
    }

    // ---------------------------------------------------------------- lot M (succès, téléportation, donjon)

    /// <summary>AchievementRewardRequest { achievement_id } : réclame la récompense d'un succès.</summary>
    public static byte[] BuildAchievementRewardFramed(int achievementId)
        => Emit(new Msg("AchievementRewardRequest").Vu("achievement_id", achievementId));

    /// <summary>Réponse à une proposition de téléportation : kind = player | group | buddy | buddies ; accept faux = champ omis (proto3).
    /// Les champs obfusqués non nommés des réponses « group » et « buddies » ne sont PAS émis (valeurs inconnues).</summary>
    public static byte[] BuildTeleportAnswerFramed(string kind, bool accept)
    {
        // Un littéral par message (et non un nom calculé) : le contrôle de contrat lit les noms de message dans le code source.
        Msg m;
        switch (kind)
        {
            case "player": m = new Msg("TeleportPlayerAnswerRequest"); if (accept) m.Vu("accept", 1); else m.Require("accept"); break;
            case "group": m = new Msg("GroupTeleportPlayerAnswerRequest"); if (accept) m.Vu("accept", 1); else m.Require("accept"); break;
            case "buddy": m = new Msg("TeleportToBuddyAnswerRequest"); if (accept) m.Vu("accept", 1); else m.Require("accept"); break;
            case "buddies": m = new Msg("TeleportBuddiesAnswerRequest"); if (accept) m.Vu("accept", 1); else m.Require("accept"); break;
            default: return Array.Empty<byte>();
        }
        return Emit(m);
    }

    /// <summary>Achat en boutique de PNJ. PREUVE : capture passive du client officiel 3.7.11.11 (2026-10-16) : <c>ExchangeBuyRequest {#1=quantité #2=identifiant d'objet (gid)}</c>,
    /// deux achats de quantité 1 sur les objets que la boutique venait de lister (ExchangeNpcShopStartedEvent), chacun confirmé par ExchangeBoughtEvent.
    /// fwwc (#1) = quantité, fwwd (#2) = gid ; le #3 (fwwe) n'a jamais été vu (valeur par défaut) donc n'est pas émis. Une quantité > 1 dans UNE requête n'a pas été observée.</summary>
    public static byte[] BuildNpcBuyFramed(int gid, int quantity)
        => Emit(new Msg("ExchangeBuyRequest").Vu("fwwc", quantity).Vu("fwwd", gid));

    /// <summary>Vente en boutique de PNJ. PREUVE : même capture : <c>ExchangeObjectsSellRequest {#1: {#1=identifiant de pile #2=quantité}}</c> (message ExchangeObject), quantité 1,
    /// deux ventes, chacune confirmée par ExchangeSoldEvent. Ce n'est PAS ExchangeSellRequest. Plusieurs piles dans une requête, ou une quantité > 1, n'ont pas été observées.</summary>
    public static byte[] BuildNpcSellFramed(IEnumerable<(int Uid, int Quantity)> stacks)
    {
        var m = new Msg("ExchangeObjectsSellRequest");
        foreach (var (uid, qty) in stacks) m.Sub("objects", new Msg("ExchangeObject").Vu("object_uid", uid).Vu("quantity", qty));
        return Emit(m);
    }

    /// <summary>QuestStartRequest { quest_id } : démarre une quête (le champ obfusqué suivant n'est pas émis, valeur inconnue).</summary>
    public static byte[] BuildQuestStartFramed(int questId) => Emit(new Msg("QuestStartRequest").Vu("quest_id", questId));

    /// <summary>ObjectAveragePricesHistoryRequest { object_gid } : historique du prix moyen d'un objet.</summary>
    public static byte[] BuildPriceHistoryFramed(int gid) => Emit(new Msg("ObjectAveragePricesHistoryRequest").Vu("object_gid", gid));

    /// <summary>DungeonExitRequest, VIDE : quitte le donjon (comme le bouton de sortie du jeu).</summary>
    public static byte[] BuildDungeonExitFramed() => Emit(new Msg("DungeonExitRequest"));

    /// <summary>ZaapRespawnSaveRequest, VIDE : sauvegarde le point de respawn sur le zaap OUVERT.</summary>
    public static byte[] BuildZaapRespawnSaveFramed() => Emit(new Msg("ZaapRespawnSaveRequest"));

    /// <summary>GuildInvitationAnswerRequest { accepted } : refus = accepted false (OMIS, proto3 ; le champ doit exister au
    /// mapping). Le champ obfusqué non résolu n'est pas émis.</summary>
    public static byte[] BuildGuildInvitationAnswerFramed(bool accepted)
        => Emit(accepted ? new Msg("GuildInvitationAnswerRequest").Vu("accepted", 1) : new Msg("GuildInvitationAnswerRequest").Require("accepted"));

    /// <summary>PlayerFightFriendlyAnswerRequest { accept, fight_id } : répond à un défi reçu (refus = accept omis).</summary>
    public static byte[] BuildFriendlyDuelAnswerFramed(long fightId, bool accept)
    {
        var m = new Msg("PlayerFightFriendlyAnswerRequest").Vi("fight_id", fightId);
        if (accept) m.Vu("accept", 1); else m.Require("accept");
        return Emit(m);
    }

    /// <summary>SpellVariantActivationRequest { spell_id } : active la variante `spellId` (hors combat).</summary>
    public static byte[] BuildSpellVariantActivationFramed(int spellId)
        => Emit(new Msg("SpellVariantActivationRequest").Vu("spell_id", spellId));

    /// <summary>ContextQuitRequest, VIDE : quitte le contexte de combat (abandon). À n'envoyer QU'EN combat.</summary>
    public static byte[] BuildContextQuitFramed() => Emit(new Msg("ContextQuitRequest"));

    // ===================== Lot K : confort =====================

    /// <summary>MapInformationRequest { map_id } : redemande au serveur la carte COURANTE (réponse :
    /// MapComplementaryInformationEvent). int64 -> Vi.</summary>
    public static byte[] BuildMapInformationRequestFramed(long mapId)
        => Emit(new Msg("MapInformationRequest").Vi("map_id", mapId));

    /// <summary>PlayerStatusUpdateRequest { status: CharacterStatus { status } }. Valeurs: 4 = privé, 1 =
    /// disponible. Le champ #1 obfusqué de CharacterStatus est OMIS (non résolu par nom).</summary>
    public static byte[] BuildPlayerStatusFramed(int status)
        => Emit(new Msg("PlayerStatusUpdateRequest").Sub("status", new Msg("CharacterStatus").Vu("status", status)));

    /// <summary>ResetCharacterCharacteristicsRequest, VIDE : réinitialise les caractéristiques (points rendus).</summary>
    public static byte[] BuildResetCharacteristicsFramed() => Emit(new Msg("ResetCharacterCharacteristicsRequest"));

    /// <summary>MapChangeOrientationRequest { direction } : le client tourne son propre personnage.
    /// <paramref name="direction"/> est l'échelle 0-7 déjà portée par <c>EntityDisposition.direction</c> et
    /// <see cref="MapActor.Direction"/> (fields.json : le seul champ clair est <c>direction</c>). Hors 0-7, ou si le
    /// mapping ne résout pas le message : trame vide, rien n'est inventé.</summary>
    public static byte[] BuildMapChangeOrientationFramed(int direction)
    {
        if (direction is < 0 or > 7) { LastForgeError = "MapChangeOrientationRequest.direction hors 0-7"; return None; }
        return Emit(new Msg("MapChangeOrientationRequest").Vu("direction", direction));
    }
}
