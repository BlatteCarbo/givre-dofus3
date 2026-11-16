extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
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

namespace GivreClone.Tests;

[CollectionDefinition("Observed grade transport logging", DisableParallelization = true)]
public sealed class ObservedGradeTransportLoggingCollection;

[Collection("Observed grade transport logging")]
public sealed class ObservedMonsterGradeDispatchTests
{
    private const uint Pid = 0x40000112;
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
        Field("_bestiary").SetValue(service,
            new Lazy<F06App::GivreClone.App.Services.Combat.MonsterThreatCatalog>(
                () => F06App::GivreClone.App.Services.Combat.MonsterThreatCatalog.Empty));
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

    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static ProtoField V(string message, string field, long value) => new()
    {
        Field = Catalog.FieldNumber(message, field)!.Value, Wire = WireType.Varint, Varint = unchecked((ulong)value)
    };
    private static ProtoField M(string message, string field, params ProtoField[] children) => new()
    {
        Field = Catalog.FieldNumber(message, field)!.Value, Wire = WireType.Len, Message = children.ToList()
    };
    private static ProtoField[] Position(long actor, int? grade, long owner)
    {
        var monster = new List<ProtoField> { V("MonsterFighter", "monster_gid", 1234) };
        if (grade.HasValue) monster.Add(V("MonsterFighter", "creature_grade", grade.Value));
        return
        [
            V("ActorPositionInformation", "actor_id", actor),
            M("ActorPositionInformation", "actor_information", M("ActorInformation", "fighter",
                M("FightFighterInformation", "stats", V("FightCharacteristics", "summoner", owner)),
                M("FightFighterInformation", "ai_fighter",
                    M("AIFighterInformation", "monster_fighter_information", monster.ToArray()))))
        ];
    }
    private static void Publish(Launch service, string name, params ProtoField[] tree) =>
        typeof(Launch).GetMethod("ObserveTreasureFighters", Private)!.Invoke(service, [Pid, name, tree]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_turn_snapshot_preserves_actor_keyed_decoded_grades(bool companion)
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
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (353, 123), (354, -2), (200, -1), (201, -3), (202, -4), (203, -5), (204, -6) };
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
        Field("_catalog").SetValue(service, Catalog);
        // Shared templates and owners are deliberately not grade identities.
        Publish(service, "FightFighterShowEvent", M("FightFighterShowEvent", "information", Position(-3, 2, 123)));
        Publish(service, "FightFighterRefreshEvent", M("FightFighterRefreshEvent", "information", Position(-4, 5, -2)));
        Publish(service, "FightFighterShowEvent", M("FightFighterShowEvent", "information", Position(-5, null, 123)));
        Publish(service, "GameActionFightEvent",
            V("GameActionFightEvent", "source_id", -2),
            M("GameActionFightEvent", "summons", M("Summons", "summons_by_context_information",
                M("SummonsByContextInformation", "summons",
                    M("SummonContextInformation", "summons",
                        M("SpawnInformation", "position", V("ActorPositionInformation", "actor_id", -6)))))));
        // Explicit owner observations retain precedence over the monster record's owner.
        Map(service, "_summonOwners")[Pid] = new ConcurrentDictionary<long, long>(new[]
        {
            new KeyValuePair<long, long>(-3, -2), new KeyValuePair<long, long>(-4, 123)
        });
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
            var fighters = json.RootElement.GetProperty("Fighters").EnumerateArray()
                .ToDictionary(f => f.GetProperty("Id").GetInt64());
            foreach (var (actor, grade, template, owner) in new (long Actor, int Grade, int Template, long? Owner)[]
            {
                (-3, 2, 1234, -2), (-4, 5, 1234, 123), (-5, 0, 1234, 123), (-6, 0, 0, -2), (-1, 0, 0, null)
            })
            {
                var observed = fighters[actor];
                Assert.Equal(grade, observed.GetProperty("MonsterGrade").GetInt32());
                Assert.Equal(template, observed.GetProperty("MonsterTemplate").GetInt32());
                Assert.Equal(owner, observed.GetProperty("SummonerId").ValueKind == System.Text.Json.JsonValueKind.Null
                    ? (long?)null : observed.GetProperty("SummonerId").GetInt64());
                Assert.Equal(1_000_000, observed.GetProperty("Life").GetInt32());
                Assert.Equal(0, observed.GetProperty("MaxLife").GetInt32());
                Assert.Equal(System.Text.Json.JsonValueKind.Null, observed.GetProperty("Characteristics").ValueKind);
            }
            Assert.Equal(0, fighter.GetProperty("MonsterGrade").GetInt32());
            Assert.Single(frames); // Only the normal finish-turn request; no cast or movement.
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Scope.Value = previousScope;
            TransportLog.Enabled = wasEnabled;
        }
    }

}

