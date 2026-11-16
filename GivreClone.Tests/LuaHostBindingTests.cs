extern alias F06App;
using System.Reflection;
using System.Runtime.CompilerServices;
using F06App::GivreClone.App.Services;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using GameNames = F06App::GivreClone.App.Services.GameNameResolver;
using MoonSharp.Interpreter;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Exercices offline des bindings réels de GivreLuaHost.
///
/// SEAM INTERNE DOCUMENTÉ :
/// Pour exécuter du Lua de manière synchrone et unitaire sans lancer un thread de fond avec boucle de jeu,
/// GivreLuaHost expose deux méthodes internes :
/// - <c>TestInitScript(CancellationToken)</c> : crée le Script MoonSharp (Preset_SoftSandbox), l'assigne à <c>_script</c>
///   et y lie l'intégralité des modules et verbes (<c>Bind(script, ct)</c>).
/// - <c>TestSelectMonster(BotWorldState, int, int, HashSet&lt;long&gt;?)</c> : appelle la sélection de groupe privée
///   <c>SelectMonster</c> pour vérifier le filtrage avec les filtres configurés dans le Lua context.
///
/// Le projet de tests GivreClone.Tests accède à ces membres grâce à l'InternalsVisibleTo configuré sur GivreClone.App.
/// </summary>
public sealed class LuaHostHarness : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo Field(string name) => typeof(LaunchService).GetField(name, Private)!;

    public LaunchService Launch { get; }
    public BotWorldStore World { get; }
    public GivreLuaHost Host { get; }
    public Script Script { get; }
    public uint Pid { get; } = 1;

    private LuaHostHarness(LaunchService launch, BotWorldStore world, GivreLuaHost host, Script script)
    {
        Launch = launch;
        World = world;
        Host = host;
        Script = script;
    }

    public static LuaHostHarness Create(uint pid = 1)
    {
        var launch = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] {
            "_controlledCombat", "_controlledHistory", "_engineControlled", "_serverTurnActor",
            "_controlledTurnStarted", "_automaticTurnStop", "_myTurnOpen", "_turnEpoch", "_charId", "_inFight", "_allies", "_fighters",
            "_charSpells", "_charSpellLevels", "_castLedgers", "_fighterStats", "_selfChars", "_selfRange", "_confirmedEnemies", "_fighterTeam",
            "_fightMonsters", "_treasureEncounters", "_treasureAttacked", "_treasure", "_fightTurnNo", "_legendaryTreasure",
            "_fightId", "_teamIntents", "_teamIntentRound", "_teamIntentFightKeys", "_selfInfo", "_characters",
            "_inventory", "_itemNames", "_jobLevels", "_knownZaaps", "_zaapiStops",
            "_lastMap", "_hdvAnyOpen", "_fightEndAt", "_travelPref", "_zaapRoute",
            "_serverInfo", "_lifeStatus", "_placelessInteractives", "_kamas", "_subArea" })
        {
            var f = Field(name);
            if (f != null) f.SetValue(launch, Activator.CreateInstance(f.FieldType));
        }

        var fTeam = Field("_teamMapWait");
        if (fTeam != null) fTeam.SetValue(launch, new Dictionary<uint, uint[]>());
        var fCombatWait = Field("_teamCombatWait");
        if (fCombatWait != null) fCombatWait.SetValue(launch, new Dictionary<uint, uint[]>());
        var fGameNames = Field("_gameNames");
        if (fGameNames != null) fGameNames.SetValue(launch, new GameNames());

        var store = new BotWorldStore(launch);
        var host = new GivreLuaHost(pid, launch, store);
        var script = host.TestInitScript();

        return new LuaHostHarness(launch, store, host, script);
    }

    public void SetCurrentMap(long mapId)
    {
        var f = Field("_lastMap");
        var mapDict = (System.Collections.Concurrent.ConcurrentDictionary<uint, long>)f.GetValue(Launch)!;
        mapDict[Pid] = mapId;
    }

    public DynValue Run(string luaSnippet) => Script.DoString(luaSnippet);

    public DynValue GetGlobal(string name) => Script.Globals.Get(name);

    public void Dispose() => Host.Dispose();
}

public class LuaHostBindingTests
{
    [Fact]
    public void SetForbiddenMonsters_keeps_both_numbers_and_names_and_getForbiddenMonsters_returns_both()
    {
        using var harness = LuaHostHarness.Create();

        harness.Run("config:setForbiddenMonsters({ 2303, \"Rouquette\" })");

        // Vérification de la variable globale FORBIDDEN_MONSTERS
        var forbiddenGlobal = harness.GetGlobal("FORBIDDEN_MONSTERS");
        Assert.Equal(DataType.Table, forbiddenGlobal.Type);
        var values = forbiddenGlobal.Table.Values.ToList();
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.Type == DataType.Number && (int)v.Number == 2303);
        Assert.Contains(values, v => v.Type == DataType.String && v.String == "Rouquette");

        // Vérification via le getter getForbiddenMonsters
        var returned = harness.Run("return config:getForbiddenMonsters()");
        Assert.Equal(DataType.Table, returned.Type);
        var returnedValues = returned.Table.Values.ToList();
        Assert.Equal(2, returnedValues.Count);
        Assert.Contains(returnedValues, v => v.Type == DataType.Number && (int)v.Number == 2303);
        Assert.Contains(returnedValues, v => v.Type == DataType.String && v.String == "Rouquette");
    }

    [Fact]
    public void SelectMonster_rejects_group_containing_forbidden_name_Rouquette()
    {
        using var harness = LuaHostHarness.Create();

        // On configure les monstres interdits via l'API config
        harness.Run("config:setForbiddenMonsters({ 2303, \"Rouquette\" })");

        var st = harness.World.Get(harness.Pid);
        st.MapId = 12345;
        st.SelfCell = 200;

        // Groupe 1 : contient Rouquette (doit être rejeté)
        var groupWithRouquette = new MapActor(
            CellId: 201,
            Direction: 0,
            ActorId: 101,
            Name: "Rouquette",
            Kind: MapActorKind.Monster,
            Monsters: new List<MonsterEntry>
            {
                new(GenericId: 9999, Name: "Rouquette", Count: 1)
            }
        );

        // Groupe 2 : monstre légitime (doit être sélectionné)
        var groupAllowed = new MapActor(
            CellId: 202,
            Direction: 0,
            ActorId: 102,
            Name: "Piaf",
            Kind: MapActorKind.Monster,
            Monsters: new List<MonsterEntry>
            {
                new(GenericId: 100, Name: "Piaf", Count: 1)
            }
        );

        st.Actors = new List<MapActor> { groupWithRouquette, groupAllowed };

        var selected = harness.Host.TestSelectMonster(st, src: 200, preferredCell: -1);
        Assert.NotNull(selected);
        Assert.Equal(102L, selected!.ActorId);
        Assert.Equal("Piaf", selected.Name);

        // Maintenant avec seulement le groupe Rouquette
        st.Actors = new List<MapActor> { groupWithRouquette };
        var selectedNone = harness.Host.TestSelectMonster(st, src: 200, preferredCell: -1);
        Assert.Null(selectedNone);
    }

    [Fact]
    public void SetMandatoryMonsters_keeps_both_numbers_and_names_and_filters_groups()
    {
        using var harness = LuaHostHarness.Create();

        harness.Run("config:setMandatoryMonsters({ 1234, \"ChefBouftou\" })");

        // Vérification globale
        var forceGlobal = harness.GetGlobal("FORCE_MONSTERS");
        Assert.Equal(DataType.Table, forceGlobal.Type);
        var values = forceGlobal.Table.Values.ToList();
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.Type == DataType.Number && (int)v.Number == 1234);
        Assert.Contains(values, v => v.Type == DataType.String && v.String == "ChefBouftou");

        // Vérification via getMandatoryMonsters
        var returned = harness.Run("return config:getMandatoryMonsters()");
        Assert.Equal(DataType.Table, returned.Type);
        var returnedValues = returned.Table.Values.ToList();
        Assert.Equal(2, returnedValues.Count);
        Assert.Contains(returnedValues, v => v.Type == DataType.Number && (int)v.Number == 1234);
        Assert.Contains(returnedValues, v => v.Type == DataType.String && v.String == "ChefBouftou");

        // Filtrage SelectMonster : groupe sans ChefBouftou rejeté, groupe avec ChefBouftou accepté
        var st = harness.World.Get(harness.Pid);
        st.MapId = 12345;
        st.SelfCell = 200;

        var groupWithoutMandatory = new MapActor(
            CellId: 201,
            Direction: 0,
            ActorId: 201,
            Name: "Bouftou",
            Kind: MapActorKind.Monster,
            Monsters: new List<MonsterEntry>
            {
                new(GenericId: 101, Name: "Bouftou", Count: 1)
            }
        );

        var groupWithMandatory = new MapActor(
            CellId: 202,
            Direction: 0,
            ActorId: 202,
            Name: "ChefBouftou",
            Kind: MapActorKind.Monster,
            Monsters: new List<MonsterEntry>
            {
                new(GenericId: 555, Name: "ChefBouftou", Count: 1)
            }
        );

        st.Actors = new List<MapActor> { groupWithoutMandatory, groupWithMandatory };

        var selected = harness.Host.TestSelectMonster(st, src: 200, preferredCell: -1);
        Assert.NotNull(selected);
        Assert.Equal(202L, selected!.ActorId);

        // Sans le groupe obligatoire, rien ne doit être éligible
        st.Actors = new List<MapActor> { groupWithoutMandatory };
        var none = harness.Host.TestSelectMonster(st, src: 200, preferredCell: -1);
        Assert.Null(none);
    }

    [Fact]
    public void TravelTo_absent_map_from_graph_returns_false_and_no_route()
    {
        using var harness = LuaHostHarness.Create();
        harness.SetCurrentMap(88080384);

        var result = harness.Run("local ok, r = travelTo(99999999); return { ok = ok, reason = r }");
        Assert.Equal(DataType.Table, result.Type);
        Assert.False(result.Table.Get("ok").Boolean);
        Assert.Equal("no_route", result.Table.Get("reason").String);
    }

    [Fact]
    public void TravelTo_malformed_target_returns_false_and_action_failed_without_throwing()
    {
        using var harness = LuaHostHarness.Create();

        // Target nil
        var resNil = harness.Run("local ok, r = travelTo(nil); return { ok = ok, reason = r }");
        Assert.False(resNil.Table.Get("ok").Boolean);
        Assert.Equal("action_failed", resNil.Table.Get("reason").String);

        // Target string invalide non-numérique
        var resStr = harness.Run("local ok, r = travelTo(\"invalid_map_format\"); return { ok = ok, reason = r }");
        Assert.False(resStr.Table.Get("ok").Boolean);
        Assert.Equal("action_failed", resStr.Table.Get("reason").String);

        // Sans argument
        var resEmpty = harness.Run("local ok, r = travelTo(); return { ok = ok, reason = r }");
        Assert.False(resEmpty.Table.Get("ok").Boolean);
        Assert.Equal("action_failed", resEmpty.Table.Get("reason").String);
    }

    [Fact]
    public void TravelTo_reason_mapping_helper_formats_expected_dynvalues()
    {
        var success = GivreLuaHost.CreateTravelToResult(true, null);
        Assert.Equal(DataType.Boolean, success.Type);
        Assert.True(success.Boolean);

        var failNoRoute = GivreLuaHost.CreateTravelToResult(false, "no_route");
        Assert.Equal(DataType.Tuple, failNoRoute.Type);
        Assert.Equal(2, failNoRoute.Tuple.Length);
        Assert.False(failNoRoute.Tuple[0].Boolean);
        Assert.Equal("no_route", failNoRoute.Tuple[1].String);

        var failDefault = GivreLuaHost.CreateTravelToResult(false, null);
        Assert.Equal(DataType.Tuple, failDefault.Type);
        Assert.Equal(2, failDefault.Tuple.Length);
        Assert.False(failDefault.Tuple[0].Boolean);
        Assert.Equal("action_failed", failDefault.Tuple[1].String);
    }

    [Fact]
    public void TravelTo_boolean_evaluation_in_if_statement_preserves_compatibility()
    {
        using var harness = LuaHostHarness.Create();
        harness.SetCurrentMap(88080384);

        // Dans un if Lua, travelTo échoué doit évaluer false
        var result = harness.Run("if travelTo(99999999) then return \"branch_true\" else return \"branch_false\" end");
        Assert.Equal("branch_false", result.String);
    }
}
