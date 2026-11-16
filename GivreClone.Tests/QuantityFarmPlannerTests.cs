extern alias F06App;

using Q = F06App::GivreClone.App.Services.QuantityFarm;
using Res = F06App::GivreClone.App.Services.Resources;
using Stock = F06App::GivreClone.App.Services.UnifiedStock;
using Xunit;

namespace GivreClone.Tests;

public class QuantityFarmPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Exact_resource_id_is_accepted_and_a_contains_name_is_refused()
    {
        var map = Woods();
        var exact = Q.QuantityFarmPlanner.ResolveResource(map, "303");
        Assert.True(exact.Accepted);
        Assert.Equal(303, exact.ObjectId);
        Assert.Equal(0, exact.MonsterId);

        var several = Q.QuantityFarmPlanner.ResolveResource(map, "frene");
        Assert.False(several.Accepted);
        Assert.Equal("Plusieurs ressources correspondent : identifiant exact requis.", several.Message);

        var uniqueName = Q.QuantityFarmPlanner.ResolveResource(map, "Frêne sombre");
        Assert.False(uniqueName.Accepted);
        Assert.Equal("Identifiant exact requis.", uniqueName.Message);

        Assert.Equal("Ressource inconnue.", Q.QuantityFarmPlanner.ResolveResource(map, "999").Message);
        var duplicated = new Res.ResourceMap(
            new[] { new Res.ResourceInfo(303, 2, 1, 0, "Frêne"), new Res.ResourceInfo(303, 2, 1, 0, "Frêne bis") },
            Array.Empty<Res.ResourceNode>());
        Assert.Equal("Plusieurs ressources portent cet identifiant.", Q.QuantityFarmPlanner.ResolveResource(duplicated, "303").Message);
    }

    [Fact]
    public void Drop_stays_on_the_requested_monster_when_another_shares_the_object()
    {
        var pick = Q.QuantityFarmPlanner.ResolveDrop(149, 885, new[] { (200, 885L), (149, 885L) });
        Assert.True(pick.Accepted);
        Assert.Equal(149, pick.MonsterId);
        Assert.Equal(885, pick.ObjectId);

        var decision = Q.QuantityFarmPlanner.Evaluate(
            Req(kind: Q.QuantityFarmKind.Monster, monsterId: 149, gid: 885, catalog: new[] { (200, 885L) }), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Refused, decision.Status);
        Assert.Equal("Ce drop n'est pas sur ce monstre.", decision.Message);
        Assert.Null(decision.Script);
    }

    [Fact]
    public void Bag_gate_ignores_the_equipped_stack()
    {
        var decision = Q.QuantityFarmPlanner.Evaluate(Req(bag: 4, equipped: 1), Boom);
        Assert.Equal(4, decision.Owned);
        Assert.Equal(6, decision.Remaining);
        Assert.Equal(Q.QuantityFarmStatus.NeedsBagOnly, decision.Status);
        Assert.NotNull(decision.Allocation);
        Assert.Equal(4, decision.Allocation!.FromInventory);
        Assert.Equal(6, decision.UnknownStorage);
    }

    [Fact]
    public void Bag_already_at_target_completes_without_building_a_script()
    {
        var decision = Q.QuantityFarmPlanner.Evaluate(Req(bag: 12, quantity: 10), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Completed, decision.Status);
        Assert.Equal(12, decision.Owned);
        Assert.Equal(0, decision.Remaining);
        Assert.Null(decision.Script);
        Assert.Contains("Aucun script lancé.", decision.Message);
    }

    [Fact]
    public void Stale_bank_is_unknown_until_the_owner_accepts_the_bag_only()
    {
        var observations = new[]
        {
            Obs(Stock.StockLocation.Inventory, "bag:10", 3, "uid-bag"),
            Obs(Stock.StockLocation.Bank, "bank:A", 50, "uid-bank", confidence: Stock.StockConfidence.Stale)
        };
        var held = Q.QuantityFarmPlanner.Evaluate(Req(bag: 3, observations: observations), Boom);
        Assert.Equal(Q.QuantityFarmStatus.NeedsBagOnly, held.Status);
        Assert.Equal(7, held.UnknownStorage);
        Assert.Equal(0, held.Allocation!.FromBank);
        Assert.Null(held.Script);

        int built = 0;
        var go = Q.QuantityFarmPlanner.Evaluate(Req(bag: 3, observations: observations, accept: true), spec =>
        {
            built++;
            return Q.QuantityFarmScript.Build(spec);
        });
        Assert.Equal(Q.QuantityFarmStatus.Ready, go.Status);
        Assert.Equal(1, built);
        Assert.Equal(0, go.Allocation!.FromBank);
        Assert.NotNull(go.Script);
        Assert.DoesNotContain("gatherAll", go.Script);
    }

    [Fact]
    public void Known_bag_bank_and_haven_can_start_without_the_bag_only_choice()
    {
        var observations = new[]
        {
            Obs(Stock.StockLocation.Inventory, "bag:10", 3, "uid-bag"),
            Obs(Stock.StockLocation.Bank, "bank:A", 0, "uid-bank"),
            Obs(Stock.StockLocation.HavenBag, "haven:10", 0, "uid-haven")
        };
        int built = 0;
        var decision = Q.QuantityFarmPlanner.Evaluate(Req(bag: 3, observations: observations), spec =>
        {
            built++;
            return Q.QuantityFarmScript.Build(spec);
        });
        Assert.Equal(0, decision.UnknownStorage);
        Assert.Equal(Q.QuantityFarmStatus.Ready, decision.Status);
        Assert.Equal(1, built);
        Assert.Equal(3, decision.Allocation!.FromInventory);
        Assert.Equal(0, decision.Allocation.FromBank);
    }

    [Fact]
    public void Same_stack_uid_in_bag_and_bank_counts_once()
    {
        var observations = new[]
        {
            Obs(Stock.StockLocation.Inventory, "bag:10", 7, "uid-123"),
            Obs(Stock.StockLocation.Bank, "bank:A", 7, "uid-123")
        };
        var decision = Q.QuantityFarmPlanner.Evaluate(Req(bag: 7, observations: observations), Boom);
        Assert.Equal(7, decision.Allocation!.FromInventory);
        Assert.Equal(0, decision.Allocation.FromBank);
        Assert.NotEqual(14, decision.Allocation.FromInventory + decision.Allocation.FromBank);
    }

    [Fact]
    public void Quantity_outside_one_to_one_billion_throws_and_does_not_build()
    {
        foreach (long quantity in new long[] { 0, -1, 1_000_000_001 })
        {
            var error = Assert.Throws<ArgumentException>(() => Q.QuantityFarmPlanner.Evaluate(Req(quantity: quantity), Boom));
            Assert.Equal("Invalid resource demand.", error.Message);
        }

        var edge = Q.QuantityFarmPlanner.Evaluate(Req(quantity: 1_000_000_000, bag: 1_000_000_000), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Completed, edge.Status);
        Assert.Null(edge.Script);
    }

    [Fact]
    public void Cancel_removes_only_the_run_token_on_that_pid()
    {
        Assert.Equal("_pendingHarvest", Q.QuantityFarmPlanner.PendingHarvest);
        Assert.Equal("_pendingAttack", Q.QuantityFarmPlanner.PendingAttack);
        Assert.Equal("_pendingMapChange", Q.QuantityFarmPlanner.PendingMapChange);
        using var runA = new CancellationTokenSource();
        using var runB = new CancellationTokenSource();
        using var manual = new CancellationTokenSource();
        var pending = new List<Q.QuantityFarmPending>();
        foreach (var slot in new[] { "_pendingHarvest", "_pendingAttack", "_pendingMapChange" })
        {
            pending.Add(new Q.QuantityFarmPending(1, slot, runA.Token));
            pending.Add(new Q.QuantityFarmPending(1, slot, runB.Token));
            pending.Add(new Q.QuantityFarmPending(2, slot, runA.Token));
            pending.Add(new Q.QuantityFarmPending(3, slot, manual.Token));
        }

        var kept = Q.QuantityFarmPlanner.Cancel(pending, 1, runA.Token);
        Assert.DoesNotContain(kept, item => item.Pid == 1 && item.Owner.Equals(runA.Token));
        Assert.Equal(3, kept.Count(item => item.Pid == 1 && item.Owner.Equals(runB.Token)));
        Assert.Equal(3, kept.Count(item => item.Pid == 2));
        Assert.Equal(3, kept.Count(item => item.Pid == 3 && item.Owner.Equals(manual.Token)));
        Assert.Equal(9, kept.Count);
    }

    [Fact]
    public void Empty_route_refuses_even_when_the_bag_only_choice_is_set()
    {
        var decision = Q.QuantityFarmPlanner.Evaluate(Req(maps: Array.Empty<long>(), accept: true), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Refused, decision.Status);
        Assert.Contains("carte", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(decision.Script);
    }

    [Fact]
    public void Missing_gather_or_fight_permission_refuses_without_a_script()
    {
        var resource = Q.QuantityFarmPlanner.Evaluate(Req(gather: false, accept: true), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Refused, resource.Status);
        Assert.Contains("Récolte", resource.Message);
        Assert.Null(resource.Script);

        var monster = Q.QuantityFarmPlanner.Evaluate(
            Req(kind: Q.QuantityFarmKind.Monster, monsterId: 149, gid: 885, fight: false, accept: true), Boom);
        Assert.Equal(Q.QuantityFarmStatus.Refused, monster.Status);
        Assert.Contains("Combat", monster.Message);
        Assert.Null(monster.Script);
    }

    private static string Boom(Q.QuantityFarmScriptSpec _) => throw new InvalidOperationException("builder");

    private static Res.ResourceMap Woods() => new(
        new[]
        {
            new Res.ResourceInfo(303, 2, 1, 0, "Frêne"),
            new Res.ResourceInfo(460, 2, 20, 0, "Frêne sombre")
        },
        Array.Empty<Res.ResourceNode>());

    private static Stock.StockObservation Obs(Stock.StockLocation location, string store, long quantity, string uid, long gid = 303,
        Stock.StockConfidence confidence = Stock.StockConfidence.Fresh)
        => new("account-A", 10, location, store, "obs-" + store, Now, "3.7.12.12", confidence, true,
            new[] { new Stock.StockStack(gid, uid, quantity) });

    private static Q.QuantityFarmRequest Req(
        long quantity = 10,
        long bag = 0,
        long equipped = 0,
        bool gather = true,
        bool fight = true,
        IReadOnlyList<long>? maps = null,
        IReadOnlyList<Stock.StockObservation>? observations = null,
        bool accept = false,
        Q.QuantityFarmKind kind = Q.QuantityFarmKind.Resource,
        int monsterId = 0,
        long gid = 303,
        IReadOnlyList<(int MonsterId, long ObjectId)>? catalog = null)
    {
        var stacks = new List<Q.QuantityFarmStack>();
        if (bag > 0) stacks.Add(new Q.QuantityFarmStack(gid, "uid-bag", bag, 63));
        if (equipped > 0) stacks.Add(new Q.QuantityFarmStack(gid, "uid-eq", equipped, 0));
        return new Q.QuantityFarmRequest
        {
            Kind = kind,
            ItemGid = gid,
            Quantity = quantity,
            MonsterId = monsterId,
            CanGather = gather,
            CanFight = fight,
            HasMap = true,
            LevelAllowed = true,
            BottingAuthorized = true,
            AcceptBagOnly = accept,
            RouteMapIds = maps ?? new long[] { 1 },
            BagStacks = stacks,
            Observations = observations,
            DropCatalog = catalog,
            AccountKey = "account-A",
            CharacterId = 10,
            GameVersion = "3.7.12.12",
            Now = Now,
            DisplayName = "Frêne"
        };
    }
}
