extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
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
public sealed class CombatUtilityDispatchIntegrationTests
{
    private const uint Pid = 0x40000113;
    private const long MapId = 88085763;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    [Theory]
    [InlineData(3737, true, 1, 0)]
    [InlineData(3738, true, 0, 2)]
    [InlineData(0, true, 0, 0)]
    [InlineData(3737, false, 0, 0)]
    public void Confirmed_transcendance_commits_only_the_variant_observed_before_cast(
        int state, bool confirmed, int extraCasts, int extraRange)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        Field("_engineControlled").SetValue(service, Activator.CreateInstance(Field("_engineControlled").FieldType));
        Map(service, "_charId")[Pid] = 123L;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (0, 123L) };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [14580] = 2 };
        var names = new Names(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        Assert.NotNull(names.GetSpellDescriptor(14580, 2));
        Field("_gameNames").SetValue(service, names);
        Map(service, "_activeFighterStates")[(Pid, 123L)] = new HashSet<int>(state == 0 ? [] : new[] { state });

        typeof(Launch).GetMethod("NoteCastForModifiers", Hidden)!.Invoke(service, [Pid, 14580]);
        // The server can remove the selecting state before its modifier confirmation arrives.
        Map(service, "_activeFighterStates")[(Pid, 123L)] = new HashSet<int>();
        if (confirmed) Map(service, "_spellModEvents")[(Pid, 123L)] = 1;
        typeof(Launch).GetMethod("CommitCastModifiers", Hidden)!.Invoke(service, [Pid, 1]);

        var mods = (GivreClone.Engine.SpellModifierSet)typeof(Launch)
            .GetMethod("SpellModsOf", Hidden)!.Invoke(service, [Pid, 123L])!;
        Assert.Equal(extraCasts, mods.ExtraCastsPerTurn(14574));
        var adjusted = mods.Adjust(14604, new GivreClone.Engine.SpellCastStats(1, 0, 4, false, false, true, false));
        Assert.Equal(4 + extraRange, adjusted.Range);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Absolute_life_refresh_repairs_drift_only_when_all_components_arrive_together(bool complete)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (string name in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            Field(name).SetValue(service, new object());
        Map(service, "_charId")[Pid] = 123L;
        Map(service, "_inFight")[Pid] = true;
        var stats = new ConcurrentDictionary<long, FighterStats>();
        stats[-1] = new(-1, 6, 3, 0, 3060, 6, 3)
        {
            Characteristics = ImmutableDictionary<int, int>.Empty
                .Add(FightStatsReader.IdLife, 3400)
                .Add(FightStatsReader.IdVitality, -340)
                .Add(FightStatsReader.IdCurLifeDelta, -3060)
        };
        Map(service, "_fighterStats")[Pid] = stats;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        FightStatsReader.UseCatalog(catalog.FieldNumber);
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        static ProtoField I(int field, long value) => new()
            { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        static ProtoField M(int field, params ProtoField[] children) => new()
            { Field = field, Wire = WireType.Len, Message = children.ToList() };
        ProtoField Characteristic(int id, int value) => M(F("FightCharacteristics", "characteristics"),
            I(F("CharacterCharacteristic", "characteristic_id"), id),
            M(F("CharacterCharacteristic", "value"), I(F("CharacterCharacteristicValue", "total"), value)));
        var updates = new List<ProtoField> { Characteristic(FightStatsReader.IdCurLifeDelta, -2500) };
        if (complete)
        {
            updates.Add(Characteristic(FightStatsReader.IdVitality, -400));
            updates.Add(Characteristic(FightStatsReader.IdLife, 3400));
        }
        var payload = new[] { I(F("FightRefreshCharacterStatsEvent", "fighter_id"), -1),
            M(F("FightRefreshCharacterStatsEvent", "stats"), updates.ToArray()) };

        uint scope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
        try
        {
            typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                [new Packet(Pid, false, catalog.CodeForName("FightRefreshCharacterStatsEvent")!,
                    "FightRefreshCharacterStatsEvent", payload, Array.Empty<byte>())]);
        }
        finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = scope; }

        Assert.Equal(complete ? 500 : 0, stats[-1].Life);
        Assert.Equal(complete ? 3000 : 3060, stats[-1].MaxLife);
        Assert.Equal(-2500, stats[-1].Characteristics[FightStatsReader.IdCurLifeDelta]);
        Assert.Equal(6, stats[-1].Pa);
        Assert.Equal(3, stats[-1].Pm);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Learned_mp_utility_dispatches_a_walk_from_zero_mp_only_after_acknowledgment(bool subspell, bool accepted)
    {
        // Same real LaunchService and socket-injector fixture convention as SummonDispatchIntegrationTests.
        // All spell data and wire inputs here are synthetic, not live-client captures.
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
        Map(service, "_combat")[Pid] = new Profile { AutoFight = true, AutoSpells = true, Speed = Speed.Turbo };
        Map(service, "_charSpells")[Pid] = new[] { 70, 71 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [70] = 1, [71] = 1 };
        var stats = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(123, new(123, 4, 0, 100, 100, 4, 0)),
            new KeyValuePair<long, FighterStats>(-100, new(-100, 0, 0, 100, 100, 0, 0))
        });
        Map(service, "_fighterStats")[Pid] = stats;
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
        var child = new Combat.SpellCastDescriptor(72, 2, 0, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(128, Combat.SpellEffectKind.Buff,
                2, 2, -1, 0, "C", 'P', 0, 0)));
        var boost = child with { SpellId = 70, Grade = 1, Ap = 1 };
        if (subspell) boost = boost with { Effects = ImmutableArray.Create(
            new Combat.SpellEffectDescriptor(1160, Combat.SpellEffectKind.Unknown, 72, 72, -1, 0, "a", 'P', 0, 0,
                DiceNum: 72, DiceSide: 2)) };
        var attack = new Combat.SpellCastDescriptor(71, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                10, 10, 0, 0, "A", 'P', 0, 0)));
        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
                { [70] = new() { [1] = (1, 0, 0, 0, false) }, [71] = new() { [1] = (3, 1, 1, 0, false) } });
        typeof(Names).GetField("_spellDescriptors", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>>
                { [70] = new() { [1] = boost }, [71] = new() { [1] = attack }, [72] = new() { [2] = child } });
        Field("_gameNames").SetValue(service, names);
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        FightStatsReader.UseCatalog(catalog.FieldNumber);
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        static ProtoField I(int field, long value) => new()
            { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        static ProtoField M(int field, params ProtoField[] children) => new()
            { Field = field, Wire = WireType.Len, Message = children.ToList() };
        void Receive(string name, params ProtoField[] payload)
        {
            uint scope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
            try
            {
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                    [new Packet(Pid, false, catalog.CodeForName(name)!, name, payload, Array.Empty<byte>())]);
            }
            finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = scope; }
        }
        void Points(params (int Characteristic, int Value)[] updates) => Receive("FightRefreshCharacterStatsEvent",
            I(F("FightRefreshCharacterStatsEvent", "fighter_id"), 123),
            M(F("FightRefreshCharacterStatsEvent", "stats"),
                updates.Select(update => M(F("FightCharacteristics", "characteristics"),
                    I(F("CharacterCharacteristic", "characteristic_id"), update.Characteristic),
                    M(F("CharacterCharacteristic", "value"), I(F("CharacterCharacteristicValue", "total"), update.Value)))).ToArray()));

        // Subscribe before starting the actual automatic-turn loop. Its cast wait is released only by decoded inputs.
        var firstCast = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<(string Code, IReadOnlyList<ProtoField> Payload)>();
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame =>
        {
            var body = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
            Assert.Empty(rest);
            var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(body)));
            frames.Enqueue((hit.Code, hit.Payload ?? new List<ProtoField>()));
            if (hit.Code != catalog.CodeForName("GameActionFightCastRequest")) return false;
            firstCast.TrySetResult(true);
            return true;
        });
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_jyjCount")[Pid] = 1;
        var turn = (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(service,
            [Pid, 0, false, CancellationToken.None, null, 1])!;
        await firstCast.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, stats[123].Pm);
        Assert.Equal((ulong)70, Assert.Single(frames).Payload.Single(f => f.Field == F("GameActionFightCastRequest", "spell_id")).Varint);
        Assert.Equal((ulong)Cell(0), Assert.Single(frames).Payload.Single(f => f.Field == F("GameActionFightCastRequest", "cell")).Varint);
        if (accepted)
        {
            Receive("SequenceStartEvent");
            Points((FightStatsReader.IdPa, 3), (FightStatsReader.IdPm, 2));
            Assert.Equal(2, stats[123].Pm); // Real stat handler, not a direct fixture resource mutation.
            Receive("SequenceEndEvent", I(F("SequenceEndEvent", "author_id"), 123),
                I(F("SequenceEndEvent", "action_id"), 1), I(F("SequenceEndEvent", "sequence_type"), 0));
        }
        else
        {
            Receive("TextInformationEvent", I(F("TextInformationEvent", "message_id"), 170));
        }
        await turn.WaitAsync(TimeSpan.FromSeconds(5));
        var requests = frames.ToArray();
        Assert.Equal(new[] { 70UL }, requests.Where(r => r.Code == catalog.CodeForName("GameActionFightCastRequest"))
            .Select(r => r.Payload.Single(f => f.Field == F("GameActionFightCastRequest", "spell_id")).Varint));
        var moves = requests.Where(r => r.Code == catalog.CodeForName("MapMovementRequest")).ToArray();
        if (accepted)
        {
            var move = Assert.Single(moves);
            Assert.Equal(new[] { Cell(0), Cell(1), Cell(2) }, MapPathfinder.ExpandPath(
                MapMovementReader.Cells(move.Payload, F("MapMovementRequest", "key_cells"))));
        }
        else
        {
            Assert.Empty(moves);
            Assert.Equal(0, stats[123].Pm);
        }
        Assert.Equal(catalog.CodeForName("FightTurnFinishRequest"), requests[^1].Code);
    }
}
