using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Mcp;
using GivreClone.App.Services.Supervisor;
using GivreControl;

namespace GivreClone.App.ViewModels;

/// <summary>Observed game data and narrowly scoped MCP controls. No return value from an item/script send
/// is interpreted as a server acknowledgement. The caller's action gate is enforced in ControlAsync.</summary>
public partial class BotsViewModel
{
    private static int Number(JsonElement args, string key, int fallback, int min, int max)
    {
        if (!args.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < min || number > max)
            throw new ArgumentException($"'{key}' must be an integer between {min} and {max}.");
        return number;
    }

    private static bool Flag(JsonElement args, string key)
        => args.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();

    private static JsonNode EffectsJson(LaunchService launch, uint pid, int uid)
        => new JsonArray(launch.ItemEffectLines(pid, uid).Select(l => (JsonNode?)new JsonObject { ["text"] = l.Text, ["negative"] = l.Negative }).ToArray());

    private static JsonNode RawEffectsJson(LaunchService launch, uint pid, int uid)
        => new JsonArray(launch.ItemEffects(pid, uid).Select(e => (JsonNode?)new JsonObject {
            ["actionId"] = e.ActionId, ["value"] = e.Value, ["min"] = e.Min, ["max"] = e.Max,
            ["constant"] = e.Const, ["hidden"] = launch.GameNames.Effect(e.ActionId)?.ShowOnItem == false }).ToArray());

    private static JsonNode? Known(long n) => n >= 0 ? JsonValue.Create(n) : null;
    private static JsonNode? Known(int n) => n >= 0 ? JsonValue.Create(n) : null;

    // Exact ids/pids are preferred. Ambiguous display names cannot select a target for a write.
    private BotSnapshot WritableGameBot(JsonElement args)
    {
        // Same pure unique-ID resolver and protected-account gate used by every MCP bot write.
        var identity = ControlWriteTarget(args);
        var bot = ControlBot(args);
        if (bot.Pid <= 0) throw new InvalidOperationException("Bot is offline.");
        if (bot.Pid != identity.Pid) throw new InvalidOperationException("Selected account session changed; action refused.");
        return bot;
    }

    private static JsonObject SettingsView(BotSettings c) => new()
    {
        ["Speed"] = c.Speed.ToString(), ["LegitSettleMinMs"] = c.LegitSettleMinMs,
        ["LegitSettleMaxMs"] = c.LegitSettleMaxMs, ["PreferWalk"] = c.PreferWalk,
        ["WalkMaxHops"] = c.WalkMaxHops, ["ZaapTravel"] = c.ZaapTravel.ToString(),
        ["AntiStuckEnabled"] = c.AntiStuckEnabled,
        ["AntiStuckRestartScript"] = c.AntiStuckRestartScript,
        ["AntiStuckTimeoutSeconds"] = c.AntiStuckTimeoutSeconds,
        ["AchievementAutoClaim"] = c.AchievementAutoClaim,
        ["NotifyAnomalyOpened"] = c.NotifyAnomalyOpened,
        ["NotifyAnomalyClosed"] = c.NotifyAnomalyClosed,
        ["RegenerationEnabled"] = c.RegenerationEnabled,
        ["RegenerationStartPercent"] = c.RegenerationStartPercent,
        ["RegenerationStopPercent"] = c.RegenerationStopPercent,
        ["RegenerationFoodGid"] = c.RegenerationFoodGid,
        ["RegenerationWaitForTeam"] = c.RegenerationWaitForTeam
    };

    /// <summary>Validate all changes before mutating the persistent settings object (no partial writes).
    /// Passwords, LLM keys, endpoints, webhooks, response text and arbitrary reflection setters are excluded.</summary>
    private static JsonObject ChangeSettings(BotSettings settings, JsonElement changes)
        => ApplySettings(settings, changes, BotSettingsStore.Save);

    /// <summary>Same production validation/commit path with an injectable persistence callback.
    /// The callback allows an offline test harness to prove transactionality without touching user files.</summary>
    private static JsonObject ApplySettings(BotSettings settings, JsonElement changes, Action persist)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(persist);
        if (changes.ValueKind != JsonValueKind.Object) throw new ArgumentException("'changes' must be an object.");
        if (changes.EnumerateObject().Count() > 12) throw new ArgumentException("Too many setting changes.");
        var operations = new List<Action>();
        var applied = new JsonArray();
        int proposedMin = settings.LegitSettleMinMs, proposedMax = settings.LegitSettleMaxMs;
        int proposedStart = settings.RegenerationStartPercent, proposedStop = settings.RegenerationStopPercent;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in changes.EnumerateObject())
        {
            if (!distinct.Add(property.Name)) throw new ArgumentException($"Duplicate setting '{property.Name}'.");
            var v = property.Value;
            int Bounded(int min, int max)
            {
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n) || n < min || n > max)
                    throw new ArgumentException($"'{property.Name}' requires an integer between {min} and {max}.");
                return n;
            }
            bool Boolean()
            {
                if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException($"'{property.Name}' requires a boolean.");
                return v.GetBoolean();
            }
            switch (property.Name)
            {
                case nameof(BotSettings.Speed):
                    string speed = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    if (!Enum.TryParse<MovementSpeed>(speed, true, out var mode)
                        || !Enum.IsDefined(mode) || int.TryParse(speed, out _)
                        || (mode is not (MovementSpeed.Rapide or MovementSpeed.Legit)))
                        throw new ArgumentException("Speed must be 'Rapide' or 'Legit'.");
                    operations.Add(() => settings.Speed = mode);
                    break;
                case nameof(BotSettings.LegitSettleMinMs):
                    proposedMin = Bounded(100, 30000);
                    int mn = proposedMin;
                    operations.Add(() => settings.LegitSettleMinMs = mn);
                    break;
                case nameof(BotSettings.LegitSettleMaxMs):
                    proposedMax = Bounded(100, 30000);
                    int mx = proposedMax;
                    operations.Add(() => settings.LegitSettleMaxMs = mx);
                    break;
                case nameof(BotSettings.PreferWalk):
                    bool walk = Boolean(); operations.Add(() => settings.PreferWalk = walk); break;
                case nameof(BotSettings.WalkMaxHops):
                    int hops = Bounded(0, 10000); operations.Add(() => settings.WalkMaxHops = hops); break;
                case nameof(BotSettings.ZaapTravel):
                    string zaapName = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    if (!Enum.TryParse<ZaapRoute>(zaapName, true, out var zaapRoute)
                        || !Enum.IsDefined(zaapRoute) || int.TryParse(zaapName, out _))
                        throw new ArgumentException("ZaapTravel must be Auto, WorldMap, Ground, HavenBag or Zaapi.");
                    operations.Add(() => settings.ZaapTravel = zaapRoute);
                    break;
                case nameof(BotSettings.AntiStuckEnabled):
                    bool watchdog = Boolean(); operations.Add(() => settings.AntiStuckEnabled = watchdog); break;
                case nameof(BotSettings.AntiStuckRestartScript):
                    bool restart = Boolean(); operations.Add(() => settings.AntiStuckRestartScript = restart); break;
                case nameof(BotSettings.AntiStuckTimeoutSeconds):
                    int timeout = Bounded(30, 3600); operations.Add(() => settings.AntiStuckTimeoutSeconds = timeout); break;
                case nameof(BotSettings.AchievementAutoClaim):
                    bool claim = Boolean(); operations.Add(() => settings.AchievementAutoClaim = claim); break;
                case nameof(BotSettings.NotifyAnomalyOpened):
                    bool opened = Boolean(); operations.Add(() => settings.NotifyAnomalyOpened = opened); break;
                case nameof(BotSettings.NotifyAnomalyClosed):
                    bool closed = Boolean(); operations.Add(() => settings.NotifyAnomalyClosed = closed); break;
                case nameof(BotSettings.RegenerationEnabled):
                    bool regeneration = Boolean(); operations.Add(() => settings.RegenerationEnabled = regeneration); break;
                case nameof(BotSettings.RegenerationStartPercent):
                    proposedStart = Bounded(1, 100);
                    int start = proposedStart;
                    operations.Add(() => settings.RegenerationStartPercent = start); break;
                case nameof(BotSettings.RegenerationStopPercent):
                    proposedStop = Bounded(1, 100);
                    int stop = proposedStop;
                    operations.Add(() => settings.RegenerationStopPercent = stop); break;
                case nameof(BotSettings.RegenerationFoodGid):
                    int food = Bounded(0, int.MaxValue); operations.Add(() => settings.RegenerationFoodGid = food); break;
                case nameof(BotSettings.RegenerationWaitForTeam):
                    bool team = Boolean(); operations.Add(() => settings.RegenerationWaitForTeam = team); break;
                default: throw new ArgumentException($"Setting '{property.Name}' is unknown or not writable via MCP.");
            }
            applied.Add(property.Name);
        }
        if (proposedMin > proposedMax) throw new ArgumentException("LegitSettleMinMs must not exceed LegitSettleMaxMs.");
        if (proposedStart > proposedStop) throw new ArgumentException("RegenerationStartPercent must not exceed RegenerationStopPercent.");
        if (operations.Count == 0) throw new ArgumentException("At least one setting is required.");
        foreach (var operation in operations) operation();
        persist();
        // Save() is best-effort and suppresses IO failures; do not claim durable persistence.
        return new JsonObject { ["ok"] = true, ["status"] = "applied_in_memory",
            ["persistenceVerified"] = false, ["applied"] = applied, ["settings"] = SettingsView(settings) };
    }

    /// <summary>Returns null only for unknown tool names; all advertised names must produce data or a clear error.</summary>
    internal async Task<JsonNode?> ControlGameAsync(ControlRequest request, AppBotOrders? orders, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var args = request.Arguments;
        var launch = Stores.Launch;
        switch (request.Tool)
        {
            case "get_character_info":
            {
                var bot = ControlBot(args);
                if (bot.Pid <= 0) throw new InvalidOperationException("Character is offline; sheet not available.");
                var game = new LotMGame(launch, (uint)bot.Pid, Stores.World);
                var sheet = game.Sheet();
                var extra = game.SheetMore();
                return new JsonObject
                {
                    ["character"] = sheet is null ? null : new JsonObject
                    {
                        ["id"] = Known(sheet.Id), ["name"] = sheet.Name, ["breed"] = sheet.Breed,
                        ["level"] = Known(sheet.Level), ["xpTotal"] = Known(sheet.XpTotal),
                        ["xpToNextLevel"] = Known(sheet.XpToNextLevel),
                        ["life"] = Known(sheet.Life), ["maxLife"] = Known(sheet.MaxLife),
                        ["energy"] = Known(sheet.Energy), ["maxEnergy"] = Known(sheet.MaxEnergy),
                        ["kamas"] = Known(sheet.Kamas)
                    },
                    ["characteristics"] = new JsonArray(launch.SelfCharacteristics((uint)bot.Pid).OrderBy(x => x.Key)
                        .Select(x => (JsonNode?)new JsonObject { ["id"] = x.Key, ["total"] = x.Value }).ToArray()),
                    ["setBonuses"] = new JsonArray(launch.SetBonusLines((uint)bot.Pid)
                        .Select(x => (JsonNode?)new JsonObject { ["text"] = x.Text, ["negative"] = x.Negative }).ToArray()),
                    ["pods"] = bot.Pods, ["podsMax"] = bot.PodsMax,
                    ["lifeAuthoritative"] = launch.SelfLifeAuthoritative((uint)bot.Pid),
                    ["spells"] = new JsonArray(extra.Spells.Take(150).Select(s => (JsonNode?)new JsonObject
                        { ["id"] = s.Id, ["name"] = s.Name, ["level"] = s.Level }).ToArray()),
                    ["jobs"] = new JsonArray(extra.Jobs.Take(100).Select(j => (JsonNode?)new JsonObject
                        { ["id"] = j.Id, ["name"] = j.Name, ["level"] = j.Level }).ToArray()),
                    ["equipment"] = new JsonArray(extra.Equipment.Take(32).Select(e => (JsonNode?)new JsonObject
                        { ["position"] = e.Position, ["gid"] = e.Gid, ["name"] = e.Name, ["level"] = Known(e.Level) }).ToArray()),
                    ["look"] = launch.SelfLook((uint)bot.Pid)?.ToLookString(),
                    ["baseLook"] = launch.BaseCharacterLook((uint)bot.Pid)?.ToLookString(),
                    ["unspentPoints"] = Known(extra.UnspentPoints), ["gameDataVersion"] = game.GameVersion,
                    ["observed"] = sheet is not null, ["serverConfirmed"] = false
                };
            }
            case "get_bank":
            {
                var bot = ControlBot(args);
                if (bot.Pid <= 0) throw new InvalidOperationException("Bot is offline; bank cache unavailable.");
                int offset = Number(args, "offset", 0, 0, 10000);
                int limit = Number(args, "limit", 100, 1, 200);
                var all = launch.Bank.Items((uint)bot.Pid).Where(i => i.Qty > 0).OrderBy(i => i.Gid).ThenBy(i => i.Uid).ToArray();
                long kamas = launch.BankKamas((uint)bot.Pid);
                var items = new JsonArray(all.Skip(offset).Take(limit).Select(i => (JsonNode?)new JsonObject
                    { ["uid"] = i.Uid, ["gid"] = i.Gid, ["name"] = launch.ItemDisplayName(i.Gid), ["quantity"] = i.Qty,
                        ["effects"] = Flag(args, "includeEffects") ? EffectsJson(launch, (uint)bot.Pid, i.Uid) : null }).ToArray());
                return new JsonObject { ["items"] = items, ["kamas"] = Known(kamas), ["snapshotKnown"] = kamas >= 0 || all.Length > 0,
                    ["totalStacks"] = all.Length, ["offset"] = offset, ["limit"] = limit, ["hasMore"] = all.Length > (long)offset + limit };
            }
            case "search_items":
            {
                string query = Required(args, "query").Trim();
                if (query.Length > 100) throw new ArgumentException("Search text too long (max 100).");
                int offset = Number(args, "offset", 0, 0, 5000);
                int limit = Number(args, "limit", 20, 1, 100);
                var hits = launch.SearchItems(query, offset + limit + 1);
                return new JsonObject { ["query"] = query, ["offset"] = offset, ["limit"] = limit,
                    ["hasMore"] = hits.Count > offset + limit, ["catalogueLoaded"] = launch.GameNames.ItemDataLoaded,
                    ["items"] = new JsonArray(hits.Skip(offset).Take(limit).Select(i => (JsonNode?)new JsonObject
                        { ["gid"] = i.Gid, ["name"] = i.Name, ["level"] = Known(i.Level) }).ToArray()) };
            }
            case "get_item_info":
            {
                long gid = args.GetProperty("gid").GetInt64();
                if (gid <= 0 || gid > int.MaxValue) throw new ArgumentException("gid must be a positive known catalogue identifier.");
                var sources = launch.GameData?.Sources(gid);
                string? name = launch.GameData?.ItemName(gid);
                int level = launch.ItemLevel((int)gid);
                string? category = launch.GameNames.ItemCategory((int)gid);
                return new JsonObject { ["gid"] = gid, ["found"] = name is not null || level >= 0 || sources is not null,
                    ["name"] = name ?? (level >= 0 ? launch.ItemDisplayName(gid) : null),
                    ["level"] = Known(level), ["category"] = category, ["iconId"] = launch.ItemIconId(gid) is var icon && icon > 0 ? JsonValue.Create(icon) : null,
                    ["craft"] = sources?.Craft is { } craft ? new JsonObject { ["job"] = craft.Job, ["level"] = craft.Level } : null,
                    ["harvestSkills"] = sources is null ? null : new JsonArray(sources.HarvestSkills.Take(50).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                    ["harvestZones"] = sources is null ? null : new JsonArray(sources.HarvestZones.Take(50).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                    ["monsters"] = sources is null ? null : new JsonArray(sources.Monsters.Take(50).Select(m => (JsonNode?)new JsonObject
                        { ["id"] = m.MonsterId, ["name"] = m.Name, ["dropRate"] = m.DropRate }).ToArray()) };
            }
            case "find_places":
            {
                var bot = ControlBot(args);
                if (bot.Pid <= 0 || bot.MapId <= 0) throw new InvalidOperationException("Bot position unavailable.");
                var db = launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                if (EcoService.OriginOf((uint)bot.Pid) is not { } placeOrigin)
                    throw new InvalidOperationException("Current map coordinates are unknown.");
                string query = Text(args, "query").Trim();
                if (query.Length > 100) throw new ArgumentException("Search text too long (max 100).");
                int limit = Number(args, "limit", 10, 1, 50);
                var places = db.Places(query, new PlaceOrigin(placeOrigin.X, placeOrigin.Y, placeOrigin.World ?? 1), limit, launch.WorldPlaceDoors)
                    ?? throw new InvalidOperationException("Map hint data unavailable.");
                return new JsonObject { ["places"] = new JsonArray(places.Select(p => (JsonNode?)new JsonObject
                    { ["name"] = p.Name, ["category"] = p.Category, ["mapId"] = p.MapId, ["coords"] = p.Coords,
                      ["subArea"] = p.SubArea, ["area"] = p.Area, ["layer"] = p.Layer, ["distance"] = p.Distance,
                      ["doors"] = new JsonArray(p.Doors.Take(20).Select(d => (JsonNode?)new JsonObject
                        { ["cell"] = d.Cell, ["mapId"] = d.MapId }).ToArray()) }).ToArray()),
                    ["fromMapId"] = bot.MapId, ["limit"] = limit, ["localDataOnly"] = true };
            }
            case "list_zaapi_destinations":
            {
                var bot = ControlBot(args);
                if (bot.Pid <= 0) throw new InvalidOperationException("Bot is offline; zaapi list unavailable.");
                uint pid = (uint)bot.Pid;
                bool received = launch.ZaapiListReceived(pid);
                var view = launch.TeleportView(pid);
                var stops = launch.ZaapiStops(pid);
                static JsonNode? TextOrNull(string? text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);
                return new JsonObject
                {
                    ["received"] = received,
                    ["dialogOpen"] = view is { Type: ZaapiTravel.ListType },
                    ["labelsFromGameData"] = launch.GameData is not null,
                    ["destinations"] = new JsonArray(stops.Select(s => (JsonNode?)new JsonObject
                    {
                        ["mapId"] = s.MapId,
                        ["name"] = TextOrNull(s.Name),
                        ["district"] = TextOrNull(s.District),
                        ["subAreaId"] = s.SubAreaId,
                        ["x"] = s.X is int x ? (JsonNode?)JsonValue.Create(x) : null,
                        ["y"] = s.Y is int y ? (JsonNode?)JsonValue.Create(y) : null,
                        ["coords"] = TextOrNull(ZaapiTravel.Coordinates(s.X, s.Y)),
                        ["costKamas"] = s.CostKamas is long cost ? (JsonNode?)JsonValue.Create(cost) : null,
                        ["category"] = TextOrNull(s.Category),
                        ["categoryId"] = s.CategoryId > 0 ? (JsonNode?)JsonValue.Create(s.CategoryId) : null
                    }).ToArray()),
                    ["localDataOnly"] = true
                };
            }
            case "get_craft_advice":
            {
                // « Fabriquer ou acheter ? » : arbre de fabrication, coût, prix HDV du résultat, liste de courses. Lecture seule.
                uint advicePid = !string.IsNullOrWhiteSpace(Text(args, "bot")) && ControlBot(args) is { Pid: > 0 } adviceBot ? (uint)adviceBot.Pid : 0;
                var adviceDb = launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                long adviceGid = Number(args, "gid", 0, 0, int.MaxValue);
                string adviceQuery = Text(args, "query").Trim();
                var adviceMatches = new JsonArray();
                if (adviceGid == 0)
                {
                    if (adviceQuery.Length < 2) throw new ArgumentException("Provide gid, or query (2 characters minimum).");
                    var found = adviceDb.FindItems(adviceQuery, 10);
                    foreach (var m in found) adviceMatches.Add(new JsonObject { ["gid"] = m.Gid, ["name"] = m.Name, ["level"] = m.Level });
                    var pick = found.FirstOrDefault(m => adviceDb.Recipe(m.Gid) is not null);
                    if (pick.Gid == 0) return new JsonObject { ["found"] = false, ["matches"] = adviceMatches, ["note"] = "No matching item with a recipe." };
                    adviceGid = pick.Gid;
                }
                var advice = EcoService.Advice(advicePid, adviceGid, Number(args, "quantity", 1, 1, 100000))
                    ?? throw new InvalidOperationException("Local game database unavailable.");
                var tree = new JsonArray(Services.Eco.CraftAdvisor.Flatten(advice.Root).Select(t => (JsonNode?)new JsonObject
                {
                    ["depth"] = t.Depth, ["gid"] = t.Node.Gid, ["name"] = t.Node.Name, ["quantity"] = t.Node.Quantity,
                    ["decision"] = t.Node.Decision.ToString().ToLowerInvariant(), ["unitCost"] = t.Node.UnitCost, ["totalCost"] = t.Node.TotalCost,
                    ["priceSource"] = t.Node.PriceSource, ["stale"] = t.Node.Stale
                }).ToArray());
                var shopping = new JsonArray(Services.Eco.CraftAdvisor.ShoppingList(advice.Root).Select(x => (JsonNode?)new JsonObject
                    { ["gid"] = x.Gid, ["name"] = x.Name, ["quantity"] = x.Quantity, ["cost"] = x.Cost }).ToArray());
                return new JsonObject
                {
                    ["found"] = true, ["gid"] = adviceGid, ["name"] = EcoService.NameOf(adviceGid), ["quantity"] = advice.Root.Quantity,
                    ["craftCost"] = advice.CraftCost, ["buyCost"] = advice.BuyCost, ["saving"] = advice.Saving, ["savingPercent"] = advice.SavingPercent,
                    ["resaleProfitAfterTax"] = advice.ResaleProfit, ["recommendation"] = advice.Recommendation, ["anyStalePrice"] = advice.AnyStale,
                    ["missingPrices"] = new JsonArray(advice.MissingPrices.Select(g => (JsonNode?)new JsonObject { ["gid"] = g, ["name"] = EcoService.NameOf(g) }).ToArray()),
                    ["tree"] = tree, ["shoppingList"] = shopping, ["matches"] = adviceMatches,
                    ["pricesFrom"] = advicePid > 0 ? "selected bot's observed HDV prices, then manual prices" : "manual prices only (pass bot for HDV prices)",
                    ["estimate"] = true
                };
            }
            case "get_market_deals":
            {
                // Lots dont le prix unitaire est sous la moyenne du marché, d'après les prix HDV DÉJÀ observés par le bot. Aucune requête envoyée.
                var dealBot = ControlBot(args);
                if (dealBot.Pid <= 0) throw new InvalidOperationException("Bot is offline; observed HDV prices unavailable.");
                uint dealPid = (uint)dealBot.Pid;
                int dealMaxAge = Number(args, "maxAgeMinutes", 180, 0, 10080);
                var quotes = launch.Hdv.KnownGids(dealPid).Select(g =>
                {
                    long age = launch.Hdv.PriceAgeMs(dealPid, g);
                    long avg = launch.Hdv.AverageFor(dealPid, g);
                    if (avg <= 0 && g > 0 && g <= int.MaxValue) avg = launch.AveragePrice(dealPid, (int)g);
                    return new Services.Eco.MarketQuote(g, EcoService.NameOf(g), launch.Hdv.MarketPricesFor(dealPid, g), avg, age == long.MaxValue ? null : age);
                }).ToList();
                var deals = Services.Eco.MarketDeals.Find(quotes, Number(args, "minDiscountPercent", 25, 1, 95), Number(args, "minLotGain", 1000, 0, int.MaxValue),
                    dealMaxAge > 0 ? dealMaxAge * 60_000L : null);
                int dealLimit = Number(args, "limit", 30, 1, 200);
                return new JsonObject
                {
                    ["knownItems"] = quotes.Count, ["deals"] = deals.Count, ["estimate"] = true,
                    ["items"] = new JsonArray(deals.Take(dealLimit).Select(d => (JsonNode?)new JsonObject
                    {
                        ["gid"] = d.Gid, ["name"] = d.Name, ["lotSize"] = d.LotSize, ["unitPrice"] = d.UnitPrice, ["averagePrice"] = d.Average,
                        ["discountPercent"] = Math.Round(d.DiscountPercent, 1), ["lotGain"] = d.LotGain, ["ageMs"] = d.AgeMs
                    }).ToArray())
                };
            }
            case "get_wealth":
            {
                // Valeur estimée du sac et de la banque au prix HDV déjà observé (ou saisi à la main). Lecture seule, indicatif.
                var wealthBot = ControlBot(args);
                if (wealthBot.Pid <= 0) throw new InvalidOperationException("Bot is offline; inventory and prices unavailable.");
                uint wp = (uint)wealthBot.Pid;
                decimal? WPrice(long g) => EcoService.PriceFor(wp, g)?.Unit;
                var wBag = Services.Eco.Wealth.Compute(launch.Inventory.Items(wp).Select(i => (i.Gid, EcoService.NameOf(i.Gid), (long)i.Qty)), WPrice, 20);
                var wBank = Services.Eco.Wealth.Compute(launch.Bank.Items(wp).Select(i => (i.Gid, EcoService.NameOf(i.Gid), (long)i.Qty)), WPrice, 20);
                JsonNode Section(Services.Eco.WealthReport r) => new JsonObject
                {
                    ["total"] = r.Total, ["pricedItems"] = r.Priced, ["unpricedItems"] = r.Unpriced,
                    ["top"] = new JsonArray(r.Top.Select(t => (JsonNode?)new JsonObject { ["gid"] = t.Gid, ["name"] = t.Name, ["quantity"] = t.Quantity, ["unitPrice"] = t.Unit, ["total"] = t.Total }).ToArray())
                };
                long wk = launch.Kamas(wp), wbk = launch.BankKamas(wp);
                return new JsonObject
                {
                    ["bag"] = Section(wBag), ["bank"] = Section(wBank), ["kamasInBag"] = Known(wk), ["kamasInBank"] = Known(wbk),
                    ["estimatedTotal"] = wBag.Total + wBank.Total + Math.Max(0, wk) + Math.Max(0, wbk), ["estimate"] = true,
                    ["note"] = "Priced from observed or manual HDV prices; unpriced items and a bank never opened this session are not counted."
                };
            }
            case "get_subscriptions":
            {
                // Dernière fin d'abonnement vue par compte (lue des paquets du jeu) ; vide tant qu'aucun compte n'a été connecté.
                var nowUtc = DateTime.UtcNow;
                var entries = SubscriptionStore.Locked(b => b.Entries.OrderBy(e => e.EndUtc).ToList());
                return new JsonObject
                {
                    ["accounts"] = new JsonArray(entries.Select(e => (JsonNode?)new JsonObject
                    {
                        ["account"] = e.Login, ["character"] = e.Character, ["endUtc"] = e.EndUtc.Year < 2000 ? null : e.EndUtc.ToString("o"),
                        ["remaining"] = Services.Eco.SubscriptionTracker.Describe(e.EndUtc, nowUtc),
                        ["level"] = Services.Eco.SubscriptionTracker.Level(e.EndUtc, nowUtc).ToString().ToLowerInvariant(),
                        ["seenUtc"] = e.SeenUtc.ToString("o")
                    }).ToArray())
                };
            }
            case "get_bot_stats":
            {
                var bot = ControlBot(args);
                if (string.IsNullOrWhiteSpace(bot.Name)) throw new ArgumentException("Character name unknown; statistics unavailable.");
                string period = Text(args, "period", "day").Trim().ToLowerInvariant();
                StatsPeriod window = period switch
                {
                    "day" => StatsPeriod.Day, "week" => StatsPeriod.Week,
                    "month" => StatsPeriod.Month, "year" => StatsPeriod.Year,
                    _ => throw new ArgumentException("period must be day, week, month or year.")
                };
                var s = Stores.Stats.Compute(bot.Name, window, seriesPoints: 2);
                return new JsonObject { ["name"] = bot.Name, ["period"] = period, ["source"] = "local statistics",
                    ["harvests"] = s.Harvests, ["resourcesQty"] = s.ResourcesQty, ["fights"] = s.Fights,
                    ["fightsWon"] = s.FightsWon, ["fightsLost"] = s.FightsLost, ["avgFightSec"] = s.AvgFightSec,
                    ["bottingSec"] = s.BottingSec, ["kamasGained"] = s.KamasGained, ["xpGained"] = s.XpGained,
                    ["estimatedValue"] = s.EstimatedValue, ["kamasPerHour"] = s.KamasPerHour,
                    ["xpPerHour"] = s.XpPerHour, ["resourcesPerHour"] = s.ResourcesPerHour,
                    ["bankVisits"] = s.BankVisits, ["deaths"] = s.Deaths,
                    ["anomalyFragments"] = s.AnomalyFragments };
            }
            case "get_anomalies":
            {
                var bot = ControlBot(args);
                if (bot.Pid <= 0) throw new InvalidOperationException("Bot is offline; server anomaly snapshot unavailable.");
                var rows = launch.OpenAnomalies((uint)bot.Pid).OrderByDescending(a => a.State.RewardRate).Take(200).ToArray();
                var arr = new JsonArray();
                foreach (var a in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var sub = launch.GameData?.SubArea(a.State.SubareaId);
                    arr.Add(new JsonObject { ["subAreaId"] = a.State.SubareaId, ["name"] = sub?.Name,
                        ["area"] = sub?.Area, ["level"] = sub?.Level,
                        ["bonusPercent"] = a.State.RewardRate, ["remainingSeconds"] = a.Remaining,
                        ["observedUtc"] = a.State.ReceivedUtc.ToString("O", CultureInfo.InvariantCulture) });
                }
                return new JsonObject { ["anomalies"] = arr, ["openObserved"] = rows.Length,
                    ["note"] = "Last received game events only; an empty cache cannot prove that no anomaly exists." };
            }
            case "list_dungeons":
            {
                var db = launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                string query = Text(args, "query").Trim();
                if (query.Length > 100) throw new ArgumentException("Search text too long (max 100).");
                int offset = Number(args, "offset", 0, 0, 5000);
                int limit = Number(args, "limit", 25, 1, 100);
                var all = db.ExpeditionDungeons().Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (query.Length > 0 && d.Id.ToString(CultureInfo.InvariantCulture) == query)).ToArray();
                return new JsonObject { ["dungeons"] = new JsonArray(all.Skip(offset).Take(limit).Select(d => (JsonNode?)new JsonObject
                    { ["id"] = d.Id, ["name"] = d.Name, ["level"] = d.Level }).ToArray()),
                    ["total"] = all.Length, ["offset"] = offset, ["limit"] = limit, ["hasMore"] = all.Length > (long)offset + limit };
            }
            case "get_bot_settings":
            {
                var bot = ControlBot(args);
                if (string.IsNullOrWhiteSpace(bot.Name)) throw new ArgumentException("Character name unknown.");
                return new JsonObject { ["name"] = bot.Name, ["settings"] = SettingsView(BotSettingsStore.Get(bot.Name)),
                    ["contractVersion"] = 1, ["sensitiveSettingsOmitted"] = true };
            }
            case "set_bot_settings":
            {
                var bot = WritableGameBot(args);
                if (string.IsNullOrWhiteSpace(bot.Name)) throw new ArgumentException("Character name unknown.");
                if (!args.TryGetProperty("changes", out var changes)) throw new ArgumentException("'changes' is required.");
                ct.ThrowIfCancellationRequested();
                var settings = BotSettingsStore.Get(bot.Name);
                var res = ChangeSettings(settings, changes);
                if (bot.Pid > 0) launch.SetRegenerationOptions((uint)bot.Pid, settings);
                res["name"] = bot.Name;
                return res;
            }
            case "start_dungeon":
            {
                if (orders is null) throw new InvalidOperationException("Bot directory is not ready.");
                var bot = WritableGameBot(args);
                if (bot.InFight) throw new InvalidOperationException("Cannot start a dungeon script during combat.");
                int dungeonId = Number(args, "dungeonId", 0, 1, int.MaxValue);
                int runs = Number(args, "runs", 1, 0, 1000);
                int maxMinutes = Number(args, "maxMinutes", 0, 0, 10080);
                var db = launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                var dungeon = db.DungeonById(dungeonId) ?? throw new ArgumentException("Unknown dungeon ID.");
                if (dungeon.MapIds.Count == 0) throw new InvalidOperationException("Dungeon rooms missing from the local game database.");
                int elixirRank = Number(args, "elixirRank", 0, 0, 3);
                var elixir = elixirRank == 0 ? null : ElixirPlanFor(dungeon.EntranceMapId > 0 ? dungeon.EntranceMapId : dungeon.ExitMapId,
                    elixirRank, Number(args, "elixirMaxPrice", 0, 0, int.MaxValue), Number(args, "elixirStock", 2, 1, 50));
                var profile = new DungeonProfile(dungeon.Name, dungeon.MapIds, dungeon.EntranceMapId,
                    dungeon.Keys.Select(k => (k.Gid, k.Quantity, k.Name)).ToArray(), runs, maxMinutes, Elixir: elixir);
                var warnings = new List<string>();
                string lua = ExpeditionScript.BuildDungeon(profile, warnings);
                if (SavedLuaScripts.SyntaxError(lua) is { } problem) throw new ArgumentException(problem);
                ct.ThrowIfCancellationRequested();
                var result = await orders.StartScriptAsync(Required(args, "bot"), lua + "\n", ct);
                var response = ControlResult(result);
                response["dungeonId"] = dungeonId;
                response["rooms"] = dungeon.MapIds.Count;
                if (elixir is { MaxPrice: 0 }) warnings.Add("elixirMaxPrice = 0 : aucun achat à l'HDV, seul le stock déjà possédé sera utilisé.");
                response["elixir"] = elixir is null ? null : JsonValue.Create(elixir.ItemGid);
                response["warnings"] = new JsonArray(warnings.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
                response["serverConfirmed"] = false;
                return response;
            }
            case "exit_dungeon":
            {
                var bot = WritableGameBot(args);
                if (bot.InFight) throw new InvalidOperationException("Cannot leave dungeon during combat.");
                if (launch.GameData?.DungeonOfMap(bot.MapId) is null)
                    throw new InvalidOperationException("Current map is not a locally known dungeon room.");
                ct.ThrowIfCancellationRequested();
                var result = await launch.DungeonExitAsync((uint)bot.Pid, ct);
                ct.ThrowIfCancellationRequested();
                bool pending = result.Reason is "pas de réponse" or "envoi impossible" or "pas de relais"
                    or "la carte n'a pas changé" or "champ de DungeonExitResponse absent du mapping";
                string status = result.Ok ? "left" : pending ? "unconfirmed" : "refused";
                return new JsonObject { ["ok"] = result.Ok, ["status"] = status,
                    ["reason"] = result.Reason, ["mapId"] = Known(result.MapId), ["serverAcknowledged"] = result.Ok };
            }
            case "use_item":
            case "equip_item":
            case "unequip_item":
            {
                var bot = WritableGameBot(args);
                int uid = Number(args, "uid", 0, 1, int.MaxValue);
                uint pid = (uint)bot.Pid;
                var item = launch.Inventory.Get(pid, uid) ?? throw new ArgumentException("Inventory UID not found for this bot.");
                if (item.Qty <= 0) throw new ArgumentException("Inventory instance has zero quantity.");
                if (bot.InFight) throw new InvalidOperationException("Item control is unavailable during combat.");
                int position = Number(args, "position", -1, -1, 28);
                if (request.Tool == "equip_item" && position is >= 16 and <= 27)
                    throw new ArgumentException("Selected position is not a standard equipment or companion slot.");
                if (request.Tool == "use_item" && item.Position != 63) throw new InvalidOperationException("Item is currently equipped.");
                if (request.Tool == "equip_item" && item.Position != 63) throw new InvalidOperationException("Item is already equipped.");
                if (request.Tool == "unequip_item" && item.Position == 63) throw new InvalidOperationException("Item is already in the bag.");
                if (request.Tool == "equip_item" && position >= 0 && !launch.EquipPositions(item.Gid).Contains(position))
                    throw new ArgumentException("Selected equipment slot is not supported by this item.");
                ct.ThrowIfCancellationRequested();
                int? target = request.Tool == "use_item" ? null : request.Tool == "unequip_item" ? 63
                    : position >= 0 ? position : launch.EquipTarget(pid, item.Gid);
                var result = await launch.InventoryItemConfirmedAsync(pid, uid, target, ct);
                return new JsonObject { ["ok"] = result.ServerConfirmed,
                    ["status"] = result.ServerConfirmed ? "confirmed" : result.Rejected || !result.Sent ? "refused" : "unconfirmed",
                    ["sent"] = result.Sent, ["uid"] = uid, ["gid"] = item.Gid, ["resultUid"] = result.ResultUid,
                    ["serverConfirmed"] = result.ServerConfirmed };
            }
            default: return null;
        }
    }
}
