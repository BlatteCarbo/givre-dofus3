extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Buffer = F06App::GivreClone.App.Services.CombatComboIngressBuffer;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Snapshot = F06App::GivreClone.App.Services.CombatComboIngressSnapshot;
using C = F06App::GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

// MapFightReader/FightStatsReader and EnvelopeBuilder have process-wide
// catalogs. Isolate this suite from unrelated xUnit collections that
// temporarily rebind those delegates.
[CollectionDefinition("Combat mark ingress isolated", DisableParallelization = true)]
public sealed class CombatMarkIngressRuntimeIsolationCollection { }

/// <summary>
/// Real protobuf framing + the SAME runtime ingress dispatcher invoked by
/// LaunchService.OnDecoded. The frames themselves are offline fixtures, not
/// authenticated network captures or a server ACK/cast correlation.
/// </summary>
[Collection("Combat mark ingress isolated")]
public sealed class CombatMarkIngressRuntimeTests
{
    private const uint Pid = 5234;
    private const long Fight = 406;
    private const long Map = 191104002;
    private const long Actor = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    static CombatMarkIngressRuntimeTests()
    {
        MapFightReader.UseCatalog(Catalog.FieldNumber);
        FightStatsReader.UseCatalog(Catalog.FieldNumber);
    }

    private static int F(string message, string name)
        => Catalog.FieldNumber(message, name)
            ?? throw new InvalidOperationException($"Unmapped field {message}.{name}");

    private static ProtoField V(string message, string name, long value) => new()
    {
        Field = F(message, name), Wire = WireType.Varint, Varint = unchecked((ulong)value)
    };

    private static ProtoField M(string message, string name, params ProtoField[] children) => new()
    {
        Field = F(message, name), Wire = WireType.Len, Message = children.ToList()
    };

    private static ProtoField[] Place(int id = 51, long author = Actor, long source = Actor,
        int type = 4) =>
    [
        M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
            V("FightMark", "mark_id", id),
            V("FightMark", "mark_type", type),
            V("FightMark", "author_id", author),
            M("FightMark", "cells", V("MarkedCell", "gask", 261)))),
        V("GameActionFightEvent", "source_id", source)
    ];

    private static ProtoField[] Activation(int id = 51, bool active = true, long source = Actor) =>
    [
        M("GameActionFightEvent", "activate_glyph_trap",
            V("ActivateGlyphTrap", "mark_id", id),
            V("ActivateGlyphTrap", "active", active ? 1 : 0)),
        V("GameActionFightEvent", "source_id", source)
    ];

    private static ProtoField[] Remove(int id = 51) =>
    [
        M("GameActionFightEvent", "unmark_cells", V("UnmarkCells", "mark_id", id))
    ];

    private static byte[] Frame(string name, IReadOnlyList<ProtoField> payload)
    {
        byte[] body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name), payload);
        var prefix = new List<byte>();
        uint remaining = (uint)body.Length;
        do
        {
            byte b = (byte)(remaining & 127);
            remaining >>= 7;
            prefix.Add(remaining == 0 ? b : (byte)(b | 128));
        } while (remaining != 0);
        return prefix.Concat(body).ToArray();
    }

    private sealed class Runtime
    {
        public readonly Decoder Decoder = new(Catalog);
        public readonly Buffer Ingress = new();
        public readonly List<C.ComboObservation> Delivered = new();
        public long FightId;
        public long MapId;
        public bool InFight;

        public Runtime()
        {
            Decoder.Packet += OnDecoded;
            Decoder.LocalEpochRevoked += Ingress.EndLocalEpoch;
        }

        public void OnDecoded(Packet packet)
        {
            if (!Ingress.Admit(packet, Decoder.IsCurrentLocalEpoch(packet))) return;
            if (!packet.ClientToServer && packet.PayloadDecoded
                && packet.Name == "MapCurrentEvent")
            {
                MapId = PayloadReader.Int(packet.Payload, F("MapCurrentEvent", "map_id"), 0);
                Ingress.MapObservedFromPacket(packet, MapId);
            }
            else
            {
                var result = Ingress.ObserveRuntimeEvent(packet, FightId, MapId,
                    Actor, InFight, Catalog.FieldNumber);
                if (result is not null) Delivered.Add(result);
            }

            // The actual LaunchService updates its larger combat/map model
            // separately; the ingress dispatcher never invents those states.
            if (!packet.ClientToServer && packet.PayloadDecoded
                && packet.Name == "FightStartingEvent")
            {
                FightId = PayloadReader.Int(packet.Payload,
                    F("FightStartingEvent", "fight_id"), 0);
                InFight = true;
            }
            else if (!packet.ClientToServer && packet.Name == "FightEndEvent")
            {
                InFight = false;
                FightId = 0;
            }
        }

        public Packet Feed(string name, bool outbound = false, params ProtoField[] payload)
        {
            // Canonical protobuf Any omits the value entirely for an empty
            // message, which the generic decoder reports as undecoded. Give
            // synthetic no-payload close events one non-semantic unknown field
            // so these tests exercise the decoded lifecycle hook itself.
            if (payload.Length == 0)
                payload = [new ProtoField { Field = 8191, Wire = WireType.Varint, Varint = 1 }];
            Packet? captured = null;
            void Capture(Packet p) => captured = p;
            Decoder.Packet += Capture;
            try { Decoder.Feed(Pid, outbound, Frame(name, payload)); }
            finally { Decoder.Packet -= Capture; }
            return Assert.IsType<Packet>(captured);
        }

        public void Start()
        {
            Feed("MapCurrentEvent", false, V("MapCurrentEvent", "map_id", Map));
            Feed("FightStartingEvent", false, V("FightStartingEvent", "fight_id", Fight));
        }

        public void Turn(int round = 1)
            => Feed("FightTurnEvent", false,
                V("FightTurnEvent", "round", round),
                V("FightTurnEvent", "character_id", Actor));

        public Snapshot? Snapshot()
            => Ingress.Snapshot(Pid, FightId, MapId, Decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public void Wire_mark_placed_activated_deactivated_removed_updates_single_diagnostic_history()
    {
        var runtime = new Runtime();
        runtime.Start();
        Assert.NotNull(runtime.Snapshot());
        runtime.Turn();
        var placed = runtime.Feed("GameActionFightEvent", false, Place());
        var initial = Assert.IsType<Snapshot>(runtime.Snapshot());

        Assert.Single(initial.Observations);
        Assert.Equal(C.ComboEventKind.MarkPlaced, initial.Observations[0].Kind);
        Assert.Equal(51, initial.Observations[0].MarkId);
        Assert.Equal(Actor, initial.Observations[0].Source);
        Assert.Equal(1, initial.Observations[0].Turn);
        Assert.Equal(initial.Generation, initial.Observations[0].Generation);
        Assert.Equal(Fight, initial.Frame!.FightId);
        Assert.Equal(initial.Generation, initial.MarkEvidence!.Generation);
        Assert.Equal(261, Assert.Single(initial.Marks[51].Cells));
        Assert.False(initial.Marks[51].Active);

        var activatedPacket = runtime.Feed("GameActionFightEvent", false, Activation());
        var activated = Assert.IsType<Snapshot>(runtime.Snapshot());
        Assert.True(activated.Marks[51].Active);
        Assert.False(initial.Marks[51].Active);
        var deactivatedPacket = runtime.Feed("GameActionFightEvent", false, Activation(active: false));
        var deactivated = Assert.IsType<Snapshot>(runtime.Snapshot());
        Assert.False(deactivated.Marks[51].Active);
        var removedPacket = runtime.Feed("GameActionFightEvent", false, Remove());
        var removed = Assert.IsType<Snapshot>(runtime.Snapshot());
        Assert.Empty(removed.Marks);
        Assert.Equal(4, removed.Observations.Count);
        Assert.Equal(new[]
        {
            C.ComboEventKind.MarkPlaced, C.ComboEventKind.MarkActivated,
            C.ComboEventKind.MarkDeactivated, C.ComboEventKind.MarkRemoved
        }, removed.Observations.Select(o => o.Kind));
        Assert.Equal(new[]
        {
            placed.DecoderOrder, activatedPacket.DecoderOrder,
            deactivatedPacket.DecoderOrder, removedPacket.DecoderOrder
        },
            removed.Observations.Select(o => o.Order));
        Assert.All(removed.Observations, observation =>
        {
            Assert.False(observation.ServerConfirmed);
            Assert.Equal(0, observation.SpellId);
            Assert.Equal(0, observation.ActionId);
            Assert.Equal(0, observation.Correlation);
        });
        Assert.False(removed.CanAuthorizeCast);
        Assert.False(removed.ServerProvenanceEstablished);
        Assert.Null(removed.ActionEvidence);
        Assert.False(removed.Review(C.ComboKind.EliotropeRedirect,
            new C.ComboEvidence(TargetId: Actor + 1, FirstMarkId: 51)).Complete);

        // Exact same admitted packet cannot be counted a second time.
        Assert.Null(runtime.Ingress.ObserveRuntimeEvent(placed, Fight, Map,
            Actor, true, Catalog.FieldNumber));
        Assert.Equal(4, runtime.Snapshot()!.Observations.Count);
    }

    [Fact]
    public void Missing_turn_direction_masks_and_ambiguous_marks_do_not_fake_cast_evidence()
    {
        var runtime = new Runtime();
        runtime.Start();
        runtime.Feed("GameActionFightEvent", false, Place()); // no mapped turn yet
        runtime.Feed("GameActionFightEvent", true, Place());  // client request, not server
        runtime.Feed("GameActionAcknowledgementRequest", true,
            V("GameActionAcknowledgementRequest", "action_id", 74));
        Assert.Empty(runtime.Snapshot()!.Observations);
        runtime.Turn();
        runtime.Feed("GameActionFightEvent", false, Place(source: Actor + 1));
        runtime.Feed("GameActionFightEvent", false, Place(author: Actor + 1));
        runtime.Feed("GameActionFightEvent", false, Place(id: 0));
        runtime.Feed("GameActionFightEvent", false, Activation()); // missing owner
        runtime.Feed("GameActionFightEvent", false, Place().Concat(Remove()).ToArray());
        Assert.Empty(runtime.Snapshot()!.Observations);

        runtime.Feed("GameActionFightEvent", false, Place());
        runtime.Feed("GameActionFightEvent", false, Place()); // reused mark ID
        Assert.Single(runtime.Snapshot()!.Observations);
        Assert.True(runtime.Snapshot()!.Observations[0].Order > 0);
        Assert.Equal(0, runtime.Snapshot()!.Observations[0].Correlation);
        Assert.False(runtime.Snapshot()!.Review(C.ComboKind.RogueWall,
            new C.ComboEvidence()).Complete);
    }

    [Fact]
    public void Late_mark_after_end_or_map_change_cannot_resurrect_old_window()
    {
        var runtime = new Runtime();
        runtime.Start();
        runtime.Turn();
        var first = runtime.Feed("GameActionFightEvent", false, Place());
        int generation = runtime.Snapshot()!.Generation;
        runtime.Feed("FightEndEvent");
        Assert.Null(runtime.Snapshot());
        runtime.Feed("GameActionFightEvent", false, Place());
        Assert.Null(runtime.Snapshot());
        Assert.Null(runtime.Ingress.ObserveRuntimeEvent(first, Fight, Map,
            Actor, true, Catalog.FieldNumber));

        runtime.Start();
        Assert.True(runtime.Snapshot()!.Generation > generation);
        runtime.Turn();
        runtime.Feed("MapCurrentEvent", false,
            V("MapCurrentEvent", "map_id", Map + 1));
        Assert.Null(runtime.Snapshot());
        runtime.Feed("GameActionFightEvent", false, Place());
        Assert.Null(runtime.Snapshot());
    }

    [Fact]
    public void Reused_pid_old_decoder_packets_and_foreign_connection_fail_closed()
    {
        var runtime = new Runtime();
        runtime.Start();
        runtime.Turn();
        var old = runtime.Feed("GameActionFightEvent", false, Place());
        var snapshot = runtime.Snapshot()!;
        long epoch = snapshot.DecoderEpoch;
        runtime.Decoder.Forget(Pid);
        Assert.Null(runtime.Snapshot());
        Assert.False(runtime.Decoder.IsCurrentLocalEpoch(old));

        runtime.Start();
        runtime.Turn();
        var next = runtime.Snapshot()!;
        Assert.True(next.DecoderEpoch > epoch);
        Assert.True(next.Generation > snapshot.Generation);
        Assert.Empty(next.Observations);
        Assert.False(runtime.Ingress.Admit(old, runtime.Decoder.IsCurrentLocalEpoch(old)));
        Assert.Null(runtime.Ingress.ObserveRuntimeEvent(old, Fight, Map,
            Actor, true, Catalog.FieldNumber));
        Assert.False(runtime.Decoder.IsCurrentLocalEpoch(old with { }));

        // Distinct decoders can reuse the same numeric LOCAL stamp. They
        // cannot authenticate each other's objects or TCP connections.
        var foreign = new Decoder(Catalog);
        Packet? other = null;
        foreign.Packet += p => other = p;
        foreign.Feed(Pid, false, Frame("GameActionFightEvent", Place()));
        Assert.NotNull(other);
        Assert.False(runtime.Decoder.IsCurrentLocalEpoch(other!));
        Assert.False(runtime.Ingress.Admit(other!, runtime.Decoder.IsCurrentLocalEpoch(other!)));
        Assert.Empty(runtime.Snapshot()!.Observations);

        // A stale TCP sender that enters Feed AFTER Forget gets a fresh local
        // stamp: it may be logged only as non-certified diagnostic history.
        runtime.Feed("GameActionFightEvent", false, Place(53));
        Assert.Single(runtime.Snapshot()!.Observations);
        Assert.False(runtime.Snapshot()!.Observations[0].ServerConfirmed);
    }

    [Fact]
    public void Wrong_context_consumes_packet_once_and_closure_does_not_replay_it()
    {
        var runtime = new Runtime();
        runtime.Start();
        runtime.Turn();
        // A genuine decoded packet, but no dispatcher callback yet.
        runtime.Decoder.Packet -= runtime.OnDecoded;
        var wrong = runtime.Feed("GameActionFightEvent", false, Place(59));
        Assert.True(runtime.Ingress.Admit(wrong, runtime.Decoder.IsCurrentLocalEpoch(wrong)));
        Assert.Null(runtime.Ingress.ObserveRuntimeEvent(wrong, Fight + 1, Map,
            Actor, true, Catalog.FieldNumber));
        Assert.Null(runtime.Ingress.ObserveRuntimeEvent(wrong, Fight, Map,
            Actor, true, Catalog.FieldNumber)); // consumed, no replay in correct context

        runtime.Decoder.Packet += runtime.OnDecoded;
        runtime.Feed("GameActionFightEvent", false, Place(60));
        Assert.Single(runtime.Snapshot()!.Observations);
        Assert.Equal(60, runtime.Snapshot()!.Observations[0].MarkId);
        runtime.Feed("MapComplementaryInformationEvent");
        Assert.Null(runtime.Snapshot());
    }

    [Fact]
    public void Reentrant_packet_observer_cannot_steal_first_mark_before_runtime_ingress()
    {
        var runtime = new Runtime();
        runtime.Start();
        runtime.Turn();
        bool nested = false;
        // The callback is subscribed AFTER the runtime ingress, as in
        // LaunchService where PacketDecoded is published after diagnostic
        // admission. Nested capture must not replace the first packet before
        // its mark has been consumed.
        runtime.Decoder.Packet += p =>
        {
            if (nested || p.Name != "GameActionFightEvent" || p.ClientToServer)
                return;
            nested = true;
            runtime.Feed("GameActionFightEvent", false, Place(62));
        };
        runtime.Feed("GameActionFightEvent", false, Place(61));
        Assert.True(nested);
        var snap = runtime.Snapshot()!;
        Assert.Equal(2, snap.Observations.Count);
        Assert.Equal(new[] { 61, 62 }, snap.Observations.Select(x => x.MarkId));
        Assert.True(snap.Observations[0].Order > 0);
        Assert.True(snap.Observations[1].Order > snap.Observations[0].Order);
        Assert.All(snap.Observations, x => Assert.False(x.ServerConfirmed));
    }
}
