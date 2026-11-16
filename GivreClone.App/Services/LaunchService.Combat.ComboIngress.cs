using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Diagnostic ingress for mark events. A decoded packet only contains a PID
/// and direction: it carries no transport connection identity. In particular,
/// a delayed callback from an old relay connection can have the same PID,
/// fight ID, map and round as a new connection. Until the decoder supplies a
/// checked transport generation, nothing held here is authoritative evidence
/// for VerifiedTacticalSequence or VerifyObservedCombo.
/// </summary>
public sealed class CombatComboIngressBuffer
{
    public const int MaximumObservations = 128;

    private sealed class Window(int generation, long fightId, long mapId, long actorId, long decoderEpoch)
    {
        public readonly int Generation = generation;
        public readonly long FightId = fightId;
        public readonly long MapId = mapId;
        public readonly long ActorId = actorId;
        public readonly long DecoderEpoch = decoderEpoch;
        public readonly List<ClassMechanics.ComboObservation> Observations = new();
        public readonly Dictionary<int, ObservedFightMark> Marks = new();
        public int Turn;
        public long Round;
        public long TurnActor;
        public long Order;
        public int BreedId; // zero until this fighter is explicitly shown
        public bool Truncated;
    }

    private readonly object _gate = new();
    private readonly Dictionary<uint, Window> _windows = new();
    private readonly Dictionary<uint, (long Epoch, long Order, WeakReference<DecodedPacket> Packet)> _lastAdmitted = new();
    private readonly Dictionary<uint, (long Epoch, long Order)> _lastConsumed = new();
    private int _generation;

    /// <summary>
    /// Only a real CaptureDecoder callback in its current LOCAL epoch may
    /// mutate the service's diagnostic window. The per-PID watermark also
    /// rejects reversed delivery after Feed released its lock.
    /// It cannot establish the underlying TCP peer or connection lifetime.
    /// </summary>
    public bool Admit(DecodedPacket packet, bool currentLocalEpoch)
    {
        if (packet is null || !currentLocalEpoch
            || packet.DecoderEpoch <= 0 || packet.DecoderOrder <= 0) return false;
        lock (_gate)
        {
            if (_lastAdmitted.TryGetValue(packet.Pid, out var last)
                && (packet.DecoderEpoch < last.Epoch
                    || packet.DecoderEpoch == last.Epoch && packet.DecoderOrder <= last.Order))
                return false;
            _lastAdmitted[packet.Pid] = (packet.DecoderEpoch, packet.DecoderOrder,
                new WeakReference<DecodedPacket>(packet));
            if (_windows.TryGetValue(packet.Pid, out var window)
                && window.DecoderEpoch != packet.DecoderEpoch)
                _windows.Remove(packet.Pid);
            return true;
        }
    }

    private bool Latest(DecodedPacket packet)
        => _lastAdmitted.TryGetValue(packet.Pid, out var last)
            && last.Epoch == packet.DecoderEpoch && last.Order == packet.DecoderOrder
            && last.Packet.TryGetTarget(out var decoded) && ReferenceEquals(decoded, packet);

    private bool Consume(DecodedPacket packet)
    {
        if (!Latest(packet)) return false;
        if (_lastConsumed.TryGetValue(packet.Pid, out var previous)
            && (packet.DecoderEpoch < previous.Epoch
                || packet.DecoderEpoch == previous.Epoch
                    && packet.DecoderOrder <= previous.Order)) return false;
        _lastConsumed[packet.Pid] = (packet.DecoderEpoch, packet.DecoderOrder);
        return true;
    }

    /// <summary>Start a fresh local diagnostic epoch. Zero means insufficient identity.</summary>
    public int Begin(uint pid, long fightId, long mapId, long actorId)
        => BeginCore(pid, fightId, mapId, actorId, decoderEpoch: 0);

    private int BeginCore(uint pid, long fightId, long mapId, long actorId, long decoderEpoch)
    {
        lock (_gate)
        {
            _windows.Remove(pid);
            if (pid == 0 || fightId <= 0 || mapId <= 0 || actorId == 0
                || _generation == int.MaxValue) return 0;
            var window = new Window(++_generation, fightId, mapId, actorId, decoderEpoch);
            _windows.Add(pid, window);
            return window.Generation;
        }
    }

    public int BeginFromPacket(DecodedPacket packet, long fightId, long mapId, long actorId)
    {
        lock (_gate)
            return !packet.ClientToServer && packet.PayloadDecoded
                && packet.Name == "FightStartingEvent" && Consume(packet)
                ? BeginCore(packet.Pid, fightId, mapId, actorId, packet.DecoderEpoch)
                : 0;
    }

    /// <summary>Map changes, end-of-fight and locally observed session loss revoke this window.</summary>
    public void End(uint pid)
    {
        lock (_gate) _windows.Remove(pid);
    }

    /// <summary>
    /// Revoke diagnostic state for precisely the retired CaptureDecoder epoch.
    /// A late Forget notification must never remove a new window created after
    /// the same PID was reused. This is local lifecycle cleanup, not a server
    /// receipt or a TCP connection-generation proof.
    /// </summary>
    public void EndLocalEpoch(uint pid, long retiredDecoderEpoch)
    {
        if (pid == 0 || retiredDecoderEpoch <= 0) return;
        lock (_gate)
        {
            if (_windows.TryGetValue(pid, out var window)
                && window.DecoderEpoch == retiredDecoderEpoch)
                _windows.Remove(pid);
            if (_lastAdmitted.TryGetValue(pid, out var admitted)
                && admitted.Epoch == retiredDecoderEpoch)
                _lastAdmitted.Remove(pid);
            if (_lastConsumed.TryGetValue(pid, out var consumed)
                && consumed.Epoch == retiredDecoderEpoch)
                _lastConsumed.Remove(pid);
        }
    }

    /// <summary>
    /// The narrow runtime packet dispatcher used by LaunchService.OnDecoded
    /// BEFORE invoking external packet observers. This is the SAME codepath as
    /// offline wire-framed tests, not a second parser or simulated ACK source.
    /// The caller must have admitted this exact decoded object from its current
    /// local epoch. All event methods still consume one-shot and check the
    /// original admission stamp. MapCurrent is handled separately by the
    /// service after its map reducer updates the current map.
    /// </summary>
    public ClassMechanics.ComboObservation? ObserveRuntimeEvent(
        DecodedPacket packet, long currentFightId, long currentMapId, long actorId,
        bool inFight, Func<string, string, int?> fieldNumber)
    {
        if (packet is null || packet.ClientToServer || !packet.PayloadDecoded
            || fieldNumber is null) return null;
        switch (packet.Name)
        {
            case "FightStartingEvent":
                long startingFight = fieldNumber("FightStartingEvent", "fight_id") is int fightField
                    ? PayloadReader.Int(packet.Payload, fightField, 0) : 0;
                BeginFromPacket(packet, startingFight, currentMapId, actorId);
                break;
            case "FightTurnEvent":
                long round = fieldNumber("FightTurnEvent", "round") is int roundField
                    ? PayloadReader.Int(packet.Payload, roundField, 0) : 0;
                TurnObservedFromPacket(packet, currentFightId, currentMapId, round,
                    FightStatsReader.TurnActor(packet.Payload));
                break;
            case "FightFighterShowEvent":
                BreedObservedFromPacket(packet, currentFightId, currentMapId);
                break;
            case "GameActionFightEvent" when inFight:
                return ObserveFromPacket(packet, currentFightId, currentMapId);
            case "FightEndEvent":
            case "MapComplementaryInformationEvent":
            case "AuthenticationTicketAcceptedEvent":
                EndFromPacket(packet);
                break;
        }
        return null;
    }

    public void EndFromPacket(DecodedPacket packet)
    {
        lock (_gate)
            if (!packet.ClientToServer && packet.PayloadDecoded
                && packet.Name is ("FightEndEvent" or "MapComplementaryInformationEvent"
                    or "AuthenticationTicketAcceptedEvent")
                && Consume(packet)) _windows.Remove(packet.Pid);
    }

    public void MapObserved(uint pid, long mapId)
    {
        lock (_gate)
            if (_windows.TryGetValue(pid, out var w) && w.MapId != mapId)
                _windows.Remove(pid);
    }

    public void MapObservedFromPacket(DecodedPacket packet, long mapId)
    {
        lock (_gate)
            if (!packet.ClientToServer && packet.PayloadDecoded
                && packet.Name == "MapCurrentEvent" && Consume(packet))
                MapObserved(packet.Pid, mapId);
    }

    /// <summary>Advance only on mapped, forward server rounds and known actors.</summary>
    public void TurnObserved(uint pid, long fightId, long mapId, long round, long turnActor)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue(pid, out var w)
                || w.FightId != fightId || w.MapId != mapId
                || round <= 0 || turnActor == 0 || round < w.Round
                || (round == w.Round && turnActor == w.TurnActor)
                || w.Turn == int.MaxValue) return;
            // A new round or a different fighter's turn in this round. Never
            // accept marks before one identified server turn was observed.
            w.Round = round;
            w.TurnActor = turnActor;
            ++w.Turn;
        }
    }

    public void TurnObservedFromPacket(DecodedPacket packet, long fightId, long mapId,
        long round, long turnActor)
    {
        lock (_gate)
            if (!packet.ClientToServer && packet.PayloadDecoded
                && packet.Name == "FightTurnEvent" && turnActor != 0
                && FightStatsReader.TurnActor(packet.Payload) == turnActor
                && Consume(packet))
                TurnObserved(packet.Pid, fightId, mapId, round, turnActor);
    }

    /// <summary>
    /// Optional breed join from a decoded FightFighterShowEvent for the SAME
    /// actor and fight window. Neither a client-side class setting nor a mark
    /// type implies the breed of the author. One-shot like other packet hooks.
    /// </summary>
    public void BreedObservedFromPacket(DecodedPacket packet, long fightId, long mapId)
    {
        if (packet is null || packet.ClientToServer || !packet.PayloadDecoded
            || packet.Name != "FightFighterShowEvent") return;
        lock (_gate)
        {
            if (!_windows.TryGetValue(packet.Pid, out var w)
                || w.FightId != fightId || w.MapId != mapId
                || w.DecoderEpoch != packet.DecoderEpoch || !Latest(packet)) return;
            var show = FightStatsReader.ReadShowFighterBreed(packet.Payload);
            if (!show.HasValue || show.Value.FighterId != w.ActorId
                || show.Value.Breed <= 0 || !Consume(packet)) return;
            // Contradictory class observations revoke the class join; they do
            // not silently replace it to make an unsupported combo plausible.
            w.BreedId = w.BreedId == 0 || w.BreedId == show.Value.Breed
                ? show.Value.Breed : -1;
        }
    }

    /// <summary>
    /// Parse the narrow mark subset with a same-window registry. Even a valid
    /// candidate is downgraded to ServerConfirmed=false: ClientToServer=false
    /// is direction metadata, not proof of an active TCP connection generation.
    /// No cast, correlation, spell ID or ACK is manufactured.
    /// </summary>
    public ClassMechanics.ComboObservation? Observe(
        DecodedPacket packet, long currentFightId, long currentMapId)
    {
        if (packet is null || packet.ClientToServer || !packet.PayloadDecoded
            || packet.Name != "GameActionFightEvent") return null;
        lock (_gate)
        {
            if (!_windows.TryGetValue(packet.Pid, out var w)
                || currentFightId != w.FightId || currentMapId != w.MapId
                || w.DecoderEpoch != 0 && packet.DecoderEpoch != w.DecoderEpoch
                || w.Turn <= 0 || w.Order == long.MaxValue) return null;
            // Packet-bound runtime histories use the REAL decoder-local order,
            // including gaps for non-mark frames. This keeps mark chronology
            // comparable to the transport diagnostic, rather than inventing
            // dense pseudo-orders. Legacy detached Observe tests (epoch zero)
            // retain their existing mark-only sequence numbers.
            long markOrder = w.DecoderEpoch > 0 ? packet.DecoderOrder : w.Order + 1;
            if (markOrder <= w.Order) return null;
            var frame = new ClassMechanics.ComboFrame(w.FightId, w.Generation,
                1, w.Turn, w.ActorId, 0);
            var prior = new ClassMechanics.ComboMarkRegistryEvidence(w.FightId,
                w.Generation, w.Marks.ToImmutableDictionary());
            var candidate = ClassMechanics.ReadConfirmedFightMarkObservation(
                packet.Name, inboundFromServer: true, inCurrentFight: true,
                frame, w.Turn, markOrder, packet.Payload, prior);
            if (candidate is null) return null;

            // The raw decoder has no current-transport proof. Do not expose
            // ClassMechanics' parsed candidate as a certified observation.
            var diagnostic = candidate with { ServerConfirmed = false };
            w.Order = markOrder;
            w.Observations.Add(diagnostic);
            if (w.Observations.Count > MaximumObservations)
            {
                w.Observations.RemoveAt(0);
                w.Truncated = true;
            }

            switch (diagnostic.Kind)
            {
                case ClassMechanics.ComboEventKind.MarkPlaced:
                    if (MapFightReader.ReadMark(packet.Payload) is { } mark)
                    {
                        // The registry is diagnostic as well, and must not
                        // grow indefinitely in a long fight with many marks.
                        if (!w.Marks.ContainsKey(mark.Id) && w.Marks.Count == MaximumObservations)
                        {
                            w.Marks.Clear();
                            w.Truncated = true;
                        }
                        w.Marks[mark.Id] = new ObservedFightMark(mark.Id, mark.Author,
                            mark.Type, mark.Cells.ToImmutableArray(), mark.Active);
                    }
                    break;
                case ClassMechanics.ComboEventKind.MarkRemoved:
                    w.Marks.Remove(diagnostic.MarkId);
                    break;
                case ClassMechanics.ComboEventKind.MarkActivated:
                case ClassMechanics.ComboEventKind.MarkDeactivated:
                    if (w.Marks.TryGetValue(diagnostic.MarkId, out var known))
                        w.Marks[diagnostic.MarkId] = known with
                        {
                            Active = diagnostic.Kind == ClassMechanics.ComboEventKind.MarkActivated
                        };
                    break;
            }
            return diagnostic;
        }
    }

    public ClassMechanics.ComboObservation? ObserveFromPacket(
        DecodedPacket packet, long currentFightId, long currentMapId)
    {
        lock (_gate)
            return Consume(packet) ? Observe(packet, currentFightId, currentMapId) : null;
    }

    public CombatComboIngressSnapshot? Snapshot(uint pid, long currentFightId,
        long currentMapId, long? currentDecoderEpoch = null)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue(pid, out var w)
                || w.FightId != currentFightId || w.MapId != currentMapId
                // Explicit 0 means Forgot/unavailable, never a wildcard.
                || currentDecoderEpoch is long expected && (expected <= 0
                    || w.DecoderEpoch != expected)) return null;
            // Deep, detached read-only observations; marks and their cell arrays
            // are immutable as well. Consumers cannot alter future snapshots.
            var marks = w.Marks.ToImmutableDictionary();
            return new CombatComboIngressSnapshot(pid, w.FightId, w.MapId,
                w.Generation, w.Turn, w.Truncated,
                Array.AsReadOnly(w.Observations.ToArray()),
                marks)
            {
                DecoderEpoch = w.DecoderEpoch,
                LastLocalDecoderOrder = _lastConsumed.TryGetValue(pid, out var consumed)
                    && consumed.Epoch == w.DecoderEpoch ? consumed.Order : 0,
                Frame = new ClassMechanics.ComboFrame(w.FightId, w.Generation,
                    1, w.Turn, w.ActorId, Math.Max(0, w.BreedId)),
                MarkEvidence = new ClassMechanics.ComboMarkRegistryEvidence(
                    w.FightId, w.Generation, marks)
            };
        }
    }
}

public sealed record CombatComboIngressSnapshot(
    uint Pid, long FightId, long MapId, int Generation, int Turn,
    bool Truncated, IReadOnlyList<ClassMechanics.ComboObservation> Observations,
    ImmutableDictionary<int, ObservedFightMark> Marks)
{
    public bool ServerProvenanceEstablished => false;
    public bool CanAuthorizeCast => false;
    public long DecoderEpoch { get; init; }
    public long LastLocalDecoderOrder { get; init; }
    /// <summary>Observed fight/turn/actor, with breed zero until supported by
    /// a same-window FightFighterShowEvent. Not a server-certified frame.</summary>
    public ClassMechanics.ComboFrame? Frame { get; init; }
    /// <summary>Immutable mark ownership/status from accepted diagnostic
    /// packets only. Never sufficient to prove a correlated cast or effect.</summary>
    public ClassMechanics.ComboMarkRegistryEvidence? MarkEvidence { get; init; }
    /// <summary>These packets never establish a source spell, learned grade,
    /// target, correlation, server cast receipt or independent action evidence.</summary>
    public ClassMechanics.ComboEvidence? ActionEvidence => null;

    /// <summary>Explicitly fail closed when a caller tries to turn an ingress
    /// diagnostic stream and external descriptors into verified combo proof.</summary>
    public ClassMechanics.ComboProof Review(ClassMechanics.ComboKind kind,
        ClassMechanics.ComboEvidence? proposedEvidence)
        => new(false, 0, 0,
            "diagnostic ingress lacks authenticated TCP identity and correlated server cast receipts");
}

public sealed partial class LaunchService
{
    private readonly CombatComboIngressBuffer _comboIngress = new();

    /// <summary>
    /// Read-only diagnostic stream. These observations are never server-certified
    /// until the transport/decoder gives this ingress a verifiable connection
    /// generation. Tactical verification must still require ServerConfirmed.
    /// </summary>
    public CombatComboIngressSnapshot? ReadCombatComboDiagnosticSnapshot(uint pid)
        => IsInFight(pid)
            ? _comboIngress?.Snapshot(pid, CurrentFightId(pid),
                _serverMapId.GetValueOrDefault(pid), _decoder?.CurrentLocalEpoch(pid) ?? 0)
            : null;

    public ClassMechanics.ComboProof ReadCombatComboDiagnosticReview(uint pid,
        ClassMechanics.ComboKind kind, ClassMechanics.ComboEvidence? evidence)
        => ReadCombatComboDiagnosticSnapshot(pid)?.Review(kind, evidence)
            ?? new ClassMechanics.ComboProof(false, 0, 0,
                "no current decoder-bound combo diagnostic snapshot");

    public IReadOnlyList<ClassMechanics.ComboObservation> ReadCombatComboObservations(uint pid)
        => ReadCombatComboDiagnosticSnapshot(pid)?.Observations
            ?? Array.Empty<ClassMechanics.ComboObservation>();
}
