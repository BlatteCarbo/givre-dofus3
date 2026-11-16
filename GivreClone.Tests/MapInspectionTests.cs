using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class MapInspectionTests
{
    private static MapActor Mob(long id, int cell, string name = "Piou", int lvl = 40) =>
        new(cell, 0, id, name, MapActorKind.Monster, new[] { new MonsterEntry(1, name, 2) }, new[] { new MonsterMember(1, lvl), new MonsterMember(1, lvl + 4) });
    private static MapActor Npc(long id, int cell, string name = "Marchand") => new(cell, 0, id, name, MapActorKind.Npc, NpcId: 7);
    private static MapActor Player(long id, int cell, string name) => new(cell, 0, id, name, MapActorKind.Player);
    private static MapInteractive Res(int el, int cell, bool enabled = true) =>
        new(cell, el, 3, 0, new[] { new InteractiveSkill(1, 10, 0, enabled) }, "Frêne", "Couper");

    [Fact]
    public void Build_classifies_entities_and_never_invents_levels()
    {
        var all = MapInspection.Build(new[] { Mob(-1, 100), Npc(-2, 101), Player(5, 102, "Moi"), new MapActor(103, 0, -9, "Inconnu", MapActorKind.Monster) },
            new[] { Res(4, 104) }, "Moi");
        Assert.Equal(new[] { MapEntityKind.Monster, MapEntityKind.Npc, MapEntityKind.Self, MapEntityKind.Monster, MapEntityKind.Resource }, all.Select(e => e.Kind));
        Assert.Equal("40–44", MapInspection.LevelText(all[0]));
        Assert.Equal("", MapInspection.LevelText(all[3]));   // niveau inconnu -> rien d'affiché
    }

    [Fact]
    public void Search_ignores_case_and_accents_and_matches_group_members()
    {
        var e = MapInspection.Build(new[] { Mob(-1, 1, "Pious des champs") }, null, null)[0];
        Assert.True(MapInspection.Matches(e, "pioùs"));
        Assert.True(MapInspection.Matches(e, "-1"));
        Assert.False(MapInspection.Matches(e, "dragon"));
        var r = MapInspection.Build(null, new[] { Res(4, 9) }, null)[0];
        Assert.True(MapInspection.Matches(r, "frene"));
    }

    [Fact]
    public void Selection_survives_updates_by_key_but_not_a_map_change_or_disappearance()
    {
        var vm = new MapInspectionModel();
        vm.Update(new[] { Mob(-1, 100), Npc(-2, 200) }, null, "Moi", 1);
        Assert.True(vm.InspectCell(100));
        Assert.Equal("a:-1", vm.Selected!.Key);

        vm.Update(new[] { Mob(-1, 120), Npc(-2, 200) }, null, "Moi", 1);   // le groupe a bougé : même entité
        Assert.Equal("a:-1", vm.Selected!.Key);
        Assert.Equal(120, vm.Selected!.CellId);

        vm.Update(new[] { Npc(-2, 200) }, null, "Moi", 1);                  // il a disparu
        Assert.Null(vm.Selected);

        Assert.True(vm.InspectCell(200));
        vm.Update(new[] { Npc(-2, 200) }, null, "Moi", 2);                  // nouvelle carte
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void Overlapping_entities_open_a_selector_and_empty_cells_fall_back_to_movement()
    {
        var vm = new MapInspectionModel();
        vm.Update(new[] { Mob(-1, 100), Npc(-2, 100), Player(5, 50, "Moi") }, new[] { Res(4, 100) }, "Moi", 1);
        Assert.False(vm.InspectCell(7));          // vide -> déplacement
        Assert.False(vm.InspectCell(50));         // notre perso seul -> rien à inspecter
        Assert.True(vm.InspectCell(100));
        Assert.Equal(3, vm.Candidates.Count);
        vm.Select(vm.Candidates[2]);
        Assert.Equal(MapEntityKind.Resource, vm.Selected!.Kind);
        vm.Clear();
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void Filters_hide_entities_from_the_map_lists_but_never_our_character()
    {
        var vm = new MapInspectionModel();
        vm.Update(new[] { Mob(-1, 1), Npc(-2, 2), Player(5, 3, "Moi"), Player(6, 4, "Autre") }, new[] { Res(4, 5) }, "Moi", 1);
        vm.Filter = MapEntityFilter.Npcs; vm.Rebuild();
        Assert.Equal(new long[] { -2, 5 }, vm.VisibleActors!.Select(a => a.ActorId).OrderBy(x => x));   // PNJ + nous
        Assert.Empty(vm.VisibleInteractives!);
        Assert.Equal(1, vm.All.Count(e => e.Kind == MapEntityKind.Monster));   // la légende compte toujours toute la carte
    }

    [Fact]
    public void Hidden_selection_is_invalidated_and_frequent_updates_keep_it_stable()
    {
        var vm = new MapInspectionModel();
        var mob = Mob(-1, 10);
        vm.Update(new[] { mob }, null, "Moi", 1);
        vm.InspectCell(10);
        for (int i = 0; i < 200; i++) vm.Update(new[] { mob }, null, "Moi", 1);
        Assert.Equal("a:-1", vm.Selected!.Key);
        vm.Filter = MapEntityFilter.Npcs; vm.Rebuild();
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void Keyboard_cycle_wraps_and_skips_our_character()
    {
        var vm = new MapInspectionModel();
        vm.Update(new[] { Mob(-1, 30), Npc(-2, 10), Player(5, 20, "Moi") }, null, "Moi", 1);
        vm.Cycle(1); Assert.Equal("a:-2", vm.Selected!.Key);   // cellule 10
        vm.Cycle(1); Assert.Equal("a:-1", vm.Selected!.Key);   // cellule 30 (le perso, cellule 20, est ignoré)
        vm.Cycle(1); Assert.Equal("a:-2", vm.Selected!.Key);   // retour au début
        vm.Cycle(-1); Assert.Equal("a:-1", vm.Selected!.Key);
    }

    [Fact]
    public void Freshness_formats_seconds_minutes_hours()
    {
        var t = new System.DateTime(2026, 10, 18, 12, 0, 0, System.DateTimeKind.Utc);
        Assert.Equal("2 s", MapInspection.Freshness(t, t.AddSeconds(2)));
        Assert.Equal("3 min", MapInspection.Freshness(t, t.AddMinutes(3)));
        Assert.Equal("", MapInspection.Freshness(null, t));
    }

    [Fact]
    public void Group_xp_counts_only_members_with_known_level_and_never_guesses()
    {
        var xp = new Dictionary<int, IReadOnlyDictionary<int, long>?> { [1] = new Dictionary<int, long> { [40] = 1000, [44] = 1200 }, [2] = null };
        System.Func<int, IReadOnlyDictionary<int, long>?> look = g => xp.GetValueOrDefault(g);
        var full = new MapActor(5, 0, -1, "G", MapActorKind.Monster, new[] { new MonsterEntry(1, "A", 2) }, new[] { new MonsterMember(1, 40), new MonsterMember(1, 44) });
        var g = GroupXp.Compute(full, look);
        Assert.True(g.Complete); Assert.Equal(2200, g.Known);

        var partial = new MapActor(5, 0, -2, "G", MapActorKind.Monster, new[] { new MonsterEntry(1, "A", 2), new MonsterEntry(2, "B", 1) },
            new[] { new MonsterMember(1, 40), new MonsterMember(1, 0), new MonsterMember(2, 30) });   // niveau inconnu + base absente
        var p = GroupXp.Compute(partial, look);
        Assert.False(p.Complete); Assert.Equal(1000, p.Known); Assert.Equal(1, p.KnownMembers); Assert.Equal(3, p.TotalMembers);

        var none = new MapActor(5, 0, -3, "G", MapActorKind.Monster, new[] { new MonsterEntry(1, "A", 2) });   // aucun niveau reçu
        Assert.False(GroupXp.Compute(none, look).Any);
        Assert.False(GroupXp.Compute(full, _ => null).Any);   // base du jeu absente
    }

    [Fact]
    public void Attack_needs_a_preview_then_a_confirmation_on_an_unchanged_target()
    {
        var m = new MapInspectionModel();
        m.Update(new[] { Mob(-1, 100) }, null, "Moi", 1);
        Assert.True(m.InspectCell(100));
        Assert.Null(m.Attack.Confirm(m.Selected));          // pas d'aperçu -> jamais d'envoi
        m.Attack.Begin(m.Selected!);
        Assert.NotNull(m.Attack.Confirm(m.Selected));       // aperçu inchangé -> accepté
        Assert.False(m.Attack.Pending);                     // et la confirmation est consommée (un seul envoi)

        m.Attack.Begin(m.Selected!);
        m.Update(new[] { Mob(-1, 130) }, null, "Moi", 1);   // le groupe a bougé
        Assert.False(m.Attack.Pending); Assert.True(m.AttackExpired);

        m.AttackExpired = false;
        m.Attack.Begin(m.Selected!);
        m.Update(new[] { Mob(-1, 130, "Piou", 50) }, null, "Moi", 1);   // composition modifiée (niveaux)
        Assert.False(m.Attack.Pending); Assert.True(m.AttackExpired);

        m.AttackExpired = false;
        m.Attack.Begin(m.Selected!);
        m.Update(new[] { Mob(-1, 130, "Piou", 50) }, null, "Moi", 2);   // changement de carte / de personnage
        Assert.False(m.Attack.Pending); Assert.Null(m.Selected);
    }

    [Fact]
    public void Attack_preview_does_not_apply_to_non_monsters_or_survive_a_new_target()
    {
        var m = new MapInspectionModel();
        m.Update(new[] { Mob(-1, 100), Npc(-2, 101) }, null, "Moi", 1);
        m.InspectCell(101);
        m.Attack.Begin(m.Selected!);
        Assert.False(m.Attack.Pending);                      // un PNJ ne se combat pas
        m.InspectCell(100); m.Attack.Begin(m.Selected!);
        m.InspectCell(101);                                  // autre cible
        Assert.False(m.Attack.Pending);
    }

    [Fact]
    public void Dialog_log_keeps_only_a_short_history_without_duplicates_or_blanks()
    {
        var log = new NpcDialogLog();
        log.Add(DialogSpeaker.Npc, "Bonjour"); log.Add(DialogSpeaker.Npc, "Bonjour"); log.Add(DialogSpeaker.Npc, "  ");
        Assert.Single(log.Items);
        for (int i = 0; i < 20; i++) log.Add(i % 2 == 0 ? DialogSpeaker.Npc : DialogSpeaker.You, "ligne " + i);
        Assert.Equal(NpcDialogLog.Max, log.Items.Count);
        Assert.Equal("ligne 19", log.Items[^1].Text);
        log.Clear(); Assert.Empty(log.Items);
    }

    [Fact]
    public void Controlled_character_and_other_players_are_distinguished_and_never_given_invented_data()
    {
        var all = MapInspection.Build(new[] { Player(5, 1, "Moi"), Player(6, 2, "Autre") }, null, "Moi");
        var self = all.Single(e => e.Key == "a:5"); var other = all.Single(e => e.Key == "a:6");
        Assert.True(CharacterInspection.IsControlled(self)); Assert.False(CharacterInspection.IsOtherPlayer(self));
        Assert.True(CharacterInspection.IsOtherPlayer(other));
        Assert.True(CharacterInspection.CanShowStats(self)); Assert.False(CharacterInspection.CanShowStats(other));
        Assert.False(CharacterInspection.HasEquipment(self));   // aucun équipement exploité : onglet absent
    }

    [Fact]
    public void Action_feedback_confirms_only_on_proof_and_otherwise_ends_unknown_or_refused()
    {
        var t = new MapActionTracker();
        t.Begin("move", 120, 0);
        Assert.Equal(ActionState.Waiting, t.State);
        t.Poll(100, 1000); Assert.Equal(ActionState.Waiting, t.State);          // pas encore arrivé
        t.Poll(120, 1500); Assert.Equal(ActionState.Confirmed, t.State);        // preuve : cellule atteinte

        t.Begin("use", -1, 0, 8000);
        t.Poll(5, 7999); Assert.Equal(ActionState.Waiting, t.State);
        t.Poll(5, 8000); Assert.Equal(ActionState.Unknown, t.State);            // aucune preuve -> inconnu, jamais « réussi »

        t.Begin("fight", -1, 0, 8000, () => true);
        t.Poll(null, 10); Assert.Equal(ActionState.Confirmed, t.State);         // preuve externe

        t.Begin("x", 5, 0, 8000, () => throw new System.InvalidOperationException());
        t.Poll(null, 10); Assert.Equal(ActionState.Waiting, t.State);           // sonde en erreur : pas de confirmation

        t.Refuse("no path"); Assert.Equal(ActionState.Refused, t.State);
        t.Poll(5, 99999); Assert.Equal(ActionState.Refused, t.State);           // un refus reste un refus
        t.Clear(); Assert.Equal(ActionState.None, t.State);
    }

    [Fact]
    public void Path_preview_is_empty_for_blocked_or_identical_cells_and_walks_a_corridor()
    {
        Assert.Empty(MapPathPreview.Compute(10, 10, _ => true));
        Assert.Empty(MapPathPreview.Compute(10, 20, _ => false));
        Assert.Empty(MapPathPreview.Compute(-1, 20, _ => true));
        var p = MapPathPreview.Compute(0, 3, _ => true);
        Assert.True(p.Count >= 2); Assert.Equal(0, p[0]); Assert.Equal(3, p[^1]);
        Assert.Empty(MapPathPreview.Compute(0, 3, c => c != 3));                 // destination non marchable
    }

    [Fact]
    public void Favorites_toggle_per_map_and_cell_without_duplicates()
    {
        var list = new List<MapFavorite>();
        list = MapFavorites.Toggle(list, 100, 5, "Banque", "npc");
        list = MapFavorites.Toggle(list, 100, 5, "Banque", "npc");           // 2e bascule = retrait
        Assert.Empty(list);
        list = MapFavorites.Toggle(list, 100, 5, "Banque", "npc");
        list = MapFavorites.Toggle(list, 200, 5, "Autre", "npc");            // même cellule, autre carte
        Assert.Equal(2, list.Count); Assert.True(MapFavorites.IsFavorite(list, 100, 5)); Assert.False(MapFavorites.IsFavorite(list, 100, 6));
        Assert.Equal(2, MapFavorites.Toggle(list, 0, 9, "x", "npc").Count);   // carte inconnue : rien d'enregistré
        var many = new List<MapFavorite>(); for (int i = 0; i < 100; i++) many = MapFavorites.Toggle(many, 1, i, "f" + i, "npc", 60);
        Assert.Equal(60, many.Count);
    }

    [Fact]
    public void Craft_plan_reports_missing_surplus_and_max_craftable_and_decomposes_to_base_items()
    {
        var plan = StationCraftPlan.Compute(new[] { (1L, "Frêne", 4), (2L, "Eau", 2) }, g => g == 1 ? 9 : 3);
        Assert.Equal(1, plan.MaxCraftable);                      // min(9/4, 3/2)
        Assert.True(plan.CanCraft(1)); Assert.False(plan.CanCraft(2)); Assert.False(plan.CanCraft(0));
        Assert.Equal(0, StationCraftPlan.Compute(new[] { (1L, "A", 1) }, _ => 0).MaxCraftable);
        Assert.Equal(0, StationCraftPlan.Compute(System.Array.Empty<(long, string, int)>(), _ => 5).MaxCraftable);

        // planche (10) = 2 bois (20) ; bois = non craftable
        var flat = CraftDecomposition.Flatten(new[] { (10L, "Planche", 3), (30L, "Clou", 1) },
            g => g == 10 ? new[] { (20L, "Bois", 2) } : null);
        Assert.Equal(new[] { ("Bois", 6), ("Clou", 1) }, flat.Select(x => (x.Name, x.Qty)));
        // boucle de recettes : coupée, pas de récursion infinie
        var loop = CraftDecomposition.Flatten(new[] { (1L, "A", 1) }, g => g == 1 ? new[] { (2L, "B", 1) } : new[] { (1L, "A", 1) });
        Assert.NotEmpty(loop);
    }

    [Fact]
    public void Inspection_stays_fast_with_thousands_of_entities_across_several_bots()
    {
        var actors = Enumerable.Range(0, 3000).Select(i => i % 3 == 0 ? Mob(-i - 1, i % 560) : i % 3 == 1 ? Npc(-i - 1, i % 560) : Player(i + 10, i % 560, "J" + i)).ToList();
        var its = Enumerable.Range(0, 800).Select(i => Res(i + 1, i % 560)).ToList();
        var bots = Enumerable.Range(0, 6).Select(_ => new MapInspectionModel()).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int round = 0; round < 20; round++)            // mises à jour FRÉQUENTES de 6 bots
            foreach (var b in bots) b.Update(actors, its, "J1", 1);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 12000, $"120 mises à jour de 3800 entités : {sw.ElapsedMilliseconds} ms");
        var one = bots[0]; one.Search = "J12";
        sw.Restart(); for (int i = 0; i < 50; i++) one.Rebuild(); sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 8000, $"50 recherches : {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Rapid_map_and_account_changes_never_keep_a_stale_selection_or_pending_attack()
    {
        var m = new MapInspectionModel();
        for (int map = 1; map <= 50; map++)
        {
            m.Update(new[] { Mob(-1, 10), Npc(-2, 20) }, null, "Moi", map);
            Assert.True(m.InspectCell(10)); m.Attack.Begin(m.Selected!);
            m.Update(new[] { Mob(-1, 10) }, null, "Moi", map + 1);   // changement de carte immédiat
            Assert.Null(m.Selected); Assert.False(m.Attack.Pending);
        }
        Assert.True(new MapInspectionModel().SelectSelf() == false);   // aucun perso -> accès rapide refuse proprement
        var withSelf = new MapInspectionModel(); withSelf.Update(new[] { Player(5, 7, "Moi") }, null, "Moi", 1);
        Assert.True(withSelf.SelectSelf()); Assert.Equal(MapEntityKind.Self, withSelf.Selected!.Kind);
        withSelf.Clear(); Assert.Null(withSelf.Selected);                // fermeture du panneau
    }

    [Fact]
    public void Harvest_all_picks_the_nearest_available_untried_target_and_stops_safely()
    {
        var its = new[] { Res(1, 100), Res(2, 101), Res(3, 300, enabled: false), Res(4, 500) };
        var group = MapInspection.Build(null, its, null);
        var tried = new HashSet<string>();
        var first = HarvestAllPlan.NextTarget(group, 99, tried);
        Assert.Equal("i:1", first!.Key);                       // la plus proche de la cellule 99
        tried.Add(first.Key);
        Assert.Equal("i:2", HarvestAllPlan.NextTarget(group, 99, tried)!.Key);
        tried.Add("i:2"); tried.Add("i:4");
        Assert.Null(HarvestAllPlan.NextTarget(group, 99, tried));   // l'épuisée (3) n'est jamais visée
        Assert.Equal("i:1", HarvestAllPlan.NextTarget(group, null, new HashSet<string>())!.Key);   // position inconnue : ordre stable

        Assert.Equal(HarvestStop.None, HarvestAllPlan.Check(false, false, false, false, false));
        Assert.Equal(HarvestStop.Cancelled, HarvestAllPlan.Check(true, true, true, true, true));
        Assert.Equal(HarvestStop.MapChanged, HarvestAllPlan.Check(false, true, true, true, true));
        Assert.Equal(HarvestStop.InFight, HarvestAllPlan.Check(false, true, true, false, true));
        Assert.Equal(HarvestStop.PodsFull, HarvestAllPlan.Check(false, false, true, false, true));
        Assert.Equal(HarvestStop.InteractionOpen, HarvestAllPlan.Check(false, false, false, false, true));
    }
}
