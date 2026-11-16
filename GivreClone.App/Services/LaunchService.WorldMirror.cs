using System;
using System.Collections.Generic;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private readonly object _relayPortGate = new();
    private readonly object _worldMirrorGate = new();
    private readonly WorldMirrorFrameCache _worldMirrorFrames = new();
    private readonly Dictionary<uint, WorldMirrorSession> _worldMirrors = new();
    private readonly Dictionary<uint, uint> _visibleToSocket = new();
    private WorldMirrorAcceptor? _worldMirrorAcceptor;

    /// <summary>Identité du catalogue et scope réellement choisi pour cette session Socket. Un scope nul refuse le miroir.</summary>
    public void NoteSocketProtocolScope(uint pid, string? catalogIdentity, string? runtimeScope)
        => _worldMirrorFrames.NoteScope(pid, catalogIdentity, runtimeScope);

    /// <summary>Génération de la session Socket. Un miroir armé sur une génération plus ancienne ne se rafraîchit pas.</summary>
    public void NoteSocketSessionGeneration(uint pid, int generation)
        => _worldMirrorFrames.NoteGeneration(pid, generation);

    /// <summary>Mémorise une réponse de login déjà décodée (IdentificationSuccess ou SelectServerSuccess), préfixée une fois.</summary>
    public void RememberMirrorLogin(uint pid, string slot, byte[] framed)
        => _worldMirrorFrames.RememberLogin(pid, slot, framed);

    public WorldMirrorLive CaptureWorldMirrorLive(uint pid, long nowMs)
    {
        var scope = _worldMirrorFrames.Scope(pid);
        return new WorldMirrorLive
        {
            NowMs = nowMs,
            InFight = IsInFight(pid),
            MapId = CurrentMap(pid) ?? 0,
            CharacterId = CharacterId(pid),
            PipelineGeneration = _worldMirrorFrames.Generation(pid),
            UpstreamWritable = HasSocketInjector(pid),
            CatalogIdentity = scope.Catalog,
            RuntimeScope = scope.Runtime,
            Snapshot = _worldMirrorFrames.Copy(pid),
        };
    }

    public WorldMirrorArmStatus TryArmWorldMirror(uint pid, long characterId, int pipelineGeneration, long attemptStartedMs)
    {
        lock (_worldMirrorGate)
        {
            if (_worldMirrors.TryGetValue(pid, out var existing) && existing.Stage != WorldMirrorSession.StageOff)
                return WorldMirrorArmStatus.AlreadyArmed;
        }
        var scope = _worldMirrorFrames.Scope(pid);
        var session = new WorldMirrorSession(line => TransportLog.Line(pid, line));
        var status = session.TryArm(new WorldMirrorArmRequest(
            characterId,
            CharacterId(pid),
            CurrentMap(pid) ?? 0,
            pipelineGeneration,
            attemptStartedMs,
            IsInFight(pid),
            HasSocketInjector(pid),
            scope.Catalog,
            scope.Runtime,
            _worldMirrorFrames.Copy(pid),
            _worldMirrorFrames.Generation(pid)));
        if (status != WorldMirrorArmStatus.Armed) return status;
        lock (_worldMirrorGate)
        {
            if (_worldMirrors.TryGetValue(pid, out var existing) && existing.Stage != WorldMirrorSession.StageOff)
                return WorldMirrorArmStatus.AlreadyArmed;
            _worldMirrors[pid] = session;
        }
        return WorldMirrorArmStatus.Armed;
    }

    /// <summary>Annule le miroir et ferme seulement la fenêtre visible. reason nul : pas de journal « session préservée ».</summary>
    public void CancelWorldMirror(uint pid, string? reason)
    {
        WorldMirrorSession? session = null;
        lock (_worldMirrorGate)
        {
            if (_worldMirrors.Remove(pid, out var found)) session = found;
            var stale = new List<uint>();
            foreach (var pair in _visibleToSocket)
                if (pair.Value == pid) stale.Add(pair.Key);
            foreach (var visible in stale) _visibleToSocket.Remove(visible);
        }
        if (session is null) return;
        session.Cancel(reason ?? "");
    }

    public void BindWorldMirror(uint visiblePid, uint socketPid)
    {
        if (visiblePid == 0 || !IsSocketPid(socketPid)) return;
        lock (_worldMirrorGate) _visibleToSocket[visiblePid] = socketPid;
    }

    private WorldMirrorAcceptor EnsureWorldMirrorAcceptor()
    {
        if (_worldMirrorAcceptor is not null) return _worldMirrorAcceptor;
        _worldMirrorAcceptor = new WorldMirrorAcceptor(
            SocketForVisibleMirror,
            VisibleMirrorOwned,
            ClassifyMirrorBody,
            (socketPid, slot) => _worldMirrorFrames.LoginFrame(socketPid, slot),
            SessionForMirror,
            socketPid => CaptureWorldMirrorLive(socketPid, Environment.TickCount64),
            (socketPid, frame) => _socketInjectors.TryGetValue(socketPid, out var inject) && inject(frame),
            (pid, line) => TransportLog.Line(pid, line));
        return _worldMirrorAcceptor;
    }

    private bool VisibleMirrorOwned(uint visiblePid)
    {
        lock (_worldMirrorGate)
            return _visibleToSocket.ContainsKey(visiblePid) && MZP9OwnedClientRegistry.IsOwnedAndLive((int)visiblePid);
    }

    private uint? SocketForVisibleMirror(uint visiblePid)
    {
        lock (_worldMirrorGate)
            return _visibleToSocket.TryGetValue(visiblePid, out var socket) ? socket : null;
    }

    private WorldMirrorSession? SessionForMirror(uint socketPid)
    {
        lock (_worldMirrorGate)
            return _worldMirrors.TryGetValue(socketPid, out var session) ? session : null;
    }

    private void PublishMirrorServerFrame(DecodedPacket packet)
    {
        if (packet.ClientToServer || !IsSocketPid(packet.Pid) || packet.RawFrame is not { Length: > 0 }) return;
        var wire = WorldMirrorFraming.Prefix(packet.RawFrame);
        if (!string.IsNullOrEmpty(packet.Name))
        {
            long? mapId = packet.Name is "MapCurrentEvent" or "MapComplementaryInformationEvent"
                ? ReadCatalogMapId(packet.Name, packet.Payload) : null;
            _worldMirrorFrames.Observe(packet.Pid, packet.Name, wire, mapId, IsInFight(packet.Pid));
        }
        SessionForMirror(packet.Pid)?.ForwardServerFrame(wire);
    }

    private long? ReadCatalogMapId(string name, IReadOnlyList<ProtoField> payload)
        => WorldMirrorIds.Positive(_catalog?.FieldNumber(name, "map_id"), payload);

    private WorldMirrorInbound ClassifyMirrorBody(byte[] body)
    {
        if (body is not { Length: > 0 }) return WorldMirrorInbound.Unknown;
        try
        {
            if (ConnectionMessages.TryReadDeviceIdentifier(body, out _))
                return WorldMirrorInbound.Login("IdentificationSuccess");
            if (ConnectionMessages.TryIsSelectServerRequest(body))
                return WorldMirrorInbound.Login("SelectServerSuccess");
        }
        catch { return WorldMirrorInbound.Unknown; }

        if (_catalog is null) return WorldMirrorInbound.Unknown;
        string? name = null;
        IReadOnlyList<ProtoField>? payload = null;
        var scratch = new CaptureDecoder(_catalog);
        scratch.Packet += decoded =>
        {
            if (!decoded.ClientToServer || string.IsNullOrEmpty(decoded.Name)) return;
            name = decoded.Name;
            payload = decoded.Payload;
        };
        try { scratch.Feed(0, true, WorldMirrorFraming.Prefix(body)); }
        catch { return WorldMirrorInbound.Unknown; }
        if (string.IsNullOrEmpty(name)) return WorldMirrorInbound.Unknown;
        long? characterId = name == "CharacterSelectionRequest"
            ? WorldMirrorIds.Positive(_catalog.FieldNumber("CharacterSelectionRequest", "character_id"), payload ?? Array.Empty<ProtoField>())
            : null;
        return WorldMirrorInbound.Game(name, characterId, WorldMirrorFraming.Prefix(body));
    }
}
