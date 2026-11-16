using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class AccountRow
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionTransitionActive))]
    public partial SessionTransitionState SessionTransitionState { get; set; }
    [ObservableProperty] public partial string SessionTransitionMessage { get; set; } = "";
    public bool SessionTransitionActive => SessionTransition?.Active == true;
    internal SessionTransition? SessionTransition;
    internal CancellationTokenSource? SessionTransitionCancellation;
    // Completion includes the game's stream/injector finally and the outer reconnect loop.
    // Retained after StopCore: no fresh login may overtake asynchronous Socket disposal.
    internal Task? SocketRunTask;
}

public partial class AccountsViewModel
{
    internal SessionSource? CaptureSessionSource(AccountRow row)
    {
        if (!row.Running || row.Pid is not > 0) return null;
        uint pid = (uint)row.Pid.Value;
        return new(row.Model.Id, row.Model.Source, row.LaunchGen, row.Pid.Value,
            row.IsSocketSession ? ConnectionMode.Socket : ConnectionMode.Mitm,
            row.IsSocketSession ? Volatile.Read(ref row.SocketSessionSeq) : 0,
            new(_launch.ServerIdOf(pid), _launch.CharacterId(pid), _launch.SelfCharacterInfo(pid)?.Name ?? ""));
    }

    /// <summary>UI-thread cancellation. Before commit the live source is untouched.</summary>
    public void CancelSessionTransition(AccountRow row)
    {
        row.SessionTransitionCancellation?.Cancel();
    }

    internal async Task<SessionTransitionResult> TransitionSessionAsync(AccountRow row, SessionTransitionRequest request,
        Action onCommit, CancellationToken cancellationToken, bool dropHotSession = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (row.SessionTransitionActive || row.ReconnectPending || IsRelaunching(row))
            return new(SessionTransitionState.Rejected, false, "Account is already reconnecting.");
        var source = request.Source;
        var target = request.Target;
        bool sameIdentity = source.Identity.IsKnown && target.Identity.IsKnown && source.Identity.Matches(target.Identity);
        bool hot = HotMirrorPlan.UseHotPath(source.Mode, target.Mode, sameIdentity, dropHotSession);
        if (source.Mode == ConnectionMode.Socket && target.Mode == ConnectionMode.Mitm && !dropHotSession
            && source.Identity.IsKnown && target.Identity.IsKnown && !sameIdentity)
            return new(SessionTransitionState.Rejected, false, HotMirrorPlan.OtherCharacter);
        long attemptStarted = Environment.TickCount64;
        string? hotStatus = null;
        int visiblePid = 0;
        bool mitmRelogin = MitmRelogin.UseInProcess(source.Mode, target.Mode, source.Identity.ServerId, target.Identity.ServerId);
        var relogin = mitmRelogin ? new MitmReloginSession() : null;
        uint reloginPid = mitmRelogin ? (uint)source.Pid : 0;
        var operation = new SessionTransition();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        row.SessionTransition = operation;
        row.SessionTransitionCancellation = cancel;
        int ownedGeneration = source.Generation, ownedPid = source.Pid;
        bool MirrorOccupied() => row.Activity != BotActivity.InGame
            || _launch.MoveInProgress((uint)source.Pid) || _launch.CurrentExchange((uint)source.Pid) != ExchangeKind.None
            || _launch.NpcDialog((uint)source.Pid) is not null || _launch.TradePending((uint)source.Pid) is not null;
        async Task<string?> WaitOutFight(CancellationToken ct)
        {
            var waited = await WorldMirrorWait.UntilAsync(
                () => _launch.IsInFight((uint)source.Pid),
                MirrorOccupied,
                () => Environment.TickCount64,
                attemptStarted,
                static (slice, token) => Task.Delay(slice, token),
                ct).ConfigureAwait(true);
            if (waited == WorldMirrorWaitResult.BudgetExpired)
                GivreLauncher.Core.Transport.TransportLog.Line((uint)source.Pid, WorldMirrorSession.BudgetLog);
            return waited switch
            {
                WorldMirrorWaitResult.Busy => "Source became busy during preflight.",
                WorldMirrorWaitResult.BudgetExpired => HotMirrorPlan.FightBudget,
                _ => null,
            };
        }
        bool Owns() => ReferenceEquals(row.SessionTransition, operation)
            && Accounts.Contains(row) && row.Model.Id == source.AccountId && row.Model.Source == source.Authentication
            && row.Running && row.LaunchGen == ownedGeneration && row.Pid == ownedPid
            && (operation.Committed || CaptureSessionSource(row) == source);
        async Task<string?> Preflight(CancellationToken ct)
        {
            if (!source.Identity.IsKnown || !target.Identity.IsKnown || !Enum.IsDefined(target.Mode))
                return "An exact observed server and character identity is required.";
            // Capture 2026-10-31 21:44:50 / 21:47:06 : aucune trame de choix de serveur après ReloginTokenEvent.
            // ReloginRequest est vide dans les deux cas ; le client choisit l'adresse tout seul.
            // Une relance MITM ne sait pas non plus cibler un serveur, donc un autre serveur reste refusé.
            if (target.Mode == ConnectionMode.Mitm && target.Identity.ServerId != source.Identity.ServerId)
                return "Targeted MITM server switching is not supported; select Socket for an explicit server.";
            if (!LicenseService.IsAuthorized) return "License is not authorized.";
            if (MirrorOccupied() || (!hot && _launch.IsInFight((uint)source.Pid)))
                return "Source session is busy or not in game.";
            if (source.Mode == ConnectionMode.Socket && row.SocketRunTask is null)
                return "Source Socket completion is unavailable.";
            if (target.Mode == ConnectionMode.Socket && !CanSocket) return "Socket permission is required.";
            if (target.Mode == ConnectionMode.Mitm)
            {
                if (!await EnsureDofusExeAsync().ConfigureAwait(true)) return "Dofus executable selection cancelled.";
                ct.ThrowIfCancellationRequested();
                if (!Owns()) return "Source session changed.";
            }
            if (MainViewModel.Instance is { } mvm)
            {
                await mvm.EnsureMappingReadyAsync().ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                if (!Owns()) return "Source session changed.";
            }
            if (!LicenseService.IsAuthorized || MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0)
                return "License or protocol mapping is unavailable.";
            if (target.Mode == ConnectionMode.Socket && !CanSocket) return "Socket permission was revoked.";
            if (!MultiAllowed(row) || !RouteReady(row)) return "Launch route or multi-account permission refused.";
            if (MirrorOccupied() || (!hot && _launch.IsInFight((uint)source.Pid)))
                return "Source became busy during preflight.";
            if (!hot) return null;
            var fight = await WaitOutFight(ct).ConfigureAwait(true);
            if (fight is not null) return fight;
            if (!Owns()) return "Source session changed.";
            if (MirrorOccupied()) return "Source became busy during preflight.";
            return null;
        }
        async Task CloseForRelaunch()
        {
            var steps = HotMirrorPlan.SourceDelegates(hot);
            if (steps.Contains(HotMirrorPlan.PreserveUpstream)) return;
            ownedGeneration = ++row.LaunchGen;
            _relaunching[source.Pid] = row;
            onCommit(); // script snapshot and suspension happen before any transport teardown
            row.ReconnectInPlace = true;
            row.ReconnectInPlaceAt = Environment.TickCount64;
            if (source.Mode == ConnectionMode.Socket)
            {
                var completion = row.SocketRunTask!;
                var socketCancellation = row.SocketCts!;
                if (steps.Contains(HotMirrorPlan.SocketCancel)) socketCancellation.Cancel();
                if (steps.Contains(HotMirrorPlan.UnregisterSocketInjector))
                    _launch.UnregisterSocketInjector((uint)source.Pid);
                await completion.ConfigureAwait(true);
                socketCancellation.Dispose();
                if (!Owns()) return;
                row.SocketCts = null;
                row.IsSocketSession = false;
                row.SocketBackoff = false;
                row.SocketParked = false;
                Volatile.Write(ref row.SocketSession, null);
                SocketStats.Forget((uint)source.Pid);
            }
            else
            {
                using var process = Process.GetProcessById(source.Pid);
                // Keep our process handle until actual exit, rather than authenticating during Kill().
                BeginInPlaceRelaunch(row, source.Pid, null);
                await process.WaitForExitAsync().ConfigureAwait(true);
                if (!Owns()) return;
            }
            _pidStats.TryRemove((uint)source.Pid, out _);
            if (steps.Contains(HotMirrorPlan.Forget)) _launch.Forget((uint)source.Pid);
            _launch.HoldLinkDown((uint)source.Pid, "Session transition", imposed: false, byServer: false, relaunching: true);
            if (steps.Contains(HotMirrorPlan.ClearMap)) Stores.World.ClearMap((uint)source.Pid);
            row.Activity = BotActivity.Connecting;
            row.MapId = null;
            row.MapCoords = null;
            row.Status = "Session transition: connecting";
        }
        async Task CloseSource()
        {
            if (relogin is { Phase: MitmReloginPhase.Idle })
            {
                onCommit();
                relogin.Arm(Environment.TickCount64);
                _launch.BeginExpectedRelogin(reloginPid);
                row.Status = "Changement de personnage…";
                row.Activity = BotActivity.Connecting;
                return;
            }
            await CloseForRelaunch().ConfigureAwait(true);
        }
        async Task<bool> WaitForReloginAsync(CancellationToken ct)
        {
            int epoch = _launch.ReloginEpoch(reloginPid);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long now = Environment.TickCount64;
                if (relogin!.Tick(now).FallbackRelaunch) return false;
                if (_launch.IsLinkSuspended(reloginPid) && relogin.Phase == MitmReloginPhase.Armed)
                    relogin.OnLinkClosed(now);
                if (_launch.ReloginEpoch(reloginPid) != epoch
                    && relogin.Phase is MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed)
                    relogin.OnSessionReady(now);
                if (relogin.Phase == MitmReloginPhase.SessionReady
                    && !_launch.IsLinkDown(reloginPid)
                    && _launch.CurrentMap(reloginPid) is > 0)
                {
                    var identity = new SessionIdentity(_launch.ServerIdOf(reloginPid), _launch.CharacterId(reloginPid),
                        _launch.SelfCharacterInfo(reloginPid)?.Name ?? "");
                    if (target.Identity.Matches(identity))
                    {
                        relogin.Confirm(now);
                        return true;
                    }
                }
                await Task.Delay(100, ct).ConfigureAwait(true);
            }
        }
        async Task<bool> LaunchVisibleMirror()
        {
            if (row.SocketCts is not { IsCancellationRequested: false } || !_launch.HasSocketInjector((uint)source.Pid))
            {
                hotStatus = HotMirrorPlan.SocketPreserved;
                return false;
            }
            if (!LicenseService.IsAuthorized || MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0
                || !MultiAllowed(row) || !RouteReady(row) || !DofusExeKnown())
            {
                hotStatus = HotMirrorPlan.SocketPreserved;
                return false;
            }
            var fight = await WaitOutFight(cancel.Token).ConfigureAwait(true);
            if (fight is not null) { hotStatus = fight; return false; }
            if (!Owns()) return false;
            var arm = _launch.TryArmWorldMirror((uint)source.Pid, source.Identity.CharacterId, source.SocketSequence, attemptStarted);
            if (arm != WorldMirrorArmStatus.Armed)
            {
                hotStatus = HotMirrorPlan.ArmMessage(arm);
                return false;
            }
            if (!TryResolveRoute(row, out var entry, out var ps) || !TryResolveGameRoute(row, out var gameEntry, out var gamePs))
            {
                _launch.CancelWorldMirror((uint)source.Pid, WorldMirrorSession.CancelledLog);
                hotStatus = HotMirrorPlan.SocketPreserved;
                return false;
            }
            row.Status = HotMirrorPlan.OpeningWindow;
            JournalRoute(row.Model.Id, (uint)source.Pid, entry, ps, gameEntry, gamePs);
            var (apiKey, refresh) = row.Model.Source == AuthSource.Browser ? Stores.Accounts.GetBrowserTokens(row.Model.Id) : (null, null);
            var (certId, certEnc) = row.Model.Source == AuthSource.Browser ? Stores.Accounts.GetCertificate(row.Model.Id) : (null, null);
            string devSeed = Stores.Accounts.GetOrCreateDeviceSeed(row.Model.Id);
            var req = new LaunchRequest(row.Login, row.Model.Source, ps, apiKey, refresh, 1, certId, certEnc,
                DeviceSeed: devSeed, GameProxy: gamePs,
                GameProxyLabel: gamePs is null ? null : ConnectionJournal.Describe(gameEntry?.Name, gamePs, gameEntry?.FromRelay == true),
                HotMirrorSocketPid: (uint)source.Pid);
            var outcome = await _launch.LaunchAsync(req, this, s => Dispatcher.UIThread.Post(() =>
            {
                if (Owns()) row.Status = s;
            }), (cid, enc) => Stores.Accounts.SetCertificate(row.Model.Id, cid, enc)).ConfigureAwait(true);
            if (row.SocketCts is not { IsCancellationRequested: false } || !_launch.HasSocketInjector((uint)source.Pid))
            {
                if (outcome.Ok) TryKill(outcome.Pid);
                _launch.CancelWorldMirror((uint)source.Pid, WorldMirrorSession.CancelledLog);
                hotStatus = HotMirrorPlan.SocketPreserved;
                return false;
            }
            if (!outcome.Ok)
            {
                _launch.CancelWorldMirror((uint)source.Pid, WorldMirrorSession.CancelledLog);
                hotStatus = string.IsNullOrWhiteSpace(outcome.Message) ? HotMirrorPlan.SocketPreserved : outcome.Message;
                return false;
            }
            visiblePid = outcome.Pid;
            return true;
        }
        async Task<bool> LaunchTarget()
        {
            if (relogin is { Phase: MitmReloginPhase.Armed })
            {
                _launch.BindSessionCharacter(reloginPid, target.Identity);
                bool sent = _launch.InjectReloginRequest(reloginPid);
                bool arrived = false;
                if (!sent) relogin.NoteInjectFailed(Environment.TickCount64);
                else
                {
                    try { arrived = await WaitForReloginAsync(cancel.Token).ConfigureAwait(true); }
                    catch (OperationCanceledException)
                    {
                        _launch.EndExpectedRelogin(reloginPid);
                        throw;
                    }
                }
                if (arrived)
                {
                    _launch.EndExpectedRelogin(reloginPid);
                    return true;
                }
                _launch.EndExpectedRelogin(reloginPid);
                row.Status = "Le client n'a pas changé de personnage à temps. Relance…";
                await CloseForRelaunch().ConfigureAwait(true);
                if (!Owns()) return false;
            }
            if (HotMirrorPlan.SourceDelegates(hot).Contains(HotMirrorPlan.PreserveUpstream))
                return await LaunchVisibleMirror().ConfigureAwait(true);
            if (!LicenseService.IsAuthorized || MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0
                || !MultiAllowed(row) || !RouteReady(row)
                || (target.Mode == ConnectionMode.Mitm && !DofusExeKnown())
                || (target.Mode == ConnectionMode.Socket && !CanSocket)) return false;
            // Both start paths increment synchronously before their first await.
            ownedGeneration = row.LaunchGen + 1;
            bool launched;
            if (target.Mode == ConnectionMode.Socket)
                launched = StartSocketCore(row, null, target.Identity.Character, explicitServerId: target.Identity.ServerId,
                    relaunchPid: source.Pid);
            else
            {
                using var selection = _launch.ExpectSessionCharacter(target.Identity);
                int connMode = row.Model.AutoConnectType == 2 ? 2 : 1;
                launched = await LaunchCoreAsync(row, autoConnect: connMode, character: target.Identity.Character,
                    relaunchPid: source.Pid, sourceAlreadyClosed: true).ConfigureAwait(true);
            }
            if (row.LaunchGen == ownedGeneration && launched && row.Pid is int pid) ownedPid = pid;
            // A synchronous guard refusal did not take the next generation.
            else if (row.LaunchGen == ownedGeneration - 1 && row.Pid == source.Pid) ownedGeneration--;
            return launched;
        }
        try
        {
            var result = await operation.RunAsync(Preflight, Owns, CloseSource, LaunchTarget,
                ct => ConfirmSessionTargetAsync(row, target, Owns, ct),
                reason =>
                {
                    if (hot)
                    {
                        if (visiblePid > 0) TryKill(visiblePid);
                        _launch.CancelWorldMirror((uint)source.Pid, WorldMirrorSession.CancelledLog);
                        row.Status = hotStatus ?? HotMirrorPlan.SocketPreserved;
                        return;
                    }
                    StopCore(row, cancelTransition: false);
                    row.Activity = BotActivity.Disconnected;
                    row.Status = reason;
                    row.ServerKickedOffline = true;
                },
                state => row.SessionTransitionState = state, cancel.Token,
                tryCommit: () =>
                {
                    if (source.Mode != ConnectionMode.Socket) return true;
                    var session = Volatile.Read(ref row.SocketSession);
                    if (session is null || session.Seq != source.SocketSequence || session.State != 0) return false;
                    if (hot) return true;
                    return Interlocked.CompareExchange(ref session.State, 1, 0) == 0;
                }).ConfigureAwait(true);
            if (hot && result.Ok) result = result with { Message = HotMirrorPlan.WindowOpened };
            else if (dropHotSession && source.Mode == ConnectionMode.Socket && target.Mode == ConnectionMode.Mitm && result.Ok)
                result = result with { Message = HotMirrorPlan.HotSessionDropped };
            else if (hot && result.Committed && !result.Ok)
                result = result with { Message = hotStatus ?? HotMirrorPlan.SocketPreserved };
            if (ReferenceEquals(row.SessionTransition, operation)) row.SessionTransitionMessage = result.Message;
            if (result.Ok && Owns())
            {
                if (hot) row.Status = HotMirrorPlan.WindowOpened;
                else row.IsSocketMode = target.Mode == ConnectionMode.Socket;
            }
            if (dropHotSession && source.Mode == ConnectionMode.Socket && target.Mode == ConnectionMode.Mitm && result.Ok && Owns())
                row.Status = HotMirrorPlan.HotSessionDropped;
            else if (hot && !result.Ok && (result.Committed || result.Message == HotMirrorPlan.FightBudget))
                row.Status = result.Message;
            return result;
        }
        finally
        {
            if (reloginPid != 0) _launch.EndExpectedRelogin(reloginPid);
            if (ReferenceEquals(row.SessionTransition, operation))
            {
                row.SessionTransitionCancellation = null;
                row.ReconnectInPlace = false;
            }
            if (RelaunchOwnedBy(source.Pid, row)) _relaunching.Remove(source.Pid);
        }
    }

    private async Task<bool> ConfirmSessionTargetAsync(AccountRow row, SessionTarget target, Func<bool> owns, CancellationToken ct)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (!owns()) { ready.TrySetResult(false); return; }
            uint pid = (uint)row.Pid!.Value;
            if (_launch.IsLinkDown(pid) || _launch.CurrentMap(pid) is not > 0) return;
            var identity = new SessionIdentity(_launch.ServerIdOf(pid), _launch.CharacterId(pid), _launch.SelfCharacterInfo(pid)?.Name ?? "");
            if (identity.IsKnown) ready.TrySetResult(target.Identity.Matches(identity));
        }
        void Changed(uint pid) { if (row.Pid == (int)pid) Dispatcher.UIThread.Post(Check); }
        void Character(uint pid, CharacterInfo _) => Changed(pid);
        void Server(uint pid, string _) => Changed(pid);
        void Map(uint pid, long _, int? x, int? y) => Changed(pid);
        // Subscribe, then check the durable observation: neither early nor late packets are lost.
        _launch.CharacterIdentified += Character;
        _launch.ServerIdentified += Server;
        _launch.MapChanged += Map;
        try
        {
            Check();
            return await ready.Task.WaitAsync(TimeSpan.FromMinutes(2), ct).ConfigureAwait(true);
        }
        finally
        {
            _launch.CharacterIdentified -= Character;
            _launch.ServerIdentified -= Server;
            _launch.MapChanged -= Map;
        }
    }
}
