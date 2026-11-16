using System;
using GivreClone.App.Services.Production;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>
    /// Read-only provenance witness for V13 stock planning. Joins the current
    /// hosted Socket/MITM link, decoder epoch, actual map/actor publication and
    /// monotonic local inventory revision. A complete witness is NOT a server
    /// ACK or proof that a preceding withdrawal/harvest/craft succeeded.
    /// </summary>
    public GuidedSupplySourceObservation? ReadGuidedSupplySource(uint pid)
    {
        if (pid == 0 || IsLinkSuspended(pid)) return null;
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        long epoch = CurrentLocalProductionDecoderEpoch(pid);
        long revision = Inventory.ObservationRevision(pid);
        if (server <= 0 || character <= 0 || epoch <= 0 || revision <= 0)
            return null;
        CaptureLinkIdentity? link = IsSocketPid(pid)
            ? _socketSourceAuthority.CurrentLink(pid)
            : _localMitmSourceAuthority.CurrentLink(pid);
        if (link is not { Pid: var owner, RunEpoch: > 0, TunnelId: > 0 } source ||
            owner != pid || source.SourceInstanceId == Guid.Empty)
            return null;

        var world = Stores.World.Get(pid);
        long map;
        long mapGeneration;
        int? usedPods = null;
        int? maxPods = null;
        lock (world.ActorPublicationGate)
        {
            map = CurrentMap(pid) ?? 0;
            if (map <= 0 || world.MapId != map || world.MapGeneration <= 0 ||
                world.SelfActorId != character || !world.SelfReadyFor(map) ||
                !world.CellsReadyFor(map)) return null;
            mapGeneration = world.MapGeneration;
            if (world.PodsAuthoritative && world.Pods.Max > 0 &&
                world.Pods.Cur >= 0 && world.Pods.Cur <= world.Pods.Max)
            {
                usedPods = world.Pods.Cur;
                maxPods = world.Pods.Max;
            }
        }
        if (Inventory.ObservationRevision(pid) != revision ||
            CurrentMap(pid) != map ||
            CurrentLocalProductionDecoderEpoch(pid) != epoch ||
            IsLinkSuspended(pid) || ServerIdOf(pid) != server ||
            CharacterId(pid) != character ||
            (IsSocketPid(pid) ? _socketSourceAuthority.CurrentLink(pid) :
                _localMitmSourceAuthority.CurrentLink(pid)) != link)
            return null;

        // Token is host-issued transport provenance + local decoder lifetime,
        // never a PID-derived identifier or individual server confirmation.
        string token = source.SourceInstanceId.ToString("N") + ":" +
            source.RunEpoch + ":" + source.TunnelId + ":" +
            source.ConnectionToken.ToString("N") + ":decoder:" + epoch;
        return new GuidedSupplySourceObservation(server, character,
            map, mapGeneration, token, revision, usedPods, maxPods);
    }
}
