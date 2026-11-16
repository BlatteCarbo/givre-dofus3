using System;
using System.Collections.Concurrent;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, uint> _nativeFollowLeader = new();
    private readonly ConcurrentDictionary<uint, (long Party, long Leader)> _nativeFollowSent = new();
    private readonly ConcurrentDictionary<uint, (long Party, long Leader)> _nativeFollowConfirmed = new();

    public bool NativeFollowConfirmed(uint pid, uint leaderPid)
        => _nativeFollowConfirmed.TryGetValue(pid, out var state)
            && state.Party == PartyIdOf(pid) && state.Party != 0 && state.Leader == CharacterId(leaderPid);

    public void ConfigureNativeFollow(uint pid, uint leaderPid, bool enabled)
    {
        if (!enabled)
        {
            _nativeFollowLeader.TryRemove(pid, out _);
            _nativeFollowConfirmed.TryRemove(pid, out _);
            if (_nativeFollowSent.TryRemove(pid, out var old))
                InjectForge(pid, MoveForge.BuildPartyStopFollowFramed(old.Leader, old.Party));
            return;
        }
        _nativeFollowLeader[pid] = leaderPid;
        TryNativeFollow(pid);
    }

    private void TryNativeFollow(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)
            || !_nativeFollowLeader.TryGetValue(pid, out uint leaderPid)) return;
        long party = PartyIdOf(pid), leader = CharacterId(leaderPid);
        if (party == 0 || leader == 0 || !IsInParty(pid, leader)) return;
        var state = (Party: party, Leader: leader);
        if (_nativeFollowSent.TryGetValue(pid, out var sent) && sent == state) return;
        _nativeFollowConfirmed.TryRemove(pid, out _);
        _nativeFollowSent[pid] = state; // Before dispatch: an offline server may answer synchronously.
        bool ok = InjectForge(pid, MoveForge.BuildPartyFollowFramed(leader, party));
        if (!ok) _nativeFollowSent.TryRemove(pid, out _);
        TeamLog($"[équipe] suivi natif pid {pid} chef={leader} groupe={party} demande={(ok ? "envoyée" : "refusée")} (confirmation attendue)");
        if (!ok) return;
        bool follow = InjectForge(pid, MoveForge.BuildAutoFollowActivationFramed());
        bool join = InjectForge(pid, MoveForge.BuildFightAutoJoinActivationFramed());
        TeamLog($"[équipe] suivi natif activation sans champs={follow}, auto-join={join}; auto-prêt non envoyé : champ fqcl non documenté");
    }

    private void ObserveNativeFollow(DecodedPacket p)
    {
        if (p.ClientToServer) return;
        if (p.Name is "PartyJoinEvent" or "PartyNewMemberEvent" or "PartyMemberUpdateEvent") TryNativeFollow(p.Pid);
        if (p.Name is "PartyLeaveEvent" or "PartyDeletedEvent" or "PartyKickedByEvent")
        {
            _nativeFollowSent.TryRemove(p.Pid, out _);
            _nativeFollowConfirmed.TryRemove(p.Pid, out _);
        }
        if (p.Name != "PartyFollowStatusUpdateEvent" || !_nativeFollowSent.TryGetValue(p.Pid, out var expected)) return;
        long Value(string field) => FieldOf(p.Name, field) is int n ? PayloadReader.Int(p.Payload, n, 0) : 0;
        if (Value("party_id") != expected.Party || Value("followed_id") != expected.Leader) return;
        bool success = Value("success") == 1 && Value("is_followed") == 1;
        if (success) _nativeFollowConfirmed[p.Pid] = expected;
        else _nativeFollowConfirmed.TryRemove(p.Pid, out _);
        TeamLog($"[équipe] suivi natif pid {p.Pid} chef={expected.Leader} confirmé={success}; repli adjacent={!success}");
    }
}
