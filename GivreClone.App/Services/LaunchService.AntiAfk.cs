using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>Pourquoi un keepalive d'orientation n'est pas envoyé.</summary>
public enum AntiAfkBlock { None, Disabled, NotInGame, InFight, Moving, Exchange, Dialog, NotDue }

/// <summary>Instant et contraintes lus par <see cref="AntiAfkPolicy.Plan"/>. L'horloge est fournie par l'appelant.</summary>
public readonly record struct AntiAfkContext(
    bool Enabled,
    int IdleMinutes,
    long NowMs,
    long LastActionMs,
    bool InGame,
    bool InFight,
    bool Moving,
    bool InDialog,
    bool InExchange,
    int? CurrentDirection);

/// <summary>Réglages et état de jeu passés à <see cref="AntiAfkSession.Evaluate"/>.</summary>
public readonly record struct AntiAfkGates(
    bool Enabled,
    int IdleMinutes,
    bool InGame,
    bool InFight,
    bool Moving,
    bool InDialog,
    bool InExchange);

public readonly record struct AntiAfkPlan(bool Send, int Direction, AntiAfkBlock Block);

/// <summary>Horloge remplaçable. La boucle de production lit <see cref="Environment.TickCount64"/> ; les tests avancent
/// <see cref="NowMs"/>.</summary>
public sealed class AntiAfkClock
{
    public long NowMs { get; set; }
    public void AdvanceMs(long ms) => NowMs += ms;
}

/// <summary>Décision pure du keepalive. Aucune trame n'est forgée ici.</summary>
public static class AntiAfkPolicy
{
    public const int DefaultIdleMinutes = 10;
    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 30;
    /// <summary>Jitter ajouté au délai, tiré une fois par fenêtre d'inactivité (0 inclus, 90 s inclus).</summary>
    public const int JitterMaxMs = 90_000;

    // Ces messages partent tout seuls (client officiel ou boucle socket) et ne remettent PAS à zéro l'inactivité serveur.
    // Journal 2026-10-31 : PingRequest à 10:00:54 puis SystemMessageDisplayEvent message_id=1 hang_up à 10:00:58 ;
    // même séquence à 13:41:54 / 13:41:58. Aucun message Inactivity/AFK/Idle dans fields.json.
    // PopupWarningCloseRequest n'a qu'un champ obfusqué (fqpt) : pas une réponse mappée, on ne l'envoie pas.
    // SystemMessageDisplayEvent id=1 est la coupure elle-même (TCP fermé dans la foulée), pas un avertissement à fermer.
    // Fenêtre 22:40-22:49 : aucune de ces trames. PlayerStatusUpdatedEvent statut 2 à 22:44:45 tombe pendant une récolte,
    // ce n'est pas le popup (seuls les statuts 1 et 4 sont confirmés).
    private static readonly HashSet<string> NotAnAction = new(StringComparer.Ordinal)
    {
        "PingRequest",
        "SequenceNumberRequest",
        "BasicLatencyStatsRequest",
        "ClientChallengeInitRequest",
        "ClientChallengeProofRequest",
        "ClientIdRequest",
        "FightPingsRequest",
        "GameActionAcknowledgementRequest",
    };

    public static int ClampMinutes(int minutes)
    {
        if (minutes < MinIdleMinutes) return MinIdleMinutes;
        if (minutes > MaxIdleMinutes) return MaxIdleMinutes;
        return minutes;
    }

    public static long IdleBudgetMs(int idleMinutes, int jitterMs)
        => ClampMinutes(idleMinutes) * 60_000L + Math.Clamp(jitterMs, 0, JitterMaxMs);

    /// <summary>Vrai si cette trame client est une action de jeu. Un nom vide ne compte pas : on n'a pas la preuve d'une action.</summary>
    public static bool CountsAsAction(string? messageName)
        => !string.IsNullOrEmpty(messageName) && !NotAnAction.Contains(messageName);

    /// <summary>Prochaine orientation 0-7. Inconnue : 1. Connue : +2 (quart de tour), pour que le serveur voie un vrai changement.</summary>
    public static int NextDirection(int? current)
    {
        if (current is not int d || d is < 0 or > 7) return 1;
        return (d + 2) & 7;
    }

    public static AntiAfkPlan Plan(in AntiAfkContext context, int jitterMs)
    {
        int direction = NextDirection(context.CurrentDirection);
        if (!context.Enabled) return new AntiAfkPlan(false, direction, AntiAfkBlock.Disabled);
        if (!context.InGame) return new AntiAfkPlan(false, direction, AntiAfkBlock.NotInGame);
        if (context.InFight) return new AntiAfkPlan(false, direction, AntiAfkBlock.InFight);
        if (context.Moving) return new AntiAfkPlan(false, direction, AntiAfkBlock.Moving);
        if (context.InExchange) return new AntiAfkPlan(false, direction, AntiAfkBlock.Exchange);
        if (context.InDialog) return new AntiAfkPlan(false, direction, AntiAfkBlock.Dialog);
        if (context.LastActionMs < 0) return new AntiAfkPlan(false, direction, AntiAfkBlock.NotDue);
        long due = context.LastActionMs + IdleBudgetMs(context.IdleMinutes, jitterMs);
        if (context.NowMs < due) return new AntiAfkPlan(false, direction, AntiAfkBlock.NotDue);
        return new AntiAfkPlan(true, direction, AntiAfkBlock.None);
    }
}

/// <summary>Fenêtre d'inactivité d'un personnage. Le jitter est figé jusqu'à la prochaine action ou keepalive.</summary>
public sealed class AntiAfkSession
{
    private readonly object _gate = new();
    private readonly Func<int> _jitter;
    private long _lastActionMs = -1;
    private int _jitterMs;
    private int? _direction;

    public AntiAfkSession(Func<int> jitter) => _jitter = jitter;

    public bool HasStarted { get { lock (_gate) return _lastActionMs >= 0; } }

    public void NoteAction(long nowMs)
    {
        lock (_gate)
        {
            _lastActionMs = nowMs;
            _jitterMs = Math.Clamp(_jitter(), 0, AntiAfkPolicy.JitterMaxMs);
        }
    }

    /// <summary>Démarre l'horloge à l'entrée en jeu si aucune action n'a encore été vue. Ne la recule pas.</summary>
    public void NoteArrival(long nowMs)
    {
        lock (_gate)
        {
            if (_lastActionMs >= 0) return;
            _lastActionMs = nowMs;
            _jitterMs = Math.Clamp(_jitter(), 0, AntiAfkPolicy.JitterMaxMs);
        }
    }

    public void NoteDirection(int direction)
    {
        if (direction is < 0 or > 7) return;
        lock (_gate) _direction = direction;
    }

    public AntiAfkPlan Evaluate(long nowMs, AntiAfkGates gates)
    {
        long last;
        int jitter;
        int? direction;
        lock (_gate) { last = _lastActionMs; jitter = _jitterMs; direction = _direction; }
        return AntiAfkPolicy.Plan(new AntiAfkContext(
            gates.Enabled, gates.IdleMinutes, nowMs, last, gates.InGame, gates.InFight, gates.Moving,
            gates.InDialog, gates.InExchange, direction), jitter);
    }

    public void CommitKeepalive(long nowMs, int direction)
    {
        lock (_gate)
        {
            if (direction is >= 0 and <= 7) _direction = direction;
            _lastActionMs = nowMs;
            _jitterMs = Math.Clamp(_jitter(), 0, AntiAfkPolicy.JitterMaxMs);
        }
    }
}

// Keepalive d'inactivité. Socket envoie toujours une orientation réelle (MapChangeOrientationRequest).
// MITM : si GivreItemIdOverlay a crocheté UnityEngine.Input.get_anyKey (inactivity.status = hooked),
// le popup client est couvert et l'orientation n'est pas envoyée. Sinon le keepalive réseau reste.
public sealed partial class LaunchService
{
    private int _antiAfkStarted;
    private readonly ConcurrentDictionary<uint, AntiAfkSession> _antiAfk = new();
    private readonly ConcurrentDictionary<uint, byte> _antiAfkDialog = new();
    private readonly ConcurrentDictionary<uint, long> _antiAfkFailAt = new();
    private readonly ConcurrentDictionary<uint, byte> _antiAfkNative = new();

    private void StartAntiAfk()
    {
        if (Interlocked.Exchange(ref _antiAfkStarted, 1) != 0) return;
        PacketDecoded += ObserveAntiAfk;
        MapActorsDecoded += ObserveAntiAfkActors;
        _ = Task.Run(AntiAfkLoopAsync);
    }

    private void ForgetAntiAfk(uint pid)
    {
        _antiAfk.TryRemove(pid, out _);
        _antiAfkDialog.TryRemove(pid, out _);
        _antiAfkFailAt.TryRemove(pid, out _);
        _antiAfkNative.TryRemove(pid, out _);
    }

    private AntiAfkSession AntiAfkOf(uint pid)
        => _antiAfk.GetOrAdd(pid, static _ => new AntiAfkSession(static () => Random.Shared.Next(0, AntiAfkPolicy.JitterMaxMs + 1)));

    private void ObserveAntiAfk(DecodedPacket packet)
    {
        try { ObserveAntiAfkCore(packet); }
        catch { /* lecture best-effort : ne pas casser les autres abonnés */ }
    }

    private void ObserveAntiAfkCore(DecodedPacket packet)
    {
        var session = AntiAfkOf(packet.Pid);
        if (packet.ClientToServer)
        {
            if (packet.Name == "MapChangeOrientationRequest")
                session.NoteDirection((int)PayloadReader.Int(packet.Payload, FNum("MapChangeOrientationRequest", "direction"), -1));
            if (AntiAfkPolicy.CountsAsAction(packet.Name)) session.NoteAction(Environment.TickCount64);
            return;
        }
        switch (packet.Name)
        {
            case "MapCurrentEvent":
            case "MapComplementaryInformationEvent":
                session.NoteArrival(Environment.TickCount64);
                _antiAfkDialog.TryRemove(packet.Pid, out _);
                break;
            case "MapChangeOrientationEvent":
                NoteOwnDirection(packet, session);
                break;
            case "NpcDialogCreationEvent":
            case "NpcDialogQuestionEvent":
                _antiAfkDialog[packet.Pid] = 1;
                break;
            case "DialogLeaveEvent":
                _antiAfkDialog.TryRemove(packet.Pid, out _);
                break;
        }
    }

    private void NoteOwnDirection(DecodedPacket packet, AntiAfkSession session)
    {
        long me = CharacterId(packet.Pid);
        if (me == 0) return;
        long actor = PayloadReader.Int(packet.Payload, FNum("MapChangeOrientationEvent", "actor_id"), 0);
        if (actor != me) return;
        session.NoteDirection((int)PayloadReader.Int(packet.Payload, FNum("MapChangeOrientationEvent", "direction"), -1));
    }

    private void ObserveAntiAfkActors(uint pid, IReadOnlyList<MapActor> actors)
    {
        try { ObserveAntiAfkActorsCore(pid, actors); }
        catch { /* lecture best-effort */ }
    }

    private void ObserveAntiAfkActorsCore(uint pid, IReadOnlyList<MapActor> actors)
    {
        long me = CharacterId(pid);
        if (me == 0) return;
        foreach (var actor in actors)
        {
            if (actor.ActorId != me) continue;
            AntiAfkOf(pid).NoteDirection(actor.Direction);
            return;
        }
    }

    private async Task AntiAfkLoopAsync()
    {
        while (true)
        {
            try { await Task.Delay(1000).ConfigureAwait(false); }
            catch (Exception) { return; }
            foreach (uint pid in _antiAfk.Keys)
            {
                try { TrySendAntiAfk(pid); }
                catch (Exception e) { LogAntiAfkFail(pid, Environment.TickCount64, e.GetType().Name); }
            }
        }
    }

    private void TrySendAntiAfk(uint pid)
    {
        if (!_antiAfk.TryGetValue(pid, out var session)) return;
        if (IsReviewedSocketMovementOnly(pid)) return;
        long now = Environment.TickCount64;
        if (!session.HasStarted && CurrentMap(pid) is not null) session.NoteArrival(now);
        var (enabled, minutes) = ReadAntiAfkSettings(pid);
        // Live 2026-11-01 : hook get_anyKey actif, le client a quand même fermé sa connexion à 05:19 (31 min sans action). Hook coupé, orientation toujours envoyée.
        if (!IsSocketPid(pid)) NativeTurboRuntime.PublishInactivityHook((int)pid, false);
        bool moving = (_movingUntil.TryGetValue(pid, out var until) && now < until)
            || (_moveGuardUntil.TryGetValue(pid, out var guard) && now < guard)
            || _botMoveConfirm.ContainsKey(pid);
        bool exchange = TradeOpen(pid) || StorageOpen(pid) || HdvAnyOpen(pid) || ChestSessionActive(pid)
            || CraftOpen(pid) || LotMOf(pid).ShopOpen;
        bool dialog = _antiAfkDialog.ContainsKey(pid) || _lastNpcDialog.ContainsKey(pid) || TeleportView(pid) is not null;
        bool inGame = CurrentMap(pid) is not null && !IsLinkSuspended(pid) && LifeStatusOf(pid) == 0;
        var plan = session.Evaluate(now, new AntiAfkGates(enabled, minutes, inGame, IsInFight(pid), moving, dialog, exchange));
        if (!plan.Send) return;

        _antiAfkNative.TryRemove(pid, out _);

        byte[] packet = MoveForge.BuildMapChangeOrientationFramed(plan.Direction);
        if (packet.Length == 0)
        {
            LogAntiAfkFail(pid, now, "MapChangeOrientationRequest non forgé (" + (MoveForge.LastForgeError ?? "mapping") + ")");
            return;
        }
        var relay = RelayForInject();
        if (relay is null)
        {
            LogAntiAfkFail(pid, now, "relais absent");
            return;
        }
        if (!relay.Inject(pid, packet))
        {
            LogAntiAfkFail(pid, now, "envoi refusé");
            return;
        }
        ShowInjected(pid, packet);
        session.CommitKeepalive(Environment.TickCount64, plan.Direction);
        _antiAfkFailAt.TryRemove(pid, out _);
        TransportLog.Line(pid, $"[anti-afk] pid {pid} : MapChangeOrientationRequest direction {plan.Direction}");
    }

    private (bool Enabled, int Minutes) ReadAntiAfkSettings(uint pid)
    {
        string? name = SelfCharacterInfo(pid)?.Name;
        if (string.IsNullOrWhiteSpace(name)) name = LicenseService.BotCharacterName((int)pid);
        if (string.IsNullOrWhiteSpace(name)) return (true, AntiAfkPolicy.DefaultIdleMinutes);
        var settings = BotSettingsStore.Get(name);
        return (settings.AntiAfkEnabled, settings.AntiAfkIdleMinutes);
    }

    private void LogAntiAfkFail(uint pid, long now, string reason)
    {
        if (_antiAfkFailAt.TryGetValue(pid, out var at) && now - at < 60_000) return;
        _antiAfkFailAt[pid] = now;
        TransportLog.Line(pid, $"[anti-afk] pid {pid} : keepalive non envoyé ({reason})");
    }
}
