extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Reader = F06App::GivreClone.App.Services.MapCellReader;

namespace GivreClone.Tests;

public sealed class CombatMapGeometryTests
{
    private const long Roleplay = 88085763, Arena = 88218629;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Flags)!;
    private static IDictionary Map(Launch s, string name) => (IDictionary)Field(name).GetValue(s)!;
    private static object? Call(Launch s, string name, params object?[] args) => typeof(Launch).GetMethod(name, Flags)!.Invoke(s, args);

    private static Launch Service()
    {
        var s = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[] { "_lastMap", "_serverMapId", "_inFight", "_combatMapTickets", "_fighters", "_charId",
            "_blockedCells", "_fightObstacles", "_controlledCombat", "_serverTurnActor" })
            Field(name).SetValue(s, Activator.CreateInstance(Field(name).FieldType));
        Field("_combatMapGate").SetValue(s, new object());
        // No installed game, disk cache, constructor, network or live accounts needed.
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        var cache = new ConcurrentDictionary<long, MapCell[]>();
        cache[Roleplay] = Grid(false);
        cache[Arena] = Grid(true);
        typeof(Reader).GetField("_cache", Flags)!.SetValue(reader, cache);
        typeof(Reader).GetField("_exitsCache", Flags)!.SetValue(reader, new ConcurrentDictionary<long, MapExits>());
        Field("_cellReader").SetValue(s, reader);
        Map(s, "_lastMap")[1u] = Roleplay;
        Map(s, "_charId")[1u] = 123L;
        Map(s, "_fighters")[1u] = new (int Cell, long ActorId)[] { (353, 123) };
        return s;
    }

    private static MapCell[] Grid(bool arena) => Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(c => new MapCell(c, arena || c != 353, arena, 0, LinkedZone: 17)).ToArray();

    private static (long MapId, MapCell[]? Cells) Load(Launch s, uint pid = 1) =>
        ((long, MapCell[]?))Call(s, "LoadFightMap", pid)!;

    [Fact]
    public async Task Map_current_before_start_and_context_ready_still_selects_the_arena()
    {
        var s = Service();
        Call(s, "ObserveServerMap", 1u, Arena); // Actual observed order: MapCurrent, ContextReady, FightStarting.
        Assert.False(s.IsInFight(1));
        Assert.Equal(Roleplay, Load(s).MapId);
        var published = new List<long>();
        s.MapCellsLoaded += (_, map, _) => published.Add(map);
        await (Task)Call(s, "StartCombatMapAsync", 1u)!;
        Assert.Equal(Arena, Load(s).MapId);
        Assert.True(Load(s).Cells!.Single(c => c.CellId == 353).Mov);
        Assert.Equal(new[] { Arena }, published);
        Assert.Equal(Roleplay, Map(s, "_lastMap")[1u]); // Preserve the known roleplay fallback.
    }

    [Fact]
    public void First_turn_and_lua_geometry_do_not_wait_for_a_background_reload()
    {
        var s = Service();
        Map(s, "_serverMapId")[1u] = Arena;
        Map(s, "_inFight")[1u] = true;
        var g = ((Func<int, bool> Walk, Func<int, int, bool> Step, int SelfCell, long ForgeMap))Call(s, "BuildFightGeom", 1u)!;
        Assert.Equal(Arena, g.ForgeMap);
        Assert.True(g.Walk(353));
        Assert.True(MapPathfinder.OrthoReachable(g.SelfCell, 5, g.Walk, g.Step).Count > 1);
        Assert.False(((Func<int, bool>)Call(s, "LosBlocker", 1u)!)(353));
    }

    [Fact]
    public void Unknown_instance_preserves_valid_geometry_but_uses_authoritative_forge_id()
    {
        var s = Service();
        Map(s, "_serverMapId")[1u] = 999999999L;
        Map(s, "_inFight")[1u] = true;
        Assert.Equal(Roleplay, Load(s).MapId);
        Assert.NotNull(Load(s).Cells);
        var g = ((Func<int, bool>, Func<int, int, bool>, int, long))Call(s, "BuildFightGeom", 1u)!;
        Assert.Equal(999999999L, g.Item4);
    }

    [Fact]
    public void Arena_and_roleplay_fallback_are_pid_isolated()
    {
        var s = Service();
        Map(s, "_serverMapId")[1u] = Arena;
        Map(s, "_inFight")[1u] = true;
        Map(s, "_lastMap")[2u] = Roleplay;
        Map(s, "_inFight")[2u] = true;
        Assert.Equal(Arena, Load(s).MapId);
        Assert.Equal(Roleplay, Load(s, 2).MapId);
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("invalidated")]
    [InlineData("new-session-same-map")]
    [InlineData("new-map")]
    [InlineData("invalid-server-map")]
    public void Stale_geometry_cannot_publish_or_erase_newer_state(string reason)
    {
        var s = Service();
        object old = new();
        Map(s, "_inFight")[1u] = true;
        Map(s, "_serverMapId")[1u] = Arena;
        Map(s, "_combatMapTickets")[1u] = old;
        int published = 0;
        s.MapCellsLoaded += (_, _, _) => published++;
        s.MapActorsDecoded += (_, _) => published++;
        s.MapInteractivesDecoded += (_, _) => published++;
        if (reason == "ended") Map(s, "_inFight")[1u] = false;
        if (reason == "invalidated") Call(s, "InvalidateCombatMap", 1u);
        if (reason == "new-session-same-map") Map(s, "_combatMapTickets")[1u] = new object();
        if (reason == "new-map") Map(s, "_serverMapId")[1u] = Roleplay;
        if (reason == "invalid-server-map") Call(s, "ObserveServerMap", 1u, 0L);
        Call(s, "PublishCombatMap", 1u, Arena, old, Grid(true), null);
        Assert.Equal(0, published);
        Assert.Equal(Roleplay, Map(s, "_lastMap")[1u]);
        Assert.Single((IEnumerable<(int, long)>)Map(s, "_fighters")[1u]!);
    }

    [Fact]
    public void Current_geometry_publication_does_not_clear_fighters_or_interactives()
    {
        var s = Service();
        object ticket = new();
        Map(s, "_inFight")[1u] = true;
        Map(s, "_serverMapId")[1u] = Arena;
        Map(s, "_combatMapTickets")[1u] = ticket;
        int relief = 0, cleared = 0;
        s.MapCellsLoaded += (_, _, _) => relief++;
        s.MapActorsDecoded += (_, _) => cleared++;
        s.MapInteractivesDecoded += (_, _) => cleared++;
        Call(s, "PublishCombatMap", 1u, Arena, ticket, Grid(true), null);
        Assert.Equal(1, relief);
        Assert.Equal(0, cleared);
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("same-map-new-snapshot")]
    [InlineData("different-map")]
    [InlineData("session-ended")]
    public async Task Delayed_roleplay_work_cannot_overwrite_a_newer_context(string transition)
    {
        var s = Service();
        var old = Call(s, "BeginRoleplayMap", 1u, Roleplay)!;
        if (transition == "combat") await (Task)Call(s, "StartCombatMapAsync", 1u)!;
        if (transition == "same-map-new-snapshot") Call(s, "BeginRoleplayMap", 1u, Roleplay);
        if (transition == "different-map") Call(s, "BeginRoleplayMap", 1u, Arena);
        if (transition == "session-ended") Call(s, "InvalidateCombatMap", 1u);
        int published = 0;
        Call(s, "PublishRoleplayMap", 1u, Roleplay, old, (Action)(() => published++));
        Assert.Equal(0, published);
    }

    [Fact]
    public async Task Same_roleplay_map_is_republished_after_the_arena_without_accepting_old_work()
    {
        var s = Service();
        var before = Call(s, "BeginRoleplayMap", 1u, Roleplay)!;
        Call(s, "ObserveServerMap", 1u, Arena);
        await (Task)Call(s, "StartCombatMapAsync", 1u)!;
        Map(s, "_inFight")[1u] = false;
        Call(s, "InvalidateCombatMap", 1u);
        Call(s, "ObserveServerMap", 1u, Roleplay);
        var after = Call(s, "BeginRoleplayMap", 1u, Roleplay)!;
        var published = new List<long>();
        Action publish = () => published.Add(Roleplay);
        Call(s, "PublishRoleplayMap", 1u, Roleplay, before, publish);
        Assert.Empty(published);
        Call(s, "PublishRoleplayMap", 1u, Roleplay, after, publish);
        Assert.Equal(new[] { Roleplay }, published);
        Assert.Equal(Roleplay, Load(s).MapId);
    }

    [Fact]
    public async Task Duplicate_readiness_refresh_keeps_the_geometry_context_stable()
    {
        var s = Service();
        Call(s, "ObserveServerMap", 1u, Arena);
        await (Task)Call(s, "StartCombatMapAsync", 1u)!;
        var ticket = Map(s, "_combatMapTickets")[1u];
        await (Task)Call(s, "RefreshCombatMapAsync", 1u)!;
        Assert.Same(ticket, Map(s, "_combatMapTickets")[1u]);
        await (Task)Call(s, "StartCombatMapAsync", 1u)!;
        Assert.NotSame(ticket, Map(s, "_combatMapTickets")[1u]);
    }
}
