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
using Config = F06App::GivreClone.App.Services.SpellConfig;
using Reader = F06App::GivreClone.App.Services.MapCellReader;
using Names = F06App::GivreClone.App.Services.GameNameResolver;
using Combat = F06App::GivreClone.App.Services.Combat;

namespace GivreClone.Tests;

public sealed class SramLearnedDispatchIntegrationTests
{
    private const uint Pid = 0x40000113;
    private const long MapId = 88085763;
    private const int Invisibility = 12913;
    private const int Attack = 42;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    [Theory]
    [InlineData(1, false, true, true)]
    [InlineData(2, false, true, true)]
    [InlineData(3, false, true, true)]
    [InlineData(1, true, true, false)]
    [InlineData(2, true, true, false)]
    [InlineData(3, true, true, false)]
    [InlineData(1, false, false, true)]
    [InlineData(2, false, false, true)]
    [InlineData(3, false, false, true)]
    [InlineData(2, true, false, false, true)]
    [InlineData(2, true, true, false, false, true)]
    [InlineData(2, true, true, true, false, true, false)]
    // Without an attack the MP gain is unproductive, but self invisibility still earns bounded defensive
    // utility against the assumed (unknown) enemy reply, so it is cast on the caster's own cell.
    public async Task Learned_invisibility_dispatches_on_self_for_productive_mp_but_manual_enemy_restriction_does_not(
        int grade, bool manualEnemy, bool hasAttack, bool expectBoost, bool deferredOnly = false, bool invulnerable = false, bool knownRule = true)
    {
        // Real LaunchService adapter and socket producer, with the same rejected-injection
        // convention as SummonDispatchIntegrationTests: no server wait or account is involved.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (string name in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            Field(name).SetValue(service, new object());
        foreach (string name in new[] { "_engineControlled", "_teamIntents" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        var profile = new Profile { AutoFight = true, AutoSpells = true, Speed = Speed.Turbo };
        using var fixture = new FakeGameDb();
        using (var writer = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = "create table SpellStateData(id integer primary key, invulnerable integer, invulnerableMelee integer, invulnerableRange integer, cantDealDamage integer); insert into SpellStateData values(9001,1,0,0,0);";
            command.ExecuteNonQuery();
            if (!knownRule)
            {
                command.CommandText = "delete from SpellStateData where id=9001";
                command.ExecuteNonQuery();
            }
        }
        using var db = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(fixture.File);
        Field("_gameDb").SetValue(service, db);
        if (invulnerable)
            Map(service, "_activeFighterStates")[(Pid, -100L)] = new HashSet<int> { 9001 };
        // Default Enemy is intentional for this manual control. Learned-only has no profile entry.
        if (manualEnemy) profile.Spells.Add(new Config { SpellId = Invisibility });
        Map(service, "_combat")[Pid] = profile;
        Map(service, "_charId")[Pid] = 123L;
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (Cell(0), 123), (Cell(2), -100) };
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_jzcSeen")[Pid] = true;
        Map(service, "_charSpells")[Pid] = hasAttack ? new[] { Invisibility, Attack } : new[] { Invisibility };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [Invisibility] = grade, [Attack] = 1 };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(123, new(123, 5, 0, 100, 100, 0, 0)),
            new KeyValuePair<long, FighterStats>(-100, new(-100, 0, 0, 1000, 1000, 0, 0))
        });
        var visibility = new FightVisibilityReducer();
        visibility.Observe(new(-100, 2), positionConfirmed: true);
        Map(service, "_fighterVisibility")[Pid] = visibility;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        // SYNTHETIC tactical corridor, not a real map or full Sram kit: at zero MP the
        // range-one attack cannot reach Cell(2). One gained MP opens Cell(1) then damage.
        typeof(Reader).GetField("_cache", Hidden)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, 3)
                    .Select(i => new MapCell(Cell(i), true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;
        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
            {
                [Invisibility] = new() { [grade] = (2, 0, grade + 3, 512, false) },
                [Attack] = new() { [1] = (3, 1, invulnerable ? 3 : 1, 0, false) }
            });
        int mp = grade == 1 ? 1 : 2;
        // Faithful exported 12913 rows (all three grades): action150 Unknown, a,A,
        // duration1; action128 allied MP, duration2. GlobalCooldown=1 is represented
        // by Uncertain, as in GameNameResolver; SpellCastDescriptor has no raw field.
        var boost = new Combat.SpellCastDescriptor(Invisibility, grade, 2, 0, grade + 3, 512, false,
            0, 0, 3, 0, "", ImmutableArray.Create(
                new Combat.SpellEffectDescriptor(150, Combat.SpellEffectKind.Unknown,
                    0, 0, -1, 1, "a,A", 80, 1, 0, Triggers: "I"),
                new Combat.SpellEffectDescriptor(128, Combat.SpellEffectKind.Buff,
                    mp, mp, -1, 2, "a", 80, 1, 0, DiceNum: mp, Triggers: "I")), Uncertain: true);
        var attack = new Combat.SpellCastDescriptor(Attack, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                10, 10, 0, 0, "A", 'P', 0, 0)));
        if (invulnerable) attack = attack with { Range = 3 };
        // Synthetic trigger-only setup on an enemy: no immediate attack or productive
        // follow-up. Neither offensive rescue path may spend AP on it blindly.
        if (deferredOnly)
            boost = boost with { Effects = ImmutableArray.Create(new Combat.SpellEffectDescriptor(
                1160, Combat.SpellEffectKind.Unknown, 0, 0, 0, 0, "A", 'P', 0, 0,
                DiceNum: 99999, DiceSide: 1, Triggers: "D|XD")) };
        typeof(Names).GetField("_spellDescriptors", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>>
            { [Invisibility] = new() { [grade] = boost }, [Attack] = new() { [1] = attack } });
        Field("_gameNames").SetValue(service, names);
        var frames = new List<byte[]>();
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame => { frames.Add(frame); return false; });

        var run = (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(service,
            [Pid, 0, false, CancellationToken.None, null, 1])!;
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        var requests = frames.Select(frame =>
        {
            var payload = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
            Assert.Empty(rest);
            return Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(payload)));
        }).ToArray();
        Assert.Equal(expectBoost
                ? new[] { catalog.CodeForName("GameActionFightCastRequest"), catalog.CodeForName("FightTurnFinishRequest") }
                : new[] { catalog.CodeForName("FightTurnFinishRequest") },
            requests.Select(r => r.Code));
        if (expectBoost)
        {
            var cast = requests[0].Payload!;
            int F(string field) => catalog.FieldNumber("GameActionFightCastRequest", field)
                ?? throw new InvalidOperationException($"Missing cast field {field}");
            Assert.Equal((ulong)(invulnerable ? Attack : Invisibility), cast.Single(f => f.Field == F("spell_id")).Varint);
            Assert.Equal((ulong)Cell(invulnerable ? 2 : 0), cast.Single(f => f.Field == F("cell")).Varint);
        }
    }
}
