using System.Collections.Concurrent;
using GivreClone.Engine;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, RegenerationOptions> _regenerationOptions = new();
    private readonly ConcurrentDictionary<uint, bool> _operatorRecovery = new();
    public void SetOperatorRecovering(uint pid, bool recovering) => _operatorRecovery[pid] = recovering;
    public void SetRegenerationOptions(uint pid, BotSettings settings) =>
        _regenerationOptions[pid] = new(settings.RegenerationEnabled, settings.RegenerationStartPercent,
            settings.RegenerationStopPercent, settings.RegenerationFoodGid, settings.RegenerationWaitForTeam);

    public RegenerationOptions RegenerationOptionsFor(uint pid) =>
        _regenerationOptions.TryGetValue(pid, out var options) ? options : new();

    public int? AuthoritativeLifePercent(uint pid) =>
        !_lifeStaleSinceFight.ContainsKey(pid) && _observedSelfLife.TryGetValue(pid, out var s) && s.Max > 0
            ? (int)(100L * s.Life / s.Max) : null;

    public bool RegenerationTeamReady(uint pid) => RegenerationTeamWaitReason(pid) is null;

    public string? RegenerationTeamWaitReason(uint pid)
    {
        var options = RegenerationOptionsFor(pid);
        if (!options.Enabled || !options.WaitForTeam) return null;
        // TeamMembers is the online roster. Offline/missing members do not hold the team;
        // online unknown HP, another fight, death or another map do.
        foreach (var member in TeamMembers(pid))
        {
            if (member.Pid == pid) continue;
            bool sameMap = (CurrentMap(member.Pid) ?? 0) != 0 && CurrentMap(member.Pid) == CurrentMap(pid);
            bool inFight = IsInFight(member.Pid), alive = LifeStatusOf(member.Pid) == 0;
            int? life = AuthoritativeLifePercent(member.Pid);
            bool recovering = _operatorRecovery.TryGetValue(member.Pid, out var recovery) && recovery;
            if (!RegenerationPolicy.MemberReady(options, online: true,
                sameMap: sameMap, inFight: inFight, alive: alive,
                authoritativePercent: life, recovering: recovering))
            {
                string reason = !sameMap ? "autre carte ou carte inconnue"
                    : inFight ? "en combat" : !alive ? "mort"
                    : life is null or < 0 or > 100 ? "PV serveur inconnus"
                    : $"PV serveur {life}% < seuil {(recovering ? options.Stop : options.Start)}%";
                return $"{(string.IsNullOrWhiteSpace(member.Name) ? $"pid {member.Pid}" : member.Name)} : {reason}";
            }
        }
        return null;
    }
}
