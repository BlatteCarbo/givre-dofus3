using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.ViewModels;
using GivreClone.Protocol;

namespace GivreClone.App.Services.ItemCraftPlanner;

public sealed record ItemCraftProductionInput(long Gid, int PerCraft, bool BuyAllowed);

/// <summary>One workshop recipe. ResultPerCraft and KamasPerCraft stay null when the local row does not
/// evidence them. KamasPerCraft is the explicit kamas of one confirmed fabrication, not a guessed formula.</summary>
public sealed record ItemCraftProductionRecipe(
    long ResultGid,
    int? ResultPerCraft,
    int? SkillId,
    long? KamasPerCraft,
    IReadOnlyList<ItemCraftProductionInput> Inputs);

/// <summary>Order for one item. SaleFloor null does not list. Energy is not an input and is never consulted.</summary>
public sealed record ItemCraftProductionRequest(
    ItemCraftProductionRecipe Recipe,
    int Quantity,
    long MaxKamas,
    bool BankAllowed,
    long? SaleFloor);

public sealed record ItemCraftProductionProgress(string Phase, int Produced, long Spent, int Listed, string Detail)
{
    public string Text
    {
        get
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            return Phase + " · " + Produced.ToString("N0", culture) + " fabriqué(s) · "
                + Spent.ToString("N0", culture) + " kamas · " + Detail;
        }
    }
}

public sealed record ItemCraftProductionResult(int Produced, long Spent, int Listed, string Stop);

public static class ItemCraftProductionStop
{
    public const string AlreadyRunning = "déjà en cours";
    public const string InvalidQuantity = "quantité invalide";
    public const string InvalidBudget = "budget invalide";
    public const string InvalidFloor = "plancher invalide";
    public const string IncompleteRecipe = "recette incomplète";
    public const string UnknownCost = "coût d'atelier inconnu";
    public const string UnknownSkill = "compétence d'atelier inconnue";
    public const string WorkshopMissing = "atelier introuvable";
    public const string Budget = "budget atteint";
    public const string MissingInputs = "ingrédients insuffisants";
    public const string BuyRefused = "achat refusé";
    public const string CraftUnconfirmed = "craft non confirmé";
    public const string QuantityReached = "quantité atteinte";
    public const string Short = "quantité non atteinte";
    public const string Stopped = "arrêté";
    public const string ListUnconfirmed = "mise en vente non confirmée";
    public const string StockUnverified = "variation du stock non confirmée";
}

/// <summary>Receipt-shaped workshop, chest and market operations. A true result or a positive quantity is a
/// server receipt. Transport acceptance alone must be returned as false or zero by the adapter.</summary>
public interface IItemCraftProductionActions : IHdvBuyPort
{
    int BagCount(long gid);
    int ChestCount(long gid);
    IReadOnlyList<MapInteractive> Interactives();
    Task<bool> OpenChestAsync(CancellationToken ct);
    Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct);
    Task<bool> CloseChestAsync(CancellationToken ct);
    Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
        IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct);
    Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct);
}

/// <summary>
/// Extra corroboration from the currently pinned character's inventory model.
/// This is NOT a server ACK: the corresponding bank/craft/market receipt remains
/// mandatory. Fail closed if a post-action count cannot match its receipt.
/// </summary>
public interface IItemCraftProductionStockObservation
{
    bool SupportsStockObservation { get; }
    bool TryReadBagCount(long gid, out int count);
}

public readonly record struct ItemCraftWorkshopReceipt(bool Confirmed, int Quantity, long ResultGid);

/// <summary>Withdraws, buys only allowed missing inputs through <see cref="HdvBuyRunner"/>, then crafts until
/// the quantity, the budget, or Stop. The pure planner is not called and its plan is not a receipt.
/// LaunchService.CraftAsync, BankOpenAsync, BankWithdrawAsync and BankCloseAsync are invoked by
/// ItemCraftProductionLaunch, which also supplies the buyer port.</summary>
public sealed class ItemCraftProductionRun
{
    // Lot sizes already accepted by the buyer and by BidHouseStore.SlotOf. Not new protocol fields.
    private static readonly int[] BuyLots = { 1, 10, 100, 1000 };
    private static readonly int[] SellLots = { 1000, 100, 10, 1 };

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// Explicit guided production entry, sharing the real IItemCraftProductionActions
    /// bag-count port and exact order with RunAsync. It performs ONLY an admitted
    /// preview and CAS journal checkpoint; gameplay is left to the separate,
    /// existing RunAsync receipt path. Missing certified observations stop safely.
    /// </summary>
    public ProfessionProductionGuidance PrepareVerifiedNextAction(
        ProfessionMcpSnapshot? catalog, ItemCraftProductionRequest request,
        IItemCraftProductionActions actions, IProfessionProductionJournal journal,
        bool explicitActivation) =>
        IsRunning
            ? new ProfessionProductionGuidance("PRODUCTION_ALREADY_RUNNING",
                null, null, 0, false, false, true)
            : ProfessionProductionWorkflow.Prepare(catalog, request, actions, journal,
                explicitActivation);

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate) cts = _cts;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Read-only validation for the actual production button BEFORE its durable
    /// Started admission. It shares the exact recipe, workshop and initial
    /// inventory checks with RunAsync, and never opens a bank or sends a craft.
    /// A successful preflight is not a lease or authorization to skip the
    /// in-run revalidation after the Started record is fsynced.
    /// </summary>
    public string? Preflight(ItemCraftProductionRequest request, IItemCraftProductionActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        if (IsRunning) return ItemCraftProductionStop.AlreadyRunning;
        var state = new State(request);
        return TryPreflightCore(state, actions, out _, out _, out _) ? null : state.Stop;
    }

    public async Task<ItemCraftProductionResult> RunAsync(ItemCraftProductionRequest request,
        IItemCraftProductionActions actions, IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        CancellationTokenSource linked;
        lock (_gate)
        {
            // Atomic with Stop(): the old sequence published _running=1
            // before assigning _cts, so Stop could miss the active attempt.
            if (_running != 0)
                return new ItemCraftProductionResult(0, 0, 0, ItemCraftProductionStop.AlreadyRunning);
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = linked;
            Volatile.Write(ref _running, 1);
        }
        var state = new State(request);
        try
        {
            await ExecuteAsync(state, actions, progress, linked.Token).ConfigureAwait(false);
            // Stop/session revocation can happen after the last confirmed receipt
            // but before returning. Never publish a clean terminal completion.
            linked.Token.ThrowIfCancellationRequested();
            return Finish(state, progress);
        }
        catch (OperationCanceledException)
        {
            state.Stop = ItemCraftProductionStop.Stopped;
            return Finish(state, progress);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cts, linked)) _cts = null;
            }
            linked.Dispose();
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task ExecuteAsync(State state, IItemCraftProductionActions actions,
        IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryPreflightCore(state, actions, out var inputs, out var workshop, out long fee))
            return;
        ct.ThrowIfCancellationRequested();
        Report(progress, state, "Préparation", "atelier " + workshop.ElementId);
        bool limitedByBudget = false;

        var request = state.Request;
        var recipe = request.Recipe;
        if (recipe.ResultPerCraft is int yield)
        {
            int planned = CeilDiv(request.Quantity, yield);
            int affordable = planned;
            if (fee > 0)
            {
                if (request.MaxKamas < fee) { state.Stop = ItemCraftProductionStop.Budget; return; }
                long fits = request.MaxKamas / fee;
                if (fits < affordable) affordable = fits > int.MaxValue ? int.MaxValue : (int)fits;
            }
            limitedByBudget = affordable < planned;
            if (affordable < 1) { state.Stop = ItemCraftProductionStop.Budget; return; }
            if (!await CoverAsync(state, actions, inputs, affordable, fee, progress, ct).ConfigureAwait(false)) return;
            for (int i = 0; i < affordable && state.Produced < request.Quantity; i++)
            {
                if (!await CraftOnceAsync(state, actions, inputs, workshop, fee, progress, ct).ConfigureAwait(false))
                    return;
            }
        }
        else
        {
            int guard = 0;
            while (state.Produced < request.Quantity && guard++ < request.Quantity)
            {
                if (!await CoverAsync(state, actions, inputs, 1, fee, progress, ct).ConfigureAwait(false)) return;
                int before = state.Produced;
                if (!await CraftOnceAsync(state, actions, inputs, workshop, fee, progress, ct).ConfigureAwait(false))
                    return;
                if (state.Produced <= before) { state.Stop = ItemCraftProductionStop.CraftUnconfirmed; return; }
            }
        }

        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(state.Stop))
        {
            state.Stop = state.Produced >= request.Quantity
                ? ItemCraftProductionStop.QuantityReached
                : limitedByBudget ? ItemCraftProductionStop.Budget : ItemCraftProductionStop.Short;
        }
        if (state.Stop == ItemCraftProductionStop.QuantityReached && request.SaleFloor is long floor)
            await ListAsync(state, actions, recipe.ResultGid, floor, progress, ct).ConfigureAwait(false);
    }

    private static bool TryPreflightCore(State state, IItemCraftProductionActions actions,
        out IReadOnlyList<ItemCraftProductionInput> inputs, out ItemCraftWorkshop workshop, out long fee)
    {
        inputs = Array.Empty<ItemCraftProductionInput>();
        workshop = default;
        fee = 0;
        var request = state.Request;
        if (request.Quantity < 1 || request.Quantity > ItemCraftPlanner.MaxQuantity)
        {
            state.Stop = ItemCraftProductionStop.InvalidQuantity;
            return false;
        }
        if (request.MaxKamas < 0)
        {
            state.Stop = ItemCraftProductionStop.InvalidBudget;
            return false;
        }
        if (request.SaleFloor is <= 0)
        {
            state.Stop = ItemCraftProductionStop.InvalidFloor;
            return false;
        }
        var recipe = request.Recipe;
        if (recipe is null || recipe.ResultGid <= 0 || recipe.ResultPerCraft is <= 0)
        {
            state.Stop = ItemCraftProductionStop.IncompleteRecipe;
            return false;
        }
        if (recipe.KamasPerCraft is not long documentedFee)
        {
            state.Stop = ItemCraftProductionStop.UnknownCost;
            return false;
        }
        fee = documentedFee;
        if (fee < 0)
        {
            state.Stop = ItemCraftProductionStop.InvalidBudget;
            return false;
        }
        var normalizedInputs = Normalize(recipe.Inputs);
        if (normalizedInputs is null)
        {
            state.Stop = ItemCraftProductionStop.IncompleteRecipe;
            return false;
        }
        inputs = normalizedInputs;
        if (recipe.SkillId is not int skill || skill <= 0)
        {
            state.Stop = ItemCraftProductionStop.UnknownSkill;
            return false;
        }
        if (fee > request.MaxKamas)
        {
            state.Stop = ItemCraftProductionStop.Budget;
            return false;
        }
        var matchedWorkshop = WorkshopLocator.Match(skill, actions.Interactives());
        if (matchedWorkshop is null)
        {
            state.Stop = ItemCraftProductionStop.WorkshopMissing;
            return false;
        }
        workshop = matchedWorkshop.Value;
        foreach (var input in inputs)
        {
            if (!ReadBag(actions, input.Gid, out int observed))
            {
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return false;
            }
            state.Ledger[input.Gid] = observed;
            // If even one craft cannot be supplied and neither the bank nor
            // buying is permitted, no mutation could ever make progress.
            // Reject before writing the durable Started entry in the real VM.
            if (observed < input.PerCraft &&
                !request.BankAllowed && !input.BuyAllowed)
            {
                state.Stop = ItemCraftProductionStop.MissingInputs;
                return false;
            }
        }
        // Legacy test adapters without a certified observation port retain
        // their historical bag-read contract. The real VM refuses those ports.
        if (actions is IItemCraftProductionStockObservation
                { SupportsStockObservation: true } &&
            !ReadBag(actions, recipe.ResultGid, out _))
        {
            state.Stop = ItemCraftProductionStop.StockUnverified;
            return false;
        }
        return true;
    }

    private async Task<bool> CoverAsync(State state, IItemCraftProductionActions actions,
        IReadOnlyList<ItemCraftProductionInput> inputs, int craftActions, long fee,
        IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        long reserved;
        try { reserved = checked(fee * craftActions); }
        catch (OverflowException) { state.Stop = ItemCraftProductionStop.IncompleteRecipe; return false; }
        if (!CanSpend(state.Spent, reserved, state.Request.MaxKamas))
        {
            state.Stop = ItemCraftProductionStop.Budget;
            return false;
        }
        long buyBudget = state.Request.MaxKamas - state.Spent - reserved;
        if (!Needs(inputs, craftActions, state.Ledger, out var missing))
        {
            state.Stop = ItemCraftProductionStop.IncompleteRecipe;
            return false;
        }
        if (missing.Count > 0 && state.Request.BankAllowed &&
            !await WithdrawAsync(state, actions, missing, progress, ct).ConfigureAwait(false))
            return false;

        foreach (var input in inputs)
        {
            if (!NeedOf(input, craftActions, out long need))
            {
                state.Stop = ItemCraftProductionStop.IncompleteRecipe;
                return false;
            }
            long have = state.Ledger.GetValueOrDefault(input.Gid);
            if (have >= need) continue;
            long shortfall = need - have;
            if (!input.BuyAllowed) { state.Stop = ItemCraftProductionStop.MissingInputs; return false; }
            if (buyBudget <= 0 || shortfall > int.MaxValue) { state.Stop = ItemCraftProductionStop.Budget; return false; }
            ct.ThrowIfCancellationRequested();
            if (!ReadBagBeforeMutation(actions, input.Gid, out int beforeBuy))
            {
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return false;
            }
            var lots = BuyLots.Select(qty => new LotQuote(qty, 0, true, buyBudget)).ToArray();
            var bought = await HdvBuyRunner.RunAsync(actions, new BuyRequest(input.Gid, (int)shortfall, lots),
                buyBudget, false, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!await MatchesStockDeltaAsync(actions, input.Gid, beforeBuy,
                    bought.Bought, ct).ConfigureAwait(false))
            {
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return false;
            }
            if (bought.Spent < 0 || bought.Bought < 0 || bought.Spent > buyBudget ||
                bought.Bought > shortfall || bought.Uncovered < 0 || bought.Uncovered > shortfall)
            {
                if (bought.Spent > 0) state.Spent += bought.Spent;
                state.Stop = ItemCraftProductionStop.Budget;
                return false;
            }
            state.Spent += bought.Spent;
            buyBudget -= bought.Spent;
            state.Ledger[input.Gid] = have + bought.Bought;
            Report(progress, state, "Achat", "gid " + input.Gid.ToString(CultureInfo.InvariantCulture)
                + " x" + bought.Bought.ToString(CultureInfo.InvariantCulture) + " · " + bought.Stop);
            if (state.Ledger[input.Gid] < need)
            {
                state.Stop = bought.Stop switch
                {
                    "achat refusé" => ItemCraftProductionStop.BuyRefused,
                    "budget atteint" or "budget non défini" => ItemCraftProductionStop.Budget,
                    _ when bought.Bought > 0 && bought.Uncovered > 0 => ItemCraftProductionStop.Budget,
                    _ => ItemCraftProductionStop.MissingInputs
                };
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> WithdrawAsync(State state, IItemCraftProductionActions actions,
        List<(ItemCraftProductionInput Input, long Count)> missing,
        IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool opened = await actions.OpenChestAsync(ct).ConfigureAwait(false);
        if (!opened)
        {
            Report(progress, state, "Retrait", "coffre non ouvert");
            return true; // Existing behavior: a verified market purchase may still supply inputs.
        }
        bool closeConfirmed = true;
        try
        {
            foreach (var (input, count) in missing)
            {
                ct.ThrowIfCancellationRequested();
                int chest = Math.Max(0, actions.ChestCount(input.Gid));
                if (chest == 0 || count <= 0) continue;
                int ask = (int)Math.Min(count, chest);
                if (!ReadBagBeforeMutation(actions, input.Gid, out int beforeWithdraw))
                {
                    state.Stop = ItemCraftProductionStop.StockUnverified;
                    return false;
                }
                int got = await actions.WithdrawAsync(input.Gid, ask, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (got < 0 || got > ask)
                {
                    // The bank receipt cannot be attributed to this exact
                    // request. Clamping would invent a confirmed quantity and
                    // could let the next craft consume an unknown inventory.
                    state.Stop = ItemCraftProductionStop.MissingInputs;
                    Report(progress, state, "Retrait", "quantité serveur incohérente ; rapprochement nécessaire");
                    return false;
                }
                if (!await MatchesStockDeltaAsync(actions, input.Gid, beforeWithdraw,
                        got, ct).ConfigureAwait(false))
                {
                    state.Stop = ItemCraftProductionStop.StockUnverified;
                    return false;
                }
                if (got == 0)
                {
                    Report(progress, state, "Retrait", "gid " + input.Gid.ToString(CultureInfo.InvariantCulture) + " non confirmé");
                    continue;
                }
                state.Ledger[input.Gid] = state.Ledger.GetValueOrDefault(input.Gid) + got;
                Report(progress, state, "Retrait", "gid " + input.Gid.ToString(CultureInfo.InvariantCulture)
                    + " x" + got.ToString(CultureInfo.InvariantCulture) + " confirmé");
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                closeConfirmed = await actions.CloseChestAsync(ct).ConfigureAwait(false);
                if (!closeConfirmed)
                {
                    state.Stop = ItemCraftProductionStop.MissingInputs;
                    Report(progress, state, "Retrait", "fermeture du coffre non confirmée ; rapprochement nécessaire");
                }
            }
        }
        return closeConfirmed;
    }

    private static async Task<bool> CraftOnceAsync(State state, IItemCraftProductionActions actions,
        IReadOnlyList<ItemCraftProductionInput> inputs, ItemCraftWorkshop workshop, long fee,
        IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanSpend(state.Spent, fee, state.Request.MaxKamas))
        {
            state.Stop = ItemCraftProductionStop.Budget;
            return false;
        }
        foreach (var input in inputs)
        {
            if (state.Ledger.GetValueOrDefault(input.Gid) < input.PerCraft)
            {
                state.Stop = ItemCraftProductionStop.MissingInputs;
                return false;
            }
        }
        var ingredients = inputs.Select(i => (i.Gid, i.PerCraft)).ToArray();
        var inputStockBefore = new Dictionary<long, int>();
        if (actions is IItemCraftProductionStockObservation { SupportsStockObservation: true })
        {
            foreach (var input in inputs)
            {
                if (!ReadBag(actions, input.Gid, out int current) ||
                    current != state.Ledger.GetValueOrDefault(input.Gid))
                {
                    state.Stop = ItemCraftProductionStop.StockUnverified;
                    return false;
                }
                inputStockBefore.Add(input.Gid, current);
            }
        }
        if (!ReadBagBeforeMutation(actions, state.Request.Recipe.ResultGid, out int beforeCraft))
        {
            state.Stop = ItemCraftProductionStop.StockUnverified;
            return false;
        }
        var receipt = await actions.CraftAsync(workshop.ElementId, workshop.SkillInstanceUid, ingredients, 1, ct)
            .ConfigureAwait(false);
        // A successful reply with GID=0 is not proof that THIS target item was
        // made; the durable caller must never complete on it. Stop/identity
        // revocation while awaiting the server response also prevents the late
        // receipt from being credited to the retired production run.
        ct.ThrowIfCancellationRequested();
        int? documentedYield = state.Request.Recipe.ResultPerCraft;
        int remaining = state.Request.Quantity - state.Produced;
        if (!receipt.Confirmed || receipt.Quantity <= 0 ||
            receipt.ResultGid != state.Request.Recipe.ResultGid ||
            (documentedYield is int expected && receipt.Quantity != expected) ||
            (documentedYield is null && receipt.Quantity > remaining))
        {
            state.Stop = ItemCraftProductionStop.CraftUnconfirmed;
            return false;
        }
        // The same target GID can theoretically appear among ingredients.
        // Compare its NET quantity change rather than counting a consumed
        // ingredient as an additional produced item.
        long expectedTargetChange = receipt.Quantity -
            inputs.Where(i => i.Gid == state.Request.Recipe.ResultGid).Sum(i => (long)i.PerCraft);
        if (!await MatchesStockDeltaAsync(actions, state.Request.Recipe.ResultGid,
                beforeCraft, expectedTargetChange, ct).ConfigureAwait(false))
        {
            state.Stop = ItemCraftProductionStop.StockUnverified;
            return false;
        }
        foreach (var input in inputs)
        {
            if (input.Gid == state.Request.Recipe.ResultGid) continue;
            if (!inputStockBefore.TryGetValue(input.Gid, out int beforeInput)) continue;
            if (!await MatchesStockDeltaAsync(actions, input.Gid, beforeInput,
                    -(long)input.PerCraft, ct).ConfigureAwait(false))
            {
                // A positive result for this GID without evidence of its
                // ingredients leaving the bag is not a complete craft witness.
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return false;
            }
        }
        foreach (var input in inputs)
            state.Ledger[input.Gid] -= input.PerCraft;
        try { state.Produced = checked(state.Produced + receipt.Quantity); }
        catch (OverflowException) { state.Stop = ItemCraftProductionStop.IncompleteRecipe; return false; }
        try { state.Spent = checked(state.Spent + fee); }
        catch (OverflowException) { state.Stop = ItemCraftProductionStop.Budget; return false; }
        Report(progress, state, "Fabrication", "x" + receipt.Quantity.ToString(CultureInfo.InvariantCulture) + " confirmé");
        ct.ThrowIfCancellationRequested();
        if (state.Spent > state.Request.MaxKamas) { state.Stop = ItemCraftProductionStop.Budget; return false; }
        return true;
    }

    private static async Task ListAsync(State state, IItemCraftProductionActions actions, long gid, long floor,
        IProgress<ItemCraftProductionProgress>? progress, CancellationToken ct)
    {
        int left = state.Produced;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryLot(left, floor, out int lot, out long price))
            {
                state.Stop = ItemCraftProductionStop.ListUnconfirmed;
                return;
            }
            if (!ReadBagBeforeMutation(actions, gid, out int beforeSale))
            {
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return;
            }
            bool confirmed = await actions.ListAsync(gid, lot, price, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!confirmed)
            {
                state.Stop = ItemCraftProductionStop.ListUnconfirmed;
                Report(progress, state, "Vente", "non confirmée");
                return;
            }
            if (!await MatchesStockDeltaAsync(actions, gid, beforeSale, -lot,
                    ct).ConfigureAwait(false))
            {
                state.Stop = ItemCraftProductionStop.StockUnverified;
                return;
            }
            state.Listed += lot;
            left -= lot;
            Report(progress, state, "Vente", "x" + lot.ToString(CultureInfo.InvariantCulture)
                + " à " + price.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool ReadBag(IItemCraftProductionActions actions, long gid, out int count)
    {
        count = 0;
        if (actions is IItemCraftProductionStockObservation { SupportsStockObservation: true } observed)
            return observed.TryReadBagCount(gid, out count) && count >= 0;
        count = actions.BagCount(gid);
        return count >= 0;
    }

    private static bool ReadBagBeforeMutation(IItemCraftProductionActions actions,
        long gid, out int count)
    {
        count = 0;
        return actions is not IItemCraftProductionStockObservation { SupportsStockObservation: true } ||
            ReadBag(actions, gid, out count);
    }

    private static async Task<bool> MatchesStockDeltaAsync(
        IItemCraftProductionActions actions, long gid, int before,
        long expected, CancellationToken ct)
    {
        // Legacy unit adapters have no independent inventory witness; they
        // still require the original operation's explicit receipt. The real
        // production entry requires an observation port before starting.
        if (actions is not IItemCraftProductionStockObservation { SupportsStockObservation: true })
            return true;
        long wanted = (long)before + expected;
        if (wanted < 0 || wanted > int.MaxValue) return false;
        // Inventory models can be one decoded event behind the definitive
        // receipt. Wait only for the observation, NEVER resend the operation.
        // A missing source/identity is an immediate failure, not a retry.
        for (int attempt = 0; attempt < 9; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReadBag(actions, gid, out int after)) return false;
            if (after == wanted) return true;
            // A larger-than-attributed move is ambiguous; do not normalize it.
            if ((expected >= 0 && after > wanted) ||
                (expected < 0 && after < wanted)) return false;
            if (attempt < 8)
                await Task.Delay(75, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static bool TryLot(int left, long floor, out int lot, out long price)
    {
        foreach (int candidate in SellLots)
        {
            if (candidate > left || candidate <= 0) continue;
            try
            {
                long total = checked(floor * candidate);
                if (total / candidate != floor) continue;
                lot = candidate;
                price = total;
                return true;
            }
            catch (OverflowException) { }
        }
        lot = 0;
        price = 0;
        return false;
    }

    private static bool CanSpend(long spent, long next, long budget)
        => spent >= 0 && next >= 0 && spent <= budget && budget - spent >= next;

    private static bool Needs(IReadOnlyList<ItemCraftProductionInput> inputs, int craftActions,
        Dictionary<long, long> ledger, out List<(ItemCraftProductionInput Input, long Count)> missing)
    {
        missing = new List<(ItemCraftProductionInput, long)>();
        foreach (var input in inputs)
        {
            if (!NeedOf(input, craftActions, out long need)) return false;
            long have = ledger.GetValueOrDefault(input.Gid);
            if (have < need) missing.Add((input, need - have));
        }
        return true;
    }

    private static bool NeedOf(ItemCraftProductionInput input, int craftActions, out long need)
    {
        try
        {
            need = checked((long)input.PerCraft * craftActions);
            return need > 0;
        }
        catch (OverflowException)
        {
            need = 0;
            return false;
        }
    }

    private static int CeilDiv(int quantity, int yield)
        => quantity / yield + (quantity % yield == 0 ? 0 : 1);

    private static IReadOnlyList<ItemCraftProductionInput>? Normalize(IReadOnlyList<ItemCraftProductionInput>? inputs)
    {
        if (inputs is null || inputs.Count == 0) return null;
        var merged = new Dictionary<long, (int Qty, bool Buy)>();
        foreach (var input in inputs)
        {
            if (input.Gid <= 0 || input.PerCraft <= 0) return null;
            if (merged.TryGetValue(input.Gid, out var previous))
            {
                try
                {
                    merged[input.Gid] = (checked(previous.Qty + input.PerCraft), previous.Buy || input.BuyAllowed);
                }
                catch (OverflowException) { return null; }
            }
            else merged[input.Gid] = (input.PerCraft, input.BuyAllowed);
        }
        return merged.OrderBy(pair => pair.Key)
            .Select(pair => new ItemCraftProductionInput(pair.Key, pair.Value.Qty, pair.Value.Buy))
            .ToArray();
    }

    private static void Report(IProgress<ItemCraftProductionProgress>? progress, State state, string phase, string detail)
        => progress?.Report(new ItemCraftProductionProgress(phase, state.Produced, state.Spent, state.Listed, detail));

    private static ItemCraftProductionResult Finish(State state, IProgress<ItemCraftProductionProgress>? progress)
    {
        if (string.IsNullOrEmpty(state.Stop)) state.Stop = ItemCraftProductionStop.Short;
        Report(progress, state, "Terminé", state.Stop);
        return new ItemCraftProductionResult(state.Produced, state.Spent, state.Listed, state.Stop);
    }

    private sealed class State
    {
        public State(ItemCraftProductionRequest request) => Request = request;
        public ItemCraftProductionRequest Request { get; }
        public Dictionary<long, long> Ledger { get; } = new();
        public int Produced { get; set; }
        public long Spent { get; set; }
        public int Listed { get; set; }
        public string Stop { get; set; } = "";
    }
}
