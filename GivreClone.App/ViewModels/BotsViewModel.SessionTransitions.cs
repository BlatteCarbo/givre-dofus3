using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    /// <summary>
    /// Capture before displaying confirmation/selection UI. Pass this immutable source back unchanged;
    /// a reconnect, stop, replacement or Socket sequence change makes the request stale.
    /// UI/MCP callers must dispatch these methods to the UI thread.
    /// </summary>
    public SessionSource? CaptureSessionSource(string accountId)
    {
        Dispatcher.UIThread.VerifyAccess();
        var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == accountId);
        return row is null ? null : _accounts.CaptureSessionSource(row);
    }

    /// <summary>
    /// Socket to Mitm on the same character keeps the Socket TCP and replays the world into a new
    /// visible window. dropHotSession is the explicit fallback: it closes that Socket and says the
    /// hot session was dropped. No target is selected implicitly.
    /// </summary>
    public async Task<SessionTransitionResult> TransitionSessionAsync(SessionTransitionRequest request,
        CancellationToken cancellationToken = default, bool dropHotSession = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == request.Source.AccountId);
        if (row is null) return new(SessionTransitionState.Rejected, false, "Account no longer exists.");
        if (_reconnectPreflight.Contains(row.Model.Id) || row.ReconnectPending)
            return new(SessionTransitionState.Rejected, false, "Account is already reconnecting.");
        bool hot = HotMirrorPlan.UseHotPath(request.Source.Mode, request.Target.Mode,
            request.Source.Identity.Matches(request.Target.Identity), dropHotSession);
        ScriptReconnect? pending = null;
        try
        {
            var result = await _accounts.TransitionSessionAsync(row, request, () =>
            {
                if (hot) return;
                var script = CaptureReconnectScript(row);
                bool resume = request.ResumeScript && request.Source.Identity.Matches(request.Target.Identity);
                SuspendForRelaunch(request.Source.Pid);
                _socketWaiting.Remove(request.Source.Pid);
                _socketResume.Remove(request.Source.Pid);
                _kickedScripts.Remove(row.Model.Id);
                ClearPlanDisconnect(row.Model.Id);
                DropScriptReconnect(row.Model.Id);
                pending = new ScriptReconnect
                {
                    AccountId = row.Model.Id, Character = request.Target.Identity.Character,
                    ExpectedServerId = request.Target.Identity.ServerId, ExpectedCharacterId = request.Target.Identity.CharacterId,
                    ScriptName = resume ? script.Name : null, ScriptBody = resume ? script.Body : null,
                    Launched = true, LaunchedAt = Environment.TickCount64, Gen = row.LaunchGen,
                    Attended = true, InPlace = true, OldPid = request.Source.Pid,
                };
                _scriptReconnects.Add(pending);
                SyncReconnectFlags();
            }, cancellationToken, dropHotSession).ConfigureAwait(true);
            if (pending is not null)
            {
                if (result.Ok)
                {
                    pending.Gen = row.LaunchGen;
                    pending.LaunchedAt = Environment.TickCount64;
                    if (row.Pid == request.Source.Pid) pending.Rekeyed = true;
                }
                else _scriptReconnects.Remove(pending);
            }
            return result;
        }
        finally { SyncReconnectFlags(); }
    }

    public void CancelSessionTransition(string accountId)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_accounts.Accounts.FirstOrDefault(a => a.Model.Id == accountId) is { } row)
            _accounts.CancelSessionTransition(row);
    }
}
