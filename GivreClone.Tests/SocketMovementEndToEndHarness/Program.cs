// Test de contrat en mémoire du vrai LaunchService.Movement.cs, sans socket,
// client Dofus, identifiant de compte ni ACK serveur réel.
using System.Collections.Concurrent;
using System.Reflection;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;

const uint Pid = 0x4000_0001;  // PID Socket SYNTHÉTIQUE, jamais un PID Windows.
const long MapId = 191_104_008; // ID de fixture de forge, pas une position live.
const int Source = 100;
const int Destination = 101;
int successes = 0;

void Check(bool condition, string what)
{
    if (!condition) throw new InvalidOperationException(what);
}

void ConfigureMapping(bool movement = true, bool confirmation = true)
{
    MoveForge.UseCatalog(
        name => name switch
        {
            "MapMovementRequest" when movement => "jrw",
            "MapMovementConfirmRequest" when confirmation => "jqi",
            "MapChangeRequest" => "jqk",
            "GameActionFightCastRequest" => "jsh",
            _ => null
        },
        (name, field) => (name, field) switch
        {
            ("MapMovementRequest", "key_cells") => 1,
            ("MapMovementRequest", "map_id") => 3,
            ("MapChangeRequest", "map_id") => 1,
            ("GameActionFightCastRequest", "spell_id") => 1,
            ("GameActionFightCastRequest", "cell") => 2,
            _ => null
        });
    EnvelopeBuilder.UseCatalog((name, field) => (name, field) switch
    {
        ("GameMessage", "request") => 1,
        ("Request", "uid") => 1,
        ("Request", "content") => 2,
        _ => null
    });
}

static T Field<T>(LaunchService svc, string name)
    => (T)(typeof(LaunchService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(svc) ?? throw new InvalidOperationException($"Missing field {name}"));

static async Task Fallback(LaunchService svc, uint pid, long seq, int dest, int count, IReadOnlyList<int> path)
{
    var m = typeof(LaunchService).GetMethod("ConfirmMoveFallbackAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Fallback implementation unavailable");
    await ((Task)(m.Invoke(svc, [pid, seq, dest, count, path])
        ?? throw new InvalidOperationException("No fallback task"))).ConfigureAwait(false);
}

static void ServerResponse(LaunchService svc, uint pid, string name)
{
    var method = typeof(LaunchService).GetMethod("OnDecoded", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("No OnDecoded handler");
    method.Invoke(svc, [new DecodedPacket(pid, ClientToServer: false, Code: "fixture",
        Name: name, Payload: Array.Empty<ProtoField>(), RawFrame: Array.Empty<byte>())]);
}

async Task Test(string name, Func<Task> action)
{
    ConfigureMapping();
    await action().ConfigureAwait(false);
    Console.WriteLine($"PASS {name}");
    successes++;
}

IReadOnlyList<int> path = MapPathfinder.ExpandPath(
    MapPathfinder.KeyMovements(Source, Destination, _ => true).Select(k => k & 0xFFF).ToArray());
Check(path.Count >= 2 && path[0] == Source && path[^1] == Destination,
    "Synthetic test cells are not reachable");

await Test("NoSocketInjectorBlocksMovement", () =>
{
    var svc = new LaunchService();
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true),
        "MoveTo succeeded without a registered synthetic socket injector");
    return Task.CompletedTask;
});

await Test("ConcurrentClicksHaveSingleDispatch", async () =>
{
    var svc = new LaunchService();
    int requests = 0;
    svc.RegisterSocketInjector(Pid, _ => { Interlocked.Increment(ref requests); return true; });
    var start = new ManualResetEventSlim();
    var first = Task.Run(() => { start.Wait(); return svc.MoveTo(Pid, MapId, Source, Destination, _ => true); });
    var second = Task.Run(() => { start.Wait(); return svc.MoveTo(Pid, MapId, Source, Destination, _ => true); });
    start.Set();
    bool[] result = [await first.ConfigureAwait(false), await second.ConfigureAwait(false)];
    Check(result.Count(ok => ok) == 1 && requests == 1,
        "concurrent clicks bypassed the per-service movement dispatch gate");
});

await Test("InvalidSourceAndMapNeverDispatch", () =>
{
    var svc = new LaunchService();
    int calls = 0;
    svc.RegisterSocketInjector(Pid, _ => { calls++; return true; });
    Check(!svc.MoveTo(Pid, 0, Source, Destination, _ => true), "zero map id was accepted");
    Check(!svc.MoveTo(Pid, MapId, -1, Destination, _ => true), "negative source was accepted");
    Check(!svc.MoveTo(Pid, MapId, Source, 560, _ => true), "out-of-range cell was accepted");
    Check(!svc.MoveTo(Pid, MapId, Source, Source, _ => true), "no-op movement was accepted");
    Field<ConcurrentDictionary<uint, long>>(svc, "_serverMapId")[Pid] = MapId + 1;
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true),
        "source stale map id ignored the current server map");
    Check(calls == 0, "an invalid movement reached the wire callback");
    return Task.CompletedTask;
});

await Test("MissingForgeContractDoesNotDispatchOrCreatePending", () =>
{
    ConfigureMapping(movement: false);
    var svc = new LaunchService();
    int calls = 0;
    svc.RegisterSocketInjector(Pid, _ => { calls++; return true; });
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true),
        "MoveTo accepted a missing MapMovementRequest code");
    Check(calls == 0, "empty frame reached socket injector");
    Check(!Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm").ContainsKey(Pid), "forge rejection left phantom pending confirmation");
    return Task.CompletedTask;
});

await Test("TransportFailureRollsBackTheGuardAndPending", () =>
{
    var svc = new LaunchService();
    int sends = 0;
    svc.RegisterSocketInjector(Pid, _ => { sends++; return false; });
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "failed dispatch was accepted");
    Check(!Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm").ContainsKey(Pid), "failed dispatch left a pending confirm");
    Check(Field<ConcurrentDictionary<uint, long>>(svc, "_moveGuardUntil").GetValueOrDefault(Pid) == 0,
        "failed dispatch left movement guard active");
    svc.RegisterSocketInjector(Pid, _ => { sends++; return true; });
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "retry after rejected dispatch blocked");
    Check(sends == 2, "unexpected frame count");
    return Task.CompletedTask;
});

await Test("SynchronousEchoSeesPrearmedSequence", async () =>
{
    var svc = new LaunchService();
    var tx = new ConcurrentQueue<byte[]>();
    Task? immediateEcho = null;
    bool wasArmedInsideInject = false;
    svc.RegisterSocketInjector(Pid, bytes =>
    {
        tx.Enqueue(bytes);
        if (tx.Count == 1)
        {
            var pending = Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(
                svc, "_botMoveConfirm");
            wasArmedInsideInject = pending.TryGetValue(Pid, out var record) &&
                                   record.Dest == Destination;
            if (wasArmedInsideInject)
                immediateEcho = Fallback(svc, Pid, record.Seq, record.Dest, record.Cells, path);
        }
        return true;
    });
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "dispatch rejected move");
    Check(wasArmedInsideInject && immediateEcho is not null, "echo arrived before pending sequence was armed");
    var expectedWire = MoveForge.BuildFramed(MapId,
        MapPathfinder.KeyMovements(Source, Destination, _ => true));
    Check(tx.TryPeek(out var first) && first.SequenceEqual(expectedWire),
        "framed request differs from MoveForge key cells/map id");
    await immediateEcho!.ConfigureAwait(false);
    Check(tx.Count == 2 && tx.ToArray()[1].SequenceEqual(MoveForge.BuildConfirmFramed()),
        "jqi was not emitted after matched echo");
    Check(Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm").ContainsKey(Pid), "dispatch of jqi was mistaken for server ACK");
    var guard = Field<ConcurrentDictionary<uint, long>>(svc, "_moveGuardUntil");
    guard[Pid] = 0; // Simulates elapsed guard only, NOT a fabricated server ACK.
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true),
        "new socket movement accepted while server outcome is still unknown");
    ServerResponse(svc, Pid, "MapMovementConfirmResponse"); // explicit SYNTHETIC server response.
    Check(!Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm").ContainsKey(Pid), "synthetic jsq did not resolve pending movement");
    Check(guard.GetValueOrDefault(Pid) == 0, "synthetic jsq did not release guard");
});

await Test("SynchronousSyntheticAckDoesNotReopenMovingActivity", () =>
{
    var svc = new LaunchService();
    svc.RegisterSocketInjector(Pid, _ =>
    {
        ServerResponse(svc, Pid, "MapMovementConfirmResponse");
        return true;
    });
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "dispatch failed");
    Check(Field<ConcurrentDictionary<uint, long>>(svc, "_moveGuardUntil").GetValueOrDefault(Pid) == 0,
        "synchronous synthetic ACK was overwritten by dispatch");
    Check(!Field<ConcurrentDictionary<uint, long>>(svc, "_movingUntil").ContainsKey(Pid),
        "movement activity reopened after synthetic ACK");
    return Task.CompletedTask;
});

await Test("WrongServerEchoCannotCauseJqi", async () =>
{
    var svc = new LaunchService();
    var tx = new ConcurrentQueue<byte[]>();
    svc.RegisterSocketInjector(Pid, packet => { tx.Enqueue(packet); return true; });
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "move not accepted");
    var pending = Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm")[Pid];
    await Fallback(svc, Pid, pending.Seq, pending.Dest, pending.Cells,
        [Source + 2, Destination]).ConfigureAwait(false);
    await Fallback(svc, Pid, pending.Seq + 1, pending.Dest, pending.Cells,
        path).ConfigureAwait(false);
    Check(tx.Count == 1, "unrelated actor/path generated movement confirm");
});

await Test("RejectedJqiIsRetriedWithoutAnotherServerEcho", async () =>
{
    var svc = new LaunchService();
    int frames = 0;
    int jqiAttempts = 0;
    svc.RegisterSocketInjector(Pid, _ =>
    {
        if (++frames == 1) return true;
        return ++jqiAttempts == 2; // 1ère tentative refusée par la file, 2e acceptée.
    });
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "movement request failed");
    var pendingStore = Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm");
    var pending = pendingStore[Pid];
    await Fallback(svc, Pid, pending.Seq, pending.Dest, pending.Cells, path).ConfigureAwait(false);
    Check(frames == 3 && jqiAttempts == 2,
        "JQI was not retried autonomously after a transport rejection");
    Check(pendingStore.ContainsKey(Pid), "successful JQI queue action falsely claimed ACK");
    await Fallback(svc, Pid, pending.Seq, pending.Dest, pending.Cells, path).ConfigureAwait(false);
    Check(frames == 3, "duplicated JQI after first accepted dispatch");
    ServerResponse(svc, Pid, "MapMovementRefusedEvent"); // explicit synthetic refusal.
    Check(!pendingStore.ContainsKey(Pid), "synthetic jqt did not clear pending move");
});

await Test("AllThreeRejectedJqiAttemptsLeaveServerOutcomeUnknown", async () =>
{
    var svc = new LaunchService();
    int frames = 0;
    svc.RegisterSocketInjector(Pid, _ => ++frames == 1); // seulement le jrw accepte.
    Check(svc.MoveTo(Pid, MapId, Source, Destination, _ => true), "movement request failed");
    var pendingStore = Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm");
    var pending = pendingStore[Pid];
    await Fallback(svc, Pid, pending.Seq, pending.Dest, pending.Cells, path).ConfigureAwait(false);
    Check(frames == 4 && pendingStore.ContainsKey(Pid),
        "three rejected JQI attempts must not create a fabricated ACK");
    var guard = Field<ConcurrentDictionary<uint, long>>(svc, "_moveGuardUntil");
    guard[Pid] = 0;
    Check(!svc.MoveTo(Pid, MapId, Source, Destination, _ => true),
        "new move slipped past an unresolved, three-times-rejected JQI");
});

await Test("ShortenedServerPathConfirmsWithoutArrivalActionsAndUnlocksNextMove", async () =>
{
    var svc = new LaunchService();
    var tx = new ConcurrentQueue<byte[]>();
    svc.RegisterSocketInjector(Pid, bytes => { tx.Enqueue(bytes); return true; });
    var waiter = svc.SendAndWaitAsync(Pid,
        () => svc.MoveTo(Pid, MapId, 128, 363, _ => true), 5000);
    var pending = Field<ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)>>(svc,
        "_botMoveConfirm")[Pid];
    var requested = Field<ConcurrentDictionary<uint, (long Seq, int[] Path)>>(svc,
        "_requestedMovePaths")[Pid].Path;
    Check(requested.Length > 2, "fixture needs a multi-step path");
    var accepted = requested.Take(2).ToArray();
    Field<ConcurrentDictionary<uint, (int Target, long SetAt)>>(svc,
        "_pendingMapChange")[Pid] = (123, Environment.TickCount64);
    await Fallback(svc, Pid, pending.Seq, pending.Dest, pending.Cells, accepted);
    Check(tx.Count == 2, "shortened server path did not get its jqi");
    Check(!waiter.IsCompleted, "queued confirmation falsely resolved movement");
    // MITM may observe the client's outgoing confirmation before the server ACK.
    typeof(LaunchService).GetMethod("OnDecoded", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(svc, [new DecodedPacket(Pid, true, "fixture", "MapMovementConfirmRequest",
            Array.Empty<ProtoField>(), Array.Empty<byte>())]);
    ServerResponse(svc, Pid, "MapMovementConfirmResponse");
    Check(await waiter == LaunchService.MoveOutcome.Superseded,
        "shortened movement falsely reported requested arrival");
    Check(tx.Count == 2, "shortened movement triggered an arrival action");
    Check(svc.MoveTo(Pid, MapId, accepted[^1], 363, _ => true),
        "movement still blocked after confirmed shortened path");
});

await Test("CastRefusalWakesBarrierImmediatelyWithoutInventingAnAck", async () =>
{
    var svc = new LaunchService();
    Field<ConcurrentDictionary<uint, bool>>(svc, "_inFight")[Pid] = true;
    Field<ConcurrentDictionary<uint, bool>>(svc, "_myTurnOpen")[Pid] = true;
    Field<ConcurrentDictionary<uint, CombatProfile>>(svc, "_combat")[Pid] = new() { Speed = CombatSpeed.Turbo };
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var begin = typeof(LaunchService).GetMethod("NoteTurboRequestTx", flags)!;
    var reject = typeof(LaunchService).GetMethod("NoteTurboCastRejection", flags)!;
    var query = typeof(LaunchService).GetMethod("TryTurboCastRejection", flags)!;
    var wait = typeof(LaunchService).GetMethod("WaitTurboActionBarrier", flags)!;
    long request = (long)begin.Invoke(svc, [Pid, "cast:12728"])!;
    var pending = (Task<bool>)wait.Invoke(svc, [Pid, request, 30000])!;
    Check(!pending.IsCompleted, "unconfirmed cast skipped sequence wait");
    reject.Invoke(svc, [Pid, 65, new[] { "2", "3" }]);
    Check(await pending.WaitAsync(TimeSpan.FromSeconds(1)), "pre-sequence server text did not release barrier");
    request = (long)begin.Invoke(svc, [Pid, "cast:12728"])!;
    pending = (Task<bool>)wait.Invoke(svc, [Pid, request, 30000])!;
    reject.Invoke(svc, [Pid, 170, new[] { "2", "3" }]);
    Check(await pending.WaitAsync(TimeSpan.FromSeconds(1)), "confirmed refusal did not wake the barrier");
    object[] queryArgs = [Pid, request, 0];
    Check((bool)query.Invoke(svc, queryArgs)! && (int)queryArgs[2] == 2,
        "refusal lost authoritative AP budget");
    Check(Field<ConcurrentDictionary<(uint Pid, long ActionId), long>>(svc, "_turboAcked").IsEmpty,
        "refusal created a fictional sequence ACK");

    long next = (long)begin.Invoke(svc, [Pid, "cast:12734"])!;
    queryArgs = [Pid, next, 0];
    Check(!(bool)query.Invoke(svc, queryArgs)!, "previous rejection leaked into next request");
    // Reply can arrive before the caller begins awaiting its barrier.
    reject.Invoke(svc, [Pid, 170, new[] { "0", "3" }]);
    Check(await ((Task<bool>)wait.Invoke(svc, [Pid, next, 30000])!).WaitAsync(TimeSpan.FromSeconds(1)),
        "refusal received before wait was lost");

    long move = (long)begin.Invoke(svc, [Pid, "move:100->101"])!;
    reject.Invoke(svc, [Pid, 170, new[] { "2", "3" }]);
    queryArgs = [Pid, move, 0];
    Check(!(bool)query.Invoke(svc, queryArgs)!, "AP text resolved a movement request");
});

await Test("SynchronousCastRefusalIsCorrelatedBeforeDispatchReturns", async () =>
{
    var svc = new LaunchService();
    Field<ConcurrentDictionary<uint, bool>>(svc, "_inFight")[Pid] = true;
    Field<ConcurrentDictionary<uint, bool>>(svc, "_myTurnOpen")[Pid] = true;
    Field<ConcurrentDictionary<uint, CombatProfile>>(svc, "_combat")[Pid] = new() { Speed = CombatSpeed.Turbo };
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var reject = typeof(LaunchService).GetMethod("NoteTurboCastRejection", flags)!;
    svc.RegisterSocketInjector(Pid, _ => { reject.Invoke(svc, [Pid, 170, new[] { "2", "3" }]); return true; });
    var send = typeof(LaunchService).GetMethods(flags).Single(m => m.Name == "SendCast" && m.GetParameters().Length == 4);
    object[] sendArgs = [Pid, 247, 12728, 0L];
    Check((bool)send.Invoke(svc, sendArgs)!, "synthetic cast was not dispatched");
    var wait = typeof(LaunchService).GetMethod("WaitTurboActionBarrier", flags)!;
    Check(await ((Task<bool>)wait.Invoke(svc, [Pid, (long)sendArgs[3], 30000])!).WaitAsync(TimeSpan.FromSeconds(1)),
        "synchronous cast refusal was not observed");
    svc.RegisterSocketInjector(Pid, _ => false);
    sendArgs = [Pid, 247, 12728, 0L];
    Check(!(bool)send.Invoke(svc, sendArgs)! && (long)sendArgs[3] == 0,
        "failed dispatch left a cast waiting for a server response");
});

Console.WriteLine($"Socket movement contract harness: {successes} PASS / 0 FAIL — offline, synthetic PID, zero network or game client");
