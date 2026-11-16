extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreClone.App.Services.Combat;
using GivreLauncher.Core.Transport;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Policy = F06App::GivreClone.App.Services.CombatMovementPolicy;
using Speed = F06App::GivreClone.App.Services.CombatSpeed;
using Reader = F06App::GivreClone.App.Services.MapCellReader;

namespace GivreClone.Tests;

[Collection("Observed grade transport logging")]
public sealed class CombatAndStatDispatchRegressionTests
{
    private const uint Pid = 0x40000111;
    private const long MapId = 88085763;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Private)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;

    private static Launch Service(out List<byte[]> frames)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Private))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        Field("_autoLevelLock").SetValue(service, new object());
        Field("_combatMapGate").SetValue(service, new object());
        Field("_movementDispatchGate").SetValue(service, new object());
        Field("_autoLevelActive").SetValue(service, new HashSet<uint>());
        Field("_statUpgradeActive").SetValue(service, new HashSet<uint>());
        Field("_autoStatRequests").SetValue(service, new Dictionary<uint, object>());
        Field("_gameNames").SetValue(service, new F06App::GivreClone.App.Services.GameNameResolver());
        Map(service, "_charId")[Pid] = 123L;
        var captured = new List<byte[]>();
        // Reject after observing dispatch, so these tests never enter server-confirmation waits.
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame =>
        {
            captured.Add(frame);
            return false;
        });
        frames = captured;
        return service;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Lua_portal_exit_costs_the_walk_to_entry_without_overwriting_confirmed_MP(bool observed, bool delayedExit)
    {
        var service = Service(out _);
        const int source = 353, exit = 300;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Private)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, MapPathfinder.CellCount)
                    .Select(c => new MapCell(c, true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (source, 123), (200, -1) };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(123, new(123, 7, 4, 100, 100, 7, 4))
        });
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile { Speed = delayedExit ? Speed.Turbo : Speed.Instant };
        Map(service, "_isqCount")[Pid] = 1;
        var turnType = typeof(Launch).GetNestedType("ScriptTurnState", BindingFlags.NonPublic)!;
        var turn = Activator.CreateInstance(turnType, nonPublic: true)!;
        turnType.GetField("ApLeft")!.SetValue(turn, 7);
        turnType.GetField("MpLeft")!.SetValue(turn, 4);
        Map(service, "_scriptTurn")[Pid] = turn;
        var geometry = ((Func<int, bool> Walk, Func<int, int, bool> Step, int SelfCell, long ForgeMap))
            typeof(Launch).GetMethod("BuildFightGeom", Private)!.Invoke(service, [Pid])!;
        int entry = MapPathfinder.OrthoReachable(source, 1, geometry.Walk, geometry.Step).Keys.First(c => c != source);
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(_ =>
        {
            Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (delayedExit ? entry : exit, 123), (200, -1) };
            if (observed)
                typeof(Launch).GetMethod("ApplyPointUpdate", Private)!.Invoke(service, [Pid, 123L, false, 3]);
            return true;
        });

        var movement = service.ScriptCombatMoveAsync(Pid, entry, CancellationToken.None);
        if (delayedExit)
        {
            Assert.False(movement.IsCompleted);
            Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (exit, 123), (200, -1) };
            Map(service, "_myTurnOpen")[Pid] = false;
            var signal = (TaskCompletionSource<bool>)Map(service, "_turboBarrierSignal")[Pid]!;
            signal.TrySetResult(true);
        }
        var result = await movement.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Moved);
        Assert.Equal(exit, result.Cell);
        Assert.Equal((7, 3), service.ScriptPointsLeft(Pid));
    }

    [Theory]
    [InlineData(true, -1, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, -1, 1)]
    public async Task Lua_movement_obeys_nearest_enemy_policy_at_dispatch(bool neverApproach, int direction, int dispatches)
    {
        var service = Service(out var frames);
        const int source = 353, enemy = 200;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Private)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, MapPathfinder.CellCount)
                    .Select(c => new MapCell(c, true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (source, 123), (enemy, -1) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile
        {
            MovementPolicy = neverApproach ? Policy.NeverApproach : Policy.Standard
        };
        // Closed previous sequence and no pending packets: the barrier returns synchronously.
        Map(service, "_isqCount")[Pid] = 1;
        var geometry = ((Func<int, bool> Walk, Func<int, int, bool> Step, int SelfCell, long ForgeMap))
            typeof(Launch).GetMethod("BuildFightGeom", Private)!.Invoke(service, [Pid])!;
        int before = MapPathfinder.CellDistance(source, enemy);
        int target = MapPathfinder.OrthoReachable(source, 3, geometry.Walk, geometry.Step).Keys.First(c =>
            c != source && Math.Sign(MapPathfinder.CellDistance(c, enemy) - before) == direction);

        var result = await service.ScriptCombatMoveAsync(Pid, target, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Moved);
        Assert.Equal(source, result.Cell);
        Assert.Equal(dispatches, frames.Count);
        Assert.All(frames, frame => Assert.NotEmpty(frame));
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    public async Task Automatic_stat_dispatch_rechecks_legacy_enablement_but_explicit_spending_is_independent(
        bool automatic, bool disableDuringPlanning, int dispatches)
    {
        var service = Service(out var frames);
        var profile = new Profile { AutoLevelStats = automatic };
        Map(service, "_combat")[Pid] = profile;
        int planningReads = 0;
        var bases = new ObservedBases(() =>
        {
            planningReads++;
            if (disableDuringPlanning) profile.AutoLevelStats = false;
        });
        var sheetType = typeof(Launch).GetNestedType("StatSheet", BindingFlags.NonPublic)!;
        var sheet = Activator.CreateInstance(sheetType, [123L, new object(), bases, 10])!;
        Map(service, "_statSheets")[Pid] = sheet;
        Task<int?> run = automatic
            ? (Task<int?>)typeof(Launch).GetMethod("UpgradeCharacteristicCoreAsync", Private)!
                .Invoke(service, [Pid, "strength", 10, CancellationToken.None, false, true])!
            : service.UpgradeCharacteristicAsync(Pid, "strength", 10, CancellationToken.None);

        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(planningReads > 0);
        Assert.Equal(dispatches, frames.Count);
        Assert.All(frames, frame => Assert.NotEmpty(frame));
    }

    [Theory]
    [InlineData(Speed.Instant)]
    [InlineData(Speed.Turbo)]
    public async Task Script_move_records_its_original_self_round_even_when_the_next_actor_header_arrives_during_dispatch(Speed speed)
    {
        var service = Service(out _);
        const int source = 353;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Private)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, MapPathfinder.CellCount)
                    .Select(c => new MapCell(c, true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (source, 123), (200, -1) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile { Speed = speed };
        Map(service, "_fightTurnNo")[Pid] = 3;
        Map(service, "_teamIntentRound")[Pid] = 7;
        Map(service, "_isqCount")[Pid] = 1;
        var geometry = ((Func<int, bool> Walk, Func<int, int, bool> Step, int SelfCell, long ForgeMap))
            typeof(Launch).GetMethod("BuildFightGeom", Private)!.Invoke(service, [Pid])!;
        int target = MapPathfinder.OrthoReachable(source, 1, geometry.Walk, geometry.Step).Keys.First(c => c != source);
        var recorder = new CombatJournalRecorder(Pid);
        recorder.Feed(DateTime.UnixEpoch, "[combat] début de tour : MOI (round 7)");
        void Observe(uint who, string line) { if (who == Pid) recorder.Feed(DateTime.UnixEpoch, line); }
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(_ =>
        {
            // Simulate a synchronous server reply, including the next actor's header.
            recorder.Feed(DateTime.UnixEpoch, "[combat] début de tour : COMPAGNON -2");
            Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (target, 123), (200, -1) };
            return true;
        });
        bool wasEnabled = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += Observe;
        try
        {
            var result = await service.ScriptCombatMoveAsync(Pid, target, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Moved);
            Assert.Equal(target, result.Cell);
            var fight = recorder.Flush("test finished")!;
            Assert.Equal(1, fight.Moves);
            Assert.Equal(0, fight.CompanionMoves);
            Assert.Equal(7, fight.TurnStats.Single().Turn);
            Assert.Equal(1, fight.TurnStats.Single().Moves);
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Enabled = wasEnabled;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispatched_cast_keeps_its_owner_and_round_when_injection_advances_the_header(bool companion)
    {
        var service = Service(out _);
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_fightTurnNo")[Pid] = 3;
        Map(service, "_teamIntentRound")[Pid] = 7;
        Field("_engineControlled").SetValue(service,
            Activator.CreateInstance(Field("_engineControlled").FieldType));
        var recorder = new CombatJournalRecorder(Pid);
        recorder.Feed(DateTime.UnixEpoch, "[combat] début de tour : MOI (round 7)");
        if (companion) recorder.Feed(DateTime.UnixEpoch, "[combat] début de tour : COMPAGNON -2");
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(_ =>
        {
            recorder.Feed(DateTime.UnixEpoch.AddSeconds(1), companion
                ? "[combat] début de tour : MOI (round 8)"
                : "[combat] début de tour : COMPAGNON -2");
            Map(service, "_teamIntentRound")[Pid] = 8;
            return true;
        });
        void Observe(uint who, string line)
        {
            if (who == Pid) recorder.Feed(DateTime.UnixEpoch.AddSeconds(2), line);
        }
        bool wasEnabled = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += Observe;
        try
        {
            var send = typeof(Launch).GetMethod("SendCast", Private, null,
                [typeof(uint), typeof(int), typeof(int), typeof(long).MakeByRefType(), typeof(bool), typeof(int)], null)!;
            Assert.True((bool)send.Invoke(service, [Pid, 200, 42, 0L, companion, 0])!);
            var fight = recorder.Flush("test finished")!;
            Assert.Equal(companion ? 0 : 1, fight.Casts);
            Assert.Equal(companion ? 1 : 0, fight.CompanionCasts);
            Assert.Equal(companion ? 0 : 1, fight.TurnStats.Single(t => t.Turn == 7).Casts);
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Enabled = wasEnabled;
        }
    }

    [Theory]
    [InlineData(false, 0, FighterVisibility.Invisible)]
    [InlineData(true, 0, FighterVisibility.Invisible)]
    [InlineData(false, 1, FighterVisibility.Detected)]
    [InlineData(true, 1, FighterVisibility.Detected)]
    [InlineData(false, 2, FighterVisibility.Visible)]
    [InlineData(true, 2, FighterVisibility.Visible)]
    [InlineData(false, 987, FighterVisibility.Unknown)]
    [InlineData(true, 987, FighterVisibility.Unknown)]
    [InlineData(false, -1, FighterVisibility.Unknown)]
    [InlineData(true, -1, FighterVisibility.Unknown)]
    public async Task Automatic_turn_snapshot_preserves_active_actor_visibility(
        bool companion, int rawState, FighterVisibility expected)
    {
        var service = Service(out var frames);
        long active = companion ? -2 : 123;
        Field("_engineControlled").SetValue(service, Activator.CreateInstance(Field("_engineControlled").FieldType));
        Field("_teamIntents").SetValue(service, Activator.CreateInstance(Field("_teamIntents").FieldType));
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Private)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, new[] { new MapCell(353, true, true, 0, LinkedZone: 17) })
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (353, 123), (354, -2), (200, -1) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_jzcSeen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile { AutoFight = true, AutoSpells = true, Speed = Speed.Turbo };
        Map(service, "_charSpells")[Pid] = new[] { 42 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [42] = 1 };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(active, new(active, 0, 0, 100, 100, 0, 0))
        });
        var visibility = new FightVisibilityReducer();
        // Missing observation and unknown raw values must not invent visibility either.
        if (rawState >= 0) visibility.Observe(new(active, rawState));
        visibility.Observe(new(-1, 2), positionConfirmed: true);
        Map(service, "_fighterVisibility")[Pid] = visibility;
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        var namesType = names.GetType();
        namesType.GetField("_combatDone", Private)!.SetValue(names, true);
        namesType.GetField("_spellCombat", Private)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>> { [42] = new() { [1] = (3, 1, 6, 0, false) } });
        var descriptor = new F06App::GivreClone.App.Services.Combat.SpellCastDescriptor(42, 1, 3, 1, 6, 0, false,
            1, 1, 0, 0, "", System.Collections.Immutable.ImmutableArray.Create(
                new F06App::GivreClone.App.Services.Combat.SpellEffectDescriptor(100,
                    F06App::GivreClone.App.Services.Combat.SpellEffectKind.Damage, 10, 10, 0, 0, "A", 'P', 0, 0)));
        namesType.GetField("_spellDescriptors", Private)!.SetValue(names,
            new Dictionary<int, Dictionary<int, F06App::GivreClone.App.Services.Combat.SpellCastDescriptor>>
                { [42] = new() { [1] = descriptor } });
        Field("_gameNames").SetValue(service, names);
        object? controlled = null;
        if (companion)
        {
            typeof(Launch).GetMethod("ObserveControlledFighter", Private)!.Invoke(service,
                [Pid, new ControlledFighter(123, active, new Dictionary<int, int> { [42] = 1 })]);
            controlled = Map(service, "_controlledCombat")[Pid];
        }
        var chunks = new SortedDictionary<int, string>();
        var lines = new List<string>();
        string tag = $"[{(companion ? "combat-companion-empty" : "combat-player-empty")}] pid={Pid} turn=1 offset=";
        void Observe(uint who, string line)
        {
            if (who != Pid) return;
            lines.Add(line);
            if (!line.StartsWith(tag, StringComparison.Ordinal)) return;
            int space = line.IndexOf(' ', tag.Length);
            chunks.Add(int.Parse(line[tag.Length..space]), line[(space + 1)..]);
        }
        bool wasEnabled = TransportLog.Enabled;
        uint previousScope = TransportLog.Scope.Value;
        TransportLog.Enabled = true;
        TransportLog.Scope.Value = Pid;
        TransportLog.EmittedWithPid += Observe;
        try
        {
            // Zero observed resources force the real producer's existing empty-plan diagnostic;
            // no cast, movement, server confirmation, or scheduler delay is needed.
            var run = (Task)typeof(Launch).GetMethod("RunFightTurn", Private)!.Invoke(service,
                [Pid, 0, false, CancellationToken.None, controlled, 1])!;
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(chunks.Count > 0, string.Join(Environment.NewLine, lines));
            using var json = System.Text.Json.JsonDocument.Parse(string.Concat(chunks.Values));
            Assert.Equal(active, json.RootElement.GetProperty("SelfId").GetInt64());
            var fighter = Assert.Single(json.RootElement.GetProperty("Fighters").EnumerateArray(),
                f => f.GetProperty("Id").GetInt64() == active);
            Assert.Equal((int)expected, fighter.GetProperty("Visibility").GetInt32());
            Assert.Equal((int)FighterPositionCertainty.Confirmed, fighter.GetProperty("PositionCertainty").GetInt32());
            Assert.Single(frames); // Only the normal finish-turn request; no cast or movement.
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Scope.Value = previousScope;
            TransportLog.Enabled = wasEnabled;
        }
    }

    // Server snapshot interface lets the test revoke enablement between entry and final dispatch,
    // without scheduler races, sleeps, or production-only test hooks.
    private sealed class ObservedBases(Action onRead) : IReadOnlyDictionary<int, int>
    {
        private readonly Dictionary<int, int> _values = CharacteristicUpgradePolicy.Stats.ToDictionary(s => s.Id, _ => 0);
        public bool TryGetValue(int key, out int value) { onRead(); return _values.TryGetValue(key, out value); }
        public int this[int key] => _values[key];
        public IEnumerable<int> Keys => _values.Keys;
        public IEnumerable<int> Values => _values.Values;
        public int Count => _values.Count;
        public bool ContainsKey(int key) => _values.ContainsKey(key);
        public IEnumerator<KeyValuePair<int, int>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
