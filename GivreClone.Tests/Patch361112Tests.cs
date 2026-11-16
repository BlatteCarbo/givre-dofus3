using System.Text;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Régressions métier historiques 3.6 transposées en fixtures SYNTHÉTIQUES à partir de
/// data/fields.json (3.7.11.11). Ce ne sont pas les octets des captures anciennes, et
/// leur passage ne certifie pas un comportement en jeu.
/// </summary>
public class Patch361112Tests
{
    private static readonly ProtocolCatalog Current = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Current.FieldNumber(message, field)
        ?? throw new Xunit.Sdk.XunitException($"Mapping 3.7 absent : {message}.{field}");
    // --- encodeur protobuf minimal (tag + valeur), pour rejouer une trame octet pour octet ---------
    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var b = new byte[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }

    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }

    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] S(int field, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }

    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    private static byte[] DetailedCharacteristic(int charId, int baseValue, int equipment, int context = 0, int used = 0) =>
        M(F("FightCharacteristics", "characteristics"),
            M(F("CharacterCharacteristic", "usable"),
                I(F("CharacterCharacteristicDetailedUsable", "base"), baseValue),
                I(F("CharacterCharacteristicDetailedUsable", "objects_and_mount_bonus"), equipment),
                context != 0 ? I(F("CharacterCharacteristicDetailedUsable", "context_modification"), context) : Array.Empty<byte>(),
                used != 0 ? I(F("CharacterCharacteristicDetailedUsable", "used"), used) : Array.Empty<byte>()),
            charId != 0 ? I(F("CharacterCharacteristic", "characteristic_id"), charId) : Array.Empty<byte>());

    // =============================================================== inventaire (isb / isa / isf)
    [Fact]
    public void Inventory_snapshot_is_read_after_the_patch()
    {
        // isb 17:06:29.646 : { kamas #1, objets #2 { position #3, item #5 { uid #1, qty #2, effets #3, gid #5 } } }
        var tree = Parse(
            I(F("InventoryContentEvent", "kamas"), 54156032),
            M(F("InventoryContentEvent", "objects"),
                I(F("ObjectItemInventory", "position"), 3),
                M(F("ObjectItemInventory", "item"), I(F("ObjectItem", "uid"), 18207351),
                    I(F("ObjectItem", "quantity"), 1), I(F("ObjectItem", "gid"), 6831))),
            M(F("InventoryContentEvent", "objects"),
                I(F("ObjectItemInventory", "position"), 6),
                M(F("ObjectItemInventory", "item"), I(F("ObjectItem", "uid"), 18207352),
                    I(F("ObjectItem", "quantity"), 1), I(F("ObjectItem", "gid"), 6834))));

        Assert.Equal(54156032, InventoryReader.ReadKamas(tree));
        var items = InventoryReader.ReadContent(tree);
        Assert.Equal(2, items.Count);                       // le bot annoncait "0 stacks"
        Assert.Equal(18207351, items[0].Uid);
        Assert.Equal(6831, items[0].Gid);
        Assert.Equal(1, items[0].Qty);
        Assert.Equal(3, items[0].Position);
    }

    [Fact]
    public void Object_added_and_quantity_are_read_after_the_patch()
    {
        // isa 17:08:18.577 (butin de fin de combat) : gid 31518, confirme par le bilan jwe.
        var added = InventoryReader.ReadAdded(Parse(
            M(F("ObjectAddedEvent", "object"),
                I(F("ObjectItemInventory", "position"), 63),
                M(F("ObjectItemInventory", "item"), I(F("ObjectItem", "uid"), 18299708),
                    I(F("ObjectItem", "quantity"), 1), I(F("ObjectItem", "gid"), 31518)))));
        Assert.NotNull(added);
        Assert.Equal(18299708, added!.Value.Uid);
        Assert.Equal(31518, added.Value.Gid);

        // isf 17:08:18.611 : { #3 { quantite #2, uid #3 } } -- exactement l'inverse de 3.6.10.
        var q = InventoryReader.ReadQuantity(Parse(
            M(F("ObjectQuantityEvent", "object"),
                I(F("ObjectUidWithQuantity", "quantity"), 2),
                I(F("ObjectUidWithQuantity", "object_uid"), 18299710))));
        Assert.Equal((18299710, 2), q);
    }

    // =============================================================== acteurs de carte (jpo / jon)
    [Fact]
    public void Map_actors_player_npc_and_monster_group_are_classified()
    {
        byte[] Actor(int cell, int direction, long id, byte[] role) =>
            M(F("MapComplementaryInformationEvent", "actors"),
                M(F("ActorPositionInformation", "actor_information"),
                    M(F("ActorInformation", "role_play_actor"), role)),
                I(F("ActorPositionInformation", "actor_id"), id),
                M(F("ActorPositionInformation", "disposition"),
                    I(F("EntityDisposition", "cell_id"), cell),
                    I(F("EntityDisposition", "direction"), direction)));
        byte[] Creature(int gid, int level) =>
            M(F("MonsterGroupStaticInformation", "underlings"),
                I(F("MonsterInGroupInformation", "gid"), gid),
                I(F("MonsterInGroupInformation", "level"), level));
        var tree = Parse(
            Actor(396, 1, 41234567890,
                M(F("RolePlayActor", "named_actor"), S(F("NamedActor", "name"), "Persoun"))),
            Actor(302, 3, -20000,
                M(F("RolePlayActor", "npc_actor"), I(F("NpcStaticInformation", "npc_id"), 522))),
            Actor(284, 5, -20007,
                M(F("RolePlayActor", "monster_group_actor"),
                    M(F("MonsterGroupActor", "identification"),
                        M(F("MonsterGroupStaticInformation", "main_creature"),
                            I(F("MonsterInGroupInformation", "gid"), 490),
                            I(F("MonsterInGroupInformation", "level"), 13)),
                        Creature(491, 13), Creature(493, 15)))));
        var actors = MapActorsReader.Extract(tree,
            F("MapComplementaryInformationEvent", "actors"), g => "Monstre" + g, n => "Pnj" + n);
        Assert.Equal(3, actors.Count);

        Assert.Equal(396, actors[0].CellId);
        Assert.Equal(1, actors[0].Direction);
        Assert.Equal(41234567890L, actors[0].ActorId);
        Assert.Equal("Persoun", actors[0].Name);
        Assert.Equal(MapActorKind.Player, actors[0].Kind);

        Assert.Equal(302, actors[1].CellId);
        Assert.Equal(MapActorKind.Npc, actors[1].Kind);
        Assert.Equal("Pnj522", actors[1].Name);   // nom résolu SEUL (plus de « (id) » accolé)
        Assert.Equal(522, actors[1].NpcId);   // id de MODÈLE exposé (compat tiers npc:npc(522, …))
        Assert.Equal(0, actors[0].NpcId);

        Assert.Equal(284, actors[2].CellId);
        Assert.Equal(-20007L, actors[2].ActorId);
        Assert.Equal(MapActorKind.Monster, actors[2].Kind);
        Assert.NotNull(actors[2].Monsters);
        Assert.Equal(3, actors[2].Monsters!.Sum(m => m.Count));      // 490, 491, 493
    }

    [Fact]
    public void Map_actor_treasure_hunt_hint_is_classified_with_its_npc_id()
    {
        var tree = Parse(M(F("MapComplementaryInformationEvent", "actors"),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "role_play_actor"),
                    I(F("RolePlayActor", "treasure_hunt_npc_id"), 2587))),
            I(F("ActorPositionInformation", "actor_id"), -3),
            M(F("ActorPositionInformation", "disposition"),
                I(F("EntityDisposition", "direction"), 1),
                I(F("EntityDisposition", "cell_id"), 210))));
        var actors = MapActorsReader.Extract(tree,
            F("MapComplementaryInformationEvent", "actors"), null, n => "Indice" + n);
        Assert.Single(actors);
        Assert.Equal(MapActorKind.TreasureHint, actors[0].Kind);
        Assert.Equal(2587, actors[0].NpcId);
        Assert.Equal("Indice2587", actors[0].Name);   // nom résolu seul (id de modèle dans NpcId)
        Assert.Equal(210, actors[0].CellId);
    }

    // =============================================================== perso (ksw)
    [Fact]
    public void Character_list_name_level_and_id_are_read()
    {
        // ksw 17:06:26.458 : entree #1 { infos #2 { #1 { look #3, classe #6 }, niveau #4, nom #5 }, id #3 }
        byte[] Entry(long id, string name, int level, int breed) =>
            M(F("CharacterListEvent", "characters"),
                M(F("Character", "character_basic_information"),
                    M(F("CharacterBasicInformation", "character_look"),
                        I(F("CharacterLook", "breed_id"), breed)),
                    I(F("CharacterBasicInformation", "level"), level),
                    S(F("CharacterBasicInformation", "name"), name)),
                I(F("Character", "id"), id));
        var tree = Parse(Entry(41234567890, "Persoun", 132, 17),
                         Entry(2412345678, "Persozero", 204, 9));

        var all = CharacterInfoReader.ExtractAll(tree);
        Assert.Equal(2, all.Count);
        Assert.Equal(41234567890L, all[0].Id);
        Assert.Equal("Persoun", all[0].Info.Name);
        Assert.Equal(132, all[0].Info.Level);
        Assert.Equal("Persozero", all[1].Info.Name);
        Assert.Equal(204, all[1].Info.Level);
        Assert.Equal(41234567890L, CharacterInfoReader.ExtractCharacterId(tree));
    }

    // Sexe : CharacterLook.base_information (#7) -> CharacterBaseInformation.gender (#4). Capture 2026-09-19 : Persoun (femme)
    // porte character_look = { ..., breed_id #6 = 17, base_information #7 = { #4 = 1 } }. Sans base_information -> Homme (0).
    [Fact]
    public void Character_gender_is_read_from_base_information()
    {
        byte[] Entry(long id, string name, int level, int breed, bool female) =>
            M(F("CharacterListEvent", "characters"),
                M(F("Character", "character_basic_information"),
                    M(F("CharacterBasicInformation", "character_look"),
                        I(F("CharacterLook", "breed_id"), breed),
                        female ? M(F("CharacterLook", "base_information"),
                            I(F("CharacterBaseInformation", "gender"), 1)) : Array.Empty<byte>()),
                    I(F("CharacterBasicInformation", "level"), level),
                    S(F("CharacterBasicInformation", "name"), name)),
                I(F("Character", "id"), id));
        var tree = Parse(Entry(41234567890, "Persoun", 134, 17, true),
                         Entry(2412345678, "Persozero", 204, 9, false));

        var all = CharacterInfoReader.ExtractAll(tree);
        Assert.Equal("Huppermage", all[0].Info.Breed);
        Assert.Equal("Femme", all[0].Info.Gender);
        Assert.Equal(1, all[0].Info.GenderIndex);
        Assert.Equal("Homme", all[1].Info.Gender);
        Assert.Equal(0, all[1].Info.GenderIndex);
    }

    // =============================================================== sorts (hnl)
    [Fact]
    public void Spell_ids_and_grades_are_read()
    {
        // hnl 17:06:30.807 : repeated #1 { #1, spell_id #2, grade #5 }
        var tree = Parse(
            M(F("SpellsEvent", "human_spells"),
                I(F("SpellItem", "available"), 1), I(F("SpellItem", "spell_id"), 13667),
                I(F("SpellItem", "spell_level"), 2)),
            M(F("SpellsEvent", "human_spells"),
                I(F("SpellItem", "available"), 1), I(F("SpellItem", "spell_id"), 13677),
                I(F("SpellItem", "spell_level"), 1)),
            I(F("SpellsEvent", "spell_visualisation"), 1));

        var lv = CharacterSpellsReader.ReadLearnedSpellLevels(tree);
        Assert.Equal(2, lv[13667]);
        Assert.Equal(1, lv[13677]);
    }

    // =============================================================== combat : PA/PM et tour
    [Fact]
    public void Action_points_are_not_over_evaluated_anymore()
    {
        // jxh 17:07:58.562, apres trois sorts a 3 PA : total 4+7 = 11, dépensé 9 -> il reste 2.
        // L'ancienne regle (somme brute) renvoyait 11 : les PA etaient sur-evalues de tout le depense.
        var tree = Parse(I(F("FightRefreshCharacterStatsEvent", "fighter_id"), 41234567890),
            M(F("FightRefreshCharacterStatsEvent", "stats"),
                DetailedCharacteristic(FightStatsReader.IdPa, baseValue: 7, equipment: 4, context: -9, used: 9)));
        var r = Assert.Single(FightStatsReader.ReadCharacteristicUpdates(tree));
        Assert.Equal(41234567890L, r.Actor);
        Assert.Equal(FightStatsReader.IdPa, r.CharId);
        Assert.Equal(2, r.Value);

        // jxh 17:07:44.474 : PM total 1+3 = 4, quatre cases parcourues -> 0 restant.
        var pm = Assert.Single(FightStatsReader.ReadCharacteristicUpdates(
            Parse(I(F("FightRefreshCharacterStatsEvent", "fighter_id"), 41234567890),
                M(F("FightRefreshCharacterStatsEvent", "stats"),
                    DetailedCharacteristic(FightStatsReader.IdPm, baseValue: 3, equipment: 1, context: -4, used: 4)))));
        Assert.Equal(0, pm.Value);
    }

    [Fact]
    public void Turn_owner_is_read_from_its_own_field()
    {
        // jwd : { round #3, duree #5, rang #6, combattant #7 }
        var mine = Parse(I(F("FightTurnEvent", "round"), 1),
            I(F("FightTurnEvent", "remaining_time"), 360),
            I(F("FightTurnEvent", "character_id"), 41234567890));
        var theirs = Parse(I(F("FightTurnEvent", "round"), 1),
            I(F("FightTurnEvent", "remaining_time"), 290),
            I(F("FightTurnEvent", "turn_index"), 2),
            I(F("FightTurnEvent", "character_id"), -4));
        Assert.True(FightStatsReader.IsFightTurnOf(mine, 41234567890));
        Assert.False(FightStatsReader.IsFightTurnOf(theirs, 41234567890));
    }

    [Fact]
    public void Show_fighter_stats_and_team_are_read()
    {
        // juo 17:06:55.768 (moi) : { #1 { information #1 { look #2, combattant #3 { spawn #2, stats #3, innerId #7 } },
        //                                fighterId #2, disposition #3 { equipe.. cellule #4 } } }
        var me = Parse(M(F("FightFighterShowEvent", "information"),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "fighter"),
                    M(F("FightFighterInformation", "spawn_information"),
                        I(F("SpawnInformation", "alive"), 1)),
                    M(F("FightFighterInformation", "stats"),
                        DetailedCharacteristic(FightStatsReader.IdPa, 7, 4),
                        DetailedCharacteristic(FightStatsReader.IdPm, 3, 1),
                        DetailedCharacteristic(FightStatsReader.IdVitality, 100, 1539),
                        DetailedCharacteristic(34, 20, 5),
                        DetailedCharacteristic(FightStatsReader.IdShield, 0, 350),
                        DetailedCharacteristic(FightStatsReader.IdLife, 710, 0)),
                    I(F("FightFighterInformation", "character_id"), 41234567890))),
            I(F("ActorPositionInformation", "actor_id"), 41234567890),
            M(F("ActorPositionInformation", "disposition"),
                I(F("EntityDisposition", "direction"), 3),
                I(F("EntityDisposition", "cell_id"), 272))));

        var st = FightStatsReader.ReadShowFighter(me);
        Assert.NotNull(st);
        Assert.Equal(41234567890L, st!.Value.FighterId);
        Assert.Equal(11, st.Value.Pa);
        Assert.Equal(4, st.Value.Pm);
        Assert.Equal(2349, st.Value.MaxLife);            // 710 + 1639, la valeur annoncee par le serveur
        Assert.Equal(25, st.Value.Characteristics[34]);
        Assert.Equal(350, st.Value.Characteristics[FightStatsReader.IdShield]);
        Assert.Equal(11, st.Value.Characteristics[FightStatsReader.IdPa]);
        Assert.Equal(25, (st.Value with { Life = 1000 }).Characteristics[34]);
        Assert.Equal(272, FightStatsReader.ReadShowFighterCell(me));

        // camp : piege proto3 intact -- team 0 (moi, l'agresseur) est OMISE, team 1 (monstres) est presente.
        Assert.Equal((41234567890L, 0), FightStatsReader.ReadFighterTeam(me));
    }

    [Fact]
    public void Team_roster_is_read()
    {
        // jym 17:06:55.167 : { #1 { membres #2 [ #2 { id #4, { nom #2, niveau #3 } #5 } ], meneur #5, camp #8 } }
        var tree = Parse(M(F("FightTeamUpdateEvent", "team"),
            M(F("FightTeamInformation", "team_members"),
                M(F("FightTeamMembersInformation", "team_members"),
                    I(F("FightTeamMemberInformation", "member_id"), 41234567890),
                    M(F("FightTeamMemberInformation", "character_member"),
                        S(F("FightTeamMemberCharacter", "name"), "Persoun"),
                        I(F("FightTeamMemberCharacter", "level"), 132)))),
            I(F("FightTeamInformation", "leader_id"), 41234567890)),
            I(F("FightTeamUpdateEvent", "fight_id"), 102));
        var ids = FightStatsReader.ReadTeamMemberIds(tree);
        Assert.Contains(41234567890L, ids);
    }

    // Régression du POISON D'ALLIÉ (log 13:05) : en approchant le combat d'un AUTRE joueur, on reçoit son roster kae.
    // Sa forme détaillée contient une 2e entrée { #3 { rang, id, niveau }, #4 = -1 } dont le #4 = -1 est un MARQUEUR,
    // PAS un membre -> ReadTeamMemberIds le sort quand même. L'INVARIANT qui protège du poison : ce roster étranger ne
    // contient JAMAIS MON charId. Le handler décide « mon équipe » par `members.Contains(_charId)` (JAMAIS un repli
    // positionnel type max-des-positifs, qui prenait le meneur étranger 44123456789 pour « moi » et latchait le -1 en
    // allié -> monstre -1 jamais ciblé -> 0 ennemi -> tours passés). Ici : mon charId absent -> pas mon équipe -> sûr.
    [Fact]
    public void Foreign_team_roster_does_not_contain_my_char_id()
    {
        // Roster de « Beurette » (44123456789) tel que capturé, avec l'entrée parasite #4 = -1.
        var foreign = Parse(M(1,
            M(2,
                M(2, I(4, 44123456789), M(5, M(1, S(2, "LCD")), S(2, "Beurette"), I(3, 241))),
                M(2, M(3, I(3, 55), I(4, 44123456789), I(6, 200)), I(4, -1))),
            I(5, 44123456789), I(8, 1)), I(2, 1425));
        var ids = FightStatsReader.ReadTeamMemberIds(foreign);
        Assert.DoesNotContain(41234567890L, ids);   // <- l'invariant : ce roster n'est PAS le mien (Fix A ne l'empoisonne pas)
    }

    [Fact]
    public void Death_and_fight_dispositions_are_read()
    {
        Assert.Equal(-5L, MapFightReader.ReadKilledFighter(Parse(
            I(F("GameActionFightEvent", "source_id"), 41234567890),
            M(F("GameActionFightEvent", "death"), I(F("Death", "target_id"), -5)))));
        // La poussée ne doit PAS être interprétée comme la mort.
        Assert.Null(MapFightReader.ReadKilledFighter(Parse(
            M(F("GameActionFightEvent", "slide"),
                I(F("Slide", "start_cell"), 328), I(F("Slide", "end_cell"), 343),
                I(F("Slide", "target_id"), 41234567890)))));

        // kkr : entrees repetees #1 { acteur #1, direction #2, cellule #4 }
        var pos = MapFightReader.ReadDispositions(Parse(
            M(F("EntitiesDispositionEvent", "dispositions"),
                I(F("EntityDisposition", "entity_id"), 41234567890),
                I(F("EntityDisposition", "direction"), 3),
                I(F("EntityDisposition", "cell_id"), 204)),
            M(F("EntitiesDispositionEvent", "dispositions"),
                I(F("EntityDisposition", "entity_id"), -1),
                I(F("EntityDisposition", "direction"), 5),
                I(F("EntityDisposition", "cell_id"), 386))));
        Assert.Equal(2, pos.Count);
        Assert.Equal((204, 41234567890L), pos[0]);
        Assert.Equal((386, -1L), pos[1]);
    }

    // Régression du RE-PLACEMENT (log 12:26) : après un re-placement en phase de placement, EntitiesDispositionEvent
    // (kkr) porte ma VRAIE case (373). Le lecteur DOIT la sortir. Le bug était le CÂBLAGE : le handler visait
    // FightSynchronizeEvent (jul), dont les combattants sont au champ #3 IMBRIQUÉ ({ #2 actorId, #3{ #4 cell } }) que
    // ReadDispositions (répété #1) ne lit PAS -> 0 combattant -> SelfCell restait sur la case du juo initial (285) ->
    // tirs refusés « vous visez à 9 ». Ce test fige que kkr donne 373, et qu'une trame jul ne donne rien ici.
    [Fact]
    public void Fight_positions_come_from_kkr_shape_not_jul_shape()
    {
        // kkr réel du log : moi (41234567890) case 373, ennemi (-1) case 270.
        var kkr = MapFightReader.ReadDispositions(Parse(
            M(F("EntitiesDispositionEvent", "dispositions"),
                I(F("EntityDisposition", "entity_id"), -1),
                I(F("EntityDisposition", "direction"), 1),
                I(F("EntityDisposition", "cell_id"), 270)),
            M(F("EntitiesDispositionEvent", "dispositions"),
                I(F("EntityDisposition", "entity_id"), 41234567890),
                I(F("EntityDisposition", "direction"), 5),
                I(F("EntityDisposition", "cell_id"), 373))));
        Assert.Equal(373, kkr.Single(x => x.ActorId == 41234567890L).Cell);
        Assert.Equal(270, kkr.Single(x => x.ActorId == -1L).Cell);

        // Forme jul (FightSynchronizeEvent) : combattants au #3, case imbriquée en #3.#4 -> ReadDispositions n'en tire
        // RIEN (c'est pour ça que l'ancien câblage sur jul laissait la position périmée). On ne lit QUE kkr.
        var jul = MapFightReader.ReadDispositions(Parse(
            M(3, I(2, 41234567890), M(3, I(4, 373))),
            M(3, I(2, -1), M(3, I(4, 270)))));
        Assert.Empty(jul);

        // Piège proto3 : un combattant sur la case 0 (coin) OMET cell_id (#4). Il ne doit PAS être jeté (sinon le
        // full-replace le fait disparaître) -> case 0. entity_id présent (#1), aucun #4.
        var cell0 = MapFightReader.ReadDispositions(Parse(
            M(F("EntitiesDispositionEvent", "dispositions"),
                I(F("EntityDisposition", "entity_id"), 41234567890))));
        Assert.Equal((0, 41234567890L), cell0.Single());
    }

    // VÉRITÉ = matching_current.json + fields.json (04/10). Le pont de confirmed.json renommait 9 codes (juo -> GameFightShowFighter
    // au lieu de FightFighterShowEvent…) et ses sections _fields/_aliases n'étaient PAS appliquées par la prod : un handler ou
    // une lecture marchait en test et pas en prod, ou l'inverse selon le confirmed.json servi. Désormais matching fait foi aussi
    // en RÉCEPTION : un confirmed.json (même périmé, même servi par le serveur) ne renomme plus rien.
    [Fact]
    public void Stale_confirmed_cannot_rename_a_code_of_the_matching()
    {
        const string matching = "{ \"type.ankama.com/juo\": \"MessageType.FightFighterShowEvent\","
            + " \"type.ankama.com/kkg\": \"MessageType.ContextReadyRequest\","
            + " \"type.ankama.com/jop\": \"MessageType.MapInformationRequest\" }";
        // L'ancien pont exact (servi par le serveur jusqu'au 04/10) + un code absent du matching.
        const string confirmed = "{ \"juo\": \"GameFightShowFighter\", \"kkg\": \"MapInformationRequest\", \"zzz\": \"OnlyInConfirmed\" }";

        var cat = ProtocolCatalog.LoadFromStrings(null, matching, confirmed, null);

        Assert.Equal("FightFighterShowEvent", cat.NameForCode("juo"));   // matching gagne en réception
        Assert.Equal("ContextReadyRequest", cat.NameForCode("kkg"));     // plus de collision avec MapInformationRequest
        Assert.Equal("jop", cat.CodeForName("MapInformationRequest"));   // la forge vise le vrai code
        Assert.Equal("kkg", cat.CodeForName("ContextReadyRequest"));
        Assert.Equal("OnlyInConfirmed", cat.NameForCode("zzz"));         // confirmed ne fait que COMBLER un code inconnu
    }

    [Fact]
    public void Confirmed_field_sections_are_ignored()
    {
        const string fields = "{ \"Slide\": { \"start_cell\": 1, \"end_cell\": 2, \"target_id\": 4 }, \"MapMovementRequest\": { \"map_id\": 9 } }";
        const string confirmed = "{ \"_aliases\": { \"GameActionFightSlideEvent\": \"Slide\" },"
            + " \"_fields\": { \"MapMovementRequest\": { \"map_id\": 3 }, \"EntityDisposition\": { \"cell_id\": 4 } } }";

        var cat = ProtocolCatalog.LoadFromStrings(null, null, confirmed, fields);

        Assert.Null(cat.FieldNumber("GameActionFightSlideEvent", "start_cell"));   // plus d'alias de message
        Assert.Equal(9, cat.FieldNumber("MapMovementRequest", "map_id"));          // fields.json n'est plus écrasé
        Assert.Null(cat.FieldNumber("EntityDisposition", "cell_id"));             // plus de pin hors fields.json
        Assert.Equal(1, cat.FieldNumber("Slide", "start_cell"));
    }

    // Forge-safety ("son mapping = le nôtre") : matching_current.json (v4) fait AUTORITÉ pour nom->code (le chemin
    // d'ÉMISSION via CodeForName). Un confirmed.json PÉRIMÉ ne peut plus réassigner un nom déjà lié par v4 à un code
    // DIFFÉRENT -> le bug du dépôt banque (confirmed inversait la paire transfert All<->Existing) est structurellement
    // impossible, même si l'override périmé était encore présent.
    [Fact]
    public void Stale_confirmed_cannot_hijack_the_forge_code_of_a_v4_name()
    {
        // v4 (vérité) : jzb = TransferAll, kdu = TransferExisting.
        const string matching = "{ \"type.ankama.com/jzb\": \"MessageType.ExchangeObjectTransferAllFromInventoryRequest\","
            + " \"type.ankama.com/kdu\": \"MessageType.ExchangeObjectTransferExistingFromInventoryRequest\" }";
        // confirmed PÉRIMÉ : l'inversion exacte qui cassait le dépôt banque (jzb<->kdu).
        const string confirmed = "{ \"jzb\": \"ExchangeObjectTransferExistingFromInventoryRequest\","
            + " \"kdu\": \"ExchangeObjectTransferAllFromInventoryRequest\" }";

        var cat = ProtocolCatalog.LoadFromStrings(null, matching, confirmed, null);

        // Le chemin forge (CodeForName) suit v4, PAS l'override périmé -> "dépôt tout" repart bien avec jzb.
        Assert.Equal("jzb", cat.CodeForName("ExchangeObjectTransferAllFromInventoryRequest"));
        Assert.Equal("kdu", cat.CodeForName("ExchangeObjectTransferExistingFromInventoryRequest"));
    }

    // Regression du defi CASSE : ReadChallengeProposalIds lisait la liste au #2 (= timer) et l'id au #2 (= targets) ->
    // renvoyait TOUJOURS vide. Correct (fields.json) : challenge_proposals=1 (repeated Challenge), Challenge.challenge_id=5.
    // Le catalogue est cable par TestSetup -> ce test valide le chemin catalogue (numeros resolus par nom).
    [Fact]
    public void Challenge_proposals_are_read_from_field_1_and_id_5()
    {
        // ChallengeProposalEvent : challenge_proposals[] = #1 { challenge_id #5 } ; timer = #2.
        var ids = FightPrepReader.ReadChallengeProposalIds(Parse(
            M(F("ChallengeProposalEvent", "challenge_proposals"),
                I(F("Challenge", "challenge_id"), 55),
                M(F("Challenge", "targets"), I(F("ChallengeTarget", "target_id"), 12))),
            M(F("ChallengeProposalEvent", "challenge_proposals"),
                I(F("Challenge", "challenge_id"), 60)),
            I(F("ChallengeProposalEvent", "timer"), 30)));
        Assert.Equal(new[] { 55, 60 }, ids);
    }
}

// ===================================================================================================
// CHEMIN EMISSION : ce que le bot ENVOIE vraiment sur le fil.
//
// Les 22 codes 3 lettres figes dans MoveForge etaient TOUS morts apres le patch (0/22 present dans
// confirmed.json), ET le slot GameRequest de l enveloppe est passe de #2 a #1. Les deux defauts sont
// independants et chacun suffit a rendre le bot totalement inerte, sans la moindre trace dans les
// journaux. Ces tests decodent les OCTETS REELLEMENT PRODUITS, pas l intention du code.
// ===================================================================================================
public class ForgeEmissionTests
{
    // Le catalogue REEL est branche pour tout l'assembly par TestSetup (module initializer) : les forges resolvent
    // donc code + champs depuis le mapping livre, exactement comme en prod.
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Code(string name) => Mapping.CodeForName(name)
        ?? throw new InvalidOperationException($"Message {name} absent du mapping livré");

    private static int Field(string message, string name) => Mapping.FieldNumber(message, name)
        ?? throw new InvalidOperationException($"Champ {message}.{name} absent du mapping livré");

    // Retire le prefixe varint(len) et rend l arbre du GameMessage.
    private static IReadOnlyList<ProtoField> Frame(byte[] framed)
    {
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);          // le prefixe annonce EXACTEMENT le reste
        return RawProtobuf.Parse(framed[pos..]);
    }

    // GameMessage -> (code du type_url, charge utile du message de jeu).
    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
    {
        var gm = Frame(framed);
        // Le oneof GameMessage a tourné en 3.7.11.11 : seuls les slots du mapping livré font foi.
        var requestSlot = Field("GameMessage", "request");
        var eventSlot = Field("GameMessage", "event");
        var req = gm.FirstOrDefault(f => f.Field == requestSlot && f.Message is not null)?.Message;
        Assert.NotNull(req);
        Assert.DoesNotContain(gm, f => f.Field == eventSlot);
        var any = req!.FirstOrDefault(f => f.Field == Field("Request", "content") && f.Message is not null)?.Message;
        Assert.NotNull(any);
        var url = any!.FirstOrDefault(f => f.Field == 1)?.String;
        Assert.NotNull(url);
        Assert.StartsWith("type.ankama.com/", url!);
        var payload = any.FirstOrDefault(f => f.Field == 2 && f.Message is not null)?.Message
                      ?? new List<ProtoField>();
        return (url!["type.ankama.com/".Length..], payload);
    }

    private static long V(IReadOnlyList<ProtoField> p, int field)
        => p.First(f => f.Field == field && f.Wire == WireType.Varint).AsSignedVarint;

    [Fact]
    public void Request_goes_into_the_new_envelope_slot()
    {
        var (code, payload) = Request(MoveForge.BuildReadyFramed());
        Assert.Equal(Code("FightReadyRequest"), code);
        Assert.Equal(1, V(payload, Field("FightReadyRequest", "is_ready")));
    }

    [Fact]
    public void Cast_sends_spell_then_cell()
    {
        // Le forgeur doit écrire les valeurs dans les champs de GameActionFightCastRequest du mapping livré.
        var (code, p) = Request(MoveForge.BuildCastFramed(cell: 343, spellId: 13721));
        Assert.Equal(Code("GameActionFightCastRequest"), code);
        Assert.Equal(13721, V(p, Field("GameActionFightCastRequest", "spell_id")));
        Assert.Equal(343, V(p, Field("GameActionFightCastRequest", "cell")));
    }

    [Fact]
    public void Movement_sends_cells_then_map()
    {
        // L'index du champ map_id varie entre versions; le champ key_cells reste packé.
        var (code, p) = Request(MoveForge.BuildFramed(191104008, new[] { 100, 200 }));
        Assert.Equal(Code("MapMovementRequest"), code);
        Assert.Equal(191104008, V(p, Field("MapMovementRequest", "map_id")));
        var cells = p.First(f => f.Field == Field("MapMovementRequest", "key_cells") && f.Wire == WireType.Len);
        Assert.NotNull(cells.Bytes);
    }

    [Fact]
    public void Placement_sends_cell_at_2_and_actor_at_5()
    {
        var (code, p) = Request(MoveForge.BuildPlacementFramed(41234567890L, 204));
        Assert.Equal(Code("FightPlacementPositionRequest"), code);
        Assert.Equal(204, V(p, Field("FightPlacementPositionRequest", "cell_id")));
        Assert.Equal(41234567890L, V(p, Field("FightPlacementPositionRequest", "entity_id")));
    }

    [Fact]
    public void Turn_ready_request_carries_is_ready_true()
    {
        var ready = Request(MoveForge.BuildTurnReadyFramed());
        Assert.Equal(Code("FightTurnReadyRequest"), ready.Code);
        Assert.Equal(1L, V(ready.Payload, Field("FightTurnReadyRequest", "is_ready")));
    }

    [Fact]
    public void Turn_finish_and_confirm_are_empty_but_correctly_addressed()
    {
        var turn = Request(MoveForge.BuildTurnFinishFramed());
        Assert.Equal(Code("FightTurnFinishRequest"), turn.Code);
        Assert.Empty(turn.Payload);
        var confirm = Request(MoveForge.BuildConfirmFramed());
        Assert.Equal(Code("MapMovementConfirmRequest"), confirm.Code);
        Assert.Empty(confirm.Payload);
    }

    [Fact]
    public void Map_change_object_use_and_havenbag_use_field_one()
    {
        var mc = Request(MoveForge.BuildMapChangeFramed(191104002));
        Assert.Equal(Code("MapChangeRequest"), mc.Code);
        Assert.Equal(191104002, V(mc.Payload, Field("MapChangeRequest", "map_id")));

        var ou = Request(MoveForge.BuildObjectUseFramed(14852379));
        Assert.Equal(Code("ObjectUseRequest"), ou.Code);
        Assert.Equal(14852379, V(ou.Payload, Field("ObjectUseRequest", "object_uid")));

        // Le havre-sac partait avec owner en #2 : injection "OK" mais rien ne s ouvrait cote serveur.
        var hb = Request(MoveForge.BuildHavenBagEnterFramed(Code("HavenBagEnterRequest"), 41234567890L));
        Assert.Equal(Code("HavenBagEnterRequest"), hb.Code);
        Assert.Equal(41234567890L, V(hb.Payload, Field("HavenBagEnterRequest", "owner")));
    }

    [Fact]
    public void Object_drop_forges_the_nested_uid_quantity()
    {
        // ObjectDropRequest résolu via le mapping : { object = ObjectUidWithQuantity { quantity, object_uid } }.
        var r = Request(MoveForge.BuildObjectDropFramed(objectUid: 14852379, quantity: 5));
        Assert.Equal(Code("ObjectDropRequest"), r.Code);
        var obj = r.Payload.Single(f => f.Field == Field("ObjectDropRequest", "object") && f.Message is not null).Message!;
        Assert.Equal(5, V(obj, Field("ObjectUidWithQuantity", "quantity")));
        Assert.Equal(14852379, V(obj, Field("ObjectUidWithQuantity", "object_uid")));
    }


    // ====================================================== defis (kty / kuh / kuf / kul)
    [Fact]
    public void Challenge_sequence_replays_the_real_client_exchange()
    {
        // Tous les codes et numéros proviennent du catalogue livré (ils tournent entre builds).
        Assert.Equal(Code("ChallengeReadyRequest"), Request(MoveForge.BuildChallengeRequestFramed()).Code);
        Assert.Empty(Request(MoveForge.BuildChallengeRequestFramed()).Payload!);

        var sel = Request(MoveForge.BuildChallengeSelectFramed(35));
        Assert.Equal(Code("ChallengeSelectionRequest"), sel.Code);
        Assert.Equal(35, V(sel.Payload, Field("ChallengeSelectionRequest", "challenge_id")));

        var val = Request(MoveForge.BuildChallengeValidateFramed(35));
        Assert.Equal(Code("ChallengeValidateRequest"), val.Code);
        Assert.Equal(35, V(val.Payload, Field("ChallengeValidateRequest", "challenge_id")));

        // En XP (enum 0), le champ par défaut est omis par proto3.
        var loot = Request(MoveForge.BuildChallengeBonusFramed(true));
        Assert.Equal(Code("ChallengeBonusChoiceRequest"), loot.Code);
        Assert.Equal(1, V(loot.Payload, Field("ChallengeBonusChoiceRequest", "challenge_bonus")));
        Assert.Empty(Request(MoveForge.BuildChallengeBonusFramed(false)).Payload!);
    }

    // ====================================================== interactifs (iva)
    [Fact]
    public void Interactive_use_request_sends_element_then_skill()
    {
        // Le mapping 3.7 définit les champs de l'interactif; leur numérotation est variable entre versions.
        var r = Request(MoveForge.BuildInteractiveUseFramed(skillInstanceUid: 22990, elementId: 513989));
        Assert.Equal(Code("InteractiveUseRequest"), r.Code);
        var elementField = Field("InteractiveUseRequest", "element_id");
        var skillField = Field("InteractiveUseRequest", "skill_instance_uid");
        Assert.Equal(513989, V(r.Payload, elementField));
        Assert.Equal(22990, V(r.Payload, skillField));
        Assert.Equal(new[] { elementField, skillField }.OrderBy(x => x).ToArray(),
            r.Payload.Select(f => f.Field).OrderBy(x => x).ToArray());
    }
}

/// <summary>
/// Interactifs, elements a etat et zaaps : la couche qui a casse EN SILENCE au patch (plus aucune ressource
/// sur la carte, « 0 destinations connues » a chaque zaap). Trames rejouees depuis le log du 2026-09-08 19:23.
/// </summary>
public class InteractivesAndZaapTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Mapping.FieldNumber(message, field)
        ?? throw new InvalidOperationException($"Missing protocol field {message}.{field}");
    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var b = new byte[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }

    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }

    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }

    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    [Fact]
    public void Map_interactives_are_read_at_field_eight_after_the_patch()
    {
        // The same semantic events, encoded with the 3.7.11.11 field numbers.
        var tree = Parse(
            I(F("MapComplementaryInformationEvent", "map_id"), 191104004),
            I(F("MapComplementaryInformationEvent", "subarea_id"), 95),
            M(F("MapComplementaryInformationEvent", "interactive_elements"),
                I(F("InteractiveElement", "element_type_id"), 33),
                I(F("InteractiveElement", "on_current_map"), 1),
                I(F("InteractiveElement", "element_id"), 513989),
                M(F("InteractiveElement", "disabled_skills"),
                    I(F("InteractiveElementSkill", "skill_instance_uid"), 22990),
                    I(F("InteractiveElementSkill", "skill_id"), 39))),
            M(F("MapComplementaryInformationEvent", "interactive_elements"),
                I(F("InteractiveElement", "element_type_id"), 313),
                I(F("InteractiveElement", "on_current_map"), 1),
                I(F("InteractiveElement", "element_id"), 515220),
                M(F("InteractiveElement", "enabled_skills"),
                    I(F("InteractiveElementSkill", "name_id"), 51),
                    I(F("InteractiveElementSkill", "skill_instance_uid"), 22989),
                    I(F("InteractiveElementSkill", "skill_id"), 355))),
            M(F("MapComplementaryInformationEvent", "stated_elements"),
                I(F("StatedElement", "on_current_map"), 1),
                I(F("StatedElement", "state"), 1),
                I(F("StatedElement", "element_id"), 513989),
                I(F("StatedElement", "cell_id"), 450)),
            M(F("MapComplementaryInformationEvent", "stated_elements"),
                I(F("StatedElement", "on_current_map"), 1),
                I(F("StatedElement", "element_id"), 515220),
                I(F("StatedElement", "cell_id"), 108)));

        var list = MapInteractivesReader.Extract(tree);
        Assert.Equal(2, list.Count);                        // le bot n en trouvait plus AUCUN

        var epuise = list.Single(x => x.ElementId == 513989);
        Assert.Equal(450, epuise.CellId);                   // #4 : cell et state avaient ECHANGE de place
        Assert.Equal(33, epuise.ElementTypeId);
        Assert.Equal(1, epuise.State);
        Assert.False(epuise.Harvestable);                   // skill range en disabled_skills

        var dispo = list.Single(x => x.ElementId == 515220);
        Assert.Equal(108, dispo.CellId);
        Assert.Equal(313, dispo.ElementTypeId);
        Assert.True(dispo.Harvestable);
        Assert.Equal(22989, dispo.GatherSkill!.SkillInstanceUid);
        Assert.Equal(355, dispo.GatherSkill!.SkillId);
    }

    [Fact]
    public void A_harvested_element_moves_its_skill_from_enabled_to_disabled()
    {
        // Same skill moves from enabled_skills to disabled_skills.
        var avant = MapInteractivesReader.ReadElementSkillsUpdate(
            Parse(M(F("InteractiveElementUpdatedEvent", "interactive_element"),
                I(F("InteractiveElement", "element_type_id"), 33),
                I(F("InteractiveElement", "on_current_map"), 1),
                I(F("InteractiveElement", "element_id"), 513989),
                M(F("InteractiveElement", "enabled_skills"),
                    I(F("InteractiveElementSkill", "skill_instance_uid"), 22990),
                    I(F("InteractiveElementSkill", "skill_id"), 39)))));
        var apres = MapInteractivesReader.ReadElementSkillsUpdate(
            Parse(M(F("InteractiveElementUpdatedEvent", "interactive_element"),
                I(F("InteractiveElement", "element_type_id"), 33),
                I(F("InteractiveElement", "on_current_map"), 1),
                I(F("InteractiveElement", "element_id"), 513989),
                M(F("InteractiveElement", "disabled_skills"),
                    I(F("InteractiveElementSkill", "skill_instance_uid"), 22990),
                    I(F("InteractiveElementSkill", "skill_id"), 39)))));

        Assert.Equal(513989, avant!.Value.ElementId);
        Assert.True(avant.Value.Skills.Single().Enabled);
        Assert.Equal(39, avant.Value.Skills.Single().SkillId);
        Assert.Equal(513989, apres!.Value.ElementId);
        Assert.False(apres.Value.Skills.Single().Enabled);
    }

    [Fact]
    public void Stated_element_update_reads_the_cell_and_not_the_state()
    {
        var apres = MapInteractivesReader.ReadStatedUpdate(Parse(
            M(F("StatedElementUpdatedEvent", "stated_element"),
                I(F("StatedElement", "state"), 2),
                I(F("StatedElement", "element_id"), 513989),
                I(F("StatedElement", "cell_id"), 450))));
        Assert.Equal((450, 513989, 2), apres);

        var dispo = MapInteractivesReader.ReadStatedUpdate(Parse(
            M(F("StatedElementUpdatedEvent", "stated_element"),
                I(F("StatedElement", "element_id"), 513989),
                I(F("StatedElement", "cell_id"), 450))));
        Assert.Equal((450, 513989, 0), dispo);              // proto3 : state 0 = champ ABSENT
    }

    [Fact]
    public void Teleport_destinations_are_read_from_field_one()
    {
        int destinations = F("TeleportDestinationsEvent", "destinations");
        int mapId = F("TeleportDestination", "map_id");
        var tree = Parse(
            M(destinations, I(mapId, 88085249), I(F("TeleportDestination", "cost"), 490)),
            M(destinations, I(mapId, 20973313), I(F("TeleportDestination", "cost"), 469)),
            M(destinations, I(mapId, 88212481), I(F("TeleportDestination", "cost"), 170)),
            I(F("TeleportDestinationsEvent", "type"), 3),
            I(F("TeleportDestinationsEvent", "spawn_map_id"), 88085249));

        var (maps, spawn) = TeleportReader.ReadDestinations(tree);
        Assert.Equal(3, maps.Count);
        Assert.Contains(20973313L, maps);
        Assert.Equal(88085249L, spawn);
    }
}
