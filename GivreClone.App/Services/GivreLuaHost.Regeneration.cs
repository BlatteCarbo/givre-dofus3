using System.Threading;
using GivreClone.Engine;

namespace GivreClone.App.Services;

public sealed partial class GivreLuaHost
{
    private bool _operatorRecovering;

    private bool PrepareOperatorRegeneration(CancellationToken ct)
    {
        var options = _launch.RegenerationOptionsFor(_pid);
        if (!options.Enabled) { _operatorRecovering = false; _launch.SetOperatorRecovering(_pid, false); return true; }
        int? currentPct = _launch.AuthoritativeLifePercent(_pid) is null ? null : _launch.SelfLifePercent(_pid);
        var decision = RegenerationPolicy.Decide(options, currentPct, _operatorRecovering);
        if (decision == RegenerationDecision.WaitForLife)
        {
            Warn("vie", Loc.T("L.Bots.Settings.RegenUnknown"));
            return false;
        }
        if (decision == RegenerationDecision.Regenerate)
        {
            _operatorRecovering = true;
            _launch.SetOperatorRecovering(_pid, true);
            if (!Regenerate(options.Stop, -1, ct, options.FoodGid, authoritative: false)) return false;
        }
        _operatorRecovering = false;
        _launch.SetOperatorRecovering(_pid, false);
        if (_launch.RegenerationTeamWaitReason(_pid) is not { } waiting) return true;
        Info("vie", Loc.T("L.Bots.Settings.RegenWaiting") + " — " + waiting);
        return WaitUntil(() => _launch.RegenerationTeamReady(_pid) || _launch.IsInFight(_pid),
            TeamWaitTimeoutMs, ct) && !_launch.IsInFight(_pid);
    }
}
