extern alias F06App;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using LogEntry = F06App::GivreClone.App.Services.LuaLogEntry;
using LogLevel = F06App::GivreClone.App.Services.LuaLogLevel;
using LuaRunOutcome = F06App::GivreClone.App.Services.LuaRunOutcome;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;

namespace GivreClone.Tests;

public sealed class ScriptArrivalCancellationTests
{
    private const uint Pid = 123;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Fields = ["_pendingMapChange", "_pendingHarvest", "_pendingAttack"];
    private static LaunchService Service()
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (var name in Fields)
        {
            var field = typeof(LaunchService).GetField(name, Private)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        return service;
    }
    private static IDictionary Pending(LaunchService service, string name)
        => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(service)!;
    private static void Arm(LaunchService service, uint pid, CancellationToken owner)
    {
        Pending(service, Fields[0])[pid] = (456, Environment.TickCount64, owner);
        Pending(service, Fields[1])[pid] = (1, 2, true, owner);
        // A pending attack now carries a target lease plus movement/arrival
        // correlation. These tests still assert cancellation semantics, not
        // network success; seed a structurally complete dummy action.
        Pending(service, Fields[2])[pid] = (3L, (string?)null, owner, 42L,
            (Func<bool>)(() => true), Environment.TickCount64, 100L, 200,
            (Func<int, bool>)(_ => true),
            (Func<int, Func<bool>, bool>)((_, submit) => submit()));
    }
    private static void Cancel(LaunchService service, CancellationTokenSource run)
        => typeof(LaunchService).GetMethod("CancelScriptArrivalActions", Private)!.Invoke(service, [Pid, run]);

    [Fact]
    public void Stop_removes_all_three_arrival_actions_and_is_repeatable()
    {
        var service = Service();
        using var run = new CancellationTokenSource();
        Arm(service, Pid, run.Token);
        Cancel(service, run);
        Cancel(service, run);
        Assert.True(run.IsCancellationRequested);
        foreach (var name in Fields) Assert.False(Pending(service, name).Contains(Pid));
    }

    [Fact]
    public void Late_stop_of_A_preserves_replacement_B_and_another_bot()
    {
        var service = Service();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        Arm(service, Pid, a.Token);
        Arm(service, Pid, b.Token);
        Arm(service, Pid + 1, a.Token);
        Cancel(service, a);
        Assert.False(b.IsCancellationRequested);
        foreach (var name in Fields)
        {
            Assert.True(Pending(service, name).Contains(Pid));
            Assert.True(Pending(service, name).Contains(Pid + 1));
        }
        Cancel(service, b);
        foreach (var name in Fields) Assert.False(Pending(service, name).Contains(Pid));
    }

    [Fact]
    public void Stopping_script_does_not_remove_manual_actions()
    {
        var service = Service();
        using var run = new CancellationTokenSource();
        Arm(service, Pid, default);
        Cancel(service, run);
        foreach (var name in Fields) Assert.True(Pending(service, name).Contains(Pid));
    }
    [Fact]
    public void A_failing_cancellation_observer_still_cleans_arrival_actions()
    {
        var service = Service();
        using var run = new CancellationTokenSource();
        using var observer = run.Token.Register(() => throw new InvalidOperationException("observer"));
        Arm(service, Pid, run.Token);
        Cancel(service, run);
        foreach (var name in Fields) Assert.False(Pending(service, name).Contains(Pid));
    }

    // ------------------------------------------------------------------
    // Task 22 : l'arrêt COOPÉRATIF pendant le RÉGLAGE D'ARRIVÉE d'un zaapi.
    //
    // Le PIÈGE historique : le réglage avalait le ScriptStopException (marqueur d'arrêt
    // du runtime Lua) et rendait quand même « arrivée réussie ». Le run repartait alors
    // sur la carte d'arrivée et l'instruction Lua SUIVANTE s'exécutait, alors que le
    // script venait d'être arrêté.
    //
    // Ces tests passent par le VRAI chemin d'arrivée (QuickZaapiAsync -> ConfirmZaapTeleport
    // -> réglage d'arrivée) : aucun algorithme dupliqué, aucune assertion sur le source.
    // ------------------------------------------------------------------

    private const uint ZaapiPid = 0x40000001;
    private const long OriginMap = 162791424;
    private const long DestinationMap = 54172969;
    private const int ZaapiCost = 20;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private sealed class ZaapiFixture : IDisposable
    {
        public required Host Host { get; init; }
        public required Launch Service { get; init; }
        public required CancellationTokenSource Stop { get; init; }
        public int Injections { get; set; }
        public void Dispose() { Stop.Dispose(); Host.Dispose(); }
    }

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, Private)!.SetValue(target, value);

    private static IDictionary Field(Launch service, string name)
        => (IDictionary)typeof(Launch).GetField(name, Private)!.GetValue(service)!;

    /// <summary>
    /// Zaapi OUVERT sur la carte courante, destination annoncée (20 kamas) et transport
    /// d'injection branché : l'envoi du TeleportRequest fait réellement arriver le perso
    /// (carte observée + position self confirmée) et demande l'arrêt après
    /// <paramref name="stopAfterArrival"/> — via <paramref name="requestStop"/> si fourni
    /// (arrêt du run Lua), sinon via l'annulation du jeton passé par l'appelant.
    /// </summary>
    private static ZaapiFixture Zaapi(int settleMs, TimeSpan? stopAfterArrival, Action<Host>? requestStop = null)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Private).Where(f => f.FieldType.IsGenericType
            && f.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>)))
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        // Le reste des tables de l'hôte (hors ConcurrentDictionary) : même jeu de champs que la
        // bancale hors-ligne de LuaHostBindingTests — sans quoi un verbe Lua touche une table
        // jamais initialisée (LaunchService n'est pas construit : GetUninitializedObject).
        foreach (var name in new[]
        {
            "_controlledCombat", "_controlledHistory", "_engineControlled", "_serverTurnActor",
            "_controlledTurnStarted", "_automaticTurnStop", "_myTurnOpen", "_turnEpoch", "_charId", "_allies", "_fighters",
            "_charSpells", "_charSpellLevels", "_castLedgers", "_fighterStats", "_selfChars", "_selfRange",
            "_confirmedEnemies", "_fighterTeam", "_fightMonsters", "_treasureEncounters", "_treasureAttacked",
            "_treasure", "_fightTurnNo", "_legendaryTreasure", "_fightId", "_teamIntents", "_teamIntentRound",
            "_teamIntentFightKeys", "_selfInfo", "_characters", "_inventory", "_itemNames", "_jobLevels",
            "_knownZaaps", "_zaapiStops", "_hdvAnyOpen", "_fightEndAt", "_travelPref", "_zaapRoute",
            "_serverInfo", "_lifeStatus", "_placelessInteractives", "_subArea"
        })
        {
            var field = typeof(Launch).GetField(name, Private);
            if (field is not null) field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        var teamWait = typeof(Launch).GetField("_teamMapWait", Private);
        if (teamWait is not null) teamWait.SetValue(service, new Dictionary<uint, uint[]>());
        var teamCombatWait = typeof(Launch).GetField("_teamCombatWait", Private);
        if (teamCombatWait is not null) teamCombatWait.SetValue(service, new Dictionary<uint, uint[]>());
        SetField(service, "_catalog", Catalog);
        SetField(service, "_gameNames", new F06App::GivreClone.App.Services.GameNameResolver());

        Field(service, "_lastMap")[ZaapiPid] = OriginMap;
        Field(service, "_kamas")[ZaapiPid] = 1000L;
        Field(service, "_openTeleportType")[ZaapiPid] = GivreClone.App.Services.ZaapiTravel.ListType;
        var destination = new TeleportReader.Destination(DestinationMap, ZaapiCost, 601, 80, CostKnown: true, Type: 1, TypeKnown: true);
        Field(service, "_teleportOpen")[ZaapiPid] =
            new Launch.TeleportState(GivreClone.App.Services.ZaapiTravel.ListType, new[] { destination }, OriginMap);

        var world = new WorldStore(service);
        var state = world.Get(ZaapiPid);
        state.Interactives = new[]
        {
            new MapInteractive(50, 900001, GivreClone.App.Services.ZaapiTravel.ElementTypeId, 0,
                new[] { new InteractiveSkill(4242, GivreClone.App.Services.ZaapiTravel.SkillId, 0, true) })
        };
        state.MapId = OriginMap;
        state.CellsMap = OriginMap;
        state.SelfCell = 100;
        state.SelfCellMap = OriginMap;
        state.SelfCellConfirmed = true;
        state.Cells = new[] { new MapCell(100, true, true, 0) };

        var host = new Host(ZaapiPid, service, world);
        SetField(host, "_settleMinMs", settleMs);
        SetField(host, "_settleMaxMs", settleMs);

        var stop = new CancellationTokenSource();
        var fixture = new ZaapiFixture { Host = host, Service = service, Stop = stop };
        service.RegisterSocketInjector(ZaapiPid, packet =>
        {
            fixture.Injections++;
            // L'ARRIVÉE EST OBSERVÉE : carte observée + position self confirmée.
            Field(service, "_lastMap")[ZaapiPid] = DestinationMap;
            var arrived = world.Get(ZaapiPid);
            arrived.MapId = DestinationMap;
            arrived.CellsMap = DestinationMap;
            arrived.SelfCell = 100;
            arrived.SelfCellMap = DestinationMap;
            arrived.SelfCellConfirmed = true;
            arrived.Cells = new[] { new MapCell(100, true, true, 0) };
            if (stopAfterArrival is { } delay)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay);
                    if (requestStop is not null) requestStop(host);   // arrêt COOPÉRATIF du run Lua
                    else stop.Cancel();
                });
            return true;
        });

        return fixture;
    }

    [Fact]
    public async Task Stop_during_the_zaapi_arrival_settlement_is_not_reported_as_a_success()
    {
        // Réglage long (20 s) et arrêt demandé 1,5 s après l'arrivée OBSERVÉE : l'arrêt tombe
        // dans le settle de carte (donc PENDANT le réglage), jamais avant l'envoi ni après.
        using var fixture = Zaapi(settleMs: 20_000, stopAfterArrival: TimeSpan.FromMilliseconds(1_500));
        var logs = new List<LogEntry>();
        fixture.Host.Log += logs.Add;

        var result = await fixture.Host.QuickZaapiAsync(DestinationMap, 0, fixture.Stop.Token);

        // Le TeleportRequest a RÉELLEMENT été injecté et l'arrivée observée : le test passe par le
        // VRAI chemin d'arrivée (zaapi ouvert -> envoi -> confirmation -> réglage d'arrivée).
        Assert.Equal(1, fixture.Injections);
        Assert.Equal(DestinationMap, fixture.Service.CurrentMap(ZaapiPid));
        Assert.True(fixture.Stop.IsCancellationRequested);

        // L'arrêt COOPÉRATIF n'est PAS un succès : le voyage est rendu « annulé ».
        Assert.False(result.Ok);
        Assert.Equal(GivreClone.App.Services.QuickActions.Cancelled, result.Reason);
        Assert.NotNull(fixture.Host.LastTravelResult);
        Assert.False(fixture.Host.LastTravelResult!.Value.Ok);
        // ... et aucun succès « arrivée » n'est journalisé.
        Assert.DoesNotContain(logs, e => e.Level == LogLevel.Success);
    }

    [Fact]
    public async Task Normal_zaapi_arrival_still_reports_the_arrival_and_its_reason()
    {
        using var fixture = Zaapi(settleMs: 0, stopAfterArrival: null);
        var logs = new List<LogEntry>();
        fixture.Host.Log += logs.Add;

        var result = await fixture.Host.QuickZaapiAsync(DestinationMap, 0, fixture.Stop.Token);

        string expected = GivreClone.App.Services.ZaapiTravel.ArrivedReason(DestinationMap, ZaapiCost);
        Assert.True(result.Ok);
        Assert.Equal(expected, result.Reason);
        Assert.Equal((true, expected), fixture.Host.LastTravelResult!.Value);
        Assert.Contains(logs, e => e.Level == LogLevel.Success && e.Category == "trajet" && e.Message == expected);
    }

    // ------------------------------------------------------------------
    // Task 22 (preuve Lua) : le chemin LUA-facing du réglage d'arrivée (FinishZaapi) doit
    // PROPAGER l'arrêt jusqu'au run : le script s'arrête et l'instruction LUA SUIVANTE ne
    // s'exécute pas. Ce chemin est déjà correct en production (aucun catch) : ce test le
    // PROUVE, il ne prouve pas un correctif (pas de faux « échec avant » inventé).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Stop_during_the_lua_facing_zaapi_settlement_stops_the_script_before_the_next_statement()
    {
        // Même fixture de transport que ci-dessus (vraie injection TeleportRequest, vraie
        // observation d'arrivée) ; l'arrêt est demandé 1,5 s après l'arrivée observée, avec un
        // réglage de 20 s -> il tombe DANS le settle de FinishZaapi.
        using var fixture = Zaapi(settleMs: 20_000, stopAfterArrival: TimeSpan.FromMilliseconds(1_500),
            requestStop: h => h.Stop("task22-stop"));
        var logs = new List<LogEntry>();
        fixture.Host.Log += logs.Add;

        // VRAI script Lua : le verbe enregistré zaapi.go (même forme que le script MCP
        // « zaapi_to » de production), puis une instruction observable.
        fixture.Host.RunOnce(
            "print('BEFORE-ZAAPI')\n" +
            $"zaapi.go({DestinationMap})\n" +
            "print('AFTER-ZAAPI')\n");

        var outcome = await fixture.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(60));

        // Le TeleportRequest a really been injected and the arrival observed: the verb really ran.
        Assert.Equal(1, fixture.Injections);
        Assert.Equal(DestinationMap, fixture.Service.CurrentMap(ZaapiPid));

        // Le run s'arrête sur l'arrêt COOPÉRATIF (et non sur une erreur de script).
        Assert.Equal(LuaRunOutcome.Stopped, outcome);

        // Le script a bien démarré (l'instruction AVANT le verbe a tourné)...
        Assert.Contains(logs, e => e.Message.Contains("BEFORE-ZAAPI"));
        // ... mais l'instruction APRÈS le verbe n'a JAMAIS été exécutée : l'arrêt a déroulé
        // le script au milieu du réglage d'arrivée.
        Assert.DoesNotContain(logs, e => e.Message.Contains("AFTER-ZAAPI"));
    }
}
