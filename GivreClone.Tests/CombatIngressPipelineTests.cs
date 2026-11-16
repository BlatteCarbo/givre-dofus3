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
using C = F06App::GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

/// <summary>
/// Local packet integration contract matching LaunchService.Packets' guarded
/// ingress hooks. All frames are synthetic, never server TCP receipts.
/// No game, network, ACK or tactical sender is involved.
/// </summary>
public sealed class CombatIngressPipelineTests
{
    private const uint Pid = 9217;
    private const long FightId = 410;
    private const long MapId = 191104002;
    private const long ActorId = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    static CombatIngressPipelineTests()
    {
        MapFightReader.UseCatalog(Catalog.FieldNumber);
        FightStatsReader.UseCatalog(Catalog.FieldNumber);
    }

    private static int F(string message, string field)
        => Catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"missing mapping {message}.{field}");

    private static ProtoField V(string message, string field, long value) => new()
    {
        Field = F(message, field), Wire = WireType.Varint, Varint = unchecked((ulong)value)
    };

    private static ProtoField M(string message, string field, params ProtoField[] children) => new()
    {
        Field = F(message, field), Wire = WireType.Len, Message = children.ToList()
    };

    private static ProtoField[] Place(int id = 51) =>
    [
        M("GameActionFightEvent", "mark_cells",
            M("MarkCells", "mark",
                V("FightMark", "mark_id", id),
                V("FightMark", "mark_type", 4),
                V("FightMark", "author_id", ActorId),
                M("FightMark", "cells", V("MarkedCell", "gask", 261)))),
        V("GameActionFightEvent", "source_id", ActorId)
    ];

    private static ProtoField[] Activate(int id = 51) =>
    [
        M("GameActionFightEvent", "activate_glyph_trap",
            V("ActivateGlyphTrap", "mark_id", id),
            V("ActivateGlyphTrap", "active", 1)),
        V("GameActionFightEvent", "source_id", ActorId)
    ];

    private static ProtoField[] Show(long id = ActorId, int breed = C.EliotropeBreedId) =>
    [
        M("FightFighterShowEvent", "information",
            V("ActorPositionInformation", "actor_id", id),
            M("ActorPositionInformation", "actor_information",
                M("ActorInformation", "fighter",
                    M("FightFighterInformation", "named_fighter",
                        M("NamedFighterInformation", "character_information",
                            V("FightCharacterInformation", "breed_id", breed))))))
    ];

    private static byte[] Wire(string name, IReadOnlyList<ProtoField> payload)
    {
        byte[] body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name), payload);
        var len = new List<byte>();
        uint rest = (uint)body.Length;
        do
        {
            byte b = (byte)(rest & 127);
            rest >>= 7;
            len.Add(rest == 0 ? b : (byte)(b | 128));
        } while (rest > 0);
        return len.Concat(body).ToArray();
    }

    private sealed class Pipeline
    {
        public readonly Decoder Decoder = new(Catalog);
        public readonly Buffer Ingress = new();
        public long Map;
        public long Fight;
        public int Forwarded;

        public Pipeline()
        {
            Decoder.Packet += Dispatch;
        }

        public Packet? Feed(string name, bool clientToServer = false, params ProtoField[] payload)
        {
            Packet? emitted = null;
            void Capture(Packet packet) => emitted = packet;
            Decoder.Packet += Capture;
            try { Decoder.Feed(Pid, clientToServer, Wire(name, payload)); }
            finally { Decoder.Packet -= Capture; }
            return emitted;
        }

        public void Dispatch(Packet p)
        {
            if (!Ingress.Admit(p, Decoder.IsCurrentLocalEpoch(p))) return;
            ++Forwarded;
            switch (p.Name)
            {
                case "MapCurrentEvent" when !p.ClientToServer:
                    Map = PayloadReader.Int(p.Payload, F("MapCurrentEvent", "map_id"), 0);
                    Ingress.MapObservedFromPacket(p, Map);
                    break;
                case "FightStartingEvent" when !p.ClientToServer:
                    Fight = PayloadReader.Int(p.Payload, F("FightStartingEvent", "fight_id"), 0);
                    Ingress.BeginFromPacket(p, Fight, Map, ActorId);
                    break;
                case "FightFighterShowEvent" when !p.ClientToServer:
                    Ingress.BreedObservedFromPacket(p, Fight, Map);
                    break;
                case "FightTurnEvent" when !p.ClientToServer:
                    Ingress.TurnObservedFromPacket(p, Fight, Map,
                        PayloadReader.Int(p.Payload, F("FightTurnEvent", "round"), 0),
                        FightStatsReader.TurnActor(p.Payload));
                    break;
                case "GameActionFightEvent" when !p.ClientToServer:
                    Ingress.ObserveFromPacket(p, Fight, Map);
                    break;
                case "FightEndEvent" when !p.ClientToServer:
                    Ingress.EndFromPacket(p);
                    Fight = 0;
                    break;
            }
        }

        public void Start(int round = 1)
        {
            Feed("MapCurrentEvent", false, V("MapCurrentEvent", "map_id", MapId));
            Feed("FightStartingEvent", false, V("FightStartingEvent", "fight_id", FightId));
            Feed("FightTurnEvent", false,
                V("FightTurnEvent", "round", round),
                V("FightTurnEvent", "character_id", ActorId));
        }

        public F06App::GivreClone.App.Services.CombatComboIngressSnapshot? Snapshot()
            => Ingress.Snapshot(Pid, Fight, Map, Decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public void Framed_packets_populate_frame_mark_registry_and_observation_but_no_cast_receipt()
    {
        var pipe = new Pipeline();
        pipe.Start();
        pipe.Feed("FightFighterShowEvent", true, Show());
        Assert.Equal(0, pipe.Snapshot()!.Frame!.BreedId);
        pipe.Feed("FightFighterShowEvent", false, Show());
        pipe.Feed("GameActionFightEvent", false, Place());
        var before = Assert.IsType<F06App::GivreClone.App.Services.CombatComboIngressSnapshot>(
            pipe.Snapshot());

        Assert.Equal(FightId, before.Frame!.FightId);
        Assert.Equal(before.Generation, before.Frame.Generation);
        Assert.Equal(1, before.Frame.FirstTurn);
        Assert.Equal(1, before.Frame.LastTurn);
        Assert.Equal(ActorId, before.Frame.ActorId);
        Assert.Equal(C.EliotropeBreedId, before.Frame.BreedId);
        Assert.Equal(before.DecoderEpoch, pipe.Decoder.CurrentLocalEpoch(Pid));
        Assert.True(before.LastLocalDecoderOrder > 0);
        Assert.Equal(FightId, before.MarkEvidence!.FightId);
        Assert.Equal(before.Generation, before.MarkEvidence.Generation);
        Assert.Equal(ActorId, before.MarkEvidence.Marks[51].Author);
        Assert.Equal(261, Assert.Single(before.MarkEvidence.Marks[51].Cells));
        var observation = Assert.Single(before.Observations);
        Assert.Equal(C.ComboEventKind.MarkPlaced, observation.Kind);
        Assert.Equal(before.Generation, observation.Generation);
        Assert.False(observation.ServerConfirmed);
        Assert.Equal(0, observation.ActionId);
        Assert.Equal(0, observation.Correlation);
        Assert.Null(before.ActionEvidence);
        Assert.False(before.ServerProvenanceEstablished);
        Assert.False(before.CanAuthorizeCast);
        Assert.False(before.Review(C.ComboKind.EliotropeRedirect,
            new C.ComboEvidence(TargetId: 99, FirstMarkId: 51)).Complete);

        pipe.Feed("GameActionFightEvent", false, Activate());
        var after = Assert.IsType<F06App::GivreClone.App.Services.CombatComboIngressSnapshot>(
            pipe.Snapshot());
        Assert.Equal(2, after.Observations.Count);
        Assert.Equal(C.ComboEventKind.MarkActivated, after.Observations[1].Kind);
        Assert.True(after.MarkEvidence!.Marks[51].Active);
        Assert.False(before.MarkEvidence.Marks[51].Active);
        Assert.Single(before.Observations); // immutable pre-activation snapshot
    }

    [Fact]
    public void Direction_masks_ack_and_unattributed_packets_cannot_create_combo_evidence()
    {
        var pipe = new Pipeline();
        pipe.Start();
        pipe.Feed("GameActionFightEvent", true, Place());
        pipe.Feed("GameActionAcknowledgementRequest", true);
        pipe.Feed("SequenceEndEvent", false);
        var attacker = pipe.Feed("GameActionFightEvent", false,
            V("GameActionFightEvent", "source_id", ActorId + 1));
        Assert.NotNull(attacker);
        Assert.Empty(pipe.Snapshot()!.Observations);
        Assert.Empty(pipe.Snapshot()!.Marks);

        var unobserved = attacker! with { DecoderOrder = attacker.DecoderOrder + 10 };
        Assert.False(pipe.Decoder.IsCurrentLocalEpoch(unobserved));
        pipe.Dispatch(unobserved);
        Assert.Empty(pipe.Snapshot()!.Observations);

        pipe.Feed("GameActionFightEvent", false, Place());
        Assert.Single(pipe.Snapshot()!.Observations);
        Assert.False(pipe.Snapshot()!.Review(C.ComboKind.RogueWall, new C.ComboEvidence()).Complete);
        Assert.All(pipe.Snapshot()!.Observations, e => Assert.False(e.ServerConfirmed));
    }

    [Fact]
    public void Stale_callback_reversed_order_and_pid_reconnect_fail_closed()
    {
        var pipe = new Pipeline();
        pipe.Start();
        var oldMark = pipe.Feed("GameActionFightEvent", false, Place());
        Assert.NotNull(oldMark);
        var saved = pipe.Snapshot()!;

        // Replaying an already consumed packet can neither duplicate marks
        // nor reinterpret an earlier frame after other callbacks.
        pipe.Dispatch(oldMark!);
        Assert.Single(pipe.Snapshot()!.Observations);

        pipe.Decoder.Forget(Pid);
        Assert.Null(pipe.Snapshot()); // service snapshot must require CURRENT epoch
        pipe.Dispatch(oldMark!);
        Assert.Null(pipe.Snapshot());

        // Same PID/fight/map on reconnect is a NEW LOCAL epoch, not a
        // verifiable server connection generation.
        pipe.Start();
        var resumed = pipe.Snapshot()!;
        Assert.NotEqual(saved.DecoderEpoch, resumed.DecoderEpoch);
        Assert.NotEqual(saved.Generation, resumed.Generation);
        Assert.Empty(resumed.Observations);
        Assert.Empty(resumed.Marks);
        pipe.Dispatch(oldMark!);
        Assert.Empty(pipe.Snapshot()!.Observations);
        pipe.Feed("GameActionFightEvent", false, Place());
        Assert.Single(pipe.Snapshot()!.Observations);
        Assert.False(pipe.Snapshot()!.ServerProvenanceEstablished);
        Assert.False(pipe.Snapshot()!.CanAuthorizeCast);
    }

    [Fact]
    public void Reversed_decoder_callback_delivery_cannot_replay_an_older_fight_start()
    {
        var pipe = new Pipeline();
        pipe.Feed("MapCurrentEvent", false, V("MapCurrentEvent", "map_id", MapId));
        pipe.Decoder.Packet -= pipe.Dispatch; // hold two real decoded callbacks
        var earlier = pipe.Feed("FightStartingEvent", false,
            V("FightStartingEvent", "fight_id", FightId));
        var later = pipe.Feed("FightTurnEvent", false,
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", ActorId));
        Assert.NotNull(earlier);
        Assert.NotNull(later);
        Assert.True(later!.DecoderOrder > earlier!.DecoderOrder);
        pipe.Dispatch(later);
        pipe.Dispatch(earlier); // lower order must be rejected by Admit
        Assert.Null(pipe.Ingress.Snapshot(Pid, FightId, MapId,
            pipe.Decoder.CurrentLocalEpoch(Pid)));

        // A new start decodes at a new order, and only that one creates a window.
        pipe.Decoder.Packet += pipe.Dispatch;
        pipe.Feed("FightStartingEvent", false, V("FightStartingEvent", "fight_id", FightId));
        pipe.Feed("FightTurnEvent", false,
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", ActorId));
        Assert.NotNull(pipe.Snapshot());
        Assert.Equal(1, pipe.Snapshot()!.Turn);
        Assert.False(pipe.Snapshot()!.ServerProvenanceEstablished);
    }

    [Fact]
    public void Cross_decoder_packet_and_record_clone_are_never_locally_attributed()
    {
        var current = new Decoder(Catalog);
        var foreign = new Decoder(Catalog);
        Packet? original = null;
        Packet? elsewhere = null;
        current.Packet += p => original = p;
        foreign.Packet += p => elsewhere = p;
        var framed = Wire("GameActionFightEvent", Place());
        current.Feed(Pid, false, framed);
        foreign.Feed(Pid, false, framed);
        Assert.NotNull(original);
        Assert.NotNull(elsewhere);
        Assert.Equal(original!.DecoderEpoch, elsewhere!.DecoderEpoch);
        Assert.Equal(original.DecoderOrder, elsewhere.DecoderOrder);
        Assert.True(current.IsCurrentLocalEpoch(original));
        Assert.False(current.IsCurrentLocalEpoch(original with { }));
        Assert.False(current.IsCurrentLocalEpoch(elsewhere));
        Assert.False(foreign.IsCurrentLocalEpoch(original));
    }
}
