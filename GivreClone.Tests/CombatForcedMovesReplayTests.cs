using System.Text;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Régression combat du 04/10 : les sous-messages de GameActionFightEvent étaient lus sous leurs noms Dofus 2
/// (GameActionFightSlideEvent, GameActionFightDeathEvent, GameActionFightLifePointsLost, GameActionFightPointsVariation),
/// qui ne tenaient que par les _aliases de confirmed.json, absents du mapping streamé. En prod : poussées jamais appliquées
/// (mob resté à sa vieille case), morts jamais détectées, PV et PA/PM des cibles jamais décomptés. Les tests tournaient
/// avec les _aliases (TestSetup chargeait confirmed.json complet) et ne voyaient rien.
///
/// Ces tests sont des scénarios synthétiques dérivés de fields.json 3.7.11.11, pas un rejeu
/// octet pour octet d'anciennes captures 3.6. Les assertions de jeu sont conservées.
/// </summary>
public class CombatForcedMovesReplayTests
{
    private const long Me = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string msg, string field) => Catalog.FieldNumber(msg, field)
        ?? throw new Xunit.Sdk.XunitException($"Mapping 3.7 absent : {msg}.{field}");

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
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));
    private static byte[] S(int field, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }

    [Fact]
    public void Selected_character_is_read_by_names_only()
    {
        // CharacterSelectionEvent.success (capture socket 24/09) : { character #1 { basic #2 { look #1, niveau #4, nom #5 }, id #3 } }.
        // Le type imbriqué Success n'a pas d'entrée propre dans fields.json : on lit le sous-message en forme de Character.
        var success = Parse(M(F("CharacterSelectionEvent", "success"),
            M(F("Character", "character_basic_information"),
                M(F("CharacterBasicInformation", "character_look"), I(F("CharacterLook", "breed_id"), 6)),
                I(F("CharacterBasicInformation", "level"), 8),
                S(F("CharacterBasicInformation", "name"), "Persoquatre")),
            I(F("Character", "id"), 623456789012)));
        var info = CharacterInfoReader.ExtractSelected(success);
        Assert.NotNull(info);
        Assert.Equal("Persoquatre", info!.Name);
        Assert.Equal(8, info.Level);
        // Id du perso sélectionné : seule trace d'une sélection forcée (reconnexion directement en combat, 24/10 17:01).
        Assert.Equal(623456789012L, CharacterInfoReader.ExtractSelectedId(success));
    }

    [Fact]
    public void Selected_id_rejects_missing_negative_and_unrelated_ids()
    {
        var basic = M(F("Character", "character_basic_information"), S(F("CharacterBasicInformation", "name"), "Testeur"));
        Assert.Equal(0, CharacterInfoReader.ExtractSelectedId(Parse()));
        Assert.Equal(0, CharacterInfoReader.ExtractSelectedId(Parse(M(1, basic))));
        Assert.Equal(0, CharacterInfoReader.ExtractSelectedId(Parse(M(1, basic, I(F("Character", "id"), -1)))));
        Assert.Equal(0, CharacterInfoReader.ExtractSelectedId(Parse(M(1, I(F("Character", "id"), 42)))));
    }

    [Fact]
    public void Push_moves_the_monster_to_its_new_cell()
    {
        // 01:22:01.247 : sort sur 381, le Craqueboule (-1) est poussé de 425 à 396 (#14 = 5, sous-message #4).
        byte[] Slide(int from, int to) => M(F("GameActionFightEvent", "slide"),
            I(F("Slide", "start_cell"), from), I(F("Slide", "end_cell"), to),
            I(F("Slide", "target_id"), -1));
        byte[] Source() => I(F("GameActionFightEvent", "source_id"), Me);
        byte[] Action() => I(F("GameActionFightEvent", "action_id"), 5);
        var slide = MapFightReader.ReadSlide(Parse(Slide(425, 396), Source(), Action()));
        Assert.Equal((-1L, 425, 396), slide);
        // 01:20:55.186 : même sort, poussé de 303 à 274.
        Assert.Equal((-1L, 303, 274), MapFightReader.ReadSlide(Parse(Slide(303, 274), Source(), Action())));
        // Une poussée n'est PAS une mort.
        Assert.Null(MapFightReader.ReadKilledFighter(Parse(Slide(425, 396), Source(), Action())));
    }

    // Forme RÉELLE du serveur (capture 23/10 18:05:27) : slide { #1 cible, #2 cell (départ), #3 end_cell }, sans start_cell.
    [Fact]
    public void Real_slide_without_start_cell_is_applied()
    {
        byte[] Real(long who, int from, int to) => M(F("GameActionFightEvent", "slide"),
            I(F("Slide", "target_id"), who), I(F("Slide", "cell"), from), I(F("Slide", "end_cell"), to));
        Assert.Equal((33202504034L, 188, 174), MapFightReader.ReadSlide(Parse(Real(33202504034, 188, 174),
            I(F("GameActionFightEvent", "action_id"), 5))));
        Assert.Equal((-1L, 233, 220), MapFightReader.ReadSlide(Parse(Real(-1, 233, 220), I(F("GameActionFightEvent", "source_id"), Me))));
    }

    [Fact]
    public void Death_is_detected()
    {
        // jue #14 = 103, cible au sous-message death #23.
        Assert.Equal(-5L, MapFightReader.ReadKilledFighter(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            I(F("GameActionFightEvent", "action_id"), 103),
            M(F("GameActionFightEvent", "death"), I(F("Death", "target_id"), -5)))));
    }

    [Fact]
    public void Life_loss_is_read()
    {
        // 01:22:01.247 : #14 = 91, life_points_lost #25 { loss 285, element 3, permanent 28, cible -1 }.
        var loss = FightStatsReader.ReadLifeLoss(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            I(F("GameActionFightEvent", "action_id"), 91),
            M(F("GameActionFightEvent", "life_points_lost"),
                I(F("LifePointsLost", "loss"), 285), I(F("LifePointsLost", "element_id"), 3),
                I(F("LifePointsLost", "permanent_damages"), 28), I(F("LifePointsLost", "target_id"), -1))));
        Assert.Equal((-1L, 285), loss);
    }

    [Fact]
    public void Action_and_movement_point_variations_are_read()
    {
        // 01:22:01.247 : #14 = 102 (PA), points_variation #12 { delta -4, cible moi }.
        Assert.Equal((Me, true, -4), FightStatsReader.ReadPointVariation(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            M(F("GameActionFightEvent", "points_variation"),
                I(F("PointsVariation", "delta"), -4), I(F("PointsVariation", "target_id"), Me)),
            I(F("GameActionFightEvent", "action_id"), 102))));
        // 01:21:59.446 : #14 = 129 (PM), { delta -1, cible moi }.
        Assert.Equal((Me, false, -1), FightStatsReader.ReadPointVariation(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            M(F("GameActionFightEvent", "points_variation"),
                I(F("PointsVariation", "delta"), -1), I(F("PointsVariation", "target_id"), Me)),
            I(F("GameActionFightEvent", "action_id"), 129))));
    }

    // Formes sans capture sous la main : numéros du schéma v5 (Com.Ankama…GameActionFightEvent+Types+*).
    [Fact]
    public void Teleport_throw_drop_and_reveal_relocate_the_fighter()
    {
        // teleport_on_same_map #29 { target_id #1, cell #2 }
        byte[] Relocation(string carrier, string sub, long id, int? cell) =>
            M(F("GameActionFightEvent", carrier),
                I(F(sub, "target_id"), id),
                cell is int c ? I(F(sub, "cell"), c) : Array.Empty<byte>());
        byte[] Source() => I(F("GameActionFightEvent", "source_id"), Me);
        Assert.Equal((-3L, 212, "TeleportOnSameMap"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("teleport_on_same_map", "TeleportOnSameMap", -3, 212))));
        // throw_character #26 { cell #1, target_id #2 }
        Assert.Equal((-2L, 300, "ThrowCharacter"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("throw_character", "ThrowCharacter", -2, 300))));
        // drop_character #8 { fvht #1 (msg), cell #2, target_id #3 }
        Assert.Equal((-2L, 301, "DropCharacter"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("drop_character", "DropCharacter", -2, 301))));
        // carry_character #5 { cell #1, target_id #2 } : le porté rejoint la case de son porteur
        Assert.Equal((-4L, 260, "CarryCharacter"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("carry_character", "CarryCharacter", -4, 260))));
        // invisible_detected #34 { cell #1, target_id #2 }
        Assert.Equal((-7L, 150, "InvisibleDetected"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("invisible_detected", "InvisibleDetected", -7, 150))));
        // proto3 : une case 0 est OMISE de la trame, pas inconnue.
        Assert.Equal((-3L, 0, "TeleportOnSameMap"), MapFightReader.ReadRelocation(Parse(Source(), Relocation("teleport_on_same_map", "TeleportOnSameMap", -3, null))));
        // Une poussée ou une perte de PV n'est pas une relocalisation.
        Assert.Null(MapFightReader.ReadRelocation(Parse(M(F("GameActionFightEvent", "slide"),
            I(F("Slide", "start_cell"), 425), I(F("Slide", "end_cell"), 396), I(F("Slide", "target_id"), -1)),
            I(F("GameActionFightEvent", "action_id"), 5))));
        Assert.Null(MapFightReader.ReadRelocation(Parse(M(F("GameActionFightEvent", "life_points_lost"),
            I(F("LifePointsLost", "loss"), 285), I(F("LifePointsLost", "target_id"), -1)))));
    }

    // Forme de la rune Huppermage observée le 01/11 (mark_type 4, une case 261), puis son retrait.
    [Theory]
    [InlineData(4)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(20)]
    public void Named_fighter_breed_is_read_without_using_a_ui_class_index(int breed)
    {
        var tree = Parse(M(F("FightFighterShowEvent", "information"),
            I(F("ActorPositionInformation", "actor_id"), Me),
            M(F("ActorPositionInformation", "actor_information"),
                M(F("ActorInformation", "fighter"),
                    M(F("FightFighterInformation", "named_fighter"),
                        M(F("NamedFighterInformation", "character_information"),
                            I(F("FightCharacterInformation", "breed_id"), breed)))))));

        Assert.Equal((Me, breed), FightStatsReader.ReadShowFighterBreed(tree));
        Assert.Null(FightStatsReader.ReadShowFighterBreed(Parse(
            M(F("FightFighterShowEvent", "information"), I(F("ActorPositionInformation", "actor_id"), Me)))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Official_mark_active_field_is_eight_independent_of_the_other_boolean(bool active)
    {
        // lec.MergeFrom C04D10: tag 0x40 -> +0x48; fyj.bijg reads +0x48 into the mark constructor.
        var placed = MapFightReader.ReadMark(Parse(
            M(F("GameActionFightEvent", "mark_cells"), M(F("MarkCells", "mark"),
                I(17, 2), I(F("FightMark", "author_id"), Me),
                I(5, active ? 0 : 1), I(8, active ? 1 : 0),
                M(F("FightMark", "cells"), I(F("MarkedCell", "gask"), 261))))));

        Assert.NotNull(placed);
        Assert.Equal(active, placed.Value.Active);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mark_placement_and_removal_are_decoded(bool active)
    {
        var placed = MapFightReader.ReadMark(Parse(
            M(F("GameActionFightEvent", "mark_cells"),
                M(F("MarkCells", "mark"),
                    I(17, 2),
                    I(F("FightMark", "mark_type"), 4), I(F("FightMark", "author_id"), Me),
                    active ? I(F("FightMark", "active"), 1) : Array.Empty<byte>(),
                    M(F("FightMark", "cells"), I(F("MarkedCell", "gask"), 261)))),
            I(F("GameActionFightEvent", "source_id"), Me)));
        Assert.NotNull(placed);
        Assert.Equal(2, placed.Value.Id);
        Assert.Equal((Me, 4), (placed.Value.Author, placed.Value.Type));
        Assert.Equal(active, placed.Value.Active);
        Assert.Equal(new[] { 261 }, placed.Value.Cells);
        Assert.Equal(2, MapFightReader.ReadUnmark(Parse(M(F("GameActionFightEvent", "unmark_cells"), I(F("UnmarkCells", "mark_id"), 2)))));
        Assert.Null(MapFightReader.ReadMark(Parse(M(F("GameActionFightEvent", "unmark_cells"), I(F("UnmarkCells", "mark_id"), 2)))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mark_activation_is_distinct_from_placement_and_removal(bool active)
    {
        var payload = Parse(M(F("GameActionFightEvent", "activate_glyph_trap"),
            I(F("ActivateGlyphTrap", "mark_id"), 19),
            active ? I(F("ActivateGlyphTrap", "active"), 1) : Array.Empty<byte>()));

        Assert.Equal((19, active), MapFightReader.ReadMarkActivation(payload));
        Assert.Null(MapFightReader.ReadMark(payload));
        Assert.Null(MapFightReader.ReadUnmark(payload));
        Assert.Null(MapFightReader.ReadMarkActivation(Parse(
            M(F("GameActionFightEvent", "unmark_cells"), I(F("UnmarkCells", "mark_id"), 19)))));
    }

    [Fact]
    public void Mark_registry_tracks_only_confirmed_identity_and_preserves_previous_snapshots()
    {
        var marks = new FightMarks();
        IReadOnlyList<ProtoField> Placement(int id, int cell) => Parse(
            M(F("GameActionFightEvent", "mark_cells"), M(F("MarkCells", "mark"),
                I(17, id), I(F("FightMark", "author_id"), Me),
                I(F("FightMark", "mark_type"), 1),
                M(F("FightMark", "cells"), I(F("MarkedCell", "gask"), cell)))));
        IReadOnlyList<ProtoField> Activation(int id, bool active) => Parse(
            M(F("GameActionFightEvent", "activate_glyph_trap"), I(F("ActivateGlyphTrap", "mark_id"), id),
                active ? I(F("ActivateGlyphTrap", "active"), 1) : Array.Empty<byte>()));
        marks.Observe(Activation(19, true));
        Assert.Empty(marks.Snapshot);
        marks.Observe(Placement(19, 261));
        marks.Observe(Placement(20, 262));
        var before = marks.Snapshot;

        marks.Observe(Activation(19, true));

        Assert.True(marks.Snapshot[19].Active);
        Assert.False(before[19].Active);
        Assert.False(marks.Snapshot[20].Active);
        Assert.Equal(Me, marks.Snapshot[19].Author);
        Assert.Equal(new[] { 261 }, marks.Snapshot[19].Cells);
        marks.Observe(Activation(19, false));
        Assert.False(marks.Snapshot[19].Active);
        marks.Observe(Placement(19, 263));
        Assert.Equal(2, marks.Snapshot.Count);
        Assert.Equal(new[] { 263 }, marks.Snapshot[19].Cells);
        marks.Observe(Parse(M(F("GameActionFightEvent", "unmark_cells"), I(F("UnmarkCells", "mark_id"), 19))));
        Assert.Equal(20, Assert.Single(marks.Snapshot).Key);
        Assert.Equal(2, before.Count);
    }

    [Fact]
    public void Exchange_positions_gives_caster_and_target_cells()
    {
        // exchange_positions #39 { caster_cell_id #1, target_id #2, target_cell_id #3 }, lanceur = source_id #11.
        Assert.Equal((Me, 396, -1L, 381), MapFightReader.ReadExchangePositions(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            M(F("GameActionFightEvent", "exchange_positions"),
                I(F("ExchangePositions", "caster_cell_id"), 396),
                I(F("ExchangePositions", "target_id"), -1),
                I(F("ExchangePositions", "target_cell_id"), 381)))));
        Assert.Null(MapFightReader.ReadExchangePositions(Parse(
            I(F("GameActionFightEvent", "source_id"), Me),
            M(F("GameActionFightEvent", "slide"),
                I(F("Slide", "start_cell"), 425), I(F("Slide", "end_cell"), 396),
                I(F("Slide", "target_id"), -1)))));
    }
}
