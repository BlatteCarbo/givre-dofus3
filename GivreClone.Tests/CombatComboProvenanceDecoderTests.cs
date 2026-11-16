extern alias F06App;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Buffer = F06App::GivreClone.App.Services.CombatComboIngressBuffer;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using C = F06App::GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

/// <summary>
/// Synthetic mapped wire fixtures exercise local decoder epochs/order.
/// They do not demonstrate authenticated TCP provenance or server ACKs.
/// </summary>
public sealed class CombatComboProvenanceDecoderTests
{
    private const uint Pid = 1009;
    private const long Fight = 406;
    private const long Map = 191104002;
    private const long Actor = 41234567890;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    static CombatComboProvenanceDecoderTests()
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

    private static ProtoField[] Mark(int id = 19) =>
    [
        M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
            V("FightMark", "mark_id", id),
            V("FightMark", "mark_type", 4),
            V("FightMark", "author_id", Actor),
            M("FightMark", "cells", V("MarkedCell", "gask", 261)))),
        V("GameActionFightEvent", "source_id", Actor)
    ];

    // The wire is protobuf-frame length prefixed, as CaptureDecoder.Feed expects.
    private static byte[] Wire(string name, params ProtoField[] payload)
    {
        var body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name), payload);
        var length = new List<byte>();
        uint value = (uint)body.Length;
        do
        {
            byte b = (byte)(value & 127);
            value >>= 7;
            length.Add(value == 0 ? b : (byte)(b | 128));
        } while (value != 0);
        return length.Concat(body).ToArray();
    }

    private static Packet Receive(Decoder decoder, string name,
        params ProtoField[] payload)
    {
        Packet? captured = null;
        void Handler(Packet p) => captured = p;
        decoder.Packet += Handler;
        try { decoder.Feed(Pid, false, Wire(name, payload)); }
        finally { decoder.Packet -= Handler; }
        return Assert.IsType<Packet>(captured);
    }

    [Fact]
    public void Decoder_stamps_real_feed_order_and_renews_epoch_after_forget()
    {
        var decoder = new Decoder(Catalog);
        var wire = Wire("GameActionFightEvent", Mark());
        var frames = new List<Packet>();
        decoder.Packet += frames.Add;
        decoder.Feed(Pid, false, wire[..2]);
        Assert.Empty(frames);
        decoder.Feed(Pid, false, wire[2..]);
        var first = Assert.Single(frames);
        Assert.True(first.PayloadDecoded);
        Assert.True(first.DecoderEpoch > 0);
        Assert.True(first.DecoderOrder > 0);
        Assert.True(decoder.IsCurrentLocalEpoch(first));

        var second = Receive(decoder, "GameActionFightEvent", Mark(20));
        Assert.Equal(first.DecoderEpoch, second.DecoderEpoch);
        Assert.True(second.DecoderOrder > first.DecoderOrder);
        decoder.Forget(Pid);
        Assert.False(decoder.IsCurrentLocalEpoch(first));
        Assert.False(decoder.IsCurrentLocalEpoch(second));
        var reconnected = Receive(decoder, "GameActionFightEvent", Mark(21));
        Assert.True(reconnected.DecoderEpoch > second.DecoderEpoch);
        Assert.True(reconnected.DecoderOrder > second.DecoderOrder);
        Assert.True(decoder.IsCurrentLocalEpoch(reconnected));

        // Manually constructed packets have no decoder identity. A decoder
        // stamp alone, even when current, has NO TCP peer authenticity.
        var synthetic = new Packet(Pid, false, "jwe", "GameActionFightEvent",
            Mark(), Array.Empty<byte>());
        Assert.False(decoder.IsCurrentLocalEpoch(synthetic));
        Assert.Equal(0, synthetic.DecoderEpoch);
        Assert.Equal(0, synthetic.DecoderOrder);
    }

    [Fact]
    public void Reconnect_and_delayed_old_callback_cannot_reopen_new_diagnostic_window()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        var start = Receive(decoder, "FightStartingEvent",
            V("FightStartingEvent", "fight_id", Fight));
        Assert.True(ingress.Admit(start, decoder.IsCurrentLocalEpoch(start)));
        int firstGeneration = ingress.BeginFromPacket(start, Fight, Map, Actor);
        Assert.True(firstGeneration > 0);

        var turn = Receive(decoder, "FightTurnEvent",
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", Actor));
        Assert.True(ingress.Admit(turn, decoder.IsCurrentLocalEpoch(turn)));
        ingress.TurnObservedFromPacket(turn, Fight, Map, 1, Actor);

        var mark = Receive(decoder, "GameActionFightEvent", Mark());
        Assert.True(ingress.Admit(mark, decoder.IsCurrentLocalEpoch(mark)));
        var observation = Assert.IsType<C.ComboObservation>(
            ingress.ObserveFromPacket(mark, Fight, Map));
        Assert.False(observation.ServerConfirmed);
        Assert.Equal(firstGeneration, observation.Generation);

        decoder.Forget(Pid);
        Assert.False(decoder.IsCurrentLocalEpoch(start));
        Assert.False(ingress.Admit(mark, decoder.IsCurrentLocalEpoch(mark)));
        var restart = Receive(decoder, "FightStartingEvent",
            V("FightStartingEvent", "fight_id", Fight));
        Assert.True(ingress.Admit(restart, decoder.IsCurrentLocalEpoch(restart)));
        int newGeneration = ingress.BeginFromPacket(restart, Fight, Map, Actor);
        Assert.True(newGeneration > firstGeneration);
        Assert.Empty(ingress.Snapshot(Pid, Fight, Map)!.Observations);
        Assert.False(ingress.Admit(turn, decoder.IsCurrentLocalEpoch(turn)));
        Assert.False(ingress.Admit(mark, decoder.IsCurrentLocalEpoch(mark)));
        Assert.Null(ingress.ObserveFromPacket(mark, Fight, Map));
        Assert.Equal(newGeneration, ingress.Snapshot(Pid, Fight, Map)!.Generation);

        // Still not a TCP identity: an old relay callback that enters Feed
        // AFTER Forget is stamped as new. Without a new valid turn it must
        // not enter the history.
        var ambiguous = Receive(decoder, "GameActionFightEvent", Mark(22));
        Assert.True(ingress.Admit(ambiguous, decoder.IsCurrentLocalEpoch(ambiguous)));
        ingress.TurnObservedFromPacket(ambiguous, Fight, Map, 1, Actor);
        var emitted = ingress.ObserveFromPacket(ambiguous, Fight, Map);
        Assert.Null(emitted); // mark packet cannot establish a new turn
        var currentTurn = Receive(decoder, "FightTurnEvent",
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", Actor));
        Assert.True(ingress.Admit(currentTurn, decoder.IsCurrentLocalEpoch(currentTurn)));
        ingress.TurnObservedFromPacket(currentTurn, Fight, Map, 1, Actor);
        var untraceableOldSocket = Receive(decoder, "GameActionFightEvent", Mark(23));
        Assert.True(ingress.Admit(untraceableOldSocket,
            decoder.IsCurrentLocalEpoch(untraceableOldSocket)));
        Assert.False(Assert.IsType<C.ComboObservation>(
            ingress.ObserveFromPacket(untraceableOldSocket, Fight, Map)).ServerConfirmed);
        Assert.False(ingress.Snapshot(Pid, Fight, Map)!.ServerProvenanceEstablished);
    }

    [Fact]
    public void Reversed_order_and_direction_masks_are_refused_without_server_proof()
    {
        var decoder = new Decoder(Catalog);
        var ingress = new Buffer();
        var first = Receive(decoder, "FightStartingEvent",
            V("FightStartingEvent", "fight_id", Fight));
        var second = Receive(decoder, "FightTurnEvent",
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", Actor));
        Assert.True(ingress.Admit(second, decoder.IsCurrentLocalEpoch(second)));
        Assert.False(ingress.Admit(first, decoder.IsCurrentLocalEpoch(first)));
        Assert.Equal(0, ingress.BeginFromPacket(first, Fight, Map, Actor));
        Assert.Null(ingress.Snapshot(Pid, Fight, Map));

        var start = Receive(decoder, "FightStartingEvent",
            V("FightStartingEvent", "fight_id", Fight));
        Assert.True(ingress.Admit(start, decoder.IsCurrentLocalEpoch(start)));
        Assert.True(ingress.BeginFromPacket(start, Fight, Map, Actor) > 0);
        var turn = Receive(decoder, "FightTurnEvent",
            V("FightTurnEvent", "round", 1),
            V("FightTurnEvent", "character_id", Actor));
        Assert.True(ingress.Admit(turn, decoder.IsCurrentLocalEpoch(turn)));
        ingress.TurnObservedFromPacket(turn, Fight, Map, 1, Actor);
        var place = Receive(decoder, "GameActionFightEvent", Mark());

        Assert.True(ingress.Admit(place, decoder.IsCurrentLocalEpoch(place)));
        Assert.Null(ingress.ObserveFromPacket(place with { ClientToServer = true }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { PayloadDecoded = false }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { Name = "GameActionAcknowledgementRequest" }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { Name = "SequenceEndEvent" }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { Pid = Pid + 1 }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { DecoderEpoch = 0 }, Fight, Map));
        Assert.Null(ingress.ObserveFromPacket(place with { DecoderOrder = 0 }, Fight, Map));
        // The genuine packet is consumed once, even when the current fight is
        // wrong: it must never become admissible on a later replay.
        Assert.Null(ingress.ObserveFromPacket(place, Fight + 1, Map));
        Assert.Null(ingress.ObserveFromPacket(place, Fight, Map));

        // Exercise the map mismatch independently with a new decoded packet.
        // It too is one-shot and cannot be recycled against the correct map.
        var wrongMap = Receive(decoder, "GameActionFightEvent", Mark(20));
        Assert.True(ingress.Admit(wrongMap, decoder.IsCurrentLocalEpoch(wrongMap)));
        Assert.Null(ingress.ObserveFromPacket(wrongMap, Fight, Map + 1));
        Assert.Null(ingress.ObserveFromPacket(wrongMap, Fight, Map));

        // Positive path requires a fresh, subsequently decoded/admitted packet.
        var fresh = Receive(decoder, "GameActionFightEvent", Mark(21));
        Assert.True(ingress.Admit(fresh, decoder.IsCurrentLocalEpoch(fresh)));
        var recorded = Assert.IsType<C.ComboObservation>(
            ingress.ObserveFromPacket(fresh, Fight, Map));
        Assert.False(recorded.ServerConfirmed);
        Assert.Equal(21, recorded.MarkId);
        Assert.Equal(0, recorded.Correlation);
        Assert.Equal(0, recorded.SpellId);
        Assert.Equal(0, recorded.Grade);
        Assert.Equal(0, recorded.ActionId);
        Assert.Single(ingress.Snapshot(Pid, Fight, Map)!.Observations);
        Assert.Null(ingress.ObserveFromPacket(fresh, Fight, Map)); // same stamp never repeated
    }

    [Fact]
    public void Uninitialized_service_preserves_null_safe_combo_snapshot_facade()
    {
        // FormatterServices/RuntimeHelpers test fixtures deliberately skip
        // field constructors. Preserve the prime's null-conditional ingress.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_inFight", flags)!.SetValue(service,
            new ConcurrentDictionary<uint, bool>(new[] { new KeyValuePair<uint, bool>(Pid, true) }));
        typeof(Launch).GetField("_fightId", flags)!.SetValue(service,
            new ConcurrentDictionary<uint, long>(new[] { new KeyValuePair<uint, long>(Pid, Fight) }));
        typeof(Launch).GetField("_serverMapId", flags)!.SetValue(service,
            new ConcurrentDictionary<uint, long>(new[] { new KeyValuePair<uint, long>(Pid, Map) }));
        Assert.Null(service.ReadCombatComboDiagnosticSnapshot(Pid));
        Assert.Empty(service.ReadCombatComboObservations(Pid));
    }

    [Fact]
    public void Formatter_uninitialized_service_ignores_null_combo_ingress_during_mark_dispatch()
    {
        // Match the existing SummonDispatchIntegrationTests fixture: all
        // ConcurrentDictionary fields are initialized, the new combo ingress
        // deliberately remains null as on FormatterServices objects.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var field in typeof(Launch).GetFields(flags))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (var lockName in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            typeof(Launch).GetField(lockName, flags)!.SetValue(service, new object());

        var fight = (IDictionary)typeof(Launch).GetField("_inFight", flags)!.GetValue(service)!;
        fight[Pid] = true;
        var scope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
        try
        {
            var directPacket = new Packet(Pid, false, "jwe", "GameActionFightEvent",
                Mark(), Array.Empty<byte>());
            typeof(Launch).GetMethod("OnDecoded", flags)!.Invoke(service, [directPacket]);
        }
        finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = scope; }
    }
}
