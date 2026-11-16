extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Speed = F06App::GivreClone.App.Services.CombatSpeed;
using Reader = F06App::GivreClone.App.Services.MapCellReader;
using Names = F06App::GivreClone.App.Services.GameNameResolver;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Combat = F06App::GivreClone.App.Services.Combat;

namespace GivreClone.Tests;

[Collection("Observed grade transport logging")]
public sealed class CombatMovementContinuationDispatchTests
{
    private const uint Pid = 0x40000114;
    private const long MapId = 88085763;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_walk_replans_an_exhausted_tail_but_preserves_a_pending_cast(bool reachableCast)
    {
        // Synthetic wire/map fixture using the real turn loop, planner, movement forge, and decoded ingress.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (string name in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            Field(name).SetValue(service, new object());
        foreach (string name in new[] { "_engineControlled", "_teamIntents" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        Map(service, "_charId")[Pid] = 123L;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (Cell(0), 123), (Cell(3), -100) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_jzcSeen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile { AutoFight = true, AutoSpells = true, Speed = Speed.Instant };
        Map(service, "_charSpells")[Pid] = new[] { 71 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [71] = 1 };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(123, new(123, reachableCast ? 3 : 0, 2, 100, 100, 3, 2)),
            new KeyValuePair<long, FighterStats>(-100, new(-100, 0, 0, 100, 100, 0, 0))
        });
        var visibility = new FightVisibilityReducer();
        visibility.Observe(new(-100, 2), positionConfirmed: true);
        Map(service, "_fighterVisibility")[Pid] = visibility;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Hidden)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, 4)
                    .Select(i => new MapCell(Cell(i), true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        var attack = new Combat.SpellCastDescriptor(71, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                10, 10, 0, 0, "A", 'P', 0, 0)));
        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
                { [71] = new() { [1] = (3, 1, 1, 0, false) } });
        typeof(Names).GetField("_spellDescriptors", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>> { [71] = new() { [1] = attack } });
        Field("_gameNames").SetValue(service, names);
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        static ProtoField I(int field, long value) => new()
            { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        void Receive(string name, params ProtoField[] payload)
        {
            uint scope = TransportLog.Scope.Value;
            try
            {
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                    [new Packet(Pid, false, catalog.CodeForName(name)!, name, payload, Array.Empty<byte>())]);
            }
            finally { TransportLog.Scope.Value = scope; }
        }

        var frames = new ConcurrentQueue<(string Code, IReadOnlyList<ProtoField> Payload)>();
        var plans = new ConcurrentQueue<string>();
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(uint who, string line)
        {
            if (who == Pid && line.StartsWith("[combat-planner] turn=1 [", StringComparison.Ordinal)
                && line.Contains(" work=", StringComparison.Ordinal)) plans.Enqueue(line);
        }
        // Subscribe before execution. The injector delivers an exact server echo synchronously,
        // so no sleep, position prediction, polling, or test-only planning seam is needed.
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame =>
        {
            var body = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
            Assert.Empty(rest);
            var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(body)));
            var payload = hit.Payload ?? new List<ProtoField>();
            frames.Enqueue((hit.Code, payload));
            if (hit.Code != catalog.CodeForName("MapMovementRequest")) return false;
            var packedPath = payload.Single(f => f.Field == F("MapMovementRequest", "key_cells"));
            Receive("SequenceStartEvent");
            Receive("MapMovementEvent", I(F("MapMovementEvent", "character_id"), 123),
                new ProtoField { Field = F("MapMovementEvent", "cells"), Wire = WireType.Len, Bytes = packedPath.Bytes });
            Assert.Contains(((IEnumerable<(int Cell, long ActorId)>)Map(service, "_fighters")[Pid]!),
                f => f.ActorId == 123 && f.Cell == Cell(2));
            Receive("SequenceEndEvent", I(F("SequenceEndEvent", "author_id"), 123),
                I(F("SequenceEndEvent", "action_id"), 1), I(F("SequenceEndEvent", "sequence_type"), 0));
            confirmed.TrySetResult(true);
            return true;
        });
        bool wasEnabled = TransportLog.Enabled;
        uint previousScope = TransportLog.Scope.Value;
        TransportLog.Enabled = true;
        TransportLog.Scope.Value = Pid;
        TransportLog.EmittedWithPid += Observe;
        try
        {
            Map(service, "_myTurnOpen")[Pid] = true;
            Map(service, "_jyjCount")[Pid] = 1;
            var turn = (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(service,
                [Pid, 0, false, CancellationToken.None, null, 1])!;
            await turn.WaitAsync(TimeSpan.FromSeconds(5));
            await confirmed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requests = frames.ToArray();
            var move = Assert.Single(requests, r => r.Code == catalog.CodeForName("MapMovementRequest"));
            Assert.Equal(new[] { Cell(0), Cell(1), Cell(2) }, MapPathfinder.ExpandPath(
                MapMovementReader.Cells(move.Payload, F("MapMovementRequest", "key_cells"))));
            var casts = requests.Where(r => r.Code == catalog.CodeForName("GameActionFightCastRequest")).ToArray();
            if (reachableCast)
                Assert.Equal(71UL, Assert.Single(casts).Payload.Single(f => f.Field == F("GameActionFightCastRequest", "spell_id")).Varint);
            else
            {
                Assert.Empty(casts);
                Assert.Equal(2, plans.Count); // A fresh, legitimately empty endpoint search, not a reused empty tail.
            }
            Assert.Equal(catalog.CodeForName("FightTurnFinishRequest"), requests[^1].Code);
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Scope.Value = previousScope;
            TransportLog.Enabled = wasEnabled;
        }
    }
}
