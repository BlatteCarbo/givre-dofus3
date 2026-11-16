using System;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class ChallengeRewardPolicyTests
{
    private static readonly ChallengeContext Context = new(false, false, true, true);
    private static ChallengeRewardOffer Offer(int id, int xp, int drop) =>
        new(id, ChallengeRule.Focus, xp, drop);

    [Theory]
    [InlineData(false, 31)]
    [InlineData(true, 40)]
    public void Reward_preference_changes_the_selected_offer(bool loot, int expected)
    {
        var offers = new[] { Offer(31, 100, 10), Offer(40, 20, 200) };
        Assert.Equal(expected, ChallengeCatalog.PickPreferred(offers, _ => true, Context, loot));
    }

    [Fact]
    public void Expected_reward_still_accounts_for_rule_support()
    {
        var offers = new[] {
            new ChallengeRewardOffer(40, ChallengeRule.Prudent, 65, 0),
            new ChallengeRewardOffer(970, ChallengeRule.Pins, 90, 0) };
        Assert.Equal(40, ChallengeCatalog.PickPreferred(offers, _ => true, Context, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_expected_reward_uses_secondary_bonus(bool loot)
    {
        var offers = loot
            ? new[] { Offer(40, 20, 100), Offer(31, 30, 100) }
            : new[] { Offer(40, 100, 20), Offer(31, 100, 30) };
        Assert.Equal(31, ChallengeCatalog.PickPreferred(offers, _ => true, Context, loot));
    }

    [Fact]
    public void Missing_preferred_bonus_falls_back_to_secondary_bonus()
    {
        Assert.Equal(31, ChallengeCatalog.PickPreferred(
            new[] { Offer(40, 0, 20), Offer(31, 0, 50) }, _ => true, Context, false));
    }

    [Fact]
    public void Missing_all_metadata_preserves_server_order_instead_of_inventing_bonus()
    {
        var offers = new[] {
            new ChallengeRewardOffer(2, ChallengeRule.Statue, 0, 0),
            new ChallengeRewardOffer(6, ChallengeRule.Versatile, 0, 0) };
        Assert.Equal(2, ChallengeCatalog.PickPreferred(offers, _ => true, Context, false));
    }

    [Fact]
    public void Explicit_rejection_wins_even_for_highest_reward()
    {
        Assert.Equal(40, ChallengeCatalog.PickPreferred(
            new[] { Offer(31, 1000, 1000), Offer(40, 1, 1) }, o => o.Id != 31, Context, false));
        Assert.Null(ChallengeCatalog.PickPreferred(new[] { Offer(31, 100, 100) }, _ => false, Context, false));
        Assert.Null(ChallengeCatalog.PickPreferred(Array.Empty<ChallengeRewardOffer>(), _ => true, Context, false));
    }

    [Fact]
    public void Missing_metadata_considers_only_accepted_offers()
    {
        Assert.Equal(40, ChallengeCatalog.PickPreferred(
            new[] { Offer(31, 1000, 1000), Offer(40, 0, 0), Offer(6, 0, 0) },
            o => o.Id != 31, Context, false));
    }

    [Fact]
    public void Equal_scores_and_secondary_bonus_use_lowest_id()
    {
        Assert.Equal(31, ChallengeCatalog.PickPreferred(
            new[] { Offer(40, 100, 100), Offer(31, 100, 100) }, _ => true, Context, false));
    }

    // Nuit du 24/10 : Statue (a priori 80) raté 13 fois sur 14, choisi face à Raser les murs (65) et Prudent (75).
    [Fact]
    public void Observed_failures_override_an_optimistic_prior()
    {
        Assert.Equal(80, ChallengeCatalog.Blend(80, 0, 0));
        Assert.True(ChallengeCatalog.Blend(80, 1, 13) < 40);
        Assert.Equal(69, ChallengeCatalog.Blend(35, 9, 1));
        var offers = new[] {
            new ChallengeRewardOffer(2, ChallengeRule.Statue, 100, 100),
            new ChallengeRewardOffer(971, ChallengeRule.RaserLesMurs, 100, 100) };
        Assert.Equal(2, ChallengeCatalog.PickPreferred(offers, _ => true, Context, false));
        Assert.Equal(971, ChallengeCatalog.PickPreferred(offers, _ => true, Context, false,
            (rule, prior) => rule == ChallengeRule.Statue ? ChallengeCatalog.Blend(prior, 1, 13) : prior));
    }
}
