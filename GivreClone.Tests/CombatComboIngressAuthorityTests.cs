extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatComboIngressBuffer = F06App::GivreClone.App.Services.CombatComboIngressBuffer;
using CombatComboIngressSnapshot = F06App::GivreClone.App.Services.CombatComboIngressSnapshot;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using C = F06App::GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

/// <summary>
/// Contract fixtures are synthetic mapped payloads, not authenticated wire
/// captures. No assertion equates S->C direction with transport provenance.
/// </summary>
public sealed class CombatComboIngressAuthorityTests
{
    private const uint Pid = 1009;
    private const long Fight = 406;
    private const long Map = 191104002;
    private const long Actor = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    static CombatComboIngressAuthorityTests()
    {
        MapFightReader.UseCatalog(Catalog.FieldNumber);
        FightStatsReader.UseCatalog(Catalog.FieldNumber);
    }

    private static int F(string msg, string field) => Catalog.FieldNumber(msg, field)
        ?? throw new InvalidOperationException($"Unmapped field {msg}.{field}");

    private static ProtoField V(string msg, string field, long value) => new()
        { Field = F(msg, field), Wire = WireType.Varint, Varint = unchecked((ulong)value) };

    private static ProtoField M(string msg, string field, params ProtoField[] fields) => new()
        { Field = F(msg, field), Wire = WireType.Len, Message = fields.ToList() };

    private static ProtoField[] Place(int id = 19, long author = Actor,
        long source = Actor, bool active = false) =>
    [
        M("GameActionFightEvent", "mark_cells",
            M("MarkCells", "mark",
                V("FightMark", "mark_id", id),
                V("FightMark", "mark_type", 4),
                V("FightMark", "author_id", author),
                V("FightMark", "active", active ? 1 : 0),
                M("FightMark", "cells", V("MarkedCell", "gask", 261)))),
        V("GameActionFightEvent", "source_id", source)
    ];

    private static ProtoField[] Activate(int id = 19, bool active = true,
        long source = Actor) =>
    [
        M("GameActionFightEvent", "activate_glyph_trap",
            V("ActivateGlyphTrap", "mark_id", id),
            V("ActivateGlyphTrap", "active", active ? 1 : 0)),
        V("GameActionFightEvent", "source_id", source)
    ];

    private static ProtoField[] Remove(int id = 19) =>
    [
        M("GameActionFightEvent", "unmark_cells",
            V("UnmarkCells", "mark_id", id))
    ];

    private static DecodedPacket Packet(ProtoField[] payload, uint pid = Pid,
        bool outbound = false, string name = "GameActionFightEvent",
        bool decoded = true) =>
        new(pid, outbound, "jwe", name, payload, Array.Empty<byte>())
        { PayloadDecoded = decoded };

    private static CombatComboIngressBuffer Started()
    {
        var ingress = new CombatComboIngressBuffer();
        Assert.True(ingress.Begin(Pid, Fight, Map, Actor) > 0);
        ingress.TurnObserved(Pid, Fight, Map, 1, Actor);
        return ingress;
    }

    [Fact]
    public void Mapped_marks_are_ordered_and_bounded_but_cannot_certify_cast_or_own_mark()
    {
        var ingress = Started();
        var place = Assert.IsType<C.ComboObservation>(ingress.Observe(Packet(Place()), Fight, Map));
        Assert.Equal(C.ComboEventKind.MarkPlaced, place.Kind);
        Assert.Equal(Actor, place.Source);
        Assert.Equal(19, place.MarkId);
        Assert.False(place.ServerConfirmed);
        Assert.Equal(0, place.Correlation);
        Assert.Equal(0, place.SpellId);
        Assert.Equal(0, place.Grade);
        Assert.Equal(0, place.ActionId);

        var activation = Assert.IsType<C.ComboObservation>(
            ingress.Observe(Packet(Activate()), Fight, Map));
        Assert.Equal(C.ComboEventKind.MarkActivated, activation.Kind);
        Assert.Equal(Actor, activation.Source);
        Assert.Equal(place.Order + 1, activation.Order);

        var off = Assert.IsType<C.ComboObservation>(
            ingress.Observe(Packet(Activate(active: false)), Fight, Map));
        Assert.Equal(C.ComboEventKind.MarkDeactivated, off.Kind);
        Assert.Equal(0, off.Source);
        var removed = Assert.IsType<C.ComboObservation>(
            ingress.Observe(Packet(Remove()), Fight, Map));
        Assert.Equal(C.ComboEventKind.MarkRemoved, removed.Kind);
        Assert.Equal(0, removed.Source);

        var snapshot = Assert.IsType<CombatComboIngressSnapshot>(
            ingress.Snapshot(Pid, Fight, Map));
        Assert.False(snapshot.ServerProvenanceEstablished);
        Assert.Equal(4, snapshot.Observations.Count);
        Assert.Empty(snapshot.Marks);
        Assert.All(snapshot.Observations, x => Assert.False(x.ServerConfirmed));
        Assert.Equal(new long[] { 1, 2, 3, 4 },
            snapshot.Observations.Select(x => x.Order));
    }

    [Fact]
    public void Direction_payload_owner_and_turn_guard_prevent_false_positive_candidates()
    {
        var ingress = new CombatComboIngressBuffer();
        Assert.Equal(0, ingress.Begin(Pid, 0, Map, Actor));
        Assert.Equal(0, ingress.Begin(Pid, Fight, 0, Actor));
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map));
        Assert.True(ingress.Begin(Pid, Fight, Map, Actor) > 0);
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map)); // no authoritative turn
        ingress.TurnObserved(Pid, Fight, Map, 0, Actor);
        ingress.TurnObserved(Pid, Fight, Map, 1, 0);
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map));
        ingress.TurnObserved(Pid, Fight, Map, 1, Actor);
        Assert.Null(ingress.Observe(Packet(Place(), outbound: true), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(), decoded: false), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(), name: "SequenceEndEvent"), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(), name: "GameActionAcknowledgementRequest"), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Activate()), Fight, Map)); // no previous owner
        Assert.Null(ingress.Observe(Packet(Place(author: Actor + 1)), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(source: Actor + 1)), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(id: 0)), Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place(), pid: Pid + 1), Fight, Map));
        Assert.Empty(Assert.IsType<CombatComboIngressSnapshot>(
            ingress.Snapshot(Pid, Fight, Map)).Observations);
    }

    [Fact]
    public void Stale_pid_fight_turn_and_map_do_not_create_certified_history()
    {
        var ingress = Started();
        Assert.Null(ingress.Observe(Packet(Place()), Fight + 1, Map));
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map + 1));
        Assert.Null(ingress.Observe(Packet(Place(), pid: Pid - 1), Fight, Map));
        ingress.TurnObserved(Pid, Fight, Map, 1, Actor);
        ingress.TurnObserved(Pid, Fight, Map, 0, Actor + 1);
        Assert.Equal(1, ingress.Snapshot(Pid, Fight, Map)!.Turn);

        int oldGeneration = ingress.Snapshot(Pid, Fight, Map)!.Generation;
        ingress.End(Pid); // connection lost / local session discarded
        Assert.Null(ingress.Snapshot(Pid, Fight, Map));
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map));
        Assert.True(ingress.Begin(Pid, Fight, Map, Actor) > oldGeneration);
        ingress.TurnObserved(Pid, Fight, Map, 1, Actor);
        // Same PID/fight/map after reconnection is indistinguishable by the
        // decoder. Even a matching late callback can only be diagnostic.
        var ambiguous = Assert.IsType<C.ComboObservation>(
            ingress.Observe(Packet(Place()), Fight, Map));
        Assert.False(ambiguous.ServerConfirmed);
        Assert.Equal(1, ambiguous.Order);
        Assert.NotEqual(oldGeneration, ambiguous.Generation);

        ingress.MapObserved(Pid, Map + 1);
        Assert.Null(ingress.Snapshot(Pid, Fight, Map));
        Assert.Null(ingress.Observe(Packet(Remove()), Fight, Map));
    }

    [Fact]
    public void Snapshots_are_detached_read_only_and_old_marks_cannot_own_reused_ids()
    {
        var ingress = Started();
        Assert.NotNull(ingress.Observe(Packet(Place()), Fight, Map));
        var before = ingress.Snapshot(Pid, Fight, Map)!;
        Assert.Throws<NotSupportedException>(() =>
            ((IList<C.ComboObservation>)before.Observations).Clear());
        Assert.Equal(261, before.Marks[19].Cells.Single());
        Assert.False(before.Marks[19].Active);
        Assert.Null(ingress.Observe(Packet(Place()), Fight, Map)); // reused mark ID
        Assert.NotNull(ingress.Observe(Packet(Activate()), Fight, Map));
        var after = ingress.Snapshot(Pid, Fight, Map)!;
        Assert.Single(before.Observations);
        Assert.False(before.Marks[19].Active);
        Assert.Equal(2, after.Observations.Count);
        Assert.True(after.Marks[19].Active);
        ingress.End(Pid);
        ingress.Begin(Pid, Fight + 1, Map, Actor);
        ingress.TurnObserved(Pid, Fight + 1, Map, 1, Actor);
        Assert.Null(ingress.Observe(Packet(Activate()), Fight + 1, Map));
        Assert.Empty(ingress.Snapshot(Pid, Fight + 1, Map)!.Marks);
    }

    [Fact]
    public void Diagnostic_history_has_hard_per_fight_capacity()
    {
        var ingress = Started();
        for (int i = 1; i <= CombatComboIngressBuffer.MaximumObservations + 5; ++i)
        {
            var observation = ingress.Observe(Packet(Place(id: i)), Fight, Map);
            Assert.NotNull(observation);
            Assert.False(observation.ServerConfirmed);
        }
        var snapshot = ingress.Snapshot(Pid, Fight, Map)!;
        Assert.True(snapshot.Truncated);
        Assert.Equal(CombatComboIngressBuffer.MaximumObservations, snapshot.Observations.Count);
        Assert.Equal(6, snapshot.Observations[0].Order);
        Assert.Equal(CombatComboIngressBuffer.MaximumObservations + 5,
            snapshot.Observations[^1].Order);
        Assert.InRange(snapshot.Marks.Count, 1, CombatComboIngressBuffer.MaximumObservations);
    }
}
