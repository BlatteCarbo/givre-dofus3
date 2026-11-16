extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.App.Services;
using Xunit;
using BotsVm = F06App::GivreClone.App.ViewModels.BotsViewModel;
using GivreClone.Protocol;
using TeamService = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

// Service réel sans constructeur : aucun launcher, compte, port ni store utilisateur.
public sealed class F13TeamPlanningTests
{
    private const uint Leader = 101, Follower = 102;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static TeamService OfflineService()
    {
        var service = (TeamService)RuntimeHelpers.GetUninitializedObject(typeof(TeamService));
        foreach (var name in new[] { "_lastMap", "_inFight", "_fightId", "_teamCombatWait", "_teamMapWait", "_charId", "_selfInfo", "_serverInfo", "_linkDown", "_returnWait", "_party" })
        {
            var field = typeof(TeamService).GetField(name, Private)!;
            field.SetValue(service, field.FieldType.IsInterface ? new Dictionary<uint, uint[]>() : Activator.CreateInstance(field.FieldType));
        }
        State<long>(service, "_lastMap")[Leader] = 10;
        State<long>(service, "_lastMap")[Follower] = 20;
        return service;
    }
    private static ConcurrentDictionary<uint, T> State<T>(TeamService service, string name)
        => (ConcurrentDictionary<uint, T>)typeof(TeamService).GetField(name, Private)!.GetValue(service)!;

    [Fact]
    public void Reconnection_rechecks_a_different_script_even_in_the_same_occurrence()
    {
        Assert.Equal(PlanRunAction.Switch, PlanningEval.DecideRun("slot@20261024", "slot@20261024",
            true, "-- ancien trajet", false, true, () => "-- trajet prévu"));
    }

    [Fact]
    public void Removing_an_unrelated_slot_does_not_change_the_jitter_window()
    {
        var day = new DateTime(2026, 10, 24);
        var cp = new CharPlanning { Enabled = true, JitterMin = 10 };
        cp.Slots.Add(new PlanningSlot { Id = "inactive", Days = 1, StartMin = 1, EndMin = 2 });
        cp.Slots.Add(new PlanningSlot { Id = "stable", Days = 32, StartMin = 600, EndMin = 720 });
        var before = Enumerable.Range(0, 1440).Select(m => PlanningEval.IsActiveNow(cp, day.AddMinutes(m), "Test")).ToArray();
        cp.Slots.RemoveAt(0);
        var after = Enumerable.Range(0, 1440).Select(m => PlanningEval.IsActiveNow(cp, day.AddMinutes(m), "Test")).ToArray();
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publishing_waits_never_exposes_an_empty_intermediate_snapshot(bool map)
    {
        var service = OfflineService();
        var snapshot = new Dictionary<uint, uint[]> { [Leader] = [Follower] };
        void Publish(IReadOnlyDictionary<uint, uint[]> value)
        {
            if (map) service.SetTeamMapChangeWait(value); else service.SetTeamCombatWait(value);
        }
        bool Pending() => map ? service.TeamMapChangeWaitPending(Leader) : service.TeamCombatWaitPending(Leader);
        Publish(snapshot);
        Assert.True(Pending());
        Publish(new ObservedSnapshot(snapshot, () => Assert.True(Pending())));
        Assert.True(Pending());
        Publish(new Dictionary<uint, uint[]>());
        Assert.False(Pending()); // départ d'un membre : aucune attente résiduelle
    }

    [Fact]
    public void A_follower_in_another_fight_has_not_joined_the_leader()
    {
        var service = OfflineService();
        service.SetTeamCombatWait(new Dictionary<uint, uint[]> { [Leader] = [Follower] });
        State<bool>(service, "_inFight")[Leader] = true;
        State<bool>(service, "_inFight")[Follower] = true;
        State<long>(service, "_fightId")[Leader] = 500;
        State<long>(service, "_fightId")[Follower] = 999;
        Assert.True(service.TeamFollowersJoinPending(Leader));
        Assert.True(service.TeamCombatWaitPending(Leader));
        State<long>(service, "_fightId")[Follower] = 500;
        Assert.False(service.TeamFollowersJoinPending(Leader));
    }

    [Fact]
    public void Finished_slot_script_is_started_once_until_the_next_occurrence()
    {
        Assert.Equal(PlanRunAction.Start, PlanningEval.DecideRun(null, "slot@20261024", false, null,
            false, false, () => "-- action unique"));
        for (int tick = 0; tick < 20; tick++)
            Assert.Equal(PlanRunAction.None, PlanningEval.DecideRun("slot@20261024", "slot@20261024", false,
                null, false, false, () => throw new InvalidOperationException(), started: true));
        Assert.Equal(PlanRunAction.Start, PlanningEval.DecideRun("slot@20261024", "slot@20261025", false,
            null, false, false, () => "-- action unique", started: true));
        // Une plage sans script n'a encore rien démarré : l'assignation tardive fonctionne.
        Assert.Equal(PlanRunAction.Start, PlanningEval.DecideRun("slot@20261024", "slot@20261024", false,
            null, false, false, () => "-- action unique", started: false));
    }

    [Fact]
    public void A_known_account_binding_cannot_be_transferred_to_a_namesake()
    {
        var cp = new CharPlanning { AccountId = "compte-A" };
        Assert.True(PlanningEval.CanUseAccount(cp, "compte-A"));
        Assert.False(PlanningEval.CanUseAccount(cp, "compte-B"));
        Assert.False(PlanningEval.CanUseAccount(cp, ""));
        Assert.Equal("compte-A", cp.AccountId);
        Assert.True(PlanningEval.CanUseAccount(new CharPlanning(), "compte-A"));
    }

    [Fact]
    public void Caller_mutation_cannot_remove_a_published_follower()
    {
        var service = OfflineService();
        uint[] followers = [Follower];
        var snapshot = new Dictionary<uint, uint[]> { [Leader] = followers };
        service.SetTeamCombatWait(snapshot);
        service.SetTeamMapChangeWait(snapshot);
        followers[0] = 999; snapshot.Clear();
        Assert.True(service.TeamCombatWaitPending(Leader));
        Assert.True(service.TeamMapChangeWaitPending(Leader));
    }

    private static TeamService JoiningService()
    {
        var service = OfflineService();
        State<long>(service, "_charId")[Leader] = 1001;
        State<long>(service, "_charId")[Follower] = 1002;
        State<(long, CharacterInfo)>(service, "_selfInfo")[Leader] = (1001, new("Chef", 50, null, null));
        State<(long, CharacterInfo)>(service, "_selfInfo")[Follower] = (1002, new("Suiveur", 50, null, null));
        State<(int, string)>(service, "_serverInfo")[Leader] = (10, "Serveur");
        State<(int, string)>(service, "_serverInfo")[Follower] = (10, "Serveur");
        State<long>(service, "_fightId")[Leader] = 500;
        State<bool>(service, "_inFight")[Leader] = true;
        var view = new TeamService.TeamView(Leader, "Chef", new[] {
            new TeamService.TeamMemberInfo(Leader, "Chef", true), new TeamService.TeamMemberInfo(Follower, "Suiveur", false) });
        service.SetTeamSnapshot(new Dictionary<uint, TeamService.TeamView> { [Leader] = view, [Follower] = view });
        service.SetTeamCombatWait(new Dictionary<uint, uint[]> { [Leader] = [Follower] });
        var partyType = typeof(TeamService).GetNestedType("PartyState", BindingFlags.NonPublic)!;
        var party = Activator.CreateInstance(partyType, nonPublic: true)!;
        partyType.GetField("PartyId")!.SetValue(party, 700L);
        var members = (IDictionary)partyType.GetField("Members")!.GetValue(party)!;
        members[1001L] = "Chef"; members[1002L] = "Suiveur";
        ((IDictionary)typeof(TeamService).GetField("_party", Private)!.GetValue(service)!)[Follower] = party;
        return service;
    }

    private static bool MayJoin(TeamService service, long fight = 500)
        => (bool)typeof(TeamService).GetMethod("CanJoinTeamFight", Private)!.Invoke(service, [Follower, fight, 1001L])!;

    [Theory]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("wrong-fight")]
    [InlineData("recycled-pid")]
    [InlineData("wrong-server")]
    [InlineData("offline")]
    public void A_pending_join_is_refused_after_its_target_changes(string change)
    {
        var service = JoiningService();
        Assert.True(MayJoin(service));
        switch (change)
        {
            case "removed": ((IDictionary)typeof(TeamService).GetField("_party", Private)!.GetValue(service)!).Clear(); break;
            case "disabled": service.SetTeamCombatWait(new Dictionary<uint, uint[]>()); break;
            case "wrong-fight": State<long>(service, "_fightId")[Leader] = 999; break;
            case "recycled-pid": State<(long, CharacterInfo)>(service, "_selfInfo")[Follower] = (1002, new("Autre", 50, null, null)); break;
            case "wrong-server": State<(int, string)>(service, "_serverInfo")[Follower] = (11, "Autre"); break;
            case "offline": State<long>(service, "_lastMap").TryRemove(Follower, out _); break;
        }
        Assert.False(MayJoin(service));
        // Refus avant même de résoudre un transport ou une licence : zéro injection possible.
        Assert.False(service.FightJoin(Follower, 500, 1001));
    }

    [Fact]
    public void Group_event_can_precede_the_fighters_start_event()
    {
        var service = JoiningService();
        State<long>(service, "_fightId").TryRemove(Leader, out _);
        Assert.True(MayJoin(service));
    }

    [Fact]
    public void Supervisor_stop_guard_cancels_resumes_even_without_a_running_host()
    {
        var vm = (BotsVm)RuntimeHelpers.GetUninitializedObject(typeof(BotsVm));
        foreach (var name in new[] { "_byPid", "_luaHosts", "_planningPaused", "_linkLostResume", "_socketResume", "_scriptReconnects" })
        {
            var field = typeof(BotsVm).GetField(name, Private)!;
            field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        }
        HashSet<int> Set(string field) => (HashSet<int>)typeof(BotsVm).GetField(field, Private)!.GetValue(vm)!;
        Set("_linkLostResume").Add(102); Set("_socketResume").Add(102);
        var reconnectType = typeof(BotsVm).GetNestedType("ScriptReconnect", BindingFlags.NonPublic)!;
        var reconnect = Activator.CreateInstance(reconnectType, nonPublic: true)!;
        reconnectType.GetField("AccountId")!.SetValue(reconnect, "fixture-hors-ligne");
        reconnectType.GetField("InPlace")!.SetValue(reconnect, true);
        reconnectType.GetField("ScriptBody")!.SetValue(reconnect, "-- ancien trajet");
        ((IList)typeof(BotsVm).GetField("_scriptReconnects", Private)!.GetValue(vm)!).Add(reconnect);
        Assert.Empty((IDictionary)typeof(BotsVm).GetField("_luaHosts", Private)!.GetValue(vm)!);
        typeof(BotsVm).GetMethod("HoldSupervisorResume", Private)!.Invoke(vm, [102, "fixture-hors-ligne"]);
        Assert.Contains(102, Set("_planningPaused"));
        Assert.Empty(Set("_linkLostResume")); Assert.Empty(Set("_socketResume"));
        Assert.Null(reconnectType.GetField("ScriptBody")!.GetValue(reconnect));
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("generation")]
    [InlineData("socket-session")]
    [InlineData("character")]
    [InlineData("account")]
    public void Disconnect_countdown_cannot_target_a_replacement_session(string change)
    {
        var day = new DateTime(2026, 10, 24);
        var session = new PlanningSession("compte-A", 101, 2, 3, "Test");
        var cp = new CharPlanning { Enabled = true, AutoConnect = true, AccountId = "compte-A",
            Slots = { new PlanningSlot { Days = 32, StartMin = 600, EndMin = 660 } } };
        KeyValuePair<string, CharPlanning>[] plans = [new("Test", cp)];
        Assert.True(PlanningEval.CanDisconnectSession(session, session, plans, day.AddHours(12)));
        var current = change switch
        {
            "pid" => session with { Pid = 202 },
            "generation" => session with { LaunchGeneration = 4 },
            "socket-session" => session with { SocketSession = 5 },
            "character" => session with { Character = "Autre" },
            _ => session with { AccountId = "compte-B" },
        };
        Assert.False(PlanningEval.CanDisconnectSession(session, current, plans, day.AddHours(12)));
    }

    [Fact]
    public void Disconnect_rechecks_the_clock_even_before_the_next_scheduler_tick()
    {
        var day = new DateTime(2026, 10, 24);
        var session = new PlanningSession("compte-A", 101, 2, 3, "Test");
        var cp = new CharPlanning { Enabled = true, AutoConnect = true, AccountId = "compte-A",
            Slots = { new PlanningSlot { Days = 32, StartMin = 600, EndMin = 660 } } };
        KeyValuePair<string, CharPlanning>[] plans = [new("Test", cp)];
        Assert.True(PlanningEval.CanDisconnectSession(session, session, plans, day.AddHours(9).AddMinutes(59).AddSeconds(59)));
        Assert.False(PlanningEval.CanDisconnectSession(session, session, plans, day.AddHours(10)));
        cp.Enabled = false;
        Assert.False(PlanningEval.CanDisconnectSession(session, session, plans, day.AddHours(12)));
        cp.Enabled = true;
        cp.Slots.Clear();
        Assert.False(PlanningEval.CanDisconnectSession(session, session, plans, day.AddHours(12)));
    }

    private sealed class ObservedSnapshot(IReadOnlyDictionary<uint, uint[]> source, Action observe)
        : IReadOnlyDictionary<uint, uint[]>
    {
        public uint[] this[uint key] => source[key];
        public IEnumerable<uint> Keys => source.Keys;
        public IEnumerable<uint[]> Values => source.Values;
        public int Count => source.Count;
        public bool ContainsKey(uint key) => source.ContainsKey(key);
        public bool TryGetValue(uint key, out uint[] value) => source.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<uint, uint[]>> GetEnumerator()
        {
            observe();
            foreach (var entry in source) yield return entry;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
