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
/// Decoder-local lifecycle tests for the MITM/Socket feed contract. Neither
/// path currently supplies a verified connection token with (PID, direction,
/// wire). These synthetic frames cannot certify a server receipt.
/// </summary>
public sealed class CombatIngressTransportGenerationTests
{
    private const uint Pid = 40019;
    private const long FightId = 412;
    private const long MapId = 191104002;
    private const long Actor = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    static CombatIngressTransportGenerationTests()
    {
        MapFightReader.UseCatalog(Catalog.FieldNumber);
        FightStatsReader.UseCatalog(Catalog.FieldNumber);
    }

    private static int Field(string message, string name) => Catalog.FieldNumber(message, name)
        ?? throw new InvalidOperationException($"Missing mapping {message}.{name}");

    private static ProtoField V(string message, string field, long value) => new()
        { Field = Field(message, field), Wire = WireType.Varint, Varint = unchecked((ulong)value) };

    private static ProtoField M(string message, string field, params ProtoField[] fields) => new()
        { Field = Field(message, field), Wire = WireType.Len, Message = fields.ToList() };

    private static ProtoField[] Mark(int id = 31) =>
    [
        M("GameActionFightEvent", "mark_cells",
            M("MarkCells", "mark",
                V("FightMark", "mark_id", id),
                V("FightMark", "mark_type", 4),
                V("FightMark", "author_id", Actor),
                M("FightMark", "cells", V("MarkedCell", "gask", 261)))),
        V("GameActionFightEvent", "source_id", Actor)
    ];

    private static byte[] Framed(string name, IReadOnlyList<ProtoField> payload)
    {
        byte[] body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name), payload);
        var header = new List<byte>();
        uint remaining = (uint)body.Length;
        do
        {
            byte b = (byte)(remaining & 0x7f);
            remaining >>= 7;
            header.Add(remaining == 0 ? b : (byte)(b | 0x80));
        } while (remaining != 0);
        return header.Concat(body).ToArray();
    }

    private static Packet Receive(Decoder decoder, string name, bool up = false,
        params ProtoField[] payload)
    {
        Packet? captured = null;
        void Handler(Packet packet) => captured = packet;
        decoder.Packet += Handler;
        try { decoder.Feed(Pid, up, Framed(name, payload)); }
        finally { decoder.Packet -= Handler; }
        return Assert.IsType<Packet>(captured);
    }

    private static int Begin(Decoder decoder, Buffer ingress)
    {
        var start = Receive(decoder, "FightStartingEvent",
            false, V("FightStartingEvent", "fight_id", FightId));
        Assert.True(ingress.Admit(start, decoder.IsCurrentLocalEpoch(start)));
        return ingress.BeginFromPacket(start, FightId, MapId, Actor);
    }

    private static void Turn(Decoder decoder, Buffer ingress)
    {
        var turn = Receive(decoder, "FightTurnEvent", false,
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", Actor));
        Assert.True(ingress.Admit(turn, decoder.IsCurrentLocalEpoch(turn)));
        ingress.TurnObservedFromPacket(turn, FightId, MapId, 1, Actor);
    }

    private static C.ComboObservation Place(Decoder decoder, Buffer ingress, int id = 31)
    {
        var packet = Receive(decoder, "GameActionFightEvent", false, Mark(id));
        Assert.True(ingress.Admit(packet, decoder.IsCurrentLocalEpoch(packet)));
        return Assert.IsType<C.ComboObservation>(
            ingress.ObserveFromPacket(packet, FightId, MapId));
    }

    [Fact]
    public void Decoder_forget_notifies_exact_retired_epoch_and_removes_only_its_diagnostics()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        var notifications = new List<(uint Pid, long Epoch)>();
        decoder.LocalEpochRevoked += (pid, epoch) =>
        {
            notifications.Add((pid, epoch));
            ingress.EndLocalEpoch(pid, epoch);
        };

        int firstGeneration = Begin(decoder, ingress);
        Assert.True(firstGeneration > 0);
        Turn(decoder, ingress);
        Assert.False(Place(decoder, ingress).ServerConfirmed);
        long oldEpoch = decoder.CurrentLocalEpoch(Pid);
        Assert.True(oldEpoch > 0);
        Assert.Single(ingress.Snapshot(Pid, FightId, MapId, oldEpoch)!.Observations);

        decoder.Forget(Pid);
        Assert.Equal((Pid, oldEpoch), Assert.Single(notifications));
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
        Assert.Null(ingress.Snapshot(Pid, FightId, MapId));
        decoder.Forget(Pid); // idempotent: no duplicate retired epoch
        Assert.Single(notifications);

        int nextGeneration = Begin(decoder, ingress);
        long freshEpoch = decoder.CurrentLocalEpoch(Pid);
        Assert.True(nextGeneration > firstGeneration);
        Assert.True(freshEpoch > oldEpoch);
        Assert.Empty(ingress.Snapshot(Pid, FightId, MapId, freshEpoch)!.Observations);
        ingress.EndLocalEpoch(Pid, oldEpoch); // delayed duplicate closure
        Assert.NotNull(ingress.Snapshot(Pid, FightId, MapId, freshEpoch));
        Assert.False(ingress.Snapshot(Pid, FightId, MapId, freshEpoch)!.ServerProvenanceEstablished);
    }

    [Fact]
    public void Delayed_old_close_notification_cannot_revoke_same_pid_new_epoch()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        var delayed = new List<(uint Pid, long Epoch)>();
        decoder.LocalEpochRevoked += (pid, epoch) => delayed.Add((pid, epoch));

        Assert.True(Begin(decoder, ingress) > 0);
        long oldEpoch = decoder.CurrentLocalEpoch(Pid);
        decoder.Forget(Pid);

        // Simulate a resumed MITM/Socket decoder before the previous close
        // callback reaches the diagnostic subscriber.
        Assert.True(Begin(decoder, ingress) > 0);
        long freshEpoch = decoder.CurrentLocalEpoch(Pid);
        Assert.True(freshEpoch > oldEpoch);
        Turn(decoder, ingress);
        Assert.False(Place(decoder, ingress).ServerConfirmed);

        foreach (var (pid, epoch) in delayed)
            ingress.EndLocalEpoch(pid, epoch);
        var fresh = ingress.Snapshot(Pid, FightId, MapId, freshEpoch)!;
        Assert.Single(fresh.Observations);
        Assert.Equal(fresh.Generation, fresh.Observations[0].Generation);
        Assert.False(fresh.CanAuthorizeCast);
    }

    [Fact]
    public void Old_decoded_callback_is_rejected_after_forget_but_new_feed_is_not_tcp_proof()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        Assert.True(Begin(decoder, ingress) > 0);
        Turn(decoder, ingress);
        var oldPacket = Receive(decoder, "GameActionFightEvent", false, Mark());
        Assert.True(ingress.Admit(oldPacket, decoder.IsCurrentLocalEpoch(oldPacket)));
        Assert.False(Assert.IsType<C.ComboObservation>(
            ingress.ObserveFromPacket(oldPacket, FightId, MapId)).ServerConfirmed);

        decoder.Forget(Pid);
        Assert.False(decoder.IsCurrentLocalEpoch(oldPacket));
        Assert.False(ingress.Admit(oldPacket, decoder.IsCurrentLocalEpoch(oldPacket)));
        Assert.True(Begin(decoder, ingress) > 0);
        Assert.False(ingress.Admit(oldPacket, decoder.IsCurrentLocalEpoch(oldPacket)));
        Assert.Null(ingress.ObserveFromPacket(oldPacket, FightId, MapId));

        // An old relay pump entering Feed *after* Forget receives a fresh
        // local decoder stamp. Only transport-owned link provenance can
        // distinguish that from a legitimate new connection, so every such
        // decoded mark remains diagnostic and cannot imply an ACK.
        Turn(decoder, ingress);
        var ambiguous = Place(decoder, ingress, 44);
        Assert.False(ambiguous.ServerConfirmed);
        Assert.Equal(0, ambiguous.ActionId);
        Assert.Equal(0, ambiguous.Correlation);
        var snapshot = ingress.Snapshot(Pid, FightId, MapId,
            decoder.CurrentLocalEpoch(Pid))!;
        Assert.Null(snapshot.ActionEvidence);
        Assert.False(snapshot.CanAuthorizeCast);
        Assert.False(snapshot.ServerProvenanceEstablished);
        Assert.False(snapshot.Review(C.ComboKind.EliotropeRedirect,
            new C.ComboEvidence(TargetId: 23)).Complete);
    }

    [Fact]
    public void Wrong_direction_and_client_ack_never_promote_combat_observations()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        Assert.True(Begin(decoder, ingress) > 0);
        Turn(decoder, ingress);

        var outboundMark = Receive(decoder, "GameActionFightEvent", true, Mark());
        Assert.True(ingress.Admit(outboundMark, decoder.IsCurrentLocalEpoch(outboundMark)));
        Assert.Null(ingress.ObserveFromPacket(outboundMark, FightId, MapId));

        var clientAck = Receive(decoder, "GameActionAcknowledgementRequest", true,
            V("GameActionAcknowledgementRequest", "action_id", 778));
        Assert.True(ingress.Admit(clientAck, decoder.IsCurrentLocalEpoch(clientAck)));
        Assert.Null(ingress.ObserveFromPacket(clientAck, FightId, MapId));
        Assert.Empty(ingress.Snapshot(Pid, FightId, MapId,
            decoder.CurrentLocalEpoch(Pid))!.Observations);

        Assert.False(Place(decoder, ingress).ServerConfirmed);
        var history = ingress.Snapshot(Pid, FightId, MapId,
            decoder.CurrentLocalEpoch(Pid))!;
        Assert.Single(history.Observations);
        Assert.Equal(0, history.Observations[0].ActionId);
        Assert.Equal(0, history.Observations[0].Correlation);
        Assert.False(history.ServerProvenanceEstablished);
        Assert.False(history.CanAuthorizeCast);
    }

    [Fact]
    public void New_game_session_ticket_revokes_diagnostics_but_a_client_ticket_does_not()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        Assert.True(Begin(decoder, ingress) > 0);
        Turn(decoder, ingress);
        Assert.False(Place(decoder, ingress).ServerConfirmed);
        long epoch = decoder.CurrentLocalEpoch(Pid);

        // A synthetic nonempty payload keeps the test about ingress
        // admission, not the optional shape of this version's login event.
        static ProtoField Opaque() => new()
            { Field = 1, Wire = WireType.Varint, Varint = 1 };
        var clientTicket = Receive(decoder, "AuthenticationTicketAcceptedEvent",
            true, Opaque());
        Assert.True(ingress.Admit(clientTicket, decoder.IsCurrentLocalEpoch(clientTicket)));
        ingress.EndFromPacket(clientTicket);
        Assert.Single(ingress.Snapshot(Pid, FightId, MapId, epoch)!.Observations);

        var serverTicket = Receive(decoder, "AuthenticationTicketAcceptedEvent",
            false, Opaque());
        Assert.True(ingress.Admit(serverTicket, decoder.IsCurrentLocalEpoch(serverTicket)));
        ingress.EndFromPacket(serverTicket);
        Assert.Null(ingress.Snapshot(Pid, FightId, MapId, epoch));
        // Even a synthetic login-like packet does not assert real TCP origin:
        // no new fight window or positive action receipt can be inferred.
        Assert.Equal(epoch, decoder.CurrentLocalEpoch(Pid));
    }
}
