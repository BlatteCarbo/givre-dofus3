using System.Threading;

namespace GivreClone.App.Services;

public sealed partial class GivreLuaHost
{
    // Every travel entry point uses the same barrier, including graph paths and teleports.
    // A map replacement while waiting invalidates the caller's path and source snapshot.
    private bool WaitForTeamDeparture(CancellationToken ct, int timeoutMs = TeamWaitTimeoutMs)
    {
        ThrowIfStopping(ct);
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map == 0 || _launch.IsInFight(_pid)) return false;
        if (_launch.TeamMapChangeWaitPending(_pid)
            && !WaitUntil(() => !_launch.TeamMapChangeWaitPending(_pid)
                || (_launch.CurrentMap(_pid) ?? 0) != map || _launch.IsInFight(_pid), timeoutMs, ct)) return false;
        return (_launch.CurrentMap(_pid) ?? 0) == map && !_launch.IsInFight(_pid)
            && !_launch.TeamMapChangeWaitPending(_pid);
    }
}
