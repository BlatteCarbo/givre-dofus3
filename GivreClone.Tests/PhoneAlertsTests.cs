using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.PhoneAlerts;
using Xunit;

namespace GivreClone.Tests;

[Collection("PhoneAlertHub")]   // le hub est statique : ces tests ne tournent pas en parallèle
public class PhoneAlertHubTests
{
    private static long _now;
    public PhoneAlertHubTests() { PhoneAlertHub.Reset(); _now = 0; PhoneAlertHub.NowMs = () => _now; }

    [Fact]
    public void Events_are_numbered_and_listed_after_a_given_id()
    {
        var a = PhoneAlertHub.Raise("death", "Shadowbot", "mort");
        var b = PhoneAlertHub.Raise("pods", "Shadowbot", "pods pleins");
        Assert.Equal(new long[] { 1, 2 }, new[] { a!.Id, b!.Id });
        Assert.Equal(new[] { "pods" }, PhoneAlertHub.Since(1).Select(e => e.Kind).ToArray());
        Assert.Equal(2, PhoneAlertHub.LatestId);
        Assert.Empty(PhoneAlertHub.Since(2));
    }

    [Fact]
    public void The_same_event_is_not_repeated_before_the_cooldown()
    {
        Assert.NotNull(PhoneAlertHub.Raise("pods", "Shadowbot", "x"));
        _now = 60_000;
        Assert.Null(PhoneAlertHub.Raise("pods", "Shadowbot", "x"));                         // moins de 5 min
        Assert.NotNull(PhoneAlertHub.Raise("pods", "Autre", "x"));                       // un autre bot n'est pas gêné
        Assert.NotNull(PhoneAlertHub.Raise("pods", "Shadowbot", "x", key: "autre-clé"));     // clé plus fine
        _now = 6 * 60_000;
        Assert.NotNull(PhoneAlertHub.Raise("pods", "Shadowbot", "x"));
    }

    [Fact]
    public void Only_the_latest_200_events_are_kept()
    {
        for (int i = 0; i < 250; i++) PhoneAlertHub.Raise("k", "b", "t" + i, key: "k" + i);
        Assert.Equal(200, PhoneAlertHub.Since(0, 500).Count);
        Assert.Equal("t249", PhoneAlertHub.Since(0, 500).Last().Text);
    }

    [Fact]
    public async Task WaitAsync_returns_as_soon_as_an_event_arrives_and_empty_on_timeout()
    {
        var empty = await PhoneAlertHub.WaitAsync(0, TimeSpan.FromMilliseconds(80), CancellationToken.None);
        Assert.Empty(empty);
        var wait = PhoneAlertHub.WaitAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        PhoneAlertHub.Raise("arch", "Shadowbot", "archimonstre");
        var got = await wait;
        Assert.Single(got);
        Assert.Equal("arch", got[0].Kind);
    }

    [Fact]
    public void A_failing_subscriber_does_not_break_raising()
    {
        void Boom(PhoneEvent _) => throw new InvalidOperationException("abonné cassé");
        PhoneAlertHub.Raised += Boom;
        try { Assert.NotNull(PhoneAlertHub.Raise("x", "y", "z")); }
        finally { PhoneAlertHub.Raised -= Boom; }
    }
}

public class ArchAlertTrackerTests
{
    private static readonly Func<int, bool> Notable = id => id is 500 or 501;
    private static SeenGroup G(long actor, params (int, string)[] m) => new(actor, m);

    [Fact]
    public void An_archmonster_group_is_reported_once_and_ordinary_groups_never()
    {
        var t = new ArchAlertTracker(Notable);
        var groups = new[] { G(1, (10, "Bouftou")), G(2, (500, "Arch Bouftou"), (10, "Bouftou")) };
        var first = t.Observe(7, 100, groups, 0);
        Assert.Single(first);
        Assert.Equal(("Arch Bouftou", 2L), (first[0].Names, first[0].ActorId));
        Assert.Empty(t.Observe(7, 100, groups, 1000));                        // même groupe : déjà signalé
    }

    [Fact]
    public void A_new_map_resets_the_group_memory_but_the_cooldown_still_applies_to_the_same_monster_on_the_same_map()
    {
        var t = new ArchAlertTracker(Notable);
        var g = new[] { G(2, (500, "Arch Bouftou")) };
        Assert.Single(t.Observe(7, 100, g, 0));
        Assert.Empty(t.Observe(7, 200, new[] { G(3, (10, "Bouftou")) }, 1000));   // on quitte la carte
        Assert.Empty(t.Observe(7, 100, g, 2000));                                  // retour : même monstre, même carte, dans les 15 min
        Assert.Empty(t.Observe(7, 200, Array.Empty<SeenGroup>(), 3000));
        Assert.Single(t.Observe(7, 100, g, (long)ArchAlertTracker.Cooldown.TotalMilliseconds + 10_000));   // plus tard : de nouveau signalé
    }

    [Fact]
    public void Each_bot_is_tracked_separately_and_forget_clears_it()
    {
        var t = new ArchAlertTracker(Notable);
        var g = new[] { G(2, (501, "Arch Piou")) };
        Assert.Single(t.Observe(1, 100, g, 0));
        Assert.Single(t.Observe(2, 100, g, 0));                                    // un autre bot sur la même carte
        t.Forget(1);
        Assert.Empty(t.Observe(1, 100, g, 10));                                    // le délai par monstre reste en mémoire
    }
}
