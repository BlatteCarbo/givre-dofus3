extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreClone.App.Services.Combat;
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

public sealed class SummonDispatchIntegrationTests
{
    private const uint Pid = 0x40000112;
    private const long MapId = 88085763;
    private const int SpellId = 42;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Dispatch_reopens_only_for_the_active_actors_decoded_summon_death(bool companion, bool classic)
    {
        // Same real-service/socket rejection convention as CombatAndStatDispatchRegressionTests.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (string name in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            Field(name).SetValue(service, new object());
        foreach (string name in new[] { "_engineControlled", "_teamIntents" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        long active = companion ? -20000 : 123;
        long other = companion ? 123 : -20000;
        Map(service, "_charId")[Pid] = 123L;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[]
            { (Cell(0), active), (Cell(6), other), (Cell(3), -100) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_jzcSeen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile
            { AutoFight = true, AutoSpells = !classic, Speed = Speed.Turbo,
                Spells = classic ? new() { new() { SpellId = SpellId, IsSummon = true, MaxSummons = 2 } } : new() };
        Map(service, "_charSpells")[Pid] = new[] { SpellId };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [SpellId] = 1 };
        // Different base/player resources make an accidental controlled-actor fallback observable.
        Map(service, "_selfChars")[Pid] = new Dictionary<int, int> { [CharIds.MaxSummonedCreatures] = 0 };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(active, new(active, 9, 0, 100, 100, 0, 0)
                { Characteristics = ImmutableDictionary<int, int>.Empty.Add(CharIds.MaxSummonedCreatures, 2) }),
            new KeyValuePair<long, FighterStats>(other, new(other, 0, 0, 100, 100, 0, 0)
                { Characteristics = ImmutableDictionary<int, int>.Empty.Add(CharIds.MaxSummonedCreatures, 0) }),
            new KeyValuePair<long, FighterStats>(-100, new(-100, 0, 0, 1000, 1000, 0, 0))
        });
        var visibility = new FightVisibilityReducer();
        visibility.Observe(new(-100, 2), positionConfirmed: true);
        Map(service, "_fighterVisibility")[Pid] = visibility;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Hidden)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, 10)
                    .Select(i => new MapCell(Cell(i), true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
                { [SpellId] = new() { [1] = (3, 1, 6, 8, false) } });
        // SYNTHETIC summon + area damage descriptor: immediate damage makes search admission
        // observable. This is not a real spell or evidence of pure-summon tactical value.
        var descriptor = new Combat.SpellCastDescriptor(SpellId, 1, 3, 1, 6, 8, false, 3, 0, 0, 0, "",
            ImmutableArray.Create(
                new Combat.SpellEffectDescriptor(181, Combat.SpellEffectKind.Summon, 42, 42, 0, 0, "x", 'P', 0, 0),
                new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage, 10, 10, 0, 0, "A", 'C', 6, 0)));
        typeof(Names).GetField("_spellDescriptors", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>> { [SpellId] = new() { [1] = descriptor } });
        Field("_gameNames").SetValue(service, names);
        typeof(Launch).GetMethod("ObserveControlledFighter", Hidden)!.Invoke(service,
            [Pid, new ControlledFighter(123, -20000, new Dictionary<int, int> { [SpellId] = 1 })]);
        object? controlled = companion ? Map(service, "_controlledCombat")[Pid] : null;
        var frames = new List<byte[]>();
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame => { frames.Add(frame); return false; });
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        static byte[] V(ulong value)
        {
            var bytes = new List<byte>();
            do { byte b = (byte)(value & 127); value >>= 7; bytes.Add(value == 0 ? b : (byte)(b | 128)); }
            while (value != 0);
            return bytes.ToArray();
        }
        static byte[] I(int field, long value) => V((ulong)(field << 3)).Concat(V(unchecked((ulong)value))).ToArray();
        static byte[] M(int field, params byte[][] parts)
        {
            var body = parts.SelectMany(p => p).ToArray();
            return V((ulong)((field << 3) | 2)).Concat(V((ulong)body.Length)).Concat(body).ToArray();
        }
        // Catalog-based synthetic wire fixture, not an observed network capture.
        // Appearance must reach ownership/cap gating through the decoded ingress.
        foreach (var (id, cell, owner) in new[]
            { (-1L, Cell(4), active), (-2L, Cell(5), active), (-3L, Cell(7), other), (-4L, Cell(8), other), (-5L, Cell(9), other) })
        {
            // A different action source makes loss of the explicit summoner observable.
            var wire = I(F("GameActionFightEvent", "source_id"), owner == active ? other : active).Concat(
                M(F("GameActionFightEvent", "summons"),
                    M(F("Summons", "summons_by_actor"),
                        M(F("SummonsByActor", "summons"),
                            I(F("ActorPositionInformation", "actor_id"), id),
                            M(F("ActorPositionInformation", "disposition"), I(F("EntityDisposition", "cell_id"), cell)),
                            M(F("ActorPositionInformation", "actor_information"),
                                M(F("ActorInformation", "fighter"),
                                    M(F("FightFighterInformation", "spawn_information"), I(F("SpawnInformation", "alive"), 1)),
                                    M(F("FightFighterInformation", "stats"),
                                        I(F("FightCharacteristics", "invisibility_state"), id == -5 ? 0 : 2),
                                        I(F("FightCharacteristics", "summoned"), 1),
                                        I(F("FightCharacteristics", "summoner"), owner)))))))).ToArray();
            uint previousScope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
            try
            {
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                    [new Packet(Pid, false, "jwe", "GameActionFightEvent", RawProtobuf.Parse(wire), wire)]);
            }
            finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = previousScope; }
            var summonVisibility = (FightVisibilityReducer)Map(service, "_fighterVisibility")[Pid]!;
            Assert.Equal(id != -5, summonVisibility.Get(id).CanTarget);
        }
        void Death(long target)
        {
            var death = new ProtoField
            {
                Field = F("GameActionFightEvent", "death"), Wire = WireType.Len,
                Message = new List<ProtoField> { new()
                    { Field = F("Death", "target_id"), Wire = WireType.Varint, Varint = unchecked((ulong)target) } }
            };
            uint previousScope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
            try
            {
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                    [new Packet(Pid, false, "jwe", "GameActionFightEvent", new[] { death }, Array.Empty<byte>())]);
            }
            finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = previousScope; }
        }
        async Task<(string Code, IReadOnlyList<ProtoField> Payload)[]> Turn()
        {
            frames.Clear();
            Map(service, "_myTurnOpen")[Pid] = true;
            var run = (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(service,
                [Pid, 0, false, CancellationToken.None, controlled, 1])!;
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            return frames.Select(frame =>
            {
                var payload = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
                Assert.Empty(rest);
                var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(payload)));
                return (hit.Code, (IReadOnlyList<ProtoField>)(hit.Payload ?? new List<ProtoField>()));
            }).ToArray();
        }

        Assert.Equal(catalog.CodeForName("FightTurnFinishRequest"), Assert.Single(await Turn()).Code);
        Death(-3); // Another owner's death must not free the active actor's capacity.
        Assert.Equal(catalog.CodeForName("FightTurnFinishRequest"), Assert.Single(await Turn()).Code);
        Death(-1); // Real decoded server handler removes an owned living summon.
        var replacement = await Turn();
        Assert.Equal(new[] { catalog.CodeForName("GameActionFightCastRequest"), catalog.CodeForName("FightTurnFinishRequest") },
            replacement.Select(r => r.Code));
        var cast = replacement[0].Payload;
        Assert.Equal((ulong)SpellId, cast.Single(f => f.Field == F("GameActionFightCastRequest", "spell_id")).Varint);
        int targetCell = (int)cast.Single(f => f.Field == F("GameActionFightCastRequest", "cell")).Varint;
        Assert.Contains(targetCell, new[] { Cell(1), Cell(2), Cell(4) });
    }
}
