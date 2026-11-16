extern alias F06App;

using System.Reflection;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;
using Xunit;
using AppEco = F06App::GivreClone.App.Services.Eco;
using ForgeMode = F06App::GivreClone.App.Services.ForgeAdvice.ForgeMode;
using SmithConditionBudget = F06App::GivreClone.App.ViewModels.SmithConditionBudget;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;

namespace GivreClone.Tests;

public class ForgeConditionStepsTests
{
    private static Dictionary<string, int> S(params (string Key, int Value)[] values)
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in values) stats[key] = value;
        return stats;
    }

    [Fact]
    public void Ordered_rows_use_the_first_match_then_skip()
    {
        var rules = new[]
        {
            new ForgeCondition("Fo", 61, 999, ForgeAction.Use("Rune Ini")),
            new ForgeCondition("Fo", 0, 20, ForgeAction.Use("Rune Fo")),
            new ForgeCondition("Fo", 21, 47, ForgeAction.Use("Rune Pa Fo")),
            new ForgeCondition("Fo", 48, 57, ForgeAction.Use("Rune Ra Fo")),
        };
        Assert.Equal("Rune Ra Fo", ForgeEngine.DecideConditionSteps(S(("Fo", 54)), null, rules).Action.Rune);
        Assert.Equal("Rune Ini", ForgeEngine.DecideConditionSteps(S(("Fo", 64)), null, rules).Action.Rune);
        Assert.Equal("Rune Fo", ForgeEngine.DecideConditionSteps(S(("Fo", 20)), null, rules).Action.Rune);
        var missed = ForgeEngine.DecideConditionSteps(S(("Fo", 59)), null, rules);
        Assert.False(missed.IsHalt);
        Assert.Equal(ForgeActionKind.Skip, missed.Action.Kind);
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditions(S(("Fo", 59)), null, rules).Kind);
    }

    [Fact]
    public void Stat_and_sink_ranges_combine_with_and_or()
    {
        var and = new ForgeCondition("Ré Per Eau", 0, 4, ForgeAction.Use("Rune Ré Per Eau"), 6, 12, ForgeCombine.And);
        var or = and with { Combine = ForgeCombine.Or };
        var statOnly = new ForgeCondition("Ré Per Eau", 0, 4, ForgeAction.Use("Rune Ré Per Eau"));
        var pitOnly = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Ré Per Eau"), 6, 12);
        var always = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Ré Per Eau"));

        Assert.Equal("Rune Ré Per Eau", Act(and, S(("Ré Per Eau", 3)), 8));
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(("Ré Per Eau", 3)), 20, new[] { and }).Action.Kind);
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(("Ré Per Eau", 9)), 8, new[] { and }).Action.Kind);
        Assert.Equal("Rune Ré Per Eau", Act(or, S(("Ré Per Eau", 3)), 20));
        Assert.Equal("Rune Ré Per Eau", Act(or, S(("Ré Per Eau", 9)), 8));
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(("Ré Per Eau", 9)), 20, new[] { or }).Action.Kind);
        Assert.Equal("Rune Ré Per Eau", Act(statOnly, S(("Ré Per Eau", 4)), null));
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(("Ré Per Eau", 5)), null, new[] { statOnly }).Action.Kind);
        Assert.Equal("Rune Ré Per Eau", Act(pitOnly, S(), 6));
        Assert.Equal("Rune Ré Per Eau", Act(pitOnly, S(), 12));
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(), 5, new[] { pitOnly }).Action.Kind);
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(), 13, new[] { pitOnly }).Action.Kind);
        Assert.Equal("Rune Ré Per Eau", Act(always, S(), null));
        Assert.True(ForgeEngine.Matches(and, S(("Ré Per Eau", 3)), 8));
        Assert.False(ForgeEngine.Matches(and, S(("Ré Per Eau", 3)), 20));
        Assert.True(ForgeEngine.Matches(or, S(("Ré Per Eau", 9)), 8));
        Assert.False(ForgeEngine.Matches(or, S(("Ré Per Eau", 9)), 20));
    }

    [Fact]
    public void Open_sink_bound_stays_open_and_known_zero_is_not_absence()
    {
        var atLeast = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Ré Per Eau"), 6, null);
        Assert.Equal("Rune Ré Per Eau", Act(atLeast, S(), 6));
        Assert.Equal("Rune Ré Per Eau", Act(atLeast, S(), 100));
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(), 5, new[] { atLeast }).Action.Kind);
        var lost = new ForgeCondition("Ga Pa", 0, 0, ForgeAction.Finish);
        Assert.Equal(ForgeActionKind.Finish, ForgeEngine.DecideConditionSteps(S(("Ga Pa", 0)), null, new[] { lost }).Action.Kind);
        Assert.True(ForgeEngine.Matches(lost, S(("Ga Pa", 0)), null));
        Assert.False(ForgeEngine.Matches(lost, S(), null));
    }

    [Fact]
    public void Unknown_stat_or_sink_never_matches_and_stops_before_later_rows()
    {
        var stat = new ForgeCondition("Fo", 0, 20, ForgeAction.Use("Rune Fo"));
        var later = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Vi"));
        var unknown = ForgeEngine.DecideConditionSteps(S(), null, new[] { stat, later });
        Assert.True(unknown.IsHalt);
        Assert.Equal("Étape 1 : caractéristique inconnue « Fo ». Arrêt, aucune rune passée.", unknown.Halt);
        Assert.NotEqual("Rune Vi", unknown.Action.Rune);
        var wrapped = ForgeEngine.DecideConditions(S(), null, new[] { stat, later });
        Assert.Equal(ForgeActionKind.Skip, wrapped.Kind);
        Assert.Equal("", wrapped.Rune);
        Assert.False(ForgeEngine.Matches(stat, S(), null));
        Assert.True(ForgeEngine.Matches(stat, S(("Fo", 0)), null));

        var pit = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Ré Per Eau"), 6, 12);
        var noPit = ForgeEngine.DecideConditionSteps(S(("Ré Per Eau", 3)), null, new[] { pit, later });
        Assert.Equal("Étape 1 : puits inconnu. Arrêt, aucune rune passée.", noPit.Halt);

        var both = new ForgeCondition("Fo", 0, 20, ForgeAction.Use("Rune Fo"), 6, 12, ForgeCombine.Or);
        Assert.Equal("Étape 1 : caractéristique inconnue « Fo » et puits inconnu. Arrêt, aucune rune passée.",
            ForgeEngine.DecideConditionSteps(S(), null, new[] { both }).Halt);

        var second = ForgeEngine.DecideConditionSteps(S(("Fo", 30)), null, new[]
        {
            new ForgeCondition("Fo", 0, 10, ForgeAction.Use("Rune Fo")),
            new ForgeCondition("Vi", 0, 20, ForgeAction.Use("Rune Vi")),
        });
        Assert.Equal("Étape 2 : caractéristique inconnue « Vi ». Arrêt, aucune rune passée.", second.Halt);
    }

    [Fact]
    public void A_proven_miss_does_not_stop_when_the_other_measure_is_unknown()
    {
        var and = new ForgeCondition("Fo", 0, 4, ForgeAction.Use("Rune Fo"), 6, 12, ForgeCombine.And);
        var later = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Vi"));
        var statOut = ForgeEngine.DecideConditionSteps(S(("Fo", 9)), null, new[] { and, later });
        Assert.False(statOut.IsHalt);
        Assert.Equal("Rune Vi", statOut.Action.Rune);
        var pitOut = ForgeEngine.DecideConditionSteps(S(), 20, new[] { and, later });
        Assert.False(pitOut.IsHalt);
        Assert.Equal("Rune Vi", pitOut.Action.Rune);
        var inconclusive = ForgeEngine.DecideConditionSteps(S(("Fo", 3)), null, new[] { and, later });
        Assert.Equal("Étape 1 : puits inconnu. Arrêt, aucune rune passée.", inconclusive.Halt);
    }

    [Fact]
    public void Or_uses_a_known_side_and_stops_when_only_the_unknown_side_could_match()
    {
        var or = new ForgeCondition("Fo", 0, 4, ForgeAction.Use("Rune Fo"), 6, 12, ForgeCombine.Or);
        Assert.Equal("Rune Fo", Act(or, S(("Fo", 2)), null));
        Assert.Equal("Rune Fo", Act(or, S(), 8));
        Assert.Equal("Étape 1 : puits inconnu. Arrêt, aucune rune passée.", ForgeEngine.DecideConditionSteps(S(("Fo", 9)), null, new[] { or }).Halt);
        Assert.Equal("Étape 1 : caractéristique inconnue « Fo ». Arrêt, aucune rune passée.", ForgeEngine.DecideConditionSteps(S(), 20, new[] { or }).Halt);
    }

    [Fact]
    public void Skip_and_finish_stop_the_list_without_reading_the_next_row()
    {
        var later = new ForgeCondition(null, 0, 0, ForgeAction.Use("Rune Vi"));
        var skip = ForgeEngine.DecideConditionSteps(S(("Fo", 5)), null, new[] { new ForgeCondition("Fo", 0, 10, ForgeAction.Skip), later });
        Assert.False(skip.IsHalt);
        Assert.Equal(ForgeActionKind.Skip, skip.Action.Kind);
        var finish = ForgeEngine.DecideConditionSteps(S(("Ga Pa", 0)), null, new[] { new ForgeCondition("Ga Pa", 0, 0, ForgeAction.Finish), later });
        Assert.Equal(ForgeActionKind.Finish, finish.Action.Kind);
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditionSteps(S(), null, []).Action.Kind);
    }

    [Fact]
    public void Lookup_uses_the_dictionary_comparer_and_trims_the_key()
    {
        var rule = new ForgeCondition(" fo ", 10, 20, ForgeAction.Use("Rune Fo"));
        var sensitive = ForgeEngine.DecideConditionSteps(S(("Fo", 12)), null, new[] { rule });
        Assert.Equal("Étape 1 : caractéristique inconnue « fo ». Arrêt, aucune rune passée.", sensitive.Halt);
        var ignore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Fo"] = 12 };
        Assert.Equal("Rune Fo", ForgeEngine.DecideConditionSteps(ignore, null, new[] { rule }).Action.Rune);
        Assert.Equal("Rune Fo", ForgeEngine.DecideConditionSteps(S(("fo", 12)), null, new[] { rule }).Action.Rune);
    }

    private static string Act(ForgeCondition rule, IReadOnlyDictionary<string, int> stats, int? pit)
    {
        var outcome = ForgeEngine.DecideConditionSteps(stats, pit, new[] { rule });
        Assert.False(outcome.IsHalt);
        return outcome.Action.Rune;
    }
}

public class ForgeConditionSessionTests
{
    private static SmithMagicViewModel Model()
    {
        var vm = new SmithMagicViewModel();
        StopHistory(vm);
        Set(vm, "_loadedUid", 42L);
        return vm;
    }

    private static void StopHistory(SmithMagicViewModel vm)
    {
        object? timer = typeof(SmithMagicViewModel).GetField("_forgeHistoryTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);
        timer?.GetType().GetMethod("Stop")!.Invoke(timer, null);
    }

    private static void Set(SmithMagicViewModel vm, string field, object value)
        => typeof(SmithMagicViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    private static void Load(SmithMagicViewModel vm)
        => typeof(SmithMagicViewModel).GetMethod("LoadConditionSteps", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);

    private static void AddRuneRow(SmithMagicViewModel vm, bool pit, string link)
    {
        vm.ConditionStat = vm.ConditionStatChoices.First(choice => choice.Code == "Fo");
        vm.ConditionMin = 48;
        vm.ConditionMax = 57;
        vm.ConditionUsePit = pit;
        vm.ConditionPitMin = 6;
        vm.ConditionPitMax = 12;
        vm.ConditionLink = vm.ConditionLinkChoices.First(choice => choice.Code == link);
        vm.ConditionAction = vm.ConditionActionChoices.First(choice => choice.Code == "rune");
        vm.ConditionRuneFamily = vm.ConditionRuneChoices.First(choice => choice.Code == "Fo");
        vm.ConditionRuneTier = vm.ConditionTierChoices.First(choice => choice.Code == "Ra");
        vm.AddConditionStepCommand.Execute(null);
    }

    [Fact]
    public void No_rows_leave_the_target_planner_in_place()
    {
        var vm = Model();
        Assert.Null(vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 0 }, null));
        Assert.False(vm.HasConditionSteps);
    }

    [Fact]
    public void Added_rows_keep_order_and_map_onto_the_engine()
    {
        var vm = Model();
        AddRuneRow(vm, pit: true, link: "or");
        Assert.Equal("1. Force entre 48 et 57 ou puits entre 6 et 12 → Rune Ra Fo", vm.ConditionSteps[0].Text);
        var matched = vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 50 }, null);
        Assert.False(matched!.Value.IsHalt);
        Assert.Equal("Rune Ra Fo", matched.Value.Action.Rune);
        var unknown = vm.PreviewConditionDecision(new Dictionary<string, int>(), null);
        Assert.Equal("Étape 1 : caractéristique inconnue « Fo » et puits inconnu. Arrêt, aucune rune passée.", unknown!.Value.Halt);

        vm.ConditionStat = vm.ConditionStatChoices.First(choice => choice.Code == "Fo");
        vm.ConditionMin = 0;
        vm.ConditionMax = 100;
        vm.ConditionUsePit = false;
        vm.ConditionAction = vm.ConditionActionChoices.First(choice => choice.Code == "finish");
        vm.AddConditionStepCommand.Execute(null);
        Assert.Equal(AppEco.ForgeActionKind.Rune, vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 50 }, 0)!.Value.Action.Kind);
        vm.MoveConditionStepUpCommand.Execute(vm.ConditionSteps[1]);
        Assert.Equal(AppEco.ForgeActionKind.Finish, vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 50 }, 0)!.Value.Action.Kind);
        Assert.StartsWith("1.", vm.ConditionSteps[0].Text);
        var first = vm.ConditionSteps[0];
        vm.MoveConditionStepUpCommand.Execute(first);
        Assert.Same(first, vm.ConditionSteps[0]);
        vm.RemoveConditionStepCommand.Execute(null);
        vm.RemoveConditionStepCommand.Execute(vm.ConditionSteps[0]);
        Assert.Equal(AppEco.ForgeActionKind.Rune, vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 50 }, 0)!.Value.Action.Kind);
    }

    [Fact]
    public void Inverted_ranges_and_a_missing_item_are_refused()
    {
        var vm = Model();
        vm.ConditionStat = vm.ConditionStatChoices.First(choice => choice.Code == "Fo");
        vm.ConditionMin = 10;
        vm.ConditionMax = 1;
        vm.AddConditionStepCommand.Execute(null);
        Assert.Empty(vm.ConditionSteps);
        Assert.Equal("L'intervalle de caractéristique est inversé.", vm.Status);
        vm.ConditionStat = vm.ConditionStatChoices[0];
        vm.ConditionUsePit = true;
        vm.ConditionPitMin = 8;
        vm.ConditionPitMax = 1;
        vm.AddConditionStepCommand.Execute(null);
        Assert.Empty(vm.ConditionSteps);
        Assert.Equal("L'intervalle de puits est inversé.", vm.Status);
        Set(vm, "_loadedUid", 0L);
        vm.ConditionUsePit = false;
        vm.AddConditionStepCommand.Execute(null);
        Assert.Empty(vm.ConditionSteps);
        Assert.Equal("Charge d'abord un objet de la file.", vm.Status);
    }

    [Fact]
    public void Rows_stay_on_their_item_and_a_character_change_clears_them()
    {
        var vm = Model();
        AddRuneRow(vm, pit: false, link: "and");
        Set(vm, "_loadedUid", 11L);
        Load(vm);
        Assert.Empty(vm.ConditionSteps);
        Set(vm, "_loadedUid", 42L);
        Load(vm);
        Assert.Single(vm.ConditionSteps);
        Set(vm, "_pid", 7);
        vm.AttachPid(0);
        Assert.Empty(vm.ConditionSteps);
        Assert.Null(vm.PreviewConditionDecision(new Dictionary<string, int> { ["Fo"] = 50 }, null));
    }

    [Fact]
    public async Task Advice_start_does_not_run_condition_steps()
    {
        var vm = Model();
        AddRuneRow(vm, pit: false, link: "and");
        Set(vm, "_pid", 7);
        Assert.True(vm.SetForgeMode(ForgeMode.Advice));
        Assert.True(vm.CanStart);
        int historyCalls = 0;
        vm.ForgeHistoryReady = () => { historyCalls++; return Task.CompletedTask; };
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, historyCalls);
        Assert.False(vm.Running);
        Assert.Contains("Conseil", vm.Status);
        Assert.Single(vm.ConditionSteps);
    }

    [Fact]
    public void Named_rune_budget_matches_the_session_caps_and_ignores_an_absent_rune()
    {
        var names = new Dictionary<long, (int EffectId, string Tier, string Name)> { [10] = (118, "Ra", "Rune Ra Fo") };
        var limits = new SmithMagicLimits(2, 1, 30, true);
        var stock = new List<SmithRune> { new(10, 118, SmithRuneTier.Ra, 3, 20) };
        var lines = new Dictionary<int, int>();
        var first = SmithConditionBudget.Account(AppEco.ForgeAction.Use("Rune Ra Fo"), stock, names, limits, 0, 0, lines);
        Assert.Null(first.Halt);
        Assert.Equal(1, first.AddedRunes);
        Assert.Equal(20L, first.AddedKamas);
        Assert.Equal(2, stock[0].Quantity);
        var second = SmithConditionBudget.Account(AppEco.ForgeAction.Use("Ra Fo"), stock, names, limits, 1, 20, lines);
        Assert.Equal("Limite de runes atteinte.", second.Halt);
        Assert.Equal(0, second.AddedRunes);
        Assert.Equal(2, stock[0].Quantity);

        lines.Clear();
        stock[0] = stock[0] with { Quantity = 3, UnitPrice = 20 };
        var kamas = SmithConditionBudget.Account(AppEco.ForgeAction.Use("Rune Ra Fo"), stock, names, new SmithMagicLimits(10, 10, 30, true), 0, 20, lines);
        Assert.Equal("Budget de runes atteint.", kamas.Halt);
        Assert.Equal(3, stock[0].Quantity);

        stock[0] = stock[0] with { UnitPrice = -1 };
        var price = SmithConditionBudget.Account(AppEco.ForgeAction.Use("Rune Ra Fo"), stock, names, new SmithMagicLimits(10, 10, 100, true), 0, 0, lines);
        Assert.Equal("Prix de la rune inconnu. Arrêt, aucune rune passée.", price.Halt);
        Assert.Equal(3, stock[0].Quantity);

        var absent = SmithConditionBudget.Account(AppEco.ForgeAction.Use("Rune Vi"), stock, names, limits, 0, 0, lines);
        Assert.Null(absent.Halt);
        Assert.Equal(0, absent.AddedRunes);
        var skip = SmithConditionBudget.Account(AppEco.ForgeAction.Skip, stock, names, limits, 0, 0, lines);
        Assert.Null(skip.Halt);
        Assert.Equal(0, skip.AddedRunes);
        Assert.Null(SmithConditionBudget.Account(AppEco.ForgeAction.Use("Rune Ra Fo"), stock, null, limits, 0, 0, lines).Halt);
    }

    [Fact]
    public void Only_an_absent_bag_rune_is_a_buy_candidate()
    {
        var names = new Dictionary<long, (int EffectId, string Tier, string Name)> { [10] = (118, "Ra", "Rune Ra Fo") };
        Assert.Equal(10L, SmithConditionBudget.AbsentRuneGid("rune absente du sac : Rune Ra Fo", names));
        Assert.Equal(10L, SmithConditionBudget.AbsentRuneGid("rune absente du sac : Ra Fo", names));
        Assert.Null(SmithConditionBudget.AbsentRuneGid("rune inconnue : Rune Ra Fo", names));
        Assert.Null(SmithConditionBudget.AbsentRuneGid("étape sautée", names));
        Assert.Null(SmithConditionBudget.AbsentRuneGid("rune absente du sac : Rune Vi", names));
    }

    [Fact]
    public void Start_wires_condition_steps_without_replacing_goals_or_advice()
    {
        string root = MappingScan.RepoRoot();
        string session = File.ReadAllText(Path.Combine(root, "GivreClone.App", "ViewModels", "SmithMagicViewModel.cs"));
        string view = File.ReadAllText(Path.Combine(root, "GivreClone.App", "Views", "SmithMagicView.axaml"));
        string advice = File.ReadAllText(Path.Combine(root, "GivreClone.App", "ViewModels", "SmithMagicViewModel.Advice.cs"));
        string leveling = File.ReadAllText(Path.Combine(root, "GivreClone.App", "ViewModels", "SmithMagicViewModel.GoalsSession.cs"));
        Assert.Contains("ForgeEngine.DecideConditionSteps", session, StringComparison.Ordinal);
        Assert.Contains("if (conditional)", session, StringComparison.Ordinal);
        Assert.Contains("budget.ReserveNext", session, StringComparison.Ordinal);
        Assert.Contains("aucun objectif à atteindre", session, StringComparison.Ordinal);
        Assert.Contains("if (IsAdviceMode) { if (CanStart) StartAdvice(); return; }", session, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ConditionSteps}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddConditionStepCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StartCommand}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("DecideConditionSteps", advice, StringComparison.Ordinal);
        Assert.DoesNotContain("DecideConditionSteps", leveling, StringComparison.Ordinal);
    }
}
