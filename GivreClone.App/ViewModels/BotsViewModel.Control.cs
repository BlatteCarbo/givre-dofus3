using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Mcp;
using GivreClone.App.Services.Supervisor;
using GivreControl;

namespace GivreClone.App.ViewModels;

/// <summary>Identity-only projection for deterministic MCP target selection. Never expose Login in a tool response.</summary>
public sealed record McpControlTarget(string AccountId, string Alias, string Character, int Pid, string? Login);

/// <summary>Pure resolution and permission checks used BEFORE any MCP account mutation.</summary>
public static class McpControlAccess
{
    public static McpControlTarget Resolve(string? key, IEnumerable<McpControlTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Missing argument: bot");
        string wanted = key.Trim();
        var all = targets.ToArray();
        static McpControlTarget Unique(McpControlTarget[] matches)
            => matches.Length switch
            {
                1 => matches[0],
                0 => throw new ArgumentException("Bot not found; use list_accounts and an exact account ID."),
                _ => throw new ArgumentException("Ambiguous bot identity; use an exact account ID.")
            };

        var ids = all.Where(t => string.Equals(t.AccountId, wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ids.Length > 0) return Unique(ids);
        if (int.TryParse(wanted, out int pid) && pid > 0)
        {
            var pids = all.Where(t => t.Pid == pid).ToArray();
            if (pids.Length > 0) return Unique(pids);
        }
        return Unique(all.Where(t => string.Equals(t.Alias, wanted, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(t.Character) && string.Equals(t.Character, wanted, StringComparison.OrdinalIgnoreCase))).ToArray());
    }

    public static void DemandWritable(McpControlTarget target, IEnumerable<string>? blocked)
    {
        if (string.IsNullOrWhiteSpace(target.AccountId) || string.IsNullOrWhiteSpace(target.Login))
            throw new InvalidOperationException("Account identity cannot be verified; action refused.");
        if (McpGuards.IsBlocked(target.Login, blocked ?? McpGuards.DefaultBlockedLogins))
            throw new InvalidOperationException("Protected account; MCP actions are disabled.");
    }
}

public partial class BotsViewModel
{
    private SavedLuaScripts? _controlScripts;
    private SavedLuaScripts ControlScripts => _controlScripts ??= new SavedLuaScripts(Path.Combine(MZP19LocalClone.DataRoot, "scripts"));
    private static readonly HashSet<string> ControlReads = new(StringComparer.Ordinal)
    { "get_app_status", "list_bots", "get_bot_state", "read_bot_console", "read_forge_history", "validate_lua_script", "list_saved_scripts", "read_saved_script",
      "list_supervisors", "get_supervisor_template", "list_teams", "get_scripting_guide", "search_lua_api", "get_inventory", "get_map_state", "probe_navigation",
      "list_accounts", "read_log", "fight_metrics", "get_combat_profile", "list_expedition_zones", "get_settings",
      "get_character_info", "get_bank", "search_items", "get_item_info", "find_places", "get_bot_stats",
      "get_anomalies", "list_dungeons", "get_bot_settings", "get_bot_diagnostics",
      "get_craft_advice", "get_market_deals", "get_subscriptions", "get_wealth", "find_treasure_clue", "plan_job_xp", "find_resource_maps", "find_capturable_monsters", "list_combat_journals", "get_combat_journal", "advise_rune", "get_alerts", "get_events", "read_chat", "plan_production_chain", "recommend_zones", "estimate_spell_damage", "list_zaapi_destinations" };
    private static readonly HashSet<string> AccountWrites = new(StringComparer.Ordinal)
    { "connect_bot", "disconnect_bot", "reconnect_bot", "travel_to", "zaapi_to", "run_saved_script", "run_lua_script", "stop_script", "start_smith_leveling", "stop_smith_leveling",
      "set_combat_profile", "start_expedition", "set_bot_settings", "start_dungeon", "exit_dungeon",
      "use_item", "equip_item", "unequip_item", "send_private_message", "set_spell_variant",
      "session.transition", "session.transition.cancel", "enhanced.activate" };

    private static JsonObject RefreshCost()
    {
        long ticks = Interlocked.Read(ref TickCount);
        long bytes = Interlocked.Read(ref TickAllocatedBytes), elapsed = Interlocked.Read(ref TickElapsedTicks);
        return new JsonObject
        {
            ["ticks"] = ticks,
            ["avgAllocatedBytes"] = ticks > 0 ? bytes / ticks : 0,
            ["avgMs"] = ticks > 0 ? Math.Round(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency / ticks, 3) : 0,
        };
    }

    private static string Text(JsonElement args, string key, string fallback = "")
        => args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static string Required(JsonElement args, string key)
        => Text(args, key) is { Length: > 0 } text ? text : throw new ArgumentException($"Missing argument: {key}");
    private static JsonObject ControlResult(OrderResult result) => new() { ["ok"] = result.Ok,
        ["status"] = result.Ok ? "accepted" : "refused", ["reason"] = result.Reason,
        ["completionConfirmed"] = false };

    private async Task<JsonObject> ControlTravelAsync(JsonElement args, string script, CancellationToken ct)
    {
        int pid = ControlIdentity(args).Target.Pid;
        if (_luaHosts.TryGetValue(pid, out var running) && running.Running)
            return ControlResult(OrderResult.No(QuickActions.ScriptOwns));
        var orders = new AppBotOrders(_accounts, this, _supDir!);
        var started = await orders.StartScriptAsync(Required(args, "bot"), script, ct);
        if (!started.Ok) return ControlResult(started);
        var host = _luaHosts[pid];
        // RemoteControlServer coupe une requête à 70 s : un long trajet continue et se lit ensuite dans list_bots.
        try { await host.WaitForRunAsync(ct).WaitAsync(TimeSpan.FromSeconds(45), ct); }
        catch (TimeoutException)
        {
            return new JsonObject { ["ok"] = true, ["status"] = "running", ["reason"] = "Trajet toujours en cours.", ["completionConfirmed"] = false };
        }
        var result = host.LastTravelResult ?? (Ok: false, Reason: QuickActions.Interrupted);
        return ControlTravelResult(result);
    }

    private static JsonObject ControlTravelResult((bool Ok, string Reason) result)
        => new() { ["ok"] = result.Ok, ["status"] = result.Ok ? "completed" : "failed",
            ["reason"] = result.Reason, ["completionConfirmed"] = result.Ok };
    private JsonObject Snapshot(BotSnapshot b, string? accountId = null)
    {
        var row = accountId is not null ? _accounts.Accounts.FirstOrDefault(r => r.Model.Id == accountId)
            : _accounts.Accounts.FirstOrDefault(r => b.Pid > 0 && r.Pid == b.Pid);
        return new JsonObject {
        ["accountId"] = row?.Model.Id,
        ["connectionStatus"] = row is null ? null : SafeLog(row.Status ?? ""),
        ["account"] = b.Account, ["name"] = b.Name, ["pid"] = b.Pid, ["mode"] = b.Mode, ["status"] = b.Status,
        ["mapId"] = b.MapId, ["coords"] = b.Coords, ["level"] = b.Level, ["kamas"] = b.Kamas,
        ["life"] = b.Life, ["maxLife"] = b.MaxLife, ["pods"] = b.Pods, ["podsMax"] = b.PodsMax,
        ["inFight"] = b.InFight, ["script"] = b.Script, ["scriptRunning"] = b.ScriptRunning,
        ["team"] = b.Team, ["leader"] = b.Leader, ["disconnect"] = b.Disconnect
        };
    }
    private (McpControlTarget Target, BotSnapshot Snapshot) ControlIdentity(JsonElement args)
    {
        _supDir?.Refresh();
        if (_supDir is null) throw new InvalidOperationException("Bot directory is not ready.");
        var resolved = _accounts.Accounts.Select(r => (Row: r, Snapshot: _supDir.ByAccountId(r.Model.Id)))
            .Where(x => x.Snapshot is not null)
            .Select(x => (x.Row, Snapshot: x.Snapshot!, Target: new McpControlTarget(
                x.Row.Model.Id, x.Snapshot!.Account, x.Snapshot.Name, x.Snapshot.Pid, x.Row.Login))).ToArray();
        var target = McpControlAccess.Resolve(Required(args, "bot"), resolved.Select(x => x.Target));
        var selected = resolved.Single(x => string.Equals(x.Target.AccountId, target.AccountId, StringComparison.OrdinalIgnoreCase));
        return (selected.Target, selected.Snapshot);
    }
    private BotSnapshot ControlBot(JsonElement args) => ControlIdentity(args).Snapshot;

    private McpControlTarget ControlWriteTarget(JsonElement args)
    {
        var identity = ControlIdentity(args);
        McpControlAccess.DemandWritable(identity.Target, AppConfig.Current.McpBlockedLogins);
        // A stale PID can select a different session; the actual account row must still own it.
        if (identity.Target.Pid > 0 && !_accounts.Accounts.Any(r => r.Model.Id == identity.Target.AccountId && r.Pid == identity.Target.Pid))
            throw new InvalidOperationException("Account PID no longer matches the selected session; action refused.");
        return identity.Target;
    }

    private void GuardTeamWrite(JsonElement args)
    {
        string action = Required(args, "action");
        if (action is "get" or "observeGathered" or "waitGathered") return;
        if (action == "create")
        {
            if (!args.TryGetProperty("members", out var ids) || ids.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Team creation requires explicit account IDs.");
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String) throw new ArgumentException("Team members must be account IDs.");
                var account = _accounts.Accounts.FirstOrDefault(r => string.Equals(r.Model.Id, id.GetString(), StringComparison.Ordinal));
                if (account is null) throw new ArgumentException("Unknown team member account ID.");
                McpControlAccess.DemandWritable(new McpControlTarget(account.Model.Id, "", "", account.Pid ?? 0, account.Login),
                    AppConfig.Current.McpBlockedLogins);
            }
            return;
        }
        var teamId = Text(args, "teamId");
        var name = Text(args, "name");
        var possible = Teams.Where(t => (teamId.Length > 0 && t.Id == teamId)
            || (teamId.Length == 0 && name.Length > 0 && t.Name == name)).ToArray();
        if (possible.Length != 1) throw new ArgumentException("Unknown or ambiguous team; use its exact ID.");
        foreach (var member in possible[0].Roster)
        {
            var account = _accounts.Accounts.FirstOrDefault(r => r.Model.Id == member.AccountId)
                ?? throw new InvalidOperationException("Team member account identity cannot be verified.");
            McpControlAccess.DemandWritable(new McpControlTarget(account.Model.Id, "", "", account.Pid ?? 0, account.Login),
                AppConfig.Current.McpBlockedLogins);
        }
    }

    internal ConsoleJournal ControlJournal(JsonElement args)
    {
        var bot = ControlBot(args);
        return McpConsole.For(bot.Pid > 0 ? Stores.Launch.ServerIdOf((uint)bot.Pid) : 0, bot.Name, (uint)Math.Max(0, bot.Pid));
    }

    internal async Task<JsonNode> ControlCoreAsync(ControlRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var a = request.Arguments;
        if (!ControlReads.Contains(request.Tool) && !AppConfig.Current.McpAllowActions)
            throw new InvalidOperationException("MCP actions are disabled in GivreWork settings; read tools remain available.");
        if (AccountWrites.Contains(request.Tool)) ControlWriteTarget(a);
        _supDir?.Refresh();
        var orders = _supDir is null ? null : new AppBotOrders(_accounts, this, _supDir);
        switch (request.Tool)
        {
            case "get_scripting_guide":
                return new JsonObject { ["guide"] = "First list_bots and inspect the chosen bot. Search the Lua API, validate code, then run only on the explicitly chosen bot. A script defines move(); return false ends it. run_lua_script replaces the main script. Prefer save_script, patch_script and run_saved_script for long scripts. Read results using get_bot_state and read_bot_console with its cursor; wait_console waits for new matching lines. Syntax validation does not prove API availability or gameplay. Never guess game ids. Team followers are controlled by their leader. emergency_stop stops all scripts and supervisors, without disconnecting accounts.",
                    ["localOnly"] = true, ["syntaxValidationOnly"] = true };
            case "search_lua_api": return ControlApiSearch(a);
            case "get_inventory":
                var inventoryBot = ControlBot(a);
                if (inventoryBot.Pid <= 0) throw new ArgumentException("Bot is offline; inventory is unavailable.");
                var inventory = Stores.Launch.Inventory.Items((uint)inventoryBot.Pid).AsEnumerable();
                if (a.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String && q.GetString()!.Trim() is { Length: > 0 } needle)
                    inventory = inventory.Where(i => Stores.Launch.ItemDisplayName(i.Gid).Contains(needle, StringComparison.OrdinalIgnoreCase));
                if (Flag(a, "equippedOnly")) inventory = inventory.Where(i => i.Position is >= 0 and <= 16 or 28);   // équipement, monture et compagnon ; hors sac et altérations
                var matched = inventory.ToList();
                bool withEffects = Flag(a, "includeEffects");
                var stacks = new JsonArray();
                foreach (var item in matched.Take(500)) stacks.Add(new JsonObject { ["uid"] = item.Uid,
                    ["gid"] = item.Gid, ["name"] = Stores.Launch.ItemDisplayName(item.Gid), ["quantity"] = item.Qty, ["position"] = item.Position,
                    ["effects"] = withEffects ? EffectsJson(Stores.Launch, (uint)inventoryBot.Pid, item.Uid) : null,
                    ["rawEffects"] = withEffects ? RawEffectsJson(Stores.Launch, (uint)inventoryBot.Pid, item.Uid) : null });
                return new JsonObject { ["items"] = stacks, ["totalStacks"] = matched.Count, ["truncated"] = matched.Count > 500 };
            case "probe_navigation":
                var probeBot = ControlBot(a);
                if (probeBot.Pid <= 0) throw new ArgumentException("Bot is offline; navigation is unavailable.");
                var probeWorld = Stores.World.Get((uint)probeBot.Pid);
                var probeMap = probeWorld.MapId;
                var probe = GivreClone.Protocol.NavigationProbe.Evaluate(probeMap, probeWorld.CellsMap,
                    probeWorld.SelfCellMap, probeWorld.SelfCellConfirmed, probeWorld.SelfCell,
                    probeWorld.Cells.ToArray(), a.GetProperty("target").GetInt32(), probeBot.InFight,
                    probeWorld.CustomObstacleCells);
                if (probeMap != probeWorld.MapId)
                    throw new InvalidOperationException("Map changed during navigation probe; read state again.");
                return new JsonObject { ["mapId"] = probeMap, ["status"] = probe.Status,
                    ["cells"] = new JsonArray(probe.Cells.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                    ["steps"] = Math.Max(0, probe.Cells.Count - 1), ["walkableCells"] = probe.WalkableCells,
                    ["sent"] = false, ["dynamicOccupancyIncluded"] = false,
                    ["limitation"] = "Local geometry and script obstacles only; no server acceptance or live actor occupancy guarantee." };
            case "get_map_state":
                var mapBot = ControlBot(a);
                if (mapBot.Pid <= 0) throw new ArgumentException("Bot is offline; map is unavailable.");
                var world = Stores.World.Get((uint)mapBot.Pid);
                var actors = new JsonArray();
                foreach (var actor in world.Actors.Take(100)) actors.Add(new JsonObject { ["id"] = actor.ActorId,
                    ["name"] = actor.Name, ["kind"] = actor.Kind.ToString(), ["cell"] = actor.CellId,
                    ["npcId"] = actor.NpcId, ["groupLevel"] = actor.GroupLevel });
                var interactives = new JsonArray();
                foreach (var interactive in world.Interactives.Take(100))
                {
                    var skills = new JsonArray();
                    foreach (var skill in interactive.Skills) skills.Add(new JsonObject { ["skillId"] = skill.SkillId, ["enabled"] = skill.Enabled });
                    interactives.Add(new JsonObject { ["id"] = interactive.ElementId, ["cell"] = interactive.CellId,
                        ["name"] = interactive.Name, ["typeId"] = interactive.ElementTypeId, ["skills"] = skills });
                }
                return new JsonObject { ["mapId"] = world.MapId, ["cell"] = world.SelfCell,
                    ["cellConfirmed"] = world.SelfCellConfirmed, ["cellMap"] = world.SelfCellMap,
                    ["cellsReady"] = world.CellsReadyFor(world.MapId), ["actors"] = actors,
                    ["interactives"] = interactives, ["inFight"] = mapBot.InFight,
                    ["truncated"] = world.Actors.Count > 100 || world.Interactives.Count > 100 };
            case "get_app_status":
            {
                var log = TransportDiag.DiskStatus();
                return new JsonObject { ["app"] = "GivreWork", ["version"] = "source-local",
                    ["actionsEnabled"] = AppConfig.Current.McpAllowActions, ["localOnly"] = true,
                    ["transportLog"] = new JsonObject { ["initialized"] = log.Initialized,
                        ["workerAlive"] = log.WorkerAlive, ["file"] = log.File,
                        ["pendingLines"] = log.PendingLines, ["lastErrorType"] = log.LastErrorType,
                        ["lastFlushUtc"] = log.LastFlushUtc, ["bytesPerMinute"] = log.BytesPerMinute,
                        ["growthAlert"] = log.GrowthAlert },
                    ["refresh"] = RefreshCost(),
                    ["bots"] = _supDir?.Snapshot().Count ?? 0, ["runningSupervisors"] = _supRuntime?.RunningNames.Count ?? 0 };
            }
            case "get_bot_diagnostics": return ControlHealth(a);
            case "list_bots":
                _supDir?.Refresh();
                var bots = new JsonArray();
                foreach (var row in _accounts.Accounts)
                    if (_supDir?.ByAccountId(row.Model.Id) is { } bot) bots.Add(Snapshot(bot, row.Model.Id));
                return new JsonObject { ["bots"] = bots };
            case "get_bot_state":
                var identity = ControlIdentity(a);
                return Snapshot(identity.Snapshot, identity.Target.AccountId);
            case "read_forge_history":
                var forgeIdentity = ControlIdentity(a);
                int historyLimit = a.TryGetProperty("limit", out var forgeLimit) ? forgeLimit.GetInt32() : 200;
                if (historyLimit is < 1 or > 1000) throw new ArgumentException("History limit must be 1..1000.");
                await Stores.Launch.ForgeHistory.Ready.WaitAsync(ct);
                Stores.Launch.ReconcileSmithHistory();
                var forgeHistory = Stores.Launch.ForgeHistory.Read(accountId: forgeIdentity.Target.AccountId,
                    since: a.TryGetProperty("since", out var forgeSince) ? forgeSince.GetInt64() : 0,
                    limit: historyLimit);
                var historyJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var historyResult = JsonSerializer.SerializeToNode(forgeHistory, historyJsonOptions)!.AsObject();
                historyResult["objectives"] = JsonSerializer.SerializeToNode(
                    Stores.Launch.SmithObjectives.Read(forgeIdentity.Target.AccountId), historyJsonOptions);
                historyResult["budgetStorageError"] = Stores.Launch.SmithObjectives.StorageError;
                historyResult["persisted"] = Stores.Launch.ForgeHistory.HasArchive;
                return historyResult;
            case "start_smith_leveling":
            {
                var smithTarget = ControlWriteTarget(a);
                int jobId = a.GetProperty("jobId").GetInt32();
                int targetLevel = a.GetProperty("targetLevel").GetInt32();
                long cashCap = a.GetProperty("maxKamas").GetInt64();
                int runeCap = a.TryGetProperty("maxRunes", out var maxSmithRunes) ? maxSmithRunes.GetInt32() : 10000;
                if (targetLevel is < 1 or > 200 || cashCap <= 0 || runeCap is < 1 or > 10000)
                    throw new ArgumentException("Require targetLevel 1..200, positive maxKamas and maxRunes 1..10000.");
                if (SmithMagic.Busy) throw new InvalidOperationException("A smith session is already running or starting.");
                var smithBot = Bots.FirstOrDefault(b => b.Row.Model.Id == smithTarget.AccountId && b.Pid == smithTarget.Pid);
                if (smithBot is null || smithBot.Pid <= 0) throw new InvalidOperationException("Selected bot is not connected.");
                SelectedBot = smithBot;
                SmithMagic.AttachPid(smithBot.Pid);
                var smithJob = SmithMagic.JobChoices.FirstOrDefault(j => j.JobId == jobId)
                    ?? throw new ArgumentException("The chosen smith job has not been observed for this bot.");
                SmithMagic.Leveling = true;
                SmithMagic.AdaptiveLeveling = true;
                SmithMagic.SelectedJob = smithJob;
                SmithMagic.TargetJobLevel = targetLevel;
                SmithMagic.MaxKamas = SmithMagic.BuyMaxKamas = cashCap;
                SmithMagic.MaxRunes = runeCap;
                if (!SmithMagic.CanStart) throw new InvalidOperationException("Smith leveling admission refused.");
                SmithMagic.StartCommand.Execute(null);
                string smithStatus = SmithMagic.Running ? "running"
                    : SmithMagic.StartCommand.IsRunning ? "starting" : "not-started";
                return new JsonObject { ["status"] = smithStatus, ["accountId"] = smithTarget.AccountId,
                    ["jobId"] = jobId, ["targetLevel"] = targetLevel, ["maxKamas"] = cashCap, ["maxRunes"] = runeCap,
                    ["running"] = SmithMagic.Running, ["detail"] = SmithMagic.Status };
            }
            case "stop_smith_leveling":
            {
                var smithTarget = ControlWriteTarget(a);
                if (SelectedBot?.Row.Model.Id != smithTarget.AccountId || SelectedBot.Pid != smithTarget.Pid)
                    throw new InvalidOperationException("The requested bot does not own the selected smith session.");
                SmithMagic.StopCommand.Execute(null);
                return new JsonObject { ["status"] = "stop requested", ["accountId"] = smithTarget.AccountId };
            }
            case "read_bot_console":
                return ControlJson.Console(ControlJournal(a).Read(a.TryGetProperty("since", out var since) ? since.GetInt64() : 0,
                    a.TryGetProperty("limit", out var limit) ? limit.GetInt32() : 100, Text(a, "pattern"), Text(a, "level")));
            case "validate_lua_script":
                string? syntax = SavedLuaScripts.SyntaxError(Required(a, "code"));
                var lintJson = new JsonArray();
                if (syntax is null)
                    foreach (var d in GivreClone.Engine.LuaTooling.LuaLinter.Lint(Required(a, "code"), GivreClone.App.Services.LuaEditor.LuaLanguage.Catalog).Take(100))
                        lintJson.Add(new JsonObject { ["line"] = d.Line, ["column"] = d.Column, ["severity"] = d.Severity.ToString().ToLowerInvariant(),
                            ["code"] = d.Code, ["message"] = d.Message, ["hint"] = d.Hint });
                // syntaxOnly reste vrai pour la VALIDITÉ (aucun code exécuté) ; « lint » ajoute l'analyse statique de l'éditeur
                // (API inconnues/indisponibles, fonctions absentes du bac à sable) : un script valide peut quand même échouer à l'exécution.
                return new JsonObject { ["valid"] = syntax is null, ["syntaxOnly"] = true, ["error"] = syntax, ["lint"] = lintJson };
            case "list_saved_scripts":
                var names = new JsonArray(); foreach (var name in ControlScripts.List()) names.Add(name);
                return new JsonObject { ["scripts"] = names };
            case "read_saved_script":
                return new JsonObject { ["name"] = Required(a, "name"), ["code"] = ControlScripts.Read(Required(a, "name")) };
            case "save_script":
                ControlScripts.Save(Required(a, "name"), Required(a, "code"), a.TryGetProperty("overwrite", out var overwrite) && overwrite.GetBoolean());
                return new JsonObject { ["ok"] = true, ["name"] = Required(a, "name") };
            case "patch_script":
                ControlScripts.Patch(Required(a, "name"), Required(a, "oldText"), Text(a, "newText"));
                return new JsonObject { ["ok"] = true, ["name"] = Required(a, "name") };
            case "run_saved_script":
            case "run_lua_script":
                string code = request.Tool == "run_saved_script" ? ControlScripts.Read(Required(a, "name")) : Required(a, "code");
                if (SavedLuaScripts.SyntaxError(code) is { } problem) throw new ArgumentException(problem);
                if (orders is null) throw new InvalidOperationException("Bot directory is not ready.");
                return ControlResult(await orders.StartScriptAsync(Required(a, "bot"), code + "\n", ct));
            case "stop_script":
                return ControlResult(await orders!.StopScriptAsync(Required(a, "bot"), ct));
            case "travel_to":
            {
                // Trajet vers une carte (coordonnées x,y ou id de carte) : lance un mini-script « travelTo » sur le bot. Refusé si un script tourne déjà sur ce bot.
                if (orders is null) throw new InvalidOperationException("Bot directory is not ready.");
                bool hasMap = a.TryGetProperty("mapId", out var tm) && tm.ValueKind == JsonValueKind.Number && tm.GetInt64() > 0;
                string target;
                if (hasMap) target = tm.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (a.TryGetProperty("x", out var tx) && tx.ValueKind == JsonValueKind.Number && a.TryGetProperty("y", out var ty) && ty.ValueKind == JsonValueKind.Number)
                    target = Math.Clamp(tx.GetInt32(), -1000, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Math.Clamp(ty.GetInt32(), -1000, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else throw new ArgumentException("Give mapId, or x and y.");
                string travel = "function move()\n  if not travelTo(" + target + ") then print(\"Trajet impossible\") end\n  return false\nend\n";
                return await ControlTravelAsync(a, travel, ct);
            }
            case "zaapi_to":
            {
                // Zaapi de la carte courante vers mapId. Le script appelle zaapi.go : succès seulement si la carte change.
                if (orders is null) throw new InvalidOperationException("Bot directory is not ready.");
                if (!a.TryGetProperty("mapId", out var zaapiMap) || zaapiMap.ValueKind != JsonValueKind.Number
                    || !zaapiMap.TryGetInt64(out long zaapiMapId) || zaapiMapId <= 0)
                    throw new ArgumentException("mapId must be the destination map id.");
                string zaapi = "function move()\n  if not zaapi.go(" + zaapiMapId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") then print(\"Zaapi impossible\") end\n  return false\nend\n";
                return await ControlTravelAsync(a, zaapi, ct);
            }
            case "connect_bot":
                // mode = « socket » (sans fenêtre de jeu) ou « mitm » (client Dofus piloté) : appliqué avant la connexion, retenu pour ce compte. Ignoré si le compte est déjà en ligne.
                if (Text(a, "mode") is { Length: > 0 } wantedMode && _accounts.Accounts.FirstOrDefault(r => string.Equals(r.Model.Id?.ToString(), Required(a, "bot"), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Label, Required(a, "bot"), StringComparison.OrdinalIgnoreCase) || string.Equals(r.Login, Required(a, "bot"), StringComparison.OrdinalIgnoreCase)) is { Running: false } modeRow)
                    modeRow.IsSocketMode = string.Equals(wantedMode, "socket", StringComparison.OrdinalIgnoreCase);
                return ControlResult(await orders!.ConnectAsync(Required(a, "bot"), Text(a, "character"), Text(a, "server"), ct));
            case "disconnect_bot": return ControlResult(await orders!.DisconnectAsync(Required(a, "bot"), ct));
            case "reconnect_bot": return ControlResult(await orders!.ReconnectAsync(Required(a, "bot"), ct));
            case "list_supervisors":
                var supervisors = new JsonArray();
                foreach (var s in Supervisors) supervisors.Add(new JsonObject { ["name"] = s.Name, ["running"] = s.Running, ["problem"] = s.ProblemText });
                return new JsonObject { ["supervisors"] = supervisors };
            case "get_supervisor_template":
                var template = SupervisorTemplates.Get(Required(a, "id")) ?? throw new ArgumentException("Unknown supervisor template.");
                return new JsonObject { ["id"] = template.Id, ["title"] = template.Title, ["code"] = template.Code };
            case "run_supervisor":
                string wanted = Required(a, "name");
                var supervisor = SupervisorByName(wanted);
                string supervisorCode = Text(a, "code", supervisor?.Code ?? "");
                if (SavedLuaScripts.SyntaxError(supervisorCode) is { } supError) throw new ArgumentException(supError);
                if (supervisor?.Running == true) throw new InvalidOperationException("Supervisor already running; stop it before replacing its script.");
                if (_supRuntime is null) throw new InvalidOperationException("Supervisor runtime is not ready.");
                if (supervisor is null)
                {
                    var definition = SupervisorStore.Add(wanted, supervisorCode);
                    supervisor = new SupervisorViewModel(definition, _supRuntime, SupervisorStore.Save);
                    Supervisors.Add(supervisor);
                }
                else supervisor.Code = supervisorCode;
                supervisor.StartIfIdle();
                return new JsonObject { ["ok"] = supervisor.Running, ["name"] = supervisor.Name, ["reason"] = supervisor.ProblemText };
            case "stop_supervisor":
                string stopName = Required(a, "name");
                if (SupervisorByName(stopName) is null) throw new ArgumentException("Supervisor not found.");
                _supRuntime?.Stop(stopName, "mcp");
                return new JsonObject { ["ok"] = true, ["status"] = "stop requested" };
            case "list_teams": return await ControlTeamAsync("list", a, ct);
            case "manage_team":
                GuardTeamWrite(a);
                string teamAction = Required(a, "action");
                if (teamAction == "waitGathered")
                {
                    var teamArgs = JsonNode.Parse(a.GetRawText())!.AsObject();
                    int seconds = a.TryGetProperty("timeout", out var teamTimeout) ? teamTimeout.GetInt32() : 30;
                    teamArgs["timeout"] = Math.Clamp(seconds, 1, 50) * 1000;
                    using var teamDocument = JsonDocument.Parse(teamArgs.ToJsonString());
                    return await ControlTeamAsync(teamAction, teamDocument.RootElement.Clone(), ct);
                }
                return await ControlTeamAsync(teamAction, a, ct);
            case "emergency_stop":
                int stopped = OrderStopAllScripts();
                _supRuntime?.StopAll("mcp-emergency");
                return new JsonObject { ["ok"] = true, ["scriptsStopped"] = stopped, ["supervisorsStopRequested"] = true,
                    ["accountsDisconnected"] = false };
            case "session.transition": return await ControlSessionTransitionAsync(a, ct);
            case "session.transition.cancel": return await ControlSessionTransitionCancelAsync(a, ct);
            case "enhanced.activate": return await ControlEnhancedActivateAsync(a, ct);
            default: return await ControlGameAsync(request, orders, ct)
                          ?? await ControlExtraAsync(request, orders, ct)
                          ?? throw new ArgumentException("Unknown control tool.");
        }
    }

    private static JsonNode ControlApiSearch(JsonElement args)
    {
        var catalog = Services.LuaEditor.LuaLanguage.Catalog;
        string query = Required(args, "query");
        int max = args.TryGetProperty("max", out var maximum) ? Math.Clamp(maximum.GetInt32(), 1, 50) : 20;
        var matches = catalog.Entries.Where(e => e.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(max).ToArray();
        var entries = new JsonArray();
        foreach (var entry in matches) entries.Add(new JsonObject { ["signature"] = entry.Display,
            ["description"] = entry.Description, ["example"] = entry.Example, ["documentedStub"] = entry.IsStub });
        return new JsonObject { ["entries"] = entries, ["documentationOnly"] = true };
    }
}
