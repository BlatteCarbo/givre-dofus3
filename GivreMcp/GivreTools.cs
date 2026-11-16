using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using GivreControl;

namespace GivreMcp;

[McpServerToolType]
public sealed class GivreTools
{
    private static async Task<JsonElement> Call(string tool, object arguments, CancellationToken ct)
    {
        try { return await ControlPipeClient.CallAsync(tool, arguments, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new McpException("The local Givre control request timed out or was canceled by the bridge."); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GivreWork is not reachable", StringComparison.Ordinal))
        { throw new McpException("GivreWork is not reachable. Open GivreWork and enable its local MCP bridge."); }
        // Error strings from the application may contain script input, character data or transport diagnostics.
        // Do not relay arbitrary exception messages through the MCP process boundary.
        catch (InvalidOperationException) { throw new McpException("GivreWork rejected the request. Check the selected account, permissions, tool arguments and local diagnostics."); }
        catch (TimeoutException) { throw new McpException("The local Givre control request timed out."); }
        catch (IOException) { throw new McpException("The local Givre control pipe is unavailable or returned an invalid response."); }
        catch (UnauthorizedAccessException) { throw new McpException("Access to the local Givre control pipe was denied."); }
        catch (JsonException) { throw new McpException("The local Givre control bridge returned invalid JSON."); }
        catch (Exception) { throw new McpException("The local Givre control request failed."); }
    }

    [McpServerTool(Name = "get_app_status", ReadOnly = true), Description("Read local application status and transport-log health: worker, queued lines, last flush and error type.")]
    public Task<JsonElement> GetAppStatus(CancellationToken ct) => Call("get_app_status", new { }, ct);

    [McpServerTool(Name = "get_scripting_guide", ReadOnly = true), Description("Read the application's Lua scripting guide.")]
    public Task<JsonElement> GetScriptingGuide(CancellationToken ct) => Call("get_scripting_guide", new { }, ct);

    [McpServerTool(Name = "search_lua_api", ReadOnly = true), Description("Search the application's documented Lua API, returning at most max results.")]
    public Task<JsonElement> SearchLuaApi(string query, CancellationToken ct, int max = 20) => Call("search_lua_api", new { query, max }, ct);

    [McpServerTool(Name = "get_inventory", ReadOnly = true), Description("Read the selected bot's inventory snapshot. Optional: query (name fragment), equippedOnly, includeEffects (item effect lines as shown in the game tooltip).")]
    public Task<JsonElement> GetInventory(string bot, CancellationToken ct, string? query = null, bool equippedOnly = false, bool includeEffects = false)
        => Call("get_inventory", new { bot, query, equippedOnly, includeEffects }, ct);

    [McpServerTool(Name = "get_map_state", ReadOnly = true), Description("Read the selected bot's map snapshot.")]
    public Task<JsonElement> GetMapState(string bot, CancellationToken ct) => Call("get_map_state", new { bot }, ct);

    [McpServerTool(Name = "query_world_atlas", ReadOnly = true), Description("Query the local, read-only Dofus map atlas: capabilities, list_maps, inspect_map, map_transitions or find_path. Supply a bounded JSON request in requestJson. Local client cache only; partner DofusDB data are excluded unless separately authorized. No in-game movement or ACK.")]
    public Task<JsonElement> QueryWorldAtlas(string requestJson, CancellationToken ct)
        => Call("query_world_atlas", new { requestJson }, ct);

    [McpServerTool(Name = "query_profession_plan", ReadOnly = true), Description("Plan a profession training goal or inspect its material and crafting steps from a separately approved, pinned local dataset. Operations: capabilities, plan_job, materials, steps. No game control, no transactions, no network, and public DofusDB exports are excluded.")]
    public Task<JsonElement> QueryProfessionPlan(string requestJson, CancellationToken ct)
        => Call("query_profession_plan", new { requestJson }, ct);

    [McpServerTool(Name = "query_item_craft", ReadOnly = true), Description("Read-only universal item or equipment-set crafting plan from an explicitly AI-approved local source. Ops: capabilities, list_targets, plan_item, plan_set, materials, steps. No farm execution, item buying, gameplay or unlicensed DofusDB data.")]
    public Task<JsonElement> QueryItemCraft(string requestJson, CancellationToken ct)
        => Call("query_item_craft", new { requestJson }, ct);

    // These tools inspect only a local, independently SHA-checked cache snapshot.
    // They never call the Givre control pipe, launch a game or access DofusDB's
    // network API. Review-only disclosure requires explicit operator configuration.
    [McpServerTool(Name = "dofusdb_catalog_capabilities", ReadOnly = true), Description("Describe strictly read-only local DofusDB review-cache queries and limits. This never grants AI data access or certifies a game version.")]
    public Task<JsonElement> DofusDbCatalogCapabilities(CancellationToken ct)
        => LocalDofusDb("capabilities", ct);

    [McpServerTool(Name = "dofusdb_catalog_status", ReadOnly = true), Description("Report counts of locally SHA-verified DofusDB items, recipes and sets only with explicit operator API+AI+MCP review permission and a pinned summary hash. Game version is UNKNOWN.")]
    public Task<JsonElement> DofusDbCatalogStatus(CancellationToken ct)
        => LocalDofusDb("catalog_status", ct);

    [McpServerTool(Name = "dofusdb_search_items", ReadOnly = true), Description("Search French/English item names in the SHA-pinned local DofusDB review cache, max 30 results per page. No prices, gameplay, network calls or inferred game version.")]
    public Task<JsonElement> DofusDbSearchItems(string query, CancellationToken ct, int offset = 0, int limit = 20)
        => LocalDofusDb("search_items", ct, query: query, offset: offset, limit: limit);

    [McpServerTool(Name = "dofusdb_get_item", ReadOnly = true), Description("Look up an item by positive item GID in the local pinned DofusDB cache. The live game version, XP, market price and drop rate remain unknown.")]
    public Task<JsonElement> DofusDbGetItem(long itemId, CancellationToken ct)
        => LocalDofusDb("get_item", ct, id: itemId);

    [McpServerTool(Name = "dofusdb_get_recipe", ReadOnly = true), Description("Read recipe ingredient IDs/quantities for a result item GID from the pinned local DofusDB review cache. Yield, XP, prices and map routes remain UNKNOWN.")]
    public Task<JsonElement> DofusDbGetRecipe(long resultItemId, CancellationToken ct)
        => LocalDofusDb("get_recipe", ct, id: resultItemId);

    [McpServerTool(Name = "dofusdb_get_set", ReadOnly = true), Description("Read a DofusDB equipment set's source-listed item IDs from a local reviewed cache. No inferred membership or game-version certification.")]
    public Task<JsonElement> DofusDbGetSet(long setId, CancellationToken ct)
        => LocalDofusDb("get_set", ct, id: setId);

    [McpServerTool(Name = "dofusdb_list_sets", ReadOnly = true), Description("Browse locally hash-pinned DofusDB equipment sets with bounded pagination. Explicit operator review scope is required.")]
    public Task<JsonElement> DofusDbListSets(CancellationToken ct, int offset = 0, int limit = 20)
        => LocalDofusDb("list_sets", ct, offset: offset, limit: limit);

    private static Task<JsonElement> LocalDofusDb(string operation, CancellationToken ct,
        string? query = null, long id = 0, int offset = 0, int limit = 20)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(DofusDbCatalogReadTools.Query(operation,
            DofusDbCatalogAccess.FromEnvironment(), query, id, offset, limit));
    }

    [McpServerTool(Name = "list_bots", ReadOnly = true), Description("List configured bots.")]
    public Task<JsonElement> ListBots(CancellationToken ct) => Call("list_bots", new { }, ct);

    [McpServerTool(Name = "get_bot_state", ReadOnly = true), Description("Read a bot's observed state, exact account ID even while offline, and detailed connectionStatus. An accepted command does not confirm completion; inspect this state and read_bot_console afterwards.")]
    public Task<JsonElement> GetBotState(string bot, CancellationToken ct) => Call("get_bot_state", new { bot }, ct);

    [McpServerTool(Name = "read_bot_console", ReadOnly = true), Description("Read console entries after a cursor, optionally filtering by pattern and level.")]
    public Task<JsonElement> ReadBotConsole(string bot, CancellationToken ct, long since = 0, int limit = 100, string pattern = "", string level = "") => Call("read_bot_console", new { bot, since, limit, pattern, level }, ct);

    [McpServerTool(Name = "read_forge_history", ReadOnly = true), Description("Read measured forge sessions and events for an exact bot account after a cursor, including the persistent archive and objective budgets. Objectives preserve their original cap, wallet floor, confirmed expenses and unresolved reservations across restarts. Missing opening expenses remain null and block new paid spending. Includes support UID/GID, runes, outcome codes, stats, pool and observed job XP/levels. Unknown XP stays null. Limit 1..1000.")]
    public Task<JsonElement> ReadForgeHistory(string bot, CancellationToken ct, long since = 0, int limit = 200)
        => Call("read_forge_history", new { bot, since, limit }, ct);

    [McpServerTool(Name = "start_smith_leveling"), Description("Start bounded adaptive smith leveling for one explicitly selected connected bot and observed smith job. maxKamas caps total cash expenditure; maxRunes is 1..10000. Returns acceptance only, not completion. Track read_forge_history and stop_smith_leveling.")]
    public Task<JsonElement> StartSmithLeveling(string bot, int jobId, int targetLevel, long maxKamas, CancellationToken ct, int maxRunes = 10000)
        => Call("start_smith_leveling", new { bot, jobId, targetLevel, maxKamas, maxRunes }, ct);

    [McpServerTool(Name = "stop_smith_leveling"), Description("Request cancellation of the selected bot's smith session. Refuses a different selected account; verify final stop with read_forge_history.")]
    public Task<JsonElement> StopSmithLeveling(string bot, CancellationToken ct)
        => Call("stop_smith_leveling", new { bot }, ct);

    [McpServerTool(Name = "wait_console", ReadOnly = true), Description("Wait for a matching console entry. Timeout is in seconds; cancellation interrupts the wait.")]
    public Task<JsonElement> WaitConsole(string bot, string pattern, CancellationToken ct, long since = 0, int timeout = 30) => Call("wait_console", new { bot, pattern, since, timeout }, ct);

    [McpServerTool(Name = "validate_lua_script", ReadOnly = true), Description("Validate Lua source without executing it.")]
    public Task<JsonElement> ValidateLuaScript(string code, CancellationToken ct) => Call("validate_lua_script", new { code }, ct);

    [McpServerTool(Name = "list_saved_scripts", ReadOnly = true), Description("List saved scripts in the application's script store.")]
    public Task<JsonElement> ListSavedScripts(CancellationToken ct) => Call("list_saved_scripts", new { }, ct);

    [McpServerTool(Name = "read_saved_script", ReadOnly = true), Description("Read a named saved script.")]
    public Task<JsonElement> ReadSavedScript(string name, CancellationToken ct) => Call("read_saved_script", new { name }, ct);

    [McpServerTool(Name = "save_script"), Description("Save Lua source in the application's script store; overwrite must be explicitly enabled to replace a script.")]
    public Task<JsonElement> SaveScript(string name, string code, CancellationToken ct, bool overwrite = false) => Call("save_script", new { name, code, overwrite }, ct);

    [McpServerTool(Name = "patch_script"), Description("Replace oldText with newText in a named saved script.")]
    public Task<JsonElement> PatchScript(string name, string oldText, string newText, CancellationToken ct) => Call("patch_script", new { name, oldText, newText }, ct);

    [McpServerTool(Name = "run_saved_script"), Description("Request a saved script run on the selected bot. Reports acceptance or refusal, not game-server confirmation.")]
    public Task<JsonElement> RunSavedScript(string bot, string name, CancellationToken ct) => Call("run_saved_script", new { bot, name }, ct);

    [McpServerTool(Name = "run_lua_script"), Description("Request a Lua script run on the selected bot. Reports acceptance or refusal, not game-server confirmation.")]
    public Task<JsonElement> RunLuaScript(string bot, string code, CancellationToken ct) => Call("run_lua_script", new { bot, code }, ct);

    [McpServerTool(Name = "stop_script"), Description("Request the selected bot's script stop. Reports acceptance or refusal, not game-server confirmation.")]
    public Task<JsonElement> StopScript(string bot, CancellationToken ct) => Call("stop_script", new { bot }, ct);

    [McpServerTool(Name = "travel_to"), Description("Send the bot to a map: give mapId, or x and y (map coordinates). Starts a one-step travelTo script on the bot; refused if a script is already running there. Reports acceptance, not arrival: watch read_bot_console.")]
    public Task<JsonElement> TravelTo(string bot, CancellationToken ct, int x = 0, int y = 0, long mapId = 0, bool useXy = false)
        => Call("travel_to", mapId > 0 ? new { bot, mapId } : (object)(useXy ? new { bot, x, y } : new { bot }), ct);

    [McpServerTool(Name = "list_zaapi_destinations", ReadOnly = true), Description("Read the last zaapi list (TeleportDestinationsEvent type 1): name, district, coordinates, announced kama cost and tab category for the maps the server sent. Empty until a zaapi has been opened. No game action.")]
    public Task<JsonElement> ListZaapiDestinations(string bot, CancellationToken ct) => Call("list_zaapi_destinations", new { bot }, ct);

    [McpServerTool(Name = "zaapi_to"), Description("Use the zaapi on the bot's current map to teleport to mapId. Starts a one-step zaapi.go script; refused if a script is already running. Reports acceptance, not arrival: the trip succeeds only when the observed map changes. Watch read_bot_console for the announced cost.")]
    public Task<JsonElement> ZaapiTo(string bot, long mapId, CancellationToken ct) => Call("zaapi_to", new { bot, mapId }, ct);

    [McpServerTool(Name = "connect_bot"), Description("Connect a bot, optionally selecting a character and server. mode = 'socket' (no game window) or 'mitm' (driven Dofus client) is applied before connecting when the account is offline.")]
    public Task<JsonElement> ConnectBot(string bot, CancellationToken ct, string character = "", string server = "", string mode = "") => Call("connect_bot", new { bot, character, server, mode }, ct);

    [McpServerTool(Name = "find_capturable_monsters", ReadOnly = true), Description("Monsters with a capturable soul in the game data (archmonsters, soul bosses), filtered by name text. Use the ids with make_soul_script.")]
    public Task<JsonElement> FindCapturableMonsters(CancellationToken ct, string search = "", int limit = 30) => Call("find_capturable_monsters", new { search, limit }, ct);

    [McpServerTool(Name = "make_soul_script"), Description("Generate a soul-capture farming Lua script for a list of monster ids (from find_capturable_monsters). It only attacks groups containing one of them, and only casts 'Capture d'âmes' when a strong-enough soul stone is equipped, the spell is off cooldown (3 turns) and the fight is expected to end within the 2-turn state (damage measured during the fight, 25% margin). The stone takes the weapon slot. save=true stores it as a saved script (default name 'Capture d'âmes'). Rules come from public sources and the game data: verify in game before leaving it unattended.")]
    public Task<JsonElement> MakeSoulScript(int[] monsters, CancellationToken ct, int maxPodsPercent = 90, bool save = false, string name = "") => Call("make_soul_script", new { monsters, maxPodsPercent, save, name }, ct);

    [McpServerTool(Name = "make_gather_script"), Description("Generate a ready-to-run Lua gathering script for a resource (name or item id): a route of the nearest rich maps (from the bot's map 'bot', or (x, y)), then loops. save=true stores it under the saved scripts (default name 'Récolte <resource>', overwritten if present) so run_saved_script can start it. Uses recorded gathering points, so check the route before running unattended.")]
    public Task<JsonElement> MakeGatherScript(string resource, CancellationToken ct, string bot = "", int maps = 6, int maxPodsPercent = 90, bool includeIndoor = false, bool save = false, string name = "")
        => Call("make_gather_script", new { resource, bot, maps, maxPodsPercent, includeIndoor, save, name }, ct);

    [McpServerTool(Name = "find_resource_maps", ReadOnly = true), Description("Where to gather a resource (86 gathering resources: wood, ore, plants, crops, fish, water). Without 'resource' it lists them (filter with 'search' text and 'job': 2 woodcutter, 24 miner, 26 alchemist, 28 farmer, 36 fisherman, 1 water). With 'resource' (name or item id) it returns the maps holding it, nearest first from the bot's map ('bot') or from (x, y), otherwise richest first. Offline data from DofusDB surveys; counts are gathering points recorded, not live availability.")]
    public Task<JsonElement> FindResourceMaps(CancellationToken ct, string resource = "", string search = "", int job = 0, string bot = "", int x = 0, int y = 0, bool useXy = false, int limit = 10, bool includeIndoor = true)
        => Call("find_resource_maps", useXy ? new { resource, search, job = job == 0 ? (int?)null : job, bot, x, y, limit, includeIndoor } : new { resource, search, job = job == 0 ? (int?)null : job, bot, x = (int?)null, y = (int?)null, limit, includeIndoor }, ct);

    [McpServerTool(Name = "plan_job_xp", ReadOnly = true), Description("Offline plan to level a profession: at every level the best recipe, how many crafts, the XP and the ingredient shopping list. job = name or number; mode 'cheapest' (lowest cost per XP, uses HDV prices already observed by 'bot' or entered by hand) or 'fastest' (most XP per craft); xpBonusPercent = 100 by default. Estimate only.")]
    public Task<JsonElement> PlanJobXp(string job, CancellationToken ct, int fromLevel = 1, int toLevel = 100, string mode = "cheapest", string bot = "", int xpBonusPercent = 100) => Call("plan_job_xp", new { job, fromLevel, toLevel, mode, bot, xpBonusPercent }, ct);

    [McpServerTool(Name = "get_events", ReadOnly = true), Description("Phone alerts: bot died, pods full, disconnected, lost a fight, stuck, archmonster (or capturable-soul monster) appeared on a bot's map, subscription ending. Returns events newer than 'since' (0 = the latest ones) and the latest id; waitSec (max 25) long-polls until the next event. The same alerts are posted to the character's Discord webhook.")]
    public Task<JsonElement> GetEvents(CancellationToken ct, long since = 0, int waitSec = 0) => Call("get_events", new { since, waitSec }, ct);

    [McpServerTool(Name = "plan_production_chain", ReadOnly = true), Description("Production chain for one item (gid or query): what to GATHER (with the best map coordinates near the bot), what to BUY at the HDV, what to CRAFT, and the sale margin after tax. Read-only estimate from observed/manual prices; then chain make_gather_script, travel_to and run_lua_script yourself.")]
    public Task<JsonElement> PlanProductionChain(CancellationToken ct, string query = "", long gid = 0, int quantity = 1, string bot = "") => Call("plan_production_chain", new { query, gid, quantity, bot }, ct);

    [McpServerTool(Name = "recommend_zones", ReadOnly = true), Description("Progression assistant: fighting sub-areas suited to a character level (monsters around that level up to +25%), with map counts and monster kinds. Give 'level' or a connected 'bot'. Estimate from game data; then use list_expedition_zones(subArea) and start_expedition.")]
    public Task<JsonElement> RecommendZones(CancellationToken ct, int level = 0, string bot = "", int limit = 10, bool includeHard = false) => Call("recommend_zones", new { level, bot, limit, includeHard }, ct);

    [McpServerTool(Name = "advise_rune", ReadOnly = true), Description("Forgemagie advisor (offline, no game action): next rune to apply. linesJson = JSON array of {stat,current,min,max,target,cap?,paFrom?,raFrom?}; stockJson = JSON object like {\"Vitalité\":{\"Ba\":3,\"Pa\":2,\"Ra\":1}}. Rune gains are guide values (Vitalité 5/15/50, others 1/3/10) to verify in game; the carried-over residue (reliquat) is not modelled.")]
    public Task<JsonElement> AdviseRune(string linesJson, string stockJson, CancellationToken ct, bool allowPa = true, bool allowRa = true, bool allowOver = false) => Call("advise_rune", new { linesJson, stockJson, allowPa, allowRa, allowOver }, ct);

    [McpServerTool(Name = "set_spell_variant"), Description("Switch a spell to its other variant (e.g. Stalagmite / Onde celeste), OUT of combat only. Give spellId = the variant to activate; spellId = 0 lists the character's spells that have a variant (id, name, other variant).")]
    public Task<JsonElement> SetSpellVariant(string bot, CancellationToken ct, int spellId = 0) => Call("set_spell_variant", new { bot, spellId }, ct);

    [McpServerTool(Name = "estimate_spell_damage", ReadOnly = true), Description("Estimated damage of one spell (spellId) or of the whole combat rotation of a bot: spell dice + the character's characteristics (strength/intelligence/chance/agility, % damage, flat damage, crit) against an optional monster (monsterId, level = grade to use: resistances and life). Estimate only: ignores spell-specific modifiers, power and special effects. Reconnect the character if characteristicsKnown is false.")]
    public Task<JsonElement> EstimateSpellDamage(string bot, CancellationToken ct, int spellId = 0, int monsterId = 0, int level = 0) => Call("estimate_spell_damage", new { bot, spellId, monsterId, level }, ct);

    [McpServerTool(Name = "read_chat", ReadOnly = true), Description("A bot's chat log (all channels plus sent private messages), newer than 'since' (cursor from the previous call). channel = 'mp', 'all' or a channel number. Only channel 10 (private) is identified for sure.")]
    public Task<JsonElement> ReadChat(string bot, CancellationToken ct, long since = 0, int limit = 100, string channel = "all") => Call("read_chat", new { bot, since, limit, channel }, ct);

    [McpServerTool(Name = "send_private_message"), Description("Send a private message (MP) from a bot to a player. Actions must be enabled; the target is max 40 characters, the text max 250.")]
    public Task<JsonElement> SendPrivateMessage(string bot, string target, string text, CancellationToken ct) => Call("send_private_message", new { bot, target, text }, ct);

    [McpServerTool(Name = "list_combat_journals", ReadOnly = true), Description("Combat replay: the latest fights of all bots (or one 'bot') with outcome, duration, turns, casts, problems, longest pause and the first automatic findings (why a defeat, where time was lost). lostOnly=true keeps defeats. Works in MITM and Socket. Use get_combat_journal(id) for the full timeline of decisions.")]
    public Task<JsonElement> ListCombatJournals(CancellationToken ct, string bot = "", bool lostOnly = false, int limit = 20) => Call("list_combat_journals", new { bot, lostOnly, limit }, ct);

    [McpServerTool(Name = "get_combat_journal", ReadOnly = true), Description("Full replay of one fight (id from list_combat_journals): summary, findings, per-turn stats (time, casts, moves, HP) and the timeline of every AI decision with timestamps.")]
    public Task<JsonElement> GetCombatJournal(string id, CancellationToken ct, int maxChars = 40000) => Call("get_combat_journal", new { id, maxChars }, ct);

    [McpServerTool(Name = "make_treasure_script"), Description("Generate an automatic treasure-hunt Lua script: starts a hunt at the chest of the map where it is launched, resolves every clue with DofusDB (treasure.findClues), travels, plants the flag, digs, fights the final fight and returns to the chest. Gives up cleanly after 'maxFails' failures on a clue; hunters (phorreur) steps walk map by map. save=true stores it as a saved script (default name 'Chasse au trésor').")]
    public Task<JsonElement> MakeTreasureScript(CancellationToken ct, int level = 0, int maxFails = 3, bool save = false, string name = "") => Call("make_treasure_script", new { level, maxFails, save, name }, ct);

    [McpServerTool(Name = "find_treasure_clue", ReadOnly = true), Description("Treasure hunt: from map (x, y) in a direction (right|bottom|left|top), find the map that holds a clue. Pass the clue name as shown in the game; leave it empty to list every possible clue in that direction. The first map returned is the nearest one. Data comes from the public DofusDB API (only the position and direction are sent) and is cached on disk.")]
    public Task<JsonElement> FindTreasureClue(int x, int y, string direction, CancellationToken ct, string clue = "") => Call("find_treasure_clue", new { x, y, direction, clue }, ct);

    [McpServerTool(Name = "create_character"), Description("Create a character (Socket mode accounts only) on a server, then enter the game with it. 'bot' = account id or label from list_accounts (offline accounts included). server = server name or number; breed = French class name or number; sex = m|f; name empty = random server-proposed name; head/body = 1-based rank among the creation choices (0/omitted = random); colors = {skin,hair,outfit1..outfit4: '#RRGGBB'|number|'default'}. Reports acceptance, not game-server confirmation: check read_log.")]
    public Task<JsonElement> CreateCharacter(string bot, string server, string breed, string sex, CancellationToken ct, string name = "", int head = 0, int body = 0, Dictionary<string, string>? colors = null, bool leaveTutorial = true) => Call("create_character", new { bot, server, breed, sex, name, head, body, colors, leaveTutorial }, ct);

    [McpServerTool(Name = "delete_character", Destructive = true), Description("IRREVERSIBLY delete a character (Socket mode accounts WITH a secret code only; accounts without one are not supported yet). Pass confirm=true only after the user explicitly asked to delete this exact character. 'answer' is the secret-question answer (case-sensitive) and is never stored or logged.")]
    public Task<JsonElement> DeleteCharacter(string bot, string server, string character, string answer, bool confirm, CancellationToken ct) => Call("delete_character", new { bot, server, character, answer, confirm }, ct);

    [McpServerTool(Name = "disconnect_bot"), Description("Disconnect the selected bot.")]
    public Task<JsonElement> DisconnectBot(string bot, CancellationToken ct) => Call("disconnect_bot", new { bot }, ct);

    [McpServerTool(Name = "reconnect_bot"), Description("Reconnect the selected bot.")]
    public Task<JsonElement> ReconnectBot(string bot, CancellationToken ct) => Call("reconnect_bot", new { bot }, ct);

    [McpServerTool(Name = "list_supervisors", ReadOnly = true), Description("List supervisors and their state.")]
    public Task<JsonElement> ListSupervisors(CancellationToken ct) => Call("list_supervisors", new { }, ct);

    [McpServerTool(Name = "run_supervisor"), Description("Request a named supervisor run, optionally supplying Lua source. Reports acceptance or refusal, not game-server confirmation.")]
    public Task<JsonElement> RunSupervisor(string name, CancellationToken ct, string code = "") => Call("run_supervisor", new { name, code }, ct);

    [McpServerTool(Name = "stop_supervisor"), Description("Request a named supervisor stop. Reports acceptance or refusal, not game-server confirmation.")]
    public Task<JsonElement> StopSupervisor(string name, CancellationToken ct) => Call("stop_supervisor", new { name }, ct);

    [McpServerTool(Name = "get_supervisor_template", ReadOnly = true), Description("Read a supervisor template by identifier.")]
    public Task<JsonElement> GetSupervisorTemplate(string id, CancellationToken ct) => Call("get_supervisor_template", new { id }, ct);

    [McpServerTool(Name = "list_teams", ReadOnly = true), Description("List configured teams.")]
    public Task<JsonElement> ListTeams(CancellationToken ct) => Call("list_teams", new { }, ct);

    [McpServerTool(Name = "manage_team"), Description("Apply an application-supported team action. Omitted members are null; timeout is in seconds.")]
    public Task<JsonElement> ManageTeam(string action, string name, CancellationToken ct, string[]? members = null, string leader = "", string script = "", bool enabled = true, int timeout = 30) => Call("manage_team", new { action, name, members, leader, script, enabled, timeout }, ct);

    [McpServerTool(Name = "probe_navigation", ReadOnly = true), Description("Diagnose a route to a cell without moving. Returns stale geometry/position reasons or a local path. Excludes live actor occupancy and does not prove server acceptance.")]
    public Task<JsonElement> ProbeNavigation(string bot, int target, CancellationToken ct) => Call("probe_navigation", new { bot, target }, ct);

    [McpServerTool(Name = "list_accounts", ReadOnly = true), Description("List configured accounts with their id, label, character, pid and a 'blocked' flag (protected accounts are never connected or driven). Pass an account id as 'bot' to target an account unambiguously when two share a display name.")]
    public Task<JsonElement> ListAccounts(CancellationToken ct) => Call("list_accounts", new { }, ct);

    [McpServerTool(Name = "read_log", ReadOnly = true), Description("Read the tail of this instance's currently open transport log by default; 'file' selects a historical transport log in its logs folder. Optional regex 'pattern'. tail is capped at 2000 lines. Open-file size/timestamps reported by Windows may be stale; do not infer an empty log from them.")]
    public Task<JsonElement> ReadLog(CancellationToken ct, string file = "", int tail = 200, string pattern = "") => Call("read_log", new { file, tail, pattern }, ct);

    [McpServerTool(Name = "fight_metrics", ReadOnly = true), Description("Measure combat speed from the transport log: per fight, the wait in ms between the end of my turn and the start of my next turn (monster turns included), plus median/max and the share under 400 ms.")]
    public Task<JsonElement> FightMetrics(CancellationToken ct, string file = "", int last = 20) => Call("fight_metrics", new { file, last }, ct);

    [McpServerTool(Name = "get_combat_profile", ReadOnly = true), Description("Read a bot's saved combat profile (speed, style, target, turbo options, spell rotation settings).")]
    public Task<JsonElement> GetCombatProfile(string bot, CancellationToken ct) => Call("get_combat_profile", new { bot }, ct);

    [McpServerTool(Name = "set_combat_profile"), Description("Change fields of a bot's combat profile. 'changes' is an object of property name to value (bool, number, string or enum name). Unknown or non-simple fields are reported as ignored. Saved and pushed to the engine immediately.")]
    public Task<JsonElement> SetCombatProfile(string bot, Dictionary<string, JsonElement> changes, CancellationToken ct) => Call("set_combat_profile", new { bot, changes }, ct);

    [McpServerTool(Name = "list_expedition_zones", ReadOnly = true), Description("Browse zones for expeditions: no argument lists areas; 'area' (id or name part) lists its sub-areas with monster counts; 'subArea' (id or name part) lists its monsters and map count.")]
    public Task<JsonElement> ListExpeditionZones(CancellationToken ct, string area = "", string subArea = "") => Call("list_expedition_zones", new { area, subArea }, ct);

    [McpServerTool(Name = "start_expedition"), Description("Start an expedition on a bot: visits every map of a sub-area (nearest first) and fights groups matching the filters. mandatory/forbidden are monster ids; group size is 1..8; 0 means unlimited for maxFights/maxMinutes; stopPods is a percentage (0 = off). continueAfterDeath=true keeps going after a defeat (the character comes straight back; there is no Phoenix any more); false (default) stops at the first defeat. Reports acceptance or refusal.")]
    public Task<JsonElement> StartExpedition(string bot, int subArea, CancellationToken ct, long[]? mandatory = null, long[]? forbidden = null, int minMonsters = 1, int maxMonsters = 8, int maxFights = 0, int maxMinutes = 0, int stopPods = 95, bool continueAfterDeath = false)
        => Call("start_expedition", new { bot, subArea, mandatory = mandatory ?? Array.Empty<long>(), forbidden = forbidden ?? Array.Empty<long>(), minMonsters, maxMonsters, maxFights, maxMinutes, stopPods, continueAfterDeath }, ct);

    [McpServerTool(Name = "get_settings", ReadOnly = true), Description("Read the application's non-sensitive settings (tokens, licences, proxies and credentials are omitted).")]
    public Task<JsonElement> GetSettings(CancellationToken ct) => Call("get_settings", new { }, ct);

    [McpServerTool(Name = "get_character_info", ReadOnly = true), Description("Read observed character information, spells and jobs. Unknown server values are null.")]
    public Task<JsonElement> GetCharacterInfo(string bot, CancellationToken ct) => Call("get_character_info", new { bot }, ct);

    [McpServerTool(Name = "get_bank", ReadOnly = true), Description("Read cached bank stacks, with a bounded offset/limit. A bank never opened in this session is marked unknown. includeEffects adds item effect lines.")]
    public Task<JsonElement> GetBank(string bot, CancellationToken ct, int offset = 0, int limit = 100, bool includeEffects = false)
        => Call("get_bank", new { bot, offset, limit, includeEffects }, ct);

    [McpServerTool(Name = "search_items", ReadOnly = true), Description("Search the locally loaded item catalogue by name with bounded pagination.")]
    public Task<JsonElement> SearchItems(string query, CancellationToken ct, int offset = 0, int limit = 20) => Call("search_items", new { query, offset, limit }, ct);

    [McpServerTool(Name = "get_item_info", ReadOnly = true), Description("Read local catalogue information for an item GID. Unknown item fields are null.")]
    public Task<JsonElement> GetItemInfo(long gid, CancellationToken ct) => Call("get_item_info", new { gid }, ct);

    [McpServerTool(Name = "find_places", ReadOnly = true), Description("Find nearby known map hints such as banks, markets or zaaps. Requires a known bot position and local game data.")]
    public Task<JsonElement> FindPlaces(string bot, string query, CancellationToken ct, int limit = 10) => Call("find_places", new { bot, query, limit }, ct);

    [McpServerTool(Name = "get_bot_stats", ReadOnly = true), Description("Read locally recorded bot statistics for day, week, month or year.")]
    public Task<JsonElement> GetBotStats(string bot, CancellationToken ct, string period = "day") => Call("get_bot_stats", new { bot, period }, ct);

    [McpServerTool(Name = "get_craft_advice", ReadOnly = true), Description("Craft-or-buy advice for an item: recursive recipe tree where each craftable ingredient is priced at the cheaper of its HDV price and its own crafting, total craft cost vs HDV price, shopping list and missing prices. Give gid or a name query; pass bot to use that bot's already-observed HDV prices (otherwise manual prices only). Read-only estimate.")]
    public Task<JsonElement> GetCraftAdvice(CancellationToken ct, string? query = null, int gid = 0, int quantity = 1, string? bot = null)
        => Call("get_craft_advice", new { query, gid, quantity, bot }, ct);

    [McpServerTool(Name = "get_market_deals", ReadOnly = true), Description("Lots whose unit price is well below the market average, from HDV prices the bot has ALREADY observed (no request is sent). Filters: minDiscountPercent, minLotGain (kamas), maxAgeMinutes (0 = any age), limit. Indicative estimate; verify at the HDV before buying.")]
    public Task<JsonElement> GetMarketDeals(string bot, CancellationToken ct, int minDiscountPercent = 25, int minLotGain = 1000, int maxAgeMinutes = 180, int limit = 30)
        => Call("get_market_deals", new { bot, minDiscountPercent, minLotGain, maxAgeMinutes, limit }, ct);

    [McpServerTool(Name = "get_wealth", ReadOnly = true), Description("Estimated value of the bot's bag and bank (plus kamas), priced from HDV prices already observed or entered by hand. Read-only, indicative; unpriced items are not counted and the bank must have been opened.")]
    public Task<JsonElement> GetWealth(string bot, CancellationToken ct) => Call("get_wealth", new { bot }, ct);

    [McpServerTool(Name = "get_alerts", ReadOnly = true), Description("Current attention points per bot, same rules as the dashboard: session lost or dead character, pods at 95 % or more, life under 25 % out of fight. Read-only snapshot (no history; see get_events for the event stream).")]
    public Task<JsonElement> GetAlerts(CancellationToken ct) => Call("get_alerts", new { }, ct);

    [McpServerTool(Name = "get_subscriptions", ReadOnly = true), Description("Last Ankama subscription end date seen per account (read from game packets), with remaining time and level (ok, soon, critical, expired). Empty until an account has connected.")]
    public Task<JsonElement> GetSubscriptions(CancellationToken ct) => Call("get_subscriptions", new { }, ct);

    [McpServerTool(Name = "get_anomalies", ReadOnly = true), Description("List server-observed open anomalies for a connected bot; absence of data does not prove the server has none.")]
    public Task<JsonElement> GetAnomalies(string bot, CancellationToken ct) => Call("get_anomalies", new { bot }, ct);

    [McpServerTool(Name = "list_dungeons", ReadOnly = true), Description("List known local dungeon definitions with optional name filter and bounded pagination.")]
    public Task<JsonElement> ListDungeons(CancellationToken ct, string query = "", int offset = 0, int limit = 25) => Call("list_dungeons", new { query, offset, limit }, ct);

    [McpServerTool(Name = "get_bot_settings", ReadOnly = true), Description("Read a safe allowlist of character settings; credentials and webhooks are excluded.")]
    public Task<JsonElement> GetBotSettings(string bot, CancellationToken ct) => Call("get_bot_settings", new { bot }, ct);

    [McpServerTool(Name = "get_bot_diagnostics", ReadOnly = true), Description("Read local bot readiness evidence and unknown protocol/ACK states; no game commands are sent.")]
    public Task<JsonElement> GetBotDiagnostics(string bot, CancellationToken ct) => Call("get_bot_diagnostics", new { bot }, ct);

    [McpServerTool(Name = "set_bot_settings"), Description("Update allowlisted character settings with validated values; sensitive fields are not supported.")]
    public Task<JsonElement> SetBotSettings(string bot, Dictionary<string, JsonElement> changes, CancellationToken ct) => Call("set_bot_settings", new { bot, changes }, ct);

    [McpServerTool(Name = "start_dungeon"), Description("Generate a dungeon Lua script from local map/key definitions and request its start. Only local acceptance is reported. Optional uchronic elixir kept active between runs: elixirRank 1-3 (minor/improved/major), elixirMaxPrice (kamas, buy limit; 0 = use stock only), elixirStock (1-50).")]
    public Task<JsonElement> StartDungeon(string bot, int dungeonId, CancellationToken ct, int runs = 1, int maxMinutes = 0, int elixirRank = 0, int elixirMaxPrice = 0, int elixirStock = 2)
        => Call("start_dungeon", new { bot, dungeonId, runs, maxMinutes, elixirRank, elixirMaxPrice, elixirStock }, ct);

    [McpServerTool(Name = "exit_dungeon"), Description("Leave the current dungeon with DungeonExitRequest. Succeeds only when DungeonExitResponse carries the success value and the map then changes; otherwise returns the response reason. Refused during combat.")]
    public Task<JsonElement> ExitDungeon(string bot, CancellationToken ct) => Call("exit_dungeon", new { bot }, ct);

    [McpServerTool(Name = "use_item"), Description("Request to use one inventory item instance by UID, not catalogue GID. Reports local send acceptance.")]
    public Task<JsonElement> UseItem(string bot, int uid, CancellationToken ct) => Call("use_item", new { bot, uid }, ct);

    [McpServerTool(Name = "equip_item"), Description("Request to equip an inventory instance by UID, with an optional validated equipment position. Reports local send acceptance.")]
    public Task<JsonElement> EquipItem(string bot, int uid, CancellationToken ct, int position = -1) => Call("equip_item", new { bot, uid, position }, ct);

    [McpServerTool(Name = "unequip_item"), Description("Request to unequip an inventory instance by UID. Reports local send acceptance.")]
    public Task<JsonElement> UnequipItem(string bot, int uid, CancellationToken ct) => Call("unequip_item", new { bot, uid }, ct);

    [McpServerTool(Name = "emergency_stop"), Description("Request the application's emergency stop for all controlled activity.")]
    public Task<JsonElement> EmergencyStop(CancellationToken ct) => Call("emergency_stop", new { }, ct);
}
