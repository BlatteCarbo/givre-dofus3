using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using C = GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

/// <summary>
/// Contract fixtures against the mapped GameActionFightEvent structure.
/// The bytes/fields are synthetic (not claimed to be a live server replay).
/// Only mark ID, recorded author, activity and cell are derivable here.
/// </summary>
public sealed class CombatComboObservationsIngressTests
{
    private const long Actor = 41234567890;
    private const long Fight = 203;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static int F(string msg, string name) => Catalog.FieldNumber(msg, name)
        ?? throw new InvalidOperationException($"Unmapped 3.7.13.17 field {msg}.{name}");

    private static ProtoField V(string msg, string field, long value) => new()
        { Field = F(msg, field), Wire = WireType.Varint, Varint = unchecked((ulong)value) };

    private static ProtoField Nested(string msg, string field, params ProtoField[] children) => new()
        { Field = F(msg, field), Wire = WireType.Len, Message = children.ToList() };

    private static C.ComboFrame Frame(int breed = C.EliotropeBreedId)
        => new(Fight, 8, 1, 3, Actor, breed);

    private static C.ComboMarkRegistryEvidence Registry(params ObservedFightMark[] marks)
        => new(Fight, 8, marks.ToImmutableDictionary(m => m.Id));

    private static IReadOnlyList<ProtoField> MarkPacket(int id = 19, long author = Actor,
        int type = 4, int cell = 261, bool active = true, long? source = Actor)
    {
        var payload = new List<ProtoField> {
            Nested("GameActionFightEvent", "mark_cells",
                Nested("MarkCells", "mark", V("FightMark", "mark_id", id),
                    V("FightMark", "mark_type", type), V("FightMark", "author_id", author),
                    V("FightMark", "active", active ? 1 : 0),
                    Nested("FightMark", "cells", V("MarkedCell", "gask", cell))))
        };
        if (source.HasValue) payload.Add(V("GameActionFightEvent", "source_id", source.Value));
        return payload;
    }

    private static IReadOnlyList<ProtoField> Activate(int id = 19, bool active = true,
        long? source = Actor)
    {
        var payload = new List<ProtoField> {
            Nested("GameActionFightEvent", "activate_glyph_trap",
                V("ActivateGlyphTrap", "mark_id", id),
                V("ActivateGlyphTrap", "active", active ? 1 : 0))
        };
        if (source.HasValue) payload.Add(V("GameActionFightEvent", "source_id", source.Value));
        return payload;
    }

    private static IReadOnlyList<ProtoField> Unmark(int id = 19)
        => new[] { Nested("GameActionFightEvent", "unmark_cells",
            V("UnmarkCells", "mark_id", id)) };

    private static C.ComboObservation? Read(IReadOnlyList<ProtoField> payload,
        C.ComboMarkRegistryEvidence? prior = null, string message = "GameActionFightEvent",
        bool inbound = true, bool fighting = true, C.ComboFrame? frame = null,
        int turn = 2, long order = 4)
        => C.ReadConfirmedFightMarkObservation(message, inbound, fighting, frame ?? Frame(),
            turn, order, payload, prior ?? Registry());

    [Fact]
    public void Official_mark_cells_yield_only_mark_identity_not_spell_cast_ack_portal_or_wall()
    {
        // mark_type 4 is used by a Huppermage rune in the 3.7 capture:
        // it alone must never be rebranded as an Eliotrope portal.
        var placed = Assert.IsType<C.ComboObservation>(Read(MarkPacket()));
        Assert.Equal(C.ComboEventKind.MarkPlaced, placed.Kind);
        Assert.Equal(Actor, placed.Source);
        Assert.Equal(19, placed.MarkId);
        Assert.Equal(4, placed.MarkType);
        Assert.Equal(261, placed.ToCell);
        Assert.Equal((Fight, 8, 2, 4L),
            (placed.FightId, placed.Generation, placed.Turn, placed.Order));
        Assert.True(placed.ServerConfirmed);
        Assert.Equal(0, placed.ActionId); // NOT the portal spell's action 1181
        Assert.Equal(0, placed.Correlation); // no server cast-ACK correlation
        Assert.Equal(0, placed.SpellId);
        Assert.Equal(0, placed.Grade);
        Assert.Equal(0, placed.TemplateId); // mark_type != bomb wallDataId
        Assert.Equal(0, placed.Subject); // mark packet does not identify hit target
    }

    [Theory]
    [InlineData(false, true, "GameActionFightEvent")]
    [InlineData(true, false, "GameActionFightEvent")]
    [InlineData(true, true, "GameActionAcknowledgementRequest")]
    [InlineData(true, true, "SequenceEndEvent")]
    [InlineData(true, true, "GameActionFightCastRequest")]
    public void Outbound_out_of_fight_or_ack_requests_never_become_confirmed_combo_evidence(
        bool inbound, bool fighting, string message)
        => Assert.Null(Read(MarkPacket(), inbound: inbound, fighting: fighting, message: message));

    [Fact]
    public void Wrong_fight_generation_turn_or_non_monotonic_order_refuses_mark_evidence()
    {
        var wire = MarkPacket();
        Assert.Null(Read(wire, prior: Registry() with { FightId = Fight + 1 }));
        Assert.Null(Read(wire, prior: Registry() with { Generation = 7 }));
        Assert.Null(Read(wire, frame: Frame() with { FightId = Fight + 1 }));
        Assert.Null(Read(wire, turn: 0));
        Assert.Null(Read(wire, turn: 4));
        Assert.Null(Read(wire, order: 0));
    }

    [Fact]
    public void Mark_source_must_agree_with_the_author_before_a_new_placement_can_be_recorded()
    {
        Assert.Null(Read(MarkPacket(author: Actor + 1)));
        Assert.Null(Read(MarkPacket(source: null)));
        Assert.Null(Read(MarkPacket(id: 0)));
        Assert.Null(Read(MarkPacket(author: 0, source: 0)));
        Assert.Null(Read(MarkPacket(type: 0)));
        Assert.Null(Read(MarkPacket(cell: 560)));
        Assert.Null(Read(MarkPacket(), Registry(new ObservedFightMark(19, Actor, 4,
            ImmutableArray.Create(261), false))));
    }

    [Fact]
    public void Independent_mark_activation_requires_same_fight_registry_owner_and_source()
    {
        var mark = new ObservedFightMark(19, Actor, 4, ImmutableArray.Create(261), false);
        var owned = Registry(mark);
        var active = Assert.IsType<C.ComboObservation>(Read(Activate(), owned));
        Assert.Equal(C.ComboEventKind.MarkActivated, active.Kind);
        Assert.Equal(19, active.MarkId);
        Assert.Equal(Actor, active.Source);
        Assert.Equal(261, active.ToCell);
        Assert.Equal(4, active.MarkType);
        Assert.Equal(0, active.ActionId);
        Assert.Equal(0, active.Correlation);

        Assert.Null(Read(Activate())); // mark ID alone contains no owner
        Assert.Null(Read(Activate(source: null), owned));
        Assert.Null(Read(Activate(source: Actor + 1), owned));
        Assert.Null(Read(Activate(), owned with { Generation = 7 }));
        Assert.Null(Read(Activate(id: 0), owned));
        Assert.Null(Read(Activate(id: 20), owned));
    }

    [Fact]
    public void Confirmed_inactive_or_removed_mark_is_a_negative_witness_without_an_invented_author()
    {
        var off = Assert.IsType<C.ComboObservation>(Read(Activate(active: false, source: null)));
        Assert.Equal(C.ComboEventKind.MarkDeactivated, off.Kind);
        Assert.Equal(19, off.MarkId);
        Assert.Equal(0, off.Source);
        Assert.True(off.ServerConfirmed);

        var removed = Assert.IsType<C.ComboObservation>(Read(Unmark()));
        Assert.Equal(C.ComboEventKind.MarkRemoved, removed.Kind);
        Assert.Equal(19, removed.MarkId);
        Assert.Equal(0, removed.Source);
        Assert.Null(Read(Unmark(id: 0)));
        Assert.Null(Read(Activate(id: 0, active: false)));
    }

    [Fact]
    public void Exchange_positions_and_damage_without_causal_cast_never_emit_telefrag_or_detonation()
    {
        var exchange = new ProtoField[] {
            V("GameActionFightEvent", "source_id", Actor),
            Nested("GameActionFightEvent", "exchange_positions",
                V("ExchangePositions", "caster_cell_id", 396),
                V("ExchangePositions", "target_id", -4),
                V("ExchangePositions", "target_cell_id", 381))
        };
        Assert.NotNull(MapFightReader.ReadExchangePositions(exchange));
        Assert.Null(Read(exchange));
        // FightMark data has no life, spell ID or target; never infer action 99.
        var damaged = new ProtoField[] {
            V("GameActionFightEvent", "source_id", Actor),
            Nested("GameActionFightEvent", "life_points_lost",
                V("LifePointsLost", "loss", 15),
                V("LifePointsLost", "target_id", -4))
        };
        Assert.Null(Read(damaged));
    }

    [Fact]
    public void Ambiguous_mark_and_unmark_payload_is_not_accepted_as_a_server_sequence_step()
    {
        var ambiguous = MarkPacket().Concat(Unmark()).ToArray();
        Assert.Null(Read(ambiguous));
    }

    [Fact]
    public void Real_mark_deactivation_event_invalidates_an_otherwise_complete_rogue_proof()
    {
        // Positive steps below are artificial requirements, NOT live proof of
        // server detonation. The negative mark event is decoded by the same
        // catalog-backed adapter that the packet hook would use.
        int Cell(int x) => MapPathfinder.CellFromMapPoint(x, 0);
        SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind, int dice = 0)
            => new(id, 1, 2, 1, 6, 0, false, 1, 1, 0, 0, "",
                ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 0, 0, 0, 0,
                    "a,A", 'P', 1, 0, DiceNum: dice, Triggers: "I")));
        var frame = Frame(C.RogueBreedId);
        const long target = -4;
        var active = ImmutableHashSet.Create(Cell(11), Cell(12));
        var evidence = new C.ComboEvidence(TargetId: target,
            SourceSpell: Spell(13444, 1008, SpellEffectKind.Summon, 3112),
            FollowupSpell: Spell(13455, 99, SpellEffectKind.Damage),
            FirstMarkId: 19, TargetCell: Cell(11), FirstBombId: 101, SecondBombId: 102,
            Wall: new C.VerifiedRogueWall(2, Actor, 101, 102, active, true),
            CurrentBombs: new[] {
                new PlannerFighter(101, Cell(10), false, 100, 100,
                    MonsterTemplate: 3112, SummonerId: Actor),
                new PlannerFighter(102, Cell(13), false, 100, 100,
                    MonsterTemplate: 3112, SummonerId: Actor)
            },
            EffectActionId: 99, FollowupCorrelation: 900,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.RogueBreedId, FollowupBreedId: C.RogueBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [13444] = 1 });
        C.ComboObservation Event(int order, int turn, C.ComboEventKind kind,
            long subject = 0, int spellId = 0, int template = 0,
            int markId = 0, int toCell = -1, int actionId = 0, long correlation = 0)
            => new(order, Fight, 8, turn, Actor, subject, kind, SpellId: spellId,
                Grade: spellId == 0 ? 0 : 1, TemplateId: template, MarkId: markId,
                ToCell: toCell, ActionId: actionId, Correlation: correlation,
                ServerConfirmed: true);
        var original = new[] {
            Event(1, 1, C.ComboEventKind.Summon, 101, 13444, template: 3112),
            Event(2, 1, C.ComboEventKind.Summon, 102, 13444, template: 3112),
            Event(3, 2, C.ComboEventKind.WallActivated, template: 2, markId: 19),
            Event(4, 2, C.ComboEventKind.Move, target, toCell: Cell(11))
                with { Source = target },
            Event(5, 2, C.ComboEventKind.DetonationConfirmed, 101, 13455,
                template: 3112, correlation: 900),
            Event(6, 2, C.ComboEventKind.Effect, target, 13455,
                actionId: 99, correlation: 900)
        };
        var valid = C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, original);
        Assert.True(valid.Complete, valid.Reason);

        var serverOff = Assert.IsType<C.ComboObservation>(Read(
            Activate(id: 19, active: false, source: null), frame: frame, order: 4));
        Assert.Equal(C.ComboEventKind.MarkDeactivated, serverOff.Kind);
        var invalidated = original.Take(3).Concat(new[] { serverOff })
            .Concat(original.Skip(3).Select(e => e with { Order = e.Order + 1 })).ToArray();
        var result = C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, invalidated);
        Assert.False(result.Complete);
        Assert.Equal("authoritative removal invalidates combo", result.Reason);
    }
}
