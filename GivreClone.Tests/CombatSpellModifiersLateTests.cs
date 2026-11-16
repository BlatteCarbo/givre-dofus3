extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Engine;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Names = F06App::GivreClone.App.Services.GameNameResolver;

namespace GivreClone.Tests;

/// <summary>
/// Synthetic packet-order cases; no client, relay, gameplay or installed data.
/// They exercise the private methods used by SendCast / WaitAfterCast and by
/// the decoded actor-only ApplySpellModifierEvent.
/// </summary>
public sealed class CombatSpellModifiersLateTests
{
    private const uint Pid = 0x40000147;
    private const long Actor = 123;
    private const int FirstBuff = 70001;  // Synthetic fixture IDs, not client spell IDs.
    private const int SecondBuff = 70002;
    private const int OrdinarySpell = 70003;
    private const int TargetSpell = 70004;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static object? Invoke(Launch service, string method, params object[] arguments) =>
        typeof(Launch).GetMethod(method, Hidden)!.Invoke(service, arguments);

    private static Launch Fixture()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        Field("_engineControlled").SetValue(service,
            Activator.CreateInstance(Field("_engineControlled").FieldType));
        Map(service, "_charId")[Pid] = Actor;
        // SelfActorId only accepts the authenticated character ID when the
        // server fight roster also contains that fighter. Without this
        // witness every actor-only ApplySpellModifierEvent is correctly
        // rejected, so the test must model a real in-fight identity.
        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (0, Actor) };
        Map(service, "_fightTurnNo")[Pid] = 1;
        Map(service, "_fightStartAt")[Pid] = 111L;
        Map(service, "_serverTurnActor")[Pid] = Actor;
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int>
        {
            [FirstBuff] = 1, [SecondBuff] = 1, [OrdinarySpell] = 1
        };

        var names = new Names();
        typeof(Names).GetField("_spellModifiers", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, List<SpellModifierEffect>>>
            {
                [FirstBuff] = new()
                {
                    [1] = new()
                    {
                        new(SpellModifierActions.MaxRangeUp, TargetSpell, 3, 1)
                    }
                },
                [SecondBuff] = new()
                {
                    [1] = new()
                    {
                        new(SpellModifierActions.MaxRangeUp, TargetSpell, 7, 1)
                    }
                }
            });
        Field("_gameNames").SetValue(service, names);
        return service;
    }

    private static void Note(Launch service, int spell) =>
        Invoke(service, "NoteCastForModifiers", Pid, spell);
    private static bool Commit(Launch service, int turn = 1) =>
        (bool)Invoke(service, "CommitCastModifiers", Pid, turn)!;
    private static void Event(Launch service, long actor = Actor) =>
        Invoke(service, "OnSpellModifierEvent", Pid, actor);
    private static SpellModifierSet Modifiers(Launch service) =>
        (SpellModifierSet)Invoke(service, "SpellModsOf", Pid, Actor)!;
    private static int ExtraRange(Launch service)
    {
        var updated = Modifiers(service).Adjust(TargetSpell,
            new SpellCastStats(2, 1, 4, false, false, true, false));
        return updated.Range - 4;
    }

    [Fact]
    public void Wait_without_actor_event_does_not_consume_pending_or_invent_modifier()
    {
        var service = Fixture();
        Note(service, FirstBuff);

        Assert.False(Commit(service));
        Assert.False(Commit(service));
        Assert.Equal(0, ExtraRange(service));

        // Delayed packet arrives only after both wait checks.
        Event(service);
        Assert.Equal(3, ExtraRange(service));
    }

    [Fact]
    public void Event_before_wait_commits_once_and_duplicate_event_does_not_stack()
    {
        var service = Fixture();
        Note(service, FirstBuff);

        Event(service);
        int version = Modifiers(service).Version;
        Assert.True(version > 0);
        Assert.False(Commit(service));
        Event(service);
        Assert.Equal(version, Modifiers(service).Version);
        Assert.Equal(3, ExtraRange(service));
    }

    [Fact]
    public void Ordinary_cast_between_timeout_and_late_event_keeps_only_modifier_candidate()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Assert.False(Commit(service));

        Note(service, OrdinarySpell);
        Event(service);

        Assert.Equal(3, ExtraRange(service));
    }

    [Fact]
    public void Two_unconfirmed_modifier_casts_from_same_actor_are_ambiguous_and_fail_closed()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Assert.False(Commit(service));
        Note(service, SecondBuff);

        // The next actor-only event might belong to either cast.
        Event(service);
        Assert.Equal(0, ExtraRange(service));
        Assert.False(Commit(service));

        // Even a third note and packet cannot recover the unknown association.
        Note(service, SecondBuff);
        Event(service);
        Assert.Equal(0, ExtraRange(service));
    }

    [Fact]
    public void Wrong_actor_event_cannot_confirm_own_pending_cast()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Event(service, actor: 456);
        Assert.False(Commit(service));
        Assert.Equal(0, ExtraRange(service));

        Event(service);
        Assert.Equal(3, ExtraRange(service));
    }

    [Fact]
    public void Late_packet_after_turn_transition_is_not_retroactively_applied()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Assert.False(Commit(service));
        Map(service, "_fightTurnNo")[Pid] = 2;

        Event(service);
        Assert.Equal(0, ExtraRange(service));

        // Without a cast-level event ID, more events from turn 1 might arrive.
        // The actor remains quarantined, not silently re-armed on the new turn.
        Note(service, SecondBuff);
        Event(service);
        Assert.Equal(0, ExtraRange(service));
    }

    [Fact]
    public void Packet_during_another_fighters_turn_does_not_confirm_own_cast()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Map(service, "_serverTurnActor")[Pid] = 456L;

        Event(service);
        Assert.Equal(0, ExtraRange(service));
    }

    [Fact]
    public void New_fight_clears_ambiguous_quarantine_and_discards_old_events()
    {
        var service = Fixture();
        Note(service, FirstBuff);
        Note(service, SecondBuff);
        Event(service);
        Assert.Equal(0, ExtraRange(service));

        Invoke(service, "ForgetSpellModifiers", Pid);
        Map(service, "_fightStartAt")[Pid] = 222L;
        Note(service, SecondBuff);
        Assert.False(Commit(service));
        Event(service);
        Assert.Equal(7, ExtraRange(service));
    }
}
