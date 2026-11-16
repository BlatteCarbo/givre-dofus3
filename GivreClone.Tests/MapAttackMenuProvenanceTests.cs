extern alias F06App;

using System;
using GivreClone.Protocol;
using Xunit;
using Origin = F06App::GivreClone.App.Controls.MapAttackMenuOrigin;
using Request = F06App::GivreClone.App.Controls.MapAttackMenuRequest;

namespace GivreClone.Tests;

/// <summary>Pure offline tests; never create an Avalonia window, bot, socket or client.</summary>
public sealed class MapAttackMenuProvenanceTests
{
    private static MapActor Monster(long id = -301, int cell = 125) =>
        new(cell, 0, id, "fixture-group", MapActorKind.Monster);

    private static Origin Capture(
        object? bot, IReadOnlyList<MapActor> visible, IReadOnlyList<MapActor>? displayed = null,
        int pid = 1001, long map = 1888, int epoch = 7, long selection = 13,
        long worldGeneration = 31, int? displayedEpoch = null)
        => Assert.IsType<Origin>(Origin.Capture(
            bot, pid, map, epoch, selection, worldGeneration,
            visible, displayed ?? visible, displayedEpoch ?? epoch));

    private static Origin Recapture(
        Origin previous, object? bot = null, IReadOnlyList<MapActor>? visible = null,
        int? pid = null, long? map = null, int? epoch = null,
        long? selection = null, long? worldGeneration = null)
        => Capture(bot ?? previous.Bot, visible ?? previous.VisibleActors,
            pid: pid ?? previous.Pid, map: map ?? previous.MapId,
            epoch: epoch ?? previous.MapEpoch,
            selection: selection ?? previous.SelectionGeneration,
            worldGeneration: worldGeneration ?? previous.WorldMapGeneration);

    [Fact]
    public void Exact_target_and_exact_bot_context_allow_menu_action()
    {
        var group = Monster();
        IReadOnlyList<MapActor> all = [group];
        var bot = new object();
        var origin = Capture(bot, all);
        var request = new Request(group, origin);
        Assert.Same(group, request.Target);
        Assert.Same(origin, request.Origin);
        Assert.True(origin.Allows(Recapture(origin), request.Target, all));
    }

    [Fact]
    public void Switching_to_another_bot_refuses_original_menu_even_when_pid_map_and_actor_match()
    {
        var target = Monster();
        IReadOnlyList<MapActor> visible = [target];
        var original = Capture(new object(), visible);
        Assert.False(original.Allows(Recapture(original, bot: new object()), target, visible));
    }

    [Fact]
    public void Switching_bot_A_to_B_and_back_to_A_revokes_menu_by_selection_generation()
    {
        var target = Monster();
        IReadOnlyList<MapActor> visible = [target];
        var botA = new object();
        var menuOnA = Capture(botA, visible, selection: 13);
        var reselectedA = Recapture(menuOnA, selection: 15);
        Assert.Same(botA, reselectedA.Bot);
        Assert.False(menuOnA.Allows(reselectedA, target, visible));
    }

    [Fact]
    public void Reconnect_pid_or_new_bot_world_generation_revokes_menu()
    {
        var target = Monster();
        IReadOnlyList<MapActor> actors = [target];
        var menu = Capture(new object(), actors);
        Assert.False(menu.Allows(Recapture(menu, pid: 1002), target, actors));
        Assert.False(menu.Allows(Recapture(menu, worldGeneration: 32), target, actors));
        Assert.False(menu.Allows(Recapture(menu, worldGeneration: 30), target, actors));
    }

    [Fact]
    public void Changed_map_id_or_map_epoch_revokes_menu_even_for_recycled_actor_id()
    {
        var target = Monster();
        IReadOnlyList<MapActor> actors = [target];
        var menu = Capture(new object(), actors);
        Assert.False(menu.Allows(Recapture(menu, map: 1889), target, actors));
        Assert.False(menu.Allows(Recapture(menu, epoch: 8), target, actors));
    }

    [Fact]
    public void Changed_displayed_actor_snapshot_revokes_menu_even_if_same_target_record_survives()
    {
        var target = Monster();
        IReadOnlyList<MapActor> oldActors = [target];
        IReadOnlyList<MapActor> newActors = [target];
        var menu = Capture(new object(), oldActors);
        Assert.False(menu.Allows(Recapture(menu, visible: newActors), target, newActors));
    }

    [Fact]
    public void Reused_contextual_id_same_cell_and_same_value_cannot_target_a_replacement()
    {
        var old = Monster();
        var successor = Monster(); // record-value equal, different observed instance
        IReadOnlyList<MapActor> visible = [old];
        var menu = Capture(new object(), visible);
        Assert.False(menu.Allows(Recapture(menu), old, [successor]));
        Assert.False(menu.Allows(Recapture(menu), successor, [successor]));
        Assert.False(menu.Allows(Recapture(menu), old, Array.Empty<MapActor>()));
    }

    [Fact]
    public void Moved_or_removed_monster_cannot_be_targeted_from_old_popup()
    {
        var target = Monster();
        IReadOnlyList<MapActor> visible = [target];
        var menu = Capture(new object(), visible);
        Assert.False(menu.Allows(Recapture(menu), target, [target with { CellId = 126 }]));
        Assert.False(menu.Allows(Recapture(menu), target, Array.Empty<MapActor>()));
    }

    [Fact]
    public void Duplicate_actor_contextual_ids_never_authorize_attack()
    {
        var target = Monster();
        IReadOnlyList<MapActor> visible = [target];
        var menu = Capture(new object(), visible);
        Assert.False(menu.Allows(Recapture(menu), target, [target, Monster()]));
        IReadOnlyList<MapActor> displayedDupes = [target, Monster()];
        var duplicateMenu = Capture(new object(), displayedDupes);
        Assert.False(duplicateMenu.Allows(Recapture(duplicateMenu), target, displayedDupes));
    }

    [Fact]
    public void No_menu_origin_when_ui_binding_is_still_showing_previous_bot()
    {
        var group = Monster();
        IReadOnlyList<MapActor> botAVisible = [group];
        IReadOnlyList<MapActor> botBVisible = [Monster(-302)];
        Assert.Null(Origin.Capture(new object(), 1002, 1888, 7, 14, 32,
            botBVisible, botAVisible, 7));
        Assert.Null(Origin.Capture(new object(), 1002, 1888, 7, 14, 32,
            botBVisible, botBVisible, 6));
    }

    [Fact]
    public void Missing_session_map_or_actor_evidence_never_creates_a_menu_origin()
    {
        var actor = Monster();
        IReadOnlyList<MapActor> list = [actor];
        var bot = new object();
        Assert.Null(Origin.Capture(null, 1001, 1888, 7, 13, 31, list, list, 7));
        Assert.Null(Origin.Capture(bot, 0, 1888, 7, 13, 31, list, list, 7));
        Assert.Null(Origin.Capture(bot, 1001, 0, 7, 13, 31, list, list, 7));
        Assert.Null(Origin.Capture(bot, 1001, 1888, 0, 13, 31, list, list, 0));
        Assert.Null(Origin.Capture(bot, 1001, 1888, 7, 0, 31, list, list, 7));
        Assert.Null(Origin.Capture(bot, 1001, 1888, 7, 13, 0, list, list, 7));
        Assert.Null(Origin.Capture(bot, 1001, 1888, 7, 13, 31, null, list, 7));
        Assert.Null(Origin.Capture(bot, 1001, 1888, 7, 13, 31, list, null, 7));
    }

    [Fact]
    public void Only_negative_monster_contextual_id_and_valid_cell_can_be_authorized()
    {
        var bot = new object();
        foreach (var bad in new[]
        {
            Monster(301), Monster(0), Monster(-301, -1), Monster(-301, 560),
            Monster() with { Kind = MapActorKind.Player },
            Monster() with { Kind = MapActorKind.Npc }
        })
        {
            IReadOnlyList<MapActor> all = [bad];
            var origin = Capture(bot, all);
            Assert.False(origin.Allows(Recapture(origin), bad, all));
        }
    }

    [Fact]
    public void Null_or_missing_action_target_never_authorizes_any_bot()
    {
        var target = Monster();
        IReadOnlyList<MapActor> all = [target];
        var menu = Capture(new object(), all);
        Assert.False(menu.Allows(null, target, all));
        Assert.False(menu.Allows(Recapture(menu), null, all));
        Assert.False(menu.Allows(Recapture(menu), target, null));
    }

    [Fact]
    public void Forged_zero_or_missing_origin_fields_are_rejected_even_if_current_values_match()
    {
        var target = Monster();
        IReadOnlyList<MapActor> all = [target];
        var valid = Capture(new object(), all);
        foreach (var invalid in new[]
        {
            valid with { Pid = 0 },
            valid with { MapId = 0 },
            valid with { MapEpoch = 0 },
            valid with { SelectionGeneration = 0 },
            valid with { WorldMapGeneration = 0 },
            valid with { VisibleActors = null! }
        })
            Assert.False(invalid.Allows(invalid, target, all));
    }
}
