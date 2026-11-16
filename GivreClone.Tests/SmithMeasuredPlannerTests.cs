using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.Tests;

public sealed class SmithMeasuredPlannerTests
{
    [Fact]
    public void Zero_cash_headroom_still_allows_owned_runes_but_never_a_paid_sample()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 1), new(2, 20, 5, 0)];
        Assert.Equal(2, SmithMeasuredPlanner.Choose(choices, [], cashHeadroom: 0)!.ItemUid);
        Assert.Null(SmithMeasuredPlanner.Choose([choices[0]], [], cashHeadroom: 0));
    }

    [Fact]
    public void Joint_ranking_can_choose_another_support_and_rune_by_measured_cost_per_xp()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 100), new(1, 20, 5, 200), new(2, 20, 5, 50)];
        SmithXpObservation[] observations = [new(1, 10, 5, 10), new(1, 20, 5, 100), new(2, 20, 5, 50)];
        Assert.Equal(choices[2], SmithMeasuredPlanner.Choose(choices, observations, cashHeadroom: 200));
    }

    [Fact]
    public void Blank_batch_is_excluded_without_repeating_it_until_a_failure_streak()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 0), new(1, 20, 5, 0)];
        SmithXpObservation[] observations = [new(1, 10, 5, 0)];
        Assert.Equal(20, SmithMeasuredPlanner.Choose(choices, observations)!.RuneGid);
        Assert.Null(SmithMeasuredPlanner.Choose([choices[0]], observations));
    }

    [Fact]
    public void Measured_XP_changes_the_choice_then_fresh_zero_moves_away()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 100), new(2, 10, 5, 100)];
        var observations = new List<SmithXpObservation> { new(1, 10, 5, 50), new(2, 10, 5, 100) };

        Assert.Equal(2, SmithMeasuredPlanner.Choose(choices, observations)!.ItemUid);

        observations.Add(new(2, 10, 5, 0));
        Assert.Equal(1, SmithMeasuredPlanner.Choose(choices, observations)!.ItemUid);
    }

    [Fact]
    public void Unknown_XP_is_not_treated_as_zero_cost_productivity()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 0), new(2, 10, 5, 100)];
        SmithXpObservation[] observations = [new(1, 10, 5, null), new(2, 10, 5, 50)];

        Assert.Equal(2, SmithMeasuredPlanner.Choose(choices, observations)!.ItemUid);
    }

    [Fact]
    public void Existing_stock_does_not_get_charged_its_replacement_price()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 0), new(2, 10, 5, 50)];
        SmithXpObservation[] observations = [new(1, 10, 5, 10), new(2, 10, 5, 1000)];

        Assert.Equal(1, SmithMeasuredPlanner.Choose(choices, observations)!.ItemUid);
    }

    [Fact]
    public void Exploration_uses_unmeasured_stock_before_a_paid_sample()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 1, 100), new(2, 10, 1, 0)];

        Assert.Equal(2, SmithMeasuredPlanner.Choose(choices, [])!.ItemUid);
    }

    [Fact]
    public void Unknown_quotes_and_nonproductive_choices_cannot_trigger_spending()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, null), new(2, 10, 5, 100)];
        SmithXpObservation[] observations = [new(2, 10, 5, 0)];

        Assert.Null(SmithMeasuredPlanner.Choose(choices, observations));
    }

    [Fact]
    public void Equal_measured_efficiency_keeps_the_current_support()
    {
        SmithLevelingChoice[] choices = [new(1, 10, 5, 100), new(2, 10, 5, 100, CurrentSupport: true)];
        SmithXpObservation[] observations = [new(1, 10, 5, 50), new(2, 10, 5, 50)];

        Assert.Equal(2, SmithMeasuredPlanner.Choose(choices, observations)!.ItemUid);
    }
}
