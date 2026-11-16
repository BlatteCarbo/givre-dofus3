extern alias F06App;

using System.Collections.ObjectModel;
using FightChallengeItem = F06App::GivreClone.App.ViewModels.FightChallengeItem;
using FightChallengeUiState = F06App::GivreClone.App.ViewModels.FightChallengeUiState;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Régressions hors jeu du panneau de choix de défi. La liste moteur reste volontairement
/// disponible après le choix : le refresh UI ne doit pas la réafficher.
/// </summary>
public sealed class FightChallengeUiStateTests
{
    private static FightChallengeItem Offer(int id, int xp = 50, int drop = 30)
        => new(id, $"Défi {id}", $"+{xp}% XP · +{drop}% butin");

    [Fact]
    public void Successful_choice_is_hidden_during_request_and_stays_hidden_on_next_refresh()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        var serverOffers = new[] { Offer(7), Offer(9) };

        Assert.True(state.ObserveFight(true, 501));
        state.Synchronize(shown, serverOffers);
        Assert.Equal(2, shown.Count);

        Assert.True(state.TryBeginPick(shown[0], shown, out var generation));
        Assert.False(state.TryBeginPick(shown[1], shown, out _)); // double clic / autre choix concurrent
        state.Synchronize(shown, serverOffers); // tick pendant l'attente réseau
        Assert.Empty(shown);

        Assert.True(state.CompletePick(generation, terminal: true)); // ChallengePick.Ok
        state.Synchronize(shown, serverOffers); // PreFight expose toujours les mêmes propositions
        Assert.Empty(shown);
    }

    [Fact]
    public void Already_selected_is_terminal_but_send_failure_allows_retry()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        var serverOffers = new[] { Offer(5) };
        state.ObserveFight(true, 100);
        state.Synchronize(shown, serverOffers);

        Assert.True(state.TryBeginPick(shown[0], shown, out var first));
        Assert.True(state.CompletePick(first, terminal: false)); // ChallengePick.SendFailed
        state.Synchronize(shown, serverOffers);
        Assert.Single(shown);

        Assert.True(state.TryBeginPick(shown[0], shown, out var second));
        Assert.True(state.CompletePick(second, terminal: true)); // ChallengePick.Already
        state.Synchronize(shown, serverOffers);
        Assert.Empty(shown);
    }

    [Fact]
    public void Offers_with_the_same_count_update_when_identity_bonus_or_order_changes()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        state.ObserveFight(true, 100);
        var initial = new[] { Offer(3, 10), Offer(7, 20) };
        state.Synchronize(shown, initial);
        var firstRow = shown[0];

        state.Synchronize(shown, new[] { Offer(3, 10), Offer(7, 20) });
        Assert.Same(firstRow, shown[0]); // refresh identique : pas de remplacement/flicker

        state.Synchronize(shown, new[] { Offer(7, 20), Offer(3, 10) });
        Assert.Equal(new[] { 7, 3 }, shown.Select(x => x.Id));

        state.Synchronize(shown, new[] { Offer(7, 21), Offer(3, 10) });
        Assert.Contains("+21% XP", shown[0].Bonus);

        state.Synchronize(shown, new[] { Offer(8, 21), Offer(3, 10) });
        Assert.Equal(8, shown[0].Id);
    }

    [Fact]
    public void New_fight_discards_previous_selection_and_ignores_old_async_completion()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        var oldOffers = new[] { Offer(5) };
        state.ObserveFight(true, 81);
        state.Synchronize(shown, oldOffers);
        var stale = shown[0];
        Assert.True(state.TryBeginPick(stale, shown, out var oldGeneration));

        Assert.True(state.ObserveFight(true, 82)); // même PID ; pas de tick hors-combat entre les deux
        var newOffers = new[] { Offer(6) };
        state.Synchronize(shown, newOffers);
        Assert.Equal(6, Assert.Single(shown).Id);
        Assert.False(state.CompletePick(oldGeneration, terminal: true));
        Assert.False(state.TryBeginPick(stale, shown, out _)); // ancien bouton impossible à réutiliser
        Assert.True(state.TryBeginPick(shown[0], shown, out _));
    }

    [Fact]
    public void Unknown_fight_id_becoming_known_does_not_undo_a_confirmed_choice()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        var serverOffers = new[] { Offer(22) };
        state.ObserveFight(true, 0);
        state.Synchronize(shown, serverOffers);
        Assert.True(state.TryBeginPick(shown[0], shown, out var generation));
        Assert.True(state.CompletePick(generation, terminal: true));

        Assert.False(state.ObserveFight(true, 12345)); // premier identifiant connu, même combat
        state.Synchronize(shown, serverOffers);
        Assert.Empty(shown);

        Assert.True(state.ObserveFight(false, 0));
        Assert.True(state.ObserveFight(true, 12345)); // nouveau combat après sortie observée
        state.Synchronize(shown, serverOffers);
        Assert.Single(shown);
    }

    [Fact]
    public void No_fight_or_no_proposals_cannot_select_a_challenge()
    {
        var state = new FightChallengeUiState();
        var shown = new ObservableCollection<FightChallengeItem>();
        var offer = Offer(1);
        state.Synchronize(shown, new[] { offer });
        Assert.Empty(shown);
        Assert.False(state.TryBeginPick(offer, shown, out _));

        state.ObserveFight(true, 77);
        state.Synchronize(shown, new[] { offer });
        Assert.Single(shown);
        state.Synchronize(shown, null);
        Assert.Empty(shown);
        Assert.False(state.TryBeginPick(offer, shown, out _));
    }
}
